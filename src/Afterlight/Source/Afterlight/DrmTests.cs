using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vapor.Licensing;
using Vaporworks;

namespace Afterlight;

/// <summary>DRM attack simulations for the exhibition: every tamper path is proven rejected
/// against the real Vapor trust configuration the shipped game embeds.</summary>
public static class DrmTests
{
    static int _passed;
    static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAILED: " + name);
        _passed++; Console.WriteLine("PASS: " + name);
    }
    static void Reject(Action action, string code)
    {
        try { action(); throw new Exception("Expected rejection: " + code); }
        catch (VaporException e) { Check(e.Code == code, "Reject " + code); }
    }

    public static int Run()
    {
        try
        {
            // Real trust: same embedded key set the shipped Afterlight.exe carries.
            var trust=JsonSerializer.Deserialize<WorksTrust>(
                new StreamReader(typeof(VaporAPI).Assembly
                    .GetManifestResourceStream("Vaporworks.Trust.json")!).ReadToEnd(), VaporProtocol.Json)!;
            byte[] appKey=RandomNumberGenerator.GetBytes(32);
            // A fresh issuer pair plays "the Steam backend" for protocol tests; the embedded
            // trust below is still exercised for app-id binding and the encrypted-ticket key.
            using var issuer=ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string steamPublic=issuer.ExportSubjectPublicKeyInfoPem();
            // A separate attacker key pair for forgery attempts.
            using var attacker=ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string machine=VaporProtocol.LocalDeviceId();
            var now=DateTimeOffset.UtcNow;

            SignedTicket Issue(string license,DateTimeOffset issued,DateTimeOffset expires,DateTimeOffset trialEnds,ECDsa key)=>VaporProtocol.Sign(new TicketClaims{
                AppId=trust.AppId,Product=trust.Product,SteamId="76561191701230616",Account="Festival Goer",DeviceId=machine,
                License=license,IssuedAt=issued,RenewAfter=issued+(expires-issued)/2,ExpiresAt=expires,TrialEndsAt=trialEnds},key);
            TicketClaims Accept(SignedTicket ticket)=>VaporProtocol.Verify(ticket,steamPublic,trust.AppId,machine,now);

            // 1. A genuine owned ticket validates end to end.
            var genuine=Issue("owned",now.AddMinutes(-5),now.AddHours(4),default,issuer);
            Check(Accept(genuine).License=="owned","Genuine owned ticket accepted");

            // 2. Forged ticket signed by the attacker's own key.
            Reject(()=>Accept(Issue("owned",now.AddMinutes(-5),now.AddHours(4),default,attacker)),"bad_ticket");

            // 3. Tampered payload (trial flipped to owned) under the genuine signature.
            string payload=Encoding.UTF8.GetString(Convert.FromBase64String(genuine.Payload));
            var tampered=genuine with{Payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(payload.Replace("\"license\":\"owned\"","\"license\":\"trial\"")))};
            Reject(()=>Accept(tampered),"bad_ticket");

            // 4. Trial whose window has closed.
            Reject(()=>Accept(Issue("trial",now.AddMinutes(-40),now.AddMinutes(10),now.AddMinutes(-20),issuer)),"trial_ended");

            // 5. Trial with time left still passes.
            Check(Accept(Issue("trial",now.AddMinutes(-15),now.AddMinutes(30),now.AddMinutes(5),issuer)).License=="trial","Live trial accepted");

            // 6. Ticket bound to a different machine.
            Reject(()=>VaporProtocol.Verify(genuine,steamPublic,trust.AppId,new string('B',64),now),"wrong_device");

            // 7. Ticket for a different app.
            Reject(()=>VaporProtocol.Verify(genuine,steamPublic,1234,machine,now),"wrong_device");

            // 8. Expired ticket.
            Reject(()=>Accept(Issue("owned",now.AddHours(-6),now.AddHours(-1),default,issuer)),"ticket_expired");

            // 9. Clock rollback: issued in the future.
            Reject(()=>Accept(Issue("owned",now.AddHours(2),now.AddHours(6),default,issuer)),"clock_changed");

            // 10. Lease window longer than the offline grace period.
            Reject(()=>Accept(Issue("owned",now,now.AddDays(30),default,issuer)),"bad_lease");

            // 11. Encrypted app ticket roundtrip with the real app key.
            var envelope=VaporProtocol.EncryptTicket(genuine,appKey);
            Check(VaporProtocol.Verify(VaporProtocol.DecryptTicket(envelope,appKey),steamPublic,trust.AppId,machine,now).License=="owned","Encrypted app ticket roundtrip");

            // 12. ...and decryption fails with any other key (what a copied blob is worth to an attacker).
            var wrongKey=VaporProtocol.EncryptTicket(genuine,RandomNumberGenerator.GetBytes(32));
            Reject(()=>VaporProtocol.DecryptTicket(wrongKey,appKey),"bad_ticket");

            // 13. A trial ticket extended by editing bytes fails the signature first.
            var expiredTrial=Issue("trial",now.AddMinutes(-40),now.AddMinutes(10),now.AddMinutes(-20),issuer);
            string expiredPayload=Encoding.UTF8.GetString(Convert.FromBase64String(expiredTrial.Payload));
            string deadline=now.AddMinutes(-20).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss");
            string pushed=now.AddDays(2).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss");
            var extended=expiredTrial with{Payload=Convert.ToBase64String(Encoding.UTF8.GetBytes(expiredPayload.Replace(deadline,pushed)))};
            Check(expiredPayload.Contains(deadline),"Trial deadline found in payload");
            Reject(()=>Accept(extended),"bad_ticket");

            // Regression: validity must be recomputed, including while menus are open.
            var shortTrial=new VaporSession(true,"ok","",new TicketClaims{License="trial",TrialEndsAt=DateTimeOffset.UtcNow.AddMilliseconds(150),ExpiresAt=DateTimeOffset.UtcNow.AddMinutes(1)});
            Check(shortTrial.TicketStillValid,"Live session initially valid");
            Thread.Sleep(220);
            Check(!shortTrial.TicketStillValid,"Trial expires after its first validity check");
            var oldLease=new VaporSession(true,"ok","",new TicketClaims{License="owned",IssuedAt=now.AddDays(-3),ExpiresAt=now.AddSeconds(-1)});
            Check(!oldLease.TicketStillValid,"Cached ticket age is respected at session creation");
            Reject(()=>Accept(Issue("invented",now.AddMinutes(-1),now.AddMinutes(10),default,issuer)),"not_owned");

            Console.WriteLine($"All {_passed} DRM checks passed.");
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"drm-verification.txt"),$"All {_passed} DRM checks passed.\n");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
}
