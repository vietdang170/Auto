using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// Shared look for Auto, Auto Paste and Auto Click: light grey page, white rounded cards, icon headings.
namespace VietDang.Design
{
    internal static class Theme
    {
        internal static readonly Color Window = Color.FromArgb(243, 244, 246);
        internal static readonly Color Surface = Color.White;
        internal static readonly Color Subtle = Color.FromArgb(248, 250, 252);
        internal static readonly Color Border = Color.FromArgb(226, 232, 240);
        internal static readonly Color Strong = Color.FromArgb(203, 213, 225);
        internal static readonly Color Divider = Color.FromArgb(238, 242, 246);
        internal static readonly Color Text = Color.FromArgb(15, 23, 42);
        internal static readonly Color Body = Color.FromArgb(51, 65, 85);
        internal static readonly Color Muted = Color.FromArgb(100, 116, 139);
        internal static readonly Color Faint = Color.FromArgb(148, 163, 184);
        internal static readonly Color Blue = Color.FromArgb(37, 99, 235);
        internal static readonly Color BlueDark = Color.FromArgb(29, 78, 216);
        internal static readonly Color BlueSoft = Color.FromArgb(239, 246, 255);
        internal static readonly Color BlueTint = Color.FromArgb(219, 234, 254);
        internal static readonly Color BlueLine = Color.FromArgb(191, 219, 254);
        internal static readonly Color Navy = Color.FromArgb(30, 64, 175);
        internal static readonly Color Green = Color.FromArgb(21, 128, 61);
        internal static readonly Color GreenDark = Color.FromArgb(22, 101, 52);
        internal static readonly Color GreenBright = Color.FromArgb(22, 163, 74);
        internal static readonly Color GreenSoft = Color.FromArgb(240, 253, 244);
        internal static readonly Color GreenLine = Color.FromArgb(187, 247, 208);
        internal static readonly Color Red = Color.FromArgb(220, 38, 38);
        internal static readonly Color RedSoft = Color.FromArgb(254, 242, 242);
        internal static readonly Color RedLine = Color.FromArgb(254, 202, 202);
        internal static readonly Color Amber = Color.FromArgb(180, 83, 9);
        internal static readonly Color AmberSoft = Color.FromArgb(255, 251, 235);
        internal static readonly Color AmberLine = Color.FromArgb(253, 230, 138);

        internal static readonly Font Regular = new Font("Segoe UI", 9f);
        internal static readonly Font Semibold = new Font("Segoe UI Semibold", 9f);
        internal static readonly Font Small = new Font("Segoe UI", 8.25f);
        internal static readonly Font SmallBold = new Font("Segoe UI Semibold", 8.25f);
        internal static readonly Font Caps = new Font("Segoe UI Semibold", 7.75f);
        internal static readonly Font CardTitle = new Font("Segoe UI Semibold", 10f);
        internal static readonly Font PageTitle = new Font("Segoe UI Semibold", 13f);
        internal static readonly Font Status = new Font("Segoe UI Semibold", 13f);
        internal static readonly Font Mono = new Font("Consolas", 9.5f);
        internal static readonly Font MonoBold = new Font("Consolas", 9.5f, FontStyle.Bold);
        internal static readonly Font MonoSmall = new Font("Consolas", 8.25f);

        private static readonly Dictionary<float, Font> Icons = new Dictionary<float, Font>();
        private static string _iconFamily;

