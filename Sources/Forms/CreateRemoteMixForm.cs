using System;
using System.Windows.Forms;

namespace VoxCharger
{
    public partial class CreateRemoteMixForm : Form
    {
        public string MixName => NameTextBox.Text.Trim();
        public int MusicIdStart => (int)MusicIdStartNumeric.Value;

        public CreateRemoteMixForm()
        {
            InitializeComponent();
        }

        private void OnOkButtonClick(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(NameTextBox.Text.Trim()))
            {
                MessageBox.Show("Mix name cannot be empty.", "Error",
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
