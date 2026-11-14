using System.Text.RegularExpressions;

namespace Aether.Installer;

/// <summary>
/// Linux only: Steam must start Block Story through BepInEx's script, or the game runs without mods. Reads Steam's own
/// <c>userdata/*/config/localconfig.vdf</c> to tell whether the launch option is already set, so the installer only asks when needed.
/// </summary>
public static class SteamLaunchOption
{
    public const string Value = "./run_bepinex.sh %command%";
    const string AppId = "270110";

    /// <summary>
    /// True when run_bepinex.sh is the BepInEx 5.4.23.2 script (another installer may have put it over Aether's). That script
    /// only looks for "SteamLaunch" as the second argument, current Steam puts it later, and the game exits at once.
    /// </summary>
    public static bool ScriptIsOutdated(string gameDir)
    {
        try
        {
            string script = Path.Combine(gameDir, "run_bepinex.sh");
            if (!File.Exists(script)) return false;
            string text = File.ReadAllText(script);
            return text.Contains("\"$2\" = \"SteamLaunch\"") && !text.Contains("Could not determine game executable launched by Steam");
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    /// <summary>True when some Steam user has a launch option for Block Story that runs BepInEx (run_bepinex.sh, bs_steam.sh or aether-launch).
    /// False when none is set or Steam's files cannot be found (then the installer shows the hint).</summary>
    public static bool IsSet(string gameDir) => IsSet(gameDir, null);

    /// <summary>Like <see cref="IsSet(string)"/>, but when <paramref name="script"/> is given only a launch option naming that script counts.</summary>
    public static bool IsSet(string gameDir, string? script)
    {
        // <steam>/steamapps/common/BlockStory -> <steam>
        string? steam = Directory.GetParent(gameDir)?.Parent?.Parent?.FullName;
        if (steam == null) return false;
        string userdata = Path.Combine(steam, "userdata");
        if (!Directory.Exists(userdata)) return false;
        foreach (string user in Directory.GetDirectories(userdata))
        {
            string file = Path.Combine(user, "config", "localconfig.vdf");
            try { if (File.Exists(file) && RunsBepInEx(File.ReadAllText(file), script)) return true; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return false;
    }

    /// <summary>Looks inside every <c>"270110" { ... }</c> block of a localconfig.vdf for a LaunchOptions that runs BepInEx.</summary>
    public static bool RunsBepInEx(string vdf, string? script = null)
    {
        foreach (Match m in Regex.Matches(vdf, "\"" + AppId + "\"\\s*\\{"))
        {
            int depth = 1, i = m.Index + m.Length, start = i;
            while (i < vdf.Length && depth > 0)
            {
                if (vdf[i] == '{') depth++;
                else if (vdf[i] == '}') depth--;
                i++;
            }
            var opt = Regex.Match(vdf.Substring(start, i - start), "\"LaunchOptions\"\\s*\"([^\"]*)\"");
            string v = opt.Success ? opt.Groups[1].Value : "";
            if (script != null ? v.Contains(script) : v.Contains("run_bepinex.sh") || v.Contains("bs_steam.sh") || v.Contains("aether-launch")) return true;
        }
        return false;
    }
}
