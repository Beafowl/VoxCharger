using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace VoxCharger
{
    // Repair-mix dialog: scans every chart in the loaded mix against its
    // manifest entry, displays drift status in a list, and offers a one-click
    // re-export of selected drifted charts.
    public class RepairForm : Form
    {
        private readonly ListView _list;
        private readonly Button   _refresh;
        private readonly Button   _repair;
        private readonly Button   _close;
        private readonly Label    _summary;
        private readonly CheckBox _selectDrift;
        private List<ChartReport> _reports = new List<ChartReport>();

        public RepairForm()
        {
            Text          = "Repair Mix";
            ClientSize    = new Size(820, 480);
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize   = new Size(640, 360);

            _list = new ListView
            {
                Location      = new Point(12, 12),
                Size          = new Size(796, 380),
                Anchor        = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                View          = View.Details,
                FullRowSelect = true,
                CheckBoxes    = true,
                MultiSelect   = true,
                GridLines     = true,
                HideSelection = false,
            };
            _list.Columns.Add("",         24);  // checkbox spacer
            _list.Columns.Add("Id",       50);
            _list.Columns.Add("Code",     200);
            _list.Columns.Add("Status",   100);
            _list.Columns.Add("Detail",   400);

            _summary = new Label
            {
                Location  = new Point(12, 400),
                Size      = new Size(796, 20),
                Anchor    = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                AutoSize  = false,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _selectDrift = new CheckBox
            {
                Location = new Point(12, 425),
                Size     = new Size(220, 24),
                Anchor   = AnchorStyles.Bottom | AnchorStyles.Left,
                Text     = "Select all drifted charts",
                Checked  = true,
            };
            _selectDrift.CheckedChanged += (s, e) => SelectDrifted(_selectDrift.Checked);

            _refresh = new Button
            {
                Location = new Point(540, 425),
                Size     = new Size(80, 28),
                Anchor   = AnchorStyles.Bottom | AnchorStyles.Right,
                Text     = "Refresh",
            };
            _refresh.Click += (s, e) => RefreshReports();

            _repair = new Button
            {
                Location = new Point(626, 425),
                Size     = new Size(110, 28),
                Anchor   = AnchorStyles.Bottom | AnchorStyles.Right,
                Text     = "Repair Selected",
            };
            _repair.Click += OnRepairClick;

            _close = new Button
            {
                Location = new Point(742, 425),
                Size     = new Size(66, 28),
                Anchor   = AnchorStyles.Bottom | AnchorStyles.Right,
                Text     = "Close",
                DialogResult = DialogResult.Cancel,
            };
            _close.Click += (s, e) => Close();

            Controls.Add(_list);
            Controls.Add(_summary);
            Controls.Add(_selectDrift);
            Controls.Add(_refresh);
            Controls.Add(_repair);
            Controls.Add(_close);
            CancelButton = _close;

            Load += (s, e) => RefreshReports();
        }

        private void RefreshReports()
        {
            _list.Items.Clear();

            try
            {
                _reports = MixRepairer.Verify();
            }
            catch (Exception ex)
            {
                _summary.Text = $"verify failed: {ex.Message}";
                return;
            }

            int ok = 0, drift = 0, missing = 0, legacy = 0;
            foreach (var r in _reports)
            {
                var item = new ListViewItem("");
                item.SubItems.Add(r.Id.ToString("D4"));
                item.SubItems.Add(r.CodeName ?? "");
                item.SubItems.Add(r.Status.ToString());
                item.SubItems.Add(r.Detail ?? "");
                item.Tag = r;
                item.ForeColor = ColorFor(r.Status);
                _list.Items.Add(item);

                switch (r.Status)
                {
                    case ChartStatus.Ok:             ok++; break;
                    case ChartStatus.OutputDrift:
                    case ChartStatus.SourceDrift:    drift++; break;
                    case ChartStatus.SourceMissing:  missing++; break;
                    case ChartStatus.NotInManifest:  legacy++; break;
                }
            }

            _summary.Text = $"{_reports.Count} charts: {ok} ok, {drift} drifted, {missing} source missing, {legacy} not in manifest";
            SelectDrifted(_selectDrift.Checked);
        }

        private void SelectDrifted(bool checkedState)
        {
            foreach (ListViewItem item in _list.Items)
            {
                if (!(item.Tag is ChartReport r)) continue;
                bool isDrifted = r.Status == ChartStatus.OutputDrift || r.Status == ChartStatus.SourceDrift;
                item.Checked = checkedState && isDrifted;
            }
        }

        private void OnRepairClick(object sender, EventArgs e)
        {
            var selected = _list.Items.Cast<ListViewItem>()
                .Where(i => i.Checked && i.Tag is ChartReport)
                .Select(i => (ChartReport)i.Tag)
                .ToList();

            if (selected.Count == 0)
            {
                MessageBox.Show(this, "No charts selected.", "Repair Mix", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            int repaired = 0, failed = 0;
            var errors = new List<string>();

            UseWaitCursor = true;
            _repair.Enabled = false;
            try
            {
                foreach (var report in selected)
                {
                    try
                    {
                        MixRepairer.Repair(report);
                        repaired++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        errors.Add($"{report.CodeName}: {ex.Message}");
                    }
                }

                if (repaired > 0)
                    AssetManager.Headers.Save(AssetManager.MdbFilename);
            }
            finally
            {
                UseWaitCursor = false;
                _repair.Enabled = true;
            }

            string msg = $"Repaired {repaired} chart(s).";
            if (failed > 0)
                msg += $"\n\n{failed} failed:\n - " + string.Join("\n - ", errors.Take(10));

            MessageBox.Show(this, msg, "Repair Mix",
                MessageBoxButtons.OK,
                failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);

            RefreshReports();
        }

        private static Color ColorFor(ChartStatus s)
        {
            switch (s)
            {
                case ChartStatus.Ok:             return Color.FromArgb(120, 120, 120);
                case ChartStatus.OutputDrift:    return Color.FromArgb(192, 64, 64);
                case ChartStatus.SourceDrift:    return Color.FromArgb(192, 128, 0);
                case ChartStatus.SourceMissing:  return Color.FromArgb(128, 0, 0);
                case ChartStatus.NotInManifest:  return Color.FromArgb(96, 96, 160);
                default:                         return SystemColors.WindowText;
            }
        }
    }
}
