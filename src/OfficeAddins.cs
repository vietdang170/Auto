using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using VietDang.Design;

// "Add-in Office" tab: installs the V-TOOL add-ins for Word and Excel straight from their GitHub releases.
// The steps are the same as the setup.cmd files shipped in dotm.zip and xlam.zip, done by V-TOOL itself so nothing waits
// for a key press in a hidden console window. The add-ins keep themselves up to date once installed, so V-TOOL only
// installs and removes them.
namespace VietDang.VTool
{
    internal enum AddinState { Missing, Installed, Disabled }

    // Where Office keeps its settings and files. The real one is the current user; the integration checks use a
    // throw-away registry key and folder so the user's own Office is never touched.
    internal sealed class OfficeEnvironment
    {
        internal readonly RegistryKey User;     // stands for HKEY_CURRENT_USER; null = no registry at all
        internal readonly RegistryKey Machine;  // stands for HKEY_LOCAL_MACHINE (read only); may be null
        internal readonly string AppData;       // %APPDATA%
        internal OfficeEnvironment(RegistryKey user, RegistryKey machine, string appData) { User = user; Machine = machine; AppData = appData; }
        internal static OfficeEnvironment Current
        {
            get { return new OfficeEnvironment(Registry.CurrentUser, Registry.LocalMachine, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)); }
        }
    }

    internal abstract class OfficeAddin
    {
        internal abstract string App { get; }            // "Word" / "Excel"
        internal abstract string ProcessName { get; }    // WINWORD / EXCEL
        internal abstract string ZipUrl { get; }
        internal abstract string MainFile { get; }
        internal abstract string[] Files { get; }
        internal abstract string InstallFolder(OfficeEnvironment env);
        internal abstract AddinState State(OfficeEnvironment env);
        internal abstract void InstallFrom(OfficeEnvironment env, string source);
        internal abstract void Uninstall(OfficeEnvironment env);

        internal static readonly OfficeAddin Word = new WordAddin();
        internal static readonly OfficeAddin Excel = new ExcelAddin();

        internal bool IsRunning
        {
            get
            {
                Process[] running = Process.GetProcessesByName(ProcessName);
                foreach (Process process in running) process.Dispose();
                return running.Length > 0;
            }
        }
        internal string RunningMessage
        {
            get { return App + " đang mở. Hãy lưu tài liệu, đóng hết cửa sổ " + App + " rồi bấm lại."; }
        }

        // Downloads the zip from GitHub, takes the add-in files out of it and installs them.
        internal void DownloadAndInstall(OfficeEnvironment env, Action<string> progress)
        {
            string work = Path.Combine(Path.GetTempPath(), "V-TOOL.Addin." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress("Đang tải " + Path.GetFileName(ZipUrl) + " từ GitHub…");
                string zip = Path.Combine(work, "addin.zip");
                using (WebResponse response = Updates.Request(ZipUrl, "application/octet-stream").GetResponse())
                using (Stream input = response.GetResponseStream())
                using (FileStream output = File.Create(zip))
                    input.CopyTo(output);
                progress("Đang giải nén…");
                string files = Path.Combine(work, "files");
                Extract(zip, Files, files);
                if (IsRunning) throw new IOException(RunningMessage);
                progress("Đang cài đặt…");
                InstallFrom(env, files);
            }
            finally { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        // Only the named files are taken out of the zip, wherever they sit inside it; everything else (setup.cmd…) is ignored.
        internal static void Extract(string zip, string[] wanted, string folder)
        {
            Directory.CreateDirectory(folder);
            List<string> missing = new List<string>(wanted);
            try
            {
                using (FileStream stream = File.OpenRead(zip))
                using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string name = Path.GetFileName(entry.FullName.Replace('/', '\\'));
                        string match = missing.Find(delegate(string file) { return string.Equals(file, name, StringComparison.OrdinalIgnoreCase); });
                        if (match == null || entry.Length == 0) continue;
                        using (Stream input = entry.Open())
                        using (FileStream output = File.Create(Path.Combine(folder, match)))
                            input.CopyTo(output);
                        missing.Remove(match);
                    }
                }
            }
            catch (InvalidDataException) { throw new InvalidDataException("File tải về không phải file zip hợp lệ."); }
            if (missing.Count > 0) throw new InvalidDataException("File zip trên GitHub thiếu: " + string.Join(", ", missing.ToArray()) + ".");
        }

        protected static void CopyAll(string[] files, string source, string folder, string app)
        {
            Directory.CreateDirectory(folder);
            foreach (string file in files)
            {
                string target = Path.Combine(folder, file);
                try { File.Copy(Path.Combine(source, file), target, true); }
                catch (IOException) { throw new IOException("Không chép được " + file + " vào " + folder + ". Hãy đóng hẳn " + app + " rồi thử lại."); }
                catch (UnauthorizedAccessException) { throw new IOException("Không có quyền ghi " + file + " vào " + folder + "."); }
                Unblock(target);
            }
        }
        protected static void DeleteAll(string[] files, string folder, string app)
        {
            foreach (string file in files)
            {
                string path = Path.Combine(folder, file);
                try { if (File.Exists(path)) File.Delete(path); }
                catch (IOException) { throw new IOException("Không xóa được " + file + ". Hãy đóng hẳn " + app + " rồi thử lại."); }
                catch (UnauthorizedAccessException) { throw new IOException("Không có quyền xóa " + path + "."); }
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool DeleteFile(string path);
        // Removes the "downloaded from the Internet" mark, so Office and Windows do not block the macros or the DLL.
        internal static void Unblock(string path) { DeleteFile(path + ":Zone.Identifier"); }

        internal static RegistryKey Open(RegistryKey root, string path)
        {
            if (root == null) return null;
            try { return root.OpenSubKey(path); }
            catch (System.Security.SecurityException) { return null; }
        }
    }

    // Word: vtool.dotm and Vietnamese.dic go into Word's Startup folder, which Word loads and trusts by itself.
    internal sealed class WordAddin : OfficeAddin
    {
        private static readonly string[] Versions = { "16.0", "15.0", "14.0", "12.0", "11.0", "10.0", "9.0" };
        internal override string App { get { return "Word"; } }
        internal override string ProcessName { get { return "WINWORD"; } }
        internal override string ZipUrl { get { return "https://github.com/vietdang170/Addin-word/releases/latest/download/dotm.zip"; } }
        internal override string MainFile { get { return "vtool.dotm"; } }
        internal override string[] Files { get { return new string[] { "vtool.dotm", "Vietnamese.dic" }; } }

        // The Startup folder set in Word (Options > Advanced > File Locations), else Word's default one.
        internal override string InstallFolder(OfficeEnvironment env)
        {
            foreach (RegistryKey root in new RegistryKey[] { env.User, env.Machine })
            {
                foreach (string version in Versions)
                {
                    using (RegistryKey options = Open(root, @"Software\Microsoft\Office\" + version + @"\Word\Options"))
                    {
                        string value = options == null ? null : Convert.ToString(options.GetValue("STARTUP-PATH"));
                        if (string.IsNullOrEmpty(value)) continue;
                        value = Environment.ExpandEnvironmentVariables(value.Replace("\"", "")).Trim();
                        if (value.Length > 0) return value.TrimEnd('\\');
                    }
                }
            }
            return Path.Combine(env.AppData, @"Microsoft\Word\STARTUP");
        }
        internal override AddinState State(OfficeEnvironment env)
        {
            return File.Exists(Path.Combine(InstallFolder(env), MainFile)) ? AddinState.Installed : AddinState.Missing;
        }
        internal override void InstallFrom(OfficeEnvironment env, string source) { CopyAll(Files, source, InstallFolder(env), App); }
        // Word lists the add-ins of its Startup folder by itself, so removing the files also removes it from Templates and Add-ins.
        internal override void Uninstall(OfficeEnvironment env) { DeleteAll(Files, InstallFolder(env), App); }
    }

    // Excel: the three files go into %APPDATA%\Microsoft\AddIns\vtool; an OPEN entry switches the add-in on and a
    // Trusted Location lets its macros run without a prompt.
    internal sealed class ExcelAddin : OfficeAddin
    {
        private static readonly string[] Versions = { "16.0", "15.0", "14.0" };
        private const string TrustName = "LocationVTool";
        private static readonly Regex OpenName = new Regex("^OPEN([0-9]*)$", RegexOptions.IgnoreCase);
        internal override string App { get { return "Excel"; } }
        internal override string ProcessName { get { return "EXCEL"; } }
        internal override string ZipUrl { get { return "https://github.com/vietdang170/Addin-excel/releases/latest/download/xlam.zip"; } }
        internal override string MainFile { get { return "vtool.xlam"; } }
        internal override string[] Files { get { return new string[] { "vtool.xlam", "VNFastSearch.dll", "Vietnamese.dic" }; } }
        internal override string InstallFolder(OfficeEnvironment env) { return Path.Combine(env.AppData, @"Microsoft\AddIns\vtool"); }

        private static string Base(string version) { return @"Software\Microsoft\Office\" + version + @"\Excel"; }
        // Every Excel version that has been run on this computer; 16.0 (2016 to 365) when Excel has never been opened.
        private static List<string> InstalledVersions(OfficeEnvironment env)
        {
            List<string> found = new List<string>();
            foreach (string version in Versions)
                using (RegistryKey key = Open(env.User, Base(version))) if (key != null) found.Add(version);
            return found;
        }

        // An OPEN value is a path in quotes, optionally after switches such as /R (read-only).
        internal static string PathOf(string value)
        {
            string text = (value ?? "").Trim();
            while (text.StartsWith("/") && text.Length > 2)
            {
                int space = text.IndexOf(' ');
                if (space < 0) break;
                text = text.Substring(space + 1).TrimStart();
            }
            return text.Trim('"').Trim();
        }
        private bool IsOurs(string path)
        {
            string name;
            try { name = Path.GetFileName(path); }
            catch (ArgumentException) { return false; }
            return string.Equals(name, MainFile, StringComparison.OrdinalIgnoreCase);
        }

        // OPEN, OPEN1, OPEN2… in Excel's order. Excel stops reading at the first missing number, so they are always rewritten without gaps.
        internal static List<string> ReadOpen(RegistryKey options)
        {
            SortedDictionary<int, string> found = new SortedDictionary<int, string>();
            foreach (string name in options.GetValueNames())
            {
                Match match = OpenName.Match(name);
                int index;
                if (!match.Success) continue;
                if (match.Groups[1].Value.Length == 0) index = 0;
                else if (!int.TryParse(match.Groups[1].Value, out index)) continue;
                found[index] = Convert.ToString(options.GetValue(name));
            }
            return new List<string>(found.Values);
        }
        private static void WriteOpen(RegistryKey options, List<string> values)
        {
            foreach (string name in options.GetValueNames())
                if (OpenName.IsMatch(name)) options.DeleteValue(name, false);
            for (int i = 0; i < values.Count; i++)
                options.SetValue(i == 0 ? "OPEN" : "OPEN" + i, values[i], RegistryValueKind.String);
        }

        internal override AddinState State(OfficeEnvironment env)
        {
            string file = Path.Combine(InstallFolder(env), MainFile);
            if (!File.Exists(file)) return AddinState.Missing;
            foreach (string version in InstalledVersions(env))
                using (RegistryKey options = Open(env.User, Base(version) + @"\Options"))
                {
                    if (options == null) continue;
                    foreach (string value in ReadOpen(options))
                        if (string.Equals(PathOf(value), file, StringComparison.OrdinalIgnoreCase)) return AddinState.Installed;
                }
            return AddinState.Disabled;
        }

        internal override void InstallFrom(OfficeEnvironment env, string source)
        {
            string folder = InstallFolder(env);
            CopyAll(Files, source, folder, App);
            if (env.User == null) return;
            List<string> versions = InstalledVersions(env);
            if (versions.Count == 0) versions.Add("16.0");
            foreach (string version in versions)
            {
                Forget(env, version);
                using (RegistryKey options = env.User.CreateSubKey(Base(version) + @"\Options"))
                {
                    List<string> open = ReadOpen(options);
                    // No /R, so the add-in's VBA can still be edited and saved.
                    open.Add("\"" + Path.Combine(folder, MainFile) + "\"");
                    WriteOpen(options, open);
                }
                using (RegistryKey trust = env.User.CreateSubKey(Base(version) + @"\Security\Trusted Locations\" + TrustName))
                {
                    trust.SetValue("Path", folder + "\\", RegistryValueKind.String);
                    trust.SetValue("AllowSubfolders", 1, RegistryValueKind.DWord);
                    trust.SetValue("Description", "V-TOOL", RegistryValueKind.String);
                }
            }
        }

        // Takes vtool.xlam off the add-in list of one Excel version: the ticked entries (OPEN…) and the unticked ones (Add-in Manager).
        private void Forget(OfficeEnvironment env, string version)
        {
            using (RegistryKey options = env.User.OpenSubKey(Base(version) + @"\Options", true))
            {
                if (options != null)
                {
                    List<string> open = ReadOpen(options);
                    if (open.RemoveAll(delegate(string value) { return IsOurs(PathOf(value)); }) > 0) WriteOpen(options, open);
                }
            }
            using (RegistryKey manager = env.User.OpenSubKey(Base(version) + @"\Add-in Manager", true))
            {
                if (manager != null)
                    foreach (string name in manager.GetValueNames())
                        if (IsOurs(PathOf(name))) manager.DeleteValue(name, false);
            }
        }

        internal override void Uninstall(OfficeEnvironment env)
        {
            string folder = InstallFolder(env);
            DeleteAll(Files, folder, App);
            try { if (Directory.Exists(folder) && Directory.GetFileSystemEntries(folder).Length == 0) Directory.Delete(folder); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (env.User == null) return;
            foreach (string version in InstalledVersions(env))
            {
                Forget(env, version);
                using (RegistryKey locations = env.User.OpenSubKey(Base(version) + @"\Security\Trusted Locations", true))
                    if (locations != null) locations.DeleteSubKeyTree(TrustName, false);
            }
        }
    }

    // The icon of an installed program (WINWORD.EXE, EXCEL.EXE) exactly as Windows shows it, read from the program file
    // itself. Null when the program is not installed; the card then keeps its plain glyph.
    internal static class AppIcons
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);

        internal static Icon ForProgram(string exe, int size)
        {
            string path = ProgramPath(exe);
            if (path == null) return null;
            IntPtr large, small;
            try
            {
                // Low word: size of the large icon; high word: size of the small one.
                if (SHDefExtractIcon(path, 0, 0, out large, out small, (uint)size | (16u << 16)) != 0) return null;
            }
            catch (Exception) { return null; }
            try { return large == IntPtr.Zero ? null : (Icon)Icon.FromHandle(large).Clone(); }
            finally
            {
                if (large != IntPtr.Zero) DestroyIcon(large);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
        }
        // Office registers its programs under App Paths, the same place the Run box looks them up.
        private static string ProgramPath(string exe)
        {
            foreach (RegistryKey root in new RegistryKey[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using (RegistryKey key = OfficeAddin.Open(root, @"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                {
                    string path = key == null ? null : Convert.ToString(key.GetValue(""));
                    if (string.IsNullOrEmpty(path)) continue;
                    path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
                    if (File.Exists(path)) return path;
                }
            }
            return null;
        }
    }

    // One card per add-in: state on the title line, what gets installed, a progress/result line and the two buttons.
    // Without a progress/result line the description sits alone, centred next to the buttons.
    internal sealed class AddinCard : Card
    {
        private readonly Pill _state = new Pill { Dot = true };
        private readonly TableLayoutPanel _layout;
        private readonly Label _detail, _status;
        internal readonly UiButton InstallButton = new UiButton("Cài đặt", Glyph.Download, ButtonKind.Primary) { Height = 34, MinimumFit = 112 };
        internal readonly UiButton RemoveButton = new UiButton("Gỡ cài đặt", Glyph.Delete, ButtonKind.Danger) { Height = 34, Margin = new Padding(0, 0, 8, 0) };
        internal readonly OfficeAddin Addin;

        internal AddinCard(OfficeAddin addin, string glyph, string detail) : base(glyph, "V-TOOL cho " + addin.App)
        {
            Addin = addin;
            AddTitleBadge(_state);
            TitleIcon = AppIcons.ForProgram(addin.ProcessName + ".EXE", Dpi.Px(20));
            _layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Margin = Padding.Empty };
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 0));
            _detail = new Label { Text = detail, Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 0, 0, 1), Font = Theme.Regular, ForeColor = Theme.Body,
                TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false, AutoEllipsis = true };
            _status = new Label { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(0, 3, 0, 0), Font = Theme.SmallBold, ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.TopLeft, UseMnemonic = false, AutoEllipsis = true };
            _layout.Controls.Add(_detail, 0, 0);
            _layout.Controls.Add(_status, 0, 1);
            FlowLayoutPanel buttons = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                Anchor = AnchorStyles.Right, Margin = new Padding(16, 0, 0, 0) };
            buttons.Controls.Add(RemoveButton); buttons.Controls.Add(InstallButton);
            _layout.Controls.Add(buttons, 1, 0);
            _layout.SetRowSpan(buttons, 2);
            Controls.Add(_layout);
        }

        internal void ShowState(AddinState state, bool busy)
        {
            if (state == AddinState.Installed) { _state.Text = "Đã cài đặt"; _state.SetColors(Theme.GreenSoft, Theme.GreenLine, Theme.Green); }
            else if (state == AddinState.Disabled) { _state.Text = "Chưa bật trong " + Addin.App; _state.SetColors(Theme.AmberSoft, Theme.AmberLine, Theme.Amber); }
            else { _state.Text = "Chưa cài đặt"; _state.SetColors(Theme.Divider, Theme.Border, Theme.Muted); }
            // Already installed: the Install button stays greyed out; Remove is offered while any file is there.
            InstallButton.Enabled = !busy && state != AddinState.Installed;
            RemoveButton.Enabled = !busy && state != AddinState.Missing;
            PerformLayout();
        }
        internal void SetStatus(string text, Color color)
        {
            bool shown = !string.IsNullOrEmpty(text);
            _status.Text = text ?? ""; _status.ForeColor = color;
            // Two lines share the height around the middle; one line is centred.
            _layout.SuspendLayout();
            _layout.RowStyles[0] = shown ? new RowStyle(SizeType.Percent, 50) : new RowStyle(SizeType.Percent, 100);
            _layout.RowStyles[1] = shown ? new RowStyle(SizeType.Percent, 50) : new RowStyle(SizeType.Absolute, 0);
            _detail.TextAlign = shown ? ContentAlignment.BottomLeft : ContentAlignment.MiddleLeft;
            _layout.ResumeLayout(true);
        }
    }

    internal sealed class AddinPage : Panel
    {
        private readonly OfficeEnvironment _env;
        private readonly AddinCard _word, _excel;
        private bool _busy;

        internal AddinPage(OfficeEnvironment env)
        {
            _env = env;
            Dock = DockStyle.Fill; BackColor = Theme.Window;
            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(12), BackColor = Theme.Window };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Kit.PageHeader(Glyph.Puzzle, "Add-in Office", ""), 0, 0);
            _word = new AddinCard(OfficeAddin.Word, Glyph.Word, "Cài vtool.dotm và từ điển Vietnamese.dic vào Word.")
                { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            _excel = new AddinCard(OfficeAddin.Excel, Glyph.Excel, "Cài vtool.xlam, VNFastSearch.dll, Vietnamese.dic vào Excel.")
                { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
            layout.Controls.Add(_word, 0, 1);
            layout.Controls.Add(_excel, 0, 2);
            layout.Controls.Add(new InfoNote(Glyph.Info, "Hãy đóng Word, Excel trước khi cài hoặc gỡ. Sau khi cài, add-in tự cập nhật hằng ngày khi mở Word, Excel.")
                { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 3);
            Controls.Add(layout);
            foreach (AddinCard card in new AddinCard[] { _word, _excel })
            {
                AddinCard target = card;
                card.InstallButton.Click += delegate { Install(target); };
                card.RemoveButton.Click += delegate { Remove(target); };
            }
            RefreshState();
        }

        // Reads what is installed right now; cheap enough to run each time the tab is shown.
        internal void RefreshState()
        {
            foreach (AddinCard card in new AddinCard[] { _word, _excel })
            {
                AddinState state;
                try { state = card.Addin.State(_env); }
                catch (Exception ex) { state = AddinState.Missing; card.SetStatus("Không đọc được trạng thái: " + ex.Message, Theme.Red); }
                card.ShowState(state, _busy);
            }
        }

        private void Install(AddinCard card)
        {
            if (_busy) return;
            if (card.Addin.IsRunning) { card.SetStatus(card.Addin.RunningMessage, Theme.Red); return; }
            _busy = true;
            RefreshState();
            card.SetStatus("Đang chuẩn bị…", Theme.Muted);
            Thread worker = new Thread(delegate()
            {
                string error = null;
                try
                {
                    card.Addin.DownloadAndInstall(_env, delegate(string step) { Post(delegate { card.SetStatus(step, Theme.Blue); }); });
                }
                catch (WebException ex) { error = "Không tải được từ GitHub: " + ex.Message; }
                catch (Exception ex) { error = ex.Message; }
                Post(delegate
                {
                    _busy = false;
                    RefreshState();
                    if (error != null) card.SetStatus(error, Theme.Red);
                    else card.SetStatus("Đã cài xong. Mở " + card.Addin.App + " để dùng V-TOOL.", Theme.GreenBright);
                });
            }) { IsBackground = true, Name = "Install " + card.Addin.App + " add-in" };
            worker.Start();
        }

        private void Remove(AddinCard card)
        {
            if (_busy) return;
            OfficeAddin addin = card.Addin;
            if (addin.IsRunning) { card.SetStatus(addin.RunningMessage, Theme.Red); return; }
            if (MessageBox.Show(FindForm(), "Gỡ V-TOOL khỏi " + addin.App + "?", "Gỡ cài đặt add-in", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            // Office may have been opened while the question was on screen.
            if (addin.IsRunning) { card.SetStatus(addin.RunningMessage, Theme.Red); return; }
            try
            {
                addin.Uninstall(_env);
                card.SetStatus("Đã gỡ V-TOOL khỏi " + addin.App + ".", Theme.GreenBright);
            }
            catch (Exception ex) { card.SetStatus(ex.Message, Theme.Red); }
            RefreshState();
        }

        private void Post(Action action)
        {
            try { if (IsHandleCreated && !IsDisposed) BeginInvoke(action); }
            catch (InvalidOperationException) { }
        }
    }
}
