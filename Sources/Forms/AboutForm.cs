using System;
using System.Windows.Forms;

namespace VoxCharger
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();
            Theme.Attach(this);

            // The dialog is dark, so both the title bar icon and the big picture use the white logo
            var titlebarIcon = IconLoader.ForDarkUi();
            if (titlebarIcon != null) Icon = titlebarIcon;

            try
            {
                using (var light = IconLoader.Light())
                {
                    if (light != null)
                    {
                        using (var large = new System.Drawing.Icon(light, 128, 128).ToBitmap())
                            ProfilePictureBox.Image = JacketCache.Scale(large, 80);
                    }
                }
            }
            catch
            {
                // If the embedded icon load fails for any reason, keep
                // whatever picture the designer baked in.
            }
        }

        private void OnEmailLinkLabelLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start("mailto://o2jam@cxo2.me");
        }

        private void OnGithubLinkLabelLinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            System.Diagnostics.Process.Start(((LinkLabel)sender).Text);
        }

        private void OnCloseButtonClick(object sender, EventArgs e)
        {
            Close();
        }
    }
}
