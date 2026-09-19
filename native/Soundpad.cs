using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Collections.Generic;

namespace Akis {
public sealed class SoundpadResult {
    public string Status;
    public string Message;
    public SoundpadResult(string status, string message) { Status = status; Message = message; }
}
public sealed class SoundpadCategory {
    public int Index;
    public string Name;
    public string[] Path;
    public string Label { get { return string.Join(" / ", Path); } }
    public bool Selectable;
}

// Uses Soundpad's documented local remote-control API. No playback commands.
public static class Soundpad {
    // Soundpad decodes requests using the Windows ANSI code page. Responses are UTF-8.
    // Never silently replace characters in a path with '?' or a best-fit character.
    static readonly Encoding RequestEncoding = Encoding.GetEncoding(Encoding.Default.CodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    public static bool Enabled(bool selected, string mode) { return selected && mode == "mp3"; }

    public static List<SoundpadCategory> ListCategories() {
        using (var pipe = new RemotePipe()) { return ListCategories(pipe.Request); }
    }
    public static List<SoundpadCategory> ListCategories(Func<string, string> request) {
        var result = new List<SoundpadCategory>();
        foreach (var element in Parse(request("GetCategories(false,false)")).Descendants("Category")) {
            int index;
            string name = (string)element.Attribute("name");
            if ((string)element.Attribute("type") == "1" || string.IsNullOrWhiteSpace(name) || !int.TryParse((string)element.Attribute("index"), out index) || index < 1) continue;
            var path = element.Ancestors("Category").Reverse().Select(e => (string)e.Attribute("name") ?? "").Concat(new[] {name}).ToArray();
            result.Add(new SoundpadCategory { Index = index, Name = name, Path = path });
        }
        foreach (var category in result) category.Selectable = result.Count(c => c.Path.SequenceEqual(category.Path)) == 1;
        return result;
    }
    public static SoundpadResult Import(string file, string[] categoryPath) {
        using (var pipe = new RemotePipe()) { return Import(file, categoryPath, pipe.Request); }
    }
    public static SoundpadResult Import(string file, string[] categoryPath, Func<string, string> request) {
        if (!File.Exists(file) || !Path.GetExtension(file).Equals(".mp3", StringComparison.OrdinalIgnoreCase)) throw new Exception("Soundpad'e eklemek için tamamlanmış bir MP3 dosyası gerekli.");
        if (categoryPath == null || categoryPath.Length == 0) throw new Exception("Önce bir Soundpad kategorisi seçin.");
        file = Path.GetFullPath(file);
        string quotedFile = QuoteArgument(file);
        var matches = ListCategories(request).Where(c => c.Path.SequenceEqual(categoryPath)).ToList();
        if (matches.Count == 0) throw new Exception("Seçilen Soundpad kategorisi artık bulunamıyor. Kategorileri yenileyip tekrar seçin.");
        if (matches.Count > 1) throw new Exception("Aynı konumda aynı adlı Soundpad kategorileri var. Soundpad'de adlarını ayırıp tekrar seçin.");
        var selected = matches[0];
        int category = selected.Index;
        var contents = Parse(request("GetCategory(" + category.ToString(CultureInfo.InvariantCulture) + ",true,false)"));
        var target = contents.Descendants("Category").SingleOrDefault(e => (string)e.Attribute("index") == category.ToString(CultureInfo.InvariantCulture));
        if (target == null || (string)target.Attribute("name") != selected.Name || (string)target.Attribute("type") == "1") throw new Exception("Soundpad kategori listesi değişti. Kategorileri yenileyip tekrar seçin.");
        if (target.Elements("Sound").Any(e => SameFile((string)e.Attribute("url"), file))) return new SoundpadResult("exists", "Soundpad · " + selected.Label + " kategorisinde zaten var.");
        CheckSuccess(request("DoAddSound(" + quotedFile + "," + category.ToString(CultureInfo.InvariantCulture) + ",-1)"));
        return new SoundpadResult("added", "Soundpad · " + selected.Label + " kategorisine eklendi.");
    }
    static bool SameFile(string source, string destination) {
        if (string.IsNullOrWhiteSpace(source)) return false;
        try {
            Uri uri;
            if (Uri.TryCreate(source, UriKind.Absolute, out uri) && uri.IsFile) source = uri.LocalPath;
            return string.Equals(Path.GetFullPath(source), destination, StringComparison.OrdinalIgnoreCase);
        } catch { return false; }
    }
    static string QuoteArgument(string value) {
        if (value.Any(c => c == '"' || char.IsControl(c))) throw new Exception("Dosya adı Soundpad'e aktarılamıyor.");
        return "\"" + value + "\"";
    }
    static XDocument Parse(string response) {
        if (response.StartsWith("R-", StringComparison.OrdinalIgnoreCase)) { CheckSuccess(response); throw new Exception("Soundpad kategori bilgisi döndürmedi."); }
        using (var reader = XmlReader.Create(new StringReader(response), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 4 * 1024 * 1024 })) {
            return XDocument.Load(reader);
        }
    }
    static void CheckSuccess(string response) {
        if (response == "R-200" || response.StartsWith("R-200:") || response.StartsWith("R-200 ")) return;
        if (response.StartsWith("R-403") || response.IndexOf("trial", StringComparison.OrdinalIgnoreCase) >= 0) throw new Exception("Soundpad eklemeye izin vermedi. Otomatik ekleme tam sürüm gerektirir.");
        if (response.StartsWith("R-404")) throw new Exception("Soundpad bu komutu desteklemiyor. Soundpad'i güncelleyin.");
        if (response.StartsWith("R-204")) throw new Exception("Dosya indirildi ancak Soundpad dosyaya erişemedi. Dosyanın hâlâ indirme klasöründe olduğunu kontrol edin.");
        throw new Exception("Soundpad dosyayı ekleyemedi: " + response.Substring(0, Math.Min(response.Length, 140)));
    }

