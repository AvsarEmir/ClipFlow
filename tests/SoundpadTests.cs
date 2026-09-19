using System;
using System.IO;
using System.Collections.Generic;
using System.Security;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Text;
using Akis;
class SoundpadTests {
    static void Check(bool v, string message) { if (!v) throw new Exception(message); }
    static void Throws(Action action) { bool threw=false; try { action(); } catch { threw=true; } Check(threw,"Expected rejected response"); }
    static void Main() {
        string pipeName="akis-test-"+Guid.NewGuid().ToString("N");
        string fileCommand="DoAddSound(\"C:\\Downloads\\ÇUNKU DRAG DEDEM [fQNtUSaR0mM] - 320kbps.mp3\",7,-1)";
        var encoding=Encoding.GetEncoding(Encoding.Default.CodePage,EncoderFallback.ExceptionFallback,DecoderFallback.ExceptionFallback);
        using(var server=new NamedPipeServerStream(pipeName,PipeDirection.InOut,1,PipeTransmissionMode.Message,PipeOptions.Asynchronous)) {
            Exception serverError=null;
            string payload="İndirilenler — "+new string('a',40000);
            var thread=new Thread(delegate(){try{server.WaitForConnection();byte[] b=new byte[4096];for(int n=0;n<2;n++){int read=server.Read(b,0,b.Length);byte[] expected=encoding.GetBytes(n==0?fileCommand:"GetCategories(false,false)");Check(read==expected.Length,"Wrong request byte count/code page");for(int i=0;i<read;i++)Check(b[i]==expected[i],"Request is not Windows ANSI");byte[] response=Encoding.UTF8.GetBytes(n==0?payload:"R-200");server.Write(response,0,response.Length);server.Flush();}}catch(Exception e){serverError=e;}});
            thread.IsBackground=true;thread.Start();
            var type=typeof(Soundpad).GetNestedType("RemotePipe",BindingFlags.NonPublic);
            using(var client=(IDisposable)Activator.CreateInstance(type,new object[]{pipeName})) {
                var request=type.GetMethod("Request");
                if (Encoding.Default.CodePage==1254 || Encoding.Default.CodePage==1252) {
                    Throws(()=>request.Invoke(client,new object[]{"DoAddSound(\"C:\\Downloads\\\U0001F600.mp3\",7,-1)"}));
                }
                Check((string)request.Invoke(client,new object[]{fileCommand})==payload,"UTF-8/multibuffer response corrupt");
                Check((string)request.Invoke(client,new object[]{"GetCategories(false,false)"})=="R-200","Second request failed");
            }
            Check(thread.Join(5000) && serverError==null,"Pipe server failed: "+serverError);
        }
        Console.WriteLine("PASS Named-pipe request uses Windows ANSI for Turkish filename; response remains UTF-8; unrepresentable path rejected without sending");
        string file=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Türkçe ses (test), örnek.mp3");
        File.WriteAllBytes(file,new byte[]{1,2,3});
        string cat="<Categories><Category index='2' name='Other'/><Category index='7' name='İndirilenler'/></Categories>";
        string empty="<Categories><Category index='7' name='İndirilenler'/></Categories>";
        var calls=new List<string>();
        try {
            var result=Soundpad.Import(file, new[]{"İndirilenler"},delegate(string cmd){calls.Add(cmd);if(cmd=="GetCategories(false,false)")return cat;if(cmd=="GetCategory(7,true,false)")return empty;return "R-200";});
            Check(result.Status=="added" && calls.Count==3 && calls[2]=="DoAddSound(\""+file+"\",7,-1)","Wrong category/encoding/add command");
            Console.WriteLine("PASS Turkish existing category and exact quoted file added to its index");
            calls.Clear();
            string duplicate="<Categories><Category index='7' name='İndirilenler'><Sound url='"+SecurityElement.Escape(file)+"'/></Category></Categories>";
            result=Soundpad.Import(file, new[]{"İndirilenler"},delegate(string cmd){calls.Add(cmd);return cmd.StartsWith("GetCategories")?cat:duplicate;});
            Check(result.Status=="exists" && calls.Count==2,"Duplicate caused mutation");
            Console.WriteLine("PASS Same file in target category is not added again");
            calls.Clear();
            string tree="<Categories><Category index='1' type='1' name='All'/><Category index='2' name='Oyun'><Category index='9' name='Efektler'/></Category><Category index='3' name='Müzik'><Category index='11' name='Efektler'/></Category></Categories>";
            var categories=Soundpad.ListCategories(cmd=>tree);
            Check(categories.Count==4 && categories[1].Label=="Oyun / Efektler" && categories[1].Selectable,"Category tree/virtual category filtering failed");
            result=Soundpad.Import(file,new[]{"Oyun","Efektler"},delegate(string cmd){calls.Add(cmd);return cmd.StartsWith("GetCategories")?tree:cmd.StartsWith("GetCategory(")?"<Categories><Category index='9' name='Efektler'/></Categories>":"R-200";});
            Check(result.Status=="added" && result.Message.Contains("Oyun / Efektler") && calls[2].EndsWith(",9,-1)"),"Selected nested category not used");
            calls.Clear();
            result=Soundpad.Import(file,new[]{"Oyun","Efektler"},delegate(string cmd){calls.Add(cmd);return cmd.StartsWith("GetCategories")?tree.Replace("index='9'","index='21'"):cmd.StartsWith("GetCategory(")?"<Categories><Category index='21' name='Efektler'/></Categories>":"R-200";});
            Check(result.Status=="added" && calls[2].EndsWith(",21,-1)"),"Reordered category not resolved from fresh tree");
            calls.Clear();
            Throws(()=>Soundpad.Import(file,new[]{"Silinen"},delegate(string cmd){calls.Add(cmd);return tree;}));
            Check(calls.Count==1,"Deleted selection caused fallback or mutation");
            string ambiguous="<Categories><Category index='7' name='İndirilenler'/><Category index='8' name='İndirilenler'/></Categories>";
            Check(Soundpad.ListCategories(cmd=>ambiguous).TrueForAll(c=>!c.Selectable),"Ambiguous categories selectable");
            calls.Clear();
            Throws(()=>Soundpad.Import(file,new[]{"İndirilenler"},delegate(string cmd){calls.Add(cmd);return ambiguous;}));
            Check(calls.Count==1,"Ambiguous category caused mutation");
            Console.WriteLine("PASS Category listing, nested target, reordered index, removed category and ambiguous names");
            Throws(()=>Soundpad.Import(file, new[]{"İndirilenler"},cmd=>"R-403: Trial version"));
            Throws(()=>Soundpad.Import(file, new[]{"İndirilenler"},cmd=>"R-404: Unknown command"));
            try { Soundpad.Import(file, new[]{"İndirilenler"},cmd=>cmd.StartsWith("GetCategories")?cat:cmd.StartsWith("GetCategory(")?empty:"R-204: File does not exist."); throw new Exception("Missing error"); }
            catch(Exception e) { Check(e.Message.Contains("Dosya indirildi"),"File access error must distinguish saved download from failed import"); }
            Throws(()=>Soundpad.Import(file, new[]{"İndirilenler"},cmd=>"<!DOCTYPE x [<!ENTITY e SYSTEM 'file:///C:/Windows/win.ini'>]><Categories>&e;</Categories>"));
            Throws(()=>Soundpad.Import(file, new[]{"İndirilenler"},cmd=>cmd.StartsWith("GetCategories")?cat:"<Categories><Category index='7' name='Different'/></Categories>"));
            Console.WriteLine("PASS Trial, unsupported command, XML entity and changed/ambiguous category rejected");
        } finally { File.Delete(file); }
    }
}
