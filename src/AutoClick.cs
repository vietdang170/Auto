using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using VietDang.Design;

namespace VietDang.AutoClick
{
    internal static class Program
    {
        internal static readonly int ShowMessage = RegisterWindowMessage("VietDang_AutoClick_Show_2026");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
        [STAThread]
        private static void Main()
        {
            bool first;
            using (Mutex mutex = new Mutex(true, "VietDang_AutoClick_2026", out first))
            {
                if (!first) { PostMessage(new IntPtr(0xFFFF), ShowMessage, IntPtr.Zero, IntPtr.Zero); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private const int StartId = 18001, StopId = 18002;
        private readonly AppSettings _settings;
        private readonly string _settingsPath;
        private readonly bool _embedded;
        private bool _initialized;
        internal event Action ShowRequested;
        internal event Action HideRequested;
        internal event Action ExitRequested;
        internal event Action<bool> RunningChanged;
        private readonly ClickRunner _runner = new ClickRunner(new WindowsMouse());
        private readonly Panel _editor = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        private readonly ComboBox _scenarios = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 164, Font = Theme.Regular };
        private readonly DataGridView _steps = new StepGrid();
        private readonly RadioButton _byCount = new RadioButton { Text = "Theo số lượt", AutoSize = true };
        private readonly RadioButton _byTime = new RadioButton { Text = "Theo thời gian", AutoSize = true };
        private readonly RadioButton _untilStopped = new RadioButton { Text = "Lặp liên tục cho đến khi bấm dừng", AutoSize = true };
        private readonly NumericUpDown _count = new NumericUpDown { Minimum = 1, Maximum = 1000000, Value = 1, Width = 84, TextAlign = HorizontalAlignment.Right, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono };
        private readonly NumericUpDown _duration = new NumericUpDown { Minimum = 1, Maximum = 86400, Value = 60, Width = 72, TextAlign = HorizontalAlignment.Right, BorderStyle = BorderStyle.FixedSingle, Font = Theme.Mono };
        private readonly ComboBox _durationUnit = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 70 };
        private readonly TextBox _startKeyBox = new TextBox { ReadOnly = true, BackColor = Theme.Surface, ShortcutsEnabled = false, Font = Theme.MonoBold, ForeColor = Theme.Text };
        private readonly TextBox _stopKeyBox = new TextBox { ReadOnly = true, BackColor = Theme.Surface, ShortcutsEnabled = false, Font = Theme.MonoBold, ForeColor = Theme.Text };
        private readonly Label _keyStatus = new Label { Dock = DockStyle.Fill, ForeColor = Ui.Muted, Margin = Padding.Empty, Font = Theme.Small, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
        private readonly Label _status = new Label { AutoEllipsis = true, Font = Theme.Small, ForeColor = Theme.Muted, UseMnemonic = false };
        private readonly Label _summary = new Label { AutoEllipsis = true, Font = Theme.CardTitle, ForeColor = Theme.Text, UseMnemonic = false };
        private readonly UiButton _start = new UiButton("BẮT ĐẦU CHẠY", Glyph.Play, ButtonKind.Success) { Height = 40, MinimumFit = 200 };
        private readonly UiButton _trial = new UiButton("Chạy thử 1 lần", Glyph.Repeat, ButtonKind.Secondary) { Height = 40 };
        private readonly Pill _countPill = new Pill { Font = Theme.MonoSmall };
        private readonly Pill _readyPill = new Pill { Dot = true };
        private readonly Pill _keysPill = new Pill();
        private int _hoverRow = -1;
        private readonly System.Windows.Forms.Timer _progressTimer = new System.Windows.Forms.Timer { Interval = 150 };
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _trayMenu = new ContextMenuStrip();
        private readonly Icon _appIcon;
        private Keys _startCandidate, _stopCandidate, _registeredStart, _registeredStop;
        private bool _loading, _keysReady, _modal, _threadModal, _exit, _runActive, _restoreAfterRun, _disposed;
        private CoordinatePicker _picker;
        private long _loops;
        private int _stepIndex;
        private Scenario _runningScenario;
        private string _loadError;
        private string _loadWarning;
        private bool _dirty;

        internal MainForm() : this(null) { }
        internal MainForm(AppSettings initial, string settingsPath = null, bool embedded = false)
        {
            _settingsPath = settingsPath;
            _embedded = embedded;
            try { _settings = initial ?? SettingsStore.Load(out _loadWarning, _settingsPath); _settings.Validate(); }
            catch (Exception ex) { _settings = new AppSettings(); _loadError = ex.Message; }
            Text = "AutoClick";
            ClientSize = new Size(840, _embedded ? 620 : 642);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = Theme.Regular;
            BackColor = Theme.Window;
            _appIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            Icon = _appIcon;
            BuildLayout();
            _startCandidate = (Keys)_settings.StartKey;
            _stopCandidate = (Keys)_settings.StopKey;
            UpdateKeyText();
            UpdateKeysUi();
            RefreshScenarios(_settings.SelectedIndex);
            _runner.Progress += delegate(long loops, int step) { Post(delegate { _loops = loops; _stepIndex = step; }); };
            _runner.Finished += delegate(RunResult result) { Post(delegate { FinishRun(result); }); };
            _progressTimer.Tick += delegate { UpdateProgress(); };
            _trayMenu.Items.Add("Mở AutoClick", null, delegate { ShowApp(); });
            _trayMenu.Items.Add("Dừng ngay", null, delegate { StopRun(); });
            _trayMenu.Items.Add("Thoát hẳn", null, delegate { ExitApp(); });
            _tray = new NotifyIcon { Icon = _appIcon, Visible = !embedded, Text = "AutoClick: Sẵn sàng", ContextMenuStrip = _trayMenu };
            _tray.DoubleClick += delegate { ShowApp(); };
            // Any dialog on this thread (message boxes, file pickers, the other module's dialogs) blocks the Start key.
            Application.EnterThreadModal += EnterModal;
            Application.LeaveThreadModal += LeaveModal;
            Dpi.Fit(this);
        }
        private void EnterModal(object sender, EventArgs e) { _threadModal = true; }
        private void LeaveModal(object sender, EventArgs e) { _threadModal = false; }

        private Scenario Selected { get { return _scenarios.SelectedItem as Scenario; } }
        private void BuildLayout()
        {
            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 1, RowCount = 3, BackColor = Theme.Window };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, _embedded ? 0 : 22));
            Controls.Add(root);
            root.Controls.Add(_editor, 0, 0);
            TableLayoutPanel edit = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
            edit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            edit.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            edit.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            edit.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            edit.RowStyles.Add(new RowStyle(SizeType.Absolute, 186));
            _editor.Controls.Add(edit);

