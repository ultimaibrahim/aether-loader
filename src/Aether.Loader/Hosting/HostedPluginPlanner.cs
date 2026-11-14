using System;
using System.Collections.Generic;
using System.Linq;

namespace Aether.Loader.Hosting
{
    public sealed class HostedPluginCandidate
    {
        public string Guid { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = "0.0.0";
        public string Location { get; set; } = string.Empty;

        /// <summary>Hard dependencies: guid -> minimum version (null = any).</summary>
        public Dictionary<string, string?> HardDependencies { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Soft dependencies only influence load order when present.</summary>
        public List<string> SoftDependencies { get; } = new();

        public List<string> Incompatibilities { get; } = new();
    }

    public sealed class HostedPluginPlan
    {
        public List<HostedPluginCandidate> Order { get; } = new();
        public List<KeyValuePair<HostedPluginCandidate, string>> Rejected { get; } = new();
    }

    /// <summary>
    /// Decides which BepInEx plugins found in Aether/Mods can be started and in which order, applying
    /// the same rules as BepInEx's Chainloader (hard/soft dependencies, incompatibilities, duplicates).
    /// Pure logic with no BepInEx types, so it is unit-testable.
    /// </summary>
    public static class HostedPluginPlanner
    {
        /// <param name="candidates">Plugins discovered in Aether/Mods.</param>
        /// <param name="alreadyLoaded">guid -> version of plugins BepInEx already started (BepInEx/plugins, Aether core).</param>
        public static HostedPluginPlan Plan(
            IEnumerable<HostedPluginCandidate> candidates,
            IReadOnlyDictionary<string, string> alreadyLoaded)
        {
            var plan = new HostedPluginPlan();
            var pending = new List<HostedPluginCandidate>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (HostedPluginCandidate c in candidates)
            {
                if (alreadyLoaded.ContainsKey(c.Guid))
                    plan.Rejected.Add(new(c, $"plugin '{c.Guid}' is already loaded by BepInEx (installed twice?)"));
                else if (!seen.Add(c.Guid))
                    plan.Rejected.Add(new(c, $"duplicate plugin GUID '{c.Guid}' inside Aether/Mods"));
                else
                    pending.Add(c);
            }

            // Reject until stable: removing one plugin can invalidate the ones that need it.
            bool changed = true;
            while (changed)
            {
                changed = false;
                var alive = pending.ToDictionary(p => p.Guid, p => p, StringComparer.OrdinalIgnoreCase);

                foreach (HostedPluginCandidate c in pending.ToList())
                {
                    string? reason = FindProblem(c, alive, alreadyLoaded);
                    if (reason == null) continue;
                    plan.Rejected.Add(new(c, reason));
                    pending.Remove(c);
                    changed = true;
                    break;
                }

                if (changed) continue;

                // No per-plugin problem left: reject dependency cycles, then re-check dependents.
                HashSet<string> cyclic = FindCycleMembers(pending);
                foreach (HostedPluginCandidate c in pending.Where(p => cyclic.Contains(p.Guid)).ToList())
                {
                    plan.Rejected.Add(new(c, "is part of a dependency cycle"));
                    pending.Remove(c);
                    changed = true;
                }
            }

            plan.Order.AddRange(TopologicalOrder(pending));
            return plan;
        }

        private static string? FindProblem(
            HostedPluginCandidate c,
            Dictionary<string, HostedPluginCandidate> alive,
            IReadOnlyDictionary<string, string> loaded)
        {
            foreach (var (dep, min) in c.HardDependencies)
            {
                string? have = null;
                if (loaded.TryGetValue(dep, out string? lv)) have = lv;
                else if (alive.TryGetValue(dep, out HostedPluginCandidate? hosted)) have = hosted.Version;

                if (have == null) return $"requires '{dep}', which is not installed";
                if (min != null && Version.TryParse(min, out Version? need) &&
                    Version.TryParse(have, out Version? got) && got < need)
                    return $"requires '{dep}' >= {min}, but {have} is installed";
            }

            foreach (string bad in c.Incompatibilities)
            {
                if (loaded.ContainsKey(bad) || alive.ContainsKey(bad))
                    return $"is incompatible with '{bad}', which is installed";
            }

            return null;
        }

        private static IEnumerable<string> Edges(HostedPluginCandidate n) =>
            n.HardDependencies.Keys.Concat(n.SoftDependencies);

        /// <summary>Every plugin that sits on a dependency cycle (all members, not just one).</summary>
        private static HashSet<string> FindCycleMembers(List<HostedPluginCandidate> nodes)
        {
            var byGuid = nodes.ToDictionary(n => n.Guid, n => n, StringComparer.OrdinalIgnoreCase);
            var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // 1 visiting, 2 done
            var stack = new List<string>();
            var cyclic = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Visit(HostedPluginCandidate n)
            {
                state[n.Guid] = 1;
                stack.Add(n.Guid);
                foreach (string dep in Edges(n))
                {
                    if (!byGuid.TryGetValue(dep, out HostedPluginCandidate? d)) continue;
                    if (!state.TryGetValue(d.Guid, out int s)) Visit(d);
                    else if (s == 1)
                    {
                        for (int i = stack.LastIndexOf(d.Guid); i < stack.Count; i++) cyclic.Add(stack[i]);
                    }
                }
                stack.RemoveAt(stack.Count - 1);
                state[n.Guid] = 2;
            }

            foreach (HostedPluginCandidate n in nodes)
                if (!state.ContainsKey(n.Guid)) Visit(n);
            return cyclic;
        }

        /// <summary>Dependencies first; ties broken by GUID so the order is deterministic. Input is acyclic.</summary>
        private static IEnumerable<HostedPluginCandidate> TopologicalOrder(List<HostedPluginCandidate> nodes)
        {
            var byGuid = nodes.ToDictionary(n => n.Guid, n => n, StringComparer.OrdinalIgnoreCase);
            var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<HostedPluginCandidate>();

            void Visit(HostedPluginCandidate n)
            {
                if (!done.Add(n.Guid)) return;
                foreach (string dep in Edges(n))
                    if (byGuid.TryGetValue(dep, out HostedPluginCandidate? d)) Visit(d);
                result.Add(n);
            }

            foreach (HostedPluginCandidate n in nodes.OrderBy(n => n.Guid, StringComparer.OrdinalIgnoreCase)) Visit(n);
            return result;
        }
    }
}
