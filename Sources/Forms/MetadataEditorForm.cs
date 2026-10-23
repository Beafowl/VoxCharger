using System;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace VoxCharger
{
    // Song metadata dialog of the main window. Validates the music id; the caller applies the
    // result and queues the file work (id rename, audio import).
    public class MetadataEditorForm : Form
    {
        private static readonly string[] Backgrounds = { "00", "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12", "13", "14", "15", "16", "18", "19", "27", "29", "30", "31", "34", "36", "38", "39", "40", "41", "42", "43", "44", "45", "46", "47", "48", "49", "50", "51", "53", "54", "57", "58", "59", "60", "61", "63", "65", "66", "67", "68", "69", "70", "71", "72", "73", "74", "75", "76", "77", "78", "79", "80", "81", "82", "83", "84", "86", "87", "88", "89", "90", "91", "92", "93", "95", "96", "97", "98" };
        private static readonly string[] Versions    = { "Sound Voltex: Booth", "Sound Voltex II: Infinite Infection", "Sound Voltex III: Gravity Wars", "Sound Voltex IV: Heavenly Haven", "Sound Voltex V: Vivid Wave", "Sound Voltex VI: Exceed Gear", "Sound Voltex VII: Nabla" };
        private static readonly string[] InfVersions = { "MXM", "INF", "GRV", "HVN", "VVD", "XCD" };

        private readonly VoxHeader _header;

        private readonly TextBox _id              = new TextBox();
        private readonly TextBox _title           = new TextBox();
        private readonly TextBox _titleYomigana   = new TextBox();
        private readonly TextBox _artist          = new TextBox();
        private readonly TextBox _artistYomigana  = new TextBox();
        private readonly NumericUpDown _bpmMin    = new NumericUpDown { DecimalPlaces = 2, Minimum = 0, Maximum = 10000 };
        private readonly NumericUpDown _bpmMax    = new NumericUpDown { DecimalPlaces = 2, Minimum = 0, Maximum = 10000 };
        private readonly ComboBox _version        = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly ComboBox _infVersion     = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly TextBox _date            = new TextBox();
        private readonly ComboBox _background     = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly NumericUpDown _volume    = new NumericUpDown { Minimum = 0, Maximum = 100 };
        private readonly FlatButton _musicButton  = new FlatButton { Text = "Replace music…", BoldText = false };
        private readonly FlatButton _previewButton = new FlatButton { Text = "Replace preview…", BoldText = false };

        public int MusicId                  { get; private set; }
        public string MusicFile             { get; private set; }
        public string PreviewFile           { get; private set; }

        public MetadataEditorForm(VoxHeader header, bool allowAudio)
        {
            _header = header;

            Text            = "Edit metadata";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition   = FormStartPosition.CenterParent;
            MaximizeBox     = MinimizeBox = ShowInTaskbar = false;
            Font            = Theme.Body;
            BackColor       = Theme.Window;
            ForeColor       = Theme.Text;
            ClientSize      = new Size(560, 520);
            Icon            = null;
            ShowIcon        = false;

            _version.Items.AddRange(Versions);
            _infVersion.Items.AddRange(InfVersions);
            _background.Items.AddRange(Backgrounds);

            int y = 20;
            AddRow("Music ID", _id, ref y, 120);
            AddRow("Title", _title, ref y);
            AddRow("Title reading", _titleYomigana, ref y);
            AddRow("Artist", _artist, ref y);
            AddRow("Artist reading", _artistYomigana, ref y);
            AddPair("BPM", _bpmMin, "to", _bpmMax, ref y);
            AddRow("Version", _version, ref y);
            AddRow("INF slot", _infVersion, ref y, 120);
            AddRow("Released", _date, ref y, 120);
            var calendarButton = new FlatButton { Glyph = ButtonGlyph.Calendar, Radius = 4, Size = new Size(30, 26) };
            calendarButton.Location = new Point(_date.Right + 6, _date.Top);
            calendarButton.Click += (s, e) => ShowCalendar(calendarButton);
            Controls.Add(calendarButton);
            AddRow("Background", _background, ref y, 120);
            AddRow("Volume", _volume, ref y, 120);

            _musicButton.SetBounds(20, ClientSize.Height - 56, 150, 36);
            _previewButton.SetBounds(178, ClientSize.Height - 56, 150, 36);
            _musicButton.Visible = _previewButton.Visible = allowAudio;
            _musicButton.Click   += (s, e) => MusicFile   = PickAudio("Music", _musicButton);
            _previewButton.Click += (s, e) => PreviewFile = PickAudio("Preview", _previewButton);

            var cancel = new FlatButton { Text = "Cancel", DialogResult = DialogResult.Cancel };
            var apply  = new FlatButton { Text = "Apply", Kind = ButtonKind.Primary };
            cancel.SetBounds(ClientSize.Width - 220, ClientSize.Height - 56, 96, 36);
            apply.SetBounds(ClientSize.Width - 116, ClientSize.Height - 56, 96, 36);
            apply.Click += OnApplyClick;

            Controls.AddRange(new Control[] { _musicButton, _previewButton, cancel, apply });
            AcceptButton = apply;
            CancelButton = cancel;

            Theme.Attach(this);
            Load += (s, e) => Fill();
        }

        private void AddRow(string caption, Control field, ref int y, int width = 0)
        {
            var label = new Label { Text = caption, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.TextMuted };
            label.SetBounds(20, y, 120, 26);
            field.SetBounds(150, y + 1, width > 0 ? width : ClientSize.Width - 170, 26);
            Controls.Add(label);
            Controls.Add(field);
            y += 36;
        }

        private void AddPair(string caption, Control first, string between, Control second, ref int y)
        {
            var label = new Label { Text = caption, AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.TextMuted };
            label.SetBounds(20, y, 120, 26);
            first.SetBounds(150, y + 1, 120, 26);
            var mid = new Label { Text = between, AutoSize = false, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Theme.TextMuted };
            mid.SetBounds(270, y, 40, 26);
            second.SetBounds(310, y + 1, 120, 26);
            Controls.AddRange(new[] { label, first, mid, second });
            y += 36;
        }

        private void Fill()
        {
            _id.Text             = _header.Id.ToString();
            _title.Text          = VoxHeader.WithDecodedSymbols(_header.Title);
            _titleYomigana.Text  = VoxHeader.WithDecodedSymbols(_header.TitleYomigana);
            _artist.Text         = VoxHeader.WithDecodedSymbols(_header.Artist);
            _artistYomigana.Text = VoxHeader.WithDecodedSymbols(_header.ArtistYomigana);
            _bpmMin.Value        = Clamp((decimal)_header.BpmMin, _bpmMin);
            _bpmMax.Value        = Clamp((decimal)_header.BpmMax, _bpmMax);
            _version.SelectedIndex    = Math.Max(0, Math.Min(Versions.Length - 1, (int)_header.Version - 1));
            _infVersion.SelectedIndex = _header.InfVersion == InfiniteVersion.Mxm ? 0 : Math.Max(0, Math.Min(InfVersions.Length - 1, (int)_header.InfVersion - 1));
            _date.Text           = _header.DistributionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string bg            = $"{_header.BackgroundId:D2}";
            if (!_background.Items.Contains(bg))
                _background.Items.Add(bg);
            _background.SelectedItem = bg;
            _volume.Value        = Clamp(_header.Volume, _volume);
        }

        [System.Runtime.InteropServices.DllImport("uxtheme.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        private ToolStripDropDown _calendarPopup;
        private MonthCalendar _calendar;

        // Month calendar below the release date field; picking a day fills the field.
        // The popup is created once and disposed with the dialog: disposing it from its own Closed
        // event broke the menu message filter, which still handles the click that closed it.
        private void ShowCalendar(Control anchor)
        {
            if (_calendarPopup == null)
                CreateCalendar();

            if (DateTime.TryParseExact(_date.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var current))
                _calendar.SetDate(current);

            _calendarPopup.Show(anchor, new Point(anchor.Width - _calendar.Width - 2, anchor.Height + 4));
        }

        private void CreateCalendar()
        {
            var calendar = new MonthCalendar
            {
                MaxSelectionCount = 1,
                ShowTodayCircle   = true,
                Font              = Theme.Body,
            };

            // Without visual styles the calendar honours its colors
            calendar.HandleCreated += (s, e) => SetWindowTheme(calendar.Handle, "", "");
            calendar.BackColor         = Theme.Control;
            calendar.ForeColor         = Theme.Text;
            calendar.TitleBackColor    = Theme.Accent;
            calendar.TitleForeColor    = Theme.OnAccent;
            calendar.TrailingForeColor = Theme.TextOff;

            var host  = new ToolStripControlHost(calendar) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false, Size = calendar.Size };
            var popup = new ToolStripDropDown { Padding = new Padding(1), BackColor = Theme.BorderLight, Renderer = new DarkMenuRenderer() };
            popup.Items.Add(host);

            calendar.DateSelected += (s, e) =>
            {
                _date.Text = e.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                BeginInvoke(new Action(() =>
                {
                    popup.Close();
                    _date.Focus();
                }));
            };

            host.Size      = calendar.Size;
            _calendar      = calendar;
            _calendarPopup = popup;
            FormClosed += (s, e) => popup.Dispose();
        }

        private static decimal Clamp(decimal value, NumericUpDown box)
        {
            return Math.Max(box.Minimum, Math.Min(box.Maximum, value));
        }

        private string PickAudio(string kind, FlatButton button)
        {
            using (var browser = new OpenFileDialog())
            {
                browser.Filter = "All supported formats|*.2dx;*.s3v;*.asf;*.wav;*.ogg;*.mp3;*.flac|BEMANI Music Files|*.2dx;*.s3v|Music Files|*.wav;*.ogg;*.mp3;*.flac;*.asf";
                browser.CheckFileExists = true;
                browser.Title = $"Replace {kind.ToLowerInvariant()}";
                if (browser.ShowDialog(this) != DialogResult.OK)
                    return kind == "Music" ? MusicFile : PreviewFile;

                button.Text        = $"{kind}: {System.IO.Path.GetFileName(browser.FileName)}";
                button.Highlighted = true;
                return browser.FileName;
            }
        }

        private void OnApplyClick(object sender, EventArgs e)
        {
            if (!int.TryParse(_id.Text.Trim(), out int id) || id <= 0)
            {
                Warn("Music ID must be a positive number.", _id);
                return;
            }

            if (id != _header.Id)
            {
                if (!AssetManager.ValidateMusicId(id) || AssetManager.Headers.Contains(id))
                {
                    Warn("Music ID is already taken.", _id);
                    return;
                }

                if (id < AssetManager.MixStartId)
                {
                    Warn($"Music ID must be at least {AssetManager.MixStartId}, the starting ID of this mix.", _id);
                    return;
                }
            }

            if (!DateTime.TryParseExact(_date.Text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                Warn("Release date must look like 2026-10-22.", _date);
                return;
            }

            if (string.IsNullOrWhiteSpace(_title.Text))
            {
                Warn("Title cannot be empty.", _title);
                return;
            }

            double min = (double)Math.Min(_bpmMin.Value, _bpmMax.Value);
            double max = (double)Math.Max(_bpmMin.Value, _bpmMax.Value);

            MusicId                   = id;
            _header.Title             = _title.Text;
            _header.TitleYomigana     = _titleYomigana.Text;
            _header.Artist            = _artist.Text;
            _header.ArtistYomigana    = _artistYomigana.Text;
            _header.BpmMin            = min;
            _header.BpmMax            = max;
            _header.Version           = (GameVersion)(_version.SelectedIndex + 1);
            _header.InfVersion        = _infVersion.SelectedIndex <= 0 ? InfiniteVersion.Mxm : (InfiniteVersion)(_infVersion.SelectedIndex + 1);
            _header.DistributionDate  = date;
            _header.BackgroundId      = short.Parse((_background.SelectedItem ?? "0").ToString());
            _header.Volume            = (short)_volume.Value;

            DialogResult = DialogResult.OK;
            Close();
        }

        private void Warn(string message, Control field)
        {
            MessageBox.Show(this, message, "Edit metadata", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            field.Focus();
        }
    }
}
