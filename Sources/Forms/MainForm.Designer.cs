namespace VoxCharger
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Builds the window: header bar, mix and song column on the left, song details on the right.
        /// Positions inside the columns are set by LayoutWindow.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(MainForm));

            this.HeaderPanel        = new System.Windows.Forms.Panel();
            this.SearchBox          = new VoxCharger.SearchBox();
            this.SaveButton         = new VoxCharger.FlatButton();
            this.ToolsButton        = new VoxCharger.FlatButton();
            this.AboutButton        = new VoxCharger.FlatButton();

            this.SidePanel          = new System.Windows.Forms.Panel();
            this.MixCaptionLabel    = new System.Windows.Forms.Label();
            this.MixButton          = new VoxCharger.FlatButton();
            this.ListHeaderPanel    = new System.Windows.Forms.Panel();
            this.ListCaptionLabel   = new System.Windows.Forms.Label();
            this.SortButton         = new VoxCharger.FlatButton();
            this.MusicListBox       = new VoxCharger.SongListBox();
            this.EmptyListLabel     = new System.Windows.Forms.Label();
            this.BottomPanel        = new System.Windows.Forms.Panel();
            this.SongsButton        = new VoxCharger.FlatButton();
            this.MixMenuButton      = new VoxCharger.FlatButton();

            this.ContentPanel       = new System.Windows.Forms.Panel();
            this.EmptyContentLabel  = new System.Windows.Forms.Label();
            this.GamePathButton     = new VoxCharger.FlatButton();
            this.MetadataCard       = new VoxCharger.CardPanel();
            this.JacketBox          = new System.Windows.Forms.PictureBox();
            this.ChipsLabel         = new System.Windows.Forms.Label();
            this.TitleLabel         = new System.Windows.Forms.Label();
            this.ArtistLabel        = new System.Windows.Forms.Label();
            this.ExplorerButton     = new VoxCharger.FlatButton();
            this.EditMetadataButton = new VoxCharger.FlatButton();
            this.BpmTile            = new VoxCharger.StatTile();
            this.VolumeTile         = new VoxCharger.StatTile();
            this.BackgroundTile     = new VoxCharger.StatTile();
            this.DateTile           = new VoxCharger.StatTile();
            this.FilesLabel         = new System.Windows.Forms.Label();
            this.ChartsCard         = new VoxCharger.CardPanel();
            this.ChartsTitleLabel   = new System.Windows.Forms.Label();
            this.ChartsSubLabel     = new System.Windows.Forms.Label();
            this.ReconvertButton    = new VoxCharger.FlatButton();
            this.NovTile            = new VoxCharger.ChartTile(VoxCharger.Difficulty.Novice);
            this.AdvTile            = new VoxCharger.ChartTile(VoxCharger.Difficulty.Advanced);
            this.ExhTile            = new VoxCharger.ChartTile(VoxCharger.Difficulty.Exhaust);
            this.InfTile            = new VoxCharger.ChartTile(VoxCharger.Difficulty.Infinite);

            ((System.ComponentModel.ISupportInitialize)(this.JacketBox)).BeginInit();
            this.HeaderPanel.SuspendLayout();
            this.SidePanel.SuspendLayout();
            this.ListHeaderPanel.SuspendLayout();
            this.BottomPanel.SuspendLayout();
            this.ContentPanel.SuspendLayout();
            this.MetadataCard.SuspendLayout();
            this.ChartsCard.SuspendLayout();
            this.SuspendLayout();
            //
            // HeaderPanel
            //
            this.HeaderPanel.BackColor = VoxCharger.Theme.Header;
            this.HeaderPanel.Controls.Add(this.SearchBox);
            this.HeaderPanel.Controls.Add(this.SaveButton);
            this.HeaderPanel.Controls.Add(this.ToolsButton);
            this.HeaderPanel.Controls.Add(this.AboutButton);
            this.HeaderPanel.Dock = System.Windows.Forms.DockStyle.Top;
            this.HeaderPanel.Height = 52;
            this.HeaderPanel.Name = "HeaderPanel";
            this.HeaderPanel.Paint += new System.Windows.Forms.PaintEventHandler(this.OnHeaderPanelPaint);
            //
            // SearchBox
            //
            this.SearchBox.Name = "SearchBox";
            this.SearchBox.Size = new System.Drawing.Size(420, 32);
            this.SearchBox.Input.TextChanged += new System.EventHandler(this.OnSearchTextBoxChanged);
            //
            // SaveButton
            //
            this.SaveButton.Enabled = false;
            this.SaveButton.Glyph = VoxCharger.ButtonGlyph.Save;
            this.SaveButton.Kind = VoxCharger.ButtonKind.Primary;
            this.SaveButton.Name = "SaveButton";
            this.SaveButton.Radius = 6;
            this.SaveButton.Size = new System.Drawing.Size(84, 32);
            this.SaveButton.Text = "Save";
            this.SaveButton.Click += new System.EventHandler(this.OnSaveButtonClick);
            //
            // ToolsButton
            //
            this.ToolsButton.BoldText = false;
            this.ToolsButton.Chevron = true;
            this.ToolsButton.Kind = VoxCharger.ButtonKind.Ghost;
            this.ToolsButton.Name = "ToolsButton";
            this.ToolsButton.Radius = 6;
            this.ToolsButton.Size = new System.Drawing.Size(76, 32);
            this.ToolsButton.Text = "Tools";
            this.ToolsButton.Click += new System.EventHandler(this.OnToolsButtonClick);
            //
            // AboutButton
            //
            this.AboutButton.BoldText = false;
            this.AboutButton.Kind = VoxCharger.ButtonKind.Ghost;
            this.AboutButton.Name = "AboutButton";
            this.AboutButton.Radius = 6;
            this.AboutButton.Size = new System.Drawing.Size(64, 32);
            this.AboutButton.Text = "About";
            this.AboutButton.Click += new System.EventHandler(this.OnAboutHelpMenuClick);
            //
            // SidePanel
            //
            this.SidePanel.BackColor = VoxCharger.Theme.Sidebar;
            this.SidePanel.Controls.Add(this.MixCaptionLabel);
            this.SidePanel.Controls.Add(this.MixButton);
            this.SidePanel.Controls.Add(this.ListHeaderPanel);
            this.SidePanel.Controls.Add(this.EmptyListLabel);
            this.SidePanel.Controls.Add(this.MusicListBox);
            this.SidePanel.Controls.Add(this.BottomPanel);
            this.SidePanel.Dock = System.Windows.Forms.DockStyle.Left;
            this.SidePanel.Name = "SidePanel";
            this.SidePanel.Width = 392;
            this.SidePanel.Paint += new System.Windows.Forms.PaintEventHandler(this.OnSidePanelPaint);
            //
            // MixCaptionLabel
            //
            this.MixCaptionLabel.AutoSize = true;
            this.MixCaptionLabel.Font = VoxCharger.Theme.Caption;
            this.MixCaptionLabel.ForeColor = VoxCharger.Theme.TextDim;
            this.MixCaptionLabel.Location = new System.Drawing.Point(16, 16);
            this.MixCaptionLabel.Name = "MixCaptionLabel";
            this.MixCaptionLabel.Text = "MIX";
            //
            // MixButton
            //
            this.MixButton.AlignLeft = true;
            this.MixButton.Chevron = true;
            this.MixButton.Glyph = VoxCharger.ButtonGlyph.Layers;
            this.MixButton.Name = "MixButton";
            this.MixButton.Text = "No game folder";
            this.MixButton.Click += new System.EventHandler(this.OnMixButtonClick);
            //
            // ListHeaderPanel
            //
            this.ListHeaderPanel.BackColor = VoxCharger.Theme.Strip;
            this.ListHeaderPanel.Controls.Add(this.ListCaptionLabel);
            this.ListHeaderPanel.Controls.Add(this.SortButton);
            this.ListHeaderPanel.Name = "ListHeaderPanel";
            this.ListHeaderPanel.Paint += new System.Windows.Forms.PaintEventHandler(this.OnStripPaint);
            //
            // ListCaptionLabel
            //
            this.ListCaptionLabel.AutoSize = true;
            this.ListCaptionLabel.Font = VoxCharger.Theme.Small;
            this.ListCaptionLabel.ForeColor = VoxCharger.Theme.TextDim;
            this.ListCaptionLabel.Location = new System.Drawing.Point(16, 10);
            this.ListCaptionLabel.Name = "ListCaptionLabel";
            this.ListCaptionLabel.Text = "Songs";
            //
            // SortButton
            //
            this.SortButton.BoldText = false;
            this.SortButton.Chevron = true;
            this.SortButton.Font = VoxCharger.Theme.Small;
            this.SortButton.Kind = VoxCharger.ButtonKind.Ghost;
            this.SortButton.Name = "SortButton";
            this.SortButton.Radius = 6;
            this.SortButton.Size = new System.Drawing.Size(116, 26);
            this.SortButton.Text = "Title A to Z";
            this.SortButton.Click += new System.EventHandler(this.OnSortButtonClick);
            //
            // MusicListBox
            //
            this.MusicListBox.Name = "MusicListBox";
            this.MusicListBox.SelectedIndexChanged += new System.EventHandler(this.OnMusicListBoxSelectedIndexChanged);
            this.MusicListBox.KeyDown += new System.Windows.Forms.KeyEventHandler(this.OnMusicListBoxKeyDown);
            //
            // EmptyListLabel
            //
            this.EmptyListLabel.BackColor = VoxCharger.Theme.Sidebar;
            this.EmptyListLabel.Font = VoxCharger.Theme.Body;
            this.EmptyListLabel.ForeColor = VoxCharger.Theme.TextDim;
            this.EmptyListLabel.Name = "EmptyListLabel";
            this.EmptyListLabel.Padding = new System.Windows.Forms.Padding(24);
            this.EmptyListLabel.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            this.EmptyListLabel.Visible = false;
            //
            // BottomPanel
            //
            this.BottomPanel.BackColor = VoxCharger.Theme.Strip;
            this.BottomPanel.Controls.Add(this.SongsButton);
            this.BottomPanel.Controls.Add(this.MixMenuButton);
            this.BottomPanel.Name = "BottomPanel";
            this.BottomPanel.Paint += new System.Windows.Forms.PaintEventHandler(this.OnStripPaint);
            //
            // SongsButton
            //
            this.SongsButton.Chevron = true;
            this.SongsButton.ChevronUp = true;
            this.SongsButton.ChevronAtEdge = true;
            this.SongsButton.Glyph = VoxCharger.ButtonGlyph.Plus;
            this.SongsButton.Name = "SongsButton";
            this.SongsButton.Text = "Songs";
            this.SongsButton.Click += new System.EventHandler(this.OnSongsButtonClick);
            //
            // MixMenuButton
            //
            this.MixMenuButton.Chevron = true;
            this.MixMenuButton.ChevronUp = true;
            this.MixMenuButton.ChevronAtEdge = true;
            this.MixMenuButton.Name = "MixMenuButton";
            this.MixMenuButton.Text = "Mix";
            this.MixMenuButton.Click += new System.EventHandler(this.OnMixMenuButtonClick);
            //
            // ContentPanel
            //
            this.ContentPanel.BackColor = VoxCharger.Theme.Window;
            this.ContentPanel.Controls.Add(this.EmptyContentLabel);
            this.ContentPanel.Controls.Add(this.GamePathButton);
            this.ContentPanel.Controls.Add(this.MetadataCard);
            this.ContentPanel.Controls.Add(this.ChartsCard);
            this.ContentPanel.Dock = System.Windows.Forms.DockStyle.Fill;
            this.ContentPanel.Name = "ContentPanel";
            //
            // EmptyContentLabel
            //
            this.EmptyContentLabel.Font = VoxCharger.Theme.Subtitle;
            this.EmptyContentLabel.ForeColor = VoxCharger.Theme.TextDim;
            this.EmptyContentLabel.Name = "EmptyContentLabel";
            this.EmptyContentLabel.Text = "Select a song from the list";
            this.EmptyContentLabel.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            //
            // GamePathButton
            //
            this.GamePathButton.Glyph = VoxCharger.ButtonGlyph.Folder;
            this.GamePathButton.Kind = VoxCharger.ButtonKind.Primary;
            this.GamePathButton.Name = "GamePathButton";
            this.GamePathButton.Size = new System.Drawing.Size(220, 40);
            this.GamePathButton.Text = "Choose game folder…";
            this.GamePathButton.Visible = false;
            this.GamePathButton.Click += new System.EventHandler(this.OnGamePathButtonClick);
            //
            // MetadataCard
            //
            this.MetadataCard.Controls.Add(this.JacketBox);
            this.MetadataCard.Controls.Add(this.ChipsLabel);
            this.MetadataCard.Controls.Add(this.TitleLabel);
            this.MetadataCard.Controls.Add(this.ArtistLabel);
            this.MetadataCard.Controls.Add(this.ExplorerButton);
            this.MetadataCard.Controls.Add(this.EditMetadataButton);
            this.MetadataCard.Controls.Add(this.BpmTile);
            this.MetadataCard.Controls.Add(this.VolumeTile);
            this.MetadataCard.Controls.Add(this.BackgroundTile);
            this.MetadataCard.Controls.Add(this.DateTile);
            this.MetadataCard.Controls.Add(this.FilesLabel);
            this.MetadataCard.Name = "MetadataCard";
            this.MetadataCard.Visible = false;
            //
            // JacketBox
            //
            this.JacketBox.BackColor = VoxCharger.Theme.Card;
            this.JacketBox.Cursor = System.Windows.Forms.Cursors.Hand;
            this.JacketBox.Name = "JacketBox";
            this.JacketBox.Size = new System.Drawing.Size(88, 88);
            this.JacketBox.TabStop = false;
            this.JacketBox.Paint += new System.Windows.Forms.PaintEventHandler(this.OnJacketBoxPaint);
            this.JacketBox.Click += new System.EventHandler(this.OnJacketPictureBoxClick);
            //
            // ChipsLabel
            //
            this.ChipsLabel.BackColor = VoxCharger.Theme.Card;
            this.ChipsLabel.Name = "ChipsLabel";
            this.ChipsLabel.Paint += new System.Windows.Forms.PaintEventHandler(this.OnChipsLabelPaint);
            //
            // TitleLabel
            //
            this.TitleLabel.AutoEllipsis = true;
            this.TitleLabel.BackColor = VoxCharger.Theme.Card;
            this.TitleLabel.Font = VoxCharger.Theme.Title;
            this.TitleLabel.ForeColor = VoxCharger.Theme.Text;
            this.TitleLabel.Name = "TitleLabel";
            this.TitleLabel.UseMnemonic = false;
            //
            // ArtistLabel
            //
            this.ArtistLabel.AutoEllipsis = true;
            this.ArtistLabel.BackColor = VoxCharger.Theme.Card;
            this.ArtistLabel.Font = VoxCharger.Theme.Subtitle;
            this.ArtistLabel.ForeColor = VoxCharger.Theme.TextMuted;
            this.ArtistLabel.Name = "ArtistLabel";
            this.ArtistLabel.UseMnemonic = false;
            //
            // ExplorerButton
            //
            this.ExplorerButton.BoldText = false;
            this.ExplorerButton.Name = "ExplorerButton";
            this.ExplorerButton.Size = new System.Drawing.Size(140, 36);
            this.ExplorerButton.Text = "Show in Explorer";
            this.ExplorerButton.Click += new System.EventHandler(this.OnExplorerEditMenuClick);
            //
            // EditMetadataButton
            //
            this.EditMetadataButton.Kind = VoxCharger.ButtonKind.Primary;
            this.EditMetadataButton.Name = "EditMetadataButton";
            this.EditMetadataButton.Size = new System.Drawing.Size(128, 36);
            this.EditMetadataButton.Text = "Edit metadata";
            this.EditMetadataButton.Click += new System.EventHandler(this.OnEditMetadataButtonClick);
            //
            // Stat tiles
            //
            this.BpmTile.Caption = "BPM";
            this.BpmTile.Name = "BpmTile";
            this.VolumeTile.Caption = "VOLUME";
            this.VolumeTile.Name = "VolumeTile";
            this.BackgroundTile.Caption = "BACKGROUND";
            this.BackgroundTile.Name = "BackgroundTile";
            this.DateTile.Caption = "RELEASED";
            this.DateTile.Name = "DateTile";
            //
            // FilesLabel
            //
            this.FilesLabel.AutoEllipsis = true;
            this.FilesLabel.BackColor = VoxCharger.Theme.Card;
            this.FilesLabel.Font = VoxCharger.Theme.Small;
            this.FilesLabel.ForeColor = VoxCharger.Theme.TextDim;
            this.FilesLabel.Name = "FilesLabel";
            this.FilesLabel.UseMnemonic = false;
            //
            // ChartsCard
            //
            this.ChartsCard.Controls.Add(this.ChartsTitleLabel);
            this.ChartsCard.Controls.Add(this.ChartsSubLabel);
            this.ChartsCard.Controls.Add(this.ReconvertButton);
            this.ChartsCard.Controls.Add(this.NovTile);
            this.ChartsCard.Controls.Add(this.AdvTile);
            this.ChartsCard.Controls.Add(this.ExhTile);
            this.ChartsCard.Controls.Add(this.InfTile);
            this.ChartsCard.Name = "ChartsCard";
            this.ChartsCard.Visible = false;
            //
            // ChartsTitleLabel
            //
            this.ChartsTitleLabel.AutoSize = true;
            this.ChartsTitleLabel.BackColor = VoxCharger.Theme.Card;
            this.ChartsTitleLabel.Font = VoxCharger.Theme.CardTitle;
            this.ChartsTitleLabel.ForeColor = VoxCharger.Theme.Text;
            this.ChartsTitleLabel.Location = new System.Drawing.Point(20, 18);
            this.ChartsTitleLabel.Name = "ChartsTitleLabel";
            this.ChartsTitleLabel.Text = "Charts";
            //
            // ChartsSubLabel
            //
            this.ChartsSubLabel.AutoSize = true;
            this.ChartsSubLabel.BackColor = VoxCharger.Theme.Card;
            this.ChartsSubLabel.Font = VoxCharger.Theme.Small;
            this.ChartsSubLabel.ForeColor = VoxCharger.Theme.TextDim;
            this.ChartsSubLabel.Location = new System.Drawing.Point(21, 42);
            this.ChartsSubLabel.Name = "ChartsSubLabel";
            //
            // ReconvertButton
            //
            this.ReconvertButton.BoldText = false;
            this.ReconvertButton.Name = "ReconvertButton";
            this.ReconvertButton.Size = new System.Drawing.Size(196, 34);
            this.ReconvertButton.Text = "Reconvert from ksm.dev";
            this.ReconvertButton.Visible = false;
            this.ReconvertButton.Click += new System.EventHandler(this.OnReconvertFromUrlClick);
            //
            // Chart tiles
            //
            this.NovTile.Name = "NovTile";
            this.AdvTile.Name = "AdvTile";
            this.ExhTile.Name = "ExhTile";
            this.InfTile.Name = "InfTile";
            //
            // MainForm
            //
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.None;
            this.BackColor = VoxCharger.Theme.Window;
            this.ClientSize = new System.Drawing.Size(1240, 800);
            this.Controls.Add(this.ContentPanel);
            this.Controls.Add(this.SidePanel);
            this.Controls.Add(this.HeaderPanel);
            this.Font = VoxCharger.Theme.Body;
            this.ForeColor = VoxCharger.Theme.Text;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MinimumSize = new System.Drawing.Size(1040, 720);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "VoxCharger";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.OnMainFormFormClosing);
            this.Load += new System.EventHandler(this.OnMainFormLoad);
            ((System.ComponentModel.ISupportInitialize)(this.JacketBox)).EndInit();
            this.HeaderPanel.ResumeLayout(false);
            this.HeaderPanel.PerformLayout();
            this.SidePanel.ResumeLayout(false);
            this.SidePanel.PerformLayout();
            this.ListHeaderPanel.ResumeLayout(false);
            this.ListHeaderPanel.PerformLayout();
            this.BottomPanel.ResumeLayout(false);
            this.ContentPanel.ResumeLayout(false);
            this.MetadataCard.ResumeLayout(false);
            this.ChartsCard.ResumeLayout(false);
            this.ChartsCard.PerformLayout();
            this.ResumeLayout(false);
        }

        #endregion

        private System.Windows.Forms.Panel HeaderPanel;
        private VoxCharger.SearchBox SearchBox;
        private VoxCharger.FlatButton SaveButton;
        private VoxCharger.FlatButton ToolsButton;
        private VoxCharger.FlatButton AboutButton;

        private System.Windows.Forms.Panel SidePanel;
        private System.Windows.Forms.Label MixCaptionLabel;
        private VoxCharger.FlatButton MixButton;
        private System.Windows.Forms.Panel ListHeaderPanel;
        private System.Windows.Forms.Label ListCaptionLabel;
        private VoxCharger.FlatButton SortButton;
        private VoxCharger.SongListBox MusicListBox;
        private System.Windows.Forms.Label EmptyListLabel;
        private System.Windows.Forms.Panel BottomPanel;
        private VoxCharger.FlatButton SongsButton;
        private VoxCharger.FlatButton MixMenuButton;

        private System.Windows.Forms.Panel ContentPanel;
        private System.Windows.Forms.Label EmptyContentLabel;
        private VoxCharger.FlatButton GamePathButton;
        private VoxCharger.CardPanel MetadataCard;
        private System.Windows.Forms.PictureBox JacketBox;
        private System.Windows.Forms.Label ChipsLabel;
        private System.Windows.Forms.Label TitleLabel;
        private System.Windows.Forms.Label ArtistLabel;
        private VoxCharger.FlatButton ExplorerButton;
        private VoxCharger.FlatButton EditMetadataButton;
        private VoxCharger.StatTile BpmTile;
        private VoxCharger.StatTile VolumeTile;
        private VoxCharger.StatTile BackgroundTile;
        private VoxCharger.StatTile DateTile;
        private System.Windows.Forms.Label FilesLabel;
        private VoxCharger.CardPanel ChartsCard;
        private System.Windows.Forms.Label ChartsTitleLabel;
        private System.Windows.Forms.Label ChartsSubLabel;
        private VoxCharger.FlatButton ReconvertButton;
        private VoxCharger.ChartTile NovTile;
        private VoxCharger.ChartTile AdvTile;
        private VoxCharger.ChartTile ExhTile;
        private VoxCharger.ChartTile InfTile;
    }
}
