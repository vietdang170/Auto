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

[assembly: AssemblyTitle("V-TOOL")]
[assembly: AssemblyProduct("V-TOOL")]
[assembly: AssemblyCompany("VietDang")]

namespace VietDang.VTool
{
    internal static class Program
    {
        internal const string AppName = "V-TOOL";
        internal const string SettingsKey = @"Software\VietDang\V-TOOL";
        internal static readonly int ShowMessage = RegisterWindowMessage("VietDang_VTOOL_Show_2026");
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
            using (Mutex suite = new Mutex(true, "VietDang_VTOOL_2026", out created))
            {
                if (!created) { PostMessage(new IntPtr(0xFFFF), ShowMessage, IntPtr.Zero, IntPtr.Zero); return; }
                bool pasteAvailable, clickAvailable;
                using (Mutex paste = new Mutex(true, "AutoPasteValue_SingleInstance_Mutex_2026", out pasteAvailable))
                using (Mutex click = new Mutex(true, "VietDang_AutoClick_2026", out clickAvailable))
                {
                    if (!pasteAvailable || !clickAvailable)
                    {
                        MessageBox.Show("Hãy thoát hẳn Auto, AutoPasteValue hoặc AutoClick cũ ở khay hệ thống, rồi mở " + AppName + ".", AppName);
                        return;
                    }
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    bool minimized = Array.Exists(args, delegate(string arg) { return arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase) || arg.Equals("/startup", StringComparison.OrdinalIgnoreCase) || arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase); });
                    Legacy.Migrate();
                    try { Startup.Refresh(); }
                    catch (Exception ex) { MessageBox.Show("Chưa cập nhật được đường dẫn khởi động cùng Windows:\n" + ex.Message, AppName); }
                    using (MainWindow form = new MainWindow())
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
            string folder = Path.Combine(Path.GetTempPath(), "V-TOOL.Trace." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                using (MainWindow form = new MainWindow(null, Path.Combine(folder, "autoclick.xml"), true))
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
                        form.BeginInvoke(new Action(form.ExitApp));
                    };
                    form.Shown += delegate { Boot.Mark("Cửa sổ hiện lên (Shown)"); form.BeginInvoke(new Action(delegate { Application.Idle += idle; })); };
                    form.Show();
                    Application.Run(context);
                }
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }

    // Startup timing: "V-TOOL.exe /trace" opens an isolated copy (no hotkeys, clipboard or registry), records how long
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

    // V-TOOL starts from its own Run entry. Entries left by the versions called Auto, and by the old AutoPasteValue app,
    // are moved to it.
    internal static class Startup
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        // Task Manager > Startup apps keeps its own on/off flag per Run entry; an odd first byte means switched off there.
        private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ValueName = "V-TOOL";
        private static readonly string[] LegacyNames = { "Auto", "AutoPasteValue" };
        private static string Command { get { return "\"" + Application.ExecutablePath + "\" /minimized"; } }
        private static bool HasLegacy(RegistryKey run)
        {
            foreach (string name in LegacyNames) if (run.GetValue(name) != null) return true;
            return false;
        }
        private static void DeleteLegacy(RegistryKey run) { foreach (string name in LegacyNames) run.DeleteValue(name, false); }
        private static bool HasEntry()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey))
                    return run != null && (run.GetValue(ValueName) != null || HasLegacy(run));
            }
            catch (Exception) { return false; }
        }
        // The entry is there but switched off in Task Manager, so Windows skips it at sign-in.
        internal static bool DisabledInTaskManager()
        {
            try
            {
                using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                {
                    byte[] flag = approved == null ? null : approved.GetValue(ValueName) as byte[];
                    return HasEntry() && flag != null && flag.Length > 0 && (flag[0] & 1) == 1;
                }
            }
            catch (Exception) { return false; }
        }
        // What Windows will really do at the next sign-in.
        internal static bool IsEnabled() { return HasEntry() && !DisabledInTaskManager(); }
        internal static void Set(bool enabled)
        {
            using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) run.SetValue(ValueName, Command, RegistryValueKind.String);
                else run.DeleteValue(ValueName, false);
                DeleteLegacy(run);
            }
            // Switching it on here also clears an "off" left in Task Manager; without its flag Windows runs the entry.
            using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                if (approved != null) approved.DeleteValue(ValueName, false);
        }
        // Moves an older entry to V-TOOL's own name and keeps the path current if the exe was moved or renamed.
        internal static void Refresh()
        {
            using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (run == null) return;
                bool legacy = HasLegacy(run);
                if (!legacy && run.GetValue(ValueName) == null) return;
                run.SetValue(ValueName, Command, RegistryValueKind.String);
                if (legacy) DeleteLegacy(run);
            }
        }
    }

    // Before the rename the app was called Auto. Its settings key and its AutoClick scripts are moved once to the
    // V-TOOL names, and the update downloads it kept are removed.
    internal static class Legacy
    {
        private const string OldKey = @"Software\VietDang\Auto";
        internal static void Migrate()
        {
            try
            {
                bool moved = false;
                using (RegistryKey old = Registry.CurrentUser.OpenSubKey(OldKey))
                {
                    if (old != null)
                    {
                        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKey))
                            foreach (string name in old.GetValueNames())
                                if (key.GetValue(name) == null) key.SetValue(name, old.GetValue(name), old.GetValueKind(name));
                        moved = true;
                    }
                }
                if (moved) Registry.CurrentUser.DeleteSubKeyTree(OldKey, false);
            }
            catch (Exception) { }
            string scripts = Path.GetDirectoryName(VietDang.AutoClick.SettingsStore.DefaultPath);
            MoveFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VietDang", "Auto"), scripts);
            try
            {
                string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VietDang", "Auto");
                if (Directory.Exists(downloads)) Directory.Delete(downloads, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        // Files already present in the new folder win; the old folder is removed only once everything is copied.
        private static void MoveFolder(string from, string to)
        {
            try
            {
                if (!Directory.Exists(from)) return;
                Directory.CreateDirectory(to);
                foreach (string file in Directory.GetFiles(from))
                {
                    string target = Path.Combine(to, Path.GetFileName(file));
                    if (!File.Exists(target)) File.Copy(file, target);
                }
                Directory.Delete(from, true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    // The app's own preferences, separate from the AutoPasteValue and AutoClick settings.
    internal static class UpdateSettings
    {
        private const string Key = Program.SettingsKey;
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

    internal sealed class MainWindow : Form
    {
        // Design sizes at 100 %. A page keeps at least PageMin* and scrolls in a smaller window.
        private const int NavWidth = 184, PageWidth = 840, PageHeight = 620, PageMinWidth = 840, PageMinHeight = 540, SettingsMinHeight = 620;
        private const string Author = "Thiết kế bởi VietDang\nZalo: 0974 318 666";
        // Page order follows the sidebar: AutoClick, AutoPasteValue, Add-in Office, then Settings.
        internal const int ClickPage = 0, PastePage = 1, AddinPage = 2, SettingsPage = 3;
        private readonly PasteForm _paste;
        private readonly ClickForm _click;
        private readonly bool _isolated;
        private readonly Panel _nav = new Panel { Dock = DockStyle.Left, Width = NavWidth, BackColor = Theme.Surface, Padding = new Padding(0, 0, 1, 0) };
        private readonly Panel _host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Window };
        private readonly RadioButton[] _navItems = new RadioButton[4];
        private readonly Panel[] _pages = new Panel[4];
        private readonly AddinPage _addins;
        private readonly CheckBox _startupBox = new ToggleSwitch { AccessibleName = "Mở " + Program.AppName + " khi đăng nhập Windows", Anchor = AnchorStyles.Right };
        private readonly CheckBox _updateBox = new ToggleSwitch { AccessibleName = "Tự động kiểm tra cập nhật hằng ngày", Anchor = AnchorStyles.Right };
        private int _lastTrayToggle;
        private readonly UiButton _checkButton = new UiButton("Kiểm tra ngay", Glyph.Sync, ButtonKind.Secondary) { Height = 32, Anchor = AnchorStyles.Right };
        private Label _updateStatus, _startupNote;
        private bool _checkingStartup;
        private readonly System.Windows.Forms.Timer _updateTimer = new System.Windows.Forms.Timer();
        private ReleaseInfo _ready;
        private bool _checking, _offering, _offerAfterRun, _startReported;
        private string _updatedSignal, _failedVersion;
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();
        private readonly ToolStripMenuItem _pasteToggle = new ToolStripMenuItem("AutoPasteValue");
        private readonly ToolStripMenuItem _startupItem = new ToolStripMenuItem("Khởi động cùng Windows");
        private readonly NotifyIcon _tray;
        private readonly Icon _iconOn, _iconOff;
        private int _selected;
        private bool _exit, _disposed, _modulesInitialized, _clickRunning, _syncingStartup, _placed;
        private FormWindowState _shownState = FormWindowState.Normal;

        internal MainWindow() : this(null, null, false) { }
        // isolated: integration checks must not touch the user's registry, global hotkeys or clipboard.
        internal MainWindow(VietDang.AutoClick.AppSettings clickSettings, string settingsPath, bool isolated)
        {
            _isolated = isolated;
            Text = Program.AppName;
            Font = Theme.Regular; BackColor = Theme.Window;
            // Resizable and maximizable; the first show fits it to the screen it opens on (PlaceOnScreen).
            FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true;
            StartPosition = FormStartPosition.Manual;
            ClientSize = new Size(NavWidth + PageWidth, PageHeight);
            Boot.Mark("Bắt đầu tạo cửa sổ");
            _iconOn = PasteForm.CreateTrayIcon(true, 32); _iconOff = PasteForm.CreateTrayIcon(false, 32);
            Icon = _iconOn;
            Boot.Mark("Vẽ icon khay");
            _paste = new PasteForm(true, isolated);
            Boot.Mark("Tạo trang AutoPasteValue");
            _click = new ClickForm(clickSettings, settingsPath, true);
            Boot.Mark("Tạo trang AutoClick");

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
            // Isolated runs look at an empty throw-away folder and no registry, so the user's Office is never read or changed.
            _addins = new AddinPage(isolated ? new OfficeEnvironment(null, null, Path.Combine(Path.GetTempPath(), "V-TOOL.Isolated.Office")) : OfficeEnvironment.Current);
            _pages[AddinPage].Controls.Add(_addins);
            BuildSettingsPage(_pages[SettingsPage]);
            Boot.Mark("Gắn các trang + trang Cài đặt");

            _menu.Items.Add("Mở " + Program.AppName, null, delegate { ShowPage(_selected); });
            _menu.Items.Add(_pasteToggle);
            _pasteToggle.Click += delegate { _paste.ToggleState(); };
            _menu.Items.Add(_startupItem);
            _startupItem.Click += delegate { SetStartup(!_startupItem.Checked); };
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("Thoát " + Program.AppName, null, delegate { ExitApp(); });
            _tray = new NotifyIcon { Icon = _iconOn, Text = Program.AppName, ContextMenuStrip = _menu, Visible = true };
            // A left click flips AutoPasteValue at once, like the old app. A double click opens the window; its first click has
            // already flipped AutoPasteValue, so that flip is undone and a double click never changes the state.
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
            _paste.ExitRequested += ExitApp;
            _paste.StateChanged += UpdateTray;
            _click.ShowRequested += delegate { ShowPage(ClickPage); };
            _click.HideRequested += delegate { Hide(); };
            _click.ExitRequested += ExitApp;
            _click.RunningChanged += delegate(bool running)
            {
                _clickRunning = running;
                UpdateTray();
                // An update offer that arrived during a run waits until the run is over.
                if (!running && _offerAfterRun) { _offerAfterRun = false; OfferUpdate(false); }
            };
            _navItems[0].Checked = true;
            UpdateTray();
            // Everything is built: scale it to the screen once, then allow the window to shrink below the design size,
            // where each page scrolls instead of squeezing its cards together.
            Dpi.Fit(this);
            for (int i = 0; i < _pages.Length; i++)
            {
                _pages[i].AutoScroll = true;
                _pages[i].AutoScrollMinSize = Dpi.Px(new Size(PageMinWidth, i == SettingsPage ? SettingsMinHeight : PageMinHeight));
            }
            MinimumSize = SizeFromClientSize(Dpi.Px(new Size(NavWidth + 480, 400)));
            // Both modules need window handles now: clipboard listening and hotkeys work while V-TOOL stays in the tray.
            IntPtr pasteHandle = _paste.Handle, clickHandle = _click.Handle;
            Boot.Mark("Tạo handle AutoPasteValue / AutoClick");
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
            string[] titles = { "AutoClick", "AutoPasteValue", "Add-in Office", "Cài đặt" };
            string[] glyphs = { Glyph.Mouse, Glyph.Paste, Glyph.Puzzle, Glyph.Settings };
            for (int i = 0; i < titles.Length; i++)
            {
                int index = i;
                NavItem item = new NavItem(titles[i], glyphs[i]) { Location = new Point(10, 12 + i * 42), Size = new Size(NavWidth - 21, 38) };
                item.CheckedChanged += delegate { if (item.Checked) SelectPage(index); };
                _navItems[i] = item;
                _nav.Controls.Add(item);
            }
            // Author line in the bottom-left corner; it can be selected and copied.
            TextBox author = Kit.CopyText(Author, Theme.Credit, Theme.Faint, Theme.Surface);
            author.Dock = DockStyle.Bottom;
            Panel corner = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(18, 0, 0, 12), BackColor = Theme.Surface };
            corner.Controls.Add(author);
            _nav.Controls.Add(corner);
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
                // Word or Excel may have been installed or removed outside V-TOOL since the tab was last seen.
                if (index == AddinPage) _addins.RefreshState();
                if (index == SettingsPage) RefreshStartupNote();
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
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 176));
            // The cards keep their height in a large window; the space left over stays below them.
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 230));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Kit.PageHeader(Glyph.Settings, "Cài đặt", ""), 0, 0);

            Card startup = new Card(Glyph.Power, "Khởi động cùng hệ thống") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            TableLayoutPanel startupRow = SettingRow("Mở " + Program.AppName + " khi đăng nhập Windows", StartupHint, _startupBox);
            _startupNote = (Label)startupRow.GetControlFromPosition(0, 1);
            startup.Controls.Add(startupRow);
            layout.Controls.Add(startup, 0, 1);

            Card update = new Card(Glyph.Sync, "Cập nhật phần mềm") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
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

            layout.Controls.Add(BuildSupportCard(), 0, 3);
            page.Controls.Add(layout);
        }
        // Author details and the donation QR code: text on the left, the code on the right (click it for a larger view).
        private Card BuildSupportCard()
        {
            Card card = new Card(Glyph.Heart, "Thông tin & ủng hộ tác giả") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            UiButton copy = new UiButton("Sao chép số tài khoản", Glyph.Copy, ButtonKind.Secondary) { Height = 28 };
            System.Windows.Forms.Timer reset = new System.Windows.Forms.Timer { Interval = 1500 };
            reset.Tick += delegate { reset.Stop(); copy.Text = "Sao chép số tài khoản"; };
            copy.Click += delegate
            {
                try { Clipboard.SetText(Donation.Account); copy.Text = "Đã sao chép"; reset.Stop(); reset.Start(); }
                catch (ExternalException) { }
            };
            copy.Disposed += delegate { reset.Dispose(); };
            card.AddHeader(copy);

            TableLayoutPanel columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            TableLayoutPanel text = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
            text.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) text.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            text.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            TextBox author = Kit.CopyText(Author, Theme.Small, Theme.Muted, Theme.Surface);
            author.Dock = DockStyle.Top;
            text.Controls.Add(author, 0, 0);
            text.Controls.Add(new Label { Text = "Ủng hộ tác giả", AutoSize = true, Font = Theme.Semibold, ForeColor = Theme.Text, UseMnemonic = false,
                Margin = new Padding(0, 12, 0, 2) }, 0, 1);
            text.Controls.Add(new Label { Text = "Nếu " + Program.AppName + " giúp ích cho công việc của bạn, hãy ủng hộ tác giả qua mã QR bên cạnh. Cảm ơn bạn!",
                Dock = DockStyle.Top, Height = 18, Font = Theme.Small, ForeColor = Theme.Muted, UseMnemonic = false, AutoEllipsis = true, Margin = Padding.Empty }, 0, 2);
            TextBox bank = Kit.CopyText("Ngân hàng: " + Donation.Bank + "\nChủ tài khoản: " + Donation.Holder + "\nSố tài khoản: " + Donation.AccountShown,
                Theme.Regular, Theme.Body, Theme.Surface);
            bank.Dock = DockStyle.Top;
            bank.Margin = new Padding(0, 8, 0, 0);
            text.Controls.Add(bank, 0, 3);
            columns.Controls.Add(text, 0, 0);

            QrTile qr = new QrTile(Donation.LoadQr()) { Dock = DockStyle.Fill, Margin = new Padding(16, 0, 0, 0) };
            columns.Controls.Add(qr, 1, 0);
            card.Controls.Add(columns);
            return card;
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
                catch (Exception ex) { MessageBox.Show(this, "Chưa đổi được thiết lập khởi động cùng Windows:\n" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                actual = Startup.IsEnabled();
            }
            _syncingStartup = true;
            try { _startupBox.Checked = actual; _startupItem.Checked = actual; }
            finally { _syncingStartup = false; }
            RefreshStartupNote();
        }
        private const string StartupHint = Program.AppName + " tự mở, thu nhỏ ở khay hệ thống mỗi khi bạn đăng nhập. Phím tắt và AutoPasteValue hoạt động ngay.";
        // Says when Windows will not open V-TOOL at sign-in because it was switched off in Task Manager. The registry is
        // read off the UI thread.
        private void RefreshStartupNote()
        {
            if (_isolated || _startupNote == null || _checkingStartup) return;
            _checkingStartup = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string text = StartupHint;
                Color color = Theme.Muted;
                if (Startup.DisabledInTaskManager())
                {
                    text = "Đang bị tắt trong Task Manager › Ứng dụng khởi động nên không tự mở. Bật công tắc bên cạnh để mở lại.";
                    color = Theme.Amber;
                }
                // Task Manager may have changed it while V-TOOL was running.
                bool enabled = Startup.IsEnabled();
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        _checkingStartup = false;
                        _startupNote.Text = text; _startupNote.ForeColor = color;
                        _syncingStartup = true;
                        try { _startupBox.Checked = enabled; _startupItem.Checked = enabled; }
                        finally { _syncingStartup = false; }
                    }));
                }
                catch (InvalidOperationException) { _checkingStartup = false; }
            });
        }
        // A grey icon means copies keep their formatting, exactly like the old AutoPasteValue tray icon.
        private void UpdateTray()
        {
            if (_disposed) return;
            bool pasting = _paste.PasteEnabled;
            _pasteToggle.Checked = pasting;
            _tray.Icon = pasting ? _iconOn : _iconOff; Icon = pasting ? _iconOn : _iconOff;
            _tray.Text = _clickRunning ? Program.AppName + ": AutoClick đang chạy" : pasting ? "AutoPasteValue ĐANG BẬT · nhấp để tắt" : "AutoPasteValue ĐANG TẮT · nhấp để bật";
        }
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Boot.Mark("OnShown");
            WarmUpPages();
            Boot.Mark("Chuẩn bị sẵn các tab");
            if (!_isolated) Updates.CleanUp();
            InitializeModules();
            Boot.Mark("Nạp kịch bản + phím tắt AutoClick");
            BeginInvoke(new Action(ReportStarted));
        }
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (_selected == AddinPage && !_disposed) _addins.RefreshState();
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
            Show();
            // Back from the taskbar the way it was left: maximized stays maximized.
            if (WindowState == FormWindowState.Minimized) WindowState = _shownState;
            Activate();
        }
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            PlaceOnScreen();
        }
        // First show: the size the window had last time (or the design size), never larger than the working area of the
        // screen it opens on, centred there. A 125 % or 150 % laptop screen is often smaller than the scaled design.
        private void PlaceOnScreen()
        {
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size size = Size;
            bool maximized = false;
            if (!_isolated)
            {
                try
                {
                    using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Program.SettingsKey))
                    {
                        int width = key == null ? 0 : Convert.ToInt32(key.GetValue("WindowWidth", 0));
                        int height = key == null ? 0 : Convert.ToInt32(key.GetValue("WindowHeight", 0));
                        if (width > 0 && height > 0) size = Dpi.Px(new Size(width, height));
                        maximized = key != null && Convert.ToInt32(key.GetValue("WindowMaximized", 0)) == 1;
                    }
                }
                catch (Exception) { }
            }
            size = new Size(Math.Max(MinimumSize.Width, Math.Min(size.Width, work.Width)), Math.Max(MinimumSize.Height, Math.Min(size.Height, work.Height)));
            Bounds = new Rectangle(work.Left + Math.Max(0, (work.Width - size.Width) / 2), work.Top + Math.Max(0, (work.Height - size.Height) / 2), size.Width, size.Height);
            if (maximized) WindowState = FormWindowState.Maximized;
            _placed = true;
        }
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState != FormWindowState.Minimized) _shownState = WindowState;
        }
        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) RememberPlacement();
        }
        // Stored at 100 %, so the next start fits a screen whose scale changed in between.
        private void RememberPlacement()
        {
            if (_isolated || !_placed || !IsHandleCreated) return;
            Rectangle normal = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Program.SettingsKey))
                {
                    key.SetValue("WindowWidth", (int)Math.Round(normal.Width / Dpi.Scale), RegistryValueKind.DWord);
                    key.SetValue("WindowHeight", (int)Math.Round(normal.Height / Dpi.Scale), RegistryValueKind.DWord);
                    key.SetValue("WindowMaximized", _shownState == FormWindowState.Maximized ? 1 : 0, RegistryValueKind.DWord);
                }
            }
            catch (Exception) { }
        }
        internal void ExitApp() { _exit = true; Close(); }

        // Called by Main before the window starts. signal: this run was started by an update; failed: an update was rolled back.
        internal void AfterStart(string signal, string failed)
        {
            _updatedSignal = signal; _failedVersion = failed;
            if (signal != null) SetUpdateStatus("Đã cập nhật lên phiên bản " + Updates.Current + ".", Theme.GreenBright);
            if (failed != null) SetUpdateStatus("Bản " + failed + " không khởi động được nên " + Program.AppName + " đã quay lại bản " + Updates.Current + ".", Theme.Red);
        }
        // Runs once the app is up (window shown, or modules ready when starting in the tray).
        private void ReportStarted()
        {
            if (_startReported) return;
            _startReported = true;
            Updates.ReportStarted(_updatedSignal);
            if (_failedVersion != null)
                MessageBox.Show(this, "Phiên bản " + _failedVersion + " không khởi động được, nên " + Program.AppName + " đã tự quay lại phiên bản " + Updates.Current
                    + ".\n" + Program.AppName + " sẽ không tự đề nghị lại bản này; bạn vẫn có thể bấm Kiểm tra ngay trong Cài đặt.", Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                        error = release.Problem ?? Updates.FolderProblem();
                        if (error == null) Updates.Download(release);
                    }
                }
                catch (Exception ex) { error = ex.Message; }
                try { BeginInvoke(new Action(delegate { FinishCheck(release, newer, error, manual); })); }
                catch (InvalidOperationException) { }
            }) { IsBackground = true, Name = "V-TOOL update check" };
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
            if (problem != null) { MessageBox.Show(this, problem, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            Updates.PendingInstall = _ready;
            Updates.ReopenWindow = Visible || manual;
            ExitApp();
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
            if (Visible) RememberPlacement();
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

    // Where donations go. The QR code is the author's Techcombank code, rebuilt module for module so it stays sharp at any size.
    internal static class Donation
    {
        internal const string Bank = "Techcombank", Holder = "DANG DINH VIET", Account = "858168", AccountShown = "8581 68";
        internal const string Resource = "VTool.DonateQr.png";
        internal static Bitmap LoadQr()
        {
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (stream == null) return null;
                using (Image image = Image.FromStream(stream)) return new Bitmap(image);
            }
        }
    }

    // White rounded tile with the QR code drawn at a whole number of pixels per module, so phones read it easily.
    internal sealed class QrTile : Painted
    {
        private const int Modules = 37;   // 33 modules of a version 4 code plus a 2-module white border (the card around it is white too)
        private readonly Bitmap _image;
        private readonly bool _zoomable;
        private readonly ToolTip _tip;
        internal QrTile(Bitmap image) : this(image, true) { }
        internal QrTile(Bitmap image, bool zoomable)
        {
            _image = image; _zoomable = zoomable && image != null;
            BackColor = Theme.Surface;
            if (_zoomable)
            {
                Cursor = Cursors.Hand;
                _tip = new ToolTip();
                _tip.SetToolTip(this, "Nhấp để phóng to mã QR");
            }
        }
        internal Rectangle CodeBounds
        {
            get
            {
                int side = Math.Min(ClientSize.Width, ClientSize.Height) - 4;
                int module = Math.Max(1, side / Modules), size = module * Modules;
                return new Rectangle((ClientSize.Width - size) / 2, (ClientSize.Height - size) / 2, size, size);
            }
        }
        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (!_zoomable) return;
            using (QrDialog dialog = new QrDialog(_image)) dialog.ShowDialog(FindForm());
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Behind(this));
            Rectangle code = CodeBounds;
            Rectangle frame = Rectangle.Inflate(code, 1, 1);
            Theme.Box(g, frame, 6f * Theme.Scale(g), Color.White, Theme.Border);
            if (_image == null) return;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(_image, code);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { if (_tip != null) _tip.Dispose(); if (_image != null) _image.Dispose(); }
            base.Dispose(disposing);
        }
    }

    // Larger copy of the code for scanning, with the account details underneath.
    internal sealed class QrDialog : Form
    {
        internal QrDialog(Bitmap image)
        {
            Text = "Ủng hộ tác giả";
            ClientSize = new Size(380, 470);
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
            Font = Theme.Regular; BackColor = Theme.Surface;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(16) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.Controls.Add(new QrTile(new Bitmap(image), false) { Dock = DockStyle.Fill }, 0, 0);
            TextBox details = Kit.CopyText(Donation.Bank + "\n" + Donation.Holder + "\n" + Donation.AccountShown, Theme.Semibold, Theme.Text, Theme.Surface);
            details.Dock = DockStyle.Fill; details.TextAlign = HorizontalAlignment.Center;
            layout.Controls.Add(details, 0, 1);
            UiButton close = new UiButton("Đóng", null, ButtonKind.Secondary) { Height = 32, MinimumFit = 90, Anchor = AnchorStyles.None, DialogResult = DialogResult.Cancel };
            layout.Controls.Add(close, 0, 2);
            Controls.Add(layout);
            CancelButton = close;
            Dpi.Fit(this);
        }
    }
}
