using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VoxCharger
{
    public partial class MixSelectorForm : Form
    {
        public int ExpandedHeight  = 165;
        public int CollapsedHeight = 140;

        // Shown at the name box when a space is typed or pasted
        private readonly ToolTip _hint = new ToolTip { UseAnimation = false, UseFading = false };

        public MixSelectorForm(bool createMode = false)
        {
            InitializeComponent();
            Theme.Attach(this);

            if (createMode)
            {
                Text                              = "Create Mix";
                MixSelectorDropDown.SelectedIndex = 0;
                NameTextBox.Location              = MixSelectorDropDown.Location;
                MixSelectorDropDown.Visible       = MixSelectorDropDown.Enabled = false;

                ModSelectorLabel.Text = "Enter mix name:";
                Size = new Size(Size.Width, CollapsedHeight);

                // The name becomes the mix folder; the game doesn't read a folder with a space in its name
                NameTextBox.KeyPress += (s, e) =>
                {
                    if (char.IsWhiteSpace(e.KeyChar))
                    {
                        e.Handled = true;
                        ShowSpaceHint();
                    }
                    else
                        _hint.Hide(NameTextBox);
                };
                NameTextBox.TextChanged += (s, e) => DropWhitespace();
                Disposed += (s, e) => _hint.Dispose();
            }
        }

        private void ShowSpaceHint()
        {
            _hint.Show("The game can't read a mix whose folder name contains a space. Use _ or - instead.", NameTextBox, 0, NameTextBox.Height + 2, 3000);
        }

        // Pasted or dropped text may bring spaces along
        private void DropWhitespace()
        {
            string clean = new string(NameTextBox.Text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (clean == NameTextBox.Text)
                return;

            int caret = NameTextBox.SelectionStart - (NameTextBox.Text.Length - clean.Length);
            NameTextBox.Text = clean;
            NameTextBox.SelectionStart = Math.Max(0, Math.Min(clean.Length, caret));
            ShowSpaceHint();
        }

        private void OnModSelectorFormLoad(object sender, EventArgs e)
        {
            foreach (string mix in AssetManager.GetMixes())
                MixSelectorDropDown.Items.Add(mix);
        }

        private void OnModSelectorDropDownSelectedIndexChanged(object sender, EventArgs e)
        {
            if (MixSelectorDropDown.SelectedIndex == 0)
                Size = new Size(Size.Width, ExpandedHeight);
            else
                Size = new Size(Size.Width, CollapsedHeight);

            NameTextBox.Visible    = MixSelectorDropDown.SelectedIndex == 0;
            ContinueButton.Enabled = true;
        }

        private void OnContinueButtonClick(object sender, EventArgs e)
        {
            if (MixSelectorDropDown.SelectedIndex == 0)
            {
                string problem = AssetManager.MixNameProblem(NameTextBox.Text);
                if (problem != null)
                {
                    MessageBox.Show(
                        problem,
                        "Mix name",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );

                    return;
                }
                else if (AssetManager.GetMixes().Contains(NameTextBox.Text, StringComparer.OrdinalIgnoreCase))
                {
                    MessageBox.Show(
                        "A mix with this name already exists.",
                        "Mix name",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning
                    );

                    return;
                }
            }

            try
            {
                if (MixSelectorDropDown.SelectedIndex == 0)
                    AssetManager.CreateMix(NameTextBox.Text);
                else if (MixSelectorDropDown.SelectedIndex == 1)
                    AssetManager.LoadMix(string.Empty);
                else
                    AssetManager.LoadMix(MixSelectorDropDown.SelectedItem.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );

                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
