using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
namespace AfterlightDemo;
public sealed record Manifest(string Game,string Original,string Replacement);
public sealed class Controller
{
    public string Root {get;}
    public string Sdk => Path.Combine(Root,"Vaporworks.dll");
    public string Backup => Path.Combine(Root,".sdk-backup","Vaporworks.dll");
    readonly Manifest manifest;
    readonly byte[] replacement;
    public Controller(string root)
    {
        Root=Path.GetFullPath(root);
        using var m=Assembly.GetExecutingAssembly().GetManifestResourceStream("Manifest.json")!;
        manifest=JsonSerializer.Deserialize<Manifest>(m)!;
        using var p=Assembly.GetExecutingAssembly().GetManifestResourceStream("Replacement.dll")!;
        using var bytes=new MemoryStream();p.CopyTo(bytes);replacement=bytes.ToArray();
        if(Hash(replacement)!=manifest.Replacement)throw new IOException("Replacement payload is damaged.");
    }
    public static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    public static string HashFile(string path)=>Hash(File.ReadAllBytes(path));
    public string State()
    {
        if(!File.Exists(Path.Combine(Root,"Afterlight.dll")) || HashFile(Path.Combine(Root,"Afterlight.dll"))!=manifest.Game)
            throw new IOException("This tool supports only its matching Afterlight build. Rebuild the demo tool after updating the game.");
        var hash=HashFile(Sdk);
        return hash==manifest.Original?"Protected":hash==manifest.Replacement?"Replacement active":throw new IOException("Unknown SDK. No files were changed.");
    }
    void CheckClosed()
    {
        foreach(string name in new[]{"Afterlight","Vapor"})foreach(var process in Process.GetProcessesByName(name))using(process)
        {
            try {if(string.Equals(Path.GetDirectoryName(process.MainModule?.FileName),Root,StringComparison.OrdinalIgnoreCase))throw new IOException("Close Afterlight and Vapor before changing protection.");}
            catch(System.ComponentModel.Win32Exception){throw new IOException("Close Afterlight and Vapor before changing protection.");}
        }
    }
    void WriteAtomic(byte[] bytes)
    {
        string temp=Path.Combine(Root,".sdk-"+Guid.NewGuid().ToString("N")+".tmp");
        try{File.WriteAllBytes(temp,bytes);File.Move(temp,Sdk,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public void Apply()
    {
        CheckClosed(); if(State()=="Replacement active")return;
        Directory.CreateDirectory(Path.GetDirectoryName(Backup)!);
        if(File.Exists(Backup)){if(HashFile(Backup)!=manifest.Original)throw new IOException("Backup is damaged. Original SDK kept intact.");}
        else File.Copy(Sdk,Backup,false);
        if(HashFile(Backup)!=manifest.Original)throw new IOException("Backup verification failed.");
        WriteAtomic(replacement);
    }
    public void Restore()
    {
        CheckClosed();if(State()=="Protected")return;
        if(!File.Exists(Backup)||HashFile(Backup)!=manifest.Original)throw new IOException("A verified original backup is required. Replacement left intact.");
        WriteAtomic(File.ReadAllBytes(Backup));
    }
    public Process Launch()
    {
        State();var start=new ProcessStartInfo(Path.Combine(Root,"Afterlight.exe")){UseShellExecute=false,WorkingDirectory=Root};
        start.Environment.Remove("VAPOR_LAUNCH_SECRET");start.Environment.Remove("VAPOR_CLIENT_PID");
        return Process.Start(start)!;
    }
}
