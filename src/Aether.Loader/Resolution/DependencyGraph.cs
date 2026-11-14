using System;
using System.Collections.Generic;
using System.Linq;
using Aether.Loader.Discovery;
using Aether.Loader.SemVer;

namespace Aether.Loader.Resolution
{
    public class DependencyResolutionException : Exception
    {
        public DependencyResolutionException(string message) : base(message) { }
    }

    public class MissingDependencyException : DependencyResolutionException
    {
        public string ModId { get; }
        public string RequiredModId { get; }
        public string RequiredRange { get; }

        public MissingDependencyException(string modId, string reqModId, string reqRange)
            : base($"Mod '{modId}' requires '{reqModId}' ({reqRange}), but it was not found or could not be satisfied.")
        {
            ModId = modId;
            RequiredModId = reqModId;
            RequiredRange = reqRange;
        }
    }

    /// <summary>A mod left out of the load order because something it needs is missing, and why.</summary>
    public sealed class ExcludedMod
    {
        public AetherModMetadata Mod { get; }
        public string Reason { get; }
        public ExcludedMod(AetherModMetadata mod, string reason) { Mod = mod; Reason = reason; }
    }

    public class IncompatibleModException : DependencyResolutionException
    {
        public IncompatibleModException(string message) : base(message) { }
    }

    public class DependencyCycleException : DependencyResolutionException
    {
        public IReadOnlyList<string> Cycle { get; }

        public DependencyCycleException(IEnumerable<string> cycle)
            : base($"Circular dependency detected: {string.Join(" -> ", cycle)}")
        {
            Cycle = cycle.ToList();
        }
    }

    /// <summary>
    /// Directed Acyclic Graph (DAG) dependency solver with Tarjan's SCC cycle detector.
    /// Guarantees mathematically deterministic mod load orders and rejects cycle loops.
    /// </summary>
    public sealed class DependencyGraph
    {
        private readonly Dictionary<string, AetherModMetadata> _candidates = new(StringComparer.OrdinalIgnoreCase);

        public void AddCandidate(AetherModMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(metadata.Id))
                throw new ArgumentException("Mod Id cannot be empty.", nameof(metadata));

            if (_candidates.ContainsKey(metadata.Id))
                throw new InvalidOperationException($"Duplicate mod ID registered: '{metadata.Id}'");

            _candidates[metadata.Id] = metadata;
        }

