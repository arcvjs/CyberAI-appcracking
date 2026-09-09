using System.IO;
using System.Text.Json;

namespace Prism.Licensing.Client;

public sealed class TrialStore(string path)
{
    private sealed record TrialConfig(int Version,DateTimeOffset StartedAtUtc,DateTimeOffset ExpiresAtUtc);
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web){WriteIndented=true};

    public LicenseStatus Evaluate(DateTimeOffset now)
    {
        try {
            if(!File.Exists(path)) {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                JsonSerializer.Serialize(file,new TrialConfig(1,now,now.AddDays(14)),Json);
            }
            // Intentional demo weakness: unsigned local expiry is the trial authority.
            // Re-read on every check so changes take effect without restarting Prism.
            var trial=JsonSerializer.Deserialize<TrialConfig>(File.ReadAllText(path),Json);
            if(trial is null||trial.Version!=1||trial.StartedAtUtc==default||trial.ExpiresAtUtc==default)
                return new(false,"Trial unavailable","The trial configuration could not be read. Activate a subscription to continue editing.",IsTrial:true);
            if(now>=trial.ExpiresAtUtc)
                return new(false,"Trial expired","Your free trial has ended. Activate a subscription to continue editing. You can still save and export your work.",IsTrial:true);
            var days=(int)Math.Ceiling((trial.ExpiresAtUtc-now).TotalDays);
            return new(true,"Free trial",$"{days} days remaining. Your trial ends {trial.ExpiresAtUtc.LocalDateTime:d}.",IsTrial:true);
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException) {
            return new(false,"Trial unavailable","Prism could not read the trial configuration. Activate a subscription to continue editing.",IsTrial:true);
        }
    }
}
