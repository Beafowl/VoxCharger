namespace VoxCharger
{
    partial class InputForm
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
            this.PromptLabel = new System.Windows.Forms.Label();
            this.InputTextBox = new System.Windows.Forms.TextBox();
            this.OkButton = new System.Windows.Forms.Button();
            this.CancelInputButton = new System.Windows.Forms.Button();
            this.SuspendLayout();
            //
            // PromptLabel
            //
            this.PromptLabel.AutoSize = true;
            this.PromptLabel.Location = new System.Drawing.Point(12, 14);
            this.PromptLabel.Name = "PromptLabel";
            this.PromptLabel.Size = new System.Drawing.Size(40, 13);
            this.PromptLabel.TabIndex = 0;
            this.PromptLabel.Text = "Prompt";
            //
            // InputTextBox
            //
            this.InputTextBox.Location = new System.Drawing.Point(12, 34);
            this.InputTextBox.Name = "InputTextBox";
            this.InputTextBox.Size = new System.Drawing.Size(310, 20);
            this.InputTextBox.TabIndex = 1;
            //
            // OkButton
            //
            this.OkButton.Location = new System.Drawing.Point(166, 62);
            this.OkButton.Name = "OkButton";
            this.OkButton.Size = new System.Drawing.Size(75, 25);
            this.OkButton.TabIndex = 2;
            this.OkButton.Text = "OK";
            this.OkButton.UseVisualStyleBackColor = true;
            this.OkButton.Click += new System.EventHandler(this.OnOkButtonClick);
            //
            // CancelInputButton
            //
            this.CancelInputButton.Location = new System.Drawing.Point(247, 62);
            this.CancelInputButton.Name = "CancelInputButton";
            this.CancelInputButton.Size = new System.Drawing.Size(75, 25);
            this.CancelInputButton.TabIndex = 3;
            this.CancelInputButton.Text = "Cancel";
            this.CancelInputButton.UseVisualStyleBackColor = true;
            this.CancelInputButton.Click += new System.EventHandler(this.OnCancelButtonClick);
            //
            // InputForm
            //
            this.AcceptButton = this.OkButton;
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.CancelButton = this.CancelInputButton;
            this.ClientSize = new System.Drawing.Size(334, 96);
            this.Controls.Add(this.CancelInputButton);
            this.Controls.Add(this.OkButton);
            this.Controls.Add(this.InputTextBox);
            this.Controls.Add(this.PromptLabel);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.Name = "InputForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Input";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Label PromptLabel;
        private System.Windows.Forms.TextBox InputTextBox;
        private System.Windows.Forms.Button OkButton;
        private System.Windows.Forms.Button CancelInputButton;
    }
}
