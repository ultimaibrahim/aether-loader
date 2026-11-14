using System;

namespace Aether.Registry
{
    /// <summary>
    /// Canonical namespaced identifier (namespace:path) inspired by modern Minecraft registries.
    /// Eliminates ID collisions between mods and allows dynamic runtime registration.
    /// </summary>
    public readonly struct Identifier : IEquatable<Identifier>, IComparable<Identifier>
    {
        public const string DefaultNamespace = "aether";

        public string Namespace { get; }
        public string Path { get; }

        public Identifier(string path) : this(DefaultNamespace, path) { }

        public Identifier(string @namespace, string path)
        {
            if (string.IsNullOrWhiteSpace(@namespace))
                throw new ArgumentException("Namespace cannot be empty.", nameof(@namespace));
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be empty.", nameof(path));

            Namespace = @namespace.Trim().ToLowerInvariant();
            Path = path.Trim().ToLowerInvariant();
        }

        public static Identifier Parse(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new ArgumentException("Identifier string cannot be empty.", nameof(raw));

            int colonIdx = raw.IndexOf(':');
            if (colonIdx == -1)
            {
                return new Identifier(DefaultNamespace, raw);
            }

            string ns = raw.Substring(0, colonIdx);
            string path = raw.Substring(colonIdx + 1);
            return new Identifier(ns, path);
        }

        public static bool TryParse(string raw, out Identifier id)
        {
            try
            {
                id = Parse(raw);
                return true;
            }
            catch
            {
                id = default;
                return false;
            }
        }

        public bool Equals(Identifier other) =>
            string.Equals(Namespace, other.Namespace, StringComparison.Ordinal) &&
            string.Equals(Path, other.Path, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is Identifier other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Namespace, Path);

        public int CompareTo(Identifier other)
        {
            int cmp = string.Compare(Namespace, other.Namespace, StringComparison.Ordinal);
            if (cmp != 0) return cmp;
            return string.Compare(Path, other.Path, StringComparison.Ordinal);
        }

        public override string ToString() => $"{Namespace}:{Path}";

        public static bool operator ==(Identifier left, Identifier right) => left.Equals(right);
        public static bool operator !=(Identifier left, Identifier right) => !left.Equals(right);
    }
}
