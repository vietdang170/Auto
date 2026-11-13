using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using VietDang.Design;
using ClickForm = VietDang.AutoClick.MainForm;
using PasteForm = AutoPasteValueApp.MainForm;

[assembly: AssemblyTitle("Auto")]
[assembly: AssemblyProduct("Auto")]
[assembly: AssemblyCompany("VietDang")]

namespace VietDang.AutoSuite
{
    internal static class Program
    {
        internal static readonly int ShowMessage = RegisterWindowMessage("VietDang_Auto_Show_2026");
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int RegisterWindowMessage(string name);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, int message, IntPtr w, IntPtr l);
        [STAThread]
        private static void Main(string[] args)
        {
            if (Array.Exists(args, delegate(string arg) { return arg.Equals("/trace", StringComparison.OrdinalIgnoreCase); })) { RunTrace(); return; }
            string updatedSignal = null, failedVersion = null;
            foreach (string arg in args)
            {
                if (arg.StartsWith("/updated:", StringComparison.OrdinalIgnoreCase)) updatedSignal = arg.Substring(9);
                else if (arg.StartsWith("/update-failed:", StringComparison.OrdinalIgnoreCase)) failedVersion = arg.Substring(15);
            }
            bool created;
            using (Mutex suite = new Mutex(true, "VietDang_Auto_Suite_2026", out created))
            {
                if (!created) { PostMessage(new IntPtr(0xFFFF), ShowMessage, IntPtr.Zero, IntPtr.Zero); return; }
                bool pasteAvailable, clickAvailable;
                using (Mutex paste = new Mutex(true, "AutoPasteValue_SingleInstance_Mutex_2026", out pasteAvailable))
                using (Mutex click = new Mutex(true, "VietDang_AutoClick_2026", out clickAvailable))
                {
                    if (!pasteAvailable || !clickAvailable)
                    {
                        MessageBox.Show("Hãy thoát hẳn Auto Paste Value và Auto Click cũ ở khay hệ thống, rồi mở Auto.", "Auto");
                        return;
                    }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    bool minimized = Array.Exists(args, delegate(string arg) { return arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase) || arg.Equals("/startup", StringComparison.OrdinalIgnoreCase) || arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase); });
                    try { Startup.Refresh(); }
                    catch (Exception ex) { MessageBox.Show("Chưa cập nhật được đường dẫn khởi động cùng Windows:\n" + ex.Message, "Auto"); }
                    using (AutoForm form = new AutoForm())
                    using (ApplicationContext context = new ApplicationContext())
                    {
                        form.FormClosed += delegate { context.ExitThread(); };
                        form.AfterStart(updatedSignal, failedVersion);
                        if (minimized)
                        {
                            IntPtr handle = form.Handle;
                            form.InitializeModules();
                        }
                        else form.Show();
                        Application.Run(context);
                    }
                }
            }
            // The single-instance locks are released at this point, so the new version can start.
            if (Updates.PendingInstall != null) Updates.Install(Updates.PendingInstall, Updates.ReopenWindow);
        }
        private static void RunTrace()
        {
            Boot.Start();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string folder = Path.Combine(Path.GetTempPath(), "Auto.Trace." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                using (AutoForm form = new AutoForm(null, Path.Combine(folder, "autoclick.xml"), true))
                using (ApplicationContext context = new ApplicationContext())
                {
                    Boot.Mark("Tạo xong cửa sổ (chưa hiện)");
                    form.FormClosed += delegate { context.ExitThread(); };
                    EventHandler idle = null;
                    idle = delegate
                    {
                        Application.Idle -= idle;
                        Boot.Mark("Cửa sổ đã vẽ xong, sẵn sàng thao tác");
                        Boot.Save();
                        form.BeginInvoke(new Action(form.ExitAuto));
                    };
                    form.Shown += delegate { Boot.Mark("Cửa sổ hiện lên (Shown)"); form.BeginInvoke(new Action(delegate { Application.Idle += idle; })); };
                    form.Show();
                    Application.Run(context);
                }
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }

    // Startup timing: "Auto.exe /trace" opens an isolated copy (no hotkeys, clipboard or registry), records how long
    // each startup step takes, writes startup-trace.txt next to the exe and closes again.
    internal static class Boot
    {
        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static StringBuilder _log;
        internal static void Mark(string step)
        {
            if (_log != null) _log.AppendFormat("{0,6} ms  {1}\r\n", Clock.ElapsedMilliseconds, step);
        }
        internal static void Start()
        {
            _log = new StringBuilder();
            try
            {
                using (Process self = Process.GetCurrentProcess())
                    _log.AppendFormat("Windows tạo tiến trình lúc {0:HH:mm:ss.fff}; vào Main lúc {1:HH:mm:ss.fff} (nạp .NET: {2} ms)\r\n",
                        self.StartTime, DateTime.Now, (int)(DateTime.Now - self.StartTime).TotalMilliseconds);
            }
            catch (Exception ex) { _log.AppendLine("Không đọc được thời điểm khởi tạo: " + ex.Message); }
            Mark("Vào Main");
        }
        internal static void Save()
        {
            if (_log == null) return;
            File.AppendAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "startup-trace.txt"),
                "=== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===\r\n" + _log + "\r\n", Encoding.UTF8);
            _log = null;
        }
    }

    // Auto starts from its own Run entry, so the old Auto Paste Value app can no longer repoint it.
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Auto", LegacyName = "AutoPasteValue";
        private static string Command { get { return "\"" + Application.ExecutablePath + "\" /minimized"; } }
        internal static bool IsEnabled()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey))
                    return run != null && (run.GetValue(ValueName) != null || run.GetValue(LegacyName) != null);
            }
            catch (Exception) { return false; }
        }
        internal static void Set(bool enabled)
        {
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) run.SetValue(ValueName, Command, RegistryValueKind.String);
                else run.DeleteValue(ValueName, false);
                run.DeleteValue(LegacyName, false);
            }
        }
        // Moves an entry made by the old app to Auto's own name and keeps the path current if Auto.exe was moved.
        internal static void Refresh()
        {
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (run == null) return;
                bool legacy = run.GetValue(LegacyName) != null;
                if (!legacy && run.GetValue(ValueName) == null) return;
                run.SetValue(ValueName, Command, RegistryValueKind.String);
                if (legacy) run.DeleteValue(LegacyName, false);
            }
        }
    }

    // Auto's own preferences, separate from the Auto Paste and Auto Click settings.
    internal static class UpdateSettings
    {
        private const string Key = @"Software\VietDang\Auto";
        internal static bool DailyCheck
        {
            get
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key))
                        return key == null || Convert.ToInt32(key.GetValue("DailyUpdateCheck", 1)) == 1;
                }
                catch (Exception) { return true; }
            }
            set
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key))
                        key.SetValue("DailyUpdateCheck", value ? 1 : 0, RegistryValueKind.DWord);
                }
                catch (Exception) { }
            }
        }
    }

    internal sealed class AutoForm : Form
    {
        private const int NavWidth = 184, PageWidth = 840, PageHeight = 620;
        // Page order follows the sidebar: Auto Click first, then Auto Paste, then Settings.
        internal const int ClickPage = 0, PastePage = 1, SettingsPage = 2;
        private readonly PasteForm _paste;
        private readonly ClickForm _click;
        private readonly bool _isolated;
        private readonly Panel _nav = new Panel { Dock = DockStyle.Left, Width = NavWidth, BackColor = Theme.Surface, Padding = new Padding(0, 0, 1, 0) };
        private readonly Panel _host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window };
        private readonly RadioButton[] _navItems = new RadioButton[3];
        private readonly Panel[] _pages = new Panel[3];
        private readonly CheckBox _startupBox = new ToggleSwitch { AccessibleName = "Mở Auto khi đăng nhập Windows", Anchor = AnchorStyles.Right };
        private readonly CheckBox _updateBox = new ToggleSwitch { AccessibleName = "Tự động kiểm tra cập nhật hằng ngày", Anchor = AnchorStyles.Right };
        private int _lastTrayToggle;
        private readonly UiButton _checkButton = new UiButton("Kiểm tra ngay", Glyph.Sync, ButtonKind.Secondary) { Height = 32, Anchor = AnchorStyles.Right };
        private Label _updateStatus;
        private readonly System.Windows.Forms.Timer _updateTimer = new System.Windows.Forms.Timer();
        private ReleaseInfo _ready;
        private bool _checking, _offering, _offerAfterRun, _startReported;
        private string _updatedSignal, _failedVersion;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _pasteToggle = new ToolStripMenuItem("Auto Paste");
        private readonly ToolStripMenuItem _startupItem = new ToolStripMenuItem("Khởi động cùng Windows");
        private readonly NotifyIcon _tray;
        private readonly Icon _iconOn, _iconOff;
        private int _selected;
        private bool _exit, _disposed, _modulesInitialized, _clickRunning, _syncingStartup;

        internal AutoForm() : this(null, null, false) { }
        // isolated: integration checks must not touch the user's registry, global hotkeys or clipboard.
        internal AutoForm(VietDang.AutoClick.AppSettings clickSettings, string settingsPath, bool isolated)
        {
            _isolated = isolated;
            Text = "Auto";
            Font = Theme.Regular; BackColor = Theme.Window;
            FormBorderStyle = FormBorderStyle.FixedSingle; MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(NavWidth + PageWidth, PageHeight);
            Boot.Mark("Bắt đầu tạo cửa sổ");
            _iconOn = PasteForm.CreateTrayIcon(true, 32); _iconOff = PasteForm.CreateTrayIcon(false, 32);
            Icon = _iconOn;
            Boot.Mark("Vẽ icon khay");
            _paste = new PasteForm(true, isolated);
            Boot.Mark("Tạo trang Auto Paste");
            _click = new ClickForm(clickSettings, settingsPath, true);
            Boot.Mark("Tạo trang Auto Click");

            // The fill panel goes in first so the navigation strip is docked before it.
            Controls.Add(_host); Controls.Add(_nav);
            BuildNavigation();
            for (int i = 0; i < _pages.Length; i++)
            {
                // Sized up front so each page is laid out once, before it is first shown.
                _pages[i] = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window, Visible = false, Size = new Size(PageWidth, PageHeight) };
                _host.Controls.Add(_pages[i]);
            }
            Embed(_click, _pages[ClickPage]); Embed(_paste, _pages[PastePage]);
            BuildSettingsPage(_pages[SettingsPage]);
            Boot.Mark("Gắn các trang + trang Cài đặt");

            _menu.Items.Add("Mở Auto", null, delegate { ShowPage(_selected); });
            _menu.Items.Add(_pasteToggle);
            _pasteToggle.Click += delegate { _paste.ToggleState(); };
            _menu.Items.Add(_startupItem);
            _startupItem.Click += delegate { SetStartup(!_startupItem.Checked); };
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Thoát Auto", null, delegate { ExitAuto(); });
            _tray = new NotifyIcon { Icon = _iconOn, Text = "Auto", ContextMenuStrip = _menu, Visible = true };
            // A left click flips Auto Paste at once, like the old app. A double click opens the window; its first click has
            // already flipped Auto Paste, so that flip is undone and a double click never changes the state.
            _tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                _paste.ToggleState();
                _lastTrayToggle = Environment.TickCount;
            };
            _tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                if (unchecked(Environment.TickCount - _lastTrayToggle) <= SystemInformation.DoubleClickTime * 2) _paste.ToggleState();
                ShowPage(_selected);
            };

            bool startup = !isolated && Startup.IsEnabled();
            _startupBox.Checked = startup; _startupItem.Checked = startup;
            _startupBox.CheckedChanged += delegate { SetStartup(_startupBox.Checked); };
            _updateBox.Checked = isolated || UpdateSettings.DailyCheck;
            _updateBox.CheckedChanged += delegate { if (!_isolated) UpdateSettings.DailyCheck = _updateBox.Checked; };
            _checkButton.Click += delegate { if (_ready != null) OfferUpdate(true); else StartCheck(true); };
            if (!isolated)
            {
                _updateTimer.Interval = 20000;
                _updateTimer.Tick += delegate
                {
                    _updateTimer.Interval = 3600000;
                    if (_updateBox.Checked && Updates.CheckDue) StartCheck(false);
                };
                _updateTimer.Start();
            }

            _paste.ShowRequested += delegate { ShowPage(PastePage); };
            _paste.HideRequested += delegate { Hide(); };
            _paste.ExitRequested += ExitAuto;
            _paste.StateChanged += UpdateTray;
            _click.ShowRequested += delegate { ShowPage(ClickPage); };
            _click.HideRequested += delegate { Hide(); };
            _click.ExitRequested += ExitAuto;
            _click.RunningChanged += delegate(bool running)
            {
                _clickRunning = running;
                UpdateTray();
                // An update offer that arrived during a run waits until the run is over.
                if (!running && _offerAfterRun) { _offerAfterRun = false; OfferUpdate(false); }
            };
            _navItems[0].Checked = true;
            UpdateTray();
            // Both modules need window handles now: clipboard listening and hotkeys work while Auto stays in the tray.
            IntPtr pasteHandle = _paste.Handle, clickHandle = _click.Handle;
            Boot.Mark("Tạo handle Auto Paste / Auto Click");
        }
        private static void Embed(Form module, Panel page)
        {
            module.TopLevel = false; module.FormBorderStyle = FormBorderStyle.None;
            module.ShowInTaskbar = false; module.Dock = DockStyle.Fill;
            page.Controls.Add(module); module.Show();
        }
        private void BuildNavigation()
        {
            _nav.Paint += delegate(object sender, PaintEventArgs e) { using (Pen line = new Pen(Theme.Border)) e.Graphics.DrawLine(line, _nav.Width - 1, 0, _nav.Width - 1, _nav.Height); };
            string[] titles = { "Auto Click", "Auto Paste", "Cài đặt" };
            string[] glyphs = { Glyph.Mouse, Glyph.Paste, Glyph.Settings };
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                NavItem item = new NavItem(titles[i], glyphs[i]) { Location = new Point(10, 12 + i * 42), Size = new Size(NavWidth - 21, 38) };
                item.CheckedChanged += delegate { if (item.Checked) SelectPage(index); };
                _navItems[i] = item;
                _nav.Controls.Add(item);
            }
            _nav.Controls.Add(new Label { Text = "Thiết kế bởi VietDang\nZalo: 0974 318 666", Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(18, 0, 0, 12),
                TextAlign = ContentAlignment.BottomLeft, Font = new Font("Segoe UI", 7.5f), ForeColor = Theme.Faint });
        }
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] private static extern bool RedrawWindow(IntPtr window, IntPtr rect, IntPtr region, uint flags);
        // Page changes happen with drawing frozen and are then painted in one pass, so a switch never shows half a page.
        private void Frozen(Action change)
        {
            if (!_host.IsHandleCreated || !Visible) { change(); return; }
            SendMessage(_host.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            try { change(); }
            finally
            {
                SendMessage(_host.Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
                RedrawWindow(_host.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080 | 0x0100);
            }
        }
        private void SelectPage(int index)
        {
            _selected = index;
            Frozen(delegate
            {
                _pages[index].Visible = true;
                for (int i = 0; i < _pages.Length; i++) if (i != index) _pages[i].Visible = false;
            });
        }
        // Creates the window handles of every page once, out of sight, so the first visit to a tab is as quick as later ones.
        private void WarmUpPages()
        {
            Frozen(delegate
            {
                foreach (Panel page in _pages) page.Visible = true;
                for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == _selected;
            });
        }
        private void BuildSettingsPage(Panel page)
        {
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12), BackColor = Theme.Window };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 184));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Kit.PageHeader(Glyph.Settings, "Cài đặt", ""), 0, 0);

            Card startup = new Card(Glyph.Power, "Khởi động cùng hệ thống") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            startup.Controls.Add(SettingRow("Mở Auto khi đăng nhập Windows",
                "Auto tự mở, thu nhỏ ở khay hệ thống mỗi khi bạn đăng nhập. Phím tắt và Auto Paste hoạt động ngay.", _startupBox));
            layout.Controls.Add(startup, 0, 1);

            Card update = new Card(Glyph.Sync, "Cập nhật phần mềm") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            update.AddHeader(Kit.Note("github.com/" + Updates.Repository));
            TableLayoutPanel updateRows = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            updateRows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            updateRows.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            updateRows.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            TableLayoutPanel versionRow = SettingRow("Phiên bản hiện tại: " + Updates.Current,
                "Bấm Kiểm tra ngay để tìm phiên bản mới trên GitHub.", _checkButton);
            _updateStatus = (Label)versionRow.GetControlFromPosition(0, 1);
            _updateStatus.AutoEllipsis = true;
            versionRow.Margin = new Padding(0, 0, 0, 8);
            updateRows.Controls.Add(versionRow, 0, 0);
            TableLayoutPanel dailyRow = SettingRow("Tự động kiểm tra cập nhật hằng ngày",
                "", _updateBox);
            dailyRow.Margin = new Padding(0, 8, 0, 0);
            updateRows.Controls.Add(dailyRow, 0, 1);
            update.Controls.Add(updateRows);
            layout.Controls.Add(update, 0, 2);

            Card about = new Card(Glyph.Info, "Thông tin") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            about.Controls.Add(SettingRow("",
                "Thiết kế bởi VietDang\nZalo: 0974 318 666", null));
            layout.Controls.Add(about, 0, 3);
            page.Controls.Add(layout);
        }
        // Title and description on the left, the control for that setting on the right.
        private static TableLayoutPanel SettingRow(string title, string description, Control control)
        {
            TableLayoutPanel row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            // An empty title takes no space, so a description-only row starts at the top.
            // An empty description leaves the title alone, centred next to its control.
            if (string.IsNullOrEmpty(description))
            {
                row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                row.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            }
            else
            {
                row.RowStyles.Add(new RowStyle(SizeType.Absolute, string.IsNullOrEmpty(title) ? 0 : 24));
                row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }
            row.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = Theme.Semibold, ForeColor = Theme.Text,
                TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true }, 0, 0);
            row.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill, Margin = Padding.Empty, Font = Theme.Small, ForeColor = Theme.Muted, UseMnemonic = false }, 0, 1);
            if (control != null)
            {
                control.Margin = new Padding(16, 0, 0, 0);
                row.Controls.Add(control, 1, 0);
                row.SetRowSpan(control, 2);
            }
            return row;
        }
        // The Settings checkbox and the tray menu item always show what is really in the registry.
        private void SetStartup(bool enabled)
        {
            if (_syncingStartup) return;
            bool actual = enabled;
            if (!_isolated)
            {
                try { Startup.Set(enabled); }
                catch (Exception ex) { MessageBox.Show(this, "Chưa đổi được thiết lập khởi động cùng Windows:\n" + ex.Message, "Auto", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                actual = Startup.IsEnabled();
            }
            _syncingStartup = true;
            try { _startupBox.Checked = actual; _startupItem.Checked = actual; }
            finally { _syncingStartup = false; }
        }
        // A grey icon means copies keep their formatting, exactly like the old Auto Paste tray icon.
        private void UpdateTray()
        {
            if (_disposed) return;
            bool pasting = _paste.PasteEnabled;
            _pasteToggle.Checked = pasting;
            _tray.Icon = pasting ? _iconOn : _iconOff; Icon = pasting ? _iconOn : _iconOff;
            _tray.Text = _clickRunning ? "Auto: Auto Click đang chạy" : pasting ? "Auto Paste ĐANG BẬT · nhấp để tắt" : "Auto Paste ĐANG TẮT · nhấp để bật";
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Boot.Mark("OnShown");
            WarmUpPages();
            Boot.Mark("Chuẩn bị sẵn các tab");
            if (!_isolated) Updates.CleanUp();
            InitializeModules();
            Boot.Mark("Nạp kịch bản + phím tắt Auto Click");
            BeginInvoke(new Action(ReportStarted));
        }
        internal void InitializeModules()
        {
            if (_modulesInitialized) return;
            _modulesInitialized = true; _click.InitializeSettings();
            if (!Visible) BeginInvoke(new Action(ReportStarted));
        }
        private void ShowPage(int index)
        {
            if (_disposed) return;
            _navItems[Math.Max(0, Math.Min(index, _navItems.Length - 1))].Checked = true;
            Show(); WindowState = FormWindowState.Normal; Activate();
        }
        internal void ExitAuto() { _exit = true; Close(); }

        // Called by Main before the window starts. signal: this run was started by an update; failed: an update was rolled back.
        internal void AfterStart(string signal, string failed)
        {
            _updatedSignal = signal; _failedVersion = failed;
            if (signal != null) SetUpdateStatus("Đã cập nhật lên phiên bản " + Updates.Current + ".", Theme.GreenBright);
            if (failed != null) SetUpdateStatus("Bản " + failed + " không khởi động được nên Auto đã quay lại bản " + Updates.Current + ".", Theme.Red);
        }
        // Runs once the app is up (window shown, or modules ready when starting in the tray).
        private void ReportStarted()
        {
            if (_startReported) return;
            _startReported = true;
            Updates.ReportStarted(_updatedSignal);
            if (_failedVersion != null)
                MessageBox.Show(this, "Phiên bản " + _failedVersion + " không khởi động được, nên Auto đã tự quay lại phiên bản " + Updates.Current
                    + ".\nAuto sẽ không tự đề nghị lại bản này; bạn vẫn có thể bấm Kiểm tra ngay trong Cài đặt.", "Auto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        private void SetUpdateStatus(string text, Color color)
        {
            if (_updateStatus == null) return;
            _updateStatus.Text = text;
            _updateStatus.ForeColor = color;
        }
        // Looks for a newer release on a background thread and downloads it before saying anything.
        private void StartCheck(bool manual)
        {
            if (_checking || _isolated || _disposed) return;
            _checking = true;
            _checkButton.Enabled = false;
            SetUpdateStatus("Đang kiểm tra phiên bản mới trên GitHub…", Theme.Muted);
            Thread worker = new Thread(delegate()
            {
                ReleaseInfo release = null;
                string error = null;
                bool newer = false;
                try
                {
                    release = Updates.FetchLatest();
                    Updates.LastCheck = DateTime.UtcNow;
                    newer = release != null && release.Version > Updates.Current
                        && (manual || release.Version.ToString() != Updates.FailedVersion);
                    if (newer)
                    {
                        error = Updates.FolderProblem();
                        if (error == null) Updates.Download(release);
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                try { BeginInvoke(new Action(delegate { FinishCheck(release, newer, error, manual); })); }
                catch (InvalidOperationException) { }
            }) { IsBackground = true, Name = "Auto update check" };
            worker.Start();
        }
        private void FinishCheck(ReleaseInfo release, bool newer, string error, bool manual)
        {
            if (_disposed) return;
            _checking = false;
            _checkButton.Enabled = true;
            if (error != null) { SetUpdateStatus("Không kiểm tra được: " + error, Theme.Red); return; }
            if (release == null) { SetUpdateStatus("Chưa có bản phát hành nào trên GitHub.", Theme.Muted); return; }
            if (!newer) { SetUpdateStatus("Bạn đang dùng bản mới nhất (" + Updates.Current + ") · kiểm tra lúc " + DateTime.Now.ToString("HH:mm dd/MM"), Theme.GreenBright); return; }
            _ready = release;
            _checkButton.Text = "Cập nhật ngay";
            SetUpdateStatus("Phiên bản " + release.Version + " đã tải xong, sẵn sàng cập nhật.", Theme.Blue);
            OfferUpdate(manual);
        }
        private void OfferUpdate(bool manual)
        {
            if (_ready == null || _offering || _disposed) return;
            if (_clickRunning) { _offerAfterRun = true; return; }
            _offering = true;
            DialogResult answer;
            using (UpdateDialog dialog = new UpdateDialog(_ready))
                answer = Visible ? dialog.ShowDialog(this) : dialog.ShowDialog();
            _offering = false;
            if (answer != DialogResult.OK) return;
            string problem = Updates.FolderProblem();
            if (problem != null) { MessageBox.Show(this, problem, "Auto", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Updates.PendingInstall = _ready;
            Updates.ReopenWindow = Visible || manual;
            ExitAuto();
        }
        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Program.ShowMessage) ShowPage(_selected);
            else if (message.Msg == AutoPasteValueApp.Program.WM_SHOWME) ShowPage(PastePage);
            else if (message.Msg == VietDang.AutoClick.Program.ShowMessage) ShowPage(ClickPage);
            base.WndProc(ref message);
        }
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            bool exiting = _exit || e.CloseReason != CloseReason.UserClosing;
            if (!_click.SaveForClose(exiting)) { _exit = false; Updates.PendingInstall = null; e.Cancel = true; ShowPage(ClickPage); return; }
            if (!exiting) { e.Cancel = true; Hide(); return; }
            base.OnFormClosing(e);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true; _updateTimer.Dispose(); _click.StopRun(); _tray.Visible = false; _tray.Dispose(); _menu.Dispose();
            }
            base.Dispose(disposing);
            if (disposing) { _iconOn.Dispose(); _iconOff.Dispose(); }
        }
    }
}
