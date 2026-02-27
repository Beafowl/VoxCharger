using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace VoxCharger
{
    public partial class RemoteMixForm : Form
    {
        private RemoteMixClient _client;
        private KsmDownloader _downloader = new KsmDownloader();
        private List<RemoteMix> _mixes;
        private RemoteMixDetail _selectedMix;

        public List<VoxHeader> ImportedHeaders { get; } = new List<VoxHeader>();
        public Dictionary<string, Queue<Action>> ImportedActions { get; } = new Dictionary<string, Queue<Action>>();

        public RemoteMixForm()
        {
            InitializeComponent();
            ServerUrlTextBox.Text = Properties.Settings.Default.ServerUrl ?? "";
        }

        #region --- Connection ---
        private void OnConnectButtonClick(object sender, EventArgs e)
        {
            string url = ServerUrlTextBox.Text.Trim();
            if (string.IsNullOrEmpty(url))
            {
                MessageBox.Show("Please enter a server URL.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Properties.Settings.Default.ServerUrl = url;
            Properties.Settings.Default.Save();

            _client?.Dispose();
            _client = new RemoteMixClient(url);

            RefreshMixes();
        }

        private void RefreshMixes()
        {
            try
            {
                _mixes = _client.GetMixes();
                MixListBox.Items.Clear();
                SongListBox.Items.Clear();
                _selectedMix = null;

                foreach (var mix in _mixes)
                    MixListBox.Items.Add(mix);

                CreateMixButton.Enabled = true;
                DeleteMixButton.Enabled = false;
                AddSongButton.Enabled = false;
                RemoveSongButton.Enabled = false;
                SyncButton.Enabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to connect to server:\n{ex.Message}",
                    "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region --- Mix Management ---
        private void OnMixListBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            if (MixListBox.SelectedIndex < 0 || _mixes == null)
                return;

            var mix = _mixes[MixListBox.SelectedIndex];
            try
            {
                _selectedMix = _client.GetMix(mix.id);
                RefreshSongs();
                DeleteMixButton.Enabled = true;
                AddSongButton.Enabled = true;
                SyncButton.Enabled = _selectedMix.songs.Count > 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load mix:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void RefreshSongs()
        {
            SongListBox.Items.Clear();
            RemoveSongButton.Enabled = false;

            if (_selectedMix == null)
                return;

            foreach (var song in _selectedMix.songs)
                SongListBox.Items.Add(song);

            SyncButton.Enabled = _selectedMix.songs.Count > 0;
        }

        private void OnCreateMixButtonClick(object sender, EventArgs e)
        {
            using (var form = new CreateRemoteMixForm())
            {
                if (form.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    _client.CreateMix(form.MixName, form.MusicIdStart);
                    RefreshMixes();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create mix:\n{ex.Message}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private void OnDeleteMixButtonClick(object sender, EventArgs e)
        {
            if (_selectedMix == null)
                return;

            var confirm = MessageBox.Show(
                $"Are you sure you want to delete \"{_selectedMix.name}\"?",
                "Delete Mix", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                _client.RemoveMix(_selectedMix.id);
                _selectedMix = null;
                RefreshMixes();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to delete mix:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region --- Song Management ---
        private void OnAddSongButtonClick(object sender, EventArgs e)
        {
            if (_selectedMix == null)
                return;

            using (var inputForm = new InputForm("Add Song", "Enter ksm.dev URL:"))
            {
                if (inputForm.ShowDialog() != DialogResult.OK)
                    return;

                string url = inputForm.Value;
                if (string.IsNullOrEmpty(url))
                    return;

                string title = null;
                string artist = null;
                string error = null;

                using (var loader = new LoadingForm())
                {
                    loader.SetAction(dialog =>
                    {
                        new Thread(() =>
                        {
                            string tempDir = null;
                            try
                            {
                                dialog.SetStatus("Downloading chart..");
                                tempDir = _downloader.DownloadAndExtract(url);
                                string kshFile = KsmDownloader.FindKshFile(tempDir);

                                if (kshFile != null)
                                {
                                    dialog.SetStatus("Reading chart metadata..");
                                    var ksh = new Ksh();
                                    ksh.Parse(kshFile);
                                    title = ksh.Title;
                                    artist = ksh.Artist;
                                }

                                dialog.SetStatus("Adding to server..");
                                _client.AddSong(_selectedMix.id, url, title, artist);
                                dialog.Complete();
                            }
                            catch (Exception ex)
                            {
                                error = ex.Message;
                                dialog.DialogResult = DialogResult.Abort;
                                dialog.Complete();
                            }
                            finally
                            {
                                if (tempDir != null)
                                    KsmDownloader.Cleanup(tempDir);
                            }
                        }).Start();
                    });

                    loader.ShowDialog();
                }

                if (error != null)
                {
                    MessageBox.Show($"Failed to add song:\n{error}",
                        "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }

                try
                {
                    _selectedMix = _client.GetMix(_selectedMix.id);
                    RefreshSongs();

                    // Update song count in mix list
                    int idx = MixListBox.SelectedIndex;
                    if (idx >= 0)
                    {
                        _mixes[idx].song_count = _selectedMix.songs.Count;
                        MixListBox.Items[idx] = _mixes[idx];
                    }
                }
                catch { }
            }
        }

        private void OnRemoveSongButtonClick(object sender, EventArgs e)
        {
            if (_selectedMix == null || SongListBox.SelectedIndex < 0)
                return;

            var song = _selectedMix.songs[SongListBox.SelectedIndex];
            var confirm = MessageBox.Show(
                $"Remove \"{song.title ?? "Unknown"}\" from this mix?",
                "Remove Song", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                _client.RemoveSong(_selectedMix.id, song.id);
                _selectedMix = _client.GetMix(_selectedMix.id);
                RefreshSongs();

                int idx = MixListBox.SelectedIndex;
                if (idx >= 0)
                {
                    _mixes[idx].song_count = _selectedMix.songs.Count;
                    MixListBox.Items[idx] = _mixes[idx];
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to remove song:\n{ex.Message}",
                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region --- Sync Pipeline ---
        private void OnSyncButtonClick(object sender, EventArgs e)
        {
            if (_selectedMix == null || _selectedMix.songs.Count == 0)
                return;

            if (string.IsNullOrEmpty(AssetManager.MixPath) || string.IsNullOrEmpty(AssetManager.MixName))
            {
                MessageBox.Show(
                    "Please open or create a local mix first before syncing.",
                    "Remote Mixes", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var errors = new List<string>();
            int synced = 0;
            int skipped = 0;

            using (var loader = new LoadingForm())
            {
                loader.SetAction(dialog =>
                {
                    new Thread(() =>
                    {
                        int current = 0;
                        int total = _selectedMix.songs.Count;

                        foreach (var song in _selectedMix.songs)
                        {
                            current++;
                            float progress = ((float)current / total) * 100f;

                            // Skip if already imported
                            if (AssetManager.Headers.Contains(song.music_id))
                            {
                                skipped++;
                                dialog.SetStatus($"[{current}/{total}] Skipping {song.title} (already synced)");
                                dialog.SetProgress(progress);
                                continue;
                            }

                            string tempDir = null;
                            try
                            {
                                // Download
                                dialog.SetStatus($"[{current}/{total}] Downloading {song.title ?? "Unknown"}..");
                                dialog.SetProgress(progress);
                                tempDir = _downloader.DownloadAndExtract(song.url);

                                // Find KSH
                                string kshFile = KsmDownloader.FindKshFile(tempDir);
                                if (kshFile == null)
                                {
                                    errors.Add($"{song.title}: No KSH file found in download");
                                    continue;
                                }

                                // Parse
                                dialog.SetStatus($"[{current}/{total}] Converting {song.title ?? "Unknown"}..");
                                var ksh = new Ksh();
                                ksh.Parse(kshFile);

                                // Create header with server-assigned music ID
                                var header = ksh.ToHeader();
                                header.Id = song.music_id;
                                header.Ascii = SanitizeAscii(song.title ?? "remote");
                                header.Version = GameVersion.VividWave;
                                header.InfVersion = InfiniteVersion.Mxm;
                                header.GenreId = 16;
                                header.Levels = new Dictionary<Difficulty, VoxLevelHeader>();

                                // Ensure unique ascii
                                string baseAscii = header.Ascii;
                                int counter = 1;
                                while (AssetManager.Headers.Any(h => h.Ascii == header.Ascii) ||
                                       ImportedHeaders.Any(h => h.Ascii == header.Ascii) ||
                                       Directory.Exists(AssetManager.GetMusicPath(header)))
                                {
                                    header.Ascii = $"{baseAscii}_{counter++:D2}";
                                    if (counter > 99) break;
                                }

                                // Discover all difficulty charts
                                string chartDir = Path.GetDirectoryName(kshFile);
                                var charts = Ksh.Exporter.GetCharts(chartDir, ksh.Title);
                                if (charts.Count == 0)
                                    charts[ksh.Difficulty] = new ChartInfo(ksh, ksh.ToLevelHeader(), kshFile);

                                // Export using existing pipeline
                                var exporter = new Ksh.Exporter(ksh);
                                exporter.Export(header, charts);

                                // Collect results
                                ImportedHeaders.Add(header);
                                if (!ImportedActions.ContainsKey(header.Ascii))
                                    ImportedActions[header.Ascii] = new Queue<Action>();
                                ImportedActions[header.Ascii].Enqueue(exporter.Action);

                                synced++;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"{song.title ?? "Unknown"}: {ex.Message}");
                                Debug.WriteLine($"Remote sync error: {ex}");
                            }
                            finally
                            {
                                if (tempDir != null)
                                    KsmDownloader.Cleanup(tempDir);
                            }
                        }

                        dialog.Complete();
                    }).Start();
                });

                loader.ShowDialog();
            }

            // Report results
            string message = $"Sync complete: {synced} imported, {skipped} skipped.";
            if (errors.Count > 0)
            {
                message += $"\n\n{errors.Count} error(s):";
                foreach (string err in errors)
                    message += $"\n- {err}";
            }

            MessageBox.Show(message, "Sync Results", MessageBoxButtons.OK,
                errors.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            if (ImportedHeaders.Count > 0)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        }
        #endregion

        #region --- Helpers ---
        private static string SanitizeAscii(string title)
        {
            var sb = new StringBuilder();
            foreach (char c in title.ToLower())
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
                else if (c == ' ')
                    sb.Append('_');
            }

            string result = sb.ToString().Trim('_');
            if (string.IsNullOrEmpty(result))
                result = "remote";

            if (result.Length > 16)
                result = result.Substring(0, 16);

            return result;
        }
        #endregion
    }
}
