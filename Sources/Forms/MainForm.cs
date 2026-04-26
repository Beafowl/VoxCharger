using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.WindowsAPICodePack.Dialogs;

namespace VoxCharger
{
    public partial class MainForm : Form
    {
        #region -- Variables --
        private const int DefaultVolume = 91;
        private readonly Image _dummyJacket = VoxCharger.Properties.Resources.jk_dummy_s;
        private readonly Dictionary<string, Queue<Action>> _actions = new Dictionary<string, Queue<Action>>();

        private bool _pristine = true;
        private bool _autosave = true;

        // Music list sort state. The ListBox itself has Sorted=false now so
        // we control ordering manually — the alphabetical default is
        // preserved by initialising _sortMode to TitleAsc.
        private enum SortMode { TitleAsc, TitleDesc, IdAsc, IdDesc }
        private SortMode _sortMode = SortMode.TitleAsc;

        // Live search filter applied to the music list. Empty string matches
        // every chart. Hits title, artist, ASCII code, and music id (substring,
        // case-insensitive) so a user can find a song by any of those fields.
        private string _searchText = string.Empty;
        #endregion

        // EM_SETCUEBANNER lets us put greyed-out placeholder text in the
        // search box without subclassing or hand-rolling a paint override.
        // .NET Framework 4.7.2 doesn't expose TextBox.PlaceholderText.
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, string lParam);
        private const int EM_SETCUEBANNER = 0x1501;

        #region --- Form ---
        public MainForm()
        {
            InitializeComponent();
            // Pick a titlebar/taskbar icon that reads against the current
            // Windows app theme: dark icon on light theme, white icon on
            // dark theme. The Win32 ApplicationIcon embedded in the .exe
            // (used by Explorer when the app isn't running) stays the
            // single dark variant — there's no theme-aware static icon.
            var icon = IconLoader.ForCurrentTheme();
            if (icon != null) Icon = icon;
        }

        private void OnMainFormLoad(object sender, EventArgs e)
        {
            // Greyed-out placeholder text in the search box; clears on focus,
            // reappears when empty. EM_SETCUEBANNER is the native way to do
            // this on .NET Framework, no custom paint code needed.
            SendMessage(SearchTextBox.Handle, EM_SETCUEBANNER, 1, "Search by title, artist, code, or id…");

            try
            {
                string currentDir = Application.StartupPath;
                string dbFilename = Path.Combine(currentDir, @"data\others\music_db.xml");

                if (File.Exists(dbFilename))
                {
                    AssetManager.Initialize(currentDir);

                    using (var mixSelector = new MixSelectorForm())
                    {
                        if (mixSelector.ShowDialog() == DialogResult.OK)
                            Reload();
                    }
                }
            }
            catch (Exception)
            {
                // Ignore -- user can still open manually via the Open button
            }
        }

