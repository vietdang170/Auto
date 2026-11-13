using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using VietDang.Design;

namespace AutoPasteValueApp
{
    internal static class Program
    {
        public static readonly int WM_SHOWME = RegisterWindowMessage("AutoPasteValue_ShowMe_Msg_2026");

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int RegisterWindowMessage(string lpString);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [STAThread]
        private static void Main(string[] args)
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, "AutoPasteValue_SingleInstance_Mutex_2026", out createdNew))
            {
                if (!createdNew)
                {
                    // Đã có tiến trình chạy ngầm, kích hoạt cửa sổ hiện tại
                    PostMessage((IntPtr)0xFFFF, WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                bool startMinimized = false;
                foreach (string arg in args)
                {
                    if (arg.Equals("/minimized", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("/startup", StringComparison.OrdinalIgnoreCase) ||
                        arg.Equals("-minimized", StringComparison.OrdinalIgnoreCase))
                    {
                        startMinimized = true;
                        break;
                    }
                }

                Application.Run(new TrayAppContext(startMinimized));
            }
        }
    }

    /// <summary>
    /// ApplicationContext chuyên dụng cho Tray Application: Duy trì vòng lặp tin nhắn bền bỉ,
    /// cho phép ứng dụng chạy hoàn toàn dưới khay hệ thống mà không phụ thuộc vào hiển thị Form.
    /// </summary>
    public class TrayAppContext : ApplicationContext
    {
        private readonly MainForm _form;

        public TrayAppContext(bool startMinimized)
        {
            _form = new MainForm();
            // Kích hoạt việc tạo Window Handle ngay lập tức để đăng ký Clipboard Listener và Hotkey
            IntPtr forceHandle = _form.Handle;

            if (!startMinimized)
            {
                _form.ShowAndActivate();
            }
        }
    }

    public class MainForm : Form
    {
        private readonly bool _embedded;
        // Isolated instances (integration checks) never touch the registry, global hotkeys or the clipboard.
        private readonly bool _isolated;
        internal event Action ShowRequested;
        internal event Action HideRequested;
        internal event Action ExitRequested;
        internal event Action StateChanged;
        internal bool PasteEnabled { get { return _isEnabled; } }
        #region Win32 API
        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern uint GetClipboardSequenceNumber();

        [DllImport("user32.dll")]
        private static extern IntPtr GetClipboardOwner();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint format, IntPtr data);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll")]
        private static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GlobalFree(IntPtr memory);

        [DllImport("ole32.dll")]
        private static extern int OleGetClipboard(out System.Runtime.InteropServices.ComTypes.IDataObject data);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int count);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

        [DllImport("dwmapi.dll")]
        private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

        [DllImport("oleacc.dll")]
        private static extern int AccessibleObjectFromWindow(IntPtr hwnd, uint objectId,
            ref Guid interfaceId, [MarshalAs(UnmanagedType.Interface)] out object result);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        private const int WM_CLIPBOARDUPDATE = 0x031D;
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 9001;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;
        private const uint MOD_NOREPEAT = 0x4000;
        #endregion

        #region Fields
        private bool _isEnabled = true;
        private bool _autoStart = false;
        private bool _playSound = false;
        private bool _trimSpaces = false;
        private int _hotkeyIndex = 0;
        private bool _isRealExit = false;
        private bool _listening = true;
        private bool _hotkeyFailed;

        private uint _myLastClipboardSeq = 0;
        private uint _pendingClipboardSeq;
        private int _clipboardAttempts;
        private bool _processingClipboard;
        private readonly System.Windows.Forms.Timer _clipboardTimer = new System.Windows.Forms.Timer { Interval = 40 };
        private readonly HashSet<string> _excludedApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // UI Controls
        private NotifyIcon _notifyIcon;
        private ContextMenuStrip _contextMenu;
        private ToolStripMenuItem _menuItemStatus;
        private ToolStripMenuItem _menuItemToggle;
        private ToolStripMenuItem _menuItemAutoStart;
        private ToolStripMenuItem _menuItemSound;
        private ToolStripMenuItem _menuItemOpen;
        private ToolStripMenuItem _menuItemExit;

        private Icon _iconActive;
        private Icon _iconInactive;

        private Label _lblStatus;
        private Label _lblInfo;
        private UiButton _btnToggle;
        private Pill _statePill;
        private ComboBox _cboHotkey;
        private Label _lblHotkeyHint;
        private CheckBox _chkSound;
        private CheckBox _chkTrim;

        private ListBox _lstExcludedApps;
        private Label _lblNoApps;
        private Pill _appsPill;

        // Only shown when Auto Paste runs as its own window.
        private CheckBox _chkAutoStart;
        private Button _btnClose;
        private Button _btnExit;

        private const string RegSubKey = @"Software\AutoPasteValue";
        private static readonly Color Green = Theme.GreenBright;
        private static readonly Color Red = Theme.Red;
        private static readonly Color Muted = Theme.Muted;
        #endregion

        public MainForm() : this(false, false) { }
        internal MainForm(bool embedded, bool isolated)
        {
            _embedded = embedded;
            _isolated = isolated;
            if (isolated) _isEnabled = false; else LoadSettings();
            InitializeIcons();
            InitializeComponent();
            InitializeTray();
            _clipboardTimer.Tick += ClipboardTimerTick;
            UpdateStatusUI();
            UpdateHotkeyHint();
        }

        #region Icons
        private void InitializeIcons()
        {
            _iconActive = CreateTrayIcon(true, 32);
            _iconInactive = CreateTrayIcon(false, 32);
        }

        internal static Icon CreateTrayIcon(bool active, int size)
        {
            using (Bitmap bmp = new Bitmap(size, size))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.ScaleTransform(size / 32f, size / 32f);

                    Color bg = active ? Color.FromArgb(34, 197, 94) : Color.FromArgb(148, 163, 184);
                    using (SolidBrush brush = new SolidBrush(bg))
                    {
                        g.FillEllipse(brush, 1, 1, 30, 30);
                    }

                    using (Pen border = new Pen(active ? Color.FromArgb(22, 163, 74) : Color.FromArgb(100, 116, 139), 1.5f))
                    {
                        g.DrawEllipse(border, 1, 1, 29, 29);
                    }
                    g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                    using (Font font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel))
                    using (SolidBrush textBrush = new SolidBrush(Color.White))
                    using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    {
                        g.DrawString("V", font, textBrush, new RectangleF(0, 1, 32, 30), format);
                    }
                }

                IntPtr hIcon = bmp.GetHicon();
                Icon icon = (Icon)Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
                return icon;
            }
        }
        #endregion

        #region Form Lifecycle
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_isolated) return;
            // A failed listener must not take the whole app down; the status card reports it instead.
            _listening = AddClipboardFormatListener(this.Handle);
            if (!_listening) Trace.TraceWarning("Clipboard listener unavailable: Win32 error {0}", Marshal.GetLastWin32Error());
            RegisterCurrentHotkey();
            if (_notifyIcon != null) UpdateStatusUI();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            // Another app may have released the shortcut since the last attempt.
            if (Visible && _hotkeyFailed) RegisterCurrentHotkey();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            _clipboardTimer.Stop();
            RemoveClipboardFormatListener(this.Handle);
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _clipboardTimer.Dispose();
                if (_notifyIcon != null) _notifyIcon.Dispose();
                if (_contextMenu != null) _contextMenu.Dispose();
                if (_iconActive != null) _iconActive.Dispose();
                if (_iconInactive != null) _iconInactive.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_isRealExit && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                MinimizeToTray();
                return;
            }

            _clipboardTimer.Stop();

            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }

            base.OnFormClosing(e);
        }

        private void MinimizeToTray()
        {
            if (_embedded) { if (HideRequested != null) HideRequested(); return; }
            this.Hide();
        }

        public void ShowAndActivate()
        {
            if (_embedded) { if (ShowRequested != null) ShowRequested(); return; }
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.ShowInTaskbar = true;
            this.Activate();
            this.BringToFront();
        }

        private void ExitApplication()
        {
            if (_embedded) { if (ExitRequested != null) ExitRequested(); return; }
            _isRealExit = true;
            this.Close();
            Application.Exit();
        }
        #endregion

        #region Clipboard Processing
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CLIPBOARDUPDATE)
            {
                QueueClipboardChange();
            }
            else if (m.Msg == WM_HOTKEY)
            {
                if (m.WParam.ToInt32() == HOTKEY_ID)
                {
                    ToggleState();
                }
            }
            else if (m.Msg == Program.WM_SHOWME)
            {
                ShowAndActivate();
            }

            base.WndProc(ref m);
        }

        private void QueueClipboardChange()
        {
            uint seq = GetClipboardSequenceNumber();
            if (!_isEnabled || _isRealExit || seq == 0 || seq == _myLastClipboardSeq) return;
            if (seq != _pendingClipboardSeq)
            {
                _pendingClipboardSeq = seq;
                _clipboardAttempts = 0;
            }
            if (_clipboardAttempts < 50) _clipboardTimer.Start();
        }

        private void ClipboardTimerTick(object sender, EventArgs e)
        {
            // OLE reads can pump messages; never allow a nested read/write operation.
            if (_processingClipboard) return;
            _clipboardTimer.Stop();
            if (!_isEnabled || _isRealExit) return;

            uint seq = _pendingClipboardSeq;
            bool complete = false;
            _processingClipboard = true;
            _clipboardAttempts++;
            try
            {
                complete = ProcessClipboardChange(seq);
            }
            catch (ExternalException ex)
            {
                Trace.TraceWarning("Clipboard temporarily unavailable: {0}", ex.Message);
            }
            finally
            {
                _processingClipboard = false;
            }

            if (!_isEnabled || _isRealExit || IsDisposed) return;
            uint latest = GetClipboardSequenceNumber();
            if (latest != seq && latest != _myLastClipboardSeq)
                QueueClipboardChange();
            else if (!complete && _clipboardAttempts < 50)
                _clipboardTimer.Start();
        }

        private bool ProcessClipboardChange(uint seq)
        {
            if (GetClipboardSequenceNumber() != seq) return true;
            Application.OleRequired();
            bool? allowed = CanNormalizeClipboardSource();
            if (allowed != true) return allowed.HasValue;

            // WinForms GetDataObject sleeps on contention; let our timer retry instead.
            System.Runtime.InteropServices.ComTypes.IDataObject sourceData;
            int result = OleGetClipboard(out sourceData);
            if (result < 0 || sourceData == null) return false;
            try
            {
                // Read before opening the clipboard ourselves: delayed rendering needs the source app.
                return ProcessClipboardData(new DataObject(sourceData), seq);
            }
            finally
            {
                if (Marshal.IsComObject(sourceData)) Marshal.ReleaseComObject(sourceData);
            }
        }

        private bool ProcessClipboardData(IDataObject data, uint seq)
        {
            string[] formats = data.GetFormats(false);
            if (formats.Length == 0) return false;
            // Password managers mark secrets with these formats so clipboard tools leave them alone.
            if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing", false) ||
                data.GetDataPresent("Clipboard Viewer Ignore", false)) return true;
            if (data.GetDataPresent(DataFormats.FileDrop, false) ||
                data.GetDataPresent("FileGroupDescriptorW", false) ||
                data.GetDataPresent("FileGroupDescriptor", false) ||
                data.GetDataPresent("FileContents", false)) return true;
            if (!data.GetDataPresent(DataFormats.UnicodeText) && !data.GetDataPresent(DataFormats.Text))
                return true;

            bool rich = false;
            bool richText = false;
            foreach (string format in formats)
            {
                if (!IsPlainTextFormat(format)) rich = true;
                if (format.Equals(DataFormats.Html, StringComparison.OrdinalIgnoreCase) ||
                    format.Equals(DataFormats.Rtf, StringComparison.OrdinalIgnoreCase) ||
                    format.StartsWith("Biff", StringComparison.OrdinalIgnoreCase) ||
                    format.Equals("XML Spreadsheet", StringComparison.OrdinalIgnoreCase))
                    richText = true;
            }
            if (!rich) return TrimPlainText(data, seq);
            // Excel cell selections include bitmap previews alongside their text/table formats.
            if (!richText && (data.GetDataPresent(DataFormats.Bitmap, false) ||
                data.GetDataPresent(DataFormats.Dib, false) || data.GetDataPresent("Format17", false)))
                return true;

            string text = data.GetData(DataFormats.UnicodeText) as string;
            if (text == null) text = data.GetData(DataFormats.Text) as string;
            if (text == null) return false;
            bool? allowed = CanNormalizeClipboardSource();
            if (allowed != true) return allowed.HasValue;
            if (!_isEnabled || _isRealExit || IsDisposed) return true;
            return TryWritePlainText(_trimSpaces ? TrimCells(text) : text, seq);
        }

        // Plain-text copies already have no formatting; they are only rewritten when trimming changes them.
        private bool TrimPlainText(IDataObject data, uint seq)
        {
            if (!_trimSpaces) return true;
            string text = data.GetData(DataFormats.UnicodeText) as string;
            if (text == null) text = data.GetData(DataFormats.Text) as string;
            if (text == null) return true;
            string trimmed = TrimCells(text);
            if (trimmed == text) return true;
            bool? allowed = CanNormalizeClipboardSource();
            if (allowed != true) return allowed.HasValue;
            if (!_isEnabled || _isRealExit || IsDisposed) return true;
            return TryWritePlainText(trimmed, seq);
        }

        // Works like Excel's TRIM on every cell: spaces at both ends are removed and runs of spaces inside become one.
        // Tabs and line breaks (the cell and row separators) are kept. Non-breaking spaces from web pages count as spaces.
        internal static string TrimCells(string text)
        {
            StringBuilder result = new StringBuilder(text.Length);
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i < text.Length && text[i] != '\t' && text[i] != '\r' && text[i] != '\n') continue;
                bool pending = false, written = false;
                for (int j = start; j < i; j++)
                {
                    char c = text[j];
                    if (c == ' ' || c == '\u00A0') { pending = written; continue; }
                    if (pending) { result.Append(' '); pending = false; }
                    result.Append(c);
                    written = true;
                }
                if (i < text.Length) result.Append(text[i]);
                start = i + 1;
            }
            return result.ToString();
        }

        private bool? CanNormalizeClipboardSource()
        {
            IntPtr source = GetClipboardOwner();
            if (source == IntPtr.Zero) source = GetForegroundWindow();
            string process = GetProcessName(source);
            if (IsProcessExcluded(process)) return false;
            if (!process.Equals("EXCEL.exe", StringComparison.OrdinalIgnoreCase)) return true;

            uint sourcePid;
            GetWindowThreadProcessId(source, out sourcePid);
            bool? allowed = null;
            bool found = false;
            EnumWindows(delegate(IntPtr window, IntPtr unused)
            {
                uint pid;
                GetWindowThreadProcessId(window, out pid);
                if (pid != sourcePid) return true;
                EnumChildWindows(window, delegate(IntPtr child, IntPtr ignored)
                {
                    StringBuilder name = new StringBuilder(64);
                    GetClassName(child, name, name.Capacity);
                    if (name.ToString() != "EXCEL7") return true;
                    found = true;
                    object nativeWindow = null, excel = null, commandBars = null, painter = null;
                    try
                    {
                        Guid dispatch = new Guid("00020400-0000-0000-C000-000000000046");
                        if (AccessibleObjectFromWindow(child, 0xFFFFFFF0, ref dispatch, out nativeWindow) < 0 || nativeWindow == null)
                            return false;
                        excel = nativeWindow.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, nativeWindow, null);
                        int mode = Convert.ToInt32(excel.GetType().InvokeMember("CutCopyMode", BindingFlags.GetProperty, null, excel, null));
                        // Replacing Excel's clipboard cancels Cut and Format Painter, so require explicit normal Copy.
                        if (mode != 1) { allowed = false; return false; }
                        commandBars = excel.GetType().InvokeMember("CommandBars", BindingFlags.GetProperty, null, excel, null);
                        // Excel's Format Painter rejects GetPressedMso; its built-in command 108 exposes State.
                        painter = commandBars.GetType().InvokeMember("FindControl", BindingFlags.InvokeMethod, null, commandBars,
                            new object[] { 1, 108, Type.Missing, false });
                        if (painter != null)
                            allowed = Convert.ToInt32(painter.GetType().InvokeMember("State", BindingFlags.GetProperty, null, painter, null)) == 0;
                    }
                    catch (TargetInvocationException ex) { Trace.TraceWarning("Excel state unavailable: {0}", ex.InnerException); }
                    catch (ExternalException ex) { Trace.TraceWarning("Excel state unavailable: {0}", ex.Message); }
                    finally
                    {
                        if (painter != null && Marshal.IsComObject(painter)) Marshal.ReleaseComObject(painter);
                        if (commandBars != null && Marshal.IsComObject(commandBars)) Marshal.ReleaseComObject(commandBars);
                        if (excel != null && Marshal.IsComObject(excel)) Marshal.ReleaseComObject(excel);
                        if (nativeWindow != null && Marshal.IsComObject(nativeWindow)) Marshal.ReleaseComObject(nativeWindow);
                    }
                    return false;
                }, IntPtr.Zero);
                return !found;
            }, IntPtr.Zero);
            return allowed;
        }

        private bool TryWritePlainText(string text, uint expectedSeq)
        {
            char[] chars = (text + '\0').ToCharArray();
            IntPtr memory = GlobalAlloc(0x0002, new UIntPtr(checked((uint)chars.Length * 2)));
            if (memory == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
            try
            {
                IntPtr pointer = GlobalLock(memory);
                if (pointer == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
                try { Marshal.Copy(chars, 0, pointer, chars.Length); }
                finally { GlobalUnlock(memory); }

                if (!OpenClipboard(this.Handle)) return false;
                try
                {
                    // Check while locked so a newer copy cannot be replaced with an older snapshot.
                    if (GetClipboardSequenceNumber() != expectedSeq) return true;
                    if (!EmptyClipboard()) throw new Win32Exception(Marshal.GetLastWin32Error());
                    if (SetClipboardData(13, memory) == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    memory = IntPtr.Zero; // Windows now owns this immediately rendered Unicode buffer.
                    _myLastClipboardSeq = GetClipboardSequenceNumber();
                    return true;
                }
                finally { CloseClipboard(); }
            }
            finally
            {
                if (memory != IntPtr.Zero) GlobalFree(memory);
            }
        }

        private static bool IsPlainTextFormat(string f)
        {
            if (string.IsNullOrEmpty(f)) return true;
            return f.Equals("System.String", StringComparison.OrdinalIgnoreCase) ||
                   f.Equals(DataFormats.UnicodeText, StringComparison.OrdinalIgnoreCase) ||
                   f.Equals(DataFormats.Text, StringComparison.OrdinalIgnoreCase) ||
                   f.Equals(DataFormats.OemText, StringComparison.OrdinalIgnoreCase) ||
                   f.Equals(DataFormats.Locale, StringComparison.OrdinalIgnoreCase) ||
                   f.Equals(DataFormats.StringFormat, StringComparison.OrdinalIgnoreCase);
        }
        #endregion

        #region State Toggle & UI Update
        public void ToggleState()
        {
            _isEnabled = !_isEnabled;
            if (!_isEnabled) _clipboardTimer.Stop();
            UpdateStatusUI();

            if (_playSound)
            {
                try { SystemSounds.Asterisk.Play(); } catch { }
            }

            SaveSettings();
            if (StateChanged != null) StateChanged();
        }

        private void UpdateStatusUI()
        {
            string word = !_listening ? "KHÔNG THEO DÕI ĐƯỢC CLIPBOARD" : _isEnabled ? "ĐANG BẬT" : "ĐANG TẮT";
            _lblStatus.Text = "●  " + word;
            _lblStatus.ForeColor = !_listening ? Red : _isEnabled ? Green : Muted;
            _lblInfo.Text = !_listening ? "Hãy thoát hẳn ứng dụng rồi mở lại."
                : _isEnabled ? "Nội dung copy được chuyển thành giá trị thuần (giữ Tab và xuống dòng) ngay khi bạn bấm Ctrl + C."
                : "Đang tạm dừng: nội dung copy giữ nguyên định dạng gốc.";

            // The filled green button is always the action that turns the feature on.
            _btnToggle.Text = _isEnabled ? "Tắt Auto Paste" : "Bật Auto Paste";
            _btnToggle.GlyphText = _isEnabled ? Glyph.Pause : Glyph.Play;
            _btnToggle.Kind = _isEnabled ? ButtonKind.Secondary : ButtonKind.Success;

            _statePill.Text = !_listening ? "LỖI CLIPBOARD" : _isEnabled ? "ĐANG BẬT" : "ĐANG TẮT";
            if (!_listening) _statePill.SetColors(Theme.RedSoft, Theme.RedLine, Theme.Red);
            else if (_isEnabled) _statePill.SetColors(Theme.GreenSoft, Theme.GreenLine, Theme.Green);
            else _statePill.SetColors(Theme.Divider, Theme.Border, Theme.Muted);

            Icon icon = _isEnabled ? _iconActive : _iconInactive;
            _notifyIcon.Icon = icon;
            _notifyIcon.Text = _isEnabled ? "Auto Paste Value: ĐANG BẬT" : "Auto Paste Value: ĐANG TẮT";
            if (!_embedded) this.Icon = icon;

            _menuItemStatus.Text = _isEnabled ? "Trạng thái: ĐANG BẬT" : "Trạng thái: ĐANG TẮT";
            _menuItemStatus.ForeColor = _isEnabled ? Green : Muted;
            _menuItemToggle.Text = _isEnabled ? "Tắt chế độ tự dán giá trị" : "Bật chế độ tự dán giá trị";
        }
        #endregion

        #region Hotkey Management
        private string GetHotkeyName()
        {
            switch (_hotkeyIndex)
            {
                case 1: return "Win + Alt + V";
                case 2: return "Ctrl + Alt + V";
                case 3: return "Ctrl + Shift + F9";
                default: return "";
            }
        }

        private void RegisterCurrentHotkey()
        {
            if (_isolated || !IsHandleCreated) return;
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            _hotkeyFailed = false;

            uint mod = MOD_NOREPEAT;
            uint vk = 0;

            switch (_hotkeyIndex)
            {
                case 1:
                    mod |= MOD_WIN | MOD_ALT;
                    vk = 0x56;
                    break;
                case 2:
                    mod |= MOD_CONTROL | MOD_ALT;
                    vk = 0x56;
                    break;
                case 3:
                    mod |= MOD_CONTROL | MOD_SHIFT;
                    vk = 0x78;
                    break;
                default:
                    UpdateHotkeyHint();
                    return;
            }

            // Keep the saved choice when another app holds the keys right now: it is retried the next
            // time this page is shown, instead of silently erasing what the user picked.
            _hotkeyFailed = !RegisterHotKey(this.Handle, HOTKEY_ID, mod, vk);
            UpdateHotkeyHint();
        }

        private void UpdateHotkeyHint()
        {
            if (_lblHotkeyHint == null) return;
            if (_hotkeyFailed)
            {
                _lblHotkeyHint.ForeColor = Red;
                _lblHotkeyHint.Text = GetHotkeyName() + " đang được ứng dụng khác sử dụng nên chưa hoạt động. Hãy chọn tổ hợp khác.";
            }
            else
            {
                _lblHotkeyHint.ForeColor = Muted;
                _lblHotkeyHint.Text = _hotkeyIndex == 0
                    ? "Chọn một tổ hợp phím để bật/tắt nhanh từ bất kỳ ứng dụng nào."
                    : "Bấm " + GetHotkeyName() + " ở bất kỳ đâu để bật/tắt nhanh.";
            }
        }
        #endregion

        #region Excluded Apps Management
        private void RefreshExcludedListBox()
        {
            if (_lstExcludedApps == null) return;
            _lstExcludedApps.BeginUpdate();
            _lstExcludedApps.Items.Clear();
            foreach (string app in _excludedApps)
            {
                _lstExcludedApps.Items.Add(app);
            }
            _lstExcludedApps.EndUpdate();
            bool empty = _excludedApps.Count == 0;
            _lstExcludedApps.Visible = !empty;
            _lblNoApps.Visible = empty;
            _appsPill.Text = _excludedApps.Count + " ứng dụng";
        }

        private bool IsProcessExcluded(string processName)
        {
            if (string.IsNullOrEmpty(processName)) return false;
            string clean = processName.Trim();
            if (_excludedApps.Contains(clean)) return true;

            if (!clean.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return _excludedApps.Contains(clean + ".exe");
            }
            return false;
        }

        private static string GetProcessName(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return "";

                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (pid == 0) return "";

                using (Process proc = Process.GetProcessById((int)pid))
                {
                    return proc.ProcessName + ".exe";
                }
            }
            catch
            {
                return "";
            }
        }

        private void AddExcludedApp(string appExeName)
        {
            if (string.IsNullOrEmpty(appExeName)) return;
            string name = appExeName.Trim();
            if (!_excludedApps.Contains(name))
            {
                _excludedApps.Add(name);
                RefreshExcludedListBox();
                SaveSettings();
            }
        }

        private void RemoveSelectedExcludedApp()
        {
            if (_lstExcludedApps != null && _lstExcludedApps.SelectedItem != null)
            {
                string selected = _lstExcludedApps.SelectedItem.ToString();
                _excludedApps.Remove(selected);
                RefreshExcludedListBox();
                SaveSettings();
            }
        }

        private void BrowseAndAddExe()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "Chương trình (*.exe)|*.exe|Tất cả tệp (*.*)|*.*";
                ofd.Title = "Chọn file thực thi của ứng dụng cần loại trừ";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    string fileName = Path.GetFileName(ofd.FileName);
                    AddExcludedApp(fileName);
                }
            }
        }

        // Apps with a button on the taskbar: visible, unowned top-level windows that are not tool windows and not
        // cloaked (hidden Store apps and background hosts are cloaked). Auto itself is left out.
        private List<KeyValuePair<string, string>> GetTaskbarApps()
        {
            List<KeyValuePair<string, string>> apps = new List<KeyValuePair<string, string>>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int self = Process.GetCurrentProcess().Id;
            EnumWindows((hwnd, parameter) =>
            {
                if (!IsWindowVisible(hwnd)) return true;
                int style = GetWindowLong(hwnd, -20);
                bool appWindow = (style & 0x00040000) != 0;
                if (!appWindow && ((style & 0x00000080) != 0 || (style & 0x08000000) != 0 || GetWindow(hwnd, 4) != IntPtr.Zero)) return true;
                int cloaked;
                if (DwmGetWindowAttribute(hwnd, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                StringBuilder title = new StringBuilder(256);
                if (GetWindowText(hwnd, title, title.Capacity) == 0) return true;
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                string exe = ProcessExe(pid);
                // Store apps run inside ApplicationFrameHost; the app itself owns the CoreWindow child.
                if (string.Equals(exe, "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                {
                    uint host = pid;
                    EnumChildWindows(hwnd, (child, ignored) =>
                    {
                        uint childPid;
                        GetWindowThreadProcessId(child, out childPid);
                        if (childPid == host) return true;
                        pid = childPid;
                        return false;
                    }, IntPtr.Zero);
                    exe = ProcessExe(pid);
                }
                if (exe == null || pid == (uint)self || _excludedApps.Contains(exe) || !seen.Add(exe)) return true;
                apps.Add(new KeyValuePair<string, string>(exe, title.ToString()));
                return true;
            }, IntPtr.Zero);
            apps.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            return apps;
        }

        private static string ProcessExe(uint pid)
        {
            try { using (Process process = Process.GetProcessById((int)pid)) return process.ProcessName + ".exe"; }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
        }

        private void ShowRunningAppsPicker()
        {
            List<KeyValuePair<string, string>> apps = GetTaskbarApps();
            using (Form picker = new Form())
            {
                picker.Text = "Chọn ứng dụng đang mở";
                picker.AutoScaleDimensions = new SizeF(96, 96);
                picker.AutoScaleMode = AutoScaleMode.Dpi;
                picker.ClientSize = new Size(460, 440);
                picker.StartPosition = FormStartPosition.CenterParent;
                picker.FormBorderStyle = FormBorderStyle.FixedDialog;
                picker.MaximizeBox = false;
                picker.MinimizeBox = false;
                picker.ShowInTaskbar = false;
                picker.Font = Theme.Regular;
                picker.BackColor = Theme.Window;

                TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(14, 12, 14, 12) };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
                layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
                layout.Controls.Add(new Label
                {
                    Text = "Các ứng dụng đang có cửa sổ trên thanh tác vụ. Chọn ứng dụng để Auto Paste giữ nguyên định dạng khi copy từ đó.",
                    Dock = DockStyle.Fill, Margin = Padding.Empty, Font = Theme.Small, ForeColor = Muted, UseMnemonic = false
                }, 0, 0);

                Frame frame = new Frame { Dock = DockStyle.Fill };
                float scale;
                using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) scale = screen.DpiX / 96f;
                ListBox list = new ListBox
                {
                    Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false,
                    DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = (int)Math.Round(44 * scale), Font = Theme.Regular
                };
                foreach (KeyValuePair<string, string> app in apps) list.Items.Add(app.Key);
                list.DrawItem += (sender, e) =>
                {
                    if (e.Index < 0) return;
                    bool selected = (e.State & DrawItemState.Selected) != 0;
                    Graphics g = e.Graphics;
                    float s = g.DpiX / 96f;
                    using (SolidBrush back = new SolidBrush(selected ? Theme.BlueSoft : Theme.Surface)) g.FillRectangle(back, e.Bounds);
                    using (Pen line = new Pen(Theme.Divider)) g.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
                    int icon = (int)(40 * s), half = e.Bounds.Height / 2;
                    TextRenderer.DrawText(g, Glyph.App, Theme.Icon(12f), new Rectangle(e.Bounds.X, e.Bounds.Y, icon, e.Bounds.Height), selected ? Theme.Blue : Theme.Muted, Theme.Centered);
                    Rectangle text = new Rectangle(e.Bounds.X + icon + (int)(4 * s), e.Bounds.Y, Math.Max(0, e.Bounds.Width - icon - (int)(12 * s)), half);
                    TextRenderer.DrawText(g, apps[e.Index].Key, Theme.Semibold, new Rectangle(text.X, text.Y + (int)(3 * s), text.Width, half - (int)(3 * s)),
                        selected ? Theme.BlueDark : Theme.Text, Theme.Line | TextFormatFlags.EndEllipsis);
                    TextRenderer.DrawText(g, apps[e.Index].Value, Theme.Small, new Rectangle(text.X, e.Bounds.Y + half, text.Width, half - (int)(4 * s)),
                        Theme.Muted, Theme.Line | TextFormatFlags.EndEllipsis);
                };
                Label none = new Label
                {
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Muted, UseMnemonic = false, Visible = apps.Count == 0,
                    Text = "Không có ứng dụng nào khác đang mở trên thanh tác vụ\n(hoặc tất cả đã nằm trong danh sách loại trừ)."
                };
                list.Visible = apps.Count > 0;
                frame.Controls.Add(list);
                frame.Controls.Add(none);
                layout.Controls.Add(frame, 0, 1);

                FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty, Padding = new Padding(0, 12, 0, 0), WrapContents = false };
                UiButton add = new UiButton("Thêm vào danh sách", Glyph.Add, ButtonKind.Primary) { Height = 34, Enabled = false };
                UiButton cancel = new UiButton("Hủy", null, ButtonKind.Secondary) { Height = 34, MinimumFit = 80, Margin = new Padding(0, 0, 8, 0), DialogResult = DialogResult.Cancel };
                buttons.Controls.Add(add);
                buttons.Controls.Add(cancel);
                layout.Controls.Add(buttons, 0, 2);
                picker.Controls.Add(layout);
                picker.CancelButton = cancel;

                Action addSelected = () =>
                {
                    if (list.SelectedIndex < 0) return;
                    AddExcludedApp(apps[list.SelectedIndex].Key);
                    picker.Close();
                };
                list.SelectedIndexChanged += (sender, e) => add.Enabled = list.SelectedIndex >= 0;
                list.DoubleClick += (sender, e) => addSelected();
                add.Click += (sender, e) => addSelected();
                picker.ShowDialog(this);
            }
        }
        #endregion

        #region Settings & Registry
        private void LoadSettings()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegSubKey))
                {
                    if (key != null)
                    {
                        _isEnabled = Convert.ToInt32(key.GetValue("Enabled", 1)) == 1;
                        _playSound = Convert.ToInt32(key.GetValue("Sound", 0)) == 1;
                        _trimSpaces = Convert.ToInt32(key.GetValue("TrimSpaces", 0)) == 1;
                        _hotkeyIndex = Convert.ToInt32(key.GetValue("HotkeyIndex", 0));
                        // Ctrl + Shift + V (former choice 4) is no longer offered: it hid "paste as plain text" in other apps.
                        if (_hotkeyIndex < 0 || _hotkeyIndex > 3) _hotkeyIndex = 0;

                        string excluded = key.GetValue("ExcludedApps", "") as string;
                        if (!string.IsNullOrEmpty(excluded))
                        {
                            string[] apps = excluded.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (string a in apps)
                            {
                                string trimmed = a.Trim();
                                if (!string.IsNullOrEmpty(trimmed))
                                {
                                    _excludedApps.Add(trimmed);
                                }
                            }
                        }
                    }
                }

                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (runKey != null)
                    {
                        _autoStart = runKey.GetValue("AutoPasteValue") != null;
                    }
                }
            }
            catch { }
        }

        private void SaveSettings()
        {
            if (_isolated) return;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegSubKey))
                {
                    if (key != null)
                    {
                        key.SetValue("Enabled", _isEnabled ? 1 : 0, RegistryValueKind.DWord);
                        key.SetValue("Sound", _playSound ? 1 : 0, RegistryValueKind.DWord);
                        key.SetValue("TrimSpaces", _trimSpaces ? 1 : 0, RegistryValueKind.DWord);
                        key.DeleteValue("Notification", false);
                        key.SetValue("HotkeyIndex", _hotkeyIndex, RegistryValueKind.DWord);
                        key.SetValue("ExcludedApps", string.Join(";", _excludedApps), RegistryValueKind.String);
                    }
                }

                if (_menuItemAutoStart != null)
                {
                    _menuItemAutoStart.Checked = _autoStart;
                }
                if (_menuItemSound != null)
                {
                    _menuItemSound.Checked = _playSound;
                }
            }
            catch { }
        }

        private void SetAutoStartRegistry(bool enable)
        {
            if (_isolated) return;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string val = "\"" + Application.ExecutablePath + "\" /minimized";
                            key.SetValue("AutoPasteValue", val, RegistryValueKind.String);
                        }
                        else
                        {
                            key.DeleteValue("AutoPasteValue", false);
                        }
                    }
                }
                _autoStart = enable;
                SaveSettings();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Không thể cấu hình khởi động cùng Windows: " + ex.Message, "Lỗi", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        #endregion

        #region UI Layout & Tray
        private void InitializeComponent()
        {
            this.Text = "Auto Paste Value";
            this.AutoScaleDimensions = new SizeF(96, 96);
            this.AutoScaleMode = AutoScaleMode.Dpi;
            this.ClientSize = new Size(840, _embedded ? 620 : 664);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.Font = Theme.Regular;
            this.BackColor = Theme.Window;

            TableLayoutPanel root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4, Padding = new Padding(12), BackColor = Theme.Window };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 222));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, _embedded ? 0 : 44));

            // Page header with the live state on the right.
            Card header = Kit.PageHeader(Glyph.Paste, "Tự động dán giá trị",
                "Auto Paste · Chuyển nội dung copy thành giá trị thuần, giữ Tab và xuống dòng cho Excel và Google Sheets");
            _statePill = new Pill { Dot = true, Font = Theme.SmallBold };
            header.AddHeader(_statePill);
            root.Controls.Add(header, 0, 0);
            root.SetColumnSpan(header, 2);

            // State card: big status, what it means, and the one main action.
            Card state = new Card(Glyph.Flash, "Trạng thái hoạt động") { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 5, 10) };
            TableLayoutPanel stateBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            stateBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            stateBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            stateBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            stateBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _lblStatus = new Label { Dock = DockStyle.Fill, Margin = Padding.Empty, Font = Theme.Status, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false };
            _lblInfo = new Label { Dock = DockStyle.Fill, Margin = Padding.Empty, Font = Theme.Small, ForeColor = Theme.Muted, UseMnemonic = false };
            _btnToggle = new UiButton("Tắt Auto Paste", Glyph.Pause, ButtonKind.Secondary)
            {
                AutoFit = false,
                Height = 38,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                Font = new Font("Segoe UI Semibold", 10f)
            };
            _btnToggle.Click += (sender, e) => ToggleState();
            stateBody.Controls.Add(_lblStatus, 0, 0);
            stateBody.Controls.Add(_lblInfo, 0, 1);
            stateBody.Controls.Add(_btnToggle, 0, 2);
            state.Controls.Add(stateBody);
            root.Controls.Add(state, 0, 1);

            // Shortcut and sound.
            Card shortcut = new Card(Glyph.Keyboard, "Phím tắt và tùy chọn") { Dock = DockStyle.Fill, Margin = new Padding(5, 0, 0, 10) };
            TableLayoutPanel options = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty };
            options.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            options.Controls.Add(Kit.FieldLabel("Phím tắt bật/tắt"), 0, 0);

            _cboHotkey = new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = Padding.Empty,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = Theme.Regular
            };
            _cboHotkey.Items.AddRange(new object[] {
                "Không dùng phím tắt",
                "Win + Alt + V",
                "Ctrl + Alt + V",
                "Ctrl + Shift + F9"
            });
            _cboHotkey.SelectedIndex = (_hotkeyIndex >= 0 && _hotkeyIndex < _cboHotkey.Items.Count) ? _hotkeyIndex : 0;
            _cboHotkey.SelectedIndexChanged += (sender, e) =>
            {
                _hotkeyIndex = _cboHotkey.SelectedIndex;
                RegisterCurrentHotkey();
                UpdateHotkeyHint();
                SaveSettings();
            };
            options.Controls.Add(_cboHotkey, 0, 1);

            _lblHotkeyHint = new Label
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 4, 0, 0),
                UseMnemonic = false,
                Font = Theme.Small,
                ForeColor = Muted
            };
            options.Controls.Add(_lblHotkeyHint, 0, 2);

            _chkTrim = new ToggleSwitch { Checked = _trimSpaces };
            _chkTrim.CheckedChanged += (sender, e) =>
            {
                _trimSpaces = _chkTrim.Checked;
                SaveSettings();
            };
            options.Controls.Add(SwitchRow(_chkTrim, "Tự động xóa dấu cách thừa (TRIM)"), 0, 3);

            _chkSound = new ToggleSwitch { Checked = _playSound };
            _chkSound.CheckedChanged += (sender, e) =>
            {
                _playSound = _chkSound.Checked;
                SaveSettings();
            };
            options.Controls.Add(SwitchRow(_chkSound, "Phát âm thanh khi bật / tắt"), 0, 4);
            shortcut.Controls.Add(options);
            root.Controls.Add(shortcut, 1, 1);

            // Excluded apps: list on the left, its actions on the right, a hint underneath.
            Card exclude = new Card(Glyph.Apps, "Ứng dụng loại trừ") { Dock = DockStyle.Fill, Margin = Padding.Empty };
            _appsPill = new Pill { Font = Theme.MonoSmall };
            exclude.AddTitleBadge(_appsPill);
            exclude.AddHeader(Kit.Note("Giữ nguyên định dạng gốc khi copy"));
            TableLayoutPanel excludeBody = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            excludeBody.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            excludeBody.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 178));
            excludeBody.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            excludeBody.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

            Frame frame = new Frame { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 12, 0) };
            float scale;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) scale = screen.DpiX / 96f;
            _lstExcludedApps = new ListBox
            {
                Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None,
                IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = (int)Math.Round(34 * scale),
                Font = Theme.Regular
            };
            _lstExcludedApps.DrawItem += DrawExcludedApp;
            _lblNoApps = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = Muted,
                UseMnemonic = false,
                Text = "Chưa có ứng dụng nào trong danh sách.\nBấm “App đang mở” hoặc “Chọn file .exe” để thêm."
            };
            frame.Controls.Add(_lstExcludedApps);
            frame.Controls.Add(_lblNoApps);
            RefreshExcludedListBox();

            FlowLayoutPanel excludeButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, FlowDirection = FlowDirection.TopDown, WrapContents = false };
            UiButton addRunning = new UiButton("App đang mở", Glyph.Add, ButtonKind.Secondary) { AutoFit = false, Size = new Size(178, 34), Margin = new Padding(0, 0, 0, 8) };
            addRunning.Click += (sender, e) => ShowRunningAppsPicker();
            UiButton browse = new UiButton("Chọn file .exe…", Glyph.OpenFile, ButtonKind.Secondary) { AutoFit = false, Size = new Size(178, 34), Margin = new Padding(0, 0, 0, 8) };
            browse.Click += (sender, e) => BrowseAndAddExe();
            UiButton remove = new UiButton("Xóa đã chọn", Glyph.Delete, ButtonKind.Danger) { AutoFit = false, Size = new Size(178, 34), Margin = Padding.Empty };
            remove.Click += (sender, e) => RemoveSelectedExcludedApp();
            excludeButtons.Controls.Add(addRunning);
            excludeButtons.Controls.Add(browse);
            excludeButtons.Controls.Add(remove);

            InfoNote note = new InfoNote(Glyph.Info, "Khi copy từ các ứng dụng trong danh sách, Auto Paste để nguyên định dạng gốc (màu chữ, bảng, công thức…).")
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 10, 0, 0)
            };
            excludeBody.Controls.Add(frame, 0, 0);
            excludeBody.Controls.Add(excludeButtons, 1, 0);
            excludeBody.Controls.Add(note, 0, 1);
            excludeBody.SetColumnSpan(note, 2);
            exclude.Controls.Add(excludeBody);
            root.Controls.Add(exclude, 0, 2);
            root.SetColumnSpan(exclude, 2);

            // Window-only row: the integrated Auto app has its own tray menu and Settings page.
            TableLayoutPanel bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 10, 0, 0), Visible = !_embedded };
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            bottom.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _chkAutoStart = new CheckBox { Text = "Khởi động cùng Windows", AutoSize = true, Checked = _autoStart, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
            _chkAutoStart.CheckedChanged += (sender, e) => SetAutoStartRegistry(_chkAutoStart.Checked);
            Label credit = new Label { Text = "Thiết kế bởi VietDang · Zalo: 0974 318 666", AutoSize = true, Anchor = AnchorStyles.Right, Font = new Font("Segoe UI", 7.5f), ForeColor = Muted, Margin = new Padding(0, 0, 10, 0) };
            _btnClose = new UiButton("Đóng", null, ButtonKind.Secondary) { Height = 32, MinimumFit = 90, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 8, 0) };
            _btnClose.Click += (sender, e) => MinimizeToTray();
            _btnExit = new UiButton("Thoát hẳn", Glyph.Power, ButtonKind.Danger) { Height = 32, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
            _btnExit.Click += (sender, e) => ExitApplication();
            bottom.Controls.Add(_chkAutoStart, 0, 0);
            bottom.Controls.Add(credit, 1, 0);
            bottom.Controls.Add(_btnClose, 2, 0);
            bottom.Controls.Add(_btnExit, 3, 0);
            root.Controls.Add(bottom, 0, 3);
            root.SetColumnSpan(bottom, 2);

            this.Controls.Add(root);
            if (!_embedded) this.AcceptButton = _btnClose;
        }

        // A switch with its caption; clicking the caption flips the switch too.
        private static FlowLayoutPanel SwitchRow(CheckBox toggle, string caption)
        {
            FlowLayoutPanel row = new FlowLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, WrapContents = false };
            toggle.Margin = new Padding(0, 4, 10, 0);
            toggle.AccessibleName = caption;
            Label text = new Label { Text = caption, AutoSize = true, Margin = new Padding(0, 6, 0, 0), ForeColor = Theme.Body, Cursor = Cursors.Hand, UseMnemonic = false };
            text.Click += (sender, e) => toggle.Checked = !toggle.Checked;
            row.Controls.Add(toggle);
            row.Controls.Add(text);
            return row;
        }

        private void DrawExcludedApp(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool selected = (e.State & DrawItemState.Selected) != 0;
            Graphics g = e.Graphics;
            float scale = g.DpiX / 96f;
            using (SolidBrush back = new SolidBrush(selected ? Theme.BlueSoft : Theme.Surface)) g.FillRectangle(back, e.Bounds);
            using (Pen line = new Pen(Theme.Divider)) g.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            int icon = (int)(36 * scale);
            TextRenderer.DrawText(g, Glyph.App, Theme.Icon(11f), new Rectangle(e.Bounds.X + (int)(4 * scale), e.Bounds.Y, icon, e.Bounds.Height),
                selected ? Theme.Blue : Theme.Muted, Theme.Centered);
            TextRenderer.DrawText(g, _lstExcludedApps.Items[e.Index].ToString(), selected ? Theme.Semibold : Theme.Regular,
                new Rectangle(e.Bounds.X + icon + (int)(8 * scale), e.Bounds.Y, Math.Max(0, e.Bounds.Width - icon - (int)(16 * scale)), e.Bounds.Height),
                selected ? Theme.BlueDark : Theme.Body, Theme.Line | TextFormatFlags.EndEllipsis);
        }

        private void InitializeTray()
        {
            _contextMenu = new ContextMenuStrip();

            _menuItemStatus = new ToolStripMenuItem("Trạng thái: ĐANG BẬT") { Enabled = false, Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            _menuItemToggle = new ToolStripMenuItem("Bật / Tắt") { Font = new Font("Segoe UI", 9f, FontStyle.Bold) };
            _menuItemToggle.Click += (s, e) => ToggleState();

            _menuItemAutoStart = new ToolStripMenuItem("Khởi động cùng Windows") { CheckOnClick = true, Checked = _autoStart };
            _menuItemAutoStart.Click += (s, e) => SetAutoStartRegistry(_menuItemAutoStart.Checked);

            _menuItemSound = new ToolStripMenuItem("Âm thanh thông báo") { CheckOnClick = true, Checked = _playSound };
            _menuItemSound.Click += (s, e) =>
            {
                _playSound = _menuItemSound.Checked;
                if (_chkSound != null) _chkSound.Checked = _playSound;
                SaveSettings();
            };

            _menuItemOpen = new ToolStripMenuItem("Mở bảng điều khiển");
            _menuItemOpen.Click += (s, e) => ShowAndActivate();

            _menuItemExit = new ToolStripMenuItem("Thoát hẳn");
            _menuItemExit.Click += (s, e) => ExitApplication();

            _contextMenu.Items.Add(_menuItemStatus);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_menuItemToggle);
            _contextMenu.Items.Add(_menuItemAutoStart);
            _contextMenu.Items.Add(_menuItemSound);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_menuItemOpen);
            _contextMenu.Items.Add(_menuItemExit);

            _notifyIcon = new NotifyIcon
            {
                Icon = _iconActive,
                Text = "Auto Paste Value: ĐANG BẬT",
                Visible = !_embedded,
                ContextMenuStrip = _contextMenu
            };

            _notifyIcon.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left)
                {
                    ToggleState();
                }
            };

            _notifyIcon.DoubleClick += (s, e) => ShowAndActivate();
        }
        #endregion
    }
}
