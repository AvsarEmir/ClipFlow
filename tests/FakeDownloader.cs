using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
class FakeDownloader {
    static void Main(string[] args) {
        if (args.Contains("--child")) { Thread.Sleep(60000); return; }
        // This must see EOF. A downloader must never inherit the native protocol pipe.
        Console.In.ReadToEnd();
        string root = AppDomain.CurrentDomain.BaseDirectory;
        File.WriteAllText(Path.Combine(root,"last-args.json"),new JavaScriptSerializer().Serialize(args));
        if (args.Contains("--dump-single-json")) {
            Console.WriteLine("{\"title\":\"Test video\",\"channel\":\"Akis\",\"duration\":5,\"formats\":[{\"format_id\":\"299\",\"height\":1080,\"fps\":60,\"vcodec\":\"avc1.64002a\",\"acodec\":\"none\"},{\"format_id\":\"22\",\"height\":720,\"fps\":30,\"vcodec\":\"avc1.64001f\",\"acodec\":\"mp4a.40.2\"},{\"format_id\":\"999\",\"height\":2160,\"vcodec\":\"vp9\",\"has_drm\":true}]}");
            return;
        }
        if (File.Exists(Path.Combine(root,"finish-test.txt"))) {
            string home=args.First(s=>s.StartsWith("home:")).Substring(5);
            Directory.CreateDirectory(home);
            string file=Path.Combine(home,"Consent test."+(args.Contains("--extract-audio")?"mp3":"mp4"));
            File.WriteAllBytes(file,new byte[]{1,2,3});
            Console.WriteLine("AKIS_FILE:"+new JavaScriptSerializer().Serialize(file));
            return;
        }
        var child=Process.Start(new ProcessStartInfo(System.Reflection.Assembly.GetExecutingAssembly().Location,"--child"){UseShellExecute=false,CreateNoWindow=true});
        File.WriteAllText(Path.Combine(root,"child-pid.txt"),child.Id.ToString());
        Console.WriteLine("AKIS_PROGRESS:12.5%|2.3MiB/s|00:05");
        Console.Out.Flush();
        Thread.Sleep(60000);
    }
}
