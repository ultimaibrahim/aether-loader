namespace Aether.Installer;

/// <summary>Finds the Block Story folder in the usual Steam locations.</summary>
public static class GameLocator
{
    public static string? Detect()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string>
        {
            Path.Combine(home, ".local", "share", "Steam", "steamapps", "common", "BlockStory"),
            Path.Combine(home, ".steam", "steam", "steamapps", "common", "BlockStory"),
            Path.Combine(home, ".steam", "debian-installation", "steamapps", "common", "BlockStory"),
            Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam", "steamapps", "common", "BlockStory"),
            @"C:\Program Files (x86)\Steam\steamapps\common\BlockStory",
            @"C:\Program Files\Steam\steamapps\common\BlockStory",
            Path.Combine(home, "Library", "Application Support", "Steam", "steamapps", "common", "BlockStory")
        };
        return candidates.FirstOrDefault(Directory.Exists);
    }
}
