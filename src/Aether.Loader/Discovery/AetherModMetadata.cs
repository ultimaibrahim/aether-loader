using System;
using System.Collections.Generic;
using Aether.Loader.SemVer;

namespace Aether.Loader.Discovery
{
    /// <summary>
    /// Metadata representation of an Aether mod manifest (aether.mod.json or [AetherMod]).
    /// </summary>
    public sealed class AetherModMetadata
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public SemanticVersion Version { get; set; } = new(1, 0, 0);
        public string Description { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string Environment { get; set; } = "client";

        /// <summary>
        /// Absolute path of the assembly that declared this mod, when it was loaded from disk.
        /// Empty for the built-in Aether Core pseudo-mod.
        /// </summary>
        public string SourcePath { get; set; } = string.Empty;

        public Dictionary<string, string> Depends { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Suggests { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> LoadAfter { get; } = new();
        public List<string> Breaks { get; } = new();

        public List<string> PreLaunchEntrypoints { get; } = new();
        public List<string> MainEntrypoints { get; } = new();
        public List<string> ClientEntrypoints { get; } = new();
        public List<string> SceneEntrypoints { get; } = new();

        public override string ToString() => $"{Name} ({Id} v{Version})";
    }

    /// <summary>
    /// Declarative code-first attribute to define an Aether mod without an external json file.
    /// The loader only discovers it at assembly level: <c>[assembly: AetherMod("id", "1.0.0")]</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public sealed class AetherModAttribute : Attribute
    {
        public string Id { get; }
        public string Version { get; }
        public string? Name { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }

        /// <summary>Comma/semicolon list: <c>mod_id@semver-range</c>. Example: <c>aether_ui@1.0.0</c>.</summary>
        public string? Depends { get; set; }

        /// <summary>Optional, non-blocking suggestions in the same format as <see cref="Depends"/>.</summary>
        public string? Suggests { get; set; }

        /// <summary>Comma/semicolon list of mod ids that must load before this one (soft order).</summary>
        public string? LoadAfter { get; set; }

        /// <summary>Comma/semicolon list of mod ids that must not be installed together with this one.</summary>
        public string? Breaks { get; set; }

        public AetherModAttribute(string id, string version)
        {
            Id = id;
            Version = version;
        }
    }
}