        // Windows 11 ships Segoe Fluent Icons, Windows 10 Segoe MDL2 Assets; both use the same code points.
        internal static Font Icon(float size)
        {
            Font font;
            if (Icons.TryGetValue(size, out font)) return font;
            if (_iconFamily == null)
            {
                _iconFamily = "Segoe UI Symbol";
                foreach (string name in new string[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
                {
                    using (Font probe = new Font(name, 9f))
                        if (probe.Name == name) { _iconFamily = name; break; }
                }
            }
            font = new Font(_iconFamily, size);
            Icons[size] = font;
            return font;
        }

        internal static float Scale(Graphics g) { return g.DpiX / 96f; }

        // The colour a control should clear its corners to: the nearest opaque parent background.
        internal static Color Behind(Control control)
        {
            for (Control parent = control.Parent; parent != null; parent = parent.Parent)
                if (parent.BackColor.A == 255) return parent.BackColor;
            return Window;
        }

        internal static GraphicsPath Round(RectangleF r, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0) { path.AddRectangle(r); return path; }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void Box(Graphics g, Rectangle bounds, float radius, Color fill, Color line, float width = 1f)
        {
            if (bounds.Width <= 1 || bounds.Height <= 1) return;
            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF r = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1f, bounds.Height - 1f);
            using (GraphicsPath path = Round(r, radius))
            {
                if (!fill.IsEmpty) using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (!line.IsEmpty) using (Pen pen = new Pen(line, width)) g.DrawPath(pen, path);
            }
            g.SmoothingMode = previous;
        }

        internal static Color Mix(Color from, Color to, float amount)
        {
            if (amount <= 0f) return from;
            if (amount >= 1f) return to;
            return Color.FromArgb(
                (int)Math.Round(from.A + (to.A - from.A) * amount),
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        // Smoothstep: starts and ends gently, so short transitions do not look mechanical.
        internal static float Ease(float t) { return t * t * (3f - 2f * t); }

        internal const TextFormatFlags Line = TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix;
        internal const TextFormatFlags Centered = Line | TextFormatFlags.HorizontalCenter;
    }

    // Drives one 0..1 value towards a target over a short time and repaints its control on every step.
    // Hidden controls, controls without a window yet, and systems with UI effects turned off jump straight to the end.
    internal sealed class Fader : IDisposable
    {
        private readonly Control _owner;
        private readonly System.Windows.Forms.Timer _timer = new System.Windows.Forms.Timer { Interval = 15 };
        private readonly float _step;
        private float _target;
        internal float Value;
        internal Fader(Control owner, int milliseconds)
        {
            _owner = owner;
            _step = 15f / Math.Max(15, milliseconds);
            _timer.Tick += delegate
            {
                Value = Value < _target ? Math.Min(_target, Value + _step) : Math.Max(_target, Value - _step);
                if (Value == _target) _timer.Stop();
                _owner.Invalidate();
            };
        }
        internal float Eased { get { return Theme.Ease(Value); } }
        internal void To(float target)
        {
            _target = target;
            if (!_owner.IsHandleCreated || !_owner.Visible || !SystemInformation.UIEffectsEnabled)
            {
                _timer.Stop();
                Value = target;
                _owner.Invalidate();
            }
            else if (Value != target) _timer.Start();
        }
        public void Dispose() { _timer.Dispose(); }
    }

    internal static class Glyph
    {
        internal const string Mouse = "", Paste = "", Settings = "", List = "", Timer = "";
        internal const string Keyboard = "", Add = "", Edit = "", Delete = "", Copy = "";
        internal const string Rename = "", Up = "", Down = "", Info = "", Play = "";
        internal const string Pause = "", Stop = "", Clear = "", Target = "", Checklist = "";
        internal const string Apps = "", App = "", OpenFile = "", Flash = "", Power = "";
        internal const string Sync = "", Save = "", Folder = "", Shield = "", Repeat = "";
    }

    internal static class Kit
    {
        // Top card of every page: tinted icon, page title and a one-line description; actions go on the right.
        internal static Card PageHeader(string glyph, string title, string subtitle)
        {
            Card card = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            IconTile tile = new IconTile(glyph, Theme.BlueSoft, Theme.Blue, 14f) { Location = new Point(10, 9) };
            Label heading = new Label { Text = title, Font = Theme.PageTitle, ForeColor = Theme.Text, AutoEllipsis = true, UseMnemonic = false, Location = new Point(60, 6), Size = new Size(400, 25) };
            Label caption = new Label { Text = subtitle, Font = Theme.Small, ForeColor = Theme.Muted, AutoEllipsis = true, UseMnemonic = false, Location = new Point(61, 32), Size = new Size(400, 17) };
            // Without a description the title sits in the middle of the card.
            if (string.IsNullOrEmpty(subtitle)) { heading.Top = 17; caption.Visible = false; }
            card.Controls.Add(tile); card.Controls.Add(heading); card.Controls.Add(caption);
            card.FitBeforeHeader(heading); card.FitBeforeHeader(caption);
            return card;
        }

        internal static Label FieldLabel(string text)
        {
            return new Label { Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = Theme.Small, ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.BottomLeft, UseMnemonic = false };
        }

        internal static Label Note(string text)
        {
            return new Label { Text = text, AutoSize = true, Font = Theme.Small, ForeColor = Theme.Muted, UseMnemonic = false };
        }
    }

    internal abstract class Painted : Control
    {
        protected Painted()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Margin = Padding.Empty;
        }
    }

    // White rounded card. With a title it draws an icon heading and a divider; header controls line up on the right.
    internal class Card : Panel
    {
        internal const int HeaderHeight = 44;
        private readonly List<Control> _header = new List<Control>();
        private readonly List<Control> _fit = new List<Control>();
        private readonly string _glyph, _title;
        private Control _badge;

        internal Card() : this(null, null) { }
        internal Card(string glyph, string title)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _glyph = glyph; _title = title;
            BackColor = Theme.Surface;
            Margin = Padding.Empty;
            Padding = title == null ? new Padding(14, 8, 14, 8) : new Padding(14, HeaderHeight + 10, 14, 12);
        }
        internal void AddHeader(Control control) { _header.Add(control); Controls.Add(control); }
        internal void AddTitleBadge(Control control) { _badge = control; Controls.Add(control); }
        internal void FitBeforeHeader(Control control) { _fit.Add(control); }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int band = _title == null ? ClientSize.Height : HeaderHeight, x = ClientSize.Width - 14;
            foreach (Control control in _header)
            {
                x -= control.Width;
                control.Location = new Point(x, Math.Max(0, (band - control.Height) / 2));
                x -= 6;
            }
            if (_badge != null && _title != null)
            {
                int titleWidth = TextRenderer.MeasureText(_title, Theme.CardTitle, Size.Empty, Theme.Line).Width;
                _badge.Location = new Point(40 + titleWidth + 10, (HeaderHeight - _badge.Height) / 2);
            }
            foreach (Control control in _fit) control.Width = Math.Max(0, x - 4 - control.Left);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            Theme.Box(g, ClientRectangle, 8f * Theme.Scale(g), BackColor, Theme.Border);
            if (_title == null) return;
            TextRenderer.DrawText(g, _glyph ?? "", Theme.Icon(11f), new Rectangle(14, 0, 22, HeaderHeight), Theme.Body, Theme.Line);
            TextRenderer.DrawText(g, _title, Theme.CardTitle, new Rectangle(40, 0, Math.Max(0, ClientSize.Width - 54), HeaderHeight), Theme.Text, Theme.Line | TextFormatFlags.EndEllipsis);
            using (Pen line = new Pen(Theme.Divider)) g.DrawLine(line, 1, HeaderHeight, ClientSize.Width - 2, HeaderHeight);
        }
    }

