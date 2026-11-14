using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace VietDang.AutoClick
{
    public enum ClickButton { Left, Right }
    public enum ClickAction { Single, Double, Hold }
    public enum RepeatMode { Count, Duration, UntilStopped }
    public enum DurationUnit { Seconds = 1, Minutes = 60, Hours = 3600 }

    public class ClickStep
    {
        public int X { get; set; }
        public int Y { get; set; }
        public ClickButton Button { get; set; }
        public ClickAction Action { get; set; }
        public int DelayMs { get; set; }
        public int HoldMs { get; set; }
        public ClickStep() { DelayMs = 500; HoldMs = 1000; }
        public ClickStep Copy() { return (ClickStep)MemberwiseClone(); }
        public void Validate()
        {
            if (!Enum.IsDefined(typeof(ClickButton), Button) || !Enum.IsDefined(typeof(ClickAction), Action) ||
                DelayMs < 0 || DelayMs > 3600000 || HoldMs < 10 || HoldMs > 3600000)
                throw new InvalidDataException("Tọa độ có thông số chuột hoặc thời gian không hợp lệ.");
        }
    }

    public class Scenario
    {
        public string Name { get; set; }
        public RepeatMode Mode { get; set; }
        public int RepeatCount { get; set; }
        public int DurationSeconds { get; set; }
        public DurationUnit TimeUnit { get; set; }
        public List<ClickStep> Steps { get; set; }
        public Scenario() { Name = "Kịch bản mới"; RepeatCount = 1; DurationSeconds = 60; TimeUnit = DurationUnit.Seconds; Steps = new List<ClickStep>(); }
        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80 || Steps == null || Steps.Count > 10000 ||
                !Enum.IsDefined(typeof(RepeatMode), Mode) || RepeatCount < 1 || RepeatCount > 1000000 ||
                !Enum.IsDefined(typeof(DurationUnit), TimeUnit) || DurationSeconds < (int)TimeUnit || DurationSeconds > 86400 || DurationSeconds % (int)TimeUnit != 0)
                throw new InvalidDataException("Kịch bản có tên, giới hạn chạy hoặc danh sách tọa độ không hợp lệ.");
            foreach (ClickStep step in Steps)
            {
                if (step == null) throw new InvalidDataException("Dòng tọa độ bị thiếu dữ liệu.");
                step.Validate();
            }
        }
        public Scenario Copy()
        {
            Scenario copy = (Scenario)MemberwiseClone();
            copy.Steps = new List<ClickStep>();
            foreach (ClickStep step in Steps) copy.Steps.Add(step.Copy());
            return copy;
        }
        public override string ToString() { return Name; }
    }

    public class AppSettings
    {
        public List<Scenario> Scenarios { get; set; }
        public int SelectedIndex { get; set; }
        public int StartKey { get; set; }
        public int StopKey { get; set; }
        public AppSettings() { Scenarios = new List<Scenario>(); SelectedIndex = -1; }
        public void Validate()
        {
            if (Scenarios == null || Scenarios.Count > 1000) throw new InvalidDataException("Danh sách kịch bản không hợp lệ.");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Scenario scenario in Scenarios)
            {
                if (scenario == null) throw new InvalidDataException("Kịch bản bị thiếu dữ liệu.");
                scenario.Validate();
                if (!names.Add(scenario.Name)) throw new InvalidDataException("Tên kịch bản bị trùng.");
            }
            if ((StartKey != 0 || StopKey != 0) &&
                (!Hotkeys.IsValid((Keys)StartKey) || !Hotkeys.IsValid((Keys)StopKey) || StartKey == StopKey))
                throw new InvalidDataException("Hai phím tắt chưa hợp lệ hoặc bị trùng nhau.");
        }
    }

    internal static class SettingsStore
    {
        private const string LegacyKey = @"Software\VietDang\AutoClick";
        internal static string DefaultPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VietDang", "V-TOOL", "autoclick.xml"); } }
        internal static string Serialize(AppSettings settings)
        {
            settings.Validate();
            using (StringWriter writer = new StringWriter())
            {
                new XmlSerializer(typeof(AppSettings)).Serialize(writer, settings);
                string xml = writer.ToString();
                if (xml.Length > 4000000) throw new InvalidDataException("Dữ liệu kịch bản quá lớn để lưu.");
                return xml;
            }
        }
        internal static AppSettings Deserialize(string xml)
        {
            if (xml.Length > 4000000) throw new InvalidDataException("Dữ liệu kịch bản quá lớn.");
            using (XmlReader reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
                { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4000000 }))
            {
                AppSettings settings = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(reader);
                settings.Validate();
                return settings;
            }
        }
        internal static AppSettings Load(out string warning, string filePath = null)
        {
            warning = null;
            bool migrate = filePath == null;
            filePath = Path.GetFullPath(filePath ?? DefaultPath);
            if (File.Exists(filePath) || File.Exists(filePath + ".bak"))
            {
                try { return Deserialize(File.ReadAllText(filePath)); }
                catch (Exception ex)
                {
                    if (!IsDataError(ex) && !(ex is IOException)) throw;
                    if (!File.Exists(filePath + ".bak")) throw;
                    AppSettings restored = Deserialize(File.ReadAllText(filePath + ".bak"));
                    warning = "Đã khôi phục kịch bản từ bản lưu dự phòng.";
                    return restored;
                }
            }
            if (!migrate) return new AppSettings();
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(LegacyKey))
            {
                string value = key == null ? null : key.GetValue("Settings") as string;
                string backup = key == null ? null : key.GetValue("SettingsBackup") as string;
                if (value == null && backup == null) return new AppSettings();
                AppSettings settings;
                try { settings = Deserialize(value ?? backup); }
                catch (Exception ex)
                {
                    if (!IsDataError(ex) || backup == null) throw;
                    settings = Deserialize(backup);
                    warning = "Đã nhập kịch bản từ bản lưu dự phòng của ứng dụng cũ.";
                }
                Save(settings, filePath);
                return settings;
            }
        }
        private static bool IsDataError(Exception ex)
        {
            return ex is InvalidOperationException || ex is InvalidDataException || ex is XmlException;
        }
        internal static void Save(AppSettings settings, string filePath = null)
        {
            string xml = Serialize(settings);
            filePath = Path.GetFullPath(filePath ?? DefaultPath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            string temporary = filePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (StreamWriter writer = new StreamWriter(stream, Encoding.Unicode))
                {
                    writer.Write(xml); writer.Flush(); stream.Flush(true);
                }
                if (File.Exists(filePath))
                {
                    string backupPath = filePath + ".bak";
                    try { Deserialize(File.ReadAllText(filePath)); }
                    catch (Exception ex)
                    {
                        if (!IsDataError(ex)) throw;
                        backupPath = filePath + ".damaged." + Guid.NewGuid().ToString("N") + ".xml";
                    }
                    File.Replace(temporary, filePath, backupPath, true);
                }
                else File.Move(temporary, filePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }

    internal static class Hotkeys
    {
        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr window, int id);
        internal static bool IsValid(Keys keys)
        {
            Keys key = keys & Keys.KeyCode;
            if ((keys & ~(Keys.KeyCode | Keys.Control | Keys.Alt | Keys.Shift)) != 0) return false;
            if (key >= Keys.F1 && key <= Keys.F24) return true;
            return (keys & (Keys.Control | Keys.Alt)) != 0 &&
                ((key >= Keys.A && key <= Keys.Z) || (key >= Keys.D0 && key <= Keys.D9));
        }
        internal static bool Register(IntPtr window, int id, Keys keys)
        {
            uint modifiers = 0x4000;
            if ((keys & Keys.Control) != 0) modifiers |= 2;
            if ((keys & Keys.Alt) != 0) modifiers |= 1;
            if ((keys & Keys.Shift) != 0) modifiers |= 4;
            return IsValid(keys) && RegisterHotKey(window, id, modifiers, (uint)(keys & Keys.KeyCode));
        }
        internal static string Describe(Keys keys)
        {
            if (keys == Keys.None) return "Chưa cài";
            return new KeysConverter().ConvertToString(keys);
        }
    }

    internal interface IMouseOutput
    {
        bool UserInputReleased { get; }
        void Press(ClickStep step);
        void Release(ClickButton button);
    }

    internal sealed class WindowsMouse : IMouseOutput
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)]
        private struct Input { public uint Type; public MouseInput Mouse; }
        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, Input[] inputs, int size);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        public bool UserInputReleased
        {
            get
            {
                foreach (int key in new int[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 1, 2 })
                    if ((GetAsyncKeyState(key) & 0x8000) != 0) return false;
                return true;
            }
        }
        internal static bool IsOnScreen(Point point)
        {
            foreach (Screen screen in Screen.AllScreens) if (screen.Bounds.Contains(point)) return true;
            return false;
        }
        public void Press(ClickStep step)
        {
            if (!IsOnScreen(new Point(step.X, step.Y))) throw new InvalidOperationException("Tọa độ nằm ngoài màn hình hiện tại. Hãy chọn lại tọa độ.");
            Rectangle bounds = SystemInformation.VirtualScreen;
            Input move = new Input { Mouse = new MouseInput {
                X = (int)(((long)(step.X - bounds.Left) * 65536 + 32768) / bounds.Width),
                Y = (int)(((long)(step.Y - bounds.Top) * 65536 + 32768) / bounds.Height), Flags = 0xC001 } };
            Input down = new Input { Mouse = new MouseInput { Flags = step.Button == ClickButton.Left ? 2u : 8u } };
            Send(new Input[] { move, down });
        }
        public void Release(ClickButton button)
        {
            Send(new Input[] { new Input { Mouse = new MouseInput { Flags = button == ClickButton.Left ? 4u : 16u } } });
        }
        private static void Send(Input[] inputs)
        {
            if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(Input))) != inputs.Length)
                throw new InvalidOperationException("Windows không nhận được thao tác chuột. Kiểm tra cửa sổ đích và quyền chạy của hai ứng dụng.");
        }
    }

    internal sealed class RunResult
    {
        internal string Error;
        internal bool Stopped, TimeLimit;
        internal long Loops;
    }

    internal sealed class ClickRunner : IDisposable
    {
        private readonly IMouseOutput _mouse;
        private readonly ManualResetEvent _stop = new ManualResetEvent(false);
        private readonly object _inputLock = new object();
        private readonly Stopwatch _clock = new Stopwatch();
        private Thread _thread;
        private ClickButton? _pressed;
        private Scenario _scenario;
        private bool _timeExpired;
        private volatile bool _waitingForRelease;
        [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
        [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);
        internal event Action<long, int> Progress;
        internal event Action<RunResult> Finished;
        internal bool IsRunning { get { return _thread != null && _thread.IsAlive; } }
        internal TimeSpan Elapsed { get { return _clock.Elapsed; } }
        internal bool WaitingForRelease { get { return _waitingForRelease; } }
        internal ClickRunner(IMouseOutput mouse) { _mouse = mouse; }
        internal void Start(Scenario scenario)
        {
            if (IsRunning) throw new InvalidOperationException("Kịch bản đang chạy.");
            scenario.Validate();
            if (scenario.Steps.Count == 0) throw new InvalidOperationException("Hãy thêm ít nhất một tọa độ.");
            _scenario = scenario.Copy();
            _stop.Reset();
            _timeExpired = false;
            _clock.Reset();
            _waitingForRelease = true;
            _thread = new Thread(Run) { IsBackground = true, Name = "AutoClick runner" };
            _thread.Start();
        }
        internal void Stop()
        {
            _stop.Set();
            lock (_inputLock) Release();
        }
        private bool Ended()
        {
            return _timeExpired || _stop.WaitOne(0) || (_scenario.Mode == RepeatMode.Duration && _clock.ElapsedMilliseconds >= (long)_scenario.DurationSeconds * 1000);
        }
        private bool Wait(int milliseconds)
        {
            if (Ended()) return false;
            bool reachesDeadline = false;
            if (_scenario.Mode == RepeatMode.Duration)
            {
                long remaining = Math.Max(0, (long)_scenario.DurationSeconds * 1000 - _clock.ElapsedMilliseconds);
                reachesDeadline = milliseconds >= remaining;
                milliseconds = (int)Math.Min(milliseconds, remaining);
            }
            bool stopped = _stop.WaitOne(milliseconds);
            if (reachesDeadline) _timeExpired = true;
            return !stopped && !Ended();
        }
        private void Release()
        {
            if (!_pressed.HasValue) return;
            _mouse.Release(_pressed.Value);
            _pressed = null;
        }
        private void Run()
        {
            RunResult result = new RunResult();
            bool precise = false;
            try
            {
                // Do not turn Ctrl/Alt/Shift from the start shortcut into modified mouse clicks.
                while (!_mouse.UserInputReleased && !_stop.WaitOne(10)) { }
                _waitingForRelease = false;
                // Windows rounds waits up to its ~15.6 ms tick by default; short delays and holds need 1 ms steps.
                precise = timeBeginPeriod(1) == 0;
                _clock.Start();
                while (!Ended() && (_scenario.Mode != RepeatMode.Count || result.Loops < _scenario.RepeatCount))
                {
                    for (int i = 0; i < _scenario.Steps.Count; i++)
                    {
                        if (Ended()) break;
                        if (Progress != null) Progress(result.Loops, i);
                        ClickStep step = _scenario.Steps[i];
                        int clicks = step.Action == ClickAction.Double ? 2 : 1;
                        for (int click = 0; click < clicks && !Ended(); click++)
                        {
                            lock (_inputLock)
                            {
                                if (Ended()) break;
                                // Track before SendInput so a partial native failure still attempts button-up.
                                _pressed = step.Button;
                                _mouse.Press(step);
                            }
                            int hold = step.Action == ClickAction.Hold ? step.HoldMs : Math.Max(1, Math.Min(30, SystemInformation.DoubleClickTime / 8));
                            bool keepGoing = Wait(hold);
                            lock (_inputLock) Release();
                            if (!keepGoing) break;
                            if (click + 1 < clicks && !Wait(Math.Max(1, Math.Min(50, SystemInformation.DoubleClickTime / 4)))) break;
                        }
                        if (Ended() || !Wait(step.DelayMs)) break;
                        if (i == _scenario.Steps.Count - 1) result.Loops++;
                    }
                }
                result.Stopped = _stop.WaitOne(0);
                result.TimeLimit = !result.Stopped && _scenario.Mode == RepeatMode.Duration;
            }
            catch (Exception ex) { result.Error = ex.Message; }
            finally
            {
                try { lock (_inputLock) Release(); }
                catch (Exception ex) { result.Error = ex.Message; }
                if (precise) timeEndPeriod(1);
                _clock.Stop();
                _waitingForRelease = false;
                if (Finished != null) Finished(result);
            }
        }
        public void Dispose()
        {
            Stop();
            if (_thread != null && !_thread.Join(2000)) throw new InvalidOperationException("Luồng nhấp chuột chưa dừng. Hãy thử Dừng ngay.");
            _stop.Dispose();
        }
    }
}