            // Page header: what this tab does, plus the scenario picker and its actions.
            Card header = Kit.PageHeader(Glyph.Mouse, "Tự động nhấp chuột", "AutoClick · Theo tọa độ và kịch bản");
            UiButton add = new UiButton("Thêm", Glyph.Add, ButtonKind.Secondary), duplicate = new UiButton("Nhân bản", Glyph.Copy, ButtonKind.Secondary);
            UiButton rename = new UiButton("Đổi tên", Glyph.Rename, ButtonKind.Secondary), delete = new UiButton("Xóa", Glyph.Delete, ButtonKind.Danger);
            header.AddHeader(delete); header.AddHeader(rename); header.AddHeader(duplicate); header.AddHeader(add); header.AddHeader(_scenarios);
            edit.Controls.Add(header, 0, 0); edit.SetColumnSpan(header, 2);
            add.Click += delegate { CreateScenario(); };
            duplicate.Click += delegate { DuplicateScenario(); };
            rename.Click += delegate { RenameScenario(); };
            delete.Click += delegate { DeleteScenario(); };
            _scenarios.SelectedIndexChanged += delegate { SelectScenario(); };

            // Coordinate list: the grid runs edge to edge under the card heading, actions in a footer strip.
            Card list = new Card(Glyph.List, "Danh sách tọa độ theo thứ tự thực thi") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            list.Padding = new Padding(1, Card.HeaderHeight + 1, 1, 6);
            list.AddTitleBadge(_countPill);
            list.AddHeader(_readyPill);
            BuildGrid();
            Panel footer = new Panel { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(12, 9, 12, 7) };
            footer.Paint += delegate(object sender, PaintEventArgs e) { using (Pen line = new Pen(Theme.Divider)) e.Graphics.DrawLine(line, 0, 0, footer.Width, 0); };
            FlowLayoutPanel actions = new FlowLayoutPanel { Dock = DockStyle.Left, AutoSize = true, WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty };
            UiButton addPoint = new UiButton("Thêm tọa độ", Glyph.Target, ButtonKind.Primary) { Height = 32, Margin = new Padding(0, 0, 6, 0) };
            UiButton clear = new UiButton("Xóa toàn bộ", Glyph.Clear, ButtonKind.Secondary) { Height = 32, Margin = new Padding(0, 0, 6, 0) };
            UiButton up = new UiButton("", Glyph.Up, ButtonKind.Secondary) { Height = 32, Margin = new Padding(0, 0, 4, 0), AccessibleName = "Đưa tọa độ lên" };
            UiButton down = new UiButton("", Glyph.Down, ButtonKind.Secondary) { Height = 32, Margin = Padding.Empty, AccessibleName = "Đưa tọa độ xuống" };
            ToolTip tips = new ToolTip();
            tips.SetToolTip(up, "Đưa tọa độ đang chọn lên trên"); tips.SetToolTip(down, "Đưa tọa độ đang chọn xuống dưới");
            actions.Controls.AddRange(new Control[] { addPoint, clear, up, down });
            InfoNote hint = new InfoNote(Glyph.Info, "Con trỏ chữ thập → nhấp trái để lưu tọa độ; bấm Esc để hủy.") { Dock = DockStyle.Right };
            footer.Controls.Add(hint); footer.Controls.Add(actions);
            list.Controls.Add(_steps); list.Controls.Add(footer);
            edit.Controls.Add(list, 0, 1); edit.SetColumnSpan(list, 2);
            addPoint.Click += delegate { PickPoint(); };
            clear.Click += delegate { ClearSteps(); };
            up.Click += delegate { MoveStep(-1); };
            down.Click += delegate { MoveStep(1); };

