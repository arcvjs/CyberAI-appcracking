using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using Vapor.Licensing;

namespace Vapor.Client;

public sealed class PipeServer : IDisposable
{
    readonly CancellationTokenSource cancel = new();
    readonly BackendClient backend;
    readonly Func<CachedSession> session;
    readonly Action save;
    readonly ConcurrentDictionary<string,TaskCompletionSource<int>> launches = new();
    Task? loop;
    public PipeServer(BackendClient backend,Func<CachedSession> session,Action save) { this.backend=backend;this.session=session;this.save=save; }
    public void Start() { if(loop == null) loop=AcceptLoop(); }
    public Process Launch(string executable,params string[] arguments)
    {
        string secret=VaporProtocol.NewSecret(); var registration=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        launches[secret]=registration;
        var info=new ProcessStartInfo(executable){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(executable)!};
        info.Environment["VAPOR_LAUNCH_SECRET"]=secret;info.Environment["VAPOR_CLIENT_PID"]=Environment.ProcessId.ToString();
        foreach(var argument in arguments) info.ArgumentList.Add(argument);
        try
        {
            var process=Process.Start(info)??throw new IOException("The game did not start.");
            registration.SetResult(process.Id);
            process.EnableRaisingEvents=true;process.Exited+=(_,_)=>launches.TryRemove(secret,out _);
            return process;
        }
        catch { launches.TryRemove(secret,out _);registration.TrySetCanceled();throw; }
    }
    async Task AcceptLoop()
    {
        while(!cancel.IsCancellationRequested)
        {
            try
            {
                using var pipe=new NamedPipeServerStream("vaporworks-ipc",PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancel.Token);
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancel.Token);timeout.CancelAfter(10000);
                var request=await IpcWire.Read<IpcRequest>(pipe,timeout.Token);
                if(request.Op!="app-ticket"||request.Nonce.Length!=64||!launches.TryGetValue(request.LaunchSecret,out var registration))continue;
                int pid=await registration.Task.WaitAsync(timeout.Token);
                if(request.ProcessId!=pid||!IpcWire.GetClientPid(pipe.SafePipeHandle,out uint actual)||actual!=pid)continue;
                var reply=await HandleTicket(request);
                reply=reply with{Nonce=request.Nonce,ProcessId=pid,AccountId=session().SteamId};
                byte[] payload=JsonSerializer.SerializeToUtf8Bytes(reply,VaporProtocol.Json);
                await IpcWire.Write(pipe,new IpcEnvelope(Convert.ToBase64String(payload),Convert.ToBase64String(HMACSHA256.HashData(Convert.FromHexString(request.LaunchSecret),payload))),timeout.Token);
            }
            catch(OperationCanceledException){if(cancel.IsCancellationRequested)break;}
            catch(Exception e) when(e is IOException or JsonException or ArgumentException or System.ComponentModel.Win32Exception){if(cancel.IsCancellationRequested)break;await Task.Delay(100);}
        }
    }
    async Task<IpcResponse> HandleTicket(IpcRequest request)
    {
        var current=session();
        if(request.AppId!=VaporProtocol.AfterlightAppId||request.DeviceId!=backend.DeviceId)return new(false,"wrong_device","This request belongs to a different app or device.");
        if(string.IsNullOrEmpty(current.SessionToken))return new(false,"not_signed_in","Sign in to Vapor to continue.");
        try
        {
            var response=await backend.GetAppTicketAsync(current.SessionToken,request.AppId);
            current.LastTicket=response.Ticket;current.LastProof=response.OwnershipProof;current.LastSeen=DateTimeOffset.UtcNow;save();
            return new(true,"ok","Ownership verified.",OwnershipProof:response.OwnershipProof);
        }
        catch(VaporException e)
        {
            if(e.Code is "trial_ended" or "not_owned" or "revoked" or "invalid_session") {current.LastProof=null;current.LastTicket=null;save();}
            return new(false,e.Code,e.Message);
        }
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException)
        {
            if(current.LastSeen>DateTimeOffset.UtcNow.AddMinutes(5))return new(false,"clock_changed","This PC's clock changed. Restore its date and time.");
            if(current.LastProof!=null)return new(true,"ok","Cached offline ownership.",OwnershipProof:current.LastProof);
            return new(false,"server_unavailable","No offline license is available. Open Vapor and sign in first.");
        }
    }
    public void Dispose(){cancel.Cancel();}
}
