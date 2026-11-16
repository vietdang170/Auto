using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;
using VietDang.Design;

// Updates come from GitHub Releases of vietdang170/V-TOOL. A release is tagged vX.Y.Z and carries V-TOOL.exe; its SHA-256
// comes from the digest GitHub computes for uploads (or from an optional V-TOOL.exe.sha256 file). V-TOOL downloads the new
// exe in the background into its own update folder, checks it, and only swaps it in when the user clicks "Tắt và cập nhật".
// The swap happens after V-TOOL has closed: the running exe is kept as V-TOOL.exe.old, the new one takes its name and is
// started. If the new version does not report that it started properly within 30 seconds, the old exe is put back and started.
namespace VietDang.VTool
{
    internal sealed class ReleaseInfo
    {
        internal Version Version;
        internal string Notes;
        internal string ExeUrl;
        internal string HashUrl;
        internal string Hash;         // SHA-256 that GitHub computed for the exe, when it provides one
        internal string Downloaded;   // verified copy in the update folder, once downloaded
        internal string Problem;      // why this release cannot be installed (no V-TOOL.exe, no checksum); null when it can
    }

    internal static class Updates
    {
        // GitHub keeps redirecting an old name if the repository is renamed again, and the update check follows the redirect.
        internal const string Repository = "vietdang170/V-TOOL";
        internal const string ExeName = "V-TOOL.exe";
        private const string AssemblyIdentity = "V-TOOL";
        private const string Key = Program.SettingsKey;
        private const int StartTimeoutMs = 30000;

        // Set by the window when the user chooses to update; acted on by Main after V-TOOL has closed.
        internal static ReleaseInfo PendingInstall;
        internal static bool ReopenWindow = true;

        internal static Version Current { get { return Normalize(Assembly.GetExecutingAssembly().GetName().Version); } }
        internal static string Folder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VietDang", "V-TOOL", "updates"); }
        }

        internal static Version Normalize(Version v) { return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }

        // "v1.2.3", "1.2.3" and "1.2" are accepted; anything else is not a release tag.
        internal static Version ParseTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            string text = tag.Trim();
            if (text.StartsWith("v", StringComparison.OrdinalIgnoreCase)) text = text.Substring(1);
            Version version;
            if (!Version.TryParse(text, out version)) return null;
            return Normalize(version);
        }

        // ---- remembered state ----------------------------------------------------------------------
        private static object Read(string name, object fallback)
        {
            try { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(Key)) return key == null ? fallback : key.GetValue(name, fallback); }
            catch (Exception) { return fallback; }
        }
        private static void Write(string name, object value, RegistryValueKind kind)
        {
            try { using (RegistryKey key = Registry.CurrentUser.CreateSubKey(Key)) key.SetValue(name, value, kind); }
            catch (Exception) { }
        }
        internal static DateTime LastCheck
        {
            get { long ticks; return long.TryParse(Convert.ToString(Read("LastUpdateCheck", "0"), CultureInfo.InvariantCulture), out ticks) && ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc) : DateTime.MinValue; }
            set { Write("LastUpdateCheck", value.Ticks.ToString(CultureInfo.InvariantCulture), RegistryValueKind.String); }
        }
        // A version whose install failed is not offered again automatically (a manual check still offers it).
        internal static string FailedVersion
        {
            get { return Convert.ToString(Read("FailedUpdateVersion", ""), CultureInfo.InvariantCulture); }
            set { Write("FailedUpdateVersion", value ?? "", RegistryValueKind.String); }
        }
        internal static bool CheckDue { get { return DateTime.UtcNow - LastCheck >= TimeSpan.FromHours(23); } }

        // ---- GitHub ------------------------------------------------------------------------------------
        internal static HttpWebRequest Request(string url, string accept)
        {
            // .NET Framework 4 does not enable TLS 1.2 by default; GitHub requires it.
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "V-TOOL-Updater/" + Current;
            request.Accept = accept;
            request.Timeout = 20000;
            request.ReadWriteTimeout = 30000;
            return request;
        }

        // Returns null when the repository has no published release yet.
        internal static ReleaseInfo FetchLatest()
        {
            try { return FetchFromApi(); }
            catch (WebException ex)
            {
                // The API answers at most 60 requests an hour per network address. Offices and schools share one address
                // and run out (403/429); the release pages on github.com have no such limit, so read those instead.
                HttpWebResponse http = ex.Response as HttpWebResponse;
                if (http == null || ((int)http.StatusCode != 403 && (int)http.StatusCode != 429)) throw;
            }
            return FetchFromWebsite();
        }

        private static ReleaseInfo FetchFromWebsite()
        {
            string latest = "https://github.com/" + Repository + "/releases/latest", location = null;
            // /releases/latest redirects to /releases/tag/<tag>, or to /releases when nothing is published.
            // A renamed repository first redirects to the new name's /releases/latest, so follow a few hops.
            for (int hop = 0; hop < 4 && latest != null; hop++)
            {
                HttpWebRequest request = Request(latest, "text/html");
                request.AllowAutoRedirect = false;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) location = response.Headers["Location"];
                if (location != null && location.StartsWith("/")) location = "https://github.com" + location;
                latest = location != null && location.TrimEnd('/').EndsWith("/releases/latest", StringComparison.OrdinalIgnoreCase) ? location : null;
            }
            int at = location == null ? -1 : location.IndexOf("/releases/tag/", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            string site = location.Substring(0, at) + "/releases/";
            string tag = Uri.UnescapeDataString(location.Substring(at + 14).Split('?', '#')[0]);
            ReleaseInfo info = new ReleaseInfo { Version = ParseTag(tag), Notes = "Xem ghi chú tại " + site + "tag/" + tag };
            if (info.Version == null) throw new InvalidDataException("Tên phiên bản trên GitHub không đúng dạng vX.Y.Z.");
            string html;
            using (WebResponse response = Request(site + "expanded_assets/" + Uri.EscapeDataString(tag), "text/html").GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                html = reader.ReadToEnd();
            if (html.IndexOf("/releases/download/" + tag + "/" + ExeName + "\"", StringComparison.OrdinalIgnoreCase) < 0)
            {
                info.Problem = "Bản phát hành " + info.Version + " chưa có file " + ExeName + ".";
                return info;
            }
            info.ExeUrl = site + "download/" + tag + "/" + ExeName;
            info.Hash = FindDigest(html, ExeName);
            if (info.Hash == null) info.HashUrl = info.ExeUrl + ".sha256";
            return info;
        }

        // The asset list on the release page shows "sha256:<hex>" under each file; take the one that follows the exe.
        internal static string FindDigest(string html, string asset)
        {
            Match match = Regex.Match(html ?? "", "/releases/download/[^\"]*/" + Regex.Escape(asset) + "\"[\\s\\S]*?sha256:([0-9a-fA-F]{64})");
            return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
        }

        private static ReleaseInfo FetchFromApi()
        {
            string json;
            try
            {
                using (WebResponse response = Request("https://api.github.com/repos/" + Repository + "/releases/latest", "application/vnd.github+json").GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    json = reader.ReadToEnd();
            }
            catch (WebException ex)
            {
                HttpWebResponse http = ex.Response as HttpWebResponse;
                if (http != null && http.StatusCode == HttpStatusCode.NotFound) return null;
                throw;
            }
            return ParseRelease(json);
        }

        internal static ReleaseInfo ParseRelease(string json)
        {
            Dictionary<string, object> release = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (release == null) throw new InvalidDataException("GitHub trả về dữ liệu không đọc được.");
            object draft, prerelease;
            if ((release.TryGetValue("draft", out draft) && true.Equals(draft)) || (release.TryGetValue("prerelease", out prerelease) && true.Equals(prerelease))) return null;
            ReleaseInfo info = new ReleaseInfo { Version = ParseTag(Text(release, "tag_name")), Notes = Text(release, "body") };
            if (info.Version == null) throw new InvalidDataException("Tên phiên bản trên GitHub không đúng dạng vX.Y.Z.");
            Dictionary<string, Dictionary<string, object>> byName = new Dictionary<string, Dictionary<string, object>>(StringComparer.OrdinalIgnoreCase);
            object assets;
            if (release.TryGetValue("assets", out assets) && assets is IEnumerable)
            {
                foreach (object item in (IEnumerable)assets)
                {
                    Dictionary<string, object> asset = item as Dictionary<string, object>;
                    string name = asset == null ? null : Text(asset, "name");
                    if (name != null) byName[name] = asset;
                }
            }
            // A release that cannot be installed is still returned, so an older one is simply "not newer" rather than an error.
            Dictionary<string, object> exe, hashFile;
            if (!byName.TryGetValue(ExeName, out exe))
            {
                info.Problem = "Bản phát hành " + info.Version + " chưa có file " + ExeName + ".";
                return info;
            }
            info.ExeUrl = Text(exe, "browser_download_url");
            // GitHub computes a "sha256:<hex>" digest for every uploaded file.
            string digest = Text(exe, "digest");
            if (digest != null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)) info.Hash = ParseHash(digest.Substring(7));
            if (byName.TryGetValue(ExeName + ".sha256", out hashFile)) info.HashUrl = Text(hashFile, "browser_download_url");
            if (info.Hash == null && info.HashUrl == null) info.Problem = "Bản phát hành " + info.Version + " chưa có mã kiểm tra cho " + ExeName + ".";
            return info;
        }
        private static string Text(Dictionary<string, object> map, string name)
        {
            object value;
            return map.TryGetValue(name, out value) && value != null ? Convert.ToString(value, CultureInfo.InvariantCulture) : null;
        }

        internal static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                StringBuilder hex = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(stream)) hex.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return hex.ToString();
            }
        }

        // The .sha256 file holds the hash first, optionally followed by the file name ("<hash>  V-TOOL.exe").
        internal static string ParseHash(string text)
        {
            if (text == null) return null;
            string[] parts = text.Trim().Split(new char[] { ' ', '\t', '\r', '\n', '*' }, StringSplitOptions.RemoveEmptyEntries);
            string first = parts.Length > 0 ? parts[0] : "";
            if (first.Length != 64) return null;
            foreach (char c in first) if (!Uri.IsHexDigit(c)) return null;
            return first.ToLowerInvariant();
        }

        // A file is accepted only if its hash matches and it really is V-TOOL at the announced version.
        internal static bool IsValidPackage(string path, string expectedHash, Version version)
        {
            if (!File.Exists(path) || !string.Equals(Sha256(path), expectedHash, StringComparison.OrdinalIgnoreCase)) return false;
            try
            {
                AssemblyName name = AssemblyName.GetAssemblyName(path);
                return name.Name == AssemblyIdentity && Normalize(name.Version) == version;
            }
            catch (BadImageFormatException) { return false; }
            catch (IOException) { return false; }
        }

        // Downloads the release into the update folder (or reuses an earlier verified download).
        internal static void Download(ReleaseInfo release)
        {
            Directory.CreateDirectory(Folder);
            string target = Path.Combine(Folder, "V-TOOL-" + release.Version + ".exe");
            string hash = release.Hash;
            if (hash == null)
            {
                using (WebResponse response = Request(release.HashUrl, "application/octet-stream").GetResponse())
                using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                    hash = ParseHash(reader.ReadToEnd());
            }
            if (hash == null) throw new InvalidDataException("File .sha256 trên GitHub không hợp lệ.");
            if (!IsValidPackage(target, hash, release.Version))
            {
                string part = target + ".part";
                using (WebResponse response = Request(release.ExeUrl, "application/octet-stream").GetResponse())
                using (Stream input = response.GetResponseStream())
                using (FileStream output = File.Create(part))
                    input.CopyTo(output);
                if (!IsValidPackage(part, hash, release.Version))
                {
                    File.Delete(part);
                    throw new InvalidDataException("File tải về không khớp mã kiểm tra, đã bỏ.");
                }
                if (File.Exists(target)) File.Delete(target);
                File.Move(part, target);
            }
            // Older downloads are no longer needed.
            foreach (string file in Directory.GetFiles(Folder, "V-TOOL-*.exe*"))
                if (!string.Equals(file, target, StringComparison.OrdinalIgnoreCase)) { try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            release.Downloaded = target;
        }

        // The exe can only be replaced if its folder is writable (not the case under Program Files).
        internal static string FolderProblem()
        {
            string folder = Path.GetDirectoryName(Application.ExecutablePath);
            try
            {
                string probe = Path.Combine(folder, "auto-write-test." + Guid.NewGuid().ToString("N") + ".tmp");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return null;
            }
            catch (Exception ex)
            {
                return "Không thể ghi vào thư mục " + folder + " (" + ex.Message + "). Hãy chuyển " + Path.GetFileName(Application.ExecutablePath) + " sang Desktop hoặc Documents rồi thử lại.";
            }
        }

        // ---- swapping the exe ------------------------------------------------------------------------------
        // The temporary and backup copies are named after the running exe itself (V-TOOL.exe.new, V-TOOL.exe.old), so they
        // can never be the file that is running, whatever name the exe has. They are not .exe files, so nobody opens them by mistake.
        internal static string StagedPath(string exe) { return exe + ".new"; }
        internal static string BackupPath(string exe) { return exe + ".old"; }
        internal static string FailedPath(string exe) { return exe + ".failed"; }

        // Puts the new exe in place of the current one and keeps the current one as the backup.
        internal static void Swap(string exe, string package)
        {
            string staged = StagedPath(exe), backup = BackupPath(exe);
            File.Copy(package, staged, true);
            try
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(exe, backup);
            }
            catch { TryDelete(staged); throw; }
            try { File.Move(staged, exe); }
            catch
            {
                File.Move(backup, exe);
                TryDelete(staged);
                throw;
            }
        }
        // Undoes Swap: the new exe is set aside and the backup gets its name back.
        internal static void Restore(string exe)
        {
            string backup = BackupPath(exe), failed = FailedPath(exe);
            if (!File.Exists(backup)) return;
            if (File.Exists(failed)) File.Delete(failed);
            if (File.Exists(exe)) File.Move(exe, failed);
            File.Move(backup, exe);
        }
        private static bool TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
        // Removes leftovers of an interrupted update.
        internal static void CleanUp() { TryDelete(StagedPath(Application.ExecutablePath)); }

        // Runs from Main after the window has closed and the single-instance locks are released.
        internal static void Install(ReleaseInfo release, bool showWindow)
        {
            string exe = Application.ExecutablePath;
            try { Swap(exe, release.Downloaded); }
            catch (Exception ex)
            {
                MessageBox.Show("Chưa cập nhật được, " + Program.AppName + " vẫn giữ phiên bản " + Current + ".\n\n" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Relaunch(exe, showWindow, null);
                return;
            }
            string signal = "VietDang_VTOOL_Updated_" + Guid.NewGuid().ToString("N");
            using (EventWaitHandle ready = new EventWaitHandle(false, EventResetMode.ManualReset, signal))
            {
                Process child = null;
                bool started = false;
                try
                {
                    child = Relaunch(exe, showWindow, signal);
                    Stopwatch clock = Stopwatch.StartNew();
                    while (clock.ElapsedMilliseconds < StartTimeoutMs)
                    {
                        if (ready.WaitOne(250)) { started = true; break; }
                        if (child == null || child.HasExited) break;
                    }
                }
                catch (Exception) { started = false; }
                if (started) { if (child != null) child.Dispose(); return; }
                // The new version did not come up: stop it, put the old exe back and start that instead.
                try { if (child != null && !child.HasExited) child.Kill(); } catch (Exception) { }
                if (child != null) { try { child.WaitForExit(5000); } catch (Exception) { } child.Dispose(); }
                FailedVersion = release.Version.ToString();
                try { Restore(exe); }
                catch (Exception ex)
                {
                    MessageBox.Show("Cập nhật lỗi và không khôi phục được tự động.\nHãy đổi tên " + Path.GetFileName(BackupPath(exe)) + " thành " + Path.GetFileName(exe) + " trong thư mục:\n"
                        + Path.GetDirectoryName(exe) + "\n\n" + ex.Message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                Relaunch(exe, showWindow, null, "/update-failed:" + release.Version);
            }
        }

        private static Process Relaunch(string exe, bool showWindow, string signal, string extra = null)
        {
            StringBuilder args = new StringBuilder();
            if (!showWindow) args.Append("/minimized ");
            if (signal != null) args.Append("/updated:").Append(signal).Append(' ');
            if (extra != null) args.Append(extra);
            return Process.Start(new ProcessStartInfo(exe, args.ToString().Trim()) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        // Called by the new version once its window is up: tells the old process all went well, then removes the backup.
        internal static void ReportStarted(string signal)
        {
            if (string.IsNullOrEmpty(signal)) return;
            try { using (EventWaitHandle ready = EventWaitHandle.OpenExisting(signal)) ready.Set(); }
            catch (Exception) { }
            FailedVersion = "";
            string backup = BackupPath(Application.ExecutablePath);
            ThreadPool.QueueUserWorkItem(delegate
            {
                // The old process is still exiting for a moment; retry for up to ten seconds.
                for (int i = 0; i < 20 && !TryDelete(backup); i++) Thread.Sleep(500);
            });
        }
    }

    // "A new version is ready" dialog: just the version and two choices.
    internal sealed class UpdateDialog : Form
    {
        internal UpdateDialog(ReleaseInfo release)
        {
            Text = "Cập nhật " + Program.AppName;
            ClientSize = new Size(420, 136);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
            TopMost = true; Font = Theme.Regular; BackColor = Theme.Window;

            TableLayoutPanel layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(14) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(Kit.PageHeader(Glyph.Sync, "Đã có phiên bản mới " + release.Version, ""), 0, 0);
            FlowLayoutPanel buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Margin = Padding.Empty, WrapContents = false };
            UiButton install = new UiButton("Tắt và cập nhật", Glyph.Sync, ButtonKind.Success) { Height = 36, DialogResult = DialogResult.OK };
            UiButton skip = new UiButton("Bỏ qua", null, ButtonKind.Secondary) { Height = 36, MinimumFit = 90, Margin = new Padding(0, 0, 8, 0), DialogResult = DialogResult.Cancel };
            buttons.Controls.Add(install); buttons.Controls.Add(skip);
            layout.Controls.Add(buttons, 0, 1);
            Controls.Add(layout);
            AcceptButton = install; CancelButton = skip;
            Dpi.Fit(this);
        }
    }
}
