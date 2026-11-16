using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;
using VietDang.AutoClick;

namespace VietDang.VTool
{
    internal static class IntegrationChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int _checks;
        private static object Field(object instance, string name) { return instance.GetType().GetField(name, Private).GetValue(instance); }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); _checks++; }
        private static void ExitFromTray(MainWindow form)
        {
            ContextMenuStrip menu = (ContextMenuStrip)Field(form, "_menu");
            menu.Items[menu.Items.Count - 1].PerformClick();
        }
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if (args.Length == 2 && args[0] == "--tray-exit")
                {
                    // Isolated: no registry, global hotkeys or clipboard access, so a running V-TOOL is never disturbed.
                    using (MainWindow form = new MainWindow(null, args[1], true))
                    using (ApplicationContext context = new ApplicationContext())
                    {
                        form.StartPosition = FormStartPosition.Manual; form.ShowInTaskbar = false;
                        form.FormClosed += delegate { context.ExitThread(); };
                        form.Shown += delegate { form.Location = new Point(-20000, -20000); form.BeginInvoke(new Action(delegate { ExitFromTray(form); })); };
                        form.Show(); Application.Run(context);
                    }
                    return 0;
                }
                if (args.Length == 2 && args[0] == "--scaled-preview")
                {
                    // Must come before anything touches Theme: its fonts are created once with this scale.
                    VietDang.Design.Dpi.Simulated = float.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
                    CheckLayouts();
                    Console.WriteLine("PASS: " + _checks + " layout checks at " + args[1] + "x.");
                    return 0;
                }
                CheckLayouts();
                foreach (string scale in new string[] { "1.25", "1.5" })
                {
                    ProcessStartInfo scaled = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--scaled-preview " + scale)
                        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
                    using (Process child = Process.Start(scaled))
                    {
                        string errors = child.StandardError.ReadToEnd();
                        if (!child.WaitForExit(30000)) { child.Kill(); throw new Exception("layout check at " + scale + "x timed out"); }
                        Check(child.ExitCode == 0, "layout fits at " + scale + "x screen scale\n" + errors);
                    }
                }
                RunChecks();
                RunUpdateChecks();
                RunAddinChecks();
                Console.WriteLine("PASS: " + _checks + " integrated app checks; user settings and clipboard untouched.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        private static void Settle(int milliseconds)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (wait.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
        }
        private static string Describe(Control control)
        {
            string text = control.Text;
            return control.GetType().Name + (string.IsNullOrEmpty(text) ? "" : " \"" + (text.Length > 30 ? text.Substring(0, 30) + "…" : text) + "\"");
        }
        // Nothing may stick out of its parent and no fixed text may be cut short with "…".
        private static void CheckInside(Control parent, List<string> problems)
        {
            ScrollableControl scroll = parent as ScrollableControl;
            Rectangle area = scroll != null && scroll.AutoScroll ? scroll.DisplayRectangle : parent.ClientRectangle;
            foreach (Control control in parent.Controls)
            {
                if (!control.Visible) continue;
                Rectangle bounds = control.Bounds;
                bounds.Inflate(-1, -1);
                if (!area.Contains(bounds)) problems.Add(Describe(control) + " " + control.Bounds + " sticks out of " + Describe(parent) + " " + area);
                Label label = control as Label;
                if (label != null && label.AutoEllipsis && !label.AutoSize && label.Text.Length > 0 &&
                    TextRenderer.MeasureText(label.Text, label.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width > label.ClientSize.Width)
                    problems.Add(Describe(label) + " is cut short (" + label.ClientSize.Width + " px wide)");
                DataGridView grid = control as DataGridView;
                if (grid != null && grid.Columns.GetColumnsWidth(DataGridViewElementStates.Visible) > grid.ClientSize.Width)
                    problems.Add("the coordinate grid needs sideways scrolling");
                if (!(control is DataGridView || control is ListBox || control is UpDownBase || control is ComboBox || control is TextBoxBase)) CheckInside(control, problems);
            }
        }
        // Builds the window at the current (or simulated) screen scale and checks every page at three window sizes: as it
        // opens, a 1366 x 768 laptop screen, and a large 1920 x 1080 screen. Each case is saved as a preview picture.
        private static void CheckLayouts()
        {
            float simulated = VietDang.Design.Dpi.Simulated;
            string tag = Math.Abs(simulated - 1f) < 0.01f ? "" : "." + (int)Math.Round(simulated * 100);
            string directory = Path.Combine(Path.GetTempPath(), "V-TOOL.Layout." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                string settingsPath = Path.Combine(directory, "autoclick.xml");
                AppSettings settings = new AppSettings { SelectedIndex = 0 };
                Scenario first = new Scenario { Name = "Kịch bản mẫu" };
                first.Steps.Add(new ClickStep { X = 450, Y = 250, DelayMs = 750 });
                settings.Scenarios.Add(first);
                SettingsStore.Save(settings, settingsPath);
                using (MainWindow form = new MainWindow(null, settingsPath, true))
                {
                    form.ShowInTaskbar = false;
                    form.Show(); form.Location = new Point(-20000, -20000); Application.DoEvents();
                    Check(form.FormBorderStyle == FormBorderStyle.Sizable && form.MaximizeBox, "the window can be resized and maximized");
                    Rectangle work = Screen.PrimaryScreen.WorkingArea;
                    Check(form.Width <= work.Width && form.Height <= work.Height, "the window opens no larger than the screen");
                    RadioButton[] navigation = (RadioButton[])Field(form, "_navItems");
                    Panel[] pages = (Panel[])Field(form, "_pages");
                    string[] names = { "Click", "Paste", "Addin", "Settings" };
                    Size[] sizes = { form.Size, new Size(1366, 728), new Size(1920, 1040) };
                    string[] sizeNames = { "", ".small", ".large" };
                    for (int s = 0; s < sizes.Length; s++)
                    {
                        form.Size = sizes[s];
                        for (int i = 0; i < 4; i++)
                        {
                            navigation[i].Checked = true;
                            Settle(300);
                            List<string> problems = new List<string>();
                            CheckInside(pages[i], problems);
                            string name = names[i] + tag + sizeNames[s];
                            if (tag.Length > 0 || s > 0)
                                using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                                {
                                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                                    bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "V-TOOL." + name + ".preview.png"));
                                }
                            Check(problems.Count == 0, name + " " + form.ClientSize + ": " + string.Join("; ", problems.ToArray()));
                        }
                    }
                    ExitFromTray(form);
                }
            }
            finally { Directory.Delete(directory, true); }
        }
        // Installs and removes both add-ins in a throw-away folder and registry key, from zips made here (no network, no real Office).
        private static void RunAddinChecks()
        {
            string folder = Path.Combine(Path.GetTempPath(), "V-TOOL.AddinChecks." + Guid.NewGuid().ToString("N"));
            const string parent = @"Software\VietDang\V-TOOL.Tests";
            string keyPath = parent + @"\" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(folder);
            RegistryKey root = Registry.CurrentUser.CreateSubKey(keyPath);
            try
            {
                OfficeEnvironment env = new OfficeEnvironment(root, null, Path.Combine(folder, "AppData"));
                OfficeAddin excel = OfficeAddin.Excel, word = OfficeAddin.Word;
                Check(ExcelAddin.PathOf("/R \"C:\\a b\\vtool.xlam\"") == @"C:\a b\vtool.xlam" && ExcelAddin.PathOf("\"C:\\x.xlam\"") == @"C:\x.xlam", "Excel add-in entries are read with or without /R");

                string zip = Path.Combine(folder, "xlam.zip"), files = Path.Combine(folder, "xlam");
                MakeZip(zip, "xlam/vtool.xlam", "xlam/VNFastSearch.dll", "xlam/Vietnamese.dic", "xlam/setup.cmd");
                OfficeAddin.Extract(zip, excel.Files, files);
                Check(File.Exists(Path.Combine(files, "vtool.xlam")) && File.Exists(Path.Combine(files, "VNFastSearch.dll")) && File.Exists(Path.Combine(files, "Vietnamese.dic"))
                    && !File.Exists(Path.Combine(files, "setup.cmd")), "only the add-in files are taken from the zip, from any subfolder");
                string broken = Path.Combine(folder, "broken.zip");
                MakeZip(broken, "vtool.xlam");
                bool refused = false;
                try { OfficeAddin.Extract(broken, excel.Files, Path.Combine(folder, "broken")); } catch (InvalidDataException) { refused = true; }
                Check(refused, "a zip without every add-in file is refused");

                // Excel already lists other add-ins, and an old read-only vtool.xlam elsewhere, ticked and unticked.
                string excelKey = @"Software\Microsoft\Office\16.0\Excel";
                using (RegistryKey options = root.CreateSubKey(excelKey + @"\Options"))
                {
                    options.SetValue("OPEN", "\"C:\\Other\\first.xlam\"");
                    options.SetValue("OPEN1", "/R \"C:\\Old\\vtool.xlam\"");
                    options.SetValue("OPEN2", "\"C:\\Other\\third.xlam\"");
                }
                using (RegistryKey manager = root.CreateSubKey(excelKey + @"\Add-in Manager")) manager.SetValue(@"C:\Old\vtool.xlam", "");
                Check(excel.State(env) == AddinState.Missing, "Excel add-in is reported missing before install");
                excel.InstallFrom(env, files);
                excel.InstallFrom(env, files);
                string installed = Path.Combine(excel.InstallFolder(env), "vtool.xlam");
                using (RegistryKey options = root.OpenSubKey(excelKey + @"\Options"))
                {
                    List<string> open = ExcelAddin.ReadOpen(options);
                    Check(open.Count == 3 && open[0] == "\"C:\\Other\\first.xlam\"" && open[1] == "\"C:\\Other\\third.xlam\"" && open[2] == "\"" + installed + "\""
                        && options.GetValue("OPEN3") == null, "Excel install keeps other add-ins in order, numbered without gaps, and lists V-TOOL once");
                }
                using (RegistryKey manager = root.OpenSubKey(excelKey + @"\Add-in Manager"))
                    Check(manager.GetValueNames().Length == 0, "the old unticked V-TOOL entry is removed");
                using (RegistryKey trust = root.OpenSubKey(excelKey + @"\Security\Trusted Locations\LocationVTool"))
                    Check(trust != null && (string)trust.GetValue("Path") == excel.InstallFolder(env) + "\\" && (int)trust.GetValue("AllowSubfolders") == 1, "the add-in folder becomes a trusted location");
                Check(excel.State(env) == AddinState.Installed && File.Exists(Path.Combine(excel.InstallFolder(env), "VNFastSearch.dll")), "Excel add-in is reported installed");
                using (RegistryKey options = root.OpenSubKey(excelKey + @"\Options", true)) options.DeleteValue("OPEN2");
                Check(excel.State(env) == AddinState.Disabled, "an unticked add-in is reported as not switched on");
                using (RegistryKey options = root.OpenSubKey(excelKey + @"\Options", true)) options.SetValue("OPEN2", "\"" + installed + "\"");
                excel.Uninstall(env);
                using (RegistryKey options = root.OpenSubKey(excelKey + @"\Options"))
                {
                    List<string> open = ExcelAddin.ReadOpen(options);
                    Check(open.Count == 2 && open[1] == "\"C:\\Other\\third.xlam\"" && options.GetValue("OPEN2") == null, "Excel uninstall removes only V-TOOL from the add-in list");
                }
                using (RegistryKey trust = root.OpenSubKey(excelKey + @"\Security\Trusted Locations\LocationVTool"))
                    Check(trust == null && !Directory.Exists(excel.InstallFolder(env)) && excel.State(env) == AddinState.Missing, "Excel uninstall removes the files and the trusted location");

                // Word: its default Startup folder, or the one chosen in Word's options.
                string wordZip = Path.Combine(folder, "dotm.zip"), wordFiles = Path.Combine(folder, "dotm");
                MakeZip(wordZip, "vtool.dotm", "Vietnamese.dic", "setup.cmd");
                OfficeAddin.Extract(wordZip, word.Files, wordFiles);
                Check(word.InstallFolder(env) == Path.Combine(env.AppData, @"Microsoft\Word\STARTUP"), "Word uses its default Startup folder");
                string custom = Path.Combine(folder, "My Startup");
                using (RegistryKey options = root.CreateSubKey(@"Software\Microsoft\Office\16.0\Word\Options")) options.SetValue("STARTUP-PATH", custom + "\\");
                Check(word.InstallFolder(env) == custom, "Word uses the Startup folder set in its options");
                Check(word.State(env) == AddinState.Missing, "Word add-in is reported missing before install");
                word.InstallFrom(env, wordFiles);
                Check(word.State(env) == AddinState.Installed && File.Exists(Path.Combine(custom, "Vietnamese.dic")), "Word install copies both files into Startup");
                word.Uninstall(env);
                Check(word.State(env) == AddinState.Missing && !File.Exists(Path.Combine(custom, "Vietnamese.dic")), "Word uninstall removes both files");
            }
            finally
            {
                root.Close();
                Registry.CurrentUser.DeleteSubKeyTree(keyPath, false);
                using (RegistryKey tests = Registry.CurrentUser.OpenSubKey(parent))
                    if (tests != null && tests.SubKeyCount == 0 && tests.ValueCount == 0) { tests.Close(); Registry.CurrentUser.DeleteSubKey(parent, false); }
                Directory.Delete(folder, true);
            }
        }
        private static void MakeZip(string path, params string[] entries)
        {
            using (FileStream stream = File.Create(path))
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Create))
                foreach (string entry in entries)
                    using (StreamWriter writer = new StreamWriter(archive.CreateEntry(entry).Open())) writer.Write("content of " + entry);
        }
        // The updater pieces that do not need the network: tags, hashes, release parsing and the exe swap with rollback.
        private static void RunUpdateChecks()
        {
            Check(Updates.ParseTag("v1.2.3") == new Version(1, 2, 3) && Updates.ParseTag("1.4") == new Version(1, 4, 0) && Updates.ParseTag("beta") == null, "release tags are read as versions");
            Check(Updates.ParseTag("v1.10.0") > Updates.ParseTag("v1.9.9"), "versions compare by number, not by text");
            string hash = new string('a', 64);
            Check(Updates.ParseHash(hash.ToUpperInvariant() + "  V-TOOL.exe\r\n") == hash && Updates.ParseHash("abc") == null, "sha256 files are read");
            string json = "{\"tag_name\":\"v1.1.0\",\"body\":\"Sửa lỗi\",\"draft\":false,\"prerelease\":false,\"assets\":["
                + "{\"name\":\"V-TOOL.exe\",\"browser_download_url\":\"https://example.test/V-TOOL.exe\"},"
                + "{\"name\":\"V-TOOL.exe.sha256\",\"browser_download_url\":\"https://example.test/V-TOOL.exe.sha256\"}]}";
            ReleaseInfo release = Updates.ParseRelease(json);
            Check(release.Version == new Version(1, 1, 0) && release.Notes == "Sửa lỗi" && release.ExeUrl.EndsWith("/V-TOOL.exe") && release.HashUrl.EndsWith(".sha256") && release.Problem == null, "GitHub release data is read");
            Check(Updates.ParseRelease(json.Replace("\"prerelease\":false", "\"prerelease\":true")) == null, "pre-releases are ignored");
            Check(Updates.ParseRelease(json.Replace("V-TOOL.exe.sha256", "other.txt")).Problem != null, "a release without any checksum is not installed");
            string digestOnly = "{\"tag_name\":\"v1.2.0\",\"assets\":[{\"name\":\"V-TOOL.exe\",\"browser_download_url\":\"https://example.test/V-TOOL.exe\",\"digest\":\"sha256:" + hash + "\"}]}";
            ReleaseInfo uploadedExeOnly = Updates.ParseRelease(digestOnly);
            Check(uploadedExeOnly.Hash == hash && uploadedExeOnly.HashUrl == null && uploadedExeOnly.Problem == null, "a release with only V-TOOL.exe uses GitHub's own checksum");
            ReleaseInfo oldName = Updates.ParseRelease(digestOnly.Replace("V-TOOL.exe", "Auto.exe"));
            Check(oldName.Version == new Version(1, 2, 0) && oldName.ExeUrl == null && oldName.Problem != null, "a release without V-TOOL.exe is reported instead of installed");
            string page = "<a href=\"/vietdang170/V-TOOL/releases/download/v1.0.3/V-TOOL.exe\" rel=\"nofollow\">V-TOOL.exe</a>\n<span>sha256:" + hash.ToUpperInvariant() + "</span>";
            Check(Updates.FindDigest(page, "V-TOOL.exe") == hash && Updates.FindDigest("<p>no assets</p>", "V-TOOL.exe") == null, "the checksum is also read from the release page when the API is busy");
            // The exe built next to the tests must be accepted by the updater (assembly V-TOOL, same version).
            string built = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)), "V-TOOL.exe");
            // Just after the build the antivirus may still be scanning the new exe and hold it for a moment.
            bool valid = false;
            for (int attempt = 0; attempt < 20 && !valid; attempt++)
            {
                try { valid = File.Exists(built) && Updates.IsValidPackage(built, Updates.Sha256(built), Updates.Current); }
                catch (IOException) { }
                if (!valid) System.Threading.Thread.Sleep(500);
            }
            Check(valid, "the freshly built V-TOOL.exe passes the updater's own check");
            Bitmap qr = Donation.LoadQr();
            Check(qr != null && qr.Width == 296 && qr.Height == 296, "the donation QR code is built into the app");
            qr.Dispose();

            string folder = Path.Combine(Path.GetTempPath(), "V-TOOL.UpdateChecks." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string exe = Path.Combine(folder, "V-TOOL.exe"), package = Path.Combine(folder, "download.exe");
                File.WriteAllText(exe, "old"); File.WriteAllText(package, "new");
                Check(!Updates.IsValidPackage(package, Updates.Sha256(package), new Version(1, 1, 0)), "a file that is not V-TOOL is never installed");
                Updates.Swap(exe, package);
                Check(File.ReadAllText(exe) == "new" && File.ReadAllText(exe + ".old") == "old" && !File.Exists(exe + ".new"), "swap puts the new exe in place and keeps the old one");
                Updates.Restore(exe);
                Check(File.ReadAllText(exe) == "old" && File.ReadAllText(exe + ".failed") == "new" && !File.Exists(exe + ".old"),
                    "restore brings the old exe back");
                // Running from a copy that is itself called V-TOOL.old.exe must not clash with the backup name.
                string renamed = Path.Combine(folder, "V-TOOL.old.exe");
                File.WriteAllText(renamed, "old");
                Updates.Swap(renamed, package);
                Check(File.ReadAllText(renamed) == "new" && File.ReadAllText(renamed + ".old") == "old", "an exe named V-TOOL.old.exe updates too");
            }
            finally { Directory.Delete(folder, true); }
        }
        private static void RunChecks()
        {
            string directory = Path.Combine(Path.GetTempPath(), "V-TOOL.Integration." + Guid.NewGuid().ToString("N"));
            string settingsPath = Path.Combine(directory, "autoclick.xml");
            try
            {
                AppSettings settings = new AppSettings { SelectedIndex = 0 };
                Scenario first = new Scenario { Name = "Kịch bản mẫu" };
                first.Steps.Add(new ClickStep { X = 450, Y = 250, DelayMs = 750 });
                settings.Scenarios.Add(first);
                settings.Scenarios.Add(new Scenario { Name = "Kịch bản thứ hai", Mode = RepeatMode.UntilStopped });
                SettingsStore.Save(settings, settingsPath);
                using (MainWindow form = new MainWindow(null, settingsPath, true))
                {
                    form.StartPosition = FormStartPosition.Manual; form.ShowInTaskbar = false;
                    form.Show(); form.Location = new Point(-20000, -20000); Application.DoEvents();
                    RadioButton[] navigation = (RadioButton[])Field(form, "_navItems");
                    Panel[] pages = (Panel[])Field(form, "_pages");
                    Check(form.Text == "V-TOOL" && navigation.Length == 4 && pages.Length == 4 && navigation[MainWindow.SettingsPage].Text == "Cài đặt" && navigation[MainWindow.AddinPage].Text == "Add-in Office", "vertical navigation includes Settings");
                    Check(navigation[0].Left == navigation[3].Left && navigation[0].Top < navigation[1].Top && navigation[1].Top < navigation[2].Top && navigation[2].Top < navigation[3].Top, "navigation items are stacked vertically");
                    Form paste = (Form)Field(form, "_paste"), click = (Form)Field(form, "_click");
                    Check(!paste.TopLevel && !click.TopLevel && click.Parent == pages[MainWindow.ClickPage] && paste.Parent == pages[MainWindow.PastePage], "both modules run inside one window");
                    Check(navigation[MainWindow.ClickPage].Text == "AutoClick" && navigation[MainWindow.PastePage].Text == "AutoPasteValue", "navigation order matches the pages");
                    Check(!((NotifyIcon)Field(paste, "_notifyIcon")).Visible && !((NotifyIcon)Field(click, "_tray")).Visible && ((NotifyIcon)Field(form, "_tray")).Visible, "only one tray icon is visible");
                    Size fixedSize = form.Size;
                    for (int i = 0; i < 4; i++)
                    {
                        Point fixedLocation = form.Location;
                        navigation[i].Checked = true;
                        // Let the short highlight fade finish before the page is captured.
                        Stopwatch fade = Stopwatch.StartNew();
                        while (fade.ElapsedMilliseconds < 350) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                        Check(form.Size == fixedSize, "window size remains fixed on page " + i);
                        Check(form.Location == fixedLocation, "window position remains fixed on page " + i);
                        for (int page = 0; page < 4; page++) Check(pages[page].Visible == (page == i), "only page " + i + " is shown");
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, i == MainWindow.ClickPage ? "V-TOOL.Click.preview.png" : i == MainWindow.PastePage ? "V-TOOL.Paste.preview.png" : i == MainWindow.AddinPage ? "V-TOOL.Addin.preview.png" : "V-TOOL.Settings.preview.png"));
                        }
                    }
                    Check(pages[MainWindow.ClickPage].ClientSize.Width >= 840 && pages[MainWindow.ClickPage].ClientSize.Height >= 620, "click layout fits the integrated page");
                    CheckBox startup = (CheckBox)Field(form, "_startupBox");
                    ToolStripMenuItem startupItem = (ToolStripMenuItem)Field(form, "_startupItem");
                    Check(startup.Enabled && startup.Parent != null && !startup.Checked, "Settings offers start with Windows");
                    startup.Checked = true;
                    Check(startup.Checked && startupItem.Checked, "start with Windows stays in sync with the tray menu");
                    startup.Checked = false;
                    Check(!startup.Checked && !startupItem.Checked, "start with Windows can be switched off again");
                    Check(((CheckBox)Field(form, "_updateBox")).Parent != null, "Settings offers the daily update check");
                    Check(AutoPasteValueApp.MainForm.TrimCells("  a   b \t c  \r\n d ") == "a b\tc\r\nd", "trim works per cell like Excel TRIM");
                    Check(AutoPasteValueApp.MainForm.TrimCells("\u00A0Hà  Nội\u00A0") == "Hà Nội", "trim also removes non-breaking spaces");
                    Check(AutoPasteValueApp.MainForm.CanKeepFormat(13) && AutoPasteValueApp.MainForm.CanKeepFormat((uint)DataFormats.GetFormat(DataFormats.Html).Id) &&
                        AutoPasteValueApp.MainForm.CanKeepFormat((uint)DataFormats.GetFormat(DataFormats.Rtf).Id), "excluded apps get back text, HTML and RTF");
                    Check(!AutoPasteValueApp.MainForm.CanKeepFormat(1) && !AutoPasteValueApp.MainForm.CanKeepFormat(2) && !AutoPasteValueApp.MainForm.CanKeepFormat(14) &&
                        !AutoPasteValueApp.MainForm.CanKeepFormat((uint)DataFormats.GetFormat("Ole Private Data").Id), "synthesized, handle and OLE link formats are not kept");
                    Check(AutoPasteValueApp.MainForm.IsShellSurface(IntPtr.Zero, "StartMenuExperienceHost.exe") &&
                        !AutoPasteValueApp.MainForm.IsShellSurface(IntPtr.Zero, "EXCEL.exe") && !AutoPasteValueApp.MainForm.IsShellSurface(IntPtr.Zero, "msedge.exe"),
                        "Start and the taskbar are passed through, real apps decide the paste");
                    ComboBox hotkeys = (ComboBox)Field(paste, "_cboHotkey");
                    Check(hotkeys.Items.Count == 4 && !hotkeys.Items.Contains("Ctrl + Shift + V"), "Ctrl + Shift + V is no longer offered");
                    form.Close();
                    Check(!form.IsDisposed && !form.Visible, "closing the window keeps the app in the tray");
                    ExitFromTray(form);
                    Check(form.IsDisposed, "tray Exit disposes both modules");
                }
                string warning;
                Check(SettingsStore.Load(out warning, settingsPath).Scenarios.Count == 2, "integrated tray Exit preserves all scripts");
                ProcessStartInfo info = new ProcessStartInfo(Assembly.GetExecutingAssembly().Location, "--tray-exit \"" + settingsPath + "\"")
                    { UseShellExecute = false, CreateNoWindow = true };
                using (Process child = Process.Start(info))
                {
                    if (!child.WaitForExit(8000)) { child.Kill(); throw new Exception("integrated tray-exit process timed out"); }
                    Check(child.ExitCode == 0, "integrated tray Exit ends the real message loop and process");
                }
                AppSettings reloaded = SettingsStore.Load(out warning, settingsPath);
                Check(reloaded.Scenarios.Count == 2 && reloaded.Scenarios[0].Steps.Count == 1 && reloaded.SelectedIndex == 0, "both scripts and selection survive another process and tray Exit");
                Check(reloaded.Scenarios[0].Steps[0].DelayMs == 750, "saved step timings remain unchanged");
                Check(File.Exists(settingsPath + ".bak"), "last good file backup exists");
            }
            finally
            {
                if (Directory.Exists(directory) && Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(directory, true);
            }
        }
    }
}
