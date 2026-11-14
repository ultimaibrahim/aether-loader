using System.Text;
using System.Text.Json;

namespace Aether.Installer.Lattice;

/// <summary>Fetches bytes from an address. Real runs use HTTP; tests plug in a fake.</summary>
public interface ILatticeFetcher
{
    byte[] Get(string url, long maxBytes);
}

public sealed class HttpLatticeFetcher : ILatticeFetcher
{
    public byte[] Get(string url, long maxBytes)
    {
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new IOException("Only https addresses are downloaded.");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("aether-installer");
        using var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } len && len > maxBytes) throw new IOException($"{url} is larger than the {maxBytes} bytes allowed.");
        using var ms = new MemoryStream();
        using var s = response.Content.ReadAsStream();
        var buf = new byte[81920]; int n;
        while ((n = s.Read(buf, 0, buf.Length)) > 0)
        {
            ms.Write(buf, 0, n);
            if (ms.Length > maxBytes) throw new IOException($"{url} is larger than the {maxBytes} bytes allowed.");
        }
        return ms.ToArray();
    }
}

/// <summary>A verified view of the catalog: the index, the revoked hashes and how fresh the revocation list is.</summary>
public sealed class LatticeSnapshot
{
    public required LatticeIndex Index { get; init; }
    public required IReadOnlySet<string> RevokedHashes { get; init; }
    public IReadOnlyDictionary<string, string> RevokedReasons { get; init; } = new Dictionary<string, string>();
    public required long IndexSerial { get; init; }
    /// <summary>When the revocation list was last downloaded and verified (UTC).</summary>
    public required DateTime RevocationsCheckedUtc { get; init; }
    /// <summary>True when this run could not reach Lattice and used the saved, re-verified copy.</summary>
    public bool Offline { get; init; }
    public string? Warning { get; init; }

    public static readonly TimeSpan FreshFor = TimeSpan.FromHours(24);
    public bool RevocationsFresh(DateTime nowUtc) => nowUtc - RevocationsCheckedUtc <= FreshFor && nowUtc >= RevocationsCheckedUtc - TimeSpan.FromMinutes(5);
}

/// <summary>Downloads, verifies and caches the signed index and revocation list under &lt;game&gt;/Aether/lattice/.</summary>
public static class LatticeStore
{
    public const string BaseUrl = "https://lattice.ultimaibrahim.dev";
    const long MaxDocument = 8 * 1024 * 1024;

    public static string CacheDir(string gameDir) => Path.Combine(gameDir, "Aether", "lattice");

    sealed record State(long IndexSerial, long RevocationsSerial, DateTime RevocationsCheckedUtc);

    public static LatticeSnapshot? Load(string gameDir, ILatticeFetcher fetcher, byte[] publicKey, DateTime nowUtc, out string? error, string baseUrl = BaseUrl)
    {
        string dir = CacheDir(gameDir);
        var state = ReadState(dir);
        error = null;
        try
        {
            byte[] idx = fetcher.Get(baseUrl + "/index.json", MaxDocument);
            string idxSig = Encoding.ASCII.GetString(fetcher.Get(baseUrl + "/index.json.sig", 4096));
            byte[] rev = fetcher.Get(baseUrl + "/revocations.json", MaxDocument);
            string revSig = Encoding.ASCII.GetString(fetcher.Get(baseUrl + "/revocations.json.sig", 4096));

            var snap = Build(publicKey, idx, idxSig, rev, revSig, state?.IndexSerial, state?.RevocationsSerial, nowUtc, offline: false, out error);
            if (snap == null) return null;   // Fetched but not trustworthy: never fall back silently to the cache.
            Directory.CreateDirectory(dir);
            File.WriteAllBytes(Path.Combine(dir, "index.json"), idx);
            File.WriteAllText(Path.Combine(dir, "index.json.sig"), idxSig);
            File.WriteAllBytes(Path.Combine(dir, "revocations.json"), rev);
            File.WriteAllText(Path.Combine(dir, "revocations.json.sig"), revSig);
            WriteState(dir, new State(snap.IndexSerial, SerialOf(rev), nowUtc));
            return snap;
        }
        catch (Exception e) when (e is IOException or HttpRequestException or TaskCanceledException)
        {
            var cached = FromCache(dir, publicKey, state, nowUtc, out error);
            if (cached != null) return cached;
            error = $"Could not reach Lattice ({e.Message}) and there is no saved copy.";
            return null;
        }
    }

