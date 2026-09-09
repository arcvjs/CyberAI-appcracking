using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using Vapor.Licensing;

namespace Vaporworks;

public sealed record VaporSession(bool Ok,string Code,string Message,TicketClaims? Ticket)
{
    readonly DateTimeOffset acceptedAt = DateTimeOffset.UtcNow;
    readonly long startedAt = Stopwatch.GetTimestamp();
    public DateTimeOffset EffectiveNow => acceptedAt + Stopwatch.GetElapsedTime(startedAt);
    public bool TicketStillValid => Ok && Ticket != null && EffectiveNow < Ticket.ExpiresAt &&
        (Ticket.License != "trial" || EffectiveNow < Ticket.TrialEndsAt);
}

/// <summary>Local exhibition SDK: launcher bootstrap, authenticated IPC, then independent
/// publisher signature verification. This does not implement Steam's ABI.</summary>
public static class VaporAPI
{
    static WorksTrust? trust;
    static WorksTrust Trust
    {
        get
        {
            if(trust != null) return trust;
            using var stream = typeof(VaporAPI).Assembly.GetManifestResourceStream("Vaporworks.Trust.json")!;
            return trust = JsonSerializer.Deserialize<WorksTrust>(stream,VaporProtocol.Json)!;
        }
    }
    public static string? LauncherPath => new[] {
        Path.Combine(AppContext.BaseDirectory,"Vapor.exe"),
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","Vapor.exe"))
    }.FirstOrDefault(File.Exists);
    public static bool OpenLauncher(bool play = false)
    {
        if(LauncherPath is not string path) return false;
        try
        {
            var info = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(path)! };
            info.ArgumentList.Add(play ? "--launch" : "--library");
            foreach(var key in new[]{"VAPOR_LAUNCH_SECRET","VAPOR_CLIENT_PID"}) info.Environment.Remove(key);
            Process.Start(info)?.Dispose(); return true;
        }
        catch { return false; }
    }
    public static bool RestartAppIfNecessary() => string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAPOR_LAUNCH_SECRET")) && OpenLauncher();
    public static VaporSession Init() => InitAsync().GetAwaiter().GetResult();
    public static async Task<VaporSession> InitAsync()
    {
        try
        {
            string secret = Environment.GetEnvironmentVariable("VAPOR_LAUNCH_SECRET") ?? "";
            if(secret.Length != 64 || !int.TryParse(Environment.GetEnvironmentVariable("VAPOR_CLIENT_PID"),out int clientPid)) return Failure("no_client","Open Afterlight from your Vapor library to play.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var pipe = new NamedPipeClientStream(".",Trust.PipeName,PipeDirection.InOut,PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(timeout.Token);
            if(!IpcWire.GetServerPid(pipe.SafePipeHandle,out uint actualPid) || actualPid != clientPid) return Failure("bad_session","The launcher identity could not be verified.");
            string nonce = VaporProtocol.NewSecret(); string device = VaporProtocol.LocalDeviceId();
            await IpcWire.Write(pipe,new IpcRequest("app-ticket",Trust.AppId,device,nonce,Environment.ProcessId,secret),timeout.Token);
            var envelope = await IpcWire.Read<IpcEnvelope>(pipe,timeout.Token);
            byte[] payload = Convert.FromBase64String(envelope.Payload);
            if(!CryptographicOperations.FixedTimeEquals(HMACSHA256.HashData(Convert.FromHexString(secret),payload),Convert.FromBase64String(envelope.Mac))) return Failure("bad_session","The launcher session proof was invalid.");
            var reply = JsonSerializer.Deserialize<IpcResponse>(payload,VaporProtocol.Json);
            if(reply == null || reply.Nonce != nonce || reply.ProcessId != Environment.ProcessId) return Failure("bad_session","The launcher returned a stale or unrelated session.");
            if(!reply.Ok) return Failure(reply.Code,reply.Message);
            if(reply.OwnershipProof == null) return Failure("bad_ticket","The launcher did not provide a signed ownership ticket.");
            var claims = VaporProtocol.Verify(reply.OwnershipProof,Trust.SteamPublicKey,Trust.AppId,device,DateTimeOffset.UtcNow);
            if(claims.SteamId != reply.AccountId) return Failure("wrong_account","This ticket belongs to a different account.");
            return new(true,"ok","Ownership verified.",claims);
        }
        catch(VaporException e) { return Failure(e.Code,e.Message); }
        catch(Exception e) when(e is IOException or OperationCanceledException or InvalidOperationException or FormatException or JsonException or CryptographicException)
        { return Failure("no_client","Vapor could not verify this session. Reopen the launcher and try again."); }
    }
    static VaporSession Failure(string code,string message) => new(false,code,message,null);
    public static bool TicketStillValid(VaporSession session) => session.TicketStillValid;
}
