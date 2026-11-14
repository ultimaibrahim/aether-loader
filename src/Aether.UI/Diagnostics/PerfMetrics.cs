using System.Text;
using UnityEngine;
using UnityEngine.Profiling;

namespace Aether.UI.Diagnostics
{
    /// <summary>
    /// Shared live metrics for the split performance mods. Each engine writes its own counters;
    /// the F8 overlay, the QA runner and the shell read them from one place. This is also the
    /// evidence surface for "the optimization actually ran" (culled chunks, frozen animators,
    /// stripped shadows, applied timestep, GC passes).
    /// </summary>
    public static class PerfMetrics
    {
        public static float LiveFps { get; private set; } = 60f;
        public static float FrameTimeMs { get; private set; } = 16.6f;
        public static long AllocatedRamMb { get; private set; }

        // Sodium
        public static int SodiumTotalChunks;
        public static int SodiumVisibleChunks;
        public static int SodiumFrustumCulled;
        public static int SodiumCaveCulled;
        public static int SodiumShadowCasters;
        public static int SodiumShadowsCulled;
        public static float SodiumLastScan;

        // Lithium
        public static float LithiumFixedDelta = 0.02f;
        public static bool LithiumAutoSync = true;

        // EntityCulling
        public static int EntityTotalMobs;
        public static int EntityVisibleMobs;
        public static int EntityOffscreenCulled;
        public static int EntityWallOccluded;
        public static int EntityDistanceCulled;
        public static int EntityCulledParticles;
        public static float EntityLastScan;

        // FerriteCore
        public static int FerriteGcCount;
        public static float FerriteLastGc;
        public static bool VanillaHudSuppressed;

        // Lighting
        public static bool DuskActive;
        public static string DuskPreset = "Vanilla";

        private static float fpsTimer;
        private static int fpsCounter;

        /// <summary>Called once per frame by the shell; cheap and allocation-free.</summary>
        public static void Tick()
        {
            fpsTimer += Time.unscaledDeltaTime;
            fpsCounter++;
            if (fpsTimer < 0.25f) return;

            LiveFps = fpsCounter / fpsTimer;
            FrameTimeMs = (fpsTimer / fpsCounter) * 1000f;
            fpsCounter = 0;
            fpsTimer = 0f;
            AllocatedRamMb = Profiler.usedHeapSizeLong > 0
                ? Profiler.usedHeapSizeLong / (1024 * 1024)
                : System.GC.GetTotalMemory(false) / (1024 * 1024);
        }

        public static string BuildReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"FPS={LiveFps:F1} frame={FrameTimeMs:F2}ms RAM={AllocatedRamMb}MB");
            sb.AppendLine($"Sodium: chunks {SodiumVisibleChunks}/{SodiumTotalChunks}, " +
                          $"frustum {SodiumFrustumCulled}, cave {SodiumCaveCulled}, " +
                          $"shadow casters {SodiumShadowCasters}, shadows culled {SodiumShadowsCulled}");
            sb.AppendLine($"Lithium: autoSync={LithiumAutoSync}, fixedDelta={LithiumFixedDelta:F4}s");
            sb.AppendLine($"EntityCulling: mobs {EntityVisibleMobs}/{EntityTotalMobs}, " +
                          $"offscreen {EntityOffscreenCulled}, wall {EntityWallOccluded}, " +
                          $"distance {EntityDistanceCulled}, particles {EntityCulledParticles}");
            sb.AppendLine($"FerriteCore: GC={FerriteGcCount}, vanillaHUD={VanillaHudSuppressed}");
            sb.Append($"Lighting: active={DuskActive}, preset={DuskPreset}");
            return sb.ToString();
        }
    }
}
