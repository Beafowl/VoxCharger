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
        // The game's own songs from data\others\music_db.xml, read-only
        private const string ViewOriginal = "*original";

        private readonly Dictionary<string, Queue<Action>> _actions = new Dictionary<string, Queue<Action>>();

        // False while the loaded mix has changes that are not written yet; drives the Save button
        private bool _pristine = true;

        // Mix name or ViewOriginal
        private string _view;

        private enum SortMode { TitleAsc, TitleDesc, IdAsc, IdDesc }
        private SortMode _sortMode = SortMode.TitleAsc;

        // Live search filter applied to the music list: title, artist, ascii code or music id
        private string _searchText = string.Empty;

        private readonly ContextMenuStrip _songsMenu = DarkMenuRenderer.CreateMenu();
        private readonly ContextMenuStrip _mixMenu   = DarkMenuRenderer.CreateMenu();
        private readonly ContextMenuStrip _toolsMenu = DarkMenuRenderer.CreateMenu();
        private readonly ContextMenuStrip _sortMenu  = DarkMenuRenderer.CreateMenu();
        private readonly ContextMenuStrip _viewMenu  = DarkMenuRenderer.CreateMenu();
        private readonly ContextMenuStrip _listMenu  = DarkMenuRenderer.CreateMenu();

        private ToolStripMenuItem _deleteMixItem;
        private ToolStripMenuItem _deleteSongItem;
        private ToolStripMenuItem _mixTagItem;
        private ToolStripMenuItem _startIdItem;
        private ToolStripMenuItem _reconvertItem;
        private ToolStripMenuItem _removeSongItem;
        private ToolStripSeparator _listSeparator;

        private Image _songJacket;
        #endregion

        // EM_SETCUEBANNER puts greyed-out placeholder text in the search box
        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, string lParam);
        private const int EM_SETCUEBANNER = 0x1501;

        private bool HasGame  => !string.IsNullOrEmpty(AssetManager.GamePath);
        private bool Original => _view == ViewOriginal;
        private bool CanEdit  => HasGame && !Original && !string.IsNullOrEmpty(_view) && !string.IsNullOrEmpty(AssetManager.MixName);

        private IEnumerable<VoxHeader> CurrentHeaders => AssetManager.Headers;

        private ChartTile[] Tiles => new[] { NovTile, AdvTile, ExhTile, InfTile };

        #region --- Form ---
        public MainForm()
        {
            InitializeComponent();

            var icon = IconLoader.ForCurrentTheme();
            if (icon != null) Icon = icon;

            BuildMenus();
            foreach (var tile in Tiles)
            {
                tile.EditClicked   += OnChartEditClick;
                tile.RemoveClicked += OnChartRemoveClick;
            }

            MusicListBox.ContextMenuStrip = _listMenu;
            HandleCreated += (s, e) => Theme.DarkTitleBar(this);
            foreach (var panel in new Control[] { HeaderPanel, SidePanel, ContentPanel })
                panel.SizeChanged += (s, e) => LayoutWindow();
            Theme.DarkScrollBars(ContentPanel);
        }

        private void OnMainFormLoad(object sender, EventArgs e)
        {
            SendMessage(SearchBox.Input.Handle, EM_SETCUEBANNER, 1, "Search title, artist or ID");
            LayoutWindow();

            string gamePath = null;
            foreach (string candidate in new[] { Application.StartupPath, UiSettings.GamePath })
            {
                if (!string.IsNullOrEmpty(candidate) && File.Exists(Path.Combine(candidate, @"data\others\music_db.xml")))
                {
                    gamePath = candidate;
                    break;
                }
            }

            if (gamePath == null || !OpenGame(gamePath))
                UpdateUi();
        }

        private void OnMainFormFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!ConfirmUnsaved("Quit"))
                e.Cancel = true;
        }

        // Asks to save pending changes. False when the user cancels.
        private bool ConfirmUnsaved(string title)
        {
            if (_pristine || !CanEdit)
                return true;

            var response = MessageBox.Show(
                $"Save the changes to \"{AssetManager.MixName}\" first?",
                title,
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question
            );

            if (response == DialogResult.Cancel)
                return false;

            if (response == DialogResult.Yes)
                SaveMix();

            _actions.Clear();
            _pristine = true;
            return true;
        }

        private void MarkDirty()
        {
            _pristine = false;
            UpdateUi();
        }
        #endregion

        #region --- Layout ---
        private void LayoutWindow()
        {
            if (HeaderPanel == null)
                return;

            // Header: search after the app name, actions on the right
            int right = HeaderPanel.Width - 16;
            AboutButton.Location = new Point(right - AboutButton.Width, 10);
            ToolsButton.Location = new Point(AboutButton.Left - 4 - ToolsButton.Width, 10);
            SaveButton.Location  = new Point(ToolsButton.Left - 10 - SaveButton.Width, 10);
            // Search sits above the song column it filters
            SearchBox.SetBounds(16, 10, Math.Max(160, Math.Min(SidePanel.Width - 32, SaveButton.Left - 24 - 16)), 32);

            // Left column
            int w = SidePanel.Width;
            MixButton.SetBounds(16, 36, w - 32, 40);
            ListHeaderPanel.SetBounds(0, 88, w, 34);
            SortButton.Location = new Point(w - 12 - SortButton.Width, 4);
            BottomPanel.SetBounds(0, SidePanel.Height - 60, w, 60);
            SongsButton.SetBounds(16, 12, (w - 42) / 2, 36);
            MixMenuButton.SetBounds(SongsButton.Right + 10, 12, w - 32 - SongsButton.Width - 10, 36);
            MusicListBox.SetBounds(0, ListHeaderPanel.Bottom, w, BottomPanel.Top - ListHeaderPanel.Bottom);
            EmptyListLabel.Bounds = MusicListBox.Bounds;

            // Right column
            int cw = ContentPanel.ClientSize.Width - 48;
            int ch = ContentPanel.ClientSize.Height;
            EmptyContentLabel.SetBounds(24, 0, cw, ch);
            GamePathButton.Location = new Point((ContentPanel.ClientSize.Width - GamePathButton.Width) / 2, ch / 2 + 24);

            // Metadata: jacket and names, then one row of values and one row of files
            const int gap = 10;
            MetadataCard.SetBounds(24, 20, cw, 20 + 96 + 20 + 62 + gap + 62 + 20);
            JacketBox.Location = new Point(20, 20);
            EditMetadataButton.Location = new Point(cw - 20 - EditMetadataButton.Width, 50);
            ExplorerButton.Location = new Point(EditMetadataButton.Left - 8 - ExplorerButton.Width, 50);
            int textLeft = JacketBox.Right + 20;
            TitleLabel.SetBounds(textLeft - 3, 34, ExplorerButton.Left - 16 - textLeft, 36);
            ArtistLabel.SetBounds(textLeft, 72, ExplorerButton.Left - 16 - textLeft, 24);

            int rowY  = JacketBox.Bottom + 20;
            var stats = new[] { BpmTile, VolumeTile, DateTile, BackgroundTile, VersionTile, TagTile };
            int tileW = (cw - 40 - gap * (stats.Length - 1)) / stats.Length;
            for (int i = 0; i < stats.Length; i++)
                stats[i].SetBounds(20 + i * (tileW + gap), rowY, tileW, 62);

            int fileW = (cw - 40 - gap) / 2;
            MusicTile.SetBounds(20, rowY + 62 + gap, fileW, 62);
            PreviewTile.SetBounds(20 + fileW + gap, rowY + 62 + gap, cw - 40 - fileW - gap, 62);

            ChartsCard.SetBounds(24, MetadataCard.Bottom + 16, cw, 60 + 88 + 12 + 88 + 20);
            ReconvertButton.Location = new Point(cw - 20 - ReconvertButton.Width, 14);
            int chartW = (cw - 40 - 12) / 2;
            NovTile.SetBounds(20, 60, chartW, 88);
            AdvTile.SetBounds(20 + chartW + 12, 60, chartW, 88);
            ExhTile.SetBounds(20, 60 + 88 + 12, chartW, 88);
            InfTile.SetBounds(20 + chartW + 12, 60 + 88 + 12, chartW, 88);
        }

        private void OnHeaderPanelPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            using (var pen = new Pen(Theme.Border))
                g.DrawLine(pen, 0, HeaderPanel.Height - 1, HeaderPanel.Width, HeaderPanel.Height - 1);
        }

        private void OnSidePanelPaint(object sender, PaintEventArgs e)
        {
            using (var pen = new Pen(Theme.Border))
                e.Graphics.DrawLine(pen, SidePanel.Width - 1, 0, SidePanel.Width - 1, SidePanel.Height);
        }

        private void OnStripPaint(object sender, PaintEventArgs e)
        {
            var panel = (Control)sender;
            using (var pen = new Pen(Theme.Border))
            {
                e.Graphics.DrawLine(pen, 0, 0, panel.Width, 0);
                e.Graphics.DrawLine(pen, 0, panel.Height - 1, panel.Width, panel.Height - 1);
                e.Graphics.DrawLine(pen, panel.Width - 1, 0, panel.Width - 1, panel.Height);
            }
        }

        private void OnJacketBoxPaint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Card);
            Theme.Smooth(g);
            var rect = new Rectangle(0, 0, JacketBox.Width - 1, JacketBox.Height - 1);
            var header = MusicListBox.SelectedItem as VoxHeader;
            if (_songJacket != null)
                JacketCache.DrawImage(g, _songJacket, rect, 10);
            else
                JacketCache.DrawPlaceholder(g, rect, VoxHeader.WithDecodedSymbols(header?.Title), 10, new Font("Segoe UI", 24f, FontStyle.Bold));
        }
        #endregion

        #region --- Menus ---
        private void BuildMenus()
        {
            var addSong = DarkMenuRenderer.Item("Add song");
            addSong.DropDownItems.Add(DarkMenuRenderer.Item("From KSH…", OnSingleImportMenuClick));
            addSong.DropDownItems.Add(DarkMenuRenderer.Item("From URL…", OnImportKsmDevMenuClick));
            addSong.DropDownItems.Add(DarkMenuRenderer.Item("New", OnAddNewMenuClick));

            var addSongs = DarkMenuRenderer.Item("Add songs");
            addSongs.DropDownItems.Add(DarkMenuRenderer.Item("From KSH folder…", OnBulkImportKshMenuClick));
            addSongs.DropDownItems.Add(DarkMenuRenderer.Item("From JSON…", OnImportJsonMenuClick));

            _deleteSongItem = DarkMenuRenderer.Item("Delete song", OnRemoveButtonClick, "Del");
            _deleteSongItem.Tag = "danger";
            _songsMenu.Items.AddRange(new ToolStripItem[] { addSong, addSongs, new ToolStripSeparator(), _deleteSongItem });
            _songsMenu.Opening += (s, e) => _deleteSongItem.Enabled = CanEdit && MusicListBox.SelectedItem is VoxHeader;

            _deleteMixItem = DarkMenuRenderer.Item("Delete", OnDeleteMixFileMenuClick);
            _deleteMixItem.Tag = "danger";
            _mixMenu.Items.AddRange(new ToolStripItem[] { DarkMenuRenderer.Item("Create…", OnNewFileMenuClick), _deleteMixItem });

            _mixTagItem  = DarkMenuRenderer.Item("Set mix tag…", OnMixTagFileMenuClick, "");
            _startIdItem = DarkMenuRenderer.Item("Set starting ID…", OnMixStartIdFileMenuClick, "");
            _toolsMenu.Items.AddRange(new ToolStripItem[] { _mixTagItem, _startIdItem });
            _toolsMenu.Opening += (s, e) =>
            {
                _mixTagItem.ShortcutKeyDisplayString  = AssetManager.MixTagId > 0 ? AssetManager.MixTagId.ToString() : "none";
                _startIdItem.ShortcutKeyDisplayString = AssetManager.MixStartId > 0 ? AssetManager.MixStartId.ToString() : "none";
            };

            _sortMenu.Items.AddRange(new ToolStripItem[]
            {
                DarkMenuRenderer.Item("Title A to Z", (s, e) => SetSortMode(SortMode.TitleAsc,  "Title A to Z")),
                DarkMenuRenderer.Item("Title Z to A", (s, e) => SetSortMode(SortMode.TitleDesc, "Title Z to A")),
                DarkMenuRenderer.Item("ID ascending", (s, e) => SetSortMode(SortMode.IdAsc,     "ID ascending")),
                DarkMenuRenderer.Item("ID descending", (s, e) => SetSortMode(SortMode.IdDesc,   "ID descending")),
            });

            _reconvertItem  = DarkMenuRenderer.Item("Reconvert from ksm.dev", OnReconvertFromUrlClick);
            _removeSongItem = DarkMenuRenderer.Item("Delete song", OnRemoveButtonClick, "Del");
            _removeSongItem.Tag = "danger";
            _listSeparator  = new ToolStripSeparator();
            _listMenu.Items.AddRange(new ToolStripItem[] { DarkMenuRenderer.Item("Show in Explorer", OnExplorerEditMenuClick), _reconvertItem, _listSeparator, _removeSongItem });
            _listMenu.Opening += (s, e) =>
            {
                if (!(MusicListBox.SelectedItem is VoxHeader))
                {
                    e.Cancel = true;
                    return;
                }

                RefreshReconvertControls();
                _reconvertItem.Visible  = CanEdit;
                _removeSongItem.Visible = _listSeparator.Visible = CanEdit;
            };

            foreach (var pair in new[] { (_songsMenu, SongsButton), (_mixMenu, MixMenuButton), (_toolsMenu, ToolsButton), (_sortMenu, SortButton), (_viewMenu, MixButton) })
            {
                var button = pair.Item2;
                pair.Item1.Closed += (s, e) => { button.Highlighted = false; button.Invalidate(); };
            }
        }

        private void ShowMenu(ContextMenuStrip menu, FlatButton button, bool above, bool alignRight = false)
        {
            button.Highlighted = true;
            button.Invalidate();
            menu.MinimumSize = new Size(alignRight ? 0 : Math.Min(button.Width, 360), 0);
            if (above)
                menu.Show(button, new Point(0, -4), ToolStripDropDownDirection.AboveRight);
            else if (alignRight)
                menu.Show(button, new Point(button.Width, button.Height + 4), ToolStripDropDownDirection.BelowLeft);
            else
                menu.Show(button, new Point(0, button.Height + 4), ToolStripDropDownDirection.BelowRight);
        }

        private void OnSongsButtonClick(object sender, EventArgs e)   => ShowMenu(_songsMenu, SongsButton, false);
        private void OnMixMenuButtonClick(object sender, EventArgs e) => ShowMenu(_mixMenu, MixMenuButton, false);
        private void OnToolsButtonClick(object sender, EventArgs e)   => ShowMenu(_toolsMenu, ToolsButton, false, true);
        private void OnSortButtonClick(object sender, EventArgs e)    => ShowMenu(_sortMenu, SortButton, false, true);

        // Mix selector: the two combined views, then every mix with its song count
        private void OnMixButtonClick(object sender, EventArgs e)
        {
            if (!HasGame)
            {
                OnGamePathButtonClick(sender, e);
                return;
            }

            _viewMenu.Items.Clear();
            _viewMenu.Items.Add(DarkMenuRenderer.Group("GAME"));
            int original = AssetManager.CountOriginalSongs();
            _viewMenu.Items.Add(ViewItem("Original", ViewOriginal, $"{original} songs, read-only"));

            var mixes = AssetManager.GetMixes().OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
            _viewMenu.Items.Add(new ToolStripSeparator());
            _viewMenu.Items.Add(DarkMenuRenderer.Group("MIXES"));
            if (mixes.Count == 0)
                _viewMenu.Items.Add(new ToolStripMenuItem("No mixes yet, use Mix › Create") { Enabled = false, Padding = new Padding(4, 6, 4, 6) });

            foreach (string mix in mixes)
            {
                int count = AssetManager.CountSongs(mix);
                _viewMenu.Items.Add(ViewItem(mix, mix, $"{count} song{(count == 1 ? "" : "s")}"));
            }

            ShowMenu(_viewMenu, MixButton, false);
        }

        private ToolStripMenuItem ViewItem(string text, string view, string detail)
        {
            var item = DarkMenuRenderer.Item(text, (s, e) => SelectView(view), detail);
            if (view == _view)
                item.Font = Theme.BodyBold;
            return item;
        }
        #endregion

        #region --- Game folder and views ---
        private void OnGamePathButtonClick(object sender, EventArgs e)
        {
            if (!ConfirmUnsaved("Game folder"))
                return;

            using (var browser = new CommonOpenFileDialog())
            {
                browser.IsFolderPicker = true;
                browser.Multiselect    = false;
                browser.Title          = "Select the game's contents folder (the one holding data and data_mods)";

                if (browser.ShowDialog() != CommonFileDialogResult.Ok)
                    return;

                if (!File.Exists(Path.Combine(browser.FileName, @"data\others\music_db.xml")))
                {
                    MessageBox.Show(
                        "This folder has no data\\others\\music_db.xml. Pick the game's contents folder.",
                        "Game folder",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );
                    return;
                }

                OpenGame(browser.FileName);
            }
        }

        private bool OpenGame(string gamePath)
        {
            try
            {
                Directory.CreateDirectory(Path.Combine(gamePath, "data_mods"));
                AssetManager.Initialize(gamePath);
                UiSettings.GamePath = gamePath;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Game folder", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }

            var mixes = AssetManager.GetMixes();
            string view = UiSettings.LastView;
            if (view != ViewOriginal && !mixes.Contains(view))
                view = mixes.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? ViewOriginal;

            SelectView(view, false);
            return true;
        }

        // Switches the song list to a mix or the game's own songs
        private void SelectView(string view, bool confirm = true)
        {
            if (confirm && view != _view && !ConfirmUnsaved("Change mix"))
                return;

            Cursor = Cursors.WaitCursor;
            try
            {
                AssetManager.LoadMix(view == ViewOriginal ? string.Empty : view);

                _view = view;
                UiSettings.LastView = view;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Open mix", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _view = null;
            }
            finally
            {
                Cursor = Cursors.Default;
            }

            _actions.Clear();
            _pristine = true;
            JacketCache.Clear();

            switch (_view)
            {
                case null:         MixButton.Text = "Choose a mix"; MixButton.SubText = null; break;
                case ViewOriginal: MixButton.Text = "Original"; break;
                default:           MixButton.Text = _view; break;
            }

            MusicListBox.ClearSelected();
            ApplyCurrentSort();
            UpdateUi();
            ShowSelection();
        }

        private void UpdateUi()
        {
            bool game = HasGame;
            int count = _view == null ? 0 : CurrentHeaders.Count();

            MixButton.SubText       = _view == null ? null : $"{count} song{(count == 1 ? "" : "s")}";
            MixButton.Invalidate();
            ListCaptionLabel.Text   = Original ? "Songs of the game, read-only" : "Songs in this mix";
            SongsButton.Enabled     = CanEdit;
            MixMenuButton.Enabled   = game;
            _deleteMixItem.Enabled  = CanEdit;
            ToolsButton.Enabled     = CanEdit;
            SaveButton.Enabled      = CanEdit && !_pristine;
            SortButton.Enabled      = game;
            MixButton.Enabled       = true;
            MusicListBox.Enabled    = game;
            GamePathButton.Visible  = !game;

            if (!game)
                EmptyContentLabel.Text = "VoxCharger needs the game's contents folder, the one holding data and data_mods.";
            else if (_view == null)
                EmptyContentLabel.Text = "Choose a mix above the song list.";
            else
                EmptyContentLabel.Text = "Select a song from the list";

            bool emptyList = game && _view != null && MusicListBox.Items.Count == 0;
            EmptyListLabel.Visible = emptyList;
            if (emptyList)
            {
                if (!string.IsNullOrEmpty(_searchText))
                    EmptyListLabel.Text = "No song matches the search.";
                else if (CanEdit)
                    EmptyListLabel.Text = "No songs in this mix yet.\nUse Songs › Add song or Add songs.";
                else
                    EmptyListLabel.Text = "No songs here.";
            }
        }
        #endregion

        #region --- Mix ---
        private void OnNewFileMenuClick(object sender, EventArgs e)
        {
            if (!ConfirmUnsaved("Create mix"))
                return;

            using (var mixSelector = new MixSelectorForm(createMode: true))
            {
                if (mixSelector.ShowDialog() != DialogResult.OK)
                    return;
            }

            string created = AssetManager.MixName;
            AssetManager.Initialize(AssetManager.GamePath);
            SelectView(created, false);
        }

        private void OnDeleteMixFileMenuClick(object sender, EventArgs e)
        {
            if (!CanEdit)
                return;

            var prompt = MessageBox.Show(
                $"Delete the mix \"{AssetManager.MixName}\" with all its songs and files?",
                "Delete mix",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (prompt != DialogResult.Yes)
                return;

            try
            {
                Directory.Delete(AssetManager.MixPath, true);
                _actions.Clear();
                _pristine = true;

                AssetManager.Initialize(AssetManager.GamePath);
                var next = AssetManager.GetMixes().OrderBy(m => m, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                SelectView(next ?? ViewOriginal, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Delete mix", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnMixTagFileMenuClick(object sender, EventArgs e)
        {
            using (var prompt = new InputForm("Mix Tag", "Tag ID for all songs in this mix (0 = no tag):", AssetManager.MixTagId.ToString()))
            {
                if (prompt.ShowDialog() != DialogResult.OK)
                    return;

                if (!int.TryParse(prompt.Value, out int tagId) || tagId < 0)
                {
                    MessageBox.Show(
                        "Tag ID must be a non-negative number",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );

                    return;
                }

                if (tagId == AssetManager.MixTagId)
                    return;

                AssetManager.SetMixTagId(tagId);
                MarkDirty();
            }
        }

        private void OnMixStartIdFileMenuClick(object sender, EventArgs e)
        {
            using (var prompt = new InputForm("Starting ID", "Renumber all songs of this mix consecutively from this ID (0 = keep the IDs as they are):", AssetManager.MixStartId.ToString()))
            {
                if (prompt.ShowDialog() != DialogResult.OK)
                    return;

                if (!int.TryParse(prompt.Value, out int startId) || startId < 0)
                {
                    MessageBox.Show(
                        "Starting ID must be a non-negative number",
                        "Error",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );

                    return;
                }

                int lastId = startId > 0 ? startId + Math.Max(0, AssetManager.Headers.Count - 1) : 0;
                if (lastId > AssetManager.MaxJacketMusicId)
                {
                    var limit = MessageBox.Show(
                        $"The game shows no jacket at song select for IDs above {AssetManager.MaxJacketMusicId} (its jacket loader stops at the tutorial ID 9001); the songs still play.\n\n" +
                        $"Numbering from {startId} puts songs up to about {lastId}. Continue anyway?",
                        "Starting ID",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning
                    );

                    if (limit != DialogResult.Yes)
                        return;
                }

                var moves = AssetManager.PlanMixStartId(startId);
                if (moves.Count > 0)
                {
                    var response = MessageBox.Show(
                        $"{moves.Count} of {AssetManager.Headers.Count} song(s) get a new ID so the mix is numbered consecutively from {startId} on.\n\n" +
                        "Their files are renamed when you save the mix. Continue?",
                        "Starting ID",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question
                    );

                    if (response != DialogResult.Yes)
                        return;
                }

                foreach (var action in AssetManager.SetMixStartId(startId))
                    QueueAction("Renumber", action);

                if (moves.Count > 0)
                    MarkDirty();

                ApplyCurrentSort();
                ShowSelection();

                string summary;
                if (startId <= 0)
                    summary = $"Starting ID removed. New songs continue after the highest ID in use ({AssetManager.GetNextMusicId()}).";
                else if (AssetManager.Headers.Count == 0)
                    summary = $"Starting ID set to {startId}. Imported songs will be numbered from {AssetManager.GetNextMusicId()} on.";
                else
                    summary = $"Songs are numbered {AssetManager.Headers.Min(h => h.Id)}..{AssetManager.Headers.Max(h => h.Id)}; new songs continue from {AssetManager.GetNextMusicId()}." +
                              (moves.Count > 0 ? "\nSave the mix to rename the files." : string.Empty);

                MessageBox.Show(summary, "Starting ID", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private void QueueAction(string key, Action action)
        {
            if (!_actions.ContainsKey(key))
                _actions[key] = new Queue<Action>();

            _actions[key].Enqueue(action);
        }

        private void OnSaveButtonClick(object sender, EventArgs e)
        {
            SaveMix();
        }

        private void SaveMix()
        {
            if (!CanEdit)
                return;

            try
            {
                Save(AssetManager.MdbFilename);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Save", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnAboutHelpMenuClick(object sender, EventArgs e)
        {
            using (var about = new AboutForm())
                about.ShowDialog();
        }
        #endregion

        #region --- Songs ---
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

            MarkDirty();
            AssetManager.Headers.Add(header);
            ApplyCurrentSort();
            MusicListBox.SelectedItem = header;
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

                    MarkDirty();
                    AssetManager.Headers.Add(header);
                    ApplyCurrentSort();

                    _actions[header.Ascii].Enqueue(converter.Action);
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

                    MarkDirty();
                    AssetManager.Headers.Add(header);
                    ApplyCurrentSort();

                    _actions[header.Ascii].Enqueue(converter.Action);

                    // Remember the source URL so the user can later reconvert
                    // straight from ksm.dev or export the mix as a manifest.
                    MixUrlStore.SetUrl(AssetManager.MixPath, header.Id, url);
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
                        AssetManager.Headers.Add(header);

                    // Bulk import already wrote the assets, save so the db matches what's on disk
                    ApplyCurrentSort();
                    if (converter.ResultSet.Length > 0)
                        Save(AssetManager.MdbFilename);

                    string skipped = converter.SkippedCount > 0 ? $", {converter.SkippedCount} skipped (already in this mix)" : string.Empty;
                    MessageBox.Show(
                        $"{converter.ResultSet.Length} song(s) imported{skipped}.",
                        "Bulk Import",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    WarnJacketLimit();
                }
            }
        }

        // From JSON: an Asphyxia curated list (songs with ksm.dev ids) or a mix manifest (charts with URLs)
        private void OnImportJsonMenuClick(object sender, EventArgs e)
        {
            if (!CanEdit)
                return;

            string path;
            using (var browser = new OpenFileDialog())
            {
                browser.Filter = "Song lists (*.json)|*.json|All Files|*.*";
                browser.Title  = "Add songs from a JSON list";
                if (browser.ShowDialog() != DialogResult.OK)
                    return;
                path = browser.FileName;
            }

            string text;
            try
            {
                text = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Add songs", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (text.Contains("\"songs\""))
                ImportAsphyxiaList(path);
            else if (text.Contains("\"charts\""))
                ImportMixManifest(path);
            else
                MessageBox.Show(
                    "This file is neither an Asphyxia song list nor a VoxCharger mix manifest.",
                    "Add songs",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
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
        private void ImportMixManifest(string manifestPath)
        {
            if (string.IsNullOrEmpty(AssetManager.MixPath))
                return;


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
                        AssetManager.Headers.Add(header);

                        // Re-key the URL by the music id BulkImporter chose. The music code is
                        // sanitized from the folder name, so match on the folder itself.
                        if (converter.SourceFolders.TryGetValue(header, out string folder) &&
                            asciiToUrl.TryGetValue(Path.GetFileName(folder), out string url))
                        {
                            MixUrlStore.SetUrl(AssetManager.MixPath, header.Id, url);
                        }

                        imported++;
                    }

                    // Bulk import already wrote the assets, save so the db matches what's on disk
                    ApplyCurrentSort();
                    if (imported > 0)
                        Save(AssetManager.MdbFilename);
                }

                ShowImportSummary("Import Mix", imported, errors);
            }
            finally
            {
                try { Directory.Delete(parentDir, true); } catch { }
            }
        }

        // Read an Asphyxia curated-list JSON, download every importable
        // chart's zip from its stored ksm.dev CDN URL, and import each
        // one into the currently-loaded mix preserving the music id the
        // server assigned. Use case is rebuilding a client mix without
        // having to round-trip through the Asphyxia server's filesystem
        // copy of music_db.merged.xml.
        private void ImportAsphyxiaList(string listPath)
        {
            if (string.IsNullOrEmpty(AssetManager.MixPath))
            {
                MessageBox.Show(
                    "Open or create a mix before importing an Asphyxia list.",
                    "Import Asphyxia list",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }


            AsphyxiaList.Bundle bundle;
            try
            {
                bundle = AsphyxiaList.LoadFile(listPath);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Failed to read list:\n" + ex.Message,
                    "Import Asphyxia list",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            if (bundle == null || bundle.songs == null || bundle.songs.Count == 0)
            {
                MessageBox.Show(
                    "List contains no songs.",
                    "Import Asphyxia list",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            var importable = bundle.songs.Where(AsphyxiaList.IsImportable).ToList();
            int skipped    = bundle.songs.Count - importable.Count;

            if (importable.Count == 0)
            {
                MessageBox.Show(
                    $"No importable songs in this list ({bundle.songs.Count} total; " +
                    "all skipped because they're not yet ready, have no music id, " +
                    "or have no download URL).",
                    "Import Asphyxia list",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return;
            }

            var confirm = MessageBox.Show(
                $"Found {importable.Count} importable chart(s) ({skipped} skipped).\n\n" +
                "VoxCharger will download each chart from ksm.dev and import it " +
                (AssetManager.MixStartId > 0
                    ? $"numbered consecutively from {AssetManager.MixStartId} on (the list's ids only set the order). "
                    : "with the server-assigned music id preserved. ") +
                "Existing entries at the same id will be replaced.\n\nProceed?",
                "Import Asphyxia list",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question
            );
            if (confirm != DialogResult.Yes) return;

            AsphyxiaImporter.Result result = null;
            using (var loader = new LoadingForm())
            {
                loader.SetAction(dialog =>
                {
                    var progress = new AsphyxiaImporter.Progress
                    {
                        Status  = s => dialog.SetStatus(s),
                        Percent = p => dialog.SetProgress(p),
                    };
                    try
                    {
                        result = AsphyxiaImporter.Run(importable, progress);
                    }
                    finally
                    {
                        dialog.Complete();
                    }
                });
                loader.ShowDialog();
            }

            if (result == null || result.Imported == 0)
            {
                ShowImportSummary("Import Asphyxia list", 0, result?.Errors ?? new List<string>());
                return;
            }

            try
            {
                Save(AssetManager.MdbFilename);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Charts were imported but saving music_db failed:\n" + ex.Message,
                    "Import Asphyxia list",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            // Headers were added directly to AssetManager.Headers in the
            // background thread; refresh the in-memory listbox to match.
            ApplyCurrentSort();
            _pristine = true;
            UpdateUi();

            ShowImportSummary("Import Asphyxia list", result.Imported, result.Errors);
            WarnJacketLimit();
        }

        // Song select shows the dummy jacket for ids above AssetManager.MaxJacketMusicId
        private void WarnJacketLimit()
        {
            int above = AssetManager.Headers.Count(h => h.Id > AssetManager.MaxJacketMusicId);
            if (above == 0)
                return;

            MessageBox.Show(
                $"{above} song(s) in this mix have an ID above {AssetManager.MaxJacketMusicId}. They play, but the game shows no jacket for them at song select " +
                "(its jacket loader stops at the tutorial ID 9001). Use Tools > Set starting ID to move the mix below that.",
                "Jacket limit",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
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

        private void RefreshReconvertControls()
        {
            bool hasUrl = CanEdit && SelectedSongHasReconvertUrl();
            ReconvertButton.Visible = hasUrl;
            _reconvertItem.Enabled  = hasUrl;
            _reconvertItem.Text     = hasUrl ? "Reconvert from ksm.dev" : "Reconvert from ksm.dev (no URL stored)";
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

                    MarkDirty();
                    AssetManager.Headers.Add(newHeader);
                    ApplyCurrentSort();

                    _actions[newHeader.Ascii].Enqueue(converter.Action);
                    MixUrlStore.SetUrl(AssetManager.MixPath, newHeader.Id, url);

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

        private void OnExplorerEditMenuClick(object sender, EventArgs e)
        {
            var header = MusicListBox.SelectedItem as VoxHeader;
            if (header == null)
                return;

            string path = AssetManager.GetMusicPath(header);
            Process.Start("explorer.exe", path);
        }

        private void OnMusicListBoxKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Delete && CanEdit)
            {
                OnRemoveButtonClick(sender, e);
                e.Handled = true;
            }
        }

        private void OnRemoveButtonClick(object sender, EventArgs e)
        {
            if (!CanEdit || !(MusicListBox.SelectedItem is VoxHeader header))
                return;

            var response = MessageBox.Show(
                $"Remove \"{VoxHeader.WithDecodedSymbols(header.Title)}\" from this mix? Its files are deleted when you save.",
                "Remove song",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (response != DialogResult.Yes)
                return;

            int index = MusicListBox.SelectedIndex;
            AssetManager.Headers.Remove(header);
            // Drop the sidecar URL too so a stale id doesn't survive into a future export
            MixUrlStore.RemoveUrl(AssetManager.MixPath, header.Id);

            // Pending changes of this song are moot, its assets are deleted
            _actions[header.Ascii] = new Queue<Action>();
            _actions[header.Ascii].Enqueue(() => AssetManager.DeleteAssets(header));

            ApplyCurrentSort();
            if (MusicListBox.Items.Count > 0)
                MusicListBox.SelectedIndex = Math.Min(index, MusicListBox.Items.Count - 1);

            MarkDirty();
            ShowSelection();
        }
        #endregion

        #region --- Song list ---
        // Refresh the list from the current view, applying the search filter and sort
        private void ApplyCurrentSort()
        {
            var headers = CurrentHeaders.Where(MatchesSearch);
            IEnumerable<VoxHeader> sorted;
            switch (_sortMode)
            {
                case SortMode.TitleDesc:
                    sorted = headers.OrderByDescending(h => VoxHeader.WithDecodedSymbols(h.Title) ?? "", StringComparer.OrdinalIgnoreCase);
                    break;
                case SortMode.IdAsc:
                    sorted = headers.OrderBy(h => h.Id);
                    break;
                case SortMode.IdDesc:
                    sorted = headers.OrderByDescending(h => h.Id);
                    break;
                default:
                    sorted = headers.OrderBy(h => VoxHeader.WithDecodedSymbols(h.Title) ?? "", StringComparer.OrdinalIgnoreCase);
                    break;
            }

            var keep = MusicListBox.SelectedItem;
            MusicListBox.BeginUpdate();
            MusicListBox.Items.Clear();
            MusicListBox.Items.AddRange(sorted.Cast<object>().ToArray());
            if (keep != null)
            {
                int idx = MusicListBox.Items.IndexOf(keep);
                if (idx >= 0) MusicListBox.SelectedIndex = idx;
            }
            MusicListBox.EndUpdate();

            if (HeaderPanel != null && _deleteMixItem != null)
                UpdateUi();
        }

        private void SetSortMode(SortMode mode, string label)
        {
            _sortMode = mode;
            SortButton.Text = label;
            ApplyCurrentSort();
        }

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
            _searchText = SearchBox.Input.Text != null ? SearchBox.Input.Text.Trim() : string.Empty;
            ApplyCurrentSort();
        }

        private void OnMusicListBoxSelectedIndexChanged(object sender, EventArgs e)
        {
            ShowSelection();
        }
        #endregion

        #region --- Song details ---
        private void ShowSelection()
        {
            var header = MusicListBox.SelectedItem as VoxHeader;
            bool show  = header != null;

            MetadataCard.Visible      = show;
            ChartsCard.Visible        = show;
            EmptyContentLabel.Visible = !show;
            if (!show)
            {
                _songJacket = null;
                return;
            }

            _songJacket = JacketCache.Get(JacketCache.SongJacketFile(header), 96);
            var firstLevel = header.Levels.Keys.OrderBy(d => d).Select(d => header.Levels[d]).FirstOrDefault(l => l.Jacket != null);
            if (firstLevel != null && !_pristine)
                _songJacket = JacketCache.Scale(firstLevel.Jacket, 96);

            TitleLabel.Text  = VoxHeader.WithDecodedSymbols(header.Title);
            ArtistLabel.Text = VoxHeader.WithDecodedSymbols(header.Artist);

            BpmTile.Value        = FormatBpm(header);
            VolumeTile.Value     = $"{header.Volume} %";
            BackgroundTile.Value = $"{header.BackgroundId:D2}";
            DateTile.Value       = header.DistributionDate.ToString("yyyy-MM-dd");

            int tag = AssetManager.MixTagId > 0 ? AssetManager.MixTagId : header.TagId;
            VersionTile.Value = VersionName(header.Version);
            TagTile.Value     = tag > 0 ? tag.ToString() : "none";
            MusicTile.Value   = AudioFile(header, false);
            PreviewTile.Value = AudioFile(header, true);

            EditMetadataButton.Enabled = CanEdit;
            ExplorerButton.Enabled     = Directory.Exists(AssetManager.GetMusicPath(header));

            foreach (var tile in Tiles)
                tile.Bind(header, ChartJacket(header, tile.Difficulty), CanEdit);

            RefreshReconvertControls();
            JacketBox.Invalidate();
        }

        // Jacket of one chart: a picked but unsaved image first, then the file in the music folder
        private Image ChartJacket(VoxHeader header, Difficulty difficulty)
        {
            if (!header.Levels.TryGetValue(difficulty, out var level))
                return null;

            if (level.Jacket != null)
                return JacketCache.Scale(level.Jacket, 60);

            return JacketCache.Get(JacketCache.JacketFile(header, difficulty), 60);
        }

        private static string FormatBpm(VoxHeader header)
        {
            string min = header.BpmMin.ToString("0.##");
            string max = header.BpmMax.ToString("0.##");
            return min == max ? min : $"{min} - {max}";
        }

        private static string VersionName(GameVersion version)
        {
            switch (version)
            {
                case GameVersion.Booth:             return "Booth";
                case GameVersion.InfiniteInfection: return "Infinite Infection";
                case GameVersion.GravityWars:       return "Gravity Wars";
                case GameVersion.HeavenlyHaven:     return "Heavenly Haven";
                case GameVersion.VividWave:         return "Vivid Wave";
                case GameVersion.ExceedGear:        return "Exceed Gear";
                default:                            return "Nabla";
            }
        }

        private static string AudioFile(VoxHeader header, bool preview)
        {
            try
            {
                foreach (var format in new[] { AudioFormat.S3V, AudioFormat.Iidx })
                {
                    string path = AssetManager.GetAudioPath(header, format, preview);
                    if (File.Exists(path))
                        return Path.GetFileName(path);
                }

                // Per-difficulty audio
                foreach (var diff in header.Levels.Keys)
                {
                    foreach (var format in new[] { AudioFormat.S3V, AudioFormat.Iidx })
                    {
                        string path = AssetManager.GetAudioPath(header, diff, format, preview);
                        if (File.Exists(path))
                            return Path.GetFileName(path) + " (per chart)";
                    }
                }
            }
            catch (Exception)
            {
            }

            return "missing";
        }

        private void OnEditMetadataButtonClick(object sender, EventArgs e)
        {
            if (!CanEdit || !(MusicListBox.SelectedItem is VoxHeader header))
                return;

            using (var editor = new MetadataEditorForm(header, true))
            {
                if (editor.ShowDialog(this) != DialogResult.OK)
                    return;

                if (editor.MusicId != header.Id)
                {
                    // Folder, files, thumbnails, URL sidecar and manifest follow the new id on save
                    int oldId    = header.Id;
                    int newId    = editor.MusicId;
                    string ascii = header.Ascii;
                    AssetManager.Headers.ChangeId(header, newId);
                    QueueAction("Renumber", () => AssetManager.RenameMusicAssets(oldId, newId, ascii));
                }

                foreach (var (file, preview) in new[] { (editor.MusicFile, false), (editor.PreviewFile, true) })
                {
                    if (string.IsNullOrEmpty(file) || !File.Exists(file))
                        continue;

                    string tmp = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}{Path.GetExtension(file)}");
                    File.Copy(file, tmp);

                    var importOptions = new AudioImportOptions
                    {
                        Format    = file.ToLower().EndsWith(".s3v") ? AudioFormat.S3V : AudioFormat.Iidx,
                        IsPreview = preview,
                    };
                    QueueAction(header.Ascii, () => AssetManager.ImportAudio(tmp, header, importOptions));
                }
            }

            ApplyCurrentSort();
            MarkDirty();
            ShowSelection();
        }

        private void OnChartEditClick(object sender, EventArgs e)
        {
            if (!CanEdit || !(MusicListBox.SelectedItem is VoxHeader header))
                return;

            var difficulty = ((ChartTile)sender).Difficulty;
            using (var editor = new LevelEditorForm(header, difficulty))
            {
                if (editor.ShowDialog(this) != DialogResult.OK)
                    return;

                // Update the levels in case it's newly added
                editor.Result.Difficulty  = difficulty;
                header.Levels[difficulty] = editor.Result;
                if (editor.Action != null)
                    QueueAction(header.Ascii, editor.Action);
            }

            MarkDirty();
            MusicListBox.Invalidate();
            ShowSelection();
        }

        private void OnChartRemoveClick(object sender, EventArgs e)
        {
            if (!CanEdit || !(MusicListBox.SelectedItem is VoxHeader header))
                return;

            var difficulty = ((ChartTile)sender).Difficulty;
            if (!header.Levels.ContainsKey(difficulty))
                return;

            if (header.Levels.Count == 1)
            {
                MessageBox.Show("A song needs at least one chart. Remove the song instead.", "Remove chart", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string name = Theme.DifficultyName(difficulty, header.InfVersion);
            var response = MessageBox.Show(
                $"Remove the {name} chart of \"{VoxHeader.WithDecodedSymbols(header.Title)}\"? Its files are deleted when you save.",
                "Remove chart",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning
            );

            if (response != DialogResult.Yes)
                return;

            // Paths now, a later INF version change would point them elsewhere
            string vox       = AssetManager.GetVoxPath(header, difficulty);
            string jacket    = AssetManager.GetJacketPath(header, difficulty);
            string thumbnail = AssetManager.GetThumbnailJacketPath(header, difficulty);
            QueueAction(header.Ascii, () => AssetManager.DeleteLevelAssets(header, difficulty, vox, jacket, thumbnail));
            header.Levels.Remove(difficulty);

            MarkDirty();
            MusicListBox.Invalidate();
            ShowSelection();
        }

        private void OnJacketPictureBoxClick(object sender, EventArgs e)
        {
            if (!(MusicListBox.SelectedItem is VoxHeader header) || header.Levels.Count == 0)
                return;

            var difficulty = header.Levels.Keys.Min();
            string jacket = $"{AssetManager.GetJacketPath(header, difficulty)}_b.png";
            if (!File.Exists(jacket))
                return;

            using (var image  = Image.FromFile(jacket))
            using (var viewer = new JacketViewerForm(image))
                viewer.ShowDialog(this);
        }
        #endregion

        #region --- Functions ---
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
                    if (dbFilename == AssetManager.MdbFilename)
                        AssetManager.SaveMixSettings();

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

            // Jackets written by the save replace the cached ones
            foreach (var header in AssetManager.Headers)
            {
                foreach (var level in header.Levels.Values)
                {
                    level.Jacket?.Dispose();
                    level.Jacket = null;
                }
            }

            JacketCache.Clear();
            MusicListBox.Invalidate();
            UpdateUi();
            ShowSelection();

            return errors.Count == 0;
        }
        #endregion
    }
}
