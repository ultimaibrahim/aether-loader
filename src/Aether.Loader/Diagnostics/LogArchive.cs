using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Aether.Loader.Diagnostics
{
    /// <summary>
    /// Keeps a copy of this session's logs under <c>Aether/Logs/&lt;timestamp&gt;/</c> so they survive the next launch.
    /// BepInEx recreates <c>LogOutput.log</c> before any plugin runs and Unity keeps only one <c>Player-prev.log</c>, so the
    /// only way to keep the log of a session that died natively is to copy it while the session is alive. The copy is
    /// refreshed periodically, when a crash report is created and when the game quits; only the last
    /// <c>keepSessions</c> folders are kept. Pure file logic, no Unity types.
    /// </summary>
    public sealed class LogArchive
    {
        private readonly string _root;
        private readonly int _keep;
        private readonly Dictionary<string, long> _lastLength = new Dictionary<string, long>(StringComparer.Ordinal);

        public string SessionDirectory { get; }

        public LogArchive(string root, DateTime now, int keepSessions)
        {
            _root = root;
            _keep = Math.Max(1, keepSessions);
            SessionDirectory = Path.Combine(root, now.ToString("yyyyMMdd-HHmmss"));
        }

        /// <summary>Creates the session folder and prunes older ones. Never throws.</summary>
        public bool Start()
        {
            try
            {
                Directory.CreateDirectory(SessionDirectory);
                Prune();
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Copies each existing source file into the session folder under its own name, skipping files whose size has not
        /// changed since the last copy (unless <paramref name="force"/>). Sources may be open for writing by the game.
        /// Returns how many files were copied. Never throws.
        /// </summary>
        public int Sync(IEnumerable<string> sources, bool force = false)
        {
            int copied = 0;
            foreach (string source in sources)
            {
                try
                {
                    if (string.IsNullOrEmpty(source) || !File.Exists(source)) continue;
                    long length = new FileInfo(source).Length;
                    if (!force && _lastLength.TryGetValue(source, out long last) && last == length) continue;

                    string dest = Path.Combine(SessionDirectory, Path.GetFileName(source));
                    using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    using (var output = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.Read))
                        input.CopyTo(output);
                    _lastLength[source] = length;
                    copied++;
                }
                catch { /* a log being rotated or locked must never disturb the game */ }
            }
            return copied;
        }

        /// <summary>Only folders named like <c>yyyyMMdd-HHmmss</c> are ours to prune; anything else in Logs is left alone.</summary>
        public static bool IsSessionFolder(string name) =>
            name.Length == 15 && name[8] == '-' && name.Where((c, i) => i != 8).All(char.IsDigit);

        /// <summary>Deletes the oldest session folders beyond the limit, never the current one.</summary>
        public void Prune()
        {
            try
            {
                if (!Directory.Exists(_root)) return;
                var old = Directory.GetDirectories(_root)
                    .Where(d => !string.Equals(d, SessionDirectory, StringComparison.Ordinal) && IsSessionFolder(Path.GetFileName(d)))
                    .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                    .Skip(_keep - 1)
                    .ToList();
                foreach (string dir in old)
                {
                    try { Directory.Delete(dir, true); } catch { }
                }
            }
            catch { }
        }
    }
}
