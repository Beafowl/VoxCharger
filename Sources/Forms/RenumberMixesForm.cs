using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace VoxCharger
{
    // Tools > Renumber mixes: picks the mixes and the first music id. The mixes are numbered one after
    // another in list order (their current id order), see AssetManager.PlanRenumberMixes.
    public class RenumberMixesForm : Form
    {
        private class MixItem
        {
            public string Name;
            public List<int> Ids;

            public override string ToString()
            {
                if (Ids.Count == 0)
                    return $"{Name}   (no songs)";

                return $"{Name}   {Ids.Count} song{(Ids.Count == 1 ? "" : "s")}, IDs {Ids.Min()}–{Ids.Max()}";
            }
        }

        private readonly CheckedListBox _mixes = new CheckedListBox { CheckOnClick = true, IntegralHeight = false };
        private readonly TextBox _startId      = new TextBox();
        private readonly Label _preview        = new Label { AutoSize = false, ForeColor = Theme.TextMuted };

        public IList<string> Mixes { get; private set; }
        public int StartId         { get; private set; }

        public RenumberMixesForm(IEnumerable<string> mixes, IEnumerable<string> preselected)
        {
            Text            = "Renumber mixes";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            MaximizeBox     = MinimizeBox = ShowInTaskbar = false;
            Font            = Theme.Body;
            BackColor       = Theme.Window;
            ForeColor       = Theme.Text;
            ClientSize      = new Size(520, 470);
            ShowIcon        = false;

            var intro = new Label
            {
                Text      = "Gives the songs of the selected mixes consecutive music IDs, mix after mix in the order below. " +
                            "IDs used by the game or by other mixes are skipped, so no free ID is left in between.",
                AutoSize  = false,
                ForeColor = Theme.TextMuted,
            };
            intro.SetBounds(20, 16, ClientSize.Width - 40, 54);

            // Current id order, so renumbering keeps the mixes where they are relative to each other
            var items = mixes
                .Select(name => new MixItem { Name = name, Ids = AssetManager.MusicIdsIn(AssetManager.MixDbFilename(name)) })
                .OrderBy(item => item.Ids.Count == 0 ? int.MaxValue : item.Ids.Min())
                .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _mixes.SetBounds(20, 76, ClientSize.Width - 40, 230);
            foreach (var item in items)
                _mixes.Items.Add(item, preselected.Contains(item.Name, StringComparer.OrdinalIgnoreCase));

            var startLabel = new Label { Text = "Starting ID", AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.TextMuted };
            startLabel.SetBounds(20, 320, 120, 26);
            _startId.SetBounds(150, 321, 120, 26);
            var lowest = items.Where(i => preselected.Contains(i.Name, StringComparer.OrdinalIgnoreCase) && i.Ids.Count > 0).SelectMany(i => i.Ids).DefaultIfEmpty(0).Min();
            _startId.Text = lowest > 0 ? lowest.ToString() : string.Empty;

            _preview.SetBounds(20, 356, ClientSize.Width - 40, 44);

            var cancel = new FlatButton { Text = "Cancel", DialogResult = DialogResult.Cancel };
            var apply  = new FlatButton { Text = "Renumber", Kind = ButtonKind.Primary };
            cancel.SetBounds(ClientSize.Width - 236, ClientSize.Height - 56, 96, 36);
            apply.SetBounds(ClientSize.Width - 132, ClientSize.Height - 56, 112, 36);
            apply.Click += OnApplyClick;

            Controls.AddRange(new Control[] { intro, _mixes, startLabel, _startId, _preview, cancel, apply });
            AcceptButton = apply;
            CancelButton = cancel;

            // ItemCheck fires before the check state changes
            _mixes.ItemCheck   += (s, e) => BeginInvoke(new Action(UpdatePreview));
            _startId.TextChanged += (s, e) => UpdatePreview();

            Theme.Attach(this);
            Load += (s, e) => UpdatePreview();
        }

        private List<string> CheckedMixes()
        {
            return _mixes.CheckedItems.Cast<MixItem>().Select(item => item.Name).ToList();
        }

        private void UpdatePreview()
        {
            var mixes = CheckedMixes();
            if (mixes.Count == 0)
            {
                _preview.Text = "Select the mixes to renumber.";
                return;
            }

            if (!int.TryParse(_startId.Text.Trim(), out int start) || start <= 0)
            {
                _preview.Text = "Enter the first music ID.";
                return;
            }

            var plan  = AssetManager.PlanRenumberMixes(mixes, start);
            var ids   = plan.Values.SelectMany(m => m.Values).ToList();
            int moved = plan.Values.Sum(m => m.Count(pair => pair.Key != pair.Value));
            if (ids.Count > 0 && ids.Max() > AssetManager.MaxMusicId)
            {
                _preview.ForeColor = Theme.Danger;
                _preview.Text = $"From {start} the songs would need IDs up to {ids.Max()}; the game uses IDs up to {AssetManager.MaxMusicId}.";
                return;
            }

            _preview.ForeColor = Theme.TextMuted;
            _preview.Text = ids.Count == 0
                ? "The selected mixes have no songs."
                : $"{ids.Count} songs get IDs {ids.Min()}–{ids.Max()}; {moved} of them change.";
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            var mixes = CheckedMixes();
            if (mixes.Count == 0)
            {
                MessageBox.Show(this, "Select at least one mix.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!int.TryParse(_startId.Text.Trim(), out int start) || start <= 0 || start > AssetManager.MaxMusicId)
            {
                MessageBox.Show(this, $"Starting ID must be a number from 1 to {AssetManager.MaxMusicId}.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _startId.Focus();
                return;
            }

            int highest = AssetManager.PlanRenumberMixes(mixes, start).Values.SelectMany(m => m.Values).DefaultIfEmpty(0).Max();
            if (highest > AssetManager.MaxMusicId)
            {
                MessageBox.Show(this, $"From {start} the songs would need IDs up to {highest}; the game uses IDs up to {AssetManager.MaxMusicId}. Choose a lower starting ID.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _startId.Focus();
                return;
            }

            Mixes        = mixes;
            StartId      = start;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
