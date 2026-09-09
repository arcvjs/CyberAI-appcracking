using Microsoft.Win32;
using Prism.Licensing;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.Json;
namespace Prism.Licensing.Client;

public sealed record LicenseStatus(bool CanEdit,string Title,string Message,LicenseClaims? Claims=null,bool IsTrial=false);
public sealed record ServerSettings(string ServerUrl="http://70.153.24.7:47831");
public sealed record SimpleLicenseResponse(bool Valid,DateTimeOffset ExpiresAt,string Customer="Prism user",string? Message=null);
public sealed class LicenseClient : IDisposable
{
    public LicenseTrust Trust {get;}
    public string DeviceId {get;}
    public string DeviceName=>Environment.MachineName;
    public LicenseStatus Status {get;private set;}=new(false,"Activation required","Activate Prism to start editing.");
    public static string ConfigPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prism","Config","licensing.json");
    private readonly WindowsLicenseStore _store;
    private readonly TrialStore _trial=new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prism","Config","trial.json"));
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate=new(1,1);
    private CachedActivation? _cache;
    private DateTimeOffset _nextAttempt;
    private bool _offline;
    public LicenseClient()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        if(!File.Exists(ConfigPath))File.WriteAllText(ConfigPath,JsonSerializer.Serialize(new ServerSettings(),new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
        var settings=JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(ConfigPath),LicenseProtocol.Json)??new();
        if(!Uri.TryCreate(settings.ServerUrl,UriKind.Absolute,out var endpoint)||endpoint.Scheme is not ("http" or "https")||!string.IsNullOrEmpty(endpoint.UserInfo)||!string.IsNullOrEmpty(endpoint.Query)||!string.IsNullOrEmpty(endpoint.Fragment))throw new InvalidDataException("Use an HTTP or HTTPS server URL in "+ConfigPath);
        var url=endpoint.AbsoluteUri.TrimEnd('/');
        Trust=new(url,"",false);
        DeviceId=GetDeviceId();
        _store=new WindowsLicenseStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Prism","Licensing","simple-"+LicenseProtocol.Hash(url)[..16],"activation.bin"));
        _http=new HttpClient(new HttpClientHandler{AllowAutoRedirect=false}){BaseAddress=new Uri(url+"/"),Timeout=TimeSpan.FromSeconds(8)};
        try{_cache=_store.Load();Evaluate();}catch(Exception ex)when(ex is IOException or JsonException or System.ComponentModel.Win32Exception){Status=new(false,"Activation cache unavailable","Activate again to repair the local license cache.");}
    }
    private static string GetDeviceId()
    {
        using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);
        using var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        return LicenseProtocol.Hash("prism-device-v1|"+(key?.GetValue("MachineGuid") as string??Environment.MachineName)+"|"+WindowsIdentity.GetCurrent().User?.Value);
    }
    public bool CanEditNow()=>Evaluate().CanEdit;
    private LicenseStatus Evaluate()
    {
        if(_cache?.SimpleClaims is not {} claims)return Status=_trial.Evaluate(DateTimeOffset.UtcNow);
        if(_cache.BlockedCode!=null)return Status=new(false,"License needs attention",_cache.BlockedMessage??"The server rejected this license.");
        var now=DateTimeOffset.UtcNow;
        if(now>=claims.SubscriptionExpiresAt)return Status=new(false,"Subscription expired","Activate or check your subscription to continue.");
        if(now>=claims.OfflineUntil)return Status=new(false,"Reconnect to continue","Check your subscription to renew offline access.");
        return Status=new(true,_offline?"Working offline":"Subscription active",_offline?$"Reconnect by {claims.OfflineUntil.LocalDateTime:g}.":$"Licensed to {claims.Customer}. Subscription ends {claims.SubscriptionExpiresAt.LocalDateTime:d}.",claims);
    }
    private async Task ValidateKey(string key)
    {
        using var content=new StringContent(JsonSerializer.Serialize(new {key,deviceId=DeviceId},LicenseProtocol.Json),System.Text.Encoding.UTF8,"application/json");
        using var response=await _http.PostAsync("validate",content);
        if(!response.IsSuccessStatusCode) {
            string message=$"The licensing server returned HTTP {(int)response.StatusCode}.";
            try {
                using var error=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if(error.RootElement.TryGetProperty("message",out var detail)&&detail.ValueKind==JsonValueKind.String)message=detail.GetString()??message;
            } catch(JsonException) { }
            throw new LicenseException("server_response",message);
        }
        var result=await response.Content.ReadFromJsonAsync<SimpleLicenseResponse>(LicenseProtocol.Json)??throw new LicenseException("invalid_response","Empty license response.");
        if(!result.Valid)throw new LicenseException("invalid_key",result.Message??"The server rejected this license key.");
        var now=DateTimeOffset.UtcNow;
        if(result.ExpiresAt<=now)throw new LicenseException("subscription_expired","The subscription has expired.");
        _cache=new CachedActivation {RefreshToken=key,SimpleClaims=new LicenseClaims {DeviceId=DeviceId,Customer=result.Customer,IssuedAt=now,RenewAfter=now.AddHours(12),OfflineUntil=result.ExpiresAt<now.AddDays(7)?result.ExpiresAt:now.AddDays(7),SubscriptionExpiresAt=result.ExpiresAt}};
        _offline=false;
        _store.Save(_cache);
    }
    public async Task<LicenseStatus> CheckAsync(bool force=false)
    {
        await _gate.WaitAsync();
        try {
            Evaluate();
            if(_cache?.SimpleClaims is not {} claims)return Status;
            var now=DateTimeOffset.UtcNow;
            if((force||!Status.CanEdit||now>=claims.RenewAfter)&&(force||now>=_nextAttempt)) {
                _nextAttempt=now.AddMinutes(15);
                try{await ValidateKey(_cache.RefreshToken);}
                catch(LicenseException ex){_cache.BlockedCode=ex.Code;_cache.BlockedMessage=ex.Message;_store.Save(_cache);}
                catch(Exception ex)when(ex is HttpRequestException or TaskCanceledException or JsonException){_offline=true;}
            }
            return Evaluate();
        } finally{_gate.Release();}
    }
    public async Task ActivateAsync(string licenseKey)
    {
        if(string.IsNullOrWhiteSpace(licenseKey))throw new LicenseException("missing_key","Enter your subscription license key.");
        await _gate.WaitAsync();
        try{await ValidateKey(licenseKey.Trim());Evaluate();}finally{_gate.Release();}
    }
    public async Task DeactivateAsync()
    {
        await _gate.WaitAsync();
        try{_store.Delete();_cache=null;_offline=false;_nextAttempt=default;Evaluate();}finally{_gate.Release();}
    }
    public void Dispose(){_http.Dispose();_gate.Dispose();}
}
