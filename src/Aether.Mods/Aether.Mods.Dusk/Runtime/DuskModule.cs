#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using Aether.Nano;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using com.mindblocks.i18n;
using static Aether.Nano.Nano;
using static Aether.Nano.NanoUi;

namespace Aether.Mods.Dusk
{
    /// <summary>
    /// Lighting, Shaders & Visual Atmosphere Engine Module:
    /// Re-engineers and fixes Block Story's lighting pipeline:
    /// 1. True Matte Terrain: Eliminates unnatural plastic specular highlights (_SpecColor, _Shininess, _BaseLight)
    ///    on chunk cobblestone, dirt, stone, and blocks, giving them an authentic, crisp, solid voxel look.
    /// 2. Underground Ambient & Directional Sun Suppression: Prevents directional sunlight and ambient sky glow from
    ///    bleeding through cave ceilings/mountains, plunging unlit caves into true Minecraft-style pitch darkness.
    /// 3. Zero-Ambient Player Floor: Fixes PlayerLight's hardcoded 1.0 white torch sphere when empty-handed.
    /// 4. Dynamic Hand Torchlight (OptiFine / Iris style): Warm, organic point light that illuminates walls and mobs
    ///    only when holding a Torch, Candle, Lantern, Miner Helmet, or Fire Weapon.
    /// 5. Lighting Presets & Fine-Grained ModMenu Sliders.
    /// </summary>
    public sealed partial class DuskModule
    {
        // =====================================================================
        //                         LIGHTING CONFIG ENTRIES
        // =====================================================================
        public static ConfigEntry<string> DuskPreset { get; private set; } // "MinecraftTrueDark", "VibrantMoody", "CrispDaylight", "Vanilla"
        public static ConfigEntry<float> CaveDarknessDepth { get; private set; } // 0.0 to 1.0
        public static ConfigEntry<float> BlockMatteAntiGloss { get; private set; } // 0.0 to 1.0
        public static ConfigEntry<float> MinAmbientPlayerLight { get; private set; } // 0.0 to 1.0 (Vanilla is 1.0)
        public static ConfigEntry<float> SunlightUndergroundSuppression { get; private set; } // 0.0 to 1.0
        public static ConfigEntry<float> AmbientFloorSuppression { get; private set; } // 0.0 to 1.0
        public static ConfigEntry<bool> DynamicHandTorchlight { get; private set; } // Enabled / Disabled
        public static ConfigEntry<float> DynamicTorchIntensity { get; private set; } // 0.5 to 3.0
        public static ConfigEntry<float> DynamicTorchRange { get; private set; } // 8m to 30m
        public static ConfigEntry<bool> WarmFirelightColor { get; private set; } // Rich amber vs harsh white
        public static ConfigEntry<bool> TorchFlameFlicker { get; private set; } // Organic subtle Perlin breathing

        // =====================================================================
        //                            LIGHTING STATE
        // =====================================================================
        private static float smoothCaveFactor = 0f;
        private static float lastSurfaceAmbientIntensity = -1f;
        private static float lastSurfaceReflectionIntensity = -1f;

        private static PlayerLight cachedPlayerLight = null;
        private static InvEquipment cachedEquipment = null;
        private static GameObject dynamicLightObj = null;
        private static Light dynamicLightComponent = null;

        private static float lastMaterialScanTime = 0f;
        private static float lastPlayerLightScanTime = 0f;

        private struct MaterialOriginals
        {
            public float shininess;
            public Color specColor;
            public float baseLight;
            public float lightPower;
            public bool recorded;
        }

        private static readonly Dictionary<Material, MaterialOriginals> originalMaterialProps = new Dictionary<Material, MaterialOriginals>();

        // Pre-cached warm fire colors
        private static readonly Color WarmAmberTorch = new Color(1.0f, 0.70f, 0.28f, 1.0f);
        private static readonly Color CandleSoftAmber = new Color(1.0f, 0.62f, 0.20f, 1.0f);
        private static readonly Color ElectricWhiteGold = new Color(1.0f, 0.92f, 0.65f, 1.0f);
        private static readonly Color MinerHelmetLight = new Color(1.0f, 0.95f, 0.70f, 1.0f);
        private static readonly Color FireBlazingOrange = new Color(1.0f, 0.45f, 0.12f, 1.0f);
        private static readonly Color LavaRadiantGlow = new Color(1.0f, 0.32f, 0.06f, 1.0f);
        private static readonly Color CrystalAzureGlow = new Color(0.25f, 0.85f, 1.0f, 1.0f);

