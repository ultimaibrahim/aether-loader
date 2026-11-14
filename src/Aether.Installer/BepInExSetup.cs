using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Aether.Installer;

/// <summary>
/// Installs the BepInEx build Aether is tested with. The download is only trusted when its SHA-256
/// matches the pin below; a mismatch aborts before anything is written to the game folder.
/// </summary>
public static class BepInExSetup
{
    public const string Version = "5.4.23.6";

    public sealed record Pin(string FileName, string Sha256)
    {
        public string Url => $"https://github.com/BepInEx/BepInEx/releases/download/v{Version}/{FileName}";
    }

    public static readonly Pin Linux = new(
        $"BepInEx_linux_x64_{Version}.zip",
        "f8ac87a97fb1ef85451c04ace1fddb7dd7eb0b079c179b989d6bc462c98bd365");

    public static readonly Pin Windows = new(
        $"BepInEx_win_x64_{Version}.zip",
        "6c57a714a892efb4e9e1fef63d608a3391f30ec984031c465404e4ee381a0755");

    public static Pin? ForCurrentPlatform() =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? Linux :
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? Windows : null;

    public static bool IsInstalled(string gameDir) =>
        File.Exists(Path.Combine(gameDir, "BepInEx", "core", "BepInEx.dll"));

    public static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    public static bool Matches(string path, string expectedSha256) =>
        string.Equals(Sha256Of(path), expectedSha256, StringComparison.OrdinalIgnoreCase);

    /// <summary>Finds the game executable (Linux: *.x86_64, Windows: the exe next to the *_Data folder).</summary>
    public static string? DetectExecutable(string gameDir)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            foreach (string dataDir in Directory.GetDirectories(gameDir, "*_Data"))
            {
                string exe = Path.GetFileName(dataDir)[..^"_Data".Length] + ".exe";
                if (File.Exists(Path.Combine(gameDir, exe))) return exe;
            }
            return null;
        }

        return Directory.GetFiles(gameDir, "*.x86_64").Select(Path.GetFileName).FirstOrDefault();
    }

    /// <summary>Downloads, verifies and extracts BepInEx into the game folder. Returns false on any failure.</summary>
    public static bool Install(string gameDir, bool dryRun, Action<string, string> log, Action<long, long?>? progress = null)
    {
        Pin? pin = ForCurrentPlatform();
        if (pin == null)
        {
            log("ERR", "Automatic BepInEx install supports Linux and Windows only. Install BepInEx 5.x manually.");
            return false;
        }

        if (dryRun)
        {
            log("DRY", $"Would download {pin.Url} and verify SHA-256 {pin.Sha256[..12]}…");
            return true;
        }

        string temp = Path.Combine(Path.GetTempPath(), "aether-bepinex-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            string zipPath = Path.Combine(temp, pin.FileName);
            log("GET", pin.Url);
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("aether-installer");
                using var response = http.GetAsync(pin.Url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
                response.EnsureSuccessStatusCode();
                long? total = response.Content.Headers.ContentLength;
                using var file = File.Create(zipPath);
                using var body = response.Content.ReadAsStream();
                var buffer = new byte[81920];
                long done = 0; int n;
                while ((n = body.Read(buffer, 0, buffer.Length)) > 0)
                {
                    file.Write(buffer, 0, n);
                    done += n;
                    progress?.Invoke(done, total);
                }
            }

            if (!Matches(zipPath, pin.Sha256))
            {
                log("ERR", $"SHA-256 mismatch for {pin.FileName}. Expected {pin.Sha256}, got {Sha256Of(zipPath)}. Nothing was installed.");
                return false;
            }
            log("OK", $"SHA-256 verified ({pin.Sha256[..12]}…).");

            SafeZip.Extract(zipPath, gameDir);
            log("OK", $"BepInEx {Version} installed.");
            return true;
        }
        catch (Exception ex)
        {
            log("ERR", $"BepInEx install failed: {ex.Message}");
            return false;
        }
        finally
        {
            try { Directory.Delete(temp, recursive: true); } catch { }
        }
    }

    /// <summary>Linux: make run_bepinex.sh executable and point it at the real game binary.</summary>
    public static void PostInstall(string gameDir, Action<string, string> log)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) return;

        string script = Path.Combine(gameDir, "run_bepinex.sh");
        if (!File.Exists(script)) return;

        string? exe = DetectExecutable(gameDir);
        if (exe != null)
        {
            string content = File.ReadAllText(script);
            if (content.Contains("executable_name=\"\""))
            {
                File.WriteAllText(script, content.Replace("executable_name=\"\"", $"executable_name=\"{exe}\""));
                log("PATCH", $"run_bepinex.sh -> executable_name=\"{exe}\"");
            }
        }

        File.SetUnixFileMode(script, File.GetUnixFileMode(script) | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        log("INFO", "On Steam (Linux) set the launch option to:  ./run_bepinex.sh %command%");
    }
}
