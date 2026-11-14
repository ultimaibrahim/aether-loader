using System;
using System.Collections.Generic;

namespace Aether.Loader.SemVer
{
    /// <summary>
    /// Evaluates Semantic Versioning interval constraints (e.g. ">=1.0.0 <2.0.0", "^1.2.0", "~1.2.3", "*").
    /// </summary>
    public sealed class VersionRange
    {
        private readonly List<Func<SemanticVersion, bool>> _predicates = new();
        public string RawExpression { get; }

        private VersionRange(string rawExpression)
        {
            RawExpression = rawExpression;
        }

        public bool IsSatisfiedBy(SemanticVersion version)
        {
            if (version is null) return false;
            if (_predicates.Count == 0) return true; // wildcard matches all

            foreach (var pred in _predicates)
            {
                if (!pred(version)) return false;
            }
            return true;
        }

        public static VersionRange Parse(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression) || expression.Trim() == "*")
            {
                return new VersionRange("*");
            }

            var range = new VersionRange(expression.Trim());
            var parts = expression.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            foreach (var part in parts)
            {
                var token = part.Trim();
                if (token == "*") continue;

                if (token.StartsWith("^"))
                {
                    // Caret (npm semantics): >=1.2.3 <2.0.0, >=0.2.3 <0.3.0, >=0.0.3 <0.0.4.
                    var ver = SemanticVersion.Parse(token.Substring(1));
                    range._predicates.Add(v => v >= ver);

                    SemanticVersion maxVer;
                    if (ver.Major > 0) maxVer = new SemanticVersion(ver.Major + 1, 0, 0);
                    else if (ver.Minor > 0) maxVer = new SemanticVersion(0, ver.Minor + 1, 0);
                    else maxVer = new SemanticVersion(0, 0, ver.Patch + 1);

                    range._predicates.Add(v => v < maxVer);
                }
                else if (token.StartsWith("~"))
                {
                    // Tilde: ~1.2.3 -> >=1.2.3 <1.3.0
                    var ver = SemanticVersion.Parse(token.Substring(1));
                    range._predicates.Add(v => v >= ver);
                    var maxVer = new SemanticVersion(ver.Major, ver.Minor + 1, 0);
                    range._predicates.Add(v => v < maxVer);
                }
                else if (token.StartsWith(">="))
                {
                    var ver = SemanticVersion.Parse(token.Substring(2));
                    range._predicates.Add(v => v >= ver);
                }
                else if (token.StartsWith("<="))
                {
                    var ver = SemanticVersion.Parse(token.Substring(2));
                    range._predicates.Add(v => v <= ver);
                }
                else if (token.StartsWith(">"))
                {
                    var ver = SemanticVersion.Parse(token.Substring(1));
                    range._predicates.Add(v => v > ver);
                }
                else if (token.StartsWith("<"))
                {
                    var ver = SemanticVersion.Parse(token.Substring(1));
                    range._predicates.Add(v => v < ver);
                }
                else if (token.StartsWith("="))
                {
                    var ver = SemanticVersion.Parse(token.Substring(1));
                    range._predicates.Add(v => v == ver);
                }
                else
                {
                    // Exact version
                    var ver = SemanticVersion.Parse(token);
                    range._predicates.Add(v => v == ver);
                }
            }

            return range;
        }

        public override string ToString() => RawExpression;
    }
}