        // =====================================================================
        //                         INITIALIZATION
        // =====================================================================
        public void InitDusk()
        {
            DuskPreset = Config.Bind("Dusk", "Preset", "MinecraftTrueDark",
                "Lighting preset: MinecraftTrueDark (Recommended), VibrantMoody, CrispDaylight, Vanilla.");

            CaveDarknessDepth = Config.Bind("Dusk", "CaveDarknessDepth", 0.65f,
                new ConfigDescription("Intensity of cave darkness depth.", new AcceptableValueRange<float>(0.0f, 1.0f)));

            BlockMatteAntiGloss = Config.Bind("Dusk", "BlockMatteAntiGloss", 1.0f,
                new ConfigDescription("Eliminates plastic specular sheen on cobblestone/terrain blocks. 1.0 = 100% matte voxel surface.", new AcceptableValueRange<float>(0.0f, 1.0f)));

            MinAmbientPlayerLight = Config.Bind("Dusk", "MinAmbientPlayerLight", 0.35f,
                new ConfigDescription("Minimum ambient light around player in pitch-black caves (0.35 = natural dark cave, 1.0 = Vanilla washed-out halo).", new AcceptableValueRange<float>(0.05f, 1.0f)));

            SunlightUndergroundSuppression = Config.Bind("Dusk", "SunlightUndergroundSuppression", 0.50f,
                new ConfigDescription("Softens directional specular bleed inside deep caves without darkening surface.", new AcceptableValueRange<float>(0.0f, 1.0f)));

            AmbientFloorSuppression = Config.Bind("Dusk", "AmbientFloorSuppression", 0.50f,
                new ConfigDescription("Softens ambient floor inside deep caves.", new AcceptableValueRange<float>(0.0f, 1.0f)));

            DynamicHandTorchlight = Config.Bind("Dusk", "DynamicHandTorchlight", true,
                "Dynamic Hand Lights (OptiFine / Iris style): Radiates smooth warm light around player when holding a Torch, Candle, or Fire Weapon.");

            DynamicTorchIntensity = Config.Bind("Dusk", "DynamicTorchIntensity", 1.5f,
                new ConfigDescription("Intensity multiplier for dynamic hand torchlight.", new AcceptableValueRange<float>(0.5f, 3.0f)));

            DynamicTorchRange = Config.Bind("Dusk", "DynamicTorchRange", 18.0f,
                new ConfigDescription("Light range in meters for dynamic hand torchlight.", new AcceptableValueRange<float>(8.0f, 32.0f)));

            WarmFirelightColor = Config.Bind("Dusk", "WarmFirelightColor", true,
                "Enables rich warm golden-amber firelight colors for torches and candles instead of sterile white.");

            TorchFlameFlicker = Config.Bind("Dusk", "TorchFlameFlicker", true,
                "Enables organic subtle Perlin flame breathing/flicker for hand-held lights.");

            // If a previous config saved 0.0f for MinAmbientPlayerLight (which caused pitch-black world), clamp it to safe range
            if (MinAmbientPlayerLight.Value < 0.20f)
                MinAmbientPlayerLight.Value = 0.35f;

            Log.LogInfo("[BlockStoryDusk] Lighting & Atmosphere Engine initialized.");
        }

        // =====================================================================
        //                         UPDATE TICK
        // =====================================================================
        public void UpdateDusk()
        {
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == "Menu" || currentScene == "StartScene")
            {
                if (dynamicLightComponent != null && dynamicLightComponent.enabled)
                {
                    dynamicLightComponent.enabled = false;
                }
                return;
            }

            // Periodic resolution of PlayerLight and InvEquipment
            if (Time.unscaledTime - lastPlayerLightScanTime > 1.0f)
            {
                lastPlayerLightScanTime = Time.unscaledTime;
                if (cachedPlayerLight == null)
                {
                    cachedPlayerLight = UnityEngine.Object.FindAnyObjectByType<PlayerLight>();
                }
                if (cachedEquipment == null)
                {
                    cachedEquipment = UnityEngine.Object.FindAnyObjectByType<InvEquipment>();
                }
            }

            // Periodic material matte pass
            if (Time.unscaledTime - lastMaterialScanTime > 2.0f)
            {
                lastMaterialScanTime = Time.unscaledTime;
                RefreshAllMaterials();
            }

            // Update Dynamic Hand Torchlight (OptiFine / Iris style)
            UpdateDynamicHandTorchlight();
        }

        // =====================================================================
        //                      LATE UPDATE (ATMOSPHERE PASS)
        // =====================================================================
        public void LateUpdateDusk()
        {
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene == "Menu" || currentScene == "StartScene") return;

            // Modulate only inside caves, relative to the last value the game itself produced on the
            // surface. The old code captured a one-shot "default" (which could be taken at night or
            // in a cave) and then rewrote ambientIntensity every frame, freezing day/night ambient.
            // On the surface we never write, and when the preset is Vanilla we never touch anything.
            if (DuskPreset == null || DuskPreset.Value == "Vanilla") return;

            // Evaluate cave detection ONLY when truly under a solid ceiling
            bool isCaveEnvironment = IsUnderground;
            float targetCave = isCaveEnvironment ? 1.0f : 0.0f;
            smoothCaveFactor = Mathf.MoveTowards(smoothCaveFactor, targetCave, Time.deltaTime * 1.6f);

