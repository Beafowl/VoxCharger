namespace VoxCharger
{
    partial class RemoteMixForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.ServerUrlLabel = new System.Windows.Forms.Label();
            this.ServerUrlTextBox = new System.Windows.Forms.TextBox();
            this.ConnectButton = new System.Windows.Forms.Button();
            this.MixesLabel = new System.Windows.Forms.Label();
            this.MixListBox = new System.Windows.Forms.ListBox();
            this.CreateMixButton = new System.Windows.Forms.Button();
            this.DeleteMixButton = new System.Windows.Forms.Button();
            this.SongsLabel = new System.Windows.Forms.Label();
            this.SongListBox = new System.Windows.Forms.ListBox();
            this.AddSongButton = new System.Windows.Forms.Button();
            this.RemoveSongButton = new System.Windows.Forms.Button();
            this.SyncButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // ServerUrlLabel
            //
            this.ServerUrlLabel.AutoSize = true;
            this.ServerUrlLabel.Location = new System.Drawing.Point(12, 15);
            this.ServerUrlLabel.Name = "ServerUrlLabel";
            this.ServerUrlLabel.Size = new System.Drawing.Size(63, 13);
            this.ServerUrlLabel.TabIndex = 0;
            this.ServerUrlLabel.Text = "Server URL:";
            //
            // ServerUrlTextBox
            //
            this.ServerUrlTextBox.Location = new System.Drawing.Point(81, 12);
            this.ServerUrlTextBox.Name = "ServerUrlTextBox";
            this.ServerUrlTextBox.Size = new System.Drawing.Size(400, 20);
            this.ServerUrlTextBox.TabIndex = 1;
            //
            // ConnectButton
            //
            this.ConnectButton.Location = new System.Drawing.Point(487, 10);
            this.ConnectButton.Name = "ConnectButton";
            this.ConnectButton.Size = new System.Drawing.Size(75, 23);
            this.ConnectButton.TabIndex = 2;
            this.ConnectButton.Text = "Connect";
            this.ConnectButton.UseVisualStyleBackColor = true;
            this.ConnectButton.Click += new System.EventHandler(this.OnConnectButtonClick);
            //
            // MixesLabel
            //
            this.MixesLabel.AutoSize = true;
            this.MixesLabel.Location = new System.Drawing.Point(12, 42);
            this.MixesLabel.Name = "MixesLabel";
            this.MixesLabel.Size = new System.Drawing.Size(38, 13);
            this.MixesLabel.TabIndex = 3;
            this.MixesLabel.Text = "Mixes:";
            //
            // MixListBox
            //
            this.MixListBox.FormattingEnabled = true;
            this.MixListBox.Location = new System.Drawing.Point(12, 58);
            this.MixListBox.Name = "MixListBox";
            this.MixListBox.Size = new System.Drawing.Size(220, 264);
            this.MixListBox.TabIndex = 4;
            this.MixListBox.SelectedIndexChanged += new System.EventHandler(this.OnMixListBoxSelectedIndexChanged);
            //
            // CreateMixButton
            //
            this.CreateMixButton.Enabled = false;
            this.CreateMixButton.Location = new System.Drawing.Point(12, 328);
            this.CreateMixButton.Name = "CreateMixButton";
            this.CreateMixButton.Size = new System.Drawing.Size(107, 25);
            this.CreateMixButton.TabIndex = 5;
            this.CreateMixButton.Text = "Create Mix";
            this.CreateMixButton.UseVisualStyleBackColor = true;
            this.CreateMixButton.Click += new System.EventHandler(this.OnCreateMixButtonClick);
            //
            // DeleteMixButton
            //
            this.DeleteMixButton.Enabled = false;
            this.DeleteMixButton.Location = new System.Drawing.Point(125, 328);
            this.DeleteMixButton.Name = "DeleteMixButton";
            this.DeleteMixButton.Size = new System.Drawing.Size(107, 25);
            this.DeleteMixButton.TabIndex = 6;
            this.DeleteMixButton.Text = "Delete Mix";
            this.DeleteMixButton.UseVisualStyleBackColor = true;
            this.DeleteMixButton.Click += new System.EventHandler(this.OnDeleteMixButtonClick);
            //
            // SongsLabel
            //
            this.SongsLabel.AutoSize = true;
            this.SongsLabel.Location = new System.Drawing.Point(245, 42);
            this.SongsLabel.Name = "SongsLabel";
            this.SongsLabel.Size = new System.Drawing.Size(40, 13);
            this.SongsLabel.TabIndex = 7;
            this.SongsLabel.Text = "Songs:";
            //
            // SongListBox
            //
            this.SongListBox.FormattingEnabled = true;
            this.SongListBox.Location = new System.Drawing.Point(248, 58);
            this.SongListBox.Name = "SongListBox";
            this.SongListBox.Size = new System.Drawing.Size(314, 264);
            this.SongListBox.TabIndex = 8;
            //
            // AddSongButton
            //
            this.AddSongButton.Enabled = false;
            this.AddSongButton.Location = new System.Drawing.Point(248, 328);
            this.AddSongButton.Name = "AddSongButton";
            this.AddSongButton.Size = new System.Drawing.Size(100, 25);
            this.AddSongButton.TabIndex = 9;
            this.AddSongButton.Text = "Add Song";
            this.AddSongButton.UseVisualStyleBackColor = true;
            this.AddSongButton.Click += new System.EventHandler(this.OnAddSongButtonClick);
            //
            // RemoveSongButton
            //
            this.RemoveSongButton.Enabled = false;
            this.RemoveSongButton.Location = new System.Drawing.Point(354, 328);
            this.RemoveSongButton.Name = "RemoveSongButton";
            this.RemoveSongButton.Size = new System.Drawing.Size(100, 25);
            this.RemoveSongButton.TabIndex = 10;
            this.RemoveSongButton.Text = "Remove Song";
            this.RemoveSongButton.UseVisualStyleBackColor = true;
            this.RemoveSongButton.Click += new System.EventHandler(this.OnRemoveSongButtonClick);
            //
            // SyncButton
            //
            this.SyncButton.Enabled = false;
            this.SyncButton.Location = new System.Drawing.Point(462, 328);
            this.SyncButton.Name = "SyncButton";
            this.SyncButton.Size = new System.Drawing.Size(100, 25);
            this.SyncButton.TabIndex = 11;
            this.SyncButton.Text = "Sync to Local";
            this.SyncButton.UseVisualStyleBackColor = true;
            this.SyncButton.Click += new System.EventHandler(this.OnSyncButtonClick);
            //
            // RemoteMixForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(574, 365);
            this.Controls.Add(this.SyncButton);
            this.Controls.Add(this.RemoveSongButton);
            this.Controls.Add(this.AddSongButton);
            this.Controls.Add(this.SongListBox);
            this.Controls.Add(this.SongsLabel);
            this.Controls.Add(this.DeleteMixButton);
            this.Controls.Add(this.CreateMixButton);
            this.Controls.Add(this.MixListBox);
            this.Controls.Add(this.MixesLabel);
            this.Controls.Add(this.ConnectButton);
            this.Controls.Add(this.ServerUrlTextBox);
            this.Controls.Add(this.ServerUrlLabel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "RemoteMixForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Remote Mixes";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label ServerUrlLabel;
        private System.Windows.Forms.TextBox ServerUrlTextBox;
        private System.Windows.Forms.Button ConnectButton;
        private System.Windows.Forms.Label MixesLabel;
        private System.Windows.Forms.ListBox MixListBox;
        private System.Windows.Forms.Button CreateMixButton;
        private System.Windows.Forms.Button DeleteMixButton;
        private System.Windows.Forms.Label SongsLabel;
        private System.Windows.Forms.ListBox SongListBox;
        private System.Windows.Forms.Button AddSongButton;
        private System.Windows.Forms.Button RemoveSongButton;
        private System.Windows.Forms.Button SyncButton;
    }
}
