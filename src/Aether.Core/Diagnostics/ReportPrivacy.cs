using System;
using System.Text.RegularExpressions;

namespace Aether.Core.Diagnostics
{
    /// <summary>
    /// Removes what could point at a person from text that leaves the PC in a problem report: home folders (Windows ones can have
    /// spaces), the user name, Steam ids, e-mail and IPv4 addresses. Same rules as Lattice applies on arrival (worker/src/crash.ts).
    /// Quantifiers are bounded so a long log cannot make a regex backtrack for seconds.
    /// </summary>
    public static class ReportPrivacy
    {
        private static readonly Regex WinHome = new Regex(@"[A-Za-z]:[\\/]+(?:Users|Documents and Settings)[\\/]+[^\\/""'<>|\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex UnixHome = new Regex(@"/(?:var/home|home|Users)/[^/\s""'<>|]+", RegexOptions.Compiled);
        private static readonly Regex SteamId = new Regex(@"\b7656119\d{10}\b", RegexOptions.Compiled);
        private static readonly Regex Email = new Regex(@"[A-Za-z0-9._%+-]{1,64}@[A-Za-z0-9-]{1,63}(?:\.[A-Za-z0-9-]{1,63}){0,4}\.[A-Za-z]{2,12}\b", RegexOptions.Compiled);
        private static readonly Regex Ipv4 = new Regex(@"\b(?:\d{1,3}\.){3}\d{1,3}\b", RegexOptions.Compiled);

        public static string Scrub(string s, string? userName = null)
        {
            if (string.IsNullOrEmpty(s)) return s;
            userName ??= SafeUserName();
            s = WinHome.Replace(s, "<home>");
            s = UnixHome.Replace(s, "<home>");
            if (!string.IsNullOrEmpty(userName) && userName!.Length >= 3)
                s = Regex.Replace(s, @"\b" + Regex.Escape(userName) + @"\b", "<user>", RegexOptions.IgnoreCase);
            s = SteamId.Replace(s, "<steamid>");
            s = Email.Replace(s, "<email>");
            s = Ipv4.Replace(s, "<ip>");
            return s;
        }

        private static string SafeUserName()
        {
            try { return Environment.UserName ?? string.Empty; } catch { return string.Empty; }
        }
    }
}
