using System.Diagnostics;
using System.IO;

namespace Vapor.Client;

public static class LocalService
{
    static Process? owned;
    public static bool Exhibition => File.Exists(Path.Combine(AppContext.BaseDirectory,"exhibition.json")) || Environment.GetCommandLineArgs().Contains("--exhibition");
    static string issuerId="default";
    public static string DataDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Vapor","Exhibition",issuerId,"service-data");
    public static async Task<bool> EnsureRunningAsync(BackendClient backend)
    {
        issuerId=Vapor.Licensing.VaporProtocol.Hash(backend.Trust.PublicKey)[..16];
        if(await backend.CheckHealthAsync())return true;
        if(!Exhibition||!backend.Trust.ReferenceServer||!new Uri(backend.Trust.ServerUrl).IsLoopback)return false;
        string executable=Path.Combine(AppContext.BaseDirectory,"Service","Vapor.Backend.exe");
        string seed=Path.Combine(AppContext.BaseDirectory,"Presenter","seed-data");
        if(!File.Exists(executable)||!Directory.Exists(seed))return false;
        Directory.CreateDirectory(DataDirectory);
        foreach(string source in Directory.EnumerateFiles(seed))
        {string destination=Path.Combine(DataDirectory,Path.GetFileName(source));if(!File.Exists(destination))File.Copy(source,destination);}
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=DataDirectory};
        info.Environment["VAPOR_DATA"]=DataDirectory;info.Environment["VAPOR_URL"]=backend.Trust.ServerUrl;
        owned=Process.Start(info);
        for(int i=0;i<25;i++){await Task.Delay(150);if(await backend.CheckHealthAsync())return true;if(owned?.HasExited==true)break;}
        return false;
    }
    public static void StopOwned(){try{if(owned is {HasExited:false})owned.Kill();owned?.Dispose();}catch{} }
}
