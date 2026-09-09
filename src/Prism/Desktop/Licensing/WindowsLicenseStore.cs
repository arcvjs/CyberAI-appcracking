using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Prism.Licensing;
namespace Prism.Licensing.Client;

public sealed class CachedActivation
{
    public SignedLicense? License {get;set;}
    public LicenseClaims? SimpleClaims {get;set;}
    public string RefreshToken {get;set;}="";
    public DateTimeOffset LastSeen {get;set;}
    public string? BlockedCode {get;set;}
    public string? BlockedMessage {get;set;}
}
public sealed class WindowsLicenseStore
{
    private readonly string _path;
    public WindowsLicenseStore(string path)=>_path=path;
    public CachedActivation? Load()
    {
        if(!System.IO.File.Exists(_path))return null;
        return JsonSerializer.Deserialize<CachedActivation>(Unprotect(System.IO.File.ReadAllBytes(_path)),LicenseProtocol.Json)??throw new InvalidDataException("Invalid activation cache.");
    }
    public void Save(CachedActivation activation)
    {
        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(_path)!);
        var bytes=Protect(JsonSerializer.SerializeToUtf8Bytes(activation,LicenseProtocol.Json));
        string temporary=_path+"."+Guid.NewGuid().ToString("N")+".tmp";
        using(var file=new System.IO.FileStream(temporary,System.IO.FileMode.CreateNew,System.IO.FileAccess.Write,System.IO.FileShare.None)){file.Write(bytes);file.Flush(true);}
        System.IO.File.Move(temporary,_path,true);
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
        try{Marshal.Copy(data,0,input.Data,data.Length);bool ok=protect?CryptProtectData(ref input,"Prism activation",IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);if(!ok)throw new Win32Exception(Marshal.GetLastWin32Error());var result=new byte[output.Size];Marshal.Copy(output.Data,result,0,result.Length);return result;}
        finally{Marshal.FreeHGlobal(input.Data);if(output.Data!=IntPtr.Zero)LocalFree(output.Data);}
    }
}