        private void OnMainFormFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!_pristine && SaveFileMenu.Enabled)
            {
                var response = MessageBox.Show(
                    "Save file before exit the program?",
                    "Quit",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question
                );

                if (response == DialogResult.Yes)
                    SaveFileMenu.PerformClick();
                else if (response == DialogResult.Cancel)
                    e.Cancel = true;
            }
        }
        #endregion

        #region --- Menu ---
        private void OnNewFileMenuClick(object sender, EventArgs e)
        {
            if (!_pristine && SaveFileMenu.Enabled)
            {
                var response = MessageBox.Show(
                    "Save file before open another mix?",
                    "Create Mix",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question
                );

                if (response == DialogResult.Yes)
                    SaveFileMenu.PerformClick();
                else if (response == DialogResult.Cancel)
                    return;

                _pristine = true;
                _actions.Clear();
            }

            string gamePath = AssetManager.GamePath;
            if (string.IsNullOrEmpty(AssetManager.MixPath) || !Directory.Exists(AssetManager.MixPath))
            {
                using (var browser = new CommonOpenFileDialog())
                {
                    browser.IsFolderPicker = true;
                    browser.Multiselect    = false;

                    if (browser.ShowDialog() != CommonFileDialogResult.Ok)
                        return;
                    
                    gamePath = browser.FileName;
                    PathTextBox.Text = string.Empty;
                    MusicListBox.Items.Clear();

                    ResetEditor();
                }
            }

            using (var mixSelector = new MixSelectorForm(createMode: true))
            {
                AssetManager.Initialize(gamePath);
                if (mixSelector.ShowDialog() == DialogResult.OK)
                    Reload();
            }
        }

        private void OnSaveFileMenuClick(object sender, EventArgs e)
        {
            try
            {
                if (Save(AssetManager.MdbFilename))
                {
                    MessageBox.Show(
                       "Mix has been saved successfully",
                       "Information",
                       MessageBoxButtons.OK,
                       MessageBoxIcon.Information
                   );
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message, 
                    "Error", 
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Error
                );
            }
        }


        private void OnSaveAsFileMenuClick(object sender, EventArgs e)
        {
            try
            {
                using (var exporter = new SaveFileDialog())
                {
                    exporter.Filter   = "Music DB|*.xml|All Files|*.*";
                    exporter.FileName = new FileInfo(AssetManager.MdbFilename).Name; 

                    if (exporter.ShowDialog() != DialogResult.OK)
                        return;

                    if (Save(exporter.FileName))
                    {
                        MessageBox.Show(
                           "Mix has been saved successfully",
                           "Information",
                           MessageBoxButtons.OK,
                           MessageBoxIcon.Information
                        );
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }
        }

        private void OnChangeMixFileMenuClick(object sender, EventArgs e)
        {
            if (!_pristine && SaveFileMenu.Enabled)
            {
                var response = MessageBox.Show(
                    "Save file before Open another mix?",
                    "Change Mix",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question
                );

                if (response == DialogResult.Yes)
                    SaveFileMenu.PerformClick();
                else if (response == DialogResult.Cancel)
                    return;

                _pristine = true;
                _actions.Clear();
            }
           
            using (var mixSelector = new MixSelectorForm())
            {
                AssetManager.Initialize(AssetManager.GamePath);
                if (mixSelector.ShowDialog() == DialogResult.OK)
                    Reload();
            }
        }

        private void OnDeleteMixFileMenuClick(object sender, EventArgs e)
        {
            var prompt = MessageBox.Show(
                $"Are you sure want to delete \"{AssetManager.MixName}\"?",
                "Delete Mix",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (prompt == DialogResult.Yes)
            {
                try
                {
                    FileMenu.Enabled = OpenButton.Enabled = false;
                    Directory.Delete(AssetManager.MixPath, true);

                    PathTextBox.Text = "";
                    MetadataGroupBox.Enabled = false;
                    MusicListBox.Items.Clear();

                    DisableUi();
                    ResetEditor();

                    AssetManager.Initialize(AssetManager.GamePath);
                    OnChangeMixFileMenuClick(sender, e);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        ex.Message,
                        "Delete Mix",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
                finally
                {
                    FileMenu.Enabled = OpenButton.Enabled = true;
                }
            }
        }

        // Export the current mix as a portable JSON manifest of ksm.dev URLs.
        // Charts that were imported from non-ksm.dev sources (manual KSH file
        // / new-from-blank / vox-only) won't be in the URL store and so are
        // skipped — there's nothing the importer could do with them.
        private void OnExportMixFileMenuClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(AssetManager.MixPath))
                return;

            var urlMap = MixUrlStore.Load(AssetManager.MixPath);
            if (urlMap.Count == 0)
            {
                MessageBox.Show(
                    "No ksm.dev URLs are recorded for this mix.\n\n" +
                    "Export only includes charts that were imported via " +
                    "\"Import from ksm.dev URL..\" — manually-imported KSH " +
                    "files have no URL to export.",
                    "Export Mix",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            using (var browser = new SaveFileDialog())
            {
                browser.Filter   = "Mix manifest (*.json)|*.json|All Files|*.*";
                browser.FileName = (AssetManager.MixName ?? "mix") + "_manifest.json";
                browser.Title    = "Export Mix as ksm.dev URL list";
                if (browser.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    var manifest = MixManifest.Build(AssetManager.MixName, AssetManager.Headers, urlMap);
                    MixManifest.Save(browser.FileName, manifest);
                    MessageBox.Show(
                        $"Exported {manifest.charts.Count} chart(s) to:\n{browser.FileName}",
                        "Export Mix",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "Export failed:\n" + ex.Message,
                        "Export Mix",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
        }

        // Read a manifest JSON, download every URL into the system temp dir,
        // and run the existing BulkImporter flow on the resulting folder
        // structure. The user gets ONE confirmation form (BulkImporter mode)
        // rather than one per chart, and IDs are auto-assigned starting from
        // whatever the user picks in the form.
        //
        // After the bulk import finishes, we walk the imported headers and
        // record their URLs against the new music ids in the sidecar so
        // reconvert-from-URL still works on the freshly-imported mix.
        private void OnImportMixFileMenuClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(AssetManager.MixPath))
                return;

            string manifestPath;
            using (var browser = new OpenFileDialog())
            {
                browser.Filter = "Mix manifest (*.json)|*.json|All Files|*.*";
                browser.Title  = "Import Mix from manifest";
                if (browser.ShowDialog() != DialogResult.OK)
                    return;
                manifestPath = browser.FileName;
            }

            MixManifest.Manifest manifest;
            try
            {
                manifest = MixManifest.Load(manifestPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to read manifest:\n" + ex.Message,
                    "Import Mix",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (manifest == null || manifest.charts == null || manifest.charts.Count == 0)
            {
                MessageBox.Show(
                    "Manifest contains no charts.",
                    "Import Mix",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }
            if (manifest.format != null && manifest.format != MixManifest.FormatTag)
            {
                MessageBox.Show(
                    $"Unrecognized manifest format: {manifest.format}",
                    "Import Mix",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            string parentDir = Path.Combine(Path.GetTempPath(), "VoxCharger_MixImport_" + Path.GetRandomFileName());
            Directory.CreateDirectory(parentDir);

            // entryAscii (folder name) -> URL, so we can re-key the URL store
            // by the music id BulkImporter assigns post-import.
            var asciiToUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var errors    = new List<string>();

            using (var loader = new LoadingForm())
            {
                loader.SetAction(dialog =>
                {
                    int total = manifest.charts.Count;
                    for (int i = 0; i < total; i++)
                    {
                        var entry = manifest.charts[i];
                        dialog.SetStatus($"Downloading {i + 1}/{total}: {entry.title ?? entry.url}");
                        dialog.SetProgress((i / (float)total) * 100f);

                        if (string.IsNullOrWhiteSpace(entry.url))
                        {
                            errors.Add($"#{entry.id}: missing URL");
                            continue;
                        }
                        // BulkImporter uses the folder name as the chart's
                        // ASCII code, so name each subfolder after the entry
                        // ascii (or fall back to a unique sequence). Make the
                        // name filesystem-safe.
                        string ascii = entry.ascii ?? ("chart_" + entry.id);
                        string safe  = MakeSafeFolderName(ascii);
                        string dest  = Path.Combine(parentDir, safe);
                        // Avoid collisions on duplicate ASCII codes.
                        int attempt = 1;
                        while (Directory.Exists(dest))
                            dest = Path.Combine(parentDir, safe + "_" + (++attempt));

                        try
                        {
                            using (var dl = new KsmDownloader())
                            {
                                string extracted = dl.DownloadAndExtract(entry.url);
                                // Move the extracted ksh + assets into the
                                // dest folder under our parent. The
                                // BulkImporter expects to see the ksh files
                                // directly under <parent>/<song>/.
                                string kshDir = KsmDownloader.FindKshDirectory(extracted);
                                if (kshDir == null)
                                {
                                    errors.Add($"#{entry.id} ({entry.title}): no .ksh in archive");
                                    KsmDownloader.Cleanup(extracted);
                                    continue;
                                }
                                Directory.Move(kshDir, dest);
                                // Try to clean up the now-mostly-empty
                                // extraction temp dir; ignore if it still
                                // has unrelated files.
                                try { KsmDownloader.Cleanup(extracted); } catch { }
                            }
                            asciiToUrl[Path.GetFileName(dest)] = entry.url;
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"#{entry.id} ({entry.title}): {ex.Message}");
                        }
                    }
                    dialog.Complete();
                });
                loader.ShowDialog();
            }

            if (asciiToUrl.Count == 0)
            {
                try { Directory.Delete(parentDir, true); } catch { }
                ShowImportSummary("Import Mix", 0, errors);
                return;
            }

            int imported = 0;
            try
            {
                using (var converter = new ConverterForm(parentDir, ConvertMode.BulkImporter))
                {
                    if (converter.ShowDialog() != DialogResult.OK)
                        return;

                    foreach (var header in converter.ResultSet)
                    {
                        if (!_actions.ContainsKey(header.Ascii))
                            _actions[header.Ascii] = new Queue<Action>();
                        AssetManager.Headers.Add(header);
                        MusicListBox.Items.Add(header);
                        _actions[header.Ascii].Enqueue(converter.ActionSet[header.Ascii]);

                        // Re-key the URL by the music id BulkImporter chose.
                        if (asciiToUrl.TryGetValue(header.Ascii, out string url))
                            MixUrlStore.SetUrl(AssetManager.MixPath, header.Id, url);
                        imported++;
                    }
                    ApplyCurrentSort();
                    _pristine = false;
                    if (_autosave)
                        Save(AssetManager.MdbFilename);
                }

                ShowImportSummary("Import Mix", imported, errors);
            }
            finally
            {
                try { Directory.Delete(parentDir, true); } catch { }
            }
        }

        // True when the currently-selected chart has a ksm.dev URL recorded
        // in the mix's sidecar — i.e. when "Reconvert from URL" can do
        // anything useful.
        private bool SelectedSongHasReconvertUrl()
        {
            var header = MusicListBox.SelectedItem as VoxHeader;
            if (header == null || string.IsNullOrEmpty(AssetManager.MixPath))
                return false;
            string url = MixUrlStore.GetUrl(AssetManager.MixPath, header.Id);
            return !string.IsNullOrEmpty(url);
        }

        // Keep both reconvert affordances (the visible button next to the
        // music id and the right-click context menu item) in sync with the
        // current selection's URL status. Called from selection change,
        // context menu popup, and after import / remove paths that change
        // which URLs are recorded.
        private void RefreshReconvertControls()
        {
            bool hasUrl = SelectedSongHasReconvertUrl();
            if (ReconvertButton != null)
                ReconvertButton.Enabled = hasUrl;
            if (ReconvertFromUrlMenu != null)
            {
                ReconvertFromUrlMenu.Enabled = hasUrl;
                ReconvertFromUrlMenu.Text = hasUrl
                    ? "Reconvert from ksm.dev URL"
                    : "Reconvert from ksm.dev URL (no URL stored)";
            }
        }

        private void OnMusicListContextMenuPopup(object sender, EventArgs e)
        {
            RefreshReconvertControls();
        }

        // Pull the latest version of the selected chart from its stored
        // ksm.dev URL and re-import. The existing entry's assets are deleted
        // first so re-import doesn't trip the "music code already taken"
        // check; the user re-confirms metadata in ConverterForm and the new
        // URL is recorded against whatever id ConverterForm assigns
        // (typically the same one the chart had).
        private void OnReconvertFromUrlClick(object sender, EventArgs e)
        {
            var header = MusicListBox.SelectedItem as VoxHeader;
            if (header == null) return;
            if (string.IsNullOrEmpty(AssetManager.MixPath)) return;

            string url = MixUrlStore.GetUrl(AssetManager.MixPath, header.Id);
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show(
                    "No ksm.dev URL is recorded for this chart.",
                    "Reconvert",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            var resp = MessageBox.Show(
                $"Reconvert \"{header.Title}\" from:\n{url}\n\n" +
                "This will delete the chart's existing assets and re-import " +
                "the latest version from ksm.dev.\n\nContinue?",
                "Reconvert",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (resp != DialogResult.Yes) return;

            string oldAscii = header.Ascii;
            int    oldId    = header.Id;

            // Drop the existing entry from the in-memory list + DB. The
            // assets on disk get queued for delete; we don't run that
            // queue immediately — ConverterForm.Action will overwrite the
            // music folder and the deferred delete is harmless either way
            // since it only runs on Save.
            AssetManager.Headers.Remove(header);
            MusicListBox.Items.Remove(header);
            _actions[header.Ascii] = new Queue<Action>();
            _actions[header.Ascii].Enqueue(() => AssetManager.DeleteAssets(header));
            // Save now so the deferred delete happens before re-import.
            // Otherwise ConverterForm refuses to import into a path that
            // still exists on disk under the same ASCII code.
            Save(AssetManager.MdbFilename);

            // Drop the URL too — we'll re-record it once the new entry has
            // an id. RenameUrl on the new id later if it changed.
            MixUrlStore.RemoveUrl(AssetManager.MixPath, oldId);

            // Reuse the standard ksm.dev download + import flow.
            string tempDir = null;
            string kshFile = null;
            Exception downloadError = null;
            using (var loader = new LoadingForm())
            {
                loader.SetAction(dialog =>
                {
                    try
                    {
                        dialog.SetStatus("Downloading from ksm.dev...");
                        dialog.SetProgress(20f);
                        using (var dl = new KsmDownloader())
                        {
                            tempDir = dl.DownloadAndExtract(url);
                        }
                        dialog.SetStatus("Locating chart...");
                        dialog.SetProgress(80f);
                        kshFile = KsmDownloader.FindKshFile(tempDir);
                    }
                    catch (Exception ex)
                    {
                        downloadError = ex;
                    }
                    finally
                    {
                        dialog.Complete();
                    }
                });
                loader.ShowDialog();
            }

            if (downloadError != null)
            {
                if (tempDir != null) KsmDownloader.Cleanup(tempDir);
                MessageBox.Show(
                    "Download failed:\n" + downloadError.Message,
                    "Reconvert",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }
            if (kshFile == null)
            {
                if (tempDir != null) KsmDownloader.Cleanup(tempDir);
                MessageBox.Show(
                    "The downloaded archive did not contain a .ksh file.",
                    "Reconvert",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            try
            {
                using (var converter = new ConverterForm(kshFile, ConvertMode.Importer))
                {
                    if (converter.ShowDialog() != DialogResult.OK)
                        return;

                    var newHeader = converter.Result;
                    if (!_actions.ContainsKey(newHeader.Ascii))
                        _actions[newHeader.Ascii] = new Queue<Action>();

                    _pristine = false;
                    AssetManager.Headers.Add(newHeader);
                    MusicListBox.Items.Add(newHeader);
                    ApplyCurrentSort();

                    _actions[newHeader.Ascii].Enqueue(converter.Action);
                    MixUrlStore.SetUrl(AssetManager.MixPath, newHeader.Id, url);

                    if (_autosave)
                        Save(AssetManager.MdbFilename);
                }
            }
            finally
            {
                KsmDownloader.Cleanup(tempDir);
            }
        }

        private void ShowImportSummary(string title, int succeeded, List<string> errors)
        {
            if (errors.Count == 0)
            {
                MessageBox.Show(
                    $"Done. {succeeded} chart(s) imported.",
                    title,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }
            string msg = $"Done. {succeeded} chart(s) imported, {errors.Count} failed:\n";
            int shown = Math.Min(errors.Count, 10);
            for (int i = 0; i < shown; i++) msg += "\n- " + errors[i];
            if (errors.Count > shown) msg += $"\n... and {errors.Count - shown} more.";
            MessageBox.Show(msg, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static string MakeSafeFolderName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "chart";
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw)
                sb.Append(Array.IndexOf(invalid, c) >= 0 || c == ' ' ? '_' : c);
            return sb.ToString();
        }

        private void OnAutosaveEditMenuClick(object sender, EventArgs e)
        {
            _autosave = !_autosave;
            AutosaveEditMenu.Checked = _autosave;
        }

        private void OnExplorerEditMenuClick(object sender, EventArgs e)
        {
            var header = MusicListBox.SelectedItem as VoxHeader;
            if (header == null)
                return;

            string path = AssetManager.GetMusicPath(header);
            Process.Start("explorer.exe", path);
        }

        private void OnSingleConvertToolsMenuClick(object sender, EventArgs e)
        {
            using (var browser = new OpenFileDialog())
            {
                browser.Filter = "Kshoot Chart File|*.ksh";
                browser.CheckFileExists = true;

                if (browser.ShowDialog() != DialogResult.OK)
                    return;

                using (var converter = new ConverterForm(browser.FileName, ConvertMode.Converter))
                    converter.ShowDialog();
            }
        }

        private void OnBulkConvertToolsMenuClick(object sender, EventArgs e)
        {
            using (var browser = new CommonOpenFileDialog())
            {
                browser.IsFolderPicker = true;
                browser.Multiselect    = false;

                if (browser.ShowDialog() != CommonFileDialogResult.Ok)
                    return;

                using (var converter = new ConverterForm(browser.FileName, ConvertMode.Converter))
                    converter.ShowDialog();
            }
        }

        private void OnSingleVoxConvertToolsMenuClick(object sender, EventArgs e)
        {
            using (var browser = new OpenFileDialog())
            {
                browser.Filter = "Sound Voltex Chart File|*.vox";
                browser.CheckFileExists = true;

                if (browser.ShowDialog() != DialogResult.OK)
                    return;

                using (var converter = new ConverterForm(browser.FileName, ConvertMode.ReverseConverter))
                    converter.ShowDialog();
            }
        }

        private void OnBulkVoxConvertToolsMenuClick(object sender, EventArgs e)
        {
            using (var browser = new CommonOpenFileDialog())
            {
                browser.IsFolderPicker = true;
                browser.Multiselect    = false;

                if (browser.ShowDialog() != CommonFileDialogResult.Ok)
                    return;

                using (var converter = new ConverterForm(browser.FileName, ConvertMode.BulkReverseConverter))
                    converter.ShowDialog();
            }
        }

        private void OnMusicFileBuilderClick(object sender, EventArgs e)
        {
            using (var browser  = new OpenFileDialog())
            using (var exporter = new SaveFileDialog())
            {
                browser.Filter = "Audio Files|*.wav;*.ogg;*.mp3;*.flac";
                browser.CheckFileExists = true;

                if (browser.ShowDialog() != DialogResult.OK)
                    return;

                exporter.Filter = "2DX File|*.2dx";
                if (exporter.ShowDialog() != DialogResult.OK)
                    return;

                string source = browser.FileName;
                string output = exporter.FileName;
                string error = string.Empty;
                
                using (var loader = new LoadingForm())
                {
                    loader.SetAction(dialog =>
                    {
                        try
                        {
                            dialog.SetStatus("Processing assets..");
                            DxEncoder.Encode(new[] { source }, output);

                            dialog.SetProgress(100);
                            dialog.DialogResult = DialogResult.OK;
                            dialog.Complete();
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;

                            dialog.DialogResult = DialogResult.Abort;
                            dialog.Complete();
                        }
                    });

                    if (loader.ShowDialog() == DialogResult.OK)
                    {
                        MessageBox.Show(
                            "Audio file has been converted successfully",
                            "2DX Builder",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Failed to convert audio file.\n{error}",
                            "2DX Builder",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                    }
                }
            }
        }

        private void OnS3VFileBuilderClick(object sender, EventArgs e)
        {
            using (var browser  = new OpenFileDialog())
            using (var exporter = new SaveFileDialog())
            {
                browser.Filter = "Audio Files|*.wav;*.ogg;*.mp3;*.flac";
                browser.CheckFileExists = true;

                if (browser.ShowDialog() != DialogResult.OK)
                    return;

                exporter.Filter = "S3V File|*.s3v";
                if (exporter.ShowDialog() != DialogResult.OK)
                    return;

                string source = browser.FileName;
                string output = exporter.FileName;
                string error = string.Empty;

                using (var loader = new LoadingForm())
                {
                    loader.SetAction(dialog =>
                    {
                        try
                        {
                            dialog.SetStatus("Processing assets..");
                            S3VTool.Convert(source, output, AudioImportOptions.Default);

                            dialog.SetProgress(100);
                            dialog.DialogResult = DialogResult.OK;
                            dialog.Complete();
                        }
                        catch (Exception ex)
                        {
                            error = ex.Message;

                            dialog.DialogResult = DialogResult.Abort;
                            dialog.Complete();
                        }
                    });

                    if (!File.Exists(S3VTool.ConverterFileName))
                        S3VTool.ConverterFileName = "ffmpeg.exe";

                    if (!File.Exists(S3VTool.ConverterFileName))
                    {
                        using (var ofd = new OpenFileDialog())
                        {
                            ofd.Filter = "ffmpeg.exe | ffmpeg.exe";
                            ofd.CheckFileExists = true;

                            if (ofd.ShowDialog() == DialogResult.OK)
                                S3VTool.ConverterFileName = ofd.FileName;
                        }
                    }

                    if (loader.ShowDialog() == DialogResult.OK)
                    {
                        MessageBox.Show(
                            "Audio file has been converted successfully",
                            "S3V Builder",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information
                        );
                    }
                    else
                    {
                        MessageBox.Show(
                            $"Failed to convert audio file.\n{error}",
                            "S3V Builder",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error
                        );
                    }
                }
            }
        }

        private void OnRemoteMixesToolsMenuClick(object sender, EventArgs e)
        {
            using (var remoteForm = new RemoteMixForm())
            {
                if (remoteForm.ShowDialog() != DialogResult.OK)
                    return;

                if (string.IsNullOrEmpty(AssetManager.MixPath))
                    return;

                foreach (var header in remoteForm.ImportedHeaders)
                {
                    AssetManager.Headers.Add(header);
                    MusicListBox.Items.Add(header);

                    if (remoteForm.ImportedActions.ContainsKey(header.Ascii))
                    {
                        if (!_actions.ContainsKey(header.Ascii))
                            _actions[header.Ascii] = new Queue<Action>();

                        var queue = remoteForm.ImportedActions[header.Ascii];
                        while (queue.Count > 0)
                            _actions[header.Ascii].Enqueue(queue.Dequeue());
                    }
                }

                ApplyCurrentSort();
                _pristine = false;
                if (_autosave)
                    Save(AssetManager.MdbFilename);
            }
        }

        private void OnAboutHelpMenuClick(object sender, EventArgs e)
        {
            using (var about = new AboutForm())
                about.ShowDialog();
        }

        private void OnExitFileMenuClick(object sender, EventArgs e)
        {
            Application.Exit();
        }
        #endregion

        #region -- Editor ---
        private void OnOpenButtonClick(object sender, EventArgs e)
        {
            try
            {
                if (!_pristine && SaveFileMenu.Enabled)
                {
                    var response = MessageBox.Show(
                        "Save file before open another mix?",
                        "Open Mix",
                        MessageBoxButtons.YesNoCancel,
                        MessageBoxIcon.Question
                    );

                    if (response == DialogResult.Yes)
                        SaveFileMenu.PerformClick();
                    else if (response == DialogResult.Cancel)
                        return;

                    _pristine = true;
                    _actions.Clear();
                }

                using (var browser = new CommonOpenFileDialog())
                using (var mixSelector = new MixSelectorForm())
                {
                    browser.IsFolderPicker = true;
                    if (browser.ShowDialog() == CommonFileDialogResult.Ok)
                    {
                        AssetManager.Initialize(browser.FileName);

                        PathTextBox.Text = string.Empty;
                        MusicListBox.Items.Clear();

                        ResetEditor();

                        if (mixSelector.ShowDialog() == DialogResult.OK)
                            Reload();
                    }
                }
            } 
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnAddNewMenuClick(object sender, EventArgs e)
        {
            string defaultAscii = AssetManager.MixName.ToLower().Replace(" ", string.Empty);
            var duplicates = new List<VoxHeader>();
            foreach (var h in AssetManager.Headers)
            {
                if (h.Ascii.StartsWith(defaultAscii))
                    duplicates.Add(h);
            }

            if (duplicates.Count > 0)
            {
                duplicates.Sort((a, b) => a.Ascii.CompareTo(b.Ascii));
                string ascii = $"{defaultAscii}_01";
                int counter = 1;
                foreach (var h in duplicates)
                {
                    if (h.Ascii == ascii)
                        ascii = $"{defaultAscii}_{++counter:D2}";
                    else
                        break;
                }

                defaultAscii = ascii;
            }

            var header = new VoxHeader()
            {
                Title            = "Untitled",
                TitleYomigana    = "ダミー", // dummy
                Ascii            = defaultAscii,
                Artist           = "Unknown",
                ArtistYomigana   = "ダミー", // dummy 
                Version          = GameVersion.Nabla,
                InfVersion       = InfiniteVersion.Mxm,
                BackgroundId     = short.Parse(ConverterForm.LastBackground),
                GenreId          = 16,
                BpmMin           = 1,
                BpmMax           = 1,
                Volume           = 91,
                DistributionDate = DateTime.Now,
                Levels = new Dictionary<Difficulty, VoxLevelHeader>()
                {
                    { Difficulty.Infinite, new VoxLevelHeader() {} }
                }
            };

            _pristine = false;
            AssetManager.Headers.Add(header);
            MusicListBox.Items.Add(header);
            ApplyCurrentSort();
        }

        private void OnSingleImportMenuClick(object sender, EventArgs e)
        {
            using (var browser = new OpenFileDialog())
            {
                browser.Filter = "KShoot Mania Chart|*.ksh";
                browser.CheckFileExists = true;

                if (browser.ShowDialog() != DialogResult.OK)
                    return;

                using (var converter = new ConverterForm(browser.FileName, ConvertMode.Importer))
                {
                    if (converter.ShowDialog() != DialogResult.OK)
                        return;

                    var header = converter.Result;
                    if (!_actions.ContainsKey(header.Ascii))
                        _actions[header.Ascii] = new Queue<Action>();

                    _pristine = false;
                    AssetManager.Headers.Add(header);
                    MusicListBox.Items.Add(header);
                    ApplyCurrentSort();

                    _actions[header.Ascii].Enqueue(converter.Action);
                    if (_autosave)
                        Save(AssetManager.MdbFilename);
                }
            }
        }

        // Import a chart straight from a ksm.dev song URL. Reuses the
        // existing single-import flow so the user gets the same metadata
        // confirmation form, audio normalization, and lead-in/tail policy
        // ConverterForm runs for a local .ksh — they just don't have to
        // download and unzip the chart manually.
        //
        // Accepts URLs in any of these shapes:
        //   https://ksm.dev/songs/<uuid>
        //   https://ksm.dev/songs/<uuid>/
        //   https://ksm.dev/songs/<uuid>/download
        // KsmDownloader.NormalizeDownloadUrl appends /download as needed.
        private void OnImportKsmDevMenuClick(object sender, EventArgs e)
        {
            string url;
            using (var prompt = new InputForm("Import from ksm.dev", "Paste a ksm.dev song URL:"))
            {
                if (prompt.ShowDialog() != DialogResult.OK)
                    return;
                url = prompt.Value;
            }

            if (string.IsNullOrWhiteSpace(url))
                return;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "URL must start with http:// or https://",
                    "Invalid URL",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            string tempDir = null;
            string kshFile = null;
            Exception downloadError = null;
            using (var loader = new LoadingForm())
            {
                loader.SetAction(dialog =>
                {
                    try
                    {
                        dialog.SetStatus("Downloading from ksm.dev...");
                        dialog.SetProgress(20f);
                        using (var dl = new KsmDownloader())
                        {
                            tempDir = dl.DownloadAndExtract(url);
                        }
                        dialog.SetStatus("Locating chart...");
                        dialog.SetProgress(80f);
                        kshFile = KsmDownloader.FindKshFile(tempDir);
                    }
                    catch (Exception ex)
                    {
                        // Bubble out via captured variable; LoadingForm
                        // refuses to close while _completed is false, so
                        // we must always call Complete() in finally.
                        downloadError = ex;
                    }
                    finally
                    {
                        dialog.Complete();
                    }
                });

                loader.ShowDialog();
            }

            if (downloadError != null)
            {
                if (tempDir != null) KsmDownloader.Cleanup(tempDir);
                MessageBox.Show(
                    "Download failed:\n" + downloadError.Message,
                    "ksm.dev Import",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (kshFile == null)
            {
                if (tempDir != null) KsmDownloader.Cleanup(tempDir);
                MessageBox.Show(
                    "The downloaded archive did not contain a .ksh file.",
                    "ksm.dev Import",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            try
            {
                using (var converter = new ConverterForm(kshFile, ConvertMode.Importer))
                {
                    if (converter.ShowDialog() != DialogResult.OK)
                        return;

                    var header = converter.Result;
                    if (!_actions.ContainsKey(header.Ascii))
                        _actions[header.Ascii] = new Queue<Action>();

                    _pristine = false;
                    AssetManager.Headers.Add(header);
                    MusicListBox.Items.Add(header);
                    ApplyCurrentSort();

                    _actions[header.Ascii].Enqueue(converter.Action);

                    // Remember the source URL so the user can later reconvert
                    // straight from ksm.dev or export the mix as a manifest.
                    MixUrlStore.SetUrl(AssetManager.MixPath, header.Id, url);

                    if (_autosave)
                        Save(AssetManager.MdbFilename);
                }
            }
            finally
            {
                // ConverterForm.Action copies the chart files into the
                // mix's music dir (or runs deferred), so by the time we
                // reach here it's safe to drop the temp extraction.
                KsmDownloader.Cleanup(tempDir);
            }
        }

        private void OnBulkImportKshMenuClick(object sender, EventArgs e)
        {
            using (var browser = new CommonOpenFileDialog())
            {
                browser.IsFolderPicker = true;
                browser.Multiselect    = false;

                if (browser.ShowDialog() != CommonFileDialogResult.Ok)
                    return;

                using (var converter = new ConverterForm(browser.FileName, ConvertMode.BulkImporter))
                {
                    if (converter.ShowDialog() != DialogResult.OK)
                        return;

                    foreach (var header in converter.ResultSet)
                    {
                        if (!_actions.ContainsKey(header.Ascii))
                            _actions[header.Ascii] = new Queue<Action>();

                        AssetManager.Headers.Add(header);
                        MusicListBox.Items.Add(header);

                        _actions[header.Ascii].Enqueue(converter.ActionSet[header.Ascii]);
                    }

                    ApplyCurrentSort();
                    _pristine = false;
                    if (_autosave)
                        Save(AssetManager.MdbFilename);
                }
            }
        }

        // Refresh the MusicListBox from the canonical AssetManager.Headers
        // collection, applying the current search filter and sort. Called
        // after every mutation (load, import, remove, rename, search edit,
        // sort change). The kept name "ApplyCurrentSort" is historical —
        // every existing call site benefits from the filter step too, so
        // reusing the same method keeps the call graph straightforward.
        // O(n) per call; up to a few thousand songs is well below
        // user-perceptible.
        private void ApplyCurrentSort()
        {
            if (AssetManager.Headers == null) return;

            var headers = AssetManager.Headers.Where(MatchesSearch);
            IEnumerable<VoxHeader> sorted;
            switch (_sortMode)
            {
                case SortMode.TitleDesc:
                    sorted = headers.OrderByDescending(h => h.Title ?? "", StringComparer.OrdinalIgnoreCase);
                    break;
                case SortMode.IdAsc:
                    sorted = headers.OrderBy(h => h.Id);
                    break;
                case SortMode.IdDesc:
                    sorted = headers.OrderByDescending(h => h.Id);
                    break;
                case SortMode.TitleAsc:
                default:
                    sorted = headers.OrderBy(h => h.Title ?? "", StringComparer.OrdinalIgnoreCase);
                    break;
            }

            var keep = MusicListBox.SelectedItem;
            MusicListBox.BeginUpdate();
            MusicListBox.Items.Clear();
            foreach (var h in sorted)
                MusicListBox.Items.Add(h);
            if (keep != null)
            {
                int idx = MusicListBox.Items.IndexOf(keep);
                if (idx >= 0) MusicListBox.SelectedIndex = idx;
            }
            MusicListBox.EndUpdate();
        }

        private void SetSortMode(SortMode mode, string label)
        {
            _sortMode = mode;
            SortButton.Text = "Sort: " + label;
            ApplyCurrentSort();
        }

        private void OnSortByTitleAscClick(object sender, EventArgs e)  => SetSortMode(SortMode.TitleAsc,  "Title (A → Z)");
        private void OnSortByTitleDescClick(object sender, EventArgs e) => SetSortMode(SortMode.TitleDesc, "Title (Z → A)");
        private void OnSortByIdAscClick(object sender, EventArgs e)     => SetSortMode(SortMode.IdAsc,     "Music ID ↑");
        private void OnSortByIdDescClick(object sender, EventArgs e)    => SetSortMode(SortMode.IdDesc,    "Music ID ↓");

        // Substring match across the visible-ish fields. We deliberately
        // include the music id so users can paste an id directly when they
        // know which slot they want.
        private bool MatchesSearch(VoxHeader h)
        {
            if (string.IsNullOrEmpty(_searchText)) return true;
            if (h == null) return false;
            string needle = _searchText;
            return ContainsCi(h.Title, needle)
                || ContainsCi(h.Artist, needle)
                || ContainsCi(h.Ascii, needle)
                || h.Id.ToString().IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsCi(string haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void OnSearchTextBoxChanged(object sender, EventArgs e)
        {
            _searchText = SearchTextBox.Text != null ? SearchTextBox.Text.Trim() : string.Empty;
            ApplyCurrentSort();
        }

        private void OnMetadataChanged(object sender, EventArgs e)
        {
            if (sender is TextBox textBox && !textBox.Modified)
                return;

            if (sender is Control control && !(control is ComboBox) && !control.ContainsFocus)
                return;

            if (!(MusicListBox.SelectedItem is VoxHeader header))
                return;

            if (int.TryParse(IdTextBox.Text, out int id))
            {
                // Validate ID
                if (!AssetManager.ValidateMusicId(id))
                {
                    IdTextBox.Text = header.Id.ToString();
                    MessageBox.Show("Music ID is already taken", "Duplicate", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else if (id != header.Id)
                {
                    // Rename asset folder if it exists
                    string oldPath = AssetManager.GetMusicPath(header);
                    int oldId = header.Id;
                    header.Id = id;
                    // Keep the sidecar URL keyed to the new id so reconvert
                    // and export still find it after the rename.
                    MixUrlStore.RenameUrl(AssetManager.MixPath, oldId, id);
                    string newPath = AssetManager.GetMusicPath(header);

                    if (Directory.Exists(oldPath) && !Directory.Exists(newPath))
                    {
                        if (!_actions.ContainsKey(header.Ascii))
                            _actions[header.Ascii] = new Queue<Action>();

                        _actions[header.Ascii].Enqueue(() => Directory.Move(oldPath, newPath));
                        _pristine = false;
                    }
                }
            }
            else
                IdTextBox.Text = header.Id.ToString();

            double min = (double)Math.Min(BpmMinNumericBox.Value, BpmMaxNumericBox.Value);
            double max = (double)Math.Max(BpmMinNumericBox.Value, BpmMaxNumericBox.Value);
            BpmMinNumericBox.Value = (decimal)min;
            BpmMaxNumericBox.Value = (decimal)max;

            header.Title            = TitleTextBox.Text;
            header.TitleYomigana    = TitleYomiganaTextBox.Text;
            header.Artist           = ArtistTextBox.Text;
            header.ArtistYomigana   = ArtistYomiganaTextBox.Text;
            header.BpmMin           = min;
            header.BpmMax           = max;
            header.Version          = (GameVersion)(VersionDropDown.SelectedIndex + 1);
            header.InfVersion       = InfVerDropDown.SelectedIndex == 0 ? InfiniteVersion.Mxm : (InfiniteVersion)(InfVerDropDown.SelectedIndex + 1);
            header.DistributionDate = DistributionPicker.Value;
            header.BackgroundId     = short.Parse((BackgroundDropDown.SelectedItem ?? "0").ToString().Split(' ')[0]);
            header.Volume           = (short)VolumeTrackBar.Value;

            VolumeIndicatorLabel.Text = $"{VolumeTrackBar.Value:#00}%";
            _pristine = false;
        }

        private void OnLevelEditButtonClick(object sender, EventArgs e)
        {
            if (MusicListBox.SelectedItem == null)
                return;

            var button = (Button)sender;
            var difficulty = (Difficulty)int.Parse(button.Tag.ToString());
            var header = MusicListBox.SelectedItem as VoxHeader;

            using (var editor = new LevelEditorForm(header, difficulty))
            {
                if (editor.ShowDialog() == DialogResult.OK)
                {
                    // Update the levels in case it's newly added
                    header.Levels[difficulty] = editor.Result;
                    if (editor.Action != null)
                    {
                        if (!_actions.ContainsKey(header.Ascii))
                            _actions[header.Ascii] = new Queue<Action>();

                        _actions[header.Ascii].Enqueue(editor.Action);
                    }

                    if (header.Levels.ContainsKey(Difficulty.Infinite))
                        InfVerDropDown.Items[0] = "MXM";
                    else
                        InfVerDropDown.Items[0] = "--";

                    OnInfVerDropDownSelectedIndexChanged(sender, e);

                    _pristine = false;
                    LoadJacket(header);

                    if (_autosave)
                        Save(AssetManager.MdbFilename);
                }
            }
        }

        private void OnInfVerDropDownSelectedIndexChanged(object sender, EventArgs e)
        {
            var header = MusicListBox.SelectedItem as VoxHeader;
            if (header == null || InfVerDropDown.SelectedItem == null)
                InfEditButton.Text = "--";
            else if (header.Levels.ContainsKey(Difficulty.Infinite))
                InfEditButton.Text = InfVerDropDown.SelectedItem.ToString();
        }

        private void OnImportMusicFileButtonClick(object sender, EventArgs e)
        {
            ImportAudioFile();
        }

        private void OnImportPreviewFileButtonClick(object sender, EventArgs e)
        {
            ImportAudioFile(true);
        }

        private void OnJacketPictureBoxClick(object sender, EventArgs e)
        {
            var header = MusicListBox.SelectedItem as VoxHeader;
            if (header == null)
                return;

            var control    = (Control)sender;
            var difficulty = (Difficulty)int.Parse(control.Tag.ToString());
            if (!header.Levels.ContainsKey(difficulty))
                return;

            string jacket = $"{AssetManager.GetJacketPath(header, difficulty)}_b.png";
            if (!File.Exists(jacket))
            {
                jacket = $"{AssetManager.GetDefaultJacketPath(header)}_b.png";
                if (!File.Exists(jacket))
                    return;
            }

            using (var image  = Image.FromFile(jacket))
            using (var viewer = new JacketViewerForm(image))
                viewer.ShowDialog();
        }
        #endregion

        #region --- Mix List Management ---
        private void OnMusicListBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            // Always refresh reconvert affordances on selection change so the
            // visible button matches the new selection's URL status.
            RefreshReconvertControls();

            if (!(MusicListBox.SelectedItem is VoxHeader header))
            {
                MetadataGroupBox.Enabled = false;
                ResetEditor();

                return;
            }

            IdTextBox.Text                  = header.Id.ToString();
            TitleTextBox.Text               = VoxHeader.WithDecodedSymbols(header.Title);
            TitleYomiganaTextBox.Text       = VoxHeader.WithDecodedSymbols(header.TitleYomigana);
            ArtistTextBox.Text              = VoxHeader.WithDecodedSymbols(header.Artist);
            ArtistYomiganaTextBox.Text      = VoxHeader.WithDecodedSymbols(header.ArtistYomigana);

            try
            {
                BpmMinNumericBox.Minimum = 1;
                BpmMaxNumericBox.Minimum = 1;

                BpmMinNumericBox.Value = (decimal)header.BpmMin;
                BpmMaxNumericBox.Value = (decimal)header.BpmMax;

                BpmMinNumericBox.Enabled = true;
                BpmMaxNumericBox.Enabled = true;
            }
            catch (Exception)
            {
                BpmMinNumericBox.Minimum = 0;
                BpmMaxNumericBox.Minimum = 0;
                BpmMinNumericBox.Value = 0;
                BpmMaxNumericBox.Value = 0;

                BpmMinNumericBox.Enabled = false;
                BpmMaxNumericBox.Enabled = false;
            }

            try
            {
                VersionDropDown.DropDownStyle = ComboBoxStyle.DropDownList;
                InfVerDropDown.DropDownStyle  = ComboBoxStyle.DropDownList;

                VersionDropDown.SelectedIndex   = (int)(header.Version) - 1;
                InfVerDropDown.SelectedIndex    = header.InfVersion == InfiniteVersion.Mxm ? 0 : (int)(header.InfVersion) - 1;

                if (header.Levels.ContainsKey(Difficulty.Infinite))
                    InfVerDropDown.Items[0] = "MXM";
                else
                    InfVerDropDown.Items[0] = "--";

                VersionDropDown.Enabled = true;
                InfVerDropDown.Enabled  = true;
            }
            catch (Exception)
            {
                VersionDropDown.DropDownStyle = ComboBoxStyle.DropDown;
                InfVerDropDown.DropDownStyle  = ComboBoxStyle.DropDown;

                VersionDropDown.Text    = "--";
                VersionDropDown.Enabled = false;

                InfVerDropDown.Text = "--";
                InfVerDropDown.Enabled  = false;
            }

            DistributionPicker.Value        = header.DistributionDate;
            VolumeTrackBar.Value            = header.Volume;
            BackgroundDropDown.SelectedItem = $"{header.BackgroundId:D2}";
            VolumeIndicatorLabel.Text       = $"{VolumeTrackBar.Value:#00}%";

            bool safe                          = !string.IsNullOrEmpty(AssetManager.MixName);
            AddButton.Enabled                  = safe;
            AddEditMenu.Enabled                = safe;
            RemoveButton.Enabled               = safe;
            RemoveEditMenu.Enabled             = safe;
            ImportAudioEditMenu.Enabled        = safe;
            ImportAudioPreviewEditMenu.Enabled = safe;
            ExplorerEditMenu.Enabled           = true;
            EditMenu.Enabled                   = true;
            MetadataGroupBox.Enabled           = true;
            InfEditButton.Text                 = InfVerDropDown.SelectedItem.ToString();
            
            LoadJacket(header);
        }

        private void OnRemoveButtonClick(object sender, EventArgs e)
        {
            var header = MusicListBox.SelectedItem as VoxHeader;
            if (header == null)
                return;

            var response = MessageBox.Show(
                $"Are you sure want to delete selected song?",
                "Delete",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (response == DialogResult.No)
                return;

            AssetManager.Headers.Remove(header);
            MusicListBox.Items.Remove(header);
            // Drop the sidecar URL too so a stale id doesn't survive into a
            // future export; orphaned URLs would just confuse the importer.
            MixUrlStore.RemoveUrl(AssetManager.MixPath, header.Id);

            // Clear pending modification, since this asset will be deleted anyway
            _actions[header.Ascii] = new Queue<Action>();
            _actions[header.Ascii].Enqueue(() => AssetManager.DeleteAssets(header));

            if (_autosave)
                Save(AssetManager.MdbFilename);
        }

        private void OnPathTextBoxTextChanged(object sender, EventArgs e)
        {
            MusicGroupBox.Enabled = !string.IsNullOrEmpty(PathTextBox.Text);
        }
        #endregion

        #region --- Functions ---

        private void LoadMusicDb()
        {
            using (var loader = new LoadingForm())
            {
                loader.SetAction(dialog =>
                {
                    MusicListBox.Items.Clear();
                    foreach (var header in AssetManager.Headers)
                    {
                        MusicListBox.Items.Add(header);

                        dialog.SetStatus(header.Title);
                        dialog.SetProgress(((float)MusicListBox.Items.Count / AssetManager.Headers.Count) * 100f);
                    }

                    dialog.Complete();
                });


                loader.ShowDialog();
            }

            // Apply the active sort once after the bulk add — much cheaper
            // than re-sorting per-item, and AssetManager.Headers comes back
            // in DB order, not user-visible order.
            ApplyCurrentSort();
        }

        private bool Save(string dbFilename)
        {
            var errors = new List<string>();
            using (var loader = new LoadingForm())
            {
                var proc = new Action<LoadingForm>(dialog =>
                {
                    float it = 1f;
                    foreach (var action in _actions)
                    {
                        float progress = (it++ / _actions.Count) * 100f;
                        dialog.SetStatus($"[{progress:00}%] - Processing {action.Key} assets..");
                        dialog.SetProgress(progress);

                        var queue = action.Value;
                        while (queue.Count > 0)
                        {
                            try
                            {
                                queue.Dequeue()?.Invoke();
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"{action.Key}: {ex.Message}");
                                Debug.WriteLine(ex.Message);
                            }
                        }
                    }

                    dialog.SetStatus("[100%] - Processing Music DB..");
                    dialog.SetProgress(100f);

                    AssetManager.Headers.Save(dbFilename);
                    dialog.Complete();
                });

                loader.SetAction(dialog => new Thread(() => proc(dialog)).Start());
                loader.ShowDialog();
            }

            if (errors.Count > 0)
            {
                string message = "Error occured when processing following assets:\n";
                foreach (var err in errors)
                    message += $"\n{err}";

                MessageBox.Show(message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            _actions.Clear();
            _pristine = true;

            return errors.Count == 0;
        }

        private void ImportAudioFile(bool preview = false)
        {
            var header = MusicListBox.SelectedItem as VoxHeader;
            if (header == null)
                return;

            using (var browser = new OpenFileDialog())
            {
                browser.Filter = "All supported formats|*.2dx;*.s3v;*.asf;*.wav;*.ogg;*.mp3;*.flac|BEMANI Music Files|*.2dx;*.s3v|Music Files|*.wav;*.ogg;*.mp3;*.flac;*.asf";
                browser.CheckFileExists = true;

                if (browser.ShowDialog() != DialogResult.OK)
                    return;

                string source = browser.FileName;
                string tmp = Path.Combine(
                    Path.GetTempPath(),
                    $"{Path.GetRandomFileName()}{new FileInfo(source).Extension}"
                );

                File.Copy(source, tmp);
                if (!_actions.ContainsKey(header.Ascii))
                    _actions[header.Ascii] = new Queue<Action>();

                var importOptions = new AudioImportOptions
                {
                    Format    = browser.FileName.ToLower().EndsWith(".s3v") ? AudioFormat.S3V : AudioFormat.Iidx,
                    IsPreview = preview,
                };
                _actions[header.Ascii].Enqueue(() => AssetManager.ImportAudio(tmp, header, importOptions));

                if (_autosave)
                    Save(AssetManager.MdbFilename);
            }
        }

        private void LoadJacket(VoxHeader header)
        {
            string defaultJacket = $"{AssetManager.GetDefaultJacketPath(header)}_s.png";
            foreach (Difficulty diff in Enum.GetValues(typeof(Difficulty)))
            {
                PictureBox picture;
                switch (diff)
                {
                    case Difficulty.Novice:   picture = JacketNovPictureBox; break;
                    case Difficulty.Advanced: picture = JacketAdvPictureBox; break;
                    case Difficulty.Exhaust:  picture = JacketExhPictureBox; break;
                    default:                  picture = JacketInfPictureBox; break;
                }

                if (!header.Levels.ContainsKey(diff))
                {
                    picture.Image = _dummyJacket;
                    continue;
                }

                try
                {
                    string filename = $"{AssetManager.GetJacketPath(header, diff)}_s.png";
                    if (File.Exists(filename) && (_pristine || header.Levels[diff].Jacket == null))
                    {
                        using (var image = Image.FromFile(filename))
                            picture.Image = new Bitmap(image);

                        if (header.Levels[diff].Jacket != null) // clear cache
                        {
                            header.Levels[diff].Jacket.Dispose();
                            header.Levels[diff].Jacket = null; 
                        }
                    }
                    else if (header.Levels[diff].Jacket != null)
                    {
                        // use cache for new + unsaved header
                        picture.Image = header.Levels[diff].Jacket;
                    }
                    else if (File.Exists(defaultJacket))
                    {
                        using (var image = Image.FromFile(defaultJacket))
                            picture.Image = new Bitmap(image);                    
                    }
                    else
                        picture.Image = _dummyJacket;
                }
                catch (Exception)
                {
                    picture.Image = _dummyJacket;
                }
            }
        }

        private void Reload()
        {
            PathTextBox.Text = AssetManager.MixPath;
            MetadataGroupBox.Enabled = false;

            ResetEditor();
            DisableUi();

            LoadMusicDb();
            EnableUi();
        }

        private void EnableUi()
        {
            UpdateUi(true);
        }

        private void DisableUi()
        {
            UpdateUi(false);
            ResetEditor();
        }

        private void UpdateUi(bool state)
        {
            // Dont break your goddamn kfc
            bool safe = !string.IsNullOrEmpty(AssetManager.MixName);

            // Container
            MusicGroupBox.Enabled     = state;

            // Menu
            SaveFileMenu.Enabled      = state && safe; 
            SaveAsFileMenu.Enabled    = state;
            ChangeMixFileMenu.Enabled = state;
            DeleteMixFileMenu.Enabled = state && safe;
            ExportMixFileMenu.Enabled = state && safe;
            ImportMixFileMenu.Enabled = state && safe;
            AddButton.Enabled         = state && safe;
            AddEditMenu.Enabled       = state && safe;
            RemoveButton.Enabled      = state && safe;
            RemoveEditMenu.Enabled    = state && safe;
            EditMenu.Enabled          = state && safe;
            ImportAudioEditMenu.Enabled = state && safe;
            ImportAudioPreviewEditMenu.Enabled = state && safe;

            foreach (Control control in MetadataGroupBox.Controls)
            {
                if (control is TextBox textBox)
                    textBox.ReadOnly = !safe;
                else if (control is NumericUpDown numeric)
                    numeric.Enabled = safe;
                else if (!(control is PictureBox))
                    control.Enabled = safe;
            }

            IdTextBox.ReadOnly    = !safe;
            LevelGroupBox.Enabled = true;
            foreach (Control control in LevelGroupBox.Controls)
            {
                if (!(control is PictureBox))
                    control.Enabled = safe;
                else
                    control.Enabled = true;
            }
        }

        private void ResetEditor()
        {
            foreach (Control control in MetadataGroupBox.Controls)
            {
                if (control is TextBox)
                    control.Text                       = "";
                else if (control is NumericUpDown)
                    (control as NumericUpDown).Value   = 1;
                else if (control is ComboBox)
                    (control as ComboBox).SelectedItem = null;
            }

            foreach (Control control in LevelGroupBox.Controls)
            {
                if (control is PictureBox)
                {
                    var pictureBox = control as PictureBox;
                    pictureBox.Image = _dummyJacket;
                }
                else if (control is Button && control.Tag.ToString() == "4")
                {
                    control.Text = "--";
                }
            }

            DistributionPicker.Value = DateTime.Now;
            VolumeTrackBar.Value     = DefaultVolume;

            EditMenu.Enabled                 = false;
            ImportAudioEditMenu.Enabled        = false;
            ImportAudioPreviewEditMenu.Enabled = false;
            ExplorerEditMenu.Enabled         = false;
        }
        #endregion

    }
}
