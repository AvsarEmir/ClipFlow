using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using Akis;

class FolderSettingsTests {
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static string ReadFolder() { return (string)typeof(Host).GetMethod("OutputFolder", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, null); }
    static void Main() {
        string root = AppDomain.CurrentDomain.BaseDirectory;
        string config = Path.Combine(root, "settings.json");
        byte[] old = File.Exists(config) ? File.ReadAllBytes(config) : null;
        string selected = Path.Combine(root, "Seçilen klasör"); Directory.CreateDirectory(selected);
        var json = new JavaScriptSerializer();
        try {
            File.WriteAllText(config, "{}");
            string defaultFolder = ReadFolder();
            Check(!defaultFolder.EndsWith("\\Akis"), "Default must be Downloads itself.");
            File.WriteAllText(config, "{\"extra\":\"keep\"}");
            Host.SaveFolder(selected);
            Check(ReadFolder() == selected, "Saved path was not restored.");
            Check(json.Deserialize<Dictionary<string, object>>(File.ReadAllText(config))["extra"].Equals("keep"), "Unrelated settings lost.");
            string before = File.ReadAllText(config);
            bool rejected = false;
            try { Host.SaveFolder("relative\\path"); } catch { rejected = true; }
            Check(rejected && File.ReadAllText(config) == before, "Invalid path changed the settings.");
            rejected = false;
            try { Host.SaveFolder(Path.Combine(root, Guid.NewGuid().ToString("N"))); } catch { rejected = true; }
            Check(rejected && File.ReadAllText(config) == before, "Missing directory changed settings.");
            Host.SaveFolder(null);
            Check(ReadFolder() == defaultFolder, "Default folder was not restored.");
            var settings = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(config));
            Check(!settings.ContainsKey("downloadDirectory") && settings["extra"].Equals("keep"), "Reset lost other settings.");
            File.WriteAllText(config, "broken json"); Check(ReadFolder() == defaultFolder, "Invalid config recovery failed.");
            Check(Directory.GetFiles(selected).Length == 0, "Write probe was not cleaned up.");
            Console.WriteLine("PASS Default Downloads, Unicode folder persistence, reset, invalid selection rollback, existing settings and probe cleanup");
        } finally { if (old == null) File.Delete(config); else File.WriteAllBytes(config, old); }
    }
}
