using System.Text.Json;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Aether.Installer.Lattice;

public enum SignedDocumentError { None, BadSignature, Malformed, Rollback }

/// <summary>A Lattice document (index or revocation list) whose signature was checked over the exact bytes served.</summary>
public sealed record SignedDocument(SignedDocumentError Error, long Serial, JsonDocument? Json)
{
    public bool Ok => Error == SignedDocumentError.None;

    /// <summary>True when <paramref name="signatureBase64"/> is a valid Ed25519 signature of <paramref name="body"/> (never throws).</summary>
    public static bool VerifySignature(byte[] publicKey, byte[] body, string signatureBase64)
    {
        try
        {
            if (publicKey.Length != 32) return false;
            var sig = Convert.FromBase64String(signatureBase64.Trim());
            if (sig.Length != 64) return false;
            return Ed25519.Verify(sig, 0, publicKey, 0, body, 0, body.Length);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException) { return false; }
    }

    /// <summary>
    /// Signature first, then shape, then rollback: a document with a lower serial than the last one seen is refused.
    /// Same serial is accepted (the same document fetched again).
    /// </summary>
    public static SignedDocument Open(byte[] publicKey, byte[] body, string signatureBase64, long? lastSeenSerial)
    {
        if (!VerifySignature(publicKey, body, signatureBase64)) return new(SignedDocumentError.BadSignature, 0, null);
        try
        {
            var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("format", out var f) || f.ValueKind != JsonValueKind.Number || f.GetInt32() != 1
                || !root.TryGetProperty("serial", out var s) || s.ValueKind != JsonValueKind.Number || !s.TryGetInt64(out var serial) || serial < 0)
            {
                doc.Dispose();
                return new(SignedDocumentError.Malformed, 0, null);
            }
            if (lastSeenSerial is { } seen && serial < seen) { doc.Dispose(); return new(SignedDocumentError.Rollback, serial, null); }
            return new(SignedDocumentError.None, serial, doc);
        }
        catch (JsonException) { return new(SignedDocumentError.Malformed, 0, null); }
    }
}
