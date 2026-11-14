namespace Aether.Installer;

public enum CheckLevel { Ok, Info, Warn, Fail }

/// <summary>One line of a status or doctor report. <see cref="Badge"/> is the short tag the CLI prints ("PASS", "WARN"...).</summary>
public sealed record Check(CheckLevel Level, string Badge, string Message);

/// <summary>What a read-only report found; the front end decides how to show it.</summary>
public sealed class CheckReport
{
    public List<Check> Checks { get; } = new();

    /// <summary>False when the game folder was missing; the single check then says so.</summary>
    public bool GameFound { get; set; } = true;

    /// <summary>Problems that make the report "not clean" (doctor only).</summary>
    public int Issues { get; set; }

    /// <summary>The loader's last conflict report (BepInEx/config/aether_conflicts.txt), when one exists.</summary>
    public string? ConflictReport { get; set; }

    public void Add(CheckLevel level, string badge, string message) => Checks.Add(new Check(level, badge, message));
}
