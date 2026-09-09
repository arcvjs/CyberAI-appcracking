using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Vapor.Licensing;

namespace Vapor.Client;

/// <summary>Talks to the Vapor backend: sign-in, ownership, purchase, and app tickets.</summary>
public sealed class BackendClient : IDisposable
{
    public BackendTrust Trust {get;}
    public string DeviceId=>VaporProtocol.LocalDeviceId();
    public string DeviceName=>Environment.MachineName;
    public bool Online=>_online;
    private readonly HttpClient _http;
    private bool _online=true;
    public BackendClient()
    {
        using var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("Vapor.ClientTrust.json")??throw new InvalidOperationException("This build has no backend trust configuration.");
        Trust=JsonSerializer.Deserialize<BackendTrust>(stream,VaporProtocol.Json)??throw new InvalidDataException("Invalid backend trust configuration.");
        if(!Uri.TryCreate(Trust.ServerUrl,UriKind.Absolute,out var endpoint)||(endpoint.Scheme!="https"&&!(endpoint.Scheme=="http"&&endpoint.IsLoopback)))throw new InvalidOperationException("The backend requires HTTPS; HTTP is supported only on loopback.");
        _http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(Trust.ServerUrl.TrimEnd('/')+"/"),Timeout=TimeSpan.FromSeconds(8)};
    }
    private async Task<T> SendAsync<T>(string path,object request)
    {
        using var response=await _http.PostAsJsonAsync(path,request,VaporProtocol.Json);
        if(!response.IsSuccessStatusCode){ApiError? error=null;try{error=await response.Content.ReadFromJsonAsync<ApiError>(VaporProtocol.Json);}catch(JsonException){}throw new VaporException(error?.Code??"server_unavailable",error?.Message??"The Vapor service is unavailable. Try again shortly.");}
        if(response.StatusCode==HttpStatusCode.NoContent)return default!;
        return await response.Content.ReadFromJsonAsync<T>(VaporProtocol.Json)??throw new VaporException("invalid_response","The Vapor service returned an empty response.");
    }
    private async Task<T> TryAsync<T>(string path,object request,Func<T> fallback)
    {
        try{var result=await SendAsync<T>(path,request);_online=true;return result;}
        catch(Exception error)when(error is HttpRequestException or TaskCanceledException or JsonException){_online=false;return fallback();}
    }
    public Task<LoginResponse> SignInAsync(string account,string password)=>SendAsync<LoginResponse>("api/v1/login",new LoginRequest(account,password,DeviceId,DeviceName));
    public Task<Ownership> GetOwnershipAsync(string sessionToken,int appId)=>TryAsync("api/v1/ownership",new OwnershipQuery(sessionToken,appId),()=>new Ownership(appId,"afterlight","unknown",default,false));
    public Task<Ownership> PurchaseAsync(string sessionToken,int appId)=>SendAsync<Ownership>("api/v1/purchase",new PurchaseRequest(sessionToken,appId));
    public Task<AppTicketResponse> GetAppTicketAsync(string sessionToken,int appId)=>SendAsync<AppTicketResponse>("api/v1/appticket",new AppTicketRequest(sessionToken,appId,DeviceId));
    public async Task<bool> CheckHealthAsync(){try{using var response=await _http.GetAsync("health");_online=response.IsSuccessStatusCode;return _online;}catch(Exception e)when(e is HttpRequestException or TaskCanceledException){_online=false;return false;}}
    public void Dispose()=>_http.Dispose();
}
