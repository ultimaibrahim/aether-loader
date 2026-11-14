using System.Globalization;

namespace Aether.Installer.Lattice;

/// <summary>The Ed25519 public key that signs the Lattice index and revocation list. Embedded here; rotating it means a new loader release.</summary>
public static class LatticeKey
{
    public const string PublicKeyBase64 = "t51y3GrJPO4z0nzhBmIbMoQkLv7pv8wAS4W8+ahSyYU=";

    public static byte[] Parse(string base64)
    {
        byte[] raw;
        try { raw = Convert.FromBase64String(base64.Trim()); }
        catch (FormatException) { throw new ArgumentException("The Lattice public key is not valid base64.", nameof(base64)); }
        if (raw.Length != 32)
            throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"An Ed25519 public key is 32 bytes, got {raw.Length}."), nameof(base64));
        return raw;
    }

    public static byte[] Default => Parse(PublicKeyBase64);
}
