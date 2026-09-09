using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Vapor.Licensing;

/// <summary>Publicly verifiable ownership claims for this exhibition platform.
/// These are our own wire format and policy, not Valve's proprietary ticket format.</summary>
public sealed record TicketClaims
{
    public int Version { get; init; }=1;
    public int AppId { get; init; }
    public string Product { get; init; }="";
    public string SteamId { get; init; }="";
    public string Account { get; init; }="";
    public string DeviceId { get; init; }="";
    public string TicketId { get; init; }=Guid.NewGuid().ToString("N");
    public string License { get; init; }="owned";              // owned | trial | free
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset RenewAfter { get; init; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset TrialEndsAt { get; init; }
}
public sealed record SignedTicket(string Payload,string Signature);
/// <summary>Optional backend encrypted envelope. The game uses the signed ownership proof;
/// no app decryption secret is distributed in the game SDK.</summary>
public sealed record EncryptedAppTicket(string Blob);
public sealed record LoginRequest(string Account,string Password,string DeviceId,string DeviceName);
public sealed record LoginResponse(string SteamId,string Account,string Nickname,string SessionToken);
public sealed record OwnershipQuery(string SessionToken,int AppId);
public sealed record Ownership(int AppId,string Product,string License,DateTimeOffset TrialEndsAt,bool Owned);
public sealed record AppTicketRequest(string SessionToken,int AppId,string DeviceId);
public sealed record AppTicketResponse(EncryptedAppTicket Ticket,Ownership Ownership,SignedTicket? OwnershipProof=null);
public sealed record PurchaseRequest(string SessionToken,int AppId);
public sealed record ApiError(string Code,string Message);
public sealed record BackendTrust(string ServerUrl,string PublicKey,bool ReferenceServer=true);
public sealed record WorksTrust(int AppId,string Product,string SteamPublicKey,string PipeName="vaporworks-ipc");
public sealed record IpcRequest(string Op,int AppId,string DeviceId,string Nonce="",int ProcessId=0,string LaunchSecret="");
public sealed record IpcResponse(bool Ok,string Code,string Message,EncryptedAppTicket? Ticket=null,SignedTicket? OwnershipProof=null,string Nonce="",int ProcessId=0,string AccountId="");
public sealed record IpcEnvelope(string Payload,string Mac);

public sealed class VaporException(string code,string message) : Exception(message)
{
    public string Code { get; }=code;
}
public static class VaporProtocol
{
    public const int OfflineGraceDays=7;
    public const int AfterlightAppId=2937;
    public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static SignedTicket Sign(TicketClaims claims,ECDsa signingKey)
    {
        var payload=JsonSerializer.SerializeToUtf8Bytes(claims,Json);
        var signature=signingKey.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new(Convert.ToBase64String(payload),Convert.ToBase64String(signature));
    }
    public static TicketClaims Verify(SignedTicket envelope,string steamPublicKey,int expectedAppId,string expectedDevice,DateTimeOffset now)
    {
        TicketClaims claims;
        try {
            if(envelope.Payload.Length>32768||envelope.Signature.Length>256)throw new CryptographicException();
            byte[] payload=Convert.FromBase64String(envelope.Payload),signature=Convert.FromBase64String(envelope.Signature);
            using var verifier=ECDsa.Create();verifier.ImportFromPem(steamPublicKey);
            if(verifier.KeySize!=256||!verifier.VerifyData(payload,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))throw new CryptographicException();
            claims=JsonSerializer.Deserialize<TicketClaims>(payload,Json)??throw new CryptographicException();
        }catch(Exception error)when(error is CryptographicException or FormatException or JsonException or ArgumentException or NullReferenceException){throw new VaporException("bad_ticket","This app ticket could not be verified.");}
        if(claims.Version!=1||claims.AppId!=expectedAppId||claims.DeviceId!=expectedDevice||string.IsNullOrWhiteSpace(claims.SteamId))
            throw new VaporException("wrong_device","This ticket belongs to a different app or device.");
        if(claims.License is not ("owned" or "trial" or "free") || string.IsNullOrWhiteSpace(claims.TicketId))
            throw new VaporException("not_owned","This ticket does not grant a supported game license.");
        if(claims.ExpiresAt<=claims.IssuedAt||claims.ExpiresAt>claims.IssuedAt.AddDays(OfflineGraceDays)||claims.RenewAfter<claims.IssuedAt||claims.RenewAfter>claims.ExpiresAt)
            throw new VaporException("bad_lease","This app ticket has invalid validity dates.");
        if(claims.IssuedAt>now.AddMinutes(5))
            throw new VaporException("clock_changed","Your device clock changed. Correct it and restart the game.");
        if(now>=claims.ExpiresAt)throw new VaporException("ticket_expired","This app ticket has expired. Restart the game from your Vapor library.");
        if(claims.License=="trial"&&(claims.TrialEndsAt==default||now>=claims.TrialEndsAt))
            throw new VaporException("trial_ended","Your free trial has ended. Purchase AFTERLIGHT to keep playing.");
        return claims;
    }
    public static EncryptedAppTicket EncryptTicket(SignedTicket ticket,byte[] appKey)
    {
        var plaintext=Encoding.UTF8.GetBytes(JsonSerializer.Serialize(ticket,Json));
        var nonce=RandomNumberGenerator.GetBytes(12);
        var cipher=new byte[plaintext.Length];
        var tag=new byte[16];
        using var aes=new AesGcm(appKey,16);
        aes.Encrypt(nonce,plaintext,cipher,tag);
        var blob=new byte[12+plaintext.Length+16];
        nonce.CopyTo(blob,0);cipher.CopyTo(blob,12);tag.CopyTo(blob,12+plaintext.Length);
        return new EncryptedAppTicket(Convert.ToBase64String(blob));
    }
    public static SignedTicket DecryptTicket(EncryptedAppTicket envelope,byte[] appKey)
    {
        try {
            if(string.IsNullOrEmpty(envelope.Blob)||envelope.Blob.Length>40960)throw new CryptographicException();
            var blob=Convert.FromBase64String(envelope.Blob);
            if(blob.Length<12+16)throw new CryptographicException();
            var nonce=blob[..12];var tag=blob[^16..];var cipher=blob[12..^16];
            var plaintext=new byte[cipher.Length];
            using var aes=new AesGcm(appKey,16);
            aes.Decrypt(nonce,cipher,tag,plaintext,null);
            return JsonSerializer.Deserialize<SignedTicket>(plaintext,Json)??throw new CryptographicException();
        }catch(Exception error)when(error is CryptographicException or FormatException or JsonException or ArgumentException){throw new VaporException("bad_ticket","This app ticket could not be decrypted with this app's key.");}
    }
    public static string NewSecret()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool SecretEquals(string expected,string provided)=>CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),SHA256.HashData(Encoding.UTF8.GetBytes(provided)));
    /// <summary>Same stable per-machine identity the Vapor client computes, so tickets are bound to this device.</summary>
    public static string LocalDeviceId()
    {
        using var machine=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,RegistryView.Registry64);
        using var key=machine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
        string machineId=key?.GetValue("MachineGuid") as string??Environment.MachineName;
        return Hash("vapor-device-v1|"+machineId+"|"+WindowsIdentity.GetCurrent().User?.Value);
    }
    public static bool TrialStillLive(TicketClaims claims,DateTimeOffset now)=>claims.License=="trial"&&claims.TrialEndsAt>now;
}
