using Vapor.Licensing;
namespace Vaporworks;

public sealed record VaporSession(bool Ok,string Code,string Message,TicketClaims? Ticket)
{
    public DateTimeOffset EffectiveNow => DateTimeOffset.UtcNow;
    public bool TicketStillValid => Ok && Ticket != null;
}
public static class VaporAPI
{
    public static string? LauncherPath => null;
    public static bool OpenLauncher(bool play=false) => false;
    public static bool RestartAppIfNecessary() => false;
    public static VaporSession Init() => new(true,"ok","Local API emulation",new TicketClaims {
        AppId=2937, Product="Afterlight", Account="Player", SteamId="local-player", License="owned",
        IssuedAt=DateTimeOffset.UtcNow, ExpiresAt=DateTimeOffset.MaxValue, TrialEndsAt=DateTimeOffset.MaxValue
    });
    public static Task<VaporSession> InitAsync() => Task.FromResult(Init());
    public static bool TicketStillValid(VaporSession session) => session.TicketStillValid;
}
