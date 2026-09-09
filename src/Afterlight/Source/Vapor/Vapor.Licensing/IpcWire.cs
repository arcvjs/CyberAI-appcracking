using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Vapor.Licensing;

public static class IpcWire
{
    public static async Task Write<T>(Stream stream,T message,CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message,VaporProtocol.Json);
        if(bytes.Length > 65536) throw new InvalidDataException("IPC message too large");
        byte[] header = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(header,bytes.Length);
        await stream.WriteAsync(header,token); await stream.WriteAsync(bytes,token); await stream.FlushAsync(token);
    }
    public static async Task<T> Read<T>(Stream stream,CancellationToken token)
    {
        byte[] header = new byte[4]; await stream.ReadExactlyAsync(header,token);
        int length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if(length is < 1 or > 65536) throw new InvalidDataException("Invalid IPC length");
        byte[] bytes = new byte[length]; await stream.ReadExactlyAsync(bytes,token);
        return JsonSerializer.Deserialize<T>(bytes,VaporProtocol.Json) ?? throw new InvalidDataException("Empty IPC response");
    }
    [DllImport("kernel32.dll",EntryPoint="GetNamedPipeClientProcessId",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] public static extern bool GetClientPid(SafePipeHandle pipe,out uint pid);
    [DllImport("kernel32.dll",EntryPoint="GetNamedPipeServerProcessId",SetLastError=true)]
    [return:MarshalAs(UnmanagedType.Bool)] public static extern bool GetServerPid(SafePipeHandle pipe,out uint pid);
}