            if (smoothCaveFactor <= 0.001f)
            {
                lastSurfaceAmbientIntensity = RenderSettings.ambientIntensity;
                lastSurfaceReflectionIntensity = RenderSettings.reflectionIntensity;
                return;
            }

            float baseAmbient = lastSurfaceAmbientIntensity >= 0f ? lastSurfaceAmbientIntensity : RenderSettings.ambientIntensity;
            float baseReflection = lastSurfaceReflectionIntensity >= 0f ? lastSurfaceReflectionIntensity : RenderSettings.reflectionIntensity;

            float ambSupp = Mathf.Clamp(smoothCaveFactor * AmbientFloorSuppression.Value, 0f, 1f);
            RenderSettings.ambientIntensity = Mathf.Lerp(baseAmbient, baseAmbient * 0.45f, ambSupp);
            RenderSettings.reflectionIntensity = Mathf.Lerp(baseReflection, baseReflection * 0.15f, ambSupp);
        }

        // =====================================================================
        //                 DYNAMIC HAND TORCHLIGHT (OPTIFINE STYLE)
        // =====================================================================
        private void UpdateDynamicHandTorchlight()
        {
            if (!DynamicHandTorchlight.Value)
            {
                if (dynamicLightComponent != null && dynamicLightComponent.enabled)
                {
                    dynamicLightComponent.enabled = false;
                }
                return;
            }

            bool isHolding = IsHoldingLightSource(out Color lightColor, out float lightIntensityMult);

            if (!isHolding)
            {
                if (dynamicLightComponent != null && dynamicLightComponent.enabled)
                {
                    dynamicLightComponent.enabled = false;
                }
                return;
            }

            // Ensure point light GameObject exists and is attached to camera/player
            if (dynamicLightObj == null || dynamicLightComponent == null)
            {
                Camera mainCam = CachedCamera != null ? CachedCamera : Camera.main;
                if (mainCam == null) return;

                dynamicLightObj = new GameObject("BlockStoryLens_DynamicTorchLight");
                dynamicLightObj.transform.SetParent(mainCam.transform, false);
                dynamicLightObj.transform.localPosition = new Vector3(0.20f, -0.15f, 0.35f); // Natural hand position

                dynamicLightComponent = dynamicLightObj.AddComponent<Light>();
                dynamicLightComponent.type = LightType.Point;
                dynamicLightComponent.shadows = LightShadows.None; // Fast and smooth
                dynamicLightComponent.renderMode = LightRenderMode.ForcePixel;
            }

            // Calculate organic intensity with subtle Perlin flame breathing
            float baseIntensity = DynamicTorchIntensity.Value * lightIntensityMult;
            if (TorchFlameFlicker.Value)
            {
                float flicker = (Mathf.PerlinNoise(Time.time * 5.5f, 0.0f) - 0.5f) * 0.12f;
                baseIntensity += flicker;
            }

            dynamicLightComponent.color = lightColor;
            dynamicLightComponent.intensity = Mathf.Max(0.1f, baseIntensity);
            dynamicLightComponent.range = DynamicTorchRange.Value;

            if (!dynamicLightComponent.enabled)
            {
                dynamicLightComponent.enabled = true;
            }
        }

        public static bool IsHoldingLightSource(out Color lightColor, out float intensityMultiplier)
        {
            lightColor = WarmAmberTorch;
            intensityMultiplier = 1.0f;

            if (cachedEquipment == null)
            {
                cachedEquipment = UnityEngine.Object.FindAnyObjectByType<InvEquipment>();
            }

            if (cachedEquipment != null)
            {
                // 1. Check Helmet / Head slot (Miner Helmet)
                InvGameItem headItem = cachedEquipment.GetItem(InvBaseItem.Slot.Hat);
                if (headItem != null && headItem.baseItem != null && !string.IsNullOrEmpty(headItem.baseItem.name))
                {
                    string hName = headItem.baseItem.name;
                    if (hName.IndexOf("Miner", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        lightColor = MinerHelmetLight;
                        intensityMultiplier = 1.30f;
                        return true;
                    }
                }

                // 2. Check Right Hand (Active Main Hand Tool/Weapon)
                if (cachedEquipment.rightHand != null && cachedEquipment.rightHand.item != null && cachedEquipment.rightHand.item.baseItem != null)
                {
                    string rName = cachedEquipment.rightHand.item.baseItem.name;
                    if (MatchLightItem(rName, out lightColor, out intensityMultiplier))
                    {
                        return true;
                    }
                }

                // 3. Check Left Hand (Offhand Shield/Torch slot)
                if (cachedEquipment.leftHand != null && cachedEquipment.leftHand.item != null && cachedEquipment.leftHand.item.baseItem != null)
                {
                    string lName = cachedEquipment.leftHand.item.baseItem.name;
                    if (MatchLightItem(lName, out lightColor, out intensityMultiplier))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool MatchLightItem(string itemName, out Color color, out float intensity)
        {
            color = WarmAmberTorch;
            intensity = 1.0f;

            if (string.IsNullOrEmpty(itemName)) return false;

            // Torch items (Hand Torch, Wall Torch, Torch)
            if (itemName.IndexOf("Torch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (itemName.IndexOf("Electric", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    color = ElectricWhiteGold;
                    intensity = 1.40f;
                }
                else
                {
                    color = WarmAmberTorch;
                    intensity = 1.15f;
                }
                return true;
            }

            // Candle
            if (itemName.IndexOf("Candle", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                color = CandleSoftAmber;
                intensity = 0.85f;
                return true;
            }

            // Fire weapons (Sword Fire, Fire Axe, etc.)
            if (itemName.IndexOf("Fire", StringComparison.OrdinalIgnoreCase) >= 0 ||
                itemName.IndexOf("Flame", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                color = FireBlazingOrange;
                intensity = 1.25f;
                return true;
            }

            // Lava bucket / molten items
            if (itemName.IndexOf("Lava", StringComparison.OrdinalIgnoreCase) >= 0 ||
                itemName.IndexOf("Magma", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                color = LavaRadiantGlow;
                intensity = 1.35f;
                return true;
            }

            // Glowing Crystals / Cryptonite
            if (itemName.IndexOf("Cryptonite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                itemName.IndexOf("Crystal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                itemName.IndexOf("Klorium", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                color = CrystalAzureGlow;
                intensity = 0.90f;
                return true;
            }

            return false;
        }

        // =====================================================================
        //                 MATERIAL MATTE & ANTI-GLOSS PIPELINE
        // =====================================================================
        public static void RefreshAllMaterials()
        {
            PruneOriginalMaterialProps();

            // 1. AtlasLoader SharedBlocksMaterial
            if (AtlasLoader.SharedBlocksMaterial != null)
            {
                ApplyMaterialAntiGloss(AtlasLoader.SharedBlocksMaterial);
            }

            // 2. DayTimeManager groundMaterials
            if (DayTimeManager.instance != null && DayTimeManager.instance.groundMaterials != null)
            {
                for (int i = 0; i < DayTimeManager.instance.groundMaterials.Length; i++)
                {
                    Material mat = DayTimeManager.instance.groundMaterials[i];
                    if (mat != null) ApplyMaterialAntiGloss(mat);
                }
            }

            // 3. GameSettings terrain materials
            GameSettings gs = UnityEngine.Object.FindAnyObjectByType<GameSettings>();
            if (gs != null)
            {
                if (gs.terrainMaterial != null) ApplyMaterialAntiGloss(gs.terrainMaterial);
                if (gs.transparentTerrainMaterial != null) ApplyMaterialAntiGloss(gs.transparentTerrainMaterial);
                if (gs.crossBlocksMaterial != null) ApplyMaterialAntiGloss(gs.crossBlocksMaterial);
            }

            // 4. Chunk ShaderLOD instances
            ShaderLOD[] chunkLods = UnityEngine.Object.FindObjectsByType<ShaderLOD>(FindObjectsSortMode.None);
            if (chunkLods != null)
            {
                for (int i = 0; i < chunkLods.Length; i++)
                {
                    ShaderLOD lod = chunkLods[i];
                    if (lod == null) continue;
                    if (lod.close != null)
                    {
                        for (int j = 0; j < lod.close.Length; j++)
                        {
                            if (lod.close[j] != null) ApplyMaterialAntiGloss(lod.close[j]);
                        }
                    }
                    if (lod.far != null)
                    {
                        for (int j = 0; j < lod.far.Length; j++)
                        {
                            if (lod.far[j] != null) ApplyMaterialAntiGloss(lod.far[j]);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Drops entries whose Unity Material was destroyed (chunk reload, LOD switch, world load).
        /// They otherwise accumulate for the whole session and pin native material memory.
        /// </summary>
        private static void PruneOriginalMaterialProps()
        {
            if (originalMaterialProps.Count == 0) return;

            List<Material> dead = null;
            foreach (Material key in originalMaterialProps.Keys)
            {
                if (key == null)
                {
                    if (dead == null) dead = new List<Material>();
                    dead.Add(key);
                }
            }

            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) originalMaterialProps.Remove(dead[i]);
        }

        private static void ApplyMaterialAntiGloss(Material mat)
        {
            if (mat == null) return;

            // Cache original material properties once
            if (!originalMaterialProps.TryGetValue(mat, out MaterialOriginals orig))
            {
                orig = new MaterialOriginals
                {
                    shininess = mat.HasProperty("_Shininess") ? mat.GetFloat("_Shininess") : 0.20f,
                    specColor = mat.HasProperty("_SpecColor") ? mat.GetColor("_SpecColor") : Color.gray,
                    baseLight = mat.HasProperty("_BaseLight") ? mat.GetFloat("_BaseLight") : 0.25f,
                    lightPower = mat.HasProperty("_LightPower") ? mat.GetFloat("_LightPower") : 1.0f,
                    recorded = true
                };
                originalMaterialProps[mat] = orig;
            }

            float matteFactor = BlockMatteAntiGloss.Value;
            float caveDepth = CaveDarknessDepth.Value;

            // 1. Specular Shininess: dial down to 0.0 (pure matte diffuse surface)
            if (mat.HasProperty("_Shininess"))
            {
                mat.SetFloat("_Shininess", Mathf.Lerp(orig.shininess, 0.0f, matteFactor));
            }

            // 2. Specular Color: dial down to black (completely eliminates plastic highlights)
            if (mat.HasProperty("_SpecColor"))
            {
                mat.SetColor("_SpecColor", Color.Lerp(orig.specColor, Color.black, matteFactor));
            }

            // 3. BaseLight: preserve authentic material base floor (never zero out)
            if (mat.HasProperty("_BaseLight"))
            {
                mat.SetFloat("_BaseLight", orig.baseLight);
            }

            // 4. Specular highlights keyword/uniform
            if (mat.HasProperty("_SpecularHighlights"))
            {
                mat.SetFloat("_SpecularHighlights", matteFactor >= 0.75f ? 0.0f : 1.0f);
            }
        }

        // =====================================================================
        //                         LIGHTING PRESETS
        // =====================================================================
        public static void ApplyDuskPreset(string presetId)
        {
            DuskPreset.Value = presetId;
            switch (presetId)
            {
                case "MinecraftTrueDark":
                    CaveDarknessDepth.Value = 0.85f;
                    BlockMatteAntiGloss.Value = 1.0f;
                    MinAmbientPlayerLight.Value = 0.35f;
                    SunlightUndergroundSuppression.Value = 0.80f;
                    AmbientFloorSuppression.Value = 0.60f;
                    DynamicHandTorchlight.Value = true;
                    WarmFirelightColor.Value = true;
                    TorchFlameFlicker.Value = true;
                    DynamicTorchIntensity.Value = 1.5f;
                    DynamicTorchRange.Value = 18.0f;
                    break;

                case "VibrantMoody":
                    CaveDarknessDepth.Value = 0.75f;
                    BlockMatteAntiGloss.Value = 0.90f;
                    MinAmbientPlayerLight.Value = 0.30f;
                    SunlightUndergroundSuppression.Value = 0.70f;
                    AmbientFloorSuppression.Value = 0.50f;
                    DynamicHandTorchlight.Value = true;
                    WarmFirelightColor.Value = true;
                    TorchFlameFlicker.Value = true;
                    DynamicTorchIntensity.Value = 1.6f;
                    DynamicTorchRange.Value = 16.0f;
                    break;

                case "CrispDaylight":
                    CaveDarknessDepth.Value = 0.60f;
                    BlockMatteAntiGloss.Value = 0.80f;
                    MinAmbientPlayerLight.Value = 0.40f;
                    SunlightUndergroundSuppression.Value = 0.50f;
                    AmbientFloorSuppression.Value = 0.40f;
                    DynamicHandTorchlight.Value = true;
                    WarmFirelightColor.Value = true;
                    TorchFlameFlicker.Value = false;
                    DynamicTorchIntensity.Value = 1.4f;
                    DynamicTorchRange.Value = 16.0f;
                    break;

                case "Vanilla":
                    CaveDarknessDepth.Value = 0.0f;
                    BlockMatteAntiGloss.Value = 0.0f;
                    MinAmbientPlayerLight.Value = 1.0f;
                    SunlightUndergroundSuppression.Value = 0.0f;
                    AmbientFloorSuppression.Value = 0.0f;
                    DynamicHandTorchlight.Value = false;
                    WarmFirelightColor.Value = false;
                    TorchFlameFlicker.Value = false;
                    DynamicTorchIntensity.Value = 1.0f;
                    DynamicTorchRange.Value = 14.0f;
                    break;
            }

            RefreshAllMaterials();
            if (cachedPlayerLight != null)
            {
                cachedPlayerLight.UpdateLight();
            }
        }

        // =====================================================================
        //                      MODMENU SETTINGS GUI
        // =====================================================================
        public void DrawDuskModMenuSettings(Rect rightRect, float baseY = 230f)
        {
            float sy = baseY;
            float rw = rightRect.width - 24f;

            // --- Header & Presets ---
            DrawShadowLabel(new Rect(10, sy, rw, 22), Tr("ATMOSPHERIC LIGHTING PRESETS", "PRESETS DE ILUMINACIÓN Y ATMÓSFERA"),
                SectionTitleStyle, new Color(1.0f, 0.85f, 0.25f), Color.black);

            sy += 26f;

            // Preset Buttons Row
            float btnW = (rw - 12f) / 4f;
            string curPreset = DuskPreset.Value;

            if (DrawNativeWoodButton(new Rect(12, sy, btnW, 34),
                Tr("Minecraft Dark\n(Recommended)", "Minecraft Oscuro\n(Recomendado)"),
                curPreset == "MinecraftTrueDark",
                new Color(1.0f, 0.85f, 0.35f, 1.0f)))
            {
                ApplyDuskPreset("MinecraftTrueDark");
            }

            if (DrawNativeWoodButton(new Rect(12 + btnW + 4f, sy, btnW, 34),
                Tr("Vibrant Moody\n(Warm Torches)", "Vibrante Moody\n(Antorchas Cálidas)"),
                curPreset == "VibrantMoody"))
            {
                ApplyDuskPreset("VibrantMoody");
            }

            if (DrawNativeWoodButton(new Rect(12 + (btnW + 4f) * 2f, sy, btnW, 34),
                Tr("Crisp Daylight\n& Deep Caves", "Día Nítido\ny Cuevas"),
                curPreset == "CrispDaylight"))
            {
                ApplyDuskPreset("CrispDaylight");
            }

            if (DrawNativeWoodButton(new Rect(12 + (btnW + 4f) * 3f, sy, btnW, 34),
                Tr("Vanilla Default\n(Original)", "Vanilla Original\n(Por Defecto)"),
                curPreset == "Vanilla",
                new Color(0.85f, 0.85f, 0.85f, 1.0f)))
            {
                ApplyDuskPreset("Vanilla");
            }

            sy += 46f;

            // --- Section: Shaders & Atmosphere Tuning ---
            DrawShadowLabel(new Rect(10, sy, rw, 22), Tr("SHADERS & CAVE ATMOSPHERE TUNING", "AJUSTES DE SHADERS Y ATMÓSFERA DE CUEVAS"),
                SectionTitleStyle, new Color(1.0f, 0.85f, 0.25f), Color.black);

            sy += 26f;

            // Slider 1: Block Matte / Anti-Gloss
            string matteLabel = $"{Tr("Block Matte / Anti-Gloss", "Bloques Mate / Anti-Brillo Plástico")}: {Mathf.RoundToInt(BlockMatteAntiGloss.Value * 100f)}% " +
                                $"{(BlockMatteAntiGloss.Value >= 0.95f ? Tr("(Pure Matte Voxel)", "(Vóxel 100% Mate)") : "")}";
            DrawShadowLabel(new Rect(12, sy, rw, 20), matteLabel, ToolStyle, Color.white, Color.black);
            float newMatte = GUI.HorizontalSlider(new Rect(12, sy + 20, rw, 18), BlockMatteAntiGloss.Value, 0.0f, 1.0f);
            if (Mathf.Abs(newMatte - BlockMatteAntiGloss.Value) > 0.01f)
            {
                BlockMatteAntiGloss.Value = newMatte;
                DuskPreset.Value = "Custom";
                RefreshAllMaterials();
            }

            sy += 42f;

            // Slider 2: Cave Darkness Depth
            string depthLabel = $"{Tr("Cave Darkness Depth", "Profundidad de Oscuridad en Cuevas")}: {Mathf.RoundToInt(CaveDarknessDepth.Value * 100f)}% " +
                                $"{(CaveDarknessDepth.Value >= 0.95f ? Tr("(Pitch Black Minecraft style)", "(Estilo Minecraft Oscuro)") : "")}";
            DrawShadowLabel(new Rect(12, sy, rw, 20), depthLabel, ToolStyle, Color.white, Color.black);
            float newDepth = GUI.HorizontalSlider(new Rect(12, sy + 20, rw, 18), CaveDarknessDepth.Value, 0.0f, 1.0f);
            if (Mathf.Abs(newDepth - CaveDarknessDepth.Value) > 0.01f)
            {
                CaveDarknessDepth.Value = newDepth;
                DuskPreset.Value = "Custom";
                RefreshAllMaterials();
            }

            sy += 42f;

            // Slider 3: Min Ambient Player Light
            string ambientLabel = $"{Tr("Empty-Hand Player Ambient Halo", "Halo de Luz al estar Desarmado")}: {Mathf.RoundToInt(MinAmbientPlayerLight.Value * 100f)}% " +
                                  $"{(MinAmbientPlayerLight.Value <= 0.05f ? Tr("(True Darkness - No Fake Glow)", "(Oscuridad Total - Sin Luz Falsa)") : "")}";
            DrawShadowLabel(new Rect(12, sy, rw, 20), ambientLabel, ToolStyle, Color.white, Color.black);
            float newAmbient = GUI.HorizontalSlider(new Rect(12, sy + 20, rw, 18), MinAmbientPlayerLight.Value, 0.0f, 1.0f);
            if (Mathf.Abs(newAmbient - MinAmbientPlayerLight.Value) > 0.01f)
            {
                MinAmbientPlayerLight.Value = newAmbient;
                DuskPreset.Value = "Custom";
                if (cachedPlayerLight != null) cachedPlayerLight.UpdateLight();
            }

            sy += 42f;

            // Slider 4: Underground Sun Suppression
            string sunLabel = $"{Tr("Underground Directional Sun Suppression", "Supresión de Luz Solar en Cuevas")}: {Mathf.RoundToInt(SunlightUndergroundSuppression.Value * 100f)}% " +
                              $"{(SunlightUndergroundSuppression.Value >= 0.95f ? Tr("(No Ceiling Bleed)", "(Sin Fugas de Luz Solar)") : "")}";
            DrawShadowLabel(new Rect(12, sy, rw, 20), sunLabel, ToolStyle, Color.white, Color.black);
            float newSun = GUI.HorizontalSlider(new Rect(12, sy + 20, rw, 18), SunlightUndergroundSuppression.Value, 0.0f, 1.0f);
            if (Mathf.Abs(newSun - SunlightUndergroundSuppression.Value) > 0.01f)
            {
                SunlightUndergroundSuppression.Value = newSun;
                DuskPreset.Value = "Custom";
            }

            sy += 42f;

            // Slider 5: Ambient Floor Suppression
            string floorLabel = $"{Tr("Global Ambient Shadow Floor Suppression", "Supresión del Suelo de Luz Ambiental")}: {Mathf.RoundToInt(AmbientFloorSuppression.Value * 100f)}% " +
                                $"{(AmbientFloorSuppression.Value >= 0.95f ? Tr("(Deep High-Contrast Shadows)", "(Sombras de Alto Contraste)") : "")}";
            DrawShadowLabel(new Rect(12, sy, rw, 20), floorLabel, ToolStyle, Color.white, Color.black);
            float newFloor = GUI.HorizontalSlider(new Rect(12, sy + 20, rw, 18), AmbientFloorSuppression.Value, 0.0f, 1.0f);
            if (Mathf.Abs(newFloor - AmbientFloorSuppression.Value) > 0.01f)
            {
                AmbientFloorSuppression.Value = newFloor;
                DuskPreset.Value = "Custom";
            }

            sy += 50f;

            // --- Section: Dynamic Hand Lights (OptiFine Style) ---
            DrawShadowLabel(new Rect(10, sy, rw, 22), Tr("DYNAMIC HAND LIGHTS (OPTIFINE / IRIS STYLE)", "LUCES DINÁMICAS EN MANO (ESTILO OPTIFINE)"),
                SectionTitleStyle, new Color(1.0f, 0.85f, 0.25f), Color.black);

            sy += 26f;

            // Dynamic Hand Torchlight Toggle
            string dynText = DynamicHandTorchlight.Value
                ? Tr("Dynamic Hand Torchlight: ENABLED", "Luz Dinámica de Antorchas en Mano: ACTIVADA")
                : Tr("Dynamic Hand Torchlight: DISABLED", "Luz Dinámica de Antorchas en Mano: DESACTIVADA");
            if (DrawNativeWoodButton(new Rect(12, sy, rw, 32), dynText, DynamicHandTorchlight.Value, new Color(1.0f, 0.75f, 0.35f, 1.0f)))
            {
                DynamicHandTorchlight.Value = !DynamicHandTorchlight.Value;
                if (!DynamicHandTorchlight.Value && dynamicLightComponent != null)
                {
                    dynamicLightComponent.enabled = false;
                }
            }

            sy += 38f;

            // Dynamic Torch Intensity & Range Sliders
            float halfW = (rw - 8f) / 2f;
            string intLabel = $"{Tr("Dynamic Intensity", "Intensidad Dinámica")}: {DynamicTorchIntensity.Value:F1}x";
            DrawShadowLabel(new Rect(12, sy, halfW, 20), intLabel, ToolStyle, Color.white, Color.black);
            float newInt = GUI.HorizontalSlider(new Rect(12, sy + 20, halfW, 18), DynamicTorchIntensity.Value, 0.5f, 3.0f);
            if (Mathf.Abs(newInt - DynamicTorchIntensity.Value) > 0.05f)
            {
                DynamicTorchIntensity.Value = newInt;
            }

            string rangeLabel = $"{Tr("Dynamic Range", "Alcance Dinámico")}: {Mathf.RoundToInt(DynamicTorchRange.Value)}m";
            DrawShadowLabel(new Rect(12 + halfW + 8f, sy, halfW, 20), rangeLabel, ToolStyle, Color.white, Color.black);
            float newRange = GUI.HorizontalSlider(new Rect(12 + halfW + 8f, sy + 20, halfW, 18), DynamicTorchRange.Value, 8.0f, 30.0f);
            if (Mathf.Abs(newRange - DynamicTorchRange.Value) > 0.5f)
            {
                DynamicTorchRange.Value = newRange;
            }

            sy += 44f;

            // Warm Firelight Color & Torch Flame Flicker Toggles
            string warmText = WarmFirelightColor.Value
                ? Tr("Warm Amber Firelight: ON", "Fuego Ámbar Cálido: SÍ")
                : Tr("Warm Amber Firelight: OFF (Vanilla White)", "Fuego Ámbar Cálido: NO (Blanco)");
            if (DrawNativeWoodButton(new Rect(12, sy, halfW, 32), warmText, WarmFirelightColor.Value))
            {
                WarmFirelightColor.Value = !WarmFirelightColor.Value;
                if (cachedPlayerLight != null) cachedPlayerLight.UpdateLight();
            }

            string flickerText = TorchFlameFlicker.Value
                ? Tr("Living Flame Flicker: ON", "Parpadeo de Llama Viva: SÍ")
                : Tr("Living Flame Flicker: OFF", "Parpadeo de Llama Viva: NO");
            if (DrawNativeWoodButton(new Rect(12 + halfW + 8f, sy, halfW, 32), flickerText, TorchFlameFlicker.Value))
            {
                TorchFlameFlicker.Value = !TorchFlameFlicker.Value;
            }

            sy += 44f;

            // Telemetry Status Card
            DrawShadowLabel(new Rect(10, sy, rw, 22), Tr("LIVE LIGHTING ENGINE STATUS", "ESTADO EN VIVO DEL MOTOR DE LUZ"),
                SectionTitleStyle, new Color(1.0f, 0.85f, 0.25f), Color.black);

            sy += 24f;
            string caveStatus = smoothCaveFactor >= 0.5f
                ? Tr("▼ Underground / Cave (Sun Suppressed)", "▼ Bajo Tierra / Cueva (Luz Solar Suprimida)")
                : Tr("▲ Open Surface / Daylight Active", "▲ Superficie Abierta / Luz Solar Activa");

            bool isHoldingSource = IsHoldingLightSource(out _, out _);
            string handSourceStatus = isHoldingSource
                ? Tr("✦ Holding Radiant Light Source", "✦ Sosteniendo Fuente de Luz Radiante")
                : Tr("▼ Empty Hand / Cold Tool (True Dark)", "▼ Mano Vacía / Herramienta Fría (Oscuridad)");

            string telemetryText = $"{Tr("Environment", "Entorno")}: {caveStatus}   |   {Tr("Cave Factor", "Factor Cueva")}: {smoothCaveFactor:P0}\n" +
                                   $"{Tr("Hand Light", "Luz en Mano")}: {handSourceStatus}   |   {Tr("Tracked Materials", "Materiales Optimizados")}: {originalMaterialProps.Count}";
            DrawShadowLabel(new Rect(12, sy, rw, 44), telemetryText, DescStyle, new Color(0.88f, 0.88f, 0.80f), Color.black);
        }
    }

    // =========================================================================
    //                        LIGHTING HARMONY PATCHES
    // =========================================================================
    [HarmonyPatch(typeof(PlayerLight), "UpdateLight")]
    public static class PlayerLight_UpdateLight_Patch
    {
        public static void Prefix(PlayerLight __instance)
        {
            if (__instance == null) return;

            // Ensure baseline minimum light is at least 0.02f so vanilla's 'red / num2' division never evaluates to 0/0 (NaN)
            // NaN uniforms on _TorchColor turn all chunks in the world pitch black!
            float safeMin = Mathf.Clamp(DuskModule.MinAmbientPlayerLight.Value * (1f / 3f), 0.02f, 1f / 3f);
            __instance.minRed = safeMin;
            __instance.minGreen = safeMin;
            __instance.minBlue = safeMin;
        }

        public static void Postfix(PlayerLight __instance)
        {
            if (__instance == null || DayTimeManager.instance == null || DayTimeManager.instance.groundMaterials == null)
                return;

            Material[] mats = DayTimeManager.instance.groundMaterials;

            // If empty-handed and player ambient light is dialed down, suppress the 15m white torch sphere
            bool isHolding = DuskModule.IsHoldingLightSource(out Color lightCol, out _);
            if (!isHolding && DuskModule.MinAmbientPlayerLight.Value <= 0.05f)
            {
                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];
                    if (mat != null)
                    {
                        mat.SetFloat("_TorchIntensity", 0.0f);
                        mat.SetColor("_TorchColor", Color.black);
                    }
                }
                return;
            }

            // Apply warm amber firelight tone to ground materials if enabled
            if (DuskModule.WarmFirelightColor.Value)
            {
                Color warmAmber = new Color(1.0f, 0.70f, 0.28f, 1.0f);
                for (int i = 0; i < mats.Length; i++)
                {
                    Material mat = mats[i];
                    if (mat != null && mat.HasProperty("_TorchColor"))
                    {
                        mat.SetColor("_TorchColor", warmAmber);
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(PlayerLight), "Start")]
    public static class PlayerLight_Start_Patch
    {
        public static void Postfix(PlayerLight __instance)
        {
            if (__instance == null) return;
            float safeMin = Mathf.Clamp(DuskModule.MinAmbientPlayerLight.Value * (1f / 3f), 0.02f, 1f / 3f);
            __instance.minRed = safeMin;
            __instance.minGreen = safeMin;
            __instance.minBlue = safeMin;
            DuskModule.RefreshAllMaterials();
        }
    }

    [HarmonyPatch(typeof(DayTimeManager), "Start")]
    public static class DayTimeManager_Start_Patch
    {
        public static void Postfix()
        {
            DuskModule.RefreshAllMaterials();
        }
    }
}
