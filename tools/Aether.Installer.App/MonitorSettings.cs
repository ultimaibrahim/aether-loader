using System.Globalization;
using System.Text.Json;

namespace Aether.Installer.App;

/// <summary>What the Monitor remembers between runs: the language and how the catalog is shown. Kept in the user's config folder.</summary>
internal sealed class MonitorSettings
{
    public string Language { get; set; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "es" ? "es" : "en";
    public string CatalogView { get; set; } = "cards";     // cards | list

    private static string FilePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "aether", "monitor.json");

    public static MonitorSettings Load()
    {
        try
        {
            var s = JsonSerializer.Deserialize<MonitorSettings>(File.ReadAllText(FilePath)) ?? new MonitorSettings();
            if (s.Language is not ("es" or "en")) s.Language = "en";
            if (s.CatalogView is not ("cards" or "list")) s.CatalogView = "cards";
            return s;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return new MonitorSettings(); }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* a read-only profile: keep the choice for this run */ }
    }
}
