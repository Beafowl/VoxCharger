using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace VoxCharger
{
    // Dark look shared by the main window and its dialogs
    public static class Theme
    {
        public static readonly Color Window      = Color.FromArgb(0x11, 0x13, 0x18);
        public static readonly Color Header      = Color.FromArgb(0x0e, 0x10, 0x15);
        public static readonly Color Sidebar     = Color.FromArgb(0x13, 0x16, 0x1c);
        public static readonly Color Strip       = Color.FromArgb(0x15, 0x18, 0x1f);
        public static readonly Color Card        = Color.FromArgb(0x15, 0x18, 0x1f);
        public static readonly Color Inset       = Color.FromArgb(0x11, 0x13, 0x18);
        public static readonly Color Control     = Color.FromArgb(0x1a, 0x1e, 0x27);
        public static readonly Color ControlHot  = Color.FromArgb(0x22, 0x27, 0x33);
        public static readonly Color Border      = Color.FromArgb(0x26, 0x2b, 0x36);
        public static readonly Color BorderLight = Color.FromArgb(0x2c, 0x32, 0x40);
        public static readonly Color RowLine     = Color.FromArgb(0x1c, 0x20, 0x29);
        public static readonly Color Placeholder = Color.FromArgb(0x26, 0x2b, 0x36);

        public static readonly Color Text        = Color.FromArgb(0xe6, 0xe8, 0xee);
        public static readonly Color TextSoft    = Color.FromArgb(0xc3, 0xc9, 0xd6);
        public static readonly Color TextMuted   = Color.FromArgb(0x9a, 0xa3, 0xb5);
        public static readonly Color TextDim     = Color.FromArgb(0x8b, 0x93, 0xa7);
        public static readonly Color TextOff     = Color.FromArgb(0x5d, 0x65, 0x77);
        public static readonly Color OnAccent    = Color.FromArgb(0x0b, 0x0d, 0x12);

        public static readonly Color Accent      = Color.FromArgb(0x4f, 0x8c, 0xff);
        public static readonly Color AccentHot   = Color.FromArgb(0x6c, 0xa0, 0xff);
        public static readonly Color AccentSoft  = Color.FromArgb(0x24, 0x33, 0x52);
        public static readonly Color AccentText  = Color.FromArgb(0x9f, 0xc0, 0xff);
        public static readonly Color Danger      = Color.FromArgb(0xf0, 0x69, 0x7d);

        public static readonly Font Body      = new Font("Segoe UI", 9.75f);
        public static readonly Font BodyBold  = new Font("Segoe UI", 9.75f, FontStyle.Bold);
        public static readonly Font Small     = new Font("Segoe UI", 8.25f);
        public static readonly Font SmallBold = new Font("Segoe UI", 8.25f, FontStyle.Bold);
        public static readonly Font Caption   = new Font("Segoe UI", 7.5f, FontStyle.Bold);
        public static readonly Font Tiny      = new Font("Segoe UI", 7f, FontStyle.Bold);
        public static readonly Font Badge     = new Font("Segoe UI Semibold", 8.25f);
        public static readonly Font Mono      = new Font("Consolas", 8.25f);
        public static readonly Font MonoLarge = new Font("Consolas", 11.25f);
        public static readonly Font Title     = new Font("Segoe UI", 17f, FontStyle.Bold);
        public static readonly Font Subtitle  = new Font("Segoe UI", 10.5f);
        public static readonly Font CardTitle = new Font("Segoe UI", 11f, FontStyle.Bold);

        // In-game difficulty colors
        public static Color DifficultyColor(Difficulty difficulty, InfiniteVersion infVersion)
        {
            switch (difficulty)
            {
                case Difficulty.Novice:   return Color.FromArgb(0x7f, 0x8c, 0xff);
                case Difficulty.Advanced: return Color.FromArgb(0xe9, 0xc1, 0x5a);
                case Difficulty.Exhaust:  return Color.FromArgb(0xf0, 0x69, 0x7d);
            }

            switch (infVersion)
            {
                case InfiniteVersion.Inf: return Color.FromArgb(0xef, 0x6b, 0xd0);
                case InfiniteVersion.Grv: return Color.FromArgb(0xf2, 0x9a, 0x4a);
                case InfiniteVersion.Hvn: return Color.FromArgb(0x5a, 0xc8, 0xe8);
                case InfiniteVersion.Vvd: return Color.FromArgb(0xf4, 0x72, 0xb6);
                case InfiniteVersion.Xcd: return Color.FromArgb(0x60, 0xa5, 0xfa);
                default:                  return Color.FromArgb(0xcf, 0xd5, 0xe1);
            }
        }

        public static string DifficultyName(Difficulty difficulty, InfiniteVersion infVersion)
        {
            switch (difficulty)
            {
                case Difficulty.Novice:   return "NOV";
                case Difficulty.Advanced: return "ADV";
                case Difficulty.Exhaust:  return "EXH";
            }

            return infVersion == InfiniteVersion.Mxm ? "MXM" : infVersion.ToString().ToUpperInvariant();
        }

        #region --- Drawing ---
        public static GraphicsPath RoundedRect(RectangleF rect, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            if (d <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void FillRounded(Graphics g, Color color, RectangleF rect, float radius)
        {
            using (var path = RoundedRect(rect, radius))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        public static void DrawRounded(Graphics g, Color color, RectangleF rect, float radius, float width = 1f, DashStyle dash = DashStyle.Solid)
        {
            using (var path = RoundedRect(rect, radius))
            using (var pen = new Pen(color, width) { DashStyle = dash })
                g.DrawPath(pen, path);
        }

        public static void Chevron(Graphics g, Color color, PointF center, bool up, float size = 4f)
        {
            using (var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float dy = up ? -size / 2 : size / 2;
                g.DrawLines(pen, new[]
                {
                    new PointF(center.X - size, center.Y - dy),
                    new PointF(center.X, center.Y + dy),
                    new PointF(center.X + size, center.Y - dy),
                });
            }
        }

        public static void DrawText(Graphics g, string text, Font font, Color color, Rectangle bounds, TextFormatFlags flags)
        {
            TextRenderer.DrawText(g, text, font, bounds, color, flags | TextFormatFlags.NoPrefix);
        }

        public static void Smooth(Graphics g)
        {
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        }
        #endregion

        #region --- Window chrome ---
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string appName, string idList);

        // Dark caption on Windows 10 1809+, caption color on Windows 11
        public static void DarkTitleBar(Form form, Color? caption = null)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref on, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref on, sizeof(int));

                var c = caption ?? Header;
                int colorRef = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(form.Handle, 35, ref colorRef, sizeof(int));

                int text = Text.R | (Text.G << 8) | (Text.B << 16);
                DwmSetWindowAttribute(form.Handle, 36, ref text, sizeof(int));
            }
            catch (Exception)
            {
                // Older Windows: keep the default caption
            }
        }

        // Dark scroll bars for list boxes and scrolling panels
        public static void DarkScrollBars(Control control)
        {
            try
            {
                if (control.IsHandleCreated)
                    SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
                else
                    control.HandleCreated += (s, e) => SetWindowTheme(control.Handle, "DarkMode_Explorer", null);
            }
            catch (Exception)
            {
            }
        }
        #endregion

        #region --- Dialogs ---
        // Gives a regular WinForms dialog the dark look: call from its constructor
        public static void Attach(Form form)
        {
            form.HandleCreated += (s, e) => DarkTitleBar(form, Window);
            form.Load += (s, e) => ApplyDark(form);
        }

        public static void ApplyDark(Control root)
        {
            if (root is Form form)
            {
                form.BackColor = Window;
                form.ForeColor = Text;
            }

            foreach (Control control in root.Controls)
            {
                switch (control)
                {
                    case FlatButton _:
                        break;
                    case Button button:
                        button.FlatStyle = FlatStyle.Flat;
                        button.BackColor = Theme.Control;
                        button.ForeColor = Text;
                        button.FlatAppearance.BorderColor        = BorderLight;
                        button.FlatAppearance.MouseOverBackColor = ControlHot;
                        button.FlatAppearance.MouseDownBackColor = Border;
                        button.UseVisualStyleBackColor = false;
                        break;
                    case TextBoxBase text:
                        text.BackColor   = Theme.Control;
                        text.ForeColor   = Text;
                        text.BorderStyle = BorderStyle.FixedSingle;
                        break;
                    case NumericUpDown numeric:
                        numeric.BackColor   = Theme.Control;
                        numeric.ForeColor   = Text;
                        numeric.BorderStyle = BorderStyle.FixedSingle;
                        DarkSpinner(numeric);
                        break;
                    case ComboBox combo:
                        combo.FlatStyle = FlatStyle.Flat;
                        combo.BackColor = Theme.Control;
                        combo.ForeColor = Text;
                        DarkCombo(combo);
                        break;
                    case ListBox list:
                        list.BackColor   = Theme.Control;
                        list.ForeColor   = Text;
                        list.BorderStyle = BorderStyle.FixedSingle;
                        DarkScrollBars(list);
                        break;
                    case ListView view:
                        view.BackColor = Theme.Control;
                        view.ForeColor = Text;
                        DarkScrollBars(view);
                        break;
                    case CheckBox check:
                        check.ForeColor = Text;
                        check.FlatStyle = FlatStyle.Flat;
                        break;
                    case RadioButton radio:
                        radio.ForeColor = Text;
                        radio.FlatStyle = FlatStyle.Flat;
                        break;
                    case GroupBox group:
                        group.ForeColor = TextDim;
                        group.BackColor = Window;
                        break;
                    case TabPage page:
                        page.BackColor = Window;
                        page.ForeColor = Text;
                        break;
                    case Label label:
                        if (label.ForeColor == SystemColors.ControlText || label.ForeColor.IsSystemColor)
                            label.ForeColor = Text;
                        label.BackColor = Color.Transparent;
                        break;
                    case PictureBox _:
                        break;
                    case ProgressBar _:
                        break;
                    case DateTimePicker _:
                        break;
                    case Panel panel:
                        panel.BackColor = Window;
                        break;
                }

                if (control.HasChildren && !(control is NumericUpDown))
                    ApplyDark(control);
            }
        }
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Control, object> _styled =
            new System.Runtime.CompilerServices.ConditionalWeakTable<Control, object>();

        private static bool MarkStyled(Control control)
        {
            if (_styled.TryGetValue(control, out _))
                return false;

            _styled.Add(control, null);
            return true;
        }

        // Up/down buttons of a NumericUpDown: dark fill with two chevrons instead of the system arrows
        public static void DarkSpinner(NumericUpDown numeric)
        {
            if (numeric.Controls.Count == 0 || !MarkStyled(numeric))
                return;

            var buttons = numeric.Controls[0];
            buttons.Paint += (s, e) =>
            {
                var g = e.Graphics;
                int w = buttons.Width, h = buttons.Height, half = h / 2;
                var cursor = buttons.PointToClient(Cursor.Position);
                bool inside = buttons.ClientRectangle.Contains(cursor);
                bool pressed = inside && (System.Windows.Forms.Control.MouseButtons & MouseButtons.Left) != 0;

                using (var fill = new SolidBrush(Control))
                    g.FillRectangle(fill, 0, 0, w, h);

                var hot = pressed ? Border : ControlHot;
                if (inside)
                {
                    using (var brush = new SolidBrush(hot))
                        g.FillRectangle(brush, 0, cursor.Y < half ? 0 : half, w, cursor.Y < half ? half : h - half);
                }

                using (var pen = new Pen(Border))
                {
                    g.DrawLine(pen, 0, 0, 0, h);
                    g.DrawLine(pen, 0, half, w, half);
                }

                Smooth(g);
                var color = numeric.Enabled ? TextSoft : TextOff;
                Chevron(g, color, new PointF(w / 2f, half / 2f + 0.5f), true, 3f);
                Chevron(g, color, new PointF(w / 2f, half + (h - half) / 2f), false, 3f);
            };
            buttons.MouseMove  += (s, e) => buttons.Invalidate();
            buttons.MouseLeave += (s, e) => buttons.Invalidate();
            buttons.Invalidate();
        }

        // Dropdown button of a ComboBox: painted over after the system draws it
        public static void DarkCombo(ComboBox combo)
        {
            if (!MarkStyled(combo))
                return;

            var painter = new ComboPainter(combo);
            if (combo.IsHandleCreated)
                painter.AssignHandle(combo.Handle);
            combo.HandleCreated   += (s, e) => painter.AssignHandle(combo.Handle);
            combo.HandleDestroyed += (s, e) => painter.ReleaseHandle();
            combo.Invalidate();
        }

        private class ComboPainter : NativeWindow
        {
            private const int WM_PAINT = 0x000F;
            private readonly ComboBox _combo;

            public ComboPainter(ComboBox combo)
            {
                _combo = combo;
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != WM_PAINT)
                    return;

                using (var g = Graphics.FromHwnd(_combo.Handle))
                {
                    int w = _combo.Width, h = _combo.Height;
                    int arrow = SystemInformation.VerticalScrollBarWidth;
                    var button = new Rectangle(w - arrow - 1, 1, arrow, h - 2);
                    bool hot = _combo.Enabled && _combo.ClientRectangle.Contains(_combo.PointToClient(Cursor.Position));

                    using (var fill = new SolidBrush(hot || _combo.DroppedDown ? ControlHot : Control))
                        g.FillRectangle(fill, button);
                    using (var pen = new Pen(Border))
                        g.DrawLine(pen, button.Left, button.Top, button.Left, button.Bottom - 1);
                    using (var pen = new Pen(_combo.Focused ? Accent : BorderLight))
                        g.DrawRectangle(pen, 0, 0, w - 1, h - 1);

                    Smooth(g);
                    Chevron(g, _combo.Enabled ? TextSoft : TextOff, new PointF(button.Left + button.Width / 2f, h / 2f), false, 3.5f);
                }
            }
        }
        #endregion
    }

    // Colors for dark dropdown menus (ContextMenuStrip)
    public class DarkMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground   => Theme.Control;
        public override Color ImageMarginGradientBegin      => Theme.Control;
        public override Color ImageMarginGradientMiddle     => Theme.Control;
        public override Color ImageMarginGradientEnd        => Theme.Control;
        public override Color MenuBorder                    => Theme.BorderLight;
        public override Color MenuItemBorder                => Theme.AccentSoft;
        public override Color MenuItemSelected              => Theme.AccentSoft;
        public override Color MenuItemSelectedGradientBegin => Theme.AccentSoft;
        public override Color MenuItemSelectedGradientEnd   => Theme.AccentSoft;
        public override Color MenuItemPressedGradientBegin  => Theme.AccentSoft;
        public override Color MenuItemPressedGradientEnd    => Theme.AccentSoft;
        public override Color SeparatorDark                 => Theme.BorderLight;
        public override Color SeparatorLight                => Theme.Control;
        public override Color CheckBackground               => Theme.AccentSoft;
        public override Color CheckSelectedBackground       => Theme.AccentSoft;
        public override Color CheckPressedBackground        => Theme.AccentSoft;
    }

    public class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        public DarkMenuRenderer()
            : base(new DarkMenuColors())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            if (e.Item is ToolStripLabel)
                e.TextColor = Theme.TextDim;
            else if (!e.Item.Enabled)
                e.TextColor = Theme.TextOff;
            else if (e.Item.Tag as string == "danger")
                e.TextColor = Theme.Danger;
            else if (e.TextFormat.HasFlag(TextFormatFlags.Right))
                e.TextColor = Theme.TextDim; // shortcut column, used for counts and current values
            else
                e.TextColor = Theme.Text;

            base.OnRenderItemText(e);
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.TextDim;
            base.OnRenderArrow(e);
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var r = e.ImageRectangle;
            using (var pen = new Pen(Theme.Accent, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(pen, new[] { new PointF(r.Left + 3, r.Top + r.Height / 2f), new PointF(r.Left + r.Width / 2.5f, r.Bottom - 4), new PointF(r.Right - 3, r.Top + 4) });
        }

        public static ContextMenuStrip CreateMenu()
        {
            return new ContextMenuStrip
            {
                Renderer        = new DarkMenuRenderer(),
                Font            = Theme.Body,
                ShowImageMargin = false,
                ShowCheckMargin = false,
                Padding         = new Padding(4),
            };
        }

        public static ToolStripMenuItem Item(string text, EventHandler onClick = null, string value = null)
        {
            var item = new ToolStripMenuItem(text) { Padding = new Padding(4, 6, 4, 6) };
            if (onClick != null)
                item.Click += onClick;

            if (value != null)
            {
                item.ShortcutKeyDisplayString = value;
                item.ShowShortcutKeys         = true;
            }

            item.DropDown.Renderer = new DarkMenuRenderer();
            if (item.DropDown is ToolStripDropDownMenu menu)
            {
                menu.ShowImageMargin = false;
                menu.Padding         = new Padding(4);
            }

            return item;
        }

        public static ToolStripLabel Group(string text)
        {
            return new ToolStripLabel(text) { Font = Theme.Caption, Margin = new Padding(6, 6, 0, 2) };
        }
    }

    public enum ButtonKind { Secondary, Primary, Ghost, Danger }
    public enum ButtonGlyph { None, Plus, Save, Layers, Close, Folder, Calendar }

    // Flat rounded button drawn in the dark theme
    public class FlatButton : Button
    {
        private bool _hover;
        private bool _pressed;

        public ButtonKind Kind      { get; set; } = ButtonKind.Secondary;
        public ButtonGlyph Glyph    { get; set; } = ButtonGlyph.None;
        public string SubText       { get; set; } // right-aligned muted text
        public bool Chevron         { get; set; }
        public bool ChevronUp       { get; set; }
        public bool ChevronAtEdge   { get; set; } // chevron at the right edge, content stays centered
        public bool AlignLeft       { get; set; }
        public bool Highlighted     { get; set; } // accent border, e.g. while its menu is open
        public int Radius           { get; set; } = 8;
        public bool BoldText        { get; set; } = true;

        public FlatButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font      = Theme.Body;
            Cursor    = Cursors.Hand;
            BackColor = Color.Transparent;
            ForeColor = Theme.Text;
            UseVisualStyleBackColor = false;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Cursor = Enabled ? Cursors.Hand : Cursors.Default; Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent?.BackColor ?? Theme.Window);
            Theme.Smooth(g);

            Color fill, border, text;
            if (!Enabled)
            {
                fill   = Kind == ButtonKind.Ghost ? Color.Empty : Color.FromArgb(0x16, 0x19, 0x20);
                border = Kind == ButtonKind.Ghost ? Color.Empty : Theme.Border;
                text   = Theme.TextOff;
            }
            else
            {
                switch (Kind)
                {
                    case ButtonKind.Primary:
                        fill   = _pressed ? Theme.Accent : _hover ? Theme.AccentHot : Theme.Accent;
                        border = Color.Empty;
                        text   = Theme.OnAccent;
                        break;
                    case ButtonKind.Ghost:
                        fill   = _pressed ? Theme.Border : _hover ? Theme.Control : Color.Empty;
                        border = Color.Empty;
                        text   = _hover ? Theme.Text : Theme.TextSoft;
                        break;
                    case ButtonKind.Danger:
                        fill   = _pressed ? Theme.Border : _hover ? Theme.ControlHot : Theme.Control;
                        border = Theme.BorderLight;
                        text   = Theme.Danger;
                        break;
                    default:
                        fill   = _pressed ? Theme.Border : _hover ? Theme.ControlHot : Theme.Control;
                        border = Highlighted ? Theme.Accent : Theme.BorderLight;
                        text   = ForeColor;
                        break;
                }
            }

            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (fill != Color.Empty)
                Theme.FillRounded(g, fill, rect, Radius);
            if (border != Color.Empty)
                Theme.DrawRounded(g, border, rect, Radius);

            // Content: [glyph] text ... [subtext] [chevron]
            int pad     = Math.Min(14, Math.Max(8, Height / 3));
            int glyphW  = Glyph != ButtonGlyph.None ? 20 : 0;
            int chevW   = Chevron ? 16 : 0;
            var font    = BoldText ? new Font(Font, FontStyle.Bold) : Font;
            var textSize = TextRenderer.MeasureText(g, Text ?? "", font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            int subW    = string.IsNullOrEmpty(SubText) ? 0 : TextRenderer.MeasureText(g, SubText, Theme.Small, Size.Empty, TextFormatFlags.NoPadding).Width + 8;

            int x;
            if (AlignLeft)
                x = pad;
            else
                x = (Width - (glyphW + textSize.Width + subW + (ChevronAtEdge ? 0 : chevW))) / 2;

            int cy = Height / 2;
            if (Glyph != ButtonGlyph.None)
            {
                var glyphColor = Glyph == ButtonGlyph.Layers && Enabled ? Theme.Accent : text;
                DrawGlyph(g, Glyph, glyphColor, new RectangleF(x, cy - 7, 14, 14));
                x += glyphW;
            }

            int textRight = AlignLeft ? Width - pad - chevW - subW : x + textSize.Width + 2;
            Theme.DrawText(g, Text ?? "", font, text, new Rectangle(x, 0, Math.Max(0, textRight - x), Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (subW > 0)
            {
                int sx = AlignLeft ? Width - pad - chevW - subW + 8 : x + textSize.Width + 8;
                Theme.DrawText(g, SubText, Theme.Small, Enabled ? Theme.TextDim : Theme.TextOff, new Rectangle(sx, 0, subW, Height), TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);
            }

            if (Chevron)
            {
                float chx = AlignLeft || ChevronAtEdge ? Width - pad - 5 : x + textSize.Width + subW + 10;
                Theme.Chevron(g, Theme.TextDim, new PointF(chx, cy), ChevronUp);
            }

            if (font != Font)
                font.Dispose();

            if (Focused && ShowFocusCues)
                Theme.DrawRounded(g, Theme.AccentHot, new RectangleF(2, 2, Width - 5, Height - 5), Radius - 2);
        }

        public static void DrawGlyph(Graphics g, ButtonGlyph glyph, Color color, RectangleF r)
        {
            using (var pen = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                switch (glyph)
                {
                    case ButtonGlyph.Plus:
                        g.DrawLine(pen, cx, r.Top + 2, cx, r.Bottom - 2);
                        g.DrawLine(pen, r.Left + 2, cy, r.Right - 2, cy);
                        break;
                    case ButtonGlyph.Close:
                        g.DrawLine(pen, r.Left + 3, r.Top + 3, r.Right - 3, r.Bottom - 3);
                        g.DrawLine(pen, r.Right - 3, r.Top + 3, r.Left + 3, r.Bottom - 3);
                        break;
                    case ButtonGlyph.Save:
                        g.DrawPolygon(pen, new[] { new PointF(r.Left + 2, r.Top + 1), new PointF(r.Right - 4, r.Top + 1), new PointF(r.Right - 1, r.Top + 4), new PointF(r.Right - 1, r.Bottom - 1), new PointF(r.Left + 1, r.Bottom - 1), new PointF(r.Left + 1, r.Top + 1) });
                        g.DrawLines(pen, new[] { new PointF(r.Left + 4, r.Top + 1), new PointF(r.Left + 4, r.Top + 5), new PointF(r.Right - 5, r.Top + 5), new PointF(r.Right - 5, r.Top + 1) });
                        g.DrawLines(pen, new[] { new PointF(r.Left + 4, r.Bottom - 1), new PointF(r.Left + 4, cy + 1), new PointF(r.Right - 4, cy + 1), new PointF(r.Right - 4, r.Bottom - 1) });
                        break;
                    case ButtonGlyph.Layers:
                        g.DrawPolygon(pen, new[] { new PointF(r.Left + 1, cy - 3), new PointF(cx, r.Top + 1), new PointF(r.Right - 1, cy - 3), new PointF(cx, cy + 1) });
                        g.DrawLines(pen, new[] { new PointF(r.Left + 1, cy + 1), new PointF(cx, cy + 5), new PointF(r.Right - 1, cy + 1) });
                        break;
                    case ButtonGlyph.Calendar:
                        g.DrawRectangle(pen, r.Left + 1, r.Top + 2, r.Width - 2, r.Height - 3);
                        g.DrawLine(pen, r.Left + 1, r.Top + 6, r.Right - 1, r.Top + 6);
                        g.DrawLine(pen, r.Left + 4, r.Top, r.Left + 4, r.Top + 3);
                        g.DrawLine(pen, r.Right - 4, r.Top, r.Right - 4, r.Top + 3);
                        break;
                    case ButtonGlyph.Folder:
                        g.DrawPolygon(pen, new[] { new PointF(r.Left + 1, r.Top + 3), new PointF(r.Left + 5, r.Top + 3), new PointF(r.Left + 7, r.Top + 5), new PointF(r.Right - 1, r.Top + 5), new PointF(r.Right - 1, r.Bottom - 2), new PointF(r.Left + 1, r.Bottom - 2) });
                        break;
                }
            }
        }
    }

    // Rounded card with a border, the main window's content blocks
    public class CardPanel : Panel
    {
        public Color Fill        { get; set; } = Theme.Card;
        public Color BorderColor { get; set; } = Theme.Border;
        public int Radius        { get; set; } = 12;
        public bool Dashed       { get; set; }

        public CardPanel()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Card;
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Window);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            if (Fill != Color.Empty)
                Theme.FillRounded(g, Fill, rect, Radius);
            Theme.DrawRounded(g, BorderColor, rect, Radius, 1f, Dashed ? DashStyle.Dash : DashStyle.Solid);
            base.OnPaint(e);
        }
    }

    // Small value tile: caption above a monospace value
    public class StatTile : CardPanel
    {
        private string _caption = "";
        private string _value   = "";

        public string Caption { get => _caption; set { _caption = value; Invalidate(); } }
        public string Value   { get => _value;   set { _value = value; Invalidate(); } }

        public StatTile()
        {
            Fill      = Theme.Inset;
            BackColor = Theme.Inset;
            Radius    = 8;
            Height    = 62;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            Theme.DrawText(g, _caption, Theme.Caption, Theme.TextDim, new Rectangle(14, 10, Width - 20, 16), TextFormatFlags.Left);
            Theme.DrawText(g, _value, Theme.MonoLarge, Theme.Text, new Rectangle(13, 30, Width - 20, 22), TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    // Rounded text box frame around a borderless TextBox, used for the search field
    public class SearchBox : Panel
    {
        public TextBox Input { get; }

        public SearchBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(0x17, 0x1a, 0x21);
            Input = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor   = BackColor,
                ForeColor   = Theme.Text,
                Font        = Theme.Body,
            };
            Controls.Add(Input);
            Cursor = Cursors.IBeam;
            Click += (s, e) => Input.Focus();
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Input.SetBounds(34, (Height - Input.PreferredHeight) / 2 + 1, Width - 46, Input.PreferredHeight);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(Parent?.BackColor ?? Theme.Header);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Theme.Smooth(g);
            var rect = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Theme.FillRounded(g, BackColor, rect, Height / 2f);
            Theme.DrawRounded(g, Input.Focused ? Theme.Accent : Theme.BorderLight, rect, Height / 2f);
            using (var pen = new Pen(Theme.TextDim, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                float cy = Height / 2f;
                g.DrawEllipse(pen, 13, cy - 6, 9, 9);
                g.DrawLine(pen, 20.5f, cy + 1.5f, 24, cy + 5);
            }
        }
    }
}
