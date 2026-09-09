using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Prism.Licensing;

public sealed record LicenseClaims
{
    public int Version { get; init; }=1;
    public string Product { get; init; }="prism-desktop";
    public string SubscriptionId { get; init; }="";
    public string LeaseId { get; init; }=Guid.NewGuid().ToString("N");
    public string DeviceId { get; init; }="";
    public string Customer { get; init; }="";
    public DateTimeOffset IssuedAt { get; init; }
    public DateTimeOffset RenewAfter { get; init; }
    public DateTimeOffset OfflineUntil { get; init; }
    public DateTimeOffset SubscriptionExpiresAt { get; init; }
}
public sealed record SignedLicense(string Payload,string Signature);
public sealed record ActivationRequest(string LicenseKey,string DeviceId,string DeviceName);
public sealed record RenewalRequest(string RefreshToken,string DeviceId);
public sealed record ActivationResponse(SignedLicense License,string RefreshToken);
public sealed record ApiError(string Code,string Message);
public sealed record CreateSubscriptionRequest(string Customer,int Days=30,int MaxDevices=2);
public sealed record ExtendSubscriptionRequest(int Days);
public sealed record LicenseTrust(string ServerUrl,string PublicKey,bool ReferenceServer=true);

public sealed class LicenseException(string code,string message) : Exception(message)
{
    public string Code { get; }=code;
}
public static class LicenseProtocol
{
    public const int OfflineGraceDays=7;
    public static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public static SignedLicense Sign(LicenseClaims claims,ECDsa signingKey)
    {
        var payload=JsonSerializer.SerializeToUtf8Bytes(claims,Json);
        var signature=signingKey.SignData(payload,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new(Convert.ToBase64String(payload),Convert.ToBase64String(signature));
    }
    public static LicenseClaims Verify(SignedLicense envelope,string trustedPublicKey,string expectedDevice,DateTimeOffset now,DateTimeOffset? lastSeen=null)
    {
        LicenseClaims claims;
        try {
            if(envelope.Payload.Length>32768||envelope.Signature.Length>256)throw new CryptographicException();
            byte[] payload=Convert.FromBase64String(envelope.Payload),signature=Convert.FromBase64String(envelope.Signature);
            using var verifier=ECDsa.Create();verifier.ImportFromPem(trustedPublicKey);
            if(verifier.KeySize!=256||!verifier.VerifyData(payload,signature,HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation))throw new CryptographicException();
            claims=JsonSerializer.Deserialize<LicenseClaims>(payload,Json)??throw new CryptographicException();
        }catch(Exception error)when(error is CryptographicException or FormatException or JsonException or ArgumentException or NullReferenceException) {throw new LicenseException("invalid_signature","The license could not be verified. Activate again to obtain a valid license.");}
        if(claims.Version!=1||claims.Product!="prism-desktop"||string.IsNullOrWhiteSpace(claims.SubscriptionId)||claims.DeviceId!=expectedDevice)
            throw new LicenseException("wrong_device","This license belongs to a different device or product.");
        if(claims.OfflineUntil<=claims.IssuedAt||claims.OfflineUntil>claims.IssuedAt.AddDays(OfflineGraceDays)||claims.OfflineUntil>claims.SubscriptionExpiresAt||claims.RenewAfter<claims.IssuedAt||claims.RenewAfter>claims.OfflineUntil)
            throw new LicenseException("invalid_lease","This license has invalid validity dates.");
        if(claims.IssuedAt>now.AddMinutes(5)||(lastSeen.HasValue&&now<lastSeen.Value.AddMinutes(-5)))
            throw new LicenseException("clock_changed","Your device clock changed. Correct it and reconnect to verify your subscription.");
        if(now>=claims.SubscriptionExpiresAt)throw new LicenseException("subscription_expired","Your subscription has expired. Renew it and reconnect to continue editing.");
        if(now>=claims.OfflineUntil)throw new LicenseException("offline_expired","The seven-day offline period has ended. Reconnect to verify your subscription.");
        return claims;
    }
    public static string NewSecret()=>Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    public static bool SecretEquals(string expected,string provided)=>CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(expected)),SHA256.HashData(Encoding.UTF8.GetBytes(provided)));
}
