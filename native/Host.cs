using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Akis {
public static class Host {
    static readonly string Root = AppDomain.CurrentDomain.BaseDirectory;
    static readonly object WriteLock = new object(), Gate = new object();
    static readonly Stream Input = Console.OpenStandardInput(), Output = Console.OpenStandardOutput();
    static string Folder = OutputFolder();
    static bool Busy, Cancelled, ChoosingFolder, Importing;
    static Process Child;
    static Dictionary<string, object> Video;
    static readonly Dictionary<string, bool> Formats = new Dictionary<string, bool>();
    static IntPtr JobHandle;

    static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024, RecursionLimit = 128 }; }
    static Dictionary<string, object> Obj(params object[] values) {
        var d = new Dictionary<string, object>();
        for (int i = 0; i < values.Length; i += 2) d[(string)values[i]] = values[i + 1];
        return d;
    }
    static string S(IDictionary<string, object> d, string key) { object v; return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : ""; }
    static double N(IDictionary<string, object> d, string key) { double v; return double.TryParse(S(d, key), NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0; }
    static bool B(IDictionary<string, object> d, string key) { return S(d, key).Equals("True", StringComparison.OrdinalIgnoreCase); }
    static byte[] ReadExact(int count) {
        var bytes = new byte[count]; int offset = 0;
        while (offset < count) { int n = Input.Read(bytes, offset, count - offset); if (n == 0) { if (offset == 0) return null; throw new EndOfStreamException(); } offset += n; }
        return bytes;
    }
    static void Send(object value) {
        byte[] bytes = Encoding.UTF8.GetBytes(Json().Serialize(value));
        lock (WriteLock) { Output.Write(BitConverter.GetBytes(bytes.Length), 0, 4); Output.Write(bytes, 0, bytes.Length); Output.Flush(); }
    }
    static void Reply(string id, object data) { Send(Obj("id", id, "ok", true, "data", data)); }
    static void Fail(string id, string error) { Send(Obj("id", id, "ok", false, "error", error)); }
    static void Progress(string status, double percent, string message, SoundpadResult soundpad = null) {
        lock (Gate) {
            if (Cancelled && status != "cancelled" && status != "error") return;
            var job = Obj("status", status, "percent", Math.Max(0, Math.Min(100, percent)), "title", Video == null ? "" : S(Video, "title"), "message", message);
            if (soundpad != null) job["soundpad"] = Obj("status", soundpad.Status, "message", soundpad.Message);
            Send(Obj("event", "progress", "job", job));
        }
    }

    public static int Main(string[] args) {
        if (!IsAllowedCaller(args)) return 2;
        try {
            for (;;) {
                byte[] header = ReadExact(4); if (header == null) break;
                uint length = BitConverter.ToUInt32(header, 0);
                if (length == 0 || length > 65536) return 3;
                byte[] payload = ReadExact((int)length); if (payload == null) break;
                Dictionary<string, object> request;
                try { request = Json().Deserialize<Dictionary<string, object>>(Encoding.UTF8.GetString(payload)); }
                catch { Fail("", "Geçersiz ileti."); continue; }
                string id = S(request, "id"), action = S(request, "action");
                try {
                    if (action == "health") { CheckTools(); lock (Gate) { var health = FolderInfo(); health["version"] = "1.0.0"; health["capabilities"] = new[] { "soundpadCategories", "soundpadImport" }; Reply(id, health); } }
                    else if (action == "soundpadCategories") {
                        ThreadPool.QueueUserWorkItem(delegate {
                            try { Reply(id, Obj("categories", Soundpad.ListCategories().Select(c => Obj("index", c.Index, "name", c.Name, "path", c.Path, "label", c.Label, "selectable", c.Selectable)).ToArray())); }
                            catch (Exception e) { Fail(id, e.Message); }
                        });
                    }
                    else if (action == "folder") { Directory.CreateDirectory(Folder); Process.Start(new ProcessStartInfo("explorer.exe", Quote(Folder)) { UseShellExecute = false, CreateNoWindow = true }); Reply(id, Obj()); }
                    else if (action == "chooseFolder") ChooseFolder(id);
                    else if (action == "resetFolder") { lock (Gate) { EnsureFolderChangeAllowed(); SaveFolder(null); Reply(id, FolderInfo()); } }
                    else if (action == "cancel") { lock (Gate) { if (Importing) throw new Exception("Dosya kaydedildi; Soundpad'e ekleme tamamlanıyor."); Cancel(); Reply(id, Obj()); } }
                    else if (action == "inspect" || action == "download") {
                        string url = Normalize(S(request, "url")); CheckTools();
                        lock (Gate) {
                            if (Busy) throw new Exception("Başka bir işlem sürüyor. Bitmesini bekleyin.");
                            if (ChoosingFolder && action == "download") throw new Exception("Önce klasör seçimini tamamlayın.");
                            if (action == "download") ValidateDownload(request, url);
                            Busy = true; Cancelled = false;
                        }
                        if (action == "download") Reply(id, Obj("accepted", true));
                        ThreadPool.QueueUserWorkItem(delegate {
                            try {
                                if (action == "inspect") { object result = Inspect(url); lock (Gate) { Busy = false; Reply(id, result); } }
                                else { string file = Download(request, url); FinishDownload(request, file); }
                            }
                            catch (Exception e) {
                                lock (Gate) {
                                    Busy = false;
                                    Importing = false;
                                    if (action == "inspect") Fail(id, Friendly(e.Message));
                                    else Progress(Cancelled ? "cancelled" : "error", 0, Cancelled ? "İndirme iptal edildi." : Friendly(e.Message));
                                }
                            }
                        });
                    } else Fail(id, "Bilinmeyen işlem.");
                } catch (Exception e) { Fail(id, Friendly(e.Message)); }
            }
        } catch (IOException) { } finally { Cancel(); }
        return 0;
    }
    static bool IsAllowedCaller(string[] args) {
        try {
            string chromiumId = File.ReadAllText(Path.Combine(Root, "extension-id.txt")).Trim();
            if (Regex.IsMatch(chromiumId, "^[a-p]{32}$") && args.Length >= 1 && args[0] == "chrome-extension://" + chromiumId + "/") return true;
            // Firefox sends the host manifest path first, then the add-on ID.
            string manifest = Path.Combine(Root, "host-firefox.json");
            string firefoxId = File.ReadAllText(Path.Combine(Root, "firefox-extension-id.txt")).Trim();
            return args.Length >= 2 && firefoxId == "diskora@diskora.local" && args[1] == firefoxId && File.Exists(manifest)
                && string.Equals(Path.GetFullPath(args[0]), Path.GetFullPath(manifest), StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
    }
    static void CheckTools() {
        foreach (string file in new[] { "yt-dlp.exe", "ffmpeg.exe", "ffprobe.exe", "deno.exe" })
            if (!File.Exists(Path.Combine(Root, "bin", file))) throw new Exception("Eksik bileşen: " + file + ". KUR.cmd dosyasını yeniden çalıştırın.");
    }
    static void FinishDownload(Dictionary<string, object> request, string file) {
        bool import = WantsSoundpad(request);
        SoundpadResult result = null;
        lock (Gate) {
            if (Cancelled) { Busy = false; Progress("cancelled", 100, "İşlem iptal edildi. Kaydedilmiş dosya indirme klasöründe korunur."); return; }
            Importing = import;
            if (import) Progress("importing", 100, "Soundpad · seçilen kategoriye ekleniyor…");
        }
        if (import) {
            try { result = Soundpad.Import(file, CategoryPath(request)); }
            catch (Exception e) { result = new SoundpadResult("error", "Soundpad'e eklenemedi. " + Friendly(e.Message)); }
        }
        lock (Gate) { Importing = false; Busy = false; Progress("done", 100, Path.GetFileName(file), result); }
    }
    static bool WantsSoundpad(Dictionary<string, object> request) {
        object selected;
        return request.TryGetValue("soundpadAuto", out selected) && selected is bool && Soundpad.Enabled((bool)selected, S(request, "mode"));
    }
    static string[] CategoryPath(Dictionary<string, object> request) {
        object value;
        if (!request.TryGetValue("soundpadCategory", out value) || value is string || !(value is IEnumerable)) throw new Exception("Önce bir Soundpad kategorisi seçin.");
        var path = new List<string>();
        foreach (object part in (IEnumerable)value) {
            if (!(part is string) || string.IsNullOrWhiteSpace((string)part) || ((string)part).Length > 1024 || path.Count >= 32) throw new Exception("Geçersiz Soundpad kategorisi. Kategoriyi yeniden seçin.");
            path.Add((string)part);
        }
        if (path.Count == 0) throw new Exception("Önce bir Soundpad kategorisi seçin.");
        return path.ToArray();
    }
    public static string Normalize(string raw) {
        Uri uri;
        if (!Uri.TryCreate(raw, UriKind.Absolute, out uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo != "") throw new Exception("Geçersiz YouTube bağlantısı.");
        string id = "", host = uri.Host.ToLowerInvariant();
        if (host == "youtu.be") id = uri.AbsolutePath.Trim('/');
        else if (new[] { "youtube.com", "www.youtube.com", "m.youtube.com", "music.youtube.com" }.Contains(host)) {
            if (uri.AbsolutePath == "/watch") {
                foreach (string part in uri.Query.TrimStart('?').Split('&')) { string[] pair = part.Split(new[] { '=' }, 2); if (pair.Length == 2 && pair[0] == "v") { id = Uri.UnescapeDataString(pair[1]); break; } }
            } else { Match m = Regex.Match(uri.AbsolutePath, "^/(?:shorts|live|embed)/([A-Za-z0-9_-]{11})/?$"); if (m.Success) id = m.Groups[1].Value; }
        }
        if (!Regex.IsMatch(id, "^[A-Za-z0-9_-]{11}$")) throw new Exception("Yalnızca YouTube video bağlantıları destekleniyor.");
        return "https://www.youtube.com/watch?v=" + id;
    }
    static List<string> Common() {
        return new List<string> { "--ignore-config", "--no-playlist", "--no-colors", "--encoding", "utf-8", "--socket-timeout", "25", "--retries", "3", "--extractor-retries", "2", "--ffmpeg-location", Path.Combine(Root, "bin"), "--js-runtimes", "deno:" + Path.Combine(Root, "bin", "deno.exe") };
    }
    static object Inspect(string url) {
        lock (Gate) { Video = null; Formats.Clear(); }
        var args = Common(); args.AddRange(new[] { "--dump-single-json", "--skip-download", "--", url });
        var output = new StringBuilder();
        Run(args, delegate(string line) { if (output.Length + line.Length > 32 * 1024 * 1024) throw new Exception("Video bilgisi çok büyük."); output.AppendLine(line); }, 120000);
        var info = Json().Deserialize<Dictionary<string, object>>(output.ToString());
        if (B(info, "is_live") || S(info, "live_status") == "is_upcoming") throw new Exception("Canlı veya henüz başlamamış yayın desteklenmiyor. Yayın bittikten sonra tekrar deneyin.");
        if (S(info, "_type") == "playlist") throw new Exception("Bir oynatma listesi yerine tek bir video açın.");
        var best = new Dictionary<int, Dictionary<string, object>>();
        object raw;
        if (info.TryGetValue("formats", out raw) && raw is IEnumerable) {
            foreach (object item in (IEnumerable)raw) {
                var f = item as Dictionary<string, object>; if (f == null || B(f, "has_drm")) continue;
                string codec = S(f, "vcodec"), id = S(f, "format_id"); int height = (int)N(f, "height");
                if (height <= 0 || !Regex.IsMatch(codec, "^(avc|h264|av01|av1|vp9|vp09)") || !Regex.IsMatch(id, "^[A-Za-z0-9_-]+$")) continue;
                Dictionary<string, object> old;
                if (!best.TryGetValue(height, out old) || Score(f) > Score(old)) best[height] = f;
            }
        }
        var qualities = new List<object>();
        lock (Gate) {
            foreach (var pair in best.OrderByDescending(p => p.Key)) {
                var f = pair.Value; string id = S(f, "format_id");
                Formats[id] = S(f, "acodec") != "none" && S(f, "acodec") != "";
                qualities.Add(Obj("id", id, "height", pair.Key, "fps", N(f, "fps")));
            }
            Video = Obj("url", url, "title", S(info, "title"), "channel", S(info, "channel") == "" ? S(info, "uploader") : S(info, "channel"), "duration", N(info, "duration"), "qualities", qualities);
            return Video;
        }
    }
    static double Score(Dictionary<string, object> f) { return N(f, "fps") * 1000000 + (S(f, "vcodec").StartsWith("avc") ? 100000 : 0) + N(f, "tbr"); }
    static void ValidateDownload(Dictionary<string, object> request, string url) {
        if (Video == null || S(Video, "url") != url) throw new Exception("Önce video bilgilerini yenileyin.");
        string mode = S(request, "mode"), q = S(request, "quality");
        if (mode == "mp3") { if (!new[] { "128", "192", "256", "320" }.Contains(q)) throw new Exception("Geçersiz MP3 kalitesi."); }
        else if (mode == "mp4") { if (!Formats.ContainsKey(q)) throw new Exception("Geçersiz video kalitesi. Bilgileri yenileyin."); }
        else throw new Exception("Geçersiz dosya biçimi.");
        if (WantsSoundpad(request)) CategoryPath(request);
    }
    static string Download(Dictionary<string, object> request, string url) {
        string mode = S(request, "mode"), q = S(request, "quality");
        Directory.CreateDirectory(Folder);
        string staging = Path.Combine(Folder, ".akis-temp", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string suffix = mode == "mp3" ? q + "kbps" : "video-" + q;
        var args = Common();
        args.AddRange(new[] { "--newline", "--progress", "--progress-delta", "0.7", "--no-simulate", "--no-overwrites", "--windows-filenames", "--trim-filenames", "160", "--paths", "home:" + Folder, "--paths", "temp:" + staging, "--output", "%(title).100B [%(id)s] - " + suffix + ".%(ext)s", "--progress-template", "download:AKIS_PROGRESS:%(progress._percent_str)s|%(progress._speed_str)s|%(progress._eta_str)s", "--progress-template", "postprocess:AKIS_PROCESS:%(progress.status)s", "--print", "after_move:AKIS_FILE:%(filepath)j" });
        if (mode == "mp3") args.AddRange(new[] { "--format", "bestaudio/best", "--extract-audio", "--audio-format", "mp3", "--audio-quality", q + "K" });
        else args.AddRange(new[] { "--format", Formats[q] ? q : q + "+bestaudio[ext=m4a]/" + q + "+bestaudio", "--merge-output-format", "mp4", "--remux-video", "mp4" });
        args.AddRange(new[] { "--", url });
        string file = "";
        Progress("starting", 0, "YouTube bağlantısı hazırlanıyor…");
        try {
            Run(args, delegate(string line) {
                if (line.StartsWith("AKIS_PROGRESS:")) {
                    string[] values = line.Substring(14).Split('|'); double percent;
                    double.TryParse(values[0].Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out percent);
                    Progress("downloading", percent, values.Length >= 3 ? values[1].Trim() + " · Kalan " + values[2].Trim() : "İndiriliyor…");
                } else if (line.StartsWith("AKIS_PROCESS:")) Progress("processing", 100, "Ses ve görüntü hazırlanıyor…");
                else if (line.StartsWith("AKIS_FILE:")) file = Json().Deserialize<string>(line.Substring(10));
            }, 0);
            if (Cancelled) throw new Exception("İptal edildi.");
            if (file == "" || !Path.GetFullPath(file).StartsWith(Path.GetFullPath(Folder).TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file) || new FileInfo(file).Length == 0 || !Path.GetExtension(file).Equals("." + mode, StringComparison.OrdinalIgnoreCase)) throw new Exception("Dosyanın kaydedildiği doğrulanamadı. İndirme klasörünü kontrol edin.");
            return file;
        } finally {
            try { Directory.Delete(staging, true); } catch { }
        }
    }
    static void Run(List<string> args, Action<string> lineHandler, int timeout) {
        var errors = new List<string>(); Exception callbackError = null;
        var start = new ProcessStartInfo(Path.Combine(Root, "bin", "yt-dlp.exe"), string.Join(" ", args.Select(Quote))) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = Root };
        using (var process = new Process { StartInfo = start }) {
            process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) { try { lineHandler(e.Data); } catch (Exception ex) { callbackError = ex; } } };
            process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e) { if (e.Data != null) lock(errors) { if (errors.Count >= 20) errors.RemoveAt(0); errors.Add(e.Data); } };
            lock (Gate) {
                if (Cancelled) throw new Exception("İptal edildi.");
                process.Start(); process.StandardInput.Close(); Child = process;
                try { JobHandle = CreateKillJob(process); } catch { process.Kill(); throw; }
            }
            try {
                process.BeginOutputReadLine(); process.BeginErrorReadLine();
                if (timeout > 0 && !process.WaitForExit(timeout)) { lock (Gate) { KillTree(); } process.WaitForExit(); throw new Exception("Video bilgileri alınırken zaman aşımı oluştu. Yeniden deneyin."); }
                process.WaitForExit();
                if (Cancelled) throw new Exception("İptal edildi.");
                if (callbackError != null) throw callbackError;
                if (process.ExitCode != 0) throw new Exception(string.Join("\n", errors.ToArray()));
            } finally { lock (Gate) { KillTree(); Child = null; } }
        }
    }
    static void Cancel() { lock (Gate) { Cancelled = true; KillTree(); } }
    static void KillTree() { if (JobHandle != IntPtr.Zero) { CloseHandle(JobHandle); JobHandle = IntPtr.Zero; } else if (Child != null) { try { if (!Child.HasExited) Child.Kill(); } catch { } } }
    public static string Quote(string s) {
        return "\"" + Regex.Replace(Regex.Replace(s, "(\\\\*)\"", "$1$1\\\""), "(\\\\+)$", "$1$1") + "\"";
    }
    static string Friendly(string message) {
        if (string.IsNullOrWhiteSpace(message)) return "İşlem başarısız oldu. Bağlantıyı kontrol edip tekrar deneyin.";
        string lower = message.ToLowerInvariant();
        if (lower.Contains("failed to resolve") || lower.Contains("getaddrinfo") || lower.Contains("network is unreachable")) return "YouTube'a bağlanılamadı. İnternet bağlantısını veya DNS ayarlarını kontrol edip tekrar deneyin.";
        if (lower.Contains("sign in") || lower.Contains("confirm you") || lower.Contains("private video") || lower.Contains("age-restricted")) return "YouTube bu video için oturum açma veya erişim doğrulaması istiyor. Bu sürüm herkese açık videoları destekliyor.";
        if (lower.Contains("403") || lower.Contains("requested format is not available") || lower.Contains("signature")) return "YouTube seçilen akışı sunmadı. Video bilgilerini yenileyin; sürerse GUNCELLE.cmd dosyasını çalıştırın.";
        if (lower.Contains("video unavailable") || lower.Contains("not available")) return "Bu video şu anda erişilebilir değil.";
        return message.Length > 650 ? message.Substring(message.Length - 650) : message;
    }
    [DllImport("shell32.dll")] static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
    static string DownloadsFolder() {
        IntPtr pointer;
        if (SHGetKnownFolderPath(new Guid("374DE290-123F-4565-9164-39C4925E467B"), 0, IntPtr.Zero, out pointer) == 0) { try { return Marshal.PtrToStringUni(pointer); } finally { Marshal.FreeCoTaskMem(pointer); } }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    }
    static string OutputFolder() {
        string config = Path.Combine(Root, "settings.json");
        try {
            if (File.Exists(config)) {
                string path = S(Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(config)), "downloadDirectory");
                if (Path.IsPathRooted(path) && Path.GetPathRoot(path).Length > 1) return Path.GetFullPath(path);
            }
        } catch { }
        return DownloadsFolder();
    }
    static Dictionary<string, object> FolderInfo() {
        return Obj("folder", Folder, "folderDefault", string.Equals(Folder.TrimEnd('\\', '/'), DownloadsFolder().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase), "folderChoosing", ChoosingFolder);
    }
    static void EnsureFolderChangeAllowed() {
        if (Busy) throw new Exception("Klasörü değiştirmek için devam eden işlemin bitmesini bekleyin.");
        if (ChoosingFolder) throw new Exception("Klasör seçme penceresi zaten açık.");
    }
    // Persists only native-dialog selections, never an extension-supplied path.
    public static void SaveFolder(string selected) {
        string folder = selected == null ? DownloadsFolder() : selected;
        if (!Path.IsPathRooted(folder) || Path.GetPathRoot(folder).Length < 2) throw new Exception("Geçerli bir klasör seçin.");
        folder = Path.GetFullPath(folder);
        if (!Directory.Exists(folder)) throw new Exception("Seçilen klasör bulunamadı.");
        try {
            using (var probe = new FileStream(Path.Combine(folder, ".akis-write-check-" + Guid.NewGuid().ToString("N") + ".tmp"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { probe.WriteByte(0); }
        } catch (IOException) { throw new Exception("Bu klasöre yazılamıyor. Başka bir klasör seçin."); }
        catch (UnauthorizedAccessException) { throw new Exception("Bu klasöre yazma izni yok. Başka bir klasör seçin."); }
        string config = Path.Combine(Root, "settings.json");
        var settings = new Dictionary<string, object>();
        try { if (File.Exists(config)) settings = Json().Deserialize<Dictionary<string, object>>(File.ReadAllText(config)) ?? settings; } catch { }
        if (selected == null) settings.Remove("downloadDirectory"); else settings["downloadDirectory"] = folder;
        string temp = config + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temp, Json().Serialize(settings), new UTF8Encoding(false));
            if (File.Exists(config)) File.Replace(temp, config, null); else File.Move(temp, config);
            Folder = folder;
        } finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    static void ChooseFolder(string id) {
        lock (Gate) { EnsureFolderChangeAllowed(); ChoosingFolder = true; }
        var thread = new Thread(delegate() {
            try {
                using (var owner = new Form { Text = "ClipFlow — Klasör seç", ShowInTaskbar = false, TopMost = true, Opacity = 0, StartPosition = FormStartPosition.CenterScreen })
                using (var picker = new FolderBrowserDialog { Description = "ClipFlow — İndirmelerin kaydedileceği klasörü seçin", SelectedPath = Directory.Exists(Folder) ? Folder : DownloadsFolder(), ShowNewFolderButton = true }) {
                    owner.Show(); owner.Activate();
                    var result = picker.ShowDialog(owner);
                    lock (Gate) {
                        if (result == DialogResult.OK) SaveFolder(picker.SelectedPath);
                        ChoosingFolder = false;
                        var data = FolderInfo(); data["cancelled"] = result != DialogResult.OK; Reply(id, data);
                    }
                }
            } catch (Exception e) { lock (Gate) { ChoosingFolder = false; Fail(id, Friendly(e.Message)); } }
        });
        thread.IsBackground = true;
        thread.SetApartmentState(ApartmentState.STA);
        try { thread.Start(); } catch { lock (Gate) { ChoosingFolder = false; } throw; }
    }
    [StructLayout(LayoutKind.Sequential)] struct BasicLimits { public long PerProcessUserTimeLimit, PerJobUserTimeLimit; public uint LimitFlags; public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize; public uint ActiveProcessLimit; public UIntPtr Affinity; public uint PriorityClass, SchedulingClass; }
    [StructLayout(LayoutKind.Sequential)] struct IoCounters { public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount, ReadTransferCount, WriteTransferCount, OtherTransferCount; }
    [StructLayout(LayoutKind.Sequential)] struct ExtendedLimits { public BasicLimits BasicLimitInformation; public IoCounters IoInfo; public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll")] static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    static IntPtr CreateKillJob(Process process) {
        IntPtr job = CreateJobObject(IntPtr.Zero, null); if (job == IntPtr.Zero) throw new Exception("İndirme süreci oluşturulamadı.");
        var limits = new ExtendedLimits(); limits.BasicLimitInformation.LimitFlags = 0x2000;
        int size = Marshal.SizeOf(limits); IntPtr memory = Marshal.AllocHGlobal(size);
        try {
            Marshal.StructureToPtr(limits, memory, false);
            if (!SetInformationJobObject(job, 9, memory, (uint)size) || !AssignProcessToJobObject(job, process.Handle)) { CloseHandle(job); throw new Exception("İndirme süreci yönetilemedi."); }
            return job;
        } finally { Marshal.FreeHGlobal(memory); }
    }
}}
