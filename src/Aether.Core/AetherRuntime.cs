using System;

namespace Aether.Core
{
    /// <summary>
    /// Core runtime lifecycle manager for Aether framework.
    /// Provides zero-allocation event dispatching and module coordination.
    /// </summary>
    public static class AetherRuntime
    {
        public const string Version = "1.2.1-beta";
        public const string Codename = "Ethereal Crystal";

        public static bool IsInitialized { get; private set; }

        public static event Action? OnRuntimeInitialized;

        public static void Initialize()
        {
            if (IsInitialized) return;
            IsInitialized = true;
            OnRuntimeInitialized?.Invoke();
        }
    }
}