    internal enum ButtonKind { Secondary, Primary, Success, Danger }

    // Rounded button with an optional icon; sizes itself to its text unless AutoFit is turned off.
    internal sealed class UiButton : Button
    {
        private ButtonKind _kind;
        private string _glyph;
        private bool _autoFit = true;
        private int _minimum;
        private readonly Fader _hover;

        internal UiButton(string text, string glyph, ButtonKind kind)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _glyph = glyph; _kind = kind;
            _hover = new Fader(this, 110);
            FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; UseVisualStyleBackColor = false;
            Font = Theme.Semibold; Cursor = Cursors.Hand; Height = 30; Margin = Padding.Empty;
            Text = text;
            if (string.IsNullOrEmpty(text)) AccessibleName = glyph;
            Fit();
        }
        internal bool AutoFit { get { return _autoFit; } set { _autoFit = value; Fit(); } }
        internal int MinimumFit { get { return _minimum; } set { _minimum = value; Fit(); } }
        internal ButtonKind Kind { get { return _kind; } set { _kind = value; Invalidate(); } }
        internal string GlyphText { get { return _glyph; } set { _glyph = value; Fit(); Invalidate(); } }
        private void Fit()
        {
            if (!_autoFit) return;
            bool hasText = !string.IsNullOrEmpty(Text), hasGlyph = !string.IsNullOrEmpty(_glyph);
            int width = !hasText ? Height + 2 : 22 + TextRenderer.MeasureText(Text, Font, Size.Empty, Theme.Line).Width + (hasGlyph ? 22 : 0);
            Width = Math.Max(_minimum, width);
        }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover.To(1f); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover.To(0f); }
        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (!Visible && _hover != null) _hover.To(0f); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled && _hover != null) _hover.To(0f); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) _hover.Dispose(); base.Dispose(disposing); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            Color back, line, fore;
            float hover = _hover.Eased;
            if (!Enabled) { back = Theme.Subtle; line = Theme.Border; fore = Theme.Faint; }
            else if (_kind == ButtonKind.Primary) { back = Theme.Mix(Theme.Blue, Theme.BlueDark, hover); line = back; fore = Color.White; }
            else if (_kind == ButtonKind.Success) { back = Theme.Mix(Theme.Green, Theme.GreenDark, hover); line = back; fore = Color.White; }
            else if (_kind == ButtonKind.Danger) { back = Theme.Mix(Theme.Surface, Theme.RedSoft, hover); line = Theme.RedLine; fore = Theme.Red; }
            else { back = Theme.Mix(Theme.Surface, Theme.Subtle, hover); line = Theme.Mix(Theme.Border, Theme.Strong, hover); fore = Theme.Body; }
            bool focus = Focused && ShowFocusCues && Enabled;
            float scale = Theme.Scale(g);
            Theme.Box(g, ClientRectangle, 6f * scale, back, focus ? Theme.Blue : line, focus ? 1.6f : 1f);
            int textWidth = string.IsNullOrEmpty(Text) ? 0 : TextRenderer.MeasureText(g, Text, Font, Size.Empty, Theme.Line).Width;
            int glyphWidth = string.IsNullOrEmpty(_glyph) ? 0 : (int)(16 * scale);
            int gap = textWidth > 0 && glyphWidth > 0 ? (int)(6 * scale) : 0;
            int x = (ClientSize.Width - textWidth - glyphWidth - gap) / 2;
            if (glyphWidth > 0) TextRenderer.DrawText(g, _glyph, Theme.Icon(Font.SizeInPoints), new Rectangle(x, 0, glyphWidth, ClientSize.Height), fore, Theme.Centered);
            if (textWidth > 0) TextRenderer.DrawText(g, Text, Font, new Rectangle(x + glyphWidth + gap, 0, textWidth + 2, ClientSize.Height), fore, Theme.Line);
        }
    }

    // Small rounded label: counts, states and requirements. Color.Empty background gives a plain dot + text.
    internal sealed class Pill : Painted
    {
        private Color _back = Theme.Divider, _line = Theme.Border;
        private bool _dot;
        internal Pill() { Font = Theme.Small; ForeColor = Theme.Body; Height = 22; }
        internal bool Dot { get { return _dot; } set { _dot = value; Fit(); Invalidate(); } }
        internal void SetColors(Color back, Color line, Color fore) { _back = back; _line = line; ForeColor = fore; Invalidate(); }
        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Fit(); Invalidate(); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); Fit(); Invalidate(); }
        private void Fit()
        {
            Size text = TextRenderer.MeasureText(Text ?? "", Font, Size.Empty, Theme.Line);
            Size = new Size(text.Width + 18 + (_dot ? 13 : 0), Math.Max(22, text.Height + 8));
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            float scale = Theme.Scale(g);
            Theme.Box(g, ClientRectangle, 5f * scale, _back, _line);
            int x = (int)(9 * scale);
            if (_dot)
            {
                int d = (int)(7 * scale);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush dot = new SolidBrush(ForeColor)) g.FillEllipse(dot, x, (ClientSize.Height - d) / 2, d, d);
                x += (int)(13 * scale);
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(x, 0, Math.Max(0, ClientSize.Width - x), ClientSize.Height), ForeColor, Theme.Line);
        }
    }

    internal sealed class IconTile : Painted
    {
        private readonly string _glyph;
        private readonly Color _back;
        private readonly float _size;
        internal IconTile(string glyph, Color back, Color fore, float size)
        {
            _glyph = glyph; _back = back; _size = size; ForeColor = fore; Size = new Size(40, 40);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            Theme.Box(g, ClientRectangle, 8f * Theme.Scale(g), _back, Color.Empty);
            TextRenderer.DrawText(g, _glyph, Theme.Icon(_size), ClientRectangle, ForeColor, Theme.Centered);
        }
    }

    // Grey hint strip with an info icon.
    internal sealed class InfoNote : Painted
    {
        private readonly string _glyph;
        internal InfoNote(string glyph, string text)
        {
            _glyph = glyph; Font = Theme.Small; ForeColor = Theme.Body; Text = text; Height = 30;
            Width = TextRenderer.MeasureText(text, Font, Size.Empty, Theme.Line).Width + 46;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            float scale = Theme.Scale(g);
            Theme.Box(g, ClientRectangle, 6f * scale, Theme.Subtle, Theme.Border);
            int icon = (int)(34 * scale);
            TextRenderer.DrawText(g, _glyph, Theme.Icon(10f), new Rectangle(0, 0, icon, ClientSize.Height), Theme.Blue, Theme.Centered);
            TextRenderer.DrawText(g, Text, Font, new Rectangle(icon, 0, Math.Max(0, ClientSize.Width - icon - (int)(8 * scale)), ClientSize.Height), ForeColor, Theme.Line | TextFormatFlags.EndEllipsis);
        }
    }

    // A radio choice drawn as a selectable row; extra inputs sit on its right. Tiles in one group exclude each other.
    internal sealed class OptionTile : Panel
    {
        internal readonly RadioButton Radio;
        private readonly List<Control> _right = new List<Control>();
        private OptionTile[] _group;
        internal OptionTile(RadioButton radio)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Radio = radio;
            radio.AutoSize = true; radio.Cursor = Cursors.Hand; radio.Margin = Padding.Empty;
            Controls.Add(radio);
            Cursor = Cursors.Hand; Margin = Padding.Empty;
            radio.CheckedChanged += delegate
            {
                Restyle();
                if (radio.Checked && _group != null)
                    foreach (OptionTile tile in _group) if (tile != this) tile.Radio.Checked = false;
            };
            Restyle();
        }
        internal static void Group(params OptionTile[] tiles) { foreach (OptionTile tile in tiles) tile._group = tiles; }
        internal void AddRight(Control control) { _right.Add(control); Controls.Add(control); }
        protected override void OnClick(EventArgs e) { base.OnClick(e); if (Radio.Enabled) { Radio.Checked = true; Radio.Focus(); } }
        private void Restyle()
        {
            bool on = Radio.Checked;
            BackColor = on ? Theme.BlueSoft : Theme.Subtle;
            Radio.ForeColor = on ? Theme.Blue : Theme.Body;
            Radio.Font = on ? Theme.Semibold : Theme.Regular;
            Invalidate();
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Radio.Location = new Point(12, (ClientSize.Height - Radio.Height) / 2);
            int x = ClientSize.Width - 10;
            foreach (Control control in _right)
            {
                x -= control.Width;
                control.Location = new Point(x, (ClientSize.Height - control.Height) / 2);
                x -= 6;
            }
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            Theme.Box(g, ClientRectangle, 6f * Theme.Scale(g), BackColor, Radio.Checked ? Theme.Blue : Theme.Border);
        }
    }

    // Rounded single-line field around a borderless TextBox, with an optional leading icon.
    internal sealed class FieldBox : Panel
    {
        private readonly Control _inner;
        private readonly string _glyph;
        private readonly Color _glyphColor;
        private bool _focus;
        internal FieldBox(Control inner, string glyph, Color glyphColor)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _inner = inner; _glyph = glyph; _glyphColor = glyphColor;
            BackColor = Theme.Surface; Cursor = Cursors.IBeam; Margin = Padding.Empty;
            TextBox box = inner as TextBox;
            if (box != null) box.BorderStyle = BorderStyle.None;
            Controls.Add(inner);
            inner.GotFocus += delegate { _focus = true; Invalidate(); };
            inner.LostFocus += delegate { _focus = false; Invalidate(); };
            Click += delegate { _inner.Focus(); };
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int left = string.IsNullOrEmpty(_glyph) ? 10 : 32;
            _inner.SetBounds(left, (ClientSize.Height - _inner.Height) / 2, Math.Max(0, ClientSize.Width - left - 10), _inner.Height);
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            float scale = Theme.Scale(g);
            Theme.Box(g, ClientRectangle, 6f * scale, BackColor, _focus ? Theme.Blue : Theme.Strong, _focus ? 1.6f : 1f);
            if (!string.IsNullOrEmpty(_glyph))
                TextRenderer.DrawText(g, _glyph, Theme.Icon(8f), new Rectangle(0, 0, (int)(30 * scale), ClientSize.Height), _glyphColor, Theme.Centered);
        }
    }

    // Rounded frame for lists.
    internal sealed class Frame : Panel
    {
        internal Frame()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Surface; Padding = new Padding(2); Margin = Padding.Empty;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            Theme.Box(g, ClientRectangle, 6f * Theme.Scale(g), BackColor, Theme.Strong);
        }
    }

    internal sealed class ToggleSwitch : CheckBox
    {
        private readonly Fader _slide;
        internal ToggleSwitch()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _slide = new Fader(this, 140);
            AutoSize = false; Size = new Size(40, 22); Cursor = Cursors.Hand; Text = ""; Margin = Padding.Empty;
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); _slide.To(Checked ? 1f : 0f); }
        protected override void Dispose(bool disposing) { if (disposing) _slide.Dispose(); base.Dispose(disposing); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            float scale = Theme.Scale(g);
            Rectangle track = ClientRectangle;
            float on = _slide.Eased;
            Color fill = !Enabled ? Theme.Border : Theme.Mix(Theme.Strong, Theme.GreenBright, on);
            Theme.Box(g, track, track.Height / 2f, fill, Focused && ShowFocusCues ? Theme.Blue : Color.Empty, 1.6f);
            int pad = (int)(3 * scale), d = track.Height - 2 * pad;
            int x = pad + (int)Math.Round((track.Width - 2 * pad - d) * on);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush knob = new SolidBrush(Color.White)) g.FillEllipse(knob, x, pad, d, d);
        }
    }

    // Sidebar entry: icon + label, blue highlight when selected. Radio semantics give keyboard navigation.
    internal sealed class NavItem : RadioButton
    {
        private readonly string _glyph;
        private readonly Fader _hover, _select;
        internal NavItem(string text, string glyph)
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _hover = new Fader(this, 110); _select = new Fader(this, 150);
            _glyph = glyph; Text = text; AutoSize = false; Height = 38; Cursor = Cursors.Hand; Font = Theme.Regular; Margin = Padding.Empty;
        }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); _hover.To(1f); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover.To(0f); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); _select.To(Checked ? 1f : 0f); }
        protected override void Dispose(bool disposing) { if (disposing) { _hover.Dispose(); _select.Dispose(); } base.Dispose(disposing); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            float scale = Theme.Scale(g);
            Rectangle box = ClientRectangle;
            float selected = _select.Eased;
            Color back = Theme.Mix(Theme.Mix(Theme.Surface, Theme.Subtle, _hover.Eased), Theme.BlueSoft, selected);
            if (back.ToArgb() != Theme.Surface.ToArgb()) Theme.Box(g, box, 7f * scale, back, Color.Empty);
            if (Focused && ShowFocusCues) Theme.Box(g, box, 7f * scale, Color.Empty, Theme.BlueLine);
            TextRenderer.DrawText(g, _glyph, Theme.Icon(11f), new Rectangle((int)(10 * scale), 0, (int)(24 * scale), box.Height), Theme.Mix(Theme.Muted, Theme.Blue, selected), Theme.Centered);
            TextRenderer.DrawText(g, Text, Checked ? Theme.Semibold : Theme.Regular, new Rectangle((int)(42 * scale), 0, Math.Max(0, box.Width - (int)(46 * scale)), box.Height),
                Theme.Mix(Theme.Body, Theme.Blue, selected), Theme.Line | TextFormatFlags.EndEllipsis);
        }
    }
}