            // Stop condition: three selectable rows, each with its own inputs.
            Card repeat = new Card(Glyph.Timer, "Điều kiện dừng kịch bản") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 5, 0) };
            repeat.AddHeader(Kit.Note("Tùy chọn vòng lặp"));
            OptionTile countTile = new OptionTile(_byCount) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };
            OptionTile timeTile = new OptionTile(_byTime) { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 5) };
            OptionTile foreverTile = new OptionTile(_untilStopped) { Dock = DockStyle.Fill };
            OptionTile.Group(countTile, timeTile, foreverTile);
            countTile.AddRight(Kit.Note("lần")); countTile.AddRight(_count);
            _durationUnit.Items.AddRange(new object[] { "Giây", "Phút", "Giờ" });
            _durationUnit.SelectedIndex = 0;
            timeTile.AddRight(_durationUnit); timeTile.AddRight(_duration);
            foreverTile.AddRight(new Label { Text = "∞", AutoSize = true, Font = Theme.CardTitle, ForeColor = Theme.Muted });
            TableLayoutPanel tiles = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 3; i++) tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 33.33f));
            tiles.Controls.Add(countTile, 0, 0); tiles.Controls.Add(timeTile, 0, 1); tiles.Controls.Add(foreverTile, 0, 2);
            repeat.Controls.Add(tiles);
            edit.Controls.Add(repeat, 0, 2);
            _byCount.CheckedChanged += delegate { UpdateRepeat(); };
            _byTime.CheckedChanged += delegate { UpdateRepeat(); };
            _untilStopped.CheckedChanged += delegate { UpdateRepeat(); };
            _count.ValueChanged += delegate { UpdateRepeat(); };
            _duration.ValueChanged += delegate { UpdateRepeat(); };
            _durationUnit.SelectedIndexChanged += delegate { UpdateRepeat(); };

            // Global shortcuts: both keys are required before anything can run.
            Card keys = new Card(Glyph.Keyboard, "Phím tắt") { Dock = DockStyle.Fill, Margin = new Padding(5, 0, 0, 0) };
            keys.AddHeader(_keysPill);
            TableLayoutPanel keyGrid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Margin = Padding.Empty };
            keyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            keyGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            keyGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            keyGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            keyGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            keyGrid.Controls.Add(Kit.FieldLabel("Phím Bắt đầu"), 0, 0);
            Label stopLabel = Kit.FieldLabel("Phím Dừng ngay"); stopLabel.Margin = new Padding(6, 0, 0, 0);
            keyGrid.Controls.Add(stopLabel, 1, 0);
            keyGrid.Controls.Add(new FieldBox(_startKeyBox, Glyph.Play, Theme.GreenBright) { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 6, 3) }, 0, 1);
            keyGrid.Controls.Add(new FieldBox(_stopKeyBox, Glyph.Stop, Theme.Red) { Dock = DockStyle.Fill, Margin = new Padding(6, 3, 0, 3) }, 1, 1);
            TableLayoutPanel keyFoot = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 2, 0, 0) };
            keyFoot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            keyFoot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            keyFoot.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            UiButton apply = new UiButton("Lưu phím tắt", Glyph.Save, ButtonKind.Secondary) { Height = 32, Anchor = AnchorStyles.Right, Margin = new Padding(8, 0, 0, 0) };
            keyFoot.Controls.Add(_keyStatus, 0, 0); keyFoot.Controls.Add(apply, 1, 0);
            keyGrid.Controls.Add(keyFoot, 0, 2); keyGrid.SetColumnSpan(keyFoot, 2);
            keys.Controls.Add(keyGrid);
            edit.Controls.Add(keys, 1, 2);
            _startKeyBox.KeyDown += delegate(object sender, KeyEventArgs e) { CaptureKey(e, true); };
            _stopKeyBox.KeyDown += delegate(object sender, KeyEventArgs e) { CaptureKey(e, false); };
            apply.Click += delegate { ApplyHotkeys(true); };

            // Run bar: what will run, a one-pass trial and the main start button.
            Card bar = new Card { Dock = DockStyle.Fill };
            IconTile barIcon = new IconTile(Glyph.Checklist, Theme.GreenSoft, Theme.GreenBright, 13f) { Location = new Point(10, 10) };
            _summary.SetBounds(60, 9, 300, 21); _status.SetBounds(61, 31, 300, 18);
            bar.Controls.Add(barIcon); bar.Controls.Add(_summary); bar.Controls.Add(_status);
            bar.FitBeforeHeader(_summary); bar.FitBeforeHeader(_status);
            bar.AddHeader(_start); bar.AddHeader(_trial);
            root.Controls.Add(bar, 0, 1);
            _status.Text = "Chọn kịch bản và lưu hai phím tắt trước khi chạy. Khi chạy, app ẩn xuống khay.";
            _start.Click += delegate { StartRun(false); };
            _trial.Click += delegate { StartRun(true); };
            root.Controls.Add(new Label { Text = "Thiết kế bởi VietDang · Zalo: 0974 318 666", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleRight, Font = Theme.Credit, ForeColor = Ui.Muted, Visible = !_embedded }, 0, 2);
        }
        private void BuildGrid()
        {
            _steps.Dock = DockStyle.Fill;
            _steps.Margin = Padding.Empty;
            _steps.ReadOnly = true;
            _steps.AllowUserToAddRows = false;
            _steps.AllowUserToDeleteRows = false;
            _steps.AllowUserToResizeRows = false;
            _steps.AllowUserToResizeColumns = false;
            _steps.AllowUserToOrderColumns = false;
            _steps.RowHeadersVisible = false;
            _steps.MultiSelect = false;
            _steps.ShowCellToolTips = false;
            _steps.StandardTab = true;
            _steps.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _steps.BackgroundColor = Theme.Surface;
            _steps.BorderStyle = BorderStyle.None;
            _steps.CellBorderStyle = DataGridViewCellBorderStyle.None;
            _steps.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            _steps.EnableHeadersVisualStyles = false;
            _steps.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            _steps.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            _steps.DefaultCellStyle.Font = Theme.Regular;
            // The grid does not scale its own row and column sizes with the form, so follow the screen DPI here.
            float scale = Dpi.Scale;
            _steps.ColumnHeadersHeight = (int)Math.Round(34 * scale);
            _steps.RowTemplate.Height = (int)Math.Round(38 * scale);
            string[] headers = { "STT", "Tọa độ X", "Tọa độ Y", "Nút chuột", "Thao tác", "Chờ sau (ms)", "Giữ lì (ms)", "Hành động" };
            // Fixed widths with one stretching column (Thao tác), so the layout never drifts.
            int[] widths = { 58, 92, 92, 104, 0, 116, 104, 96 };
            for (int i = 0; i < headers.Length; i++)
            {
                DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn { HeaderText = headers[i], SortMode = DataGridViewColumnSortMode.NotSortable };
                if (widths[i] == 0) { column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill; column.MinimumWidth = (int)Math.Round(96 * scale); }
                else column.Width = (int)Math.Round(widths[i] * scale);
                _steps.Columns.Add(column);
            }
            _steps.CellPainting += PaintStepCell;
            _steps.RowPrePaint += delegate(object sender, DataGridViewRowPrePaintEventArgs e) { e.PaintParts &= ~DataGridViewPaintParts.Focus; };
            _steps.Paint += PaintEmptyGrid;
            _steps.CellMouseEnter += delegate(object sender, DataGridViewCellEventArgs e)
            {
                SetHoverRow(e.RowIndex);
                _steps.Cursor = e.RowIndex >= 0 && e.ColumnIndex == 7 ? Cursors.Hand : Cursors.Default;
            };
            _steps.MouseLeave += delegate { SetHoverRow(-1); _steps.Cursor = Cursors.Default; };
            // Action column: pencil on the left half edits, bin on the right half deletes.
            _steps.CellMouseClick += delegate(object sender, DataGridViewCellMouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.ColumnIndex != 7 || Selected == null) return;
                if (e.X < _steps.Columns[7].Width / 2) EditStep(e.RowIndex); else DeleteStep(e.RowIndex);
            };
            _steps.CellDoubleClick += delegate(object sender, DataGridViewCellEventArgs e)
            {
                if (e.RowIndex >= 0 && e.ColumnIndex != 7 && Selected != null) EditStep(e.RowIndex);
            };
            _steps.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode != Keys.Delete || _steps.CurrentRow == null || Selected == null) return;
                e.Handled = true;
                DeleteStep(_steps.CurrentRow.Index);
            };
        }
        internal static void DrawMouse(Graphics g, Rectangle box, bool right, Color accent, Color outline)
        {
            float scale = Theme.Scale(g), w = 11 * scale, h = 16 * scale;
            RectangleF body = new RectangleF(box.X + (box.Width - w) / 2f, box.Y + (box.Height - h) / 2f, w, h);
            float split = body.Y + h * 0.44f, middle = body.X + w / 2f;
            SmoothingMode previous = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = Theme.Round(body, w / 2f))
            {
                using (Region button = new Region(path))
                {
                    button.Intersect(new RectangleF(right ? middle : body.X, body.Y, w / 2f, split - body.Y));
                    using (SolidBrush fill = new SolidBrush(accent)) g.FillRegion(fill, button);
                }
                using (Pen pen = new Pen(outline, 1.2f * scale))
                {
                    g.DrawPath(pen, path);
                    g.DrawLine(pen, body.X, split, body.Right, split);
                    g.DrawLine(pen, middle, body.Y, middle, split);
                }
            }
            g.SmoothingMode = previous;
        }
        private void SetHoverRow(int row)
        {
            if (row == _hoverRow) return;
            int old = _hoverRow;
            _hoverRow = row;
            if (old >= 0 && old < _steps.Rows.Count) _steps.InvalidateRow(old);
            if (row >= 0 && row < _steps.Rows.Count) _steps.InvalidateRow(row);
        }
        private void PaintEmptyGrid(object sender, PaintEventArgs e)
        {
            if (_steps.Rows.Count > 0) return;
            Rectangle area = new Rectangle(0, _steps.ColumnHeadersHeight, _steps.ClientSize.Width, Math.Max(0, _steps.ClientSize.Height - _steps.ColumnHeadersHeight));
            string text = Selected == null ? "Chưa có kịch bản. Bấm “Thêm” ở trên để tạo kịch bản đầu tiên."
                : "Chưa có tọa độ nào. Bấm “Thêm tọa độ” rồi nhấp trái vào vị trí cần bấm.";
            TextRenderer.DrawText(e.Graphics, text, Theme.Regular, area, Theme.Muted, Theme.Centered | TextFormatFlags.WordBreak);
        }
        private void PaintStepCell(object sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.ColumnIndex < 0) return;
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Rectangle cell = e.CellBounds;
            float scale = Theme.Scale(g);
            int column = e.ColumnIndex, pad = (int)(14 * scale);
            bool numeric = column == 1 || column == 2 || column == 5 || column == 6;
            TextFormatFlags flags = Theme.Line | TextFormatFlags.EndEllipsis | (numeric ? TextFormatFlags.Right : column == 7 ? TextFormatFlags.HorizontalCenter : TextFormatFlags.Left);
            Rectangle area = new Rectangle(cell.X + pad, cell.Y, Math.Max(0, cell.Width - 2 * pad), cell.Height);
            if (e.RowIndex < 0)
            {
                using (SolidBrush back = new SolidBrush(Theme.Surface)) g.FillRectangle(back, cell);
                using (Pen line = new Pen(Theme.Border)) g.DrawLine(line, cell.Left, cell.Bottom - 1, cell.Right, cell.Bottom - 1);
                TextRenderer.DrawText(g, _steps.Columns[column].HeaderText.ToUpper(CultureInfo.CurrentCulture), Theme.Caps, area, Theme.Muted, flags);
                e.Handled = true;
                return;
            }
            bool selected = _steps.Rows[e.RowIndex].Selected, hover = e.RowIndex == _hoverRow;
            using (SolidBrush back = new SolidBrush(selected ? Theme.BlueSoft : hover ? Theme.Subtle : Theme.Surface)) g.FillRectangle(back, cell);
            using (Pen line = new Pen(Theme.Divider)) g.DrawLine(line, cell.Left, cell.Bottom - 1, cell.Right, cell.Bottom - 1);
            string value = Convert.ToString(e.Value, CultureInfo.CurrentCulture);
            int middle = cell.Y + cell.Height / 2;
            if (column == 0)
            {
                int d = (int)(22 * scale);
                Rectangle circle = new Rectangle(area.X, middle - d / 2, d, d);
                if (selected)
                {
                    using (SolidBrush bar = new SolidBrush(Theme.Blue)) g.FillRectangle(bar, cell.X, cell.Y, Math.Max(3, (int)(3 * scale)), cell.Height - 1);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using (SolidBrush dot = new SolidBrush(Theme.Blue)) g.FillEllipse(dot, circle);
                    g.SmoothingMode = SmoothingMode.None;
                    TextRenderer.DrawText(g, value, Theme.SmallBold, circle, Color.White, Theme.Centered);
                }
                else TextRenderer.DrawText(g, value, Theme.Regular, circle, Theme.Muted, Theme.Centered);
            }
            else if (column == 1 || column == 2) TextRenderer.DrawText(g, value, Theme.Mono, area, Theme.Navy, flags);
            else if (column == 3)
            {
                // The pressed button is filled: blue for clicks, amber when the button is held down.
                bool hold = Convert.ToString(_steps.Rows[e.RowIndex].Cells[4].Value, CultureInfo.CurrentCulture) == "Giữ chuột";
                int icon = (int)(18 * scale);
                DrawMouse(g, new Rectangle(area.X, cell.Y, icon, cell.Height), value == "Phải", hold ? Theme.Amber : Theme.Blue, hold ? Theme.Amber : Theme.Body);
                TextRenderer.DrawText(g, hold ? value + " · giữ" : value, Theme.Regular, new Rectangle(area.X + icon + (int)(4 * scale), cell.Y, Math.Max(0, area.Width - icon), cell.Height),
                    hold ? Theme.Amber : Theme.Body, flags);
            }
            else if (column == 4)
            {
                Color fill = selected ? Theme.BlueTint : Theme.Divider, ink = selected ? Theme.BlueDark : Theme.Body;
                if (value == "Giữ chuột") { fill = Theme.AmberSoft; ink = Theme.Amber; }
                Size text = TextRenderer.MeasureText(g, value, Theme.Small, Size.Empty, Theme.Line);
                int height = (int)(22 * scale);
                Rectangle pill = new Rectangle(area.X, middle - height / 2, Math.Min(area.Width, text.Width + (int)(18 * scale)), height);
                Theme.Box(g, pill, 4f * scale, fill, Color.Empty);
                TextRenderer.DrawText(g, value, Theme.Small, pill, ink, Theme.Centered);
            }
            else if (column == 5) TextRenderer.DrawText(g, value, Theme.MonoBold, area, Theme.Text, flags);
            else if (column == 6) TextRenderer.DrawText(g, value, value == "—" ? Theme.Regular : Theme.Mono, area, value == "—" ? Theme.Faint : Theme.GreenBright, flags);
            else if (column == 7)
            {
                int slot = (int)(26 * scale), center = cell.X + cell.Width / 2;
                TextRenderer.DrawText(g, Glyph.Edit, Theme.Icon(10.5f), new Rectangle(center - slot - (int)(3 * scale), cell.Y, slot, cell.Height), Theme.Body, Theme.Centered);
                TextRenderer.DrawText(g, Glyph.Delete, Theme.Icon(10.5f), new Rectangle(center + (int)(3 * scale), cell.Y, slot, cell.Height), Theme.Red, Theme.Centered);
            }
            e.Handled = true;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            InitializeSettings();
        }
        internal void InitializeSettings()
        {
            if (_initialized) return;
            _initialized = true;
            if (_loadError != null) MessageBox.Show(this, "Không đọc được cài đặt cũ. Dữ liệu cũ vẫn được giữ cho đến khi bạn lưu thay đổi.\n" + _loadError, "AutoClick", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (_loadWarning != null) MessageBox.Show(this, _loadWarning, "AutoClick", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (_startCandidate != Keys.None || _stopCandidate != Keys.None) ApplyHotkeys(false);
            else { _keyStatus.ForeColor = Ui.Muted; _keyStatus.Text = "Bấm vào từng ô và nhấn phím muốn dùng (ví dụ F6, F7), rồi bấm Lưu phím tắt."; }
            UpdateKeysUi();
        }
        private void UpdateKeysUi()
        {
            if (_keysReady) { _keysPill.Text = "Đã kích hoạt"; _keysPill.SetColors(Theme.GreenSoft, Theme.GreenLine, Theme.Green); }
            else { _keysPill.Text = "Bắt buộc cài cả hai phím"; _keysPill.SetColors(Theme.RedSoft, Theme.RedLine, Theme.Red); }
            _start.Text = _keysReady ? "BẮT ĐẦU CHẠY (" + Hotkeys.Describe(_registeredStart) + ")" : "BẮT ĐẦU CHẠY";
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            Hotkeys.UnregisterHotKey(Handle, StartId); Hotkeys.UnregisterHotKey(Handle, StopId);
            _keysReady = false;
            base.OnHandleDestroyed(e);
        }
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_registeredStart != Keys.None)
            {
                _keysReady = Hotkeys.Register(Handle, StartId, _registeredStart) && Hotkeys.Register(Handle, StopId, _registeredStop);
                if (!_keysReady)
                {
                    Hotkeys.UnregisterHotKey(Handle, StartId); Hotkeys.UnregisterHotKey(Handle, StopId);
                    _keyStatus.ForeColor = Color.FromArgb(185, 28, 28);
                    _keyStatus.Text = "Cần lưu lại phím tắt trước khi chạy.";
                    UpdateKeysUi();
                    if (_runActive) StopRun();
                }
            }
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0312)
            {
                int id = message.WParam.ToInt32();
                if (!_runActive && (_startKeyBox.ContainsFocus || _stopKeyBox.ContainsFocus))
                    AssignKey(id == StartId ? _registeredStart : _registeredStop, _startKeyBox.ContainsFocus);
                else
                {
                    if (id == StopId) StopRun();
                    if (id == StartId && !_modal && !_threadModal && _picker == null) StartRun();
                }
            }
            else if (message.Msg == Program.ShowMessage) ShowApp();
            base.WndProc(ref message);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!SaveForClose(_exit || e.CloseReason != CloseReason.UserClosing)) { _exit = false; e.Cancel = true; ShowApp(); return; }
            if (!_exit && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; HideApp(); return; }
            base.OnFormClosing(e);
        }
        internal bool SaveForClose(bool stop)
        {
            if (stop) StopRun();
            ValidateChildren();
            return (_loadError != null && !_dirty) || Save();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                Application.EnterThreadModal -= EnterModal;
                Application.LeaveThreadModal -= LeaveModal;
                _progressTimer.Dispose(); _runner.Dispose();
                if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
                _trayMenu.Dispose();
            }
            base.Dispose(disposing);
            if (disposing && _appIcon != null) _appIcon.Dispose();
        }
        private void ShowApp()
        {
            if (_embedded) { if (ShowRequested != null) ShowRequested(); return; }
            Show(); WindowState = FormWindowState.Normal; Activate();
        }
        private void HideApp() { if (_embedded) { if (HideRequested != null) HideRequested(); } else Hide(); }
        private void ExitApp()
        {
            if (_embedded) { if (ExitRequested != null) ExitRequested(); return; }
            StopRun(); _exit = true; Close();
        }
        private void Post(Action action)
        {
            if (_disposed || IsDisposed || !IsHandleCreated) return;
            try { BeginInvoke(action); } catch (InvalidOperationException) { }
        }
        private bool Save()
        {
            if (_disposed) return true;
            _dirty = true;
            try { SettingsStore.Save(_settings, _settingsPath); _loadError = null; _dirty = false; return true; }
            catch (Exception ex) { MessageBox.Show(this, "Chưa lưu được thay đổi:\n" + ex.Message, "AutoClick", MessageBoxButtons.OK, MessageBoxIcon.Error); return false; }
        }
        private void RefreshScenarios(int index)
        {
            _loading = true;
            _scenarios.Items.Clear();
            foreach (Scenario scenario in _settings.Scenarios) _scenarios.Items.Add(scenario);
            _scenarios.SelectedIndex = index >= 0 && index < _scenarios.Items.Count ? index : -1;
            _loading = false;
            SelectScenario();
        }
        private void SelectScenario()
        {
            if (_loading || _disposed) return;
            _loading = true;
            Scenario scenario = Selected;
            _byCount.Checked = scenario == null || scenario.Mode == RepeatMode.Count;
            _byTime.Checked = scenario != null && scenario.Mode == RepeatMode.Duration;
            _untilStopped.Checked = scenario != null && scenario.Mode == RepeatMode.UntilStopped;
            _count.Value = scenario == null ? 1 : scenario.RepeatCount;
            DurationUnit unit = scenario == null ? DurationUnit.Seconds : scenario.TimeUnit;
            _durationUnit.SelectedIndex = unit == DurationUnit.Hours ? 2 : unit == DurationUnit.Minutes ? 1 : 0;
            _duration.Maximum = 86400 / (int)unit;
            _duration.Value = scenario == null ? 60 : scenario.DurationSeconds / (int)unit;
            _count.Enabled = scenario != null && _byCount.Checked;
            _duration.Enabled = scenario != null && _byTime.Checked;
            _durationUnit.Enabled = _duration.Enabled;
            _loading = false;
            RefreshSteps();
            if (_settings.SelectedIndex != _scenarios.SelectedIndex) { _settings.SelectedIndex = _scenarios.SelectedIndex; Save(); }
        }
        private string AskName(string title, string initial)
        {
            using (Form dialog = Ui.Dialog(title, new Size(390, 125)))
            {
                TextBox name = new TextBox { Text = initial, Location = new Point(14, 15), Width = 360, MaxLength = 80 };
                Button ok = Ui.Button("Lưu", 90), cancel = Ui.Button("Hủy", 90);
                ok.Location = new Point(185, 68); cancel.Location = new Point(284, 68);
                ok.DialogResult = DialogResult.OK; cancel.DialogResult = DialogResult.Cancel;
                dialog.Controls.AddRange(new Control[] { name, ok, cancel }); dialog.AcceptButton = ok; dialog.CancelButton = cancel;
                Dpi.Fit(dialog);
                _modal = true;
                try { return dialog.ShowDialog(this) == DialogResult.OK ? name.Text.Trim() : null; }
                finally { _modal = false; }
            }
        }
        private bool ValidName(string name, Scenario current)
        {
            if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show(this, "Hãy nhập tên kịch bản."); return false; }
            foreach (Scenario item in _settings.Scenarios)
                if (item != current && item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { MessageBox.Show(this, "Tên kịch bản này đã có."); return false; }
            return true;
        }
        private void CreateScenario()
        {
            string name = AskName("Thêm kịch bản", "");
            if (name == null || !ValidName(name, null)) return;
            _settings.Scenarios.Add(new Scenario { Name = name });
            _settings.SelectedIndex = _settings.Scenarios.Count - 1;
            Save(); RefreshScenarios(_settings.SelectedIndex);
        }
        private void RenameScenario()
        {
            Scenario current = Selected;
            if (current == null) { MessageBox.Show(this, "Hãy chọn kịch bản trước."); return; }
            string name = AskName("Đổi tên kịch bản", current.Name);
            if (name == null || !ValidName(name, current)) return;
            current.Name = name; Save(); RefreshScenarios(_scenarios.SelectedIndex);
        }
        private void DuplicateScenario()
        {
            Scenario current = Selected;
            if (current == null) { MessageBox.Show(this, "Hãy chọn kịch bản trước."); return; }
            if (_settings.Scenarios.Count >= 1000) { MessageBox.Show(this, "Đã đạt giới hạn 1000 kịch bản."); return; }
            Scenario copy = current.Copy();
            for (int number = 2; ; number++)
            {
                // Scenario names are unique and at most 80 characters.
                string suffix = " (" + number + ")";
                string name = (current.Name.Length + suffix.Length > 80 ? current.Name.Substring(0, 80 - suffix.Length) : current.Name) + suffix;
                bool taken = false;
                foreach (Scenario item in _settings.Scenarios)
                    if (item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { taken = true; break; }
                if (!taken) { copy.Name = name; break; }
            }
            int index = _scenarios.SelectedIndex + 1;
            _settings.Scenarios.Insert(index, copy);
            _settings.SelectedIndex = index;
            Save(); RefreshScenarios(index);
        }
        private void DeleteScenario()
        {
            if (Selected == null) return;
            _modal = true;
            try
            {
                if (MessageBox.Show(this, "Xóa kịch bản “" + Selected.Name + "” và toàn bộ tọa độ?", "AutoClick", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                int index = _scenarios.SelectedIndex;
                _settings.Scenarios.RemoveAt(index);
                _settings.SelectedIndex = _settings.Scenarios.Count == 0 ? -1 : Math.Min(index, _settings.Scenarios.Count - 1);
                Save(); RefreshScenarios(_settings.SelectedIndex);
            }
            finally { _modal = false; }
        }
        private void UpdateRepeat()
        {
            if (_loading || _disposed || (!_byCount.Checked && !_byTime.Checked && !_untilStopped.Checked)) return;
            _loading = true;
            DurationUnit unit = _durationUnit.SelectedIndex == 2 ? DurationUnit.Hours : _durationUnit.SelectedIndex == 1 ? DurationUnit.Minutes : DurationUnit.Seconds;
            _duration.Maximum = 86400 / (int)unit;
            _count.Enabled = Selected != null && _byCount.Checked;
            _duration.Enabled = Selected != null && _byTime.Checked;
            _durationUnit.Enabled = _duration.Enabled;
            _loading = false;
            if (Selected == null) return;
            Selected.Mode = _byCount.Checked ? RepeatMode.Count : _byTime.Checked ? RepeatMode.Duration : RepeatMode.UntilStopped;
            Selected.TimeUnit = unit;
            Selected.RepeatCount = (int)_count.Value; Selected.DurationSeconds = (int)_duration.Value * (int)unit;
            Save();
        }
        private void RefreshSteps()
        {
            _steps.Rows.Clear();
            Scenario scenario = Selected;
            _countPill.Text = (scenario == null ? 0 : scenario.Steps.Count) + " điểm đã ghi";
            if (scenario == null)
            {
                _readyPill.Text = "Chưa chọn kịch bản"; _readyPill.SetColors(Color.Empty, Color.Empty, Theme.Muted);
                _summary.Text = "Chưa chọn kịch bản";
                if (!_runActive) _status.Text = "Chọn hoặc thêm kịch bản. Hiện có " + _settings.Scenarios.Count + " kịch bản đã lưu.";
                _steps.Invalidate();
                return;
            }
            _readyPill.Text = "Sẵn sàng ghi nhận tọa độ"; _readyPill.SetColors(Color.Empty, Color.Empty, Theme.GreenBright);
            _summary.Text = "Kịch bản: " + scenario.Name;
            long cycle = 0;
            foreach (ClickStep step in scenario.Steps) cycle += step.DelayMs + (step.Action == ClickAction.Hold ? step.HoldMs : 0);
            if (!_runActive) _status.Text = scenario.Steps.Count + " tọa độ thực thi  •  Thời lượng dự kiến: ~"
                + (cycle / 1000.0).ToString("0.##", CultureInfo.InvariantCulture) + "s / chu kỳ";
            for (int i = 0; i < scenario.Steps.Count; i++)
            {
                ClickStep step = scenario.Steps[i];
                _steps.Rows.Add(i + 1, step.X, step.Y, step.Button == ClickButton.Left ? "Trái" : "Phải",
                    step.Action == ClickAction.Single ? "Nhấp đơn" : step.Action == ClickAction.Double ? "Nhấp đúp" : "Giữ chuột",
                    step.DelayMs.ToString(CultureInfo.InvariantCulture), step.Action == ClickAction.Hold ? step.HoldMs.ToString(CultureInfo.InvariantCulture) : "—", "");
            }
            _steps.Invalidate();
        }
        private void ClearSteps()
        {
            Scenario scenario = Selected;
            if (scenario == null || scenario.Steps.Count == 0) return;
            if (MessageBox.Show(this, "Xóa toàn bộ " + scenario.Steps.Count + " tọa độ của kịch bản “" + scenario.Name + "”?", "AutoClick",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            scenario.Steps.Clear();
            Save(); RefreshSteps();
        }
        private void SelectStep(int index)
        {
            if (index >= 0 && index < _steps.Rows.Count) _steps.CurrentCell = _steps.Rows[index].Cells[0];
        }
        private void MoveStep(int offset)
        {
            Scenario scenario = Selected;
            if (scenario == null || _steps.CurrentRow == null) return;
            int from = _steps.CurrentRow.Index, to = from + offset;
            if (from >= scenario.Steps.Count || to < 0 || to >= scenario.Steps.Count) return;
            ClickStep step = scenario.Steps[from];
            scenario.Steps.RemoveAt(from); scenario.Steps.Insert(to, step);
            Save(); RefreshSteps(); SelectStep(to);
        }
        private void DeleteStep(int index)
        {
            if (MessageBox.Show(this, "Xóa tọa độ số " + (index + 1) + "?", "AutoClick", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            Scenario scenario = Selected;
            if (scenario == null || index >= scenario.Steps.Count) return;
            scenario.Steps.RemoveAt(index);
            Save(); RefreshSteps(); SelectStep(Math.Min(index, scenario.Steps.Count - 1));
        }
        private void PickPoint()
        {
            if (Selected == null) { MessageBox.Show(this, "Hãy chọn hoặc thêm kịch bản trước."); return; }
            HideApp();
            Point? point = null;
            try
            {
                using (_picker = new CoordinatePicker())
                    if (_picker.ShowDialog() == DialogResult.OK) point = _picker.SelectedPoint;
            }
            finally { _picker = null; if (!_exit && !IsDisposed) ShowApp(); }
            if (!point.HasValue) return;
            using (StepDialog dialog = new StepDialog(new ClickStep { X = point.Value.X, Y = point.Value.Y }))
            {
                _modal = true;
                try { if (dialog.ShowDialog(this) == DialogResult.OK) { Selected.Steps.Add(dialog.Step); Save(); RefreshSteps(); SelectStep(Selected.Steps.Count - 1); } }
                finally { _modal = false; }
            }
        }
        private void EditStep(int index)
        {
            using (StepDialog dialog = new StepDialog(Selected.Steps[index]))
            {
                _modal = true;
                try { if (dialog.ShowDialog(this) == DialogResult.OK) { Selected.Steps[index] = dialog.Step; Save(); RefreshSteps(); SelectStep(index); } }
                finally { _modal = false; }
            }
        }
        private void CaptureKey(KeyEventArgs e, bool start)
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu) return;
            Keys keys = e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete ? Keys.None : e.KeyData;
            AssignKey(keys, start);
        }
        private void AssignKey(Keys keys, bool start)
        {
            if (start) _startCandidate = keys; else _stopCandidate = keys;
            UpdateKeyText();
            _keyStatus.ForeColor = Ui.Muted;
            _keyStatus.Text = "Bấm Lưu phím tắt để áp dụng. Có thể dùng F6/F7 hoặc Ctrl + Alt + một chữ.";
        }
        private void UpdateKeyText()
        {
            _startKeyBox.Text = Hotkeys.Describe(_startCandidate); _stopKeyBox.Text = Hotkeys.Describe(_stopCandidate);
        }
        private void ApplyHotkeys(bool save)
        {
            if (!Hotkeys.IsValid(_startCandidate) || !Hotkeys.IsValid(_stopCandidate) || _startCandidate == _stopCandidate)
            {
                _keyStatus.ForeColor = Color.FromArgb(185, 28, 28);
                _keyStatus.Text = "Cần hai phím khác nhau: F1–F24, hoặc Ctrl/Alt kết hợp chữ/số. Ví dụ F6 và F7.";
                UpdateKeysUi();
                return;
            }
            Hotkeys.UnregisterHotKey(Handle, StartId); Hotkeys.UnregisterHotKey(Handle, StopId);
            bool start = Hotkeys.Register(Handle, StartId, _startCandidate);
            bool stop = start && Hotkeys.Register(Handle, StopId, _stopCandidate);
            if (!stop)
            {
                Hotkeys.UnregisterHotKey(Handle, StartId); Hotkeys.UnregisterHotKey(Handle, StopId);
                _keysReady = _registeredStart != Keys.None && Hotkeys.Register(Handle, StartId, _registeredStart) && Hotkeys.Register(Handle, StopId, _registeredStop);
                if (!_keysReady) { Hotkeys.UnregisterHotKey(Handle, StartId); Hotkeys.UnregisterHotKey(Handle, StopId); }
                _keyStatus.ForeColor = Color.FromArgb(185, 28, 28);
                _keyStatus.Text = "Phím tắt đã được app khác sử dụng. Hãy chọn tổ hợp khác. " + (_keysReady ? "Hai phím đã lưu trước đó vẫn có hiệu lực." : "Chưa thể chạy.");
                UpdateKeysUi();
                return;
            }
            _registeredStart = _startCandidate; _registeredStop = _stopCandidate; _keysReady = true;
            _keyStatus.ForeColor = Color.FromArgb(22, 163, 74);
            _keyStatus.Text = "● Đang hiệu lực: Bắt đầu [" + Hotkeys.Describe(_registeredStart) + "] • Dừng [" + Hotkeys.Describe(_registeredStop) + "]";
            UpdateKeysUi();
            if (save) { _settings.StartKey = (int)_registeredStart; _settings.StopKey = (int)_registeredStop; Save(); }
        }
        // once: run the selected scenario a single time, whatever its saved stop condition is.
        private void StartRun(bool once = false)
        {
            if (_runActive || _modal || _threadModal || _picker != null) return;
            if (!_keysReady) { ShowApp(); MessageBox.Show(this, "Hãy cài và lưu đủ phím Bắt đầu và Dừng ngay trước khi chạy."); return; }
            Scenario scenario = Selected;
            if (scenario == null) { ShowApp(); MessageBox.Show(this, "Hãy chọn một kịch bản trước khi chạy."); return; }
            foreach (ClickStep step in scenario.Steps)
                if (!WindowsMouse.IsOnScreen(new Point(step.X, step.Y))) { ShowApp(); MessageBox.Show(this, "Có tọa độ nằm ngoài màn hình hiện tại. Hãy sửa tọa độ trước khi chạy."); return; }
            try
            {
                Scenario run = scenario.Copy();
                if (once) { run.Mode = RepeatMode.Count; run.RepeatCount = 1; }
                _runningScenario = run; _loops = 0; _stepIndex = 0;
                _restoreAfterRun = _embedded ? TopLevelControl != null && TopLevelControl.Visible : Visible;
                _runner.Start(run);
                _runActive = true; _editor.Enabled = false; _start.Enabled = false; _trial.Enabled = false;
                _tray.Text = "AutoClick: Đang chạy"; _progressTimer.Start();
                if (RunningChanged != null) RunningChanged(true);
                HideApp();
            }
            catch (Exception ex) { ShowApp(); MessageBox.Show(this, ex.Message, "Không thể chạy", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        }
        internal void StopRun()
        {
            if (_picker != null) { _picker.DialogResult = DialogResult.Cancel; _picker.Close(); }
            try { _runner.Stop(); }
            catch (Exception ex) { ShowApp(); MessageBox.Show(this, "Chưa nhả được chuột: " + ex.Message, "AutoClick", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            if (_runActive) _status.Text = "Đang dừng và nhả nút chuột…";
        }
        private void UpdateProgress()
        {
            if (!_runActive || _runningScenario == null) return;
            if (_runner.WaitingForRelease) { _status.Text = "Thả Ctrl/Alt/Shift và các nút chuột để kịch bản bắt đầu. Phím Dừng ngay vẫn hoạt động."; return; }
            string limit = _runningScenario.Mode == RepeatMode.Count ? "lượt " + (_loops + 1) + "/" + _runningScenario.RepeatCount :
                _runningScenario.Mode == RepeatMode.Duration ? "còn " + Math.Max(0, _runningScenario.DurationSeconds - (int)_runner.Elapsed.TotalSeconds) + " giây" :
                "đã xong " + _loops + " lượt · chạy đến khi dừng";
            _status.Text = _runningScenario.Name + " · điểm " + (_stepIndex + 1) + "/" + _runningScenario.Steps.Count + " · " + limit;
        }
        private void FinishRun(RunResult result)
        {
            _progressTimer.Stop(); _runActive = false;
            _editor.Enabled = true; _start.Enabled = true; _trial.Enabled = true;
            _tray.Text = "AutoClick: Sẵn sàng";
            if (RunningChanged != null) RunningChanged(false);
            _status.Text = result.Error != null ? "Đã dừng do lỗi: " + result.Error :
                (result.Stopped ? "Đã dừng ngay" : result.TimeLimit ? "Đã hết thời gian" : "Đã chạy xong") + " · " + result.Loops + " lượt hoàn tất.";
            if (_restoreAfterRun || result.Error != null) ShowApp();
            if (result.Error != null) MessageBox.Show(this, result.Error, "AutoClick", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    // Every cell is owner-painted, so the stock focus rectangle is switched off and drawing is double-buffered.
    internal sealed class StepGrid : DataGridView
    {
        internal StepGrid() { DoubleBuffered = true; }
        protected override bool ShowFocusCues { get { return false; } }
    }

    internal static class Ui
    {
        internal static readonly Color Muted = Theme.Muted;
        internal static Button Button(string text, int width) { return new Button { Text = text, Width = width, Height = 26, Cursor = Cursors.Hand, Margin = new Padding(3) }; }
        internal static Label Caption(string text, int width) { return new Label { Text = text, Width = width, Height = 25, TextAlign = ContentAlignment.MiddleLeft, Margin = new Padding(3) }; }
        internal static Label Section(string text) { return new Label { Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.UiFont("Segoe UI", 9f, FontStyle.Bold), ForeColor = Color.FromArgb(51, 65, 85) }; }
        internal static FlowLayoutPanel Row() { return new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = Padding.Empty, WrapContents = false }; }
        // Callers add their controls, then call Dpi.Fit before showing it.
        internal static Form Dialog(string text, Size size)
        {
            return new Form { Text = text, ClientSize = size, Font = Theme.Regular, FormBorderStyle = FormBorderStyle.FixedDialog,
                StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, ShowInTaskbar = false };
        }
    }

    internal sealed class CoordinatePicker : Form
    {
        internal Point SelectedPoint;
        internal CoordinatePicker()
        {
            FormBorderStyle = FormBorderStyle.None; StartPosition = FormStartPosition.Manual;
            Bounds = SystemInformation.VirtualScreen; TopMost = true; ShowInTaskbar = false;
            BackColor = Color.Black; Opacity = 0.22; Cursor = Cursors.Cross; KeyPreview = true;
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) { SelectedPoint = Cursor.Position; DialogResult = DialogResult.OK; Close(); }
            else if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); }
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
            base.OnKeyDown(e);
        }
    }

    internal sealed class StepDialog : Form
    {
        private readonly NumericUpDown _x = Number(-1000000, 1000000, 0);
        private readonly NumericUpDown _y = Number(-1000000, 1000000, 0);
        private readonly NumericUpDown _delay = Number(0, 3600000, 0);
        private readonly NumericUpDown _hold = Number(10, 3600000, 0);
        private readonly ComboBox _button = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        private readonly ComboBox _action = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
        internal ClickStep Step;
        private static NumericUpDown Number(decimal min, decimal max, int places)
        { return new NumericUpDown { Minimum = min, Maximum = max, DecimalPlaces = places, Width = 190, Increment = places == 0 ? 1 : 0.1m }; }
        internal StepDialog(ClickStep step)
        {
            Text = "Thiết lập tọa độ"; ClientSize = new Size(430, 305); Font = Theme.Regular;
            StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            _button.Items.AddRange(new object[] { "Chuột trái", "Chuột phải" });
            _action.Items.AddRange(new object[] { "Nhấp đơn", "Nhấp đúp", "Giữ chuột" });
            _x.Value = Math.Max(_x.Minimum, Math.Min(_x.Maximum, step.X));
            _y.Value = Math.Max(_y.Minimum, Math.Min(_y.Maximum, step.Y));
            _button.SelectedIndex = (int)step.Button; _action.SelectedIndex = (int)step.Action;
            _delay.Value = step.DelayMs; _hold.Value = step.HoldMs;
            string[] labels = { "Tọa độ X", "Tọa độ Y", "Nút chuột", "Thao tác", "Chờ sau khi nhấp (ms)", "Thời gian giữ lì (ms)" };
            Control[] fields = { _x, _y, _button, _action, _delay, _hold };
            for (int i = 0; i < fields.Length; i++)
            {
                Controls.Add(new Label { Text = labels[i], Location = new Point(15, 17 + i * 34), Size = new Size(185, 26), TextAlign = ContentAlignment.MiddleLeft });
                fields[i].Location = new Point(215, 19 + i * 34); Controls.Add(fields[i]);
            }
            _hold.Enabled = step.Action == ClickAction.Hold;
            _action.SelectedIndexChanged += delegate { _hold.Enabled = _action.SelectedIndex == (int)ClickAction.Hold; };
            Controls.Add(new Label { Text = "1.000 ms = 1 giây. Giữ lì sẽ tự nhả khi hết thời gian hoặc bấm phím dừng.", Location = new Point(15, 226), Size = new Size(397, 32), ForeColor = Ui.Muted });
            Button ok = Ui.Button("Lưu", 90), cancel = Ui.Button("Hủy", 90);
            ok.Location = new Point(215, 265); cancel.Location = new Point(315, 265); cancel.DialogResult = DialogResult.Cancel;
            Controls.AddRange(new Control[] { ok, cancel }); AcceptButton = ok; CancelButton = cancel;
            ok.Click += delegate {
                if (!WindowsMouse.IsOnScreen(new Point((int)_x.Value, (int)_y.Value))) { MessageBox.Show(this, "Tọa độ nằm ngoài màn hình hiện tại."); return; }
                Step = new ClickStep { X = (int)_x.Value, Y = (int)_y.Value, Button = (ClickButton)_button.SelectedIndex,
                    Action = (ClickAction)_action.SelectedIndex, DelayMs = (int)_delay.Value, HoldMs = (int)_hold.Value };
                DialogResult = DialogResult.OK; Close();
            };
            Dpi.Fit(this);
        }
    }
}
