using System.Diagnostics;
using System.IO;

namespace Vapor.Client;

public static class LocalService
{
    static Process? owned;
    public static bool Exhibition => File.Exists(Path.Combine(AppContext.BaseDirectory,"exhibition.json")) || Environment.GetCommandLineArgs().Contains("--exhibition");
    public static string DataDirectory => Vapor.Licensing.ExhibitionState.DirectoryPath;
    public static async Task InitializeAsync()
    {
        if (!Vapor.Licensing.ExhibitionState.Enabled) return;
        string executable = Path.Combine(AppContext.BaseDirectory, "Service", "Vapor.Backend.exe");
        if (!File.Exists(executable)) throw new IOException("Missing Service/Vapor.Backend.exe. Extract the entire Afterlight folder before launching.");
        Directory.CreateDirectory(DataDirectory);
        var info = new ProcessStartInfo(executable) { UseShellExecute=false, CreateNoWindow=true, WorkingDirectory=DataDirectory };
        info.ArgumentList.Add("--initialize");
        info.Environment["VAPOR_DATA"]=DataDirectory;
        info.Environment["VAPOR_URL"]="http://127.0.0.1:47838";
        using var process = Process.Start(info) ?? throw new IOException("Could not start the local Vapor service.");
        try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch (TimeoutException) { process.Kill(true); throw new IOException("Local Vapor setup timed out. Restart Afterlight."); }
        if(process.ExitCode!=0) throw new IOException("Local Vapor setup failed. Check that the Afterlight folder was fully extracted and the service was not blocked.");
    }
    public static async Task<bool> EnsureRunningAsync(BackendClient backend)
    {
        if(await backend.CheckHealthAsync())return true;
        if(!Exhibition||!backend.Trust.ReferenceServer||!new Uri(backend.Trust.ServerUrl).IsLoopback)return false;
        string executable=Path.Combine(AppContext.BaseDirectory,"Service","Vapor.Backend.exe");
        if(!File.Exists(executable))return false;
        var info=new ProcessStartInfo(executable){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=DataDirectory};
        info.Environment["VAPOR_DATA"]=DataDirectory;info.Environment["VAPOR_URL"]=backend.Trust.ServerUrl;
        owned=Process.Start(info);
        for(int i=0;i<25;i++){await Task.Delay(150);if(await backend.CheckHealthAsync())return true;if(owned?.HasExited==true)break;}
        return false;
    }
    public static void StopOwned(){try{if(owned is {HasExited:false})owned.Kill();owned?.Dispose();}catch{} }
}
