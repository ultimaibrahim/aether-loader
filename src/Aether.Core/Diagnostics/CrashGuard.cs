using System;
using System.Collections.Concurrent;

namespace Aether.Core.Diagnostics
{
    public delegate void CrashDiagnosticHandler(string modId, string hookName, Exception exception, bool isSuspended);

    /// <summary>
    /// Crash Isolation &amp; Mod-Blame Guard.
    /// Shields the Unity game loop and Aether runtime from unhandled exceptions thrown by third-party mods.
    /// Automatically detects spamming faults and throttles or suspends faulty listeners to preserve 60+ FPS.
    /// </summary>
    public static class CrashGuard
    {
        public const int MaxFaultThreshold = 10;
        private static readonly ConcurrentDictionary<string, int> _faultCounts = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, bool> _suspendedListeners = new(StringComparer.OrdinalIgnoreCase);

        public static event CrashDiagnosticHandler? OnCrashIntercepted;

        public static bool IsSuspended(string modId, string hookName)
        {
            string key = $"{modId}::{hookName}";
            return _suspendedListeners.TryGetValue(key, out bool suspended) && suspended;
        }

        public static bool ExecuteSafe(string modId, string hookName, Action action)
        {
            string key = $"{modId}::{hookName}";
            if (IsSuspended(modId, hookName)) return false;

            try
            {
                action();
                return true;
            }
            catch (Exception ex)
            {
                HandleException(modId, hookName, key, ex);
                return false;
            }
        }

        public static TResult ExecuteSafe<TResult>(string modId, string hookName, Func<TResult> func, TResult fallback)
        {
            string key = $"{modId}::{hookName}";
            if (IsSuspended(modId, hookName)) return fallback;

            try
            {
                return func();
            }
            catch (Exception ex)
            {
                HandleException(modId, hookName, key, ex);
                return fallback;
            }
        }

        public static void ResetFaults(string modId)
        {
            foreach (var key in _faultCounts.Keys)
            {
                if (key.StartsWith($"{modId}::", StringComparison.OrdinalIgnoreCase))
                {
                    _faultCounts.TryRemove(key, out _);
                    _suspendedListeners.TryRemove(key, out _);
                }
            }
        }

        private static void HandleException(string modId, string hookName, string key, Exception ex)
        {
            int currentFaults = _faultCounts.AddOrUpdate(key, 1, (_, count) => count + 1);
            bool willSuspend = currentFaults >= MaxFaultThreshold;

            if (willSuspend)
            {
                _suspendedListeners[key] = true;
            }

            OnCrashIntercepted?.Invoke(modId, hookName, ex, willSuspend);
        }
    }
}