    static LatticeSnapshot? FromCache(string dir, byte[] publicKey, State? state, DateTime nowUtc, out string? error)
    {
        error = null;
        string[] files = ["index.json", "index.json.sig", "revocations.json", "revocations.json.sig"];
        if (state == null || files.Any(f => !File.Exists(Path.Combine(dir, f)))) return null;
        var snap = Build(publicKey, File.ReadAllBytes(Path.Combine(dir, files[0])), File.ReadAllText(Path.Combine(dir, files[1])),
            File.ReadAllBytes(Path.Combine(dir, files[2])), File.ReadAllText(Path.Combine(dir, files[3])),
            state.IndexSerial, state.RevocationsSerial, state.RevocationsCheckedUtc, offline: true, out error, checkedAt: state.RevocationsCheckedUtc);
        return snap;
    }

    static LatticeSnapshot? Build(byte[] key, byte[] idx, string idxSig, byte[] rev, string revSig, long? idxSeen, long? revSeen,
        DateTime checkedUtc, bool offline, out string? error, DateTime? checkedAt = null)
    {
        error = null;
        var di = SignedDocument.Open(key, idx, idxSig, idxSeen);
        if (!di.Ok) { error = Describe("The catalog (index.json)", di.Error); return null; }
        var dr = SignedDocument.Open(key, rev, revSig, revSeen);
        if (!dr.Ok) { di.Json!.Dispose(); error = Describe("The revocation list", dr.Error); return null; }
        using (di.Json) using (dr.Json)
        {
            var index = LatticeIndex.Parse(di.Json!);
            if (index == null) { error = "The catalog is signed but its content is not in the expected shape."; return null; }
            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reasons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var r in dr.Json!.RootElement.GetProperty("revoked").EnumerateArray())
                {
                    string h = r.GetProperty("sha256").GetString()!.ToLowerInvariant();
                    hashes.Add(h);
                    reasons[h] = r.TryGetProperty("reason", out var re) && re.ValueKind == JsonValueKind.String ? re.GetString()! : "";
                }
            }
            catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException) { error = "The revocation list is signed but not in the expected shape."; return null; }
            return new LatticeSnapshot
            {
                Index = index, RevokedHashes = hashes, RevokedReasons = reasons, IndexSerial = di.Serial,
                RevocationsCheckedUtc = checkedAt ?? checkedUtc, Offline = offline,
                Warning = offline ? "Lattice could not be reached; using the saved catalog." : null,
            };
        }
    }

    static string Describe(string what, SignedDocumentError e) => e switch
    {
        SignedDocumentError.BadSignature => $"{what} has an invalid signature. It was not used.",
        SignedDocumentError.Rollback => $"{what} is older than one already seen (possible rollback). It was not used.",
        _ => $"{what} is signed but malformed. It was not used.",
    };

    static long SerialOf(byte[] doc) { using var d = JsonDocument.Parse(doc); return d.RootElement.GetProperty("serial").GetInt64(); }

    static State? ReadState(string dir)
    {
        try
        {
            using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "state.json")));
            var r = d.RootElement;
            return new State(r.GetProperty("indexSerial").GetInt64(), r.GetProperty("revocationsSerial").GetInt64(),
                DateTime.Parse(r.GetProperty("revocationsCheckedUtc").GetString()!, null, System.Globalization.DateTimeStyles.RoundtripKind).ToUniversalTime());
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException or FormatException or InvalidOperationException) { return null; }
    }

    static void WriteState(string dir, State s) => File.WriteAllText(Path.Combine(dir, "state.json"),
        new System.Text.Json.Nodes.JsonObject
        {
            ["indexSerial"] = s.IndexSerial, ["revocationsSerial"] = s.RevocationsSerial, ["revocationsCheckedUtc"] = s.RevocationsCheckedUtc.ToString("O"),
        }.ToJsonString());
}
