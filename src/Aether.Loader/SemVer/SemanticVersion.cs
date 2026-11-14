using System;
using System.Text.RegularExpressions;

namespace Aether.Loader.SemVer
{
    /// <summary>
    /// Represents a strict Semantic Version 2.0.0 (Major.Minor.Patch[-Prerelease][+Build]).
    /// </summary>
    public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        private static readonly Regex SemVerRegex = new(
            @"^(?<major>\d+)(\.(?<minor>\d+))?(\.(?<patch>\d+))?(-(?<prerelease>[0-9A-Za-z\.-]+))?(\+(?<build>[0-9A-Za-z\.-]+))?$",
            RegexOptions.Compiled);

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string? Prerelease { get; }
        public string? BuildMetadata { get; }

        public SemanticVersion(int major, int minor = 0, int patch = 0, string? prerelease = null, string? buildMetadata = null)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = string.IsNullOrWhiteSpace(prerelease) ? null : prerelease;
            BuildMetadata = string.IsNullOrWhiteSpace(buildMetadata) ? null : buildMetadata;
        }

        public static SemanticVersion Parse(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                throw new ArgumentException("Version string cannot be null or whitespace.", nameof(input));

            var match = SemVerRegex.Match(input.Trim());
            if (!match.Success)
                throw new FormatException($"Invalid SemVer string: '{input}'");

            int major = int.Parse(match.Groups["major"].Value);
            int minor = match.Groups["minor"].Success ? int.Parse(match.Groups["minor"].Value) : 0;
            int patch = match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value) : 0;
            string? pre = match.Groups["prerelease"].Success ? match.Groups["prerelease"].Value : null;
            string? build = match.Groups["build"].Success ? match.Groups["build"].Value : null;

            return new SemanticVersion(major, minor, patch, pre, build);
        }

        public static bool TryParse(string input, out SemanticVersion? version)
        {
            try
            {
                version = Parse(input);
                return true;
            }
            catch
            {
                version = null;
                return false;
            }
        }

        public int CompareTo(SemanticVersion? other)
        {
            if (other is null) return 1;
            if (ReferenceEquals(this, other)) return 0;

            int cmp = Major.CompareTo(other.Major);
            if (cmp != 0) return cmp;

            cmp = Minor.CompareTo(other.Minor);
            if (cmp != 0) return cmp;

            cmp = Patch.CompareTo(other.Patch);
            if (cmp != 0) return cmp;

            // Pre-release precedence rules: normal release > pre-release
            if (Prerelease == null && other.Prerelease != null) return 1;
            if (Prerelease != null && other.Prerelease == null) return -1;
            if (Prerelease != null && other.Prerelease != null)
                return ComparePrerelease(Prerelease, other.Prerelease);

            return 0;
        }

        /// <summary>
        /// SemVer 2.0.0 §11: dot-separated identifiers, numeric identifiers compare numerically and
        /// are lower than alphanumeric ones, and a shorter set has lower precedence when all its
        /// identifiers match (beta.2 < beta.10, alpha < beta, alpha < alpha.1).
        /// </summary>
        private static int ComparePrerelease(string left, string right)
        {
            string[] a = left.Split('.');
            string[] b = right.Split('.');
            int count = Math.Min(a.Length, b.Length);

            for (int i = 0; i < count; i++)
            {
                bool aNumeric = int.TryParse(a[i], out int aNum);
                bool bNumeric = int.TryParse(b[i], out int bNum);

                int cmp;
                if (aNumeric && bNumeric) cmp = aNum.CompareTo(bNum);
                else if (aNumeric) cmp = -1;               // numeric has lower precedence
                else if (bNumeric) cmp = 1;
                else cmp = string.Compare(a[i], b[i], StringComparison.Ordinal);

                if (cmp != 0) return cmp;
            }

            return a.Length.CompareTo(b.Length);
        }

        public bool Equals(SemanticVersion? other) => CompareTo(other) == 0;

        public override bool Equals(object? obj) => obj is SemanticVersion v && Equals(v);

        public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

        public override string ToString()
        {
            var str = $"{Major}.{Minor}.{Patch}";
            if (Prerelease != null) str += $"-{Prerelease}";
            if (BuildMetadata != null) str += $"+{BuildMetadata}";
            return str;
        }

        public static bool operator ==(SemanticVersion? a, SemanticVersion? b) => a?.Equals(b) ?? b is null;
        public static bool operator !=(SemanticVersion? a, SemanticVersion? b) => !(a == b);
        public static bool operator <(SemanticVersion? a, SemanticVersion? b) => a is not null && a.CompareTo(b) < 0;
        public static bool operator <=(SemanticVersion? a, SemanticVersion? b) => a is null || a.CompareTo(b) <= 0;
        public static bool operator >(SemanticVersion? a, SemanticVersion? b) => a is not null && a.CompareTo(b) > 0;
        public static bool operator >=(SemanticVersion? a, SemanticVersion? b) => a is null ? b is null : a.CompareTo(b) >= 0;
    }
}
