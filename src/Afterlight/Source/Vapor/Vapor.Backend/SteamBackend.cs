using System.Security.Cryptography;
using System.Text.Json;
using Vapor.Licensing;

namespace Vapor.Backend;

public sealed class SteamAccount
{
    public string SteamId {get;set;}="";
    public string Account {get;set;}="";
    public string Nickname {get;set;}="";
    public string PasswordHash {get;set;}="";
    public DateTimeOffset CreatedAt {get;set;}
}
public sealed class AppLicense
{
    public string SteamId {get;set;}="";
    public int AppId {get;set;}
    public string License {get;set;}="trial";           // owned | trial | free
    public DateTimeOffset GrantedAt {get;set;}
    public DateTimeOffset TrialEndsAt {get;set;}
    public bool Revoked {get;set;}
}
public sealed class CatalogApp
{
    public int AppId {get;set;}
    public string Product {get;set;}="";
    public string Title {get;set;}="";
}
public sealed record CreateAccountRequest(string Account,string Password,string Nickname);
public sealed record SetTrialRequest(int Minutes);
public sealed record GrantRequest(string License="owned");

/// <summary>The Vapor equivalent of Steam's account, ownership, and ticket-signing services.
/// It owns the signing key, the account database, the app ownership (license) database, and
/// issues signed app tickets bound to one app and one device.</summary>
public sealed class SteamBackend : IDisposable
{
    public static readonly CatalogApp[] Catalog=[new(){AppId=VaporProtocol.AfterlightAppId,Product="afterlight",Title="AFTERLIGHT"}];
    private readonly string _directory;
    private readonly ECDsa _signer;
    private readonly byte[] _appKey;
    private readonly object _gate=new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly Dictionary<string,(string SteamId,DateTimeOffset Expires)> _sessions=new();
    private List<SteamAccount> _accounts;
    private List<AppLicense> _licenses;
    public SteamBackend(string directory,Func<DateTimeOffset>? clock=null)
    {
        _clock=clock??(()=>DateTimeOffset.UtcNow);
        _directory=directory;
        _signer=ECDsa.Create();_signer.ImportFromPem(File.ReadAllText(Path.Combine(directory,"signing-key.pem")));
        _appKey=Convert.FromBase64String(File.ReadAllText(Path.Combine(directory,"app-key.txt")).Trim());
        _accounts=File.Exists(AccountsDb)?JsonSerializer.Deserialize<List<SteamAccount>>(File.ReadAllText(AccountsDb),VaporProtocol.Json)??throw new InvalidDataException("Invalid account database."):[];
        _licenses=File.Exists(LicensesDb)?JsonSerializer.Deserialize<List<AppLicense>>(File.ReadAllText(LicensesDb),VaporProtocol.Json)??throw new InvalidDataException("Invalid ownership database."):[];
    }
    private string AccountsDb=>Path.Combine(_directory,"accounts.json");
    private string LicensesDb=>Path.Combine(_directory,"ownership.json");
    public static BackendTrust Initialize(string directory,string serverUrl)
    {
        Directory.CreateDirectory(directory);
        var keyFile=Path.Combine(directory,"signing-key.pem");
        if(!File.Exists(keyFile)){using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);WriteNew(keyFile,key.ExportPkcs8PrivateKeyPem());}
        var appKeyFile=Path.Combine(directory,"app-key.txt");
        if(!File.Exists(appKeyFile))WriteNew(appKeyFile,Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        if(!File.Exists(Path.Combine(directory,"admin-key.txt")))WriteNew(Path.Combine(directory,"admin-key.txt"),VaporProtocol.NewSecret());
        using var signer=ECDsa.Create();signer.ImportFromPem(File.ReadAllText(keyFile));
        var trust=new BackendTrust(serverUrl,signer.ExportSubjectPublicKeyInfoPem());
        File.WriteAllText(Path.Combine(directory,"client-trust.json"),JsonSerializer.Serialize(trust,VaporProtocol.Json));
        var works=new WorksTrust(VaporProtocol.AfterlightAppId,"afterlight",signer.ExportSubjectPublicKeyInfoPem());
        File.WriteAllText(Path.Combine(directory,"vaporworks-trust.json"),JsonSerializer.Serialize(works,VaporProtocol.Json));
        return trust;
    }
    private static void WriteNew(string path,string text){using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);using var writer=new StreamWriter(file);writer.Write(text);}
    private void Save()
    {
        Save(AccountsDb,_accounts);Save(LicensesDb,_licenses);
        void Save(string path,object data)
        {
            var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            var bytes=JsonSerializer.SerializeToUtf8Bytes(data,VaporProtocol.Json);
            using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){file.Write(bytes);file.Flush(true);}
            File.Move(temp,path,true);
        }
    }
    private static CatalogApp FindApp(int appId)=>Catalog.FirstOrDefault(a=>a.AppId==appId)??throw new VaporException("not_found","This app is not on the Vapor store.");
    public object ListAccounts(){lock(_gate){return _accounts.Select(a=>new{a.SteamId,a.Account,a.Nickname,a.CreatedAt}).ToArray();}}
    public object ListLicenses(){lock(_gate){return _licenses.Select(l=>new{l.SteamId,l.AppId,l.License,l.GrantedAt,l.TrialEndsAt,l.Revoked}).ToArray();}}
    public (string SteamId,SteamAccount Account) CreateAccount(CreateAccountRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.Account)||request.Account.Length>60||request.Account.Any(char.IsWhiteSpace))throw new VaporException("invalid_request","Provide an account name without spaces (max 60 characters).");
        if(string.IsNullOrWhiteSpace(request.Password)||request.Password.Length>128)throw new VaporException("invalid_request","Provide a password.");
        lock(_gate)
        {
            if(_accounts.Any(a=>string.Equals(a.Account,request.Account.Trim(),StringComparison.OrdinalIgnoreCase)))throw new VaporException("account_exists","That account name is already taken.");
            var account=new SteamAccount{SteamId=NewSteamId(),Account=request.Account.Trim(),Nickname=string.IsNullOrWhiteSpace(request.Nickname)?request.Account.Trim():request.Nickname.Trim(),PasswordHash=VaporProtocol.Hash(request.Password),CreatedAt=_clock()};
            _accounts.Add(account);Save();return(account.SteamId,account);
        }
    }
    private static string NewSteamId()=>"7656119"+RandomNumberGenerator.GetInt32(1_000_000_000,1_999_999_999);
    public LoginResponse Login(LoginRequest request)
    {
        if(string.IsNullOrWhiteSpace(request.Account)||request.Account.Length>60||string.IsNullOrWhiteSpace(request.Password)||request.Password.Length>128)throw new VaporException("invalid_request","Provide an account name and a password.");
        lock(_gate)
        {
            var account=_accounts.FirstOrDefault(a=>string.Equals(a.Account,request.Account.Trim(),StringComparison.OrdinalIgnoreCase))??throw new VaporException("invalid_account","That account does not exist.");
            if(!VaporProtocol.SecretEquals(account.PasswordHash,VaporProtocol.Hash(request.Password)))throw new VaporException("invalid_password","The password you entered is incorrect.");
            var token=VaporProtocol.NewSecret();
            _sessions[token]=(account.SteamId,_clock().AddHours(24));
            return new(account.SteamId,account.Account,account.Nickname,token);
        }
    }
    private string Authorize(string sessionToken)
    {
        if(string.IsNullOrWhiteSpace(sessionToken)||sessionToken.Length>128)throw new VaporException("invalid_session","Sign in to Vapor again.");
        lock(_gate)
        {
            if(!_sessions.TryGetValue(sessionToken,out var session)||session.Expires<=_clock()){_sessions.Remove(sessionToken);throw new VaporException("invalid_session","Your sign-in expired. Sign in to Vapor again.");}
            return session.SteamId;
        }
    }
    public Ownership GetOwnership(OwnershipQuery query)
    {
        var steamId=Authorize(query.SessionToken);
        var app=FindApp(query.AppId);
        lock(_gate)
        {
            var license=_licenses.FirstOrDefault(l=>l.SteamId==steamId&&l.AppId==query.AppId&&!l.Revoked);
            return ToOwnership(app,license);
        }
    }
    private static Ownership ToOwnership(CatalogApp app,AppLicense? license)=>new(app.AppId,app.Product,license?.License??"none",license?.TrialEndsAt??default,license!=null);
    public Ownership Purchase(PurchaseRequest request)
    {
        var steamId=Authorize(request.SessionToken);
        var app=FindApp(request.AppId);
        lock(_gate)
        {
            var license=_licenses.FirstOrDefault(l=>l.SteamId==steamId&&l.AppId==request.AppId);
            if(license!=null&&license.Revoked)license.Revoked=false;
            if(license==null){license=new AppLicense{SteamId=steamId,AppId=app.AppId,GrantedAt=_clock()};_licenses.Add(license);}
            license.License="owned";license.TrialEndsAt=default;Save();
            return ToOwnership(app,license);
        }
    }
    /// <summary>Issues the encrypted app ticket the in-game SDK validates. Trial licenses stop
    /// being issued the moment the trial window closes — the ticket simply never exists.</summary>
    public AppTicketResponse IssueAppTicket(AppTicketRequest request)
    {
        var steamId=Authorize(request.SessionToken);
        var app=FindApp(request.AppId);
        if(request.DeviceId==null||request.DeviceId.Length!=64||request.DeviceId.Any(c=>!Uri.IsHexDigit(c)))throw new VaporException("invalid_request","Invalid device identifier.");
        lock(_gate)
        {
            var license=_licenses.FirstOrDefault(l=>l.SteamId==steamId&&l.AppId==request.AppId&&!l.Revoked)??throw new VaporException("not_owned","This account does not own AFTERLIGHT. Purchase it on the Vapor store.");
            var now=_clock();
            if(license.License=="trial")
            {
                if(license.TrialEndsAt==default)license.TrialEndsAt=now.AddDays(1);   // safety: unbounded trials are not issued
                if(now>=license.TrialEndsAt)throw new VaporException("trial_ended","Your free trial has ended. Purchase AFTERLIGHT to keep playing.");
            }
            var account=_accounts.Single(a=>a.SteamId==steamId);
            var expires=now.AddDays(VaporProtocol.OfflineGraceDays);
            if(license.License=="trial"&&license.TrialEndsAt<expires)expires=license.TrialEndsAt;
            var claims=new TicketClaims{AppId=app.AppId,Product=app.Product,SteamId=steamId,Account=account.Nickname,DeviceId=request.DeviceId,License=license.License,IssuedAt=now,RenewAfter=expires<now.AddHours(6)?expires:now.AddHours(6),ExpiresAt=expires,TrialEndsAt=license.TrialEndsAt};
            var signed=VaporProtocol.Sign(claims,_signer);
            var ticket=VaporProtocol.EncryptTicket(signed,_appKey);
            return new(ticket,ToOwnership(app,license),signed);
        }
    }
    // ---- administration: this is how the exhibition flips the trial state ----
    public void SetTrial(string steamId,int appId,int minutes)
    {
        if(minutes<0||minutes>60*24*30)throw new VaporException("invalid_request","Trial length must be 0 (expired) or up to 30 days.");
        var app=FindApp(appId);
        lock(_gate)
        {
            if(!_accounts.Any(a=>a.SteamId==steamId))throw new VaporException("not_found","Account not found.");
            var license=_licenses.FirstOrDefault(l=>l.SteamId==steamId&&l.AppId==appId);
            if(license==null){license=new AppLicense{SteamId=steamId,AppId=appId,GrantedAt=_clock()};_licenses.Add(license);}
            license.Revoked=false;license.License="trial";
            license.TrialEndsAt=minutes==0?_clock().AddDays(-1):_clock().AddMinutes(minutes);Save();
        }
    }
    public void SetLicense(string steamId,int appId,GrantRequest request)
    {
        if(request.License is not ("owned" or "free"))throw new VaporException("invalid_request","License must be owned or free.");
        var app=FindApp(appId);
        lock(_gate)
        {
            if(!_accounts.Any(a=>a.SteamId==steamId))throw new VaporException("not_found","Account not found.");
            var license=_licenses.FirstOrDefault(l=>l.SteamId==steamId&&l.AppId==appId);
            if(license==null){license=new AppLicense{SteamId=steamId,AppId=appId,GrantedAt=_clock()};_licenses.Add(license);}
            license.Revoked=false;license.License=request.License;license.TrialEndsAt=default;Save();
        }
    }
    public void Revoke(string steamId,int appId){FindApp(appId);lock(_gate){var license=_licenses.FirstOrDefault(l=>l.SteamId==steamId&&l.AppId==appId)??throw new VaporException("not_found","License not found.");license.Revoked=true;Save();}}
    public void Dispose()=>_signer.Dispose();
}