    sealed class RemotePipe : IDisposable {
        readonly NamedPipeClientStream pipe;
        public RemotePipe() : this("sp_remote_control") { }
        public RemotePipe(string pipeName) {
            pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { pipe.Connect(1500); pipe.ReadMode = PipeTransmissionMode.Message; }
            catch (TimeoutException) { pipe.Dispose(); throw new Exception("Soundpad açık değil veya erişilemiyor. Soundpad'i açıp aynı indirmeyi yeniden deneyin."); }
            catch (UnauthorizedAccessException) { pipe.Dispose(); throw new Exception("Soundpad bağlantısına izin verilmedi. Soundpad ve Edge'i aynı kullanıcıyla, yönetici olmadan çalıştırın."); }
            catch { pipe.Dispose(); throw; }
        }
        public string Request(string command) {
            Thread.Sleep(2); // Soundpad documents a minimum gap between requests.
            var deadline = Stopwatch.StartNew();
            byte[] bytes;
            try { bytes = RequestEncoding.GetBytes(command); }
            catch (EncoderFallbackException) { throw new Exception("Dosya adında Soundpad'in desteklemediği karakterler var. MP3 kaydedildi; dosyayı daha basit bir adla Soundpad'e ekleyebilirsiniz."); }
            var write = pipe.BeginWrite(bytes, 0, bytes.Length, null, null);
            using (var wait = write.AsyncWaitHandle) {
                if (!wait.WaitOne(5000)) throw new Exception("Soundpad yanıt vermedi. İndirdiğiniz dosya kaydedildi.");
                pipe.EndWrite(write);
            }
            byte[] buffer = new byte[16384];
            using (var data = new MemoryStream()) {
                do {
                    var read = pipe.BeginRead(buffer, 0, buffer.Length, null, null);
                    int count;
                    using (var wait = read.AsyncWaitHandle) {
                        if (!wait.WaitOne(Math.Max(0, 5000 - (int)deadline.ElapsedMilliseconds))) throw new Exception("Soundpad yanıt vermedi. İndirdiğiniz dosya kaydedildi.");
                        count = pipe.EndRead(read);
                    }
                    if (count == 0) throw new Exception("Soundpad bağlantısı kesildi.");
                    data.Write(buffer, 0, count);
                    if (data.Length > 4 * 1024 * 1024) throw new Exception("Soundpad kategori listesi çok büyük.");
                } while (!pipe.IsMessageComplete);
                return Encoding.UTF8.GetString(data.ToArray()).Trim('\0', '\r', '\n', ' ', '\uFEFF');
            }
        }
        public void Dispose() { pipe.Dispose(); }
    }
}}
