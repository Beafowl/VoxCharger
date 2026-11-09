using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VoxCharger
{
    // Tools > Rebuild song lists: picks the mixes whose music_db is written again,
    // see AssetManager.RebuildMixDbs
    public class RebuildMixesForm : Form
    {
        private class MixItem
        {
            public string Name;
            public int Songs;

            public override string ToString()
            {
                return Songs == 0 ? $"{Name}   (no songs)" : $"{Name}   {Songs} song{(Songs == 1 ? "" : "s")}";
            }
        }

        private readonly CheckedListBox _mixes = new CheckedListBox { CheckOnClick = true, IntegralHeight = false };
        private readonly Label _preview        = new Label { AutoSize = false, ForeColor = Theme.TextMuted };

        public IList<string> Mixes { get; private set; }

        public RebuildMixesForm(IEnumerable<string> mixes, IEnumerable<string> preselected)
        {
            Text            = "Rebuild song lists";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            MaximizeBox     = MinimizeBox = ShowInTaskbar = false;
            Font            = Theme.Body;
            BackColor       = Theme.Window;
            ForeColor       = Theme.Text;
            ClientSize      = new Size(520, 430);
            ShowIcon        = false;

            var intro = new Label
            {
                Text      = "Writes the song list (music_db) of the selected mixes again. The max EX score of every chart is " +
                            "computed anew from its chart file; titles, levels, radars and everything else stay as they are.",
                AutoSize  = false,
                ForeColor = Theme.TextMuted,
            };
            intro.SetBounds(20, 16, ClientSize.Width - 40, 54);

            var items = mixes
                .Select(name => new MixItem { Name = name, Songs = AssetManager.MusicIdsIn(AssetManager.MixDbFilename(name)).Count })
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _mixes.SetBounds(20, 76, ClientSize.Width - 40, 240);
            foreach (var item in items)
                _mixes.Items.Add(item, preselected.Contains(item.Name, StringComparer.OrdinalIgnoreCase));

            _preview.SetBounds(20, 326, ClientSize.Width - 40, 40);

            var cancel = new FlatButton { Text = "Cancel", DialogResult = DialogResult.Cancel };
            var apply  = new FlatButton { Text = "Rebuild", Kind = ButtonKind.Primary };
            cancel.SetBounds(ClientSize.Width - 236, ClientSize.Height - 56, 96, 36);
            apply.SetBounds(ClientSize.Width - 132, ClientSize.Height - 56, 112, 36);
            apply.Click += OnApplyClick;

            Controls.AddRange(new Control[] { intro, _mixes, _preview, cancel, apply });
            AcceptButton = apply;
            CancelButton = cancel;

            // ItemCheck fires before the check state changes
            _mixes.ItemCheck += (s, e) => BeginInvoke(new Action(UpdatePreview));

            Theme.Attach(this);
            Load += (s, e) => UpdatePreview();
        }

        private List<MixItem> CheckedMixes()
        {
            return _mixes.CheckedItems.Cast<MixItem>().ToList();
        }

        private void UpdatePreview()
        {
            var mixes = CheckedMixes();
            int songs = mixes.Sum(m => m.Songs);
            _preview.Text = mixes.Count == 0
                ? "Select the mixes to rebuild."
                : $"{mixes.Count} mix{(mixes.Count == 1 ? "" : "es")} with {songs} song{(songs == 1 ? "" : "s")}. The lists are saved right away.";
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            var mixes = CheckedMixes();
            if (mixes.Count == 0)
            {
                MessageBox.Show(this, "Select at least one mix.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Mixes        = mixes.Select(m => m.Name).ToList();
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
