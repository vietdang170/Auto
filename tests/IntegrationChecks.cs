using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using VietDang.AutoClick;

namespace VietDang.AutoSuite
{
    internal static class IntegrationChecks
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int _checks;
        private static object Field(object instance, string name) { return instance.GetType().GetField(name, Private).GetValue(instance); }
        private static void Check(bool condition, string message) { if (!condition) throw new Exception("FAIL: " + message); _checks++; }
        private static void ExitFromTray(AutoForm form)
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
                    // Isolated: no registry, global hotkeys or clipboard access, so a running Auto is never disturbed.
                    using (AutoForm form = new AutoForm(null, args[1], true))
                    using (ApplicationContext context = new ApplicationContext())
                    {
                        form.StartPosition = FormStartPosition.Manual; form.ShowInTaskbar = false;
                        form.FormClosed += delegate { context.ExitThread(); };
                        form.Shown += delegate { form.Location = new Point(-20000, -20000); form.BeginInvoke(new Action(delegate { ExitFromTray(form); })); };
                        form.Show(); Application.Run(context);
                    }
                    return 0;
                }
                RunChecks();
                RunUpdateChecks();
                Console.WriteLine("PASS: " + _checks + " integrated app checks; user settings and clipboard untouched.");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
        // The updater pieces that do not need the network: tags, hashes, release parsing and the exe swap with rollback.
        private static void RunUpdateChecks()
        {
            Check(Updates.ParseTag("v1.2.3") == new Version(1, 2, 3) && Updates.ParseTag("1.4") == new Version(1, 4, 0) && Updates.ParseTag("beta") == null, "release tags are read as versions");
            Check(Updates.ParseTag("v1.10.0") > Updates.ParseTag("v1.9.9"), "versions compare by number, not by text");
            string hash = new string('a', 64);
            Check(Updates.ParseHash(hash.ToUpperInvariant() + "  Auto.exe\r\n") == hash && Updates.ParseHash("abc") == null, "sha256 files are read");
            string json = "{\"tag_name\":\"v1.1.0\",\"body\":\"Sửa lỗi\",\"draft\":false,\"prerelease\":false,\"assets\":["
                + "{\"name\":\"Auto.exe\",\"browser_download_url\":\"https://example.test/Auto.exe\"},"
                + "{\"name\":\"Auto.exe.sha256\",\"browser_download_url\":\"https://example.test/Auto.exe.sha256\"}]}";
            ReleaseInfo release = Updates.ParseRelease(json);
            Check(release.Version == new Version(1, 1, 0) && release.Notes == "Sửa lỗi" && release.ExeUrl.EndsWith("/Auto.exe") && release.HashUrl.EndsWith(".sha256"), "GitHub release data is read");
            Check(Updates.ParseRelease(json.Replace("\"prerelease\":false", "\"prerelease\":true")) == null, "pre-releases are ignored");
            bool missing = false;
            try { Updates.ParseRelease(json.Replace("Auto.exe.sha256", "other.txt")); } catch (InvalidDataException) { missing = true; }
            Check(missing, "a release without any checksum is refused");
            string digestOnly = "{\"tag_name\":\"v1.2.0\",\"assets\":[{\"name\":\"Auto.exe\",\"browser_download_url\":\"https://example.test/Auto.exe\",\"digest\":\"sha256:" + hash + "\"}]}";
            ReleaseInfo uploadedExeOnly = Updates.ParseRelease(digestOnly);
            Check(uploadedExeOnly.Hash == hash && uploadedExeOnly.HashUrl == null, "a release with only Auto.exe uses GitHub's own checksum");
            string page = "<a href=\"/vietdang170/Auto/releases/download/v1.0.0/Auto.exe\" rel=\"nofollow\">Auto.exe</a>\n<span>sha256:" + hash.ToUpperInvariant() + "</span>";
            Check(Updates.FindDigest(page) == hash && Updates.FindDigest("<p>no assets</p>") == null, "the checksum is also read from the release page when the API is busy");

            string folder = Path.Combine(Path.GetTempPath(), "Auto.UpdateChecks." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string exe = Path.Combine(folder, "Auto.exe"), package = Path.Combine(folder, "download.exe");
                File.WriteAllText(exe, "old"); File.WriteAllText(package, "new");
                Check(!Updates.IsValidPackage(package, Updates.Sha256(package), new Version(1, 1, 0)), "a file that is not Auto is never installed");
                Updates.Swap(exe, package);
                Check(File.ReadAllText(exe) == "new" && File.ReadAllText(exe + ".old") == "old" && !File.Exists(exe + ".new"), "swap puts the new exe in place and keeps the old one");
                Updates.Restore(exe);
                Check(File.ReadAllText(exe) == "old" && File.ReadAllText(exe + ".failed") == "new" && !File.Exists(exe + ".old"),
                    "restore brings the old exe back");
                // Running from a copy that is itself called Auto.old.exe must not clash with the backup name.
                string renamed = Path.Combine(folder, "Auto.old.exe");
                File.WriteAllText(renamed, "old");
                Updates.Swap(renamed, package);
                Check(File.ReadAllText(renamed) == "new" && File.ReadAllText(renamed + ".old") == "old", "an exe named Auto.old.exe updates too");
            }
            finally { Directory.Delete(folder, true); }
        }
        private static void RunChecks()
        {
            string directory = Path.Combine(Path.GetTempPath(), "Auto.Integration." + Guid.NewGuid().ToString("N"));
            string settingsPath = Path.Combine(directory, "autoclick.xml");
            try
            {
                AppSettings settings = new AppSettings { SelectedIndex = 0 };
                Scenario first = new Scenario { Name = "Kịch bản mẫu" };
                first.Steps.Add(new ClickStep { X = 450, Y = 250, DelayMs = 750 });
                settings.Scenarios.Add(first);
                settings.Scenarios.Add(new Scenario { Name = "Kịch bản thứ hai", Mode = RepeatMode.UntilStopped });
                SettingsStore.Save(settings, settingsPath);
                using (AutoForm form = new AutoForm(null, settingsPath, true))
                {
                    form.StartPosition = FormStartPosition.Manual; form.ShowInTaskbar = false;
                    form.Show(); form.Location = new Point(-20000, -20000); Application.DoEvents();
                    RadioButton[] navigation = (RadioButton[])Field(form, "_navItems");
                    Panel[] pages = (Panel[])Field(form, "_pages");
                    Check(form.Text == "Auto" && navigation.Length == 3 && pages.Length == 3 && navigation[2].Text == "Cài đặt", "vertical navigation includes Settings");
                    Check(navigation[0].Left == navigation[2].Left && navigation[0].Top < navigation[1].Top && navigation[1].Top < navigation[2].Top, "navigation items are stacked vertically");
                    Form paste = (Form)Field(form, "_paste"), click = (Form)Field(form, "_click");
                    Check(!paste.TopLevel && !click.TopLevel && click.Parent == pages[AutoForm.ClickPage] && paste.Parent == pages[AutoForm.PastePage], "both modules run inside one window");
                    Check(navigation[AutoForm.ClickPage].Text == "Auto Click" && navigation[AutoForm.PastePage].Text == "Auto Paste", "navigation order matches the pages");
                    Check(!((NotifyIcon)Field(paste, "_notifyIcon")).Visible && !((NotifyIcon)Field(click, "_tray")).Visible && ((NotifyIcon)Field(form, "_tray")).Visible, "only one tray icon is visible");
                    Size fixedSize = form.Size;
                    for (int i = 0; i < 3; i++)
                    {
                        Point fixedLocation = form.Location;
                        navigation[i].Checked = true;
                        // Let the short highlight fade finish before the page is captured.
                        Stopwatch fade = Stopwatch.StartNew();
                        while (fade.ElapsedMilliseconds < 350) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
                        Check(form.Size == fixedSize, "window size remains fixed on page " + i);
                        Check(form.Location == fixedLocation, "window position remains fixed on page " + i);
                        for (int page = 0; page < 3; page++) Check(pages[page].Visible == (page == i), "only page " + i + " is shown");
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                            bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, i == AutoForm.ClickPage ? "Auto.Click.preview.png" : i == AutoForm.PastePage ? "Auto.Paste.preview.png" : "Auto.Settings.preview.png"));
                        }
                    }
                    Check(pages[AutoForm.ClickPage].ClientSize.Width >= 840 && pages[AutoForm.ClickPage].ClientSize.Height >= 620, "click layout fits the integrated page");
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
