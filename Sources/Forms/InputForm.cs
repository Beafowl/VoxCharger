using System;
using System.Windows.Forms;

namespace VoxCharger
{
    public partial class InputForm : Form
    {
        public string Value => InputTextBox.Text.Trim();

        public InputForm(string title, string prompt, string value = "")
        {
            InitializeComponent();
            Theme.Attach(this);
            Text = title;
            PromptLabel.Text = prompt;
            InputTextBox.Text = value;
            LayoutForPrompt();
        }

        // Long prompts wrap; the box and buttons move below the text
        private void LayoutForPrompt()
        {
            const int margin = 12;
            ClientSize = new System.Drawing.Size(420, ClientSize.Height);
            int width = ClientSize.Width - margin * 2;

            PromptLabel.AutoSize    = true;
            PromptLabel.MaximumSize = new System.Drawing.Size(width, 0);
            PromptLabel.Location    = new System.Drawing.Point(margin, 14);
            int promptHeight = PromptLabel.GetPreferredSize(new System.Drawing.Size(width, 0)).Height;

            InputTextBox.SetBounds(margin, PromptLabel.Top + promptHeight + 8, width, InputTextBox.Height);
            int buttonsTop = InputTextBox.Bottom + 12;
            ClientSize = new System.Drawing.Size(ClientSize.Width, buttonsTop + CancelInputButton.Height + margin);

            CancelInputButton.Location = new System.Drawing.Point(ClientSize.Width - margin - CancelInputButton.Width, buttonsTop);
            OkButton.Location          = new System.Drawing.Point(CancelInputButton.Left - 6 - OkButton.Width, buttonsTop);
        }

        private void OnOkButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(InputTextBox.Text.Trim()))
            {
                MessageBox.Show("Value cannot be empty.", "Error",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        private void OnCancelButtonClick(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
