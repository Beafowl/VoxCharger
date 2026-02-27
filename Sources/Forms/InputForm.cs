using System;
using System.Windows.Forms;

namespace VoxCharger
{
    public partial class InputForm : Form
    {
        public string Value => InputTextBox.Text.Trim();

        public InputForm(string title, string prompt)
        {
            InitializeComponent();
            Text = title;
            PromptLabel.Text = prompt;
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
