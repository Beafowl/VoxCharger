namespace VoxCharger
{
    partial class CreateRemoteMixForm
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
            this.NameLabel = new System.Windows.Forms.Label();
            this.NameTextBox = new System.Windows.Forms.TextBox();
            this.MusicIdStartLabel = new System.Windows.Forms.Label();
            this.MusicIdStartNumeric = new System.Windows.Forms.NumericUpDown();
            this.OkButton = new System.Windows.Forms.Button();
            this.CancelFormButton = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.MusicIdStartNumeric)).BeginInit();
            this.SuspendLayout();
            //
            // NameLabel
            //
            this.NameLabel.AutoSize = true;
            this.NameLabel.Location = new System.Drawing.Point(12, 15);
            this.NameLabel.Name = "NameLabel";
            this.NameLabel.Size = new System.Drawing.Size(57, 13);
            this.NameLabel.TabIndex = 0;
            this.NameLabel.Text = "Mix name:";
            //
            // NameTextBox
            //
            this.NameTextBox.Location = new System.Drawing.Point(110, 12);
            this.NameTextBox.Name = "NameTextBox";
            this.NameTextBox.Size = new System.Drawing.Size(212, 20);
            this.NameTextBox.TabIndex = 1;
            //
            // MusicIdStartLabel
            //
            this.MusicIdStartLabel.AutoSize = true;
            this.MusicIdStartLabel.Location = new System.Drawing.Point(12, 43);
            this.MusicIdStartLabel.Name = "MusicIdStartLabel";
            this.MusicIdStartLabel.Size = new System.Drawing.Size(82, 13);
            this.MusicIdStartLabel.TabIndex = 2;
            this.MusicIdStartLabel.Text = "Starting music ID:";
            //
            // MusicIdStartNumeric
            //
            this.MusicIdStartNumeric.Location = new System.Drawing.Point(110, 41);
            this.MusicIdStartNumeric.Maximum = new decimal(new int[] { 999999, 0, 0, 0 });
            this.MusicIdStartNumeric.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
            this.MusicIdStartNumeric.Name = "MusicIdStartNumeric";
            this.MusicIdStartNumeric.Size = new System.Drawing.Size(212, 20);
            this.MusicIdStartNumeric.TabIndex = 3;
            this.MusicIdStartNumeric.Value = new decimal(new int[] { 2000, 0, 0, 0 });
            //
            // OkButton
            //
            this.OkButton.Location = new System.Drawing.Point(166, 72);
            this.OkButton.Name = "OkButton";
            this.OkButton.Size = new System.Drawing.Size(75, 25);
            this.OkButton.TabIndex = 4;
            this.OkButton.Text = "OK";
            this.OkButton.UseVisualStyleBackColor = true;
            this.OkButton.Click += new System.EventHandler(this.OnOkButtonClick);
            //
            // CancelFormButton
            //
            this.CancelFormButton.Location = new System.Drawing.Point(247, 72);
            this.CancelFormButton.Name = "CancelFormButton";
            this.CancelFormButton.Size = new System.Drawing.Size(75, 25);
            this.CancelFormButton.TabIndex = 5;
            this.CancelFormButton.Text = "Cancel";
            this.CancelFormButton.UseVisualStyleBackColor = true;
            this.CancelFormButton.Click += new System.EventHandler(this.OnCancelButtonClick);
            //
            // CreateRemoteMixForm
            //
            this.AcceptButton = this.OkButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelFormButton;
            this.ClientSize = new System.Drawing.Size(334, 108);
            this.Controls.Add(this.CancelFormButton);
            this.Controls.Add(this.OkButton);
            this.Controls.Add(this.MusicIdStartNumeric);
            this.Controls.Add(this.MusicIdStartLabel);
            this.Controls.Add(this.NameTextBox);
            this.Controls.Add(this.NameLabel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "CreateRemoteMixForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Create Remote Mix";
            ((System.ComponentModel.ISupportInitialize)(this.MusicIdStartNumeric)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label NameLabel;
        private System.Windows.Forms.TextBox NameTextBox;
        private System.Windows.Forms.Label MusicIdStartLabel;
        private System.Windows.Forms.NumericUpDown MusicIdStartNumeric;
        private System.Windows.Forms.Button OkButton;
        private System.Windows.Forms.Button CancelFormButton;
    }
}
