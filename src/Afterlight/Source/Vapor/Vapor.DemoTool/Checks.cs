using System.Diagnostics;
using System.IO;
namespace AfterlightDemo;
public static class Checks
{
    public static int Run(string source,string report)
    {
        string root=Path.Combine(Path.GetTempPath(),"afterlight-sdk-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var results=new List<string>();Process? game=null;
        try
        {
            foreach(string file in Directory.GetFiles(source))File.Copy(file,Path.Combine(root,Path.GetFileName(file)));
            var control=new Controller(root);string original=Controller.HashFile(control.Sdk);string gameHash=Controller.HashFile(Path.Combine(root,"Afterlight.dll"));
            if(control.State()!="Protected")throw new Exception("Expected original protected SDK.");
            control.Apply();control.Apply();
            if(control.State()!="Replacement active"||Controller.HashFile(control.Backup)!=original)throw new Exception("Apply or backup failed.");
            results.Add("PASS Apply, verified backup, and repeated Apply.");
            game=control.Launch();
            bool opened=false;
            for(int i=0;i<100;i++){Thread.Sleep(100);if(game.HasExited)throw new Exception("Game exited before opening.");game.Refresh();if(game.MainWindowTitle.StartsWith("AFTERLIGHT |")){opened=true;break;}}
            if(!opened)throw new Exception("Native game window did not appear.");
            try{control.Restore();throw new Exception("Restore allowed while game running.");}catch(IOException){}
            results.Add("PASS Restore blocked while game running.");
            Thread.Sleep(23000);game.Refresh();if(game.HasExited||!game.MainWindowTitle.StartsWith("AFTERLIGHT |"))throw new Exception("Game failed SDK renewal.");
            results.Add("PASS Unchanged native game starts without launch context and survives a 20-second renewal.");
            game.CloseMainWindow();if(!game.WaitForExit(5000))game.Kill(true);game.WaitForExit();game.Dispose();game=null;
            byte[] backup=File.ReadAllBytes(control.Backup);File.WriteAllText(control.Backup,"damaged");
            try{control.Restore();throw new Exception("Damaged backup accepted.");}catch(IOException){}
            if(control.State()!="Replacement active")throw new Exception("Failed restore changed SDK.");
            File.WriteAllBytes(control.Backup,backup);results.Add("PASS Damaged backup rejected without changing active SDK.");
            control.Restore();control.Restore();if(Controller.HashFile(control.Sdk)!=original)throw new Exception("Restore changed original bytes.");
            results.Add("PASS Restore is byte-exact and repeatable.");
            if(Controller.HashFile(Path.Combine(root,"Afterlight.dll"))!=gameHash)throw new Exception("Game code changed.");
            results.Add("PASS Game assembly unchanged throughout demonstration.");
            File.WriteAllText(control.Sdk,"unknown");try{control.Apply();throw new Exception("Unknown SDK accepted.");}catch(IOException){}
            results.Add("PASS Unknown SDK rejected.");
            File.WriteAllLines(report,results);return 0;
        }
        catch(Exception error){results.Add("FAIL "+error);File.WriteAllLines(report,results);return 1;}
        finally{if(game!=null){if(!game.HasExited){game.Kill(true);game.WaitForExit();}game.Dispose();}Directory.Delete(root,true);}
    }
}
