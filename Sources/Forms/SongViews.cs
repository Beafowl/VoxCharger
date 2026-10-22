using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace VoxCharger
{
    // Small scaled copies of jacket files, so list rows don't decode PNGs on every paint
    public static class JacketCache
    {
        private const int MaxEntries = 800;
        private static readonly Dictionary<string, Image> _images = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public static Image Get(string path, int size)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            string key = $"{size}|{path}";
            if (_images.TryGetValue(key, out var cached))
                return cached;

            Image result = null;
            try
            {
                if (File.Exists(path))
                {
                    using (var image = Image.FromFile(path))
                        result = Scale(image, size);
                }
            }
            catch (Exception)
            {
                result = null;
            }

            if (_images.Count >= MaxEntries)
                Clear();

            _images[key] = result;
            return result;
        }

        public static Bitmap Scale(Image image, int size)
        {
            var bitmap = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                g.DrawImage(image, new Rectangle(0, 0, size, size));
            }
            return bitmap;
        }

        public static void Clear()
        {
            foreach (var image in _images.Values)
                image?.Dispose();

            _images.Clear();
        }

        // Song select size jacket of a chart, falling back to the song's first jacket
        public static string JacketFile(VoxHeader header, Difficulty difficulty)
        {
            try
            {
                string path = $"{AssetManager.GetJacketPath(header, difficulty)}_s.png";
                if (File.Exists(path))
                    return path;

                foreach (var diff in header.Levels.Keys.OrderBy(d => d))
                {
                    path = $"{AssetManager.GetJacketPath(header, diff)}_s.png";
                    if (File.Exists(path))
                        return path;
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        public static string SongJacketFile(VoxHeader header)
        {
            if (header?.Levels == null || header.Levels.Count == 0)
                return null;

            return JacketFile(header, header.Levels.Keys.Min());
        }

        // Placeholder: dark square with the title's first character
        public static void DrawPlaceholder(Graphics g, Rectangle rect, string title, float radius, Font font)
        {
            Theme.FillRounded(g, Color.FromArgb(0x2b, 0x32, 0x42), rect, radius);
            string letter = string.IsNullOrEmpty(title) ? "?" : char.IsSurrogate(title[0]) && title.Length > 1 ? title.Substring(0, 2) : title.Substring(0, 1);
            Theme.DrawText(g, letter.ToUpperInvariant(), font, Theme.Text, rect, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        public static void DrawImage(Graphics g, Image image, Rectangle rect, float radius)
        {
            using (var path = Theme.RoundedRect(rect, radius))
            {
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                g.DrawImage(image, rect);
                g.Clip = clip;
            }
        }
    }

    // Song list: jacket, title, artist, music id and one level badge per chart
    public class SongListBox : ListBox
    {
        private static readonly Color SelectedRow = Color.FromArgb(0x1b, 0x24, 0x36);
        private static readonly Color HoverRow    = Color.FromArgb(0x17, 0x1b, 0x23);
        private int _hover = -1;

        public SongListBox()
        {
            DrawMode       = DrawMode.OwnerDrawFixed;
            ItemHeight     = 56;
            BorderStyle    = BorderStyle.None;
            BackColor      = Theme.Sidebar;
            ForeColor      = Theme.Text;
            IntegralHeight = false;
            Font           = Theme.Body;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Theme.DarkScrollBars(this);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int index = IndexFromPoint(e.Location);
            if (index != _hover)
            {
                int old = _hover;
                _hover = index;
                if (old >= 0 && old < Items.Count) Invalidate(GetItemRectangle(old));
                if (index >= 0) Invalidate(GetItemRectangle(index));
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover >= 0 && _hover < Items.Count)
                Invalidate(GetItemRectangle(_hover));
            _hover = -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Right click selects the row under the cursor before the context menu opens
            if (e.Button == MouseButtons.Right)
            {
                int index = IndexFromPoint(e.Location);
                if (index >= 0)
                    SelectedIndex = index;
            }

            base.OnMouseDown(e);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var g = e.Graphics;
            var bounds = e.Bounds;
            if (e.Index < 0 || e.Index >= Items.Count)
            {
                using (var brush = new SolidBrush(BackColor))
                    g.FillRectangle(brush, bounds);
                return;
            }

            var header   = Items[e.Index] as VoxHeader;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            var rowColor = selected ? SelectedRow : e.Index == _hover ? HoverRow : BackColor;

            using (var buffer = new Bitmap(Math.Max(1, bounds.Width), Math.Max(1, bounds.Height)))
            using (var bg = Graphics.FromImage(buffer))
            {
                bg.Clear(rowColor);
                Theme.Smooth(bg);
                var rect = new Rectangle(0, 0, bounds.Width, bounds.Height);

                using (var pen = new Pen(Theme.RowLine))
                    bg.DrawLine(pen, 0, rect.Bottom - 1, rect.Right, rect.Bottom - 1);

                if (selected)
                {
                    using (var brush = new SolidBrush(Theme.Accent))
                        bg.FillRectangle(brush, 0, 0, 3, rect.Height);
                }

                if (header != null)
                    DrawSong(bg, rect, header, selected);

                g.DrawImageUnscaled(buffer, bounds.Location);
            }
        }

        private void DrawSong(Graphics g, Rectangle rect, VoxHeader header, bool selected)
        {
            var jacketRect = new Rectangle(16, (rect.Height - 36) / 2, 36, 36);
            var jacket = JacketCache.Get(JacketCache.SongJacketFile(header), 36);
            if (jacket != null)
                JacketCache.DrawImage(g, jacket, jacketRect, 6);
            else
                JacketCache.DrawPlaceholder(g, jacketRect, VoxHeader.WithDecodedSymbols(header.Title), 6, Theme.BodyBold);

            // Right column: id above the level badges
            var levels = (header.Levels ?? new Dictionary<Difficulty, VoxLevelHeader>()).OrderBy(l => l.Key).ToList();
            int badgesWidth = 0;
            var badgeSizes = new List<int>();
            foreach (var level in levels)
            {
                int w = Math.Max(18, TextRenderer.MeasureText(g, level.Value.Level.ToString(), Theme.Tiny, Size.Empty, TextFormatFlags.NoPadding).Width + 8);
                badgeSizes.Add(w);
                badgesWidth += w + 3;
            }
            badgesWidth = Math.Max(0, badgesWidth - 3);

            int right = rect.Width - 16;
            string id = header.Id.ToString();
            Theme.DrawText(g, id, Theme.Mono, selected ? Theme.TextSoft : Theme.TextDim, new Rectangle(right - 80, 9, 80, 16), TextFormatFlags.Right | TextFormatFlags.Top);

            int bx = right - badgesWidth;
            for (int i = 0; i < levels.Count; i++)
            {
                var badge = new Rectangle(bx, 30, badgeSizes[i], 16);
                Theme.FillRounded(g, Theme.DifficultyColor(levels[i].Key, header.InfVersion), badge, 4);
                Theme.DrawText(g, levels[i].Value.Level.ToString(), Theme.Tiny, Theme.OnAccent, badge, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                bx += badgeSizes[i] + 3;
            }

            int textLeft  = jacketRect.Right + 12;
            int textRight = right - Math.Max(badgesWidth, 40) - 10;
            Theme.DrawText(g, VoxHeader.WithDecodedSymbols(header.Title), Theme.BodyBold, Theme.Text, new Rectangle(textLeft, 9, textRight - textLeft, 20), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
            Theme.DrawText(g, VoxHeader.WithDecodedSymbols(header.Artist), Theme.Small, selected ? Theme.TextSoft : Theme.TextMuted, new Rectangle(textLeft, 30, textRight - textLeft, 18), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    // One difficulty of the selected song: jacket with the level strip, credits, file, actions
    public class ChartTile : CardPanel
    {
        private readonly FlatButton _editButton;
        private readonly FlatButton _removeButton;

        private VoxHeader _header;
        private VoxLevelHeader _level;
        private Image _jacket;
        private bool _editable;
        private bool _hover;

        public Difficulty Difficulty { get; }

        public event EventHandler EditClicked;
        public event EventHandler RemoveClicked;

        public ChartTile(Difficulty difficulty)
        {
            Difficulty = difficulty;
            Fill       = Theme.Inset;
            BackColor  = Theme.Inset;
            Radius     = 10;
            Height     = 88;

            _editButton = new FlatButton { Text = "Edit", Size = new Size(52, 30), Radius = 6, BoldText = false, Font = Theme.Small };
            _editButton.Click += (s, e) => EditClicked?.Invoke(this, EventArgs.Empty);

            _removeButton = new FlatButton { Kind = ButtonKind.Danger, Glyph = ButtonGlyph.Close, Size = new Size(30, 30), Radius = 6 };
            _removeButton.Click += (s, e) => RemoveClicked?.Invoke(this, EventArgs.Empty);

            Controls.Add(_editButton);
            Controls.Add(_removeButton);
        }

        public void Bind(VoxHeader header, Image jacket, bool editable)
        {
            _header   = header;
            _level    = header != null && header.Levels != null && header.Levels.TryGetValue(Difficulty, out var level) ? level : null;
            _jacket   = jacket;
            _editable = editable;

            bool filled = _level != null;
            Dashed      = !filled;
            Fill        = filled ? Theme.Inset : Color.Empty;
            BackColor   = filled ? Theme.Inset : Theme.Card;
            BorderColor = filled ? Theme.Border : Color.FromArgb(0x34, 0x3b, 0x4a);
            Cursor      = !filled && editable ? Cursors.Hand : Cursors.Default;

            _editButton.Visible   = filled && editable;
            _removeButton.Visible = filled && editable;

            LayoutButtons();
            Invalidate();
        }

        private string DiffName => Theme.DifficultyName(Difficulty, _header?.InfVersion ?? InfiniteVersion.Mxm);

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutButtons();
        }

        private void LayoutButtons()
        {
            if (_editButton == null || _removeButton == null)
                return; // the base constructor sizes the tile before the buttons exist

            int y = (Height - 30) / 2;
            _removeButton.Location = new Point(Width - 14 - _removeButton.Width, y);
            _editButton.Location   = new Point(_removeButton.Left - 6 - _editButton.Width, y);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (_level == null && _editable)
                EditClicked?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_level == null && _hover && _editable)
                Fill = Theme.Control;
            else if (_level == null)
                Fill = Color.Empty;

            base.OnPaint(e);
            var g = e.Graphics;
            Theme.Smooth(g);

            if (_header == null)
                return;

            if (_level == null)
            {
                string text = _editable ? $"Add {DiffName} chart" : $"No {DiffName} chart";
                var size = TextRenderer.MeasureText(g, text, Theme.Body);
                int total = (_editable ? 22 : 0) + size.Width;
                int x = (Width - total) / 2;
                if (_editable)
                {
                    FlatButton.DrawGlyph(g, ButtonGlyph.Plus, Theme.TextDim, new RectangleF(x, Height / 2f - 7, 14, 14));
                    x += 22;
                }
                Theme.DrawText(g, text, Theme.Body, Theme.TextDim, new Rectangle(x, 0, size.Width + 4, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
                return;
            }

            // Jacket with the difficulty strip underneath
            var jacketRect = new Rectangle(14, (Height - 60) / 2, 60, 60);
            using (var path = Theme.RoundedRect(jacketRect, 8))
            {
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                if (_jacket != null)
                    g.DrawImage(_jacket, jacketRect);
                else
                {
                    using (var brush = new SolidBrush(Color.FromArgb(0x2b, 0x32, 0x42)))
                        g.FillRectangle(brush, jacketRect);
                    Theme.DrawText(g, FirstLetter(), Theme.CardTitle, Theme.Text, new Rectangle(jacketRect.X, jacketRect.Y, jacketRect.Width, jacketRect.Height - 18), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                var strip = new Rectangle(jacketRect.X, jacketRect.Bottom - 18, jacketRect.Width, 18);
                using (var brush = new SolidBrush(Theme.DifficultyColor(Difficulty, _header.InfVersion)))
                    g.FillRectangle(brush, strip);
                Theme.DrawText(g, $"{DiffName} {_level.Level}", Theme.Tiny, Theme.OnAccent, strip, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                g.Clip = clip;
            }

            int left  = jacketRect.Right + 14;
            int width = (_editButton.Visible ? _editButton.Left : Width - 4) - 10 - left;
            int y     = jacketRect.Y + 2;
            DrawField(g, "Effector", Clean(_level.Effector), left, y, width, true);
            DrawField(g, "Illustrator", Clean(_level.Illustrator), left, y + 19, width, false);

            string file = string.Empty;
            try { file = Path.GetFileName(AssetManager.GetVoxPath(_header, Difficulty)); } catch (Exception) { }
            Theme.DrawText(g, file, Theme.Mono, Theme.TextDim, new Rectangle(left, y + 39, width, 16), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) || value == "dummy" ? "—" : VoxHeader.WithDecodedSymbols(value);
        }

        private string FirstLetter()
        {
            string title = VoxHeader.WithDecodedSymbols(_header?.Title);
            return string.IsNullOrEmpty(title) ? "?" : title.Substring(0, 1).ToUpperInvariant();
        }

        private static void DrawField(Graphics g, string label, string value, int x, int y, int width, bool strong)
        {
            var labelSize = TextRenderer.MeasureText(g, label + " ", Theme.Small, Size.Empty, TextFormatFlags.NoPadding);
            Theme.DrawText(g, label, Theme.Small, Theme.TextMuted, new Rectangle(x, y, labelSize.Width, 18), TextFormatFlags.Left | TextFormatFlags.NoPadding);
            Theme.DrawText(g, value, strong ? Theme.SmallBold : Theme.Small, strong ? Theme.Text : Theme.TextSoft, new Rectangle(x + labelSize.Width + 2, y, Math.Max(0, width - labelSize.Width - 2), 18), TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }
    }
}
