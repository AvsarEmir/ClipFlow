using System;
using System.IO;
using System.Collections.Generic;
namespace Akis {
public class SoundpadCategory {
    public int Index = 9;
    public string Name = "Efektler", Label = "Oyun / Efektler";
    public string[] Path = new[] {"Oyun", "Efektler"};
    public bool Selectable = true;
}
public sealed class SoundpadResult {
    public string Status, Message;
    public SoundpadResult(string status,string message){Status=status;Message=message;}
}
public static class Soundpad {
    public static bool Enabled(bool selected,string mode){return selected && mode=="mp3";}
    public static List<SoundpadCategory> ListCategories(){return new List<SoundpadCategory>{new SoundpadCategory()};}
    public static SoundpadResult Import(string file,string[] categoryPath){
        string root=AppDomain.CurrentDomain.BaseDirectory;
        File.AppendAllText(Path.Combine(root,"import-calls.txt"),file+"|"+string.Join(" / ",categoryPath)+Environment.NewLine);
        if(File.Exists(Path.Combine(root,"import-error.txt")))throw new Exception("Soundpad is offline");
        return new SoundpadResult("added","Added to "+string.Join(" / ",categoryPath));
    }
}}
