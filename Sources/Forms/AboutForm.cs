using System;
using System.Windows.Forms;

namespace VoxCharger
{
    public partial class AboutForm : Form
    {
        public AboutForm()
        {
            InitializeComponent();

            // Form titlebar / taskbar icon follows Windows theme so it stays
            // legible against the OS chrome. The big in-content profile
            // picture (the 80x80 image on the left of the form) is always
            // the dark icon — it sits on the form's light background, where
            // the white variant would disappear.
            var titlebarIcon = IconLoader.ForCurrentTheme();
            if (titlebarIcon != null) Icon = titlebarIcon;

            try
            {
                using (var dark = IconLoader.Dark())
                {
                    if (dark != null)
                        ProfilePictureBox.Image = dark.ToBitmap();
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
