using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Vapor.Licensing;

namespace Vapor.Client;

/// <summary>What the client keeps on this machine — Steam's "remember password" plus the last
/// app ticket for offline play, sealed with DPAPI like the Windows credential store.</summary>
public sealed class CachedSession
{
    public string Account {get;set;}="";
    public string Password {get;set;}="";
    public string SteamId {get;set;}="";
    public string Nickname {get;set;}="";
    public string SessionToken {get;set;}="";
    public EncryptedAppTicket? LastTicket {get;set;}
    public SignedTicket? LastProof {get;set;}
    public DateTimeOffset LastSeen {get;set;}
}
public sealed class VaporStore
{
    private readonly string _path;
    public VaporStore(string path)=>_path=path;
    public CachedSession? Load()
    {
        if(!File.Exists(_path))return null;
        return JsonSerializer.Deserialize<CachedSession>(Unprotect(File.ReadAllBytes(_path)),VaporProtocol.Json)??throw new InvalidDataException("Invalid session cache.");
    }
    public void Save(CachedSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var bytes=Protect(JsonSerializer.SerializeToUtf8Bytes(session,VaporProtocol.Json));
        string temporary=_path+"."+Guid.NewGuid().ToString("N")+".tmp";
        using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}
        File.Move(temporary,_path,true);
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob{public int Size;public IntPtr Data;}
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
    public static byte[] Protect(byte[] data)=>Transform(data,true);
    public static byte[] Unprotect(byte[] data)=>Transform(data,false);
    private static byte[] Transform(byte[] data,bool protect)
    {
        var input=new Blob{Size=data.Length,Data=Marshal.AllocHGlobal(data.Length)};Blob output=default;
        try{Marshal.Copy(data,0,input.Data,data.Length);bool ok=protect?CryptProtectData(ref input,"Vapor session",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);if(!ok)throw new Win32Exception(Marshal.GetLastWin32Error());var result=new byte[output.Size];Marshal.Copy(output.Data,result,0,output.Size);return result;}
        finally{Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero)LocalFree(output.Data);}
    }
}
