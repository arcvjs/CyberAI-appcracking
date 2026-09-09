using System.Security.Cryptography;
using System.Text.Json;
using Prism.Licensing;
namespace Prism.LicenseServer;

public sealed class DeviceActivation
{
    public string DeviceId {get;set;}="";
    public string Name {get;set;}="";
    public string RefreshHash {get;set;}="";
    public DateTimeOffset ActivatedAt {get;set;}
    public DateTimeOffset LastSeen {get;set;}
}
public sealed class Subscription
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Customer {get;set;}="";
    public string KeyHash {get;set;}="";
    public DateTimeOffset ExpiresAt {get;set;}
    public bool Revoked {get;set;}
    public int MaxDevices {get;set;}=2;
    public List<DeviceActivation> Devices {get;set;}=[];
}
public sealed class LicenseAuthority : IDisposable
{
    private readonly string _database;
    private readonly ECDsa _signer;
    private readonly object _gate=new();
    private readonly Func<DateTimeOffset> _clock;
    private List<Subscription> _subscriptions;
    public LicenseAuthority(string directory,Func<DateTimeOffset>? clock=null)
    {
        _clock=clock??(()=>DateTimeOffset.UtcNow);_database=Path.Combine(directory,"subscriptions.json");
        _signer=ECDsa.Create();_signer.ImportFromPem(File.ReadAllText(Path.Combine(directory,"signing-key.pem")));
        _subscriptions=File.Exists(_database)?JsonSerializer.Deserialize<List<Subscription>>(File.ReadAllText(_database),LicenseProtocol.Json)??throw new InvalidDataException("Invalid subscription database."):[];
    }
    public static LicenseTrust Initialize(string directory,string serverUrl)
    {
        Directory.CreateDirectory(directory);
        var keyFile=Path.Combine(directory,"signing-key.pem");
        if(!File.Exists(keyFile)){using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);WriteNew(keyFile,key.ExportPkcs8PrivateKeyPem());}
        if(!File.Exists(Path.Combine(directory,"admin-key.txt")))WriteNew(Path.Combine(directory,"admin-key.txt"),LicenseProtocol.NewSecret());
        using var signer=ECDsa.Create();signer.ImportFromPem(File.ReadAllText(keyFile));
        var trust=new LicenseTrust(serverUrl,signer.ExportSubjectPublicKeyInfoPem());
        File.WriteAllText(Path.Combine(directory,"client-trust.json"),JsonSerializer.Serialize(trust,LicenseProtocol.Json));
        return trust;
    }
    private static void WriteNew(string path,string text){using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);using var writer=new StreamWriter(file);writer.Write(text);}
    private void Save()
    {
        var temp=_database+"."+Guid.NewGuid().ToString("N")+".tmp";
        var bytes=JsonSerializer.SerializeToUtf8Bytes(_subscriptions,LicenseProtocol.Json);
        using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}
        File.Move(temp,_database,true);
    }
    public object List()
    {
        lock(_gate)return _subscriptions.Select(s=>new {s.Id,s.Customer,s.ExpiresAt,s.Revoked,s.MaxDevices,Devices=s.Devices.Select(d=>new{d.DeviceId,d.Name,d.ActivatedAt,d.LastSeen}).ToArray()}).ToArray();
    }
    public (string Key,Subscription Subscription) Create(CreateSubscriptionRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.Customer)||request.Customer.Length>120||request.Days<1||request.Days>3650||request.MaxDevices<1||request.MaxDevices>20)throw new LicenseException("invalid_request","Provide a customer name, 1–3650 subscription days, and 1–20 devices.");
        lock(_gate){var key="PRISM-"+LicenseProtocol.NewSecret();var subscription=new Subscription{Customer=request.Customer.Trim(),KeyHash=LicenseProtocol.Hash(key),ExpiresAt=_clock().AddDays(request.Days),MaxDevices=request.MaxDevices};_subscriptions.Add(subscription);Save();return(key,subscription);}
    }
    public void Extend(string id,int days)
    {
        if(days<1||days>3650)throw new LicenseException("invalid_request","Renewal must be between 1 and 3650 days.");
        lock(_gate){var s=Find(id);s.ExpiresAt=(s.ExpiresAt>_clock()?s.ExpiresAt:_clock()).AddDays(days);Save();}
    }
    public void SetRevoked(string id,bool revoked){lock(_gate){Find(id).Revoked=revoked;Save();}}
    public void RemoveDevice(string id,string device){lock(_gate){var s=Find(id);s.Devices.RemoveAll(d=>d.DeviceId==device);Save();}}
    private Subscription Find(string id)=>_subscriptions.FirstOrDefault(s=>s.Id==id)??throw new LicenseException("not_found","Subscription not found.");
    private static void ValidateDevice(string device)
    {
        if(device==null||device.Length!=64||device.Any(c=>!Uri.IsHexDigit(c)))throw new LicenseException("invalid_request","Invalid device identifier.");
    }
    public ActivationResponse Activate(ActivationRequest request)
    {
        ValidateDevice(request.DeviceId);
        if(string.IsNullOrWhiteSpace(request.LicenseKey)||request.LicenseKey.Length>128||string.IsNullOrWhiteSpace(request.DeviceName)||request.DeviceName.Length>80)throw new LicenseException("invalid_request","Provide a license key and a device name.");
        lock(_gate){
            var hash=LicenseProtocol.Hash(request.LicenseKey.Trim());
            var subscription=_subscriptions.FirstOrDefault(s=>LicenseProtocol.SecretEquals(s.KeyHash,hash))??throw new LicenseException("invalid_key","The license key was not recognized.");
            EnsureActive(subscription);
            var device=subscription.Devices.FirstOrDefault(d=>d.DeviceId==request.DeviceId);
            if(device==null){if(subscription.Devices.Count>=subscription.MaxDevices)throw new LicenseException("device_limit","All device activations are in use. Deactivate another device first.");device=new DeviceActivation{DeviceId=request.DeviceId,Name=request.DeviceName,ActivatedAt=_clock()};subscription.Devices.Add(device);}
            var refresh=LicenseProtocol.NewSecret();device.RefreshHash=LicenseProtocol.Hash(refresh);device.LastSeen=_clock();var license=Issue(subscription,device);Save();return new(license,refresh);
        }
    }
    public ActivationResponse Renew(RenewalRequest request)
    {
        ValidateDevice(request.DeviceId);
        if(string.IsNullOrWhiteSpace(request.RefreshToken)||request.RefreshToken.Length>128)throw new LicenseException("invalid_activation","Activate this device again.");
        lock(_gate){var hash=LicenseProtocol.Hash(request.RefreshToken);var subscription=_subscriptions.FirstOrDefault(s=>s.Devices.Any(d=>d.DeviceId==request.DeviceId&&LicenseProtocol.SecretEquals(d.RefreshHash,hash)))??throw new LicenseException("invalid_activation","This device is no longer activated. Activate again to continue.");EnsureActive(subscription);var device=subscription.Devices.Single(d=>d.DeviceId==request.DeviceId);device.LastSeen=_clock();var lease=Issue(subscription,device);Save();return new(lease,request.RefreshToken);}
    }
    public void Deactivate(RenewalRequest request)
    {
        ValidateDevice(request.DeviceId);
        if(string.IsNullOrWhiteSpace(request.RefreshToken)||request.RefreshToken.Length>128)throw new LicenseException("invalid_activation","Invalid activation.");
        lock(_gate){var hash=LicenseProtocol.Hash(request.RefreshToken);var subscription=_subscriptions.FirstOrDefault(s=>s.Devices.Any(d=>d.DeviceId==request.DeviceId&&LicenseProtocol.SecretEquals(d.RefreshHash,hash)))??throw new LicenseException("invalid_activation","This device is not activated.");subscription.Devices.RemoveAll(d=>d.DeviceId==request.DeviceId);Save();}
    }
    private void EnsureActive(Subscription s){if(s.Revoked)throw new LicenseException("revoked","This subscription has been revoked. Contact the license administrator.");if(s.ExpiresAt<=_clock())throw new LicenseException("subscription_expired","Your subscription has expired. Renew it before activating.");}
    private SignedLicense Issue(Subscription s,DeviceActivation device)
    {
        var now=_clock();var until=s.ExpiresAt<now.AddDays(7)?s.ExpiresAt:now.AddDays(7);
        return LicenseProtocol.Sign(new LicenseClaims{SubscriptionId=s.Id,Customer=s.Customer,DeviceId=device.DeviceId,IssuedAt=now,RenewAfter=until<now.AddHours(12)?until:now.AddHours(12),OfflineUntil=until,SubscriptionExpiresAt=s.ExpiresAt},_signer);
    }
    public void Dispose()=>_signer.Dispose();
}
