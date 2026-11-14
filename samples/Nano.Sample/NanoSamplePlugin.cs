using AetherNano;
using BepInEx;
using UnityEngine;

namespace NanoSample
{
    /// <summary>Smallest useful Nano mod: proves it loads with and without Aether, and where its setting lives.</summary>
    [BepInPlugin("dev.ultimaibrahim.nanosample", "Nano Sample", "1.0.0")]
    internal sealed class NanoSamplePlugin : NanoPlugin
    {
        private NanoSetting<string> _greeting;

        protected override void OnReady()
        {
            _greeting = Bind("General", "Greeting", "hello", "Text the sample logs on start.");
            Logger.LogInfo($"[NanoSample] {_greeting.Value} from frame {Time.frameCount}. " +
                           $"Aether present: {AetherPresent}; setting stored in {(_greeting.BackedByAether ? "Aether's config" : "BepInEx's config")}.");
        }
    }
}
