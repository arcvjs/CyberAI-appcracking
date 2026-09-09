namespace Vapor.Licensing;

public static class ExhibitionState
{
    public static bool Enabled => File.Exists(Path.Combine(AppContext.BaseDirectory, "exhibition.json"));
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vapor", "Exhibition", VaporProtocol.Hash(Path.GetFullPath(AppContext.BaseDirectory).ToUpperInvariant())[..16], "service-data");
}