        /// <summary>
        /// Like <see cref="ResolveLoadOrder"/>, but a mod whose dependency is missing or has the wrong version is left out
        /// (together with every mod that depends on it) instead of stopping everything: one broken mod must not take down
        /// the other thirty-nine. Cycles and <c>Breaks</c> conflicts still throw, as they cannot be pinned on one mod.
        /// </summary>
        public IReadOnlyList<AetherModMetadata> ResolveLoadOrderLenient(out IReadOnlyList<ExcludedMod> excluded)
        {
            var kept = new Dictionary<string, AetherModMetadata>(_candidates, StringComparer.OrdinalIgnoreCase);
            var dropped = new List<ExcludedMod>();

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var mod in kept.Values.OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).ToList())
                {
                    string? reason = null;
                    foreach (var (reqId, rangeExpr) in mod.Depends)
                    {
                        if (!kept.TryGetValue(reqId, out var target))
                            reason = _candidates.ContainsKey(reqId)
                                ? $"requires '{reqId}', which was itself left out"
                                : $"requires '{reqId}' ({rangeExpr}), which is not installed";
                        else if (!VersionRange.Parse(rangeExpr).IsSatisfiedBy(target.Version))
                            reason = $"requires '{reqId}' {rangeExpr}, but version {target.Version} is installed";
                        if (reason != null) break;
                    }
                    if (reason == null) continue;
                    kept.Remove(mod.Id);
                    dropped.Add(new ExcludedMod(mod, reason));
                    changed = true;
                }
            }

            var pruned = new DependencyGraph();
            foreach (var mod in kept.Values) pruned.AddCandidate(mod);
            excluded = dropped;
            return pruned.ResolveLoadOrder();
        }

        public IReadOnlyList<AetherModMetadata> ResolveLoadOrder()
        {
            // 1. Validate 'Breaks' / Incompatibilities
            foreach (var mod in _candidates.Values)
            {
                foreach (var breakId in mod.Breaks)
                {
                    if (_candidates.ContainsKey(breakId))
                    {
                        throw new IncompatibleModException(
                            $"Mod '{mod.Id}' is incompatible with installed mod '{breakId}' and cannot run together.");
                    }
                }
            }

            // 2. Validate 'Depends' and SemVer compatibility
            foreach (var mod in _candidates.Values)
            {
                foreach (var (reqId, rangeExpr) in mod.Depends)
                {
                    if (!_candidates.TryGetValue(reqId, out var targetMod))
                    {
                        throw new MissingDependencyException(mod.Id, reqId, rangeExpr);
                    }

                    var range = VersionRange.Parse(rangeExpr);
                    if (!range.IsSatisfiedBy(targetMod.Version))
                    {
                        throw new DependencyResolutionException(
                            $"Mod '{mod.Id}' requires '{reqId}' with version '{rangeExpr}', but found incompatible version '{targetMod.Version}'.");
                    }
                }
            }

            // 3. Build Adjacency List for DAG
            // Edge: u -> v means u depends on v, so v must load BEFORE u.
            var adj = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var mod in _candidates.Values)
            {
                adj[mod.Id] = new List<string>();
                foreach (var depId in mod.Depends.Keys)
                {
                    if (_candidates.ContainsKey(depId))
                    {
                        adj[mod.Id].Add(depId);
                    }
                }

                // Soft ordering: LoadAfter does not require a version, but the target must load first.
                foreach (var afterId in mod.LoadAfter)
                {
                    if (!string.Equals(afterId, mod.Id, StringComparison.OrdinalIgnoreCase) &&
                        _candidates.ContainsKey(afterId))
                    {
                        adj[mod.Id].Add(afterId);
                    }
                }
            }

            // 4. Tarjan's Strongly Connected Components (SCC) to detect cycles
            DetectCycles(adj);

            // 5. Topological Sort (Kahn's algorithm or post-order DFS)
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sorted = new List<AetherModMetadata>();

            void Dfs(string currentId)
            {
                if (visited.Contains(currentId)) return;
                visited.Add(currentId);

                // Visit dependencies first
                foreach (var depId in adj[currentId])
                {
                    Dfs(depId);
                }

                sorted.Add(_candidates[currentId]);
            }

            // Deterministic sort of root keys alphabetically
            foreach (var modId in _candidates.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
            {
                Dfs(modId);
            }

            return sorted;
        }

        private void DetectCycles(Dictionary<string, List<string>> adj)
        {
            int index = 0;
            var indices = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var lowlink = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stack = new Stack<string>();

            void StrongConnect(string v)
            {
                indices[v] = index;
                lowlink[v] = index;
                index++;
                stack.Push(v);
                onStack.Add(v);

                foreach (var w in adj[v])
                {
                    if (!indices.ContainsKey(w))
                    {
                        StrongConnect(w);
                        lowlink[v] = Math.Min(lowlink[v], lowlink[w]);
                    }
                    else if (onStack.Contains(w))
                    {
                        lowlink[v] = Math.Min(lowlink[v], indices[w]);
                    }
                }

                // If v is a root node, pop the stack and generate an SCC
                if (lowlink[v] == indices[v])
                {
                    var scc = new List<string>();
                    string w;
                    do
                    {
                        w = stack.Pop();
                        onStack.Remove(w);
                        scc.Add(w);
                    } while (w != v);

                    if (scc.Count > 1)
                    {
                        scc.Reverse();
                        scc.Add(scc[0]); // Complete cycle visualization
                        throw new DependencyCycleException(scc);
                    }
                }
            }

            foreach (var node in adj.Keys)
            {
                if (!indices.ContainsKey(node))
                {
                    StrongConnect(node);
                }
            }
        }
    }
}
