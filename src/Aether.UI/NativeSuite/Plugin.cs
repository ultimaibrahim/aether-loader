#pragma warning disable CS0618
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Aether.Loader;
using Aether.Loader.Discovery;
using Aether.Registry;
using Aether.UI;
using Aether.UI.Diagnostics;
using Aether.UI.Modules;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;
using Blocksters.MathLib;
using Blocksters.Terrain;
using Blocksters.Terrain.chunk;
using Blocksters.Terrain.renderer;
using com.mindblocks.i18n;

namespace BlockStoryLens
{
    public partial class BlockStorySuitePlugin : MonoBehaviour
    {
        public static BlockStorySuitePlugin Instance { get; private set; }
        public static bool HarmonyPatchesApplied { get; private set; }
        public static ManualLogSource Log { get; private set; }
        public ConfigFile Config { get; private set; }
        public ManualLogSource Logger { get; private set; }

        // --- HUD Layout & Vanilla Repositioning Config ---
        public static ConfigEntry<string> HudLayoutPreset { get; private set; } // "ModernModpack", "CompactLeft", "BottomHUD", "VanillaClassic", "Custom"
        public static ConfigEntry<bool> MoveDiamondsUnderXP { get; private set; }
        public static ConfigEntry<bool> MoveTopRightButtonsToBottomRight { get; private set; }

        // --- General ---
        public static ConfigEntry<KeyCode> MenuKey { get; private set; }

        // --- Durability & Rich Hover Tooltip Config ---
        public static ConfigEntry<bool> DurabilityEnabled { get; private set; }
        public static ConfigEntry<bool> HoverTooltipEnabled { get; private set; }

        // --- Modal & HUD Drag States ---
        private bool isModMenuOpen = false;
        private bool isDraggingHudElements = false;
        protected internal float modalClosedTimestamp = -10f;
        protected internal float modalOpenedTimestamp = -10f;
        public bool IsModalOpen => isModMenuOpen || isWayfinderOpen || isDraggingHudElements || isKeybindsManagerOpen || showKeybindsModal || isRecipeCardOpen
            || Aether.Nano.NanoPanels.AnyModal || (Time.unscaledTime - modalClosedTimestamp < 0.20f) || (Time.unscaledTime - Aether.Nano.NanoPanels.LastClosed < 0.20f);

        private int selectedModIndex = 0;
        private Vector2 scrollListPos;
        private Vector2 scrollDetailPos;
        private string lastSceneName = "";

        // Input & Native UI Isolation State
        private GameObject injectedMenuButton;

        // Cached Scene References
        private Digger cachedDigger;
        private Camera cachedCamera;
        private DisplayCurrency cachedDiamondHud;
        private Inventory cachedInventory;
        private float lastInventorySearchTime = 0f;
        private float lastSearchTime = 0f;
        private float lastMenuInjectionAttemptTime = 0f;

        // Cave & Underground State
        private bool cachedIsUnderground = false;
        // Caves, interiors and nearby mobs for the map are Aether Wayfinder's own (WayfinderCaves) since 1.1.0.

        // Rich Item Hover Tooltip Reflection
        private static readonly FieldInfo creativeItemField = typeof(DisplayCreativeItem).GetField("item", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private static readonly FieldInfo recipeItemField = typeof(RecipeItemClick).GetField("item", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        // Texture Cache & Procedural Textures
        private static readonly Dictionary<Color, Texture2D> colorTexCache = new Dictionary<Color, Texture2D>();

        // Reused physics buffer for the Lens entity raycast (avoids per-frame array allocation).
        // Hover-tooltip markup cache (item state + language).
        private static readonly Dictionary<string, (GUIContent Content, float Height)> richTooltipCache =
            new Dictionary<string, (GUIContent, float)>(StringComparer.Ordinal);

        // Static option tables for the settings tabs: allocated once (zero per-frame OnGUI churn).
        private static readonly Aether.Nano.NanoSegmentOption<string>[] HudPresetSegmentsEn =
        {
            new Aether.Nano.NanoSegmentOption<string>("ModernModpack", "Modern"),
            new Aether.Nano.NanoSegmentOption<string>("CompactLeft", "Compact Left"),
            new Aether.Nano.NanoSegmentOption<string>("BottomHUD", "Bottom HUD"),
            new Aether.Nano.NanoSegmentOption<string>("VanillaClassic", "Vanilla")
        };
        private static readonly Aether.Nano.NanoSegmentOption<string>[] HudPresetSegmentsEs =
        {
            new Aether.Nano.NanoSegmentOption<string>("ModernModpack", "Modern"),
            new Aether.Nano.NanoSegmentOption<string>("CompactLeft", "Izquierda"),
            new Aether.Nano.NanoSegmentOption<string>("BottomHUD", "Inferior"),
            new Aether.Nano.NanoSegmentOption<string>("VanillaClassic", "Vanilla")
        };

        private static readonly Aether.Nano.NanoSegmentOption<string>[] UiThemeRow1En =
        {
            new Aether.Nano.NanoSegmentOption<string>("Aether", "Aether Ink", Aether.Nano.NanoBrand.Ember),
            new Aether.Nano.NanoSegmentOption<string>("NativeWood", "Carved Wood", new Color(0.89f, 0.66f, 0.29f)),
            new Aether.Nano.NanoSegmentOption<string>("AncientStone", "Stone Slate", new Color(0.54f, 0.72f, 0.84f))
        };
        private static readonly Aether.Nano.NanoSegmentOption<string>[] UiThemeRow1Es =
        {
            new Aether.Nano.NanoSegmentOption<string>("Aether", "Tinta Aether", Aether.Nano.NanoBrand.Ember),
            new Aether.Nano.NanoSegmentOption<string>("NativeWood", "Madera", new Color(0.89f, 0.66f, 0.29f)),
            new Aether.Nano.NanoSegmentOption<string>("AncientStone", "Pizarra", new Color(0.54f, 0.72f, 0.84f))
        };
        private static readonly Aether.Nano.NanoSegmentOption<string>[] UiThemeRow2En =
        {
            new Aether.Nano.NanoSegmentOption<string>("DwarvenGold", "Antique Gold", new Color(0.84f, 0.61f, 0.23f)),
            new Aether.Nano.NanoSegmentOption<string>("ArcaneGlass", "Amethyst Glass", new Color(0.62f, 0.51f, 0.77f))
        };
        private static readonly Aether.Nano.NanoSegmentOption<string>[] UiThemeRow2Es =
        {
            new Aether.Nano.NanoSegmentOption<string>("DwarvenGold", "Oro Antiguo", new Color(0.84f, 0.61f, 0.23f)),
            new Aether.Nano.NanoSegmentOption<string>("ArcaneGlass", "Cristal Amatista", new Color(0.62f, 0.51f, 0.77f))
        };

        private static GUIStyle _menuRowTitleStyle, _menuRowCompactStyle, _menuRowMetaStyle, _menuTabStyle;
        private static GUIStyle _menuDetailTitleStyle, _menuDetailMetaStyle, _menuDetailDescStyle;
        private static GUIStyle _menuChipStyle, _menuInitialStyle;
        private static GUIStyle _tipTitleStyle, _tipMetaStyle, _tipBodyStyle, _tipChipStyle;
        private static readonly GUIContent _menuMeasure = new GUIContent();


        // Styles
        private GUIStyle titleStyle;
        private GUIStyle toolStyle;
        private GUIStyle subStyle;
        private GUIStyle smallStyle;
        private GUIStyle menuHeaderStyle;
        private GUIStyle menuTitleStyle;
        private GUIStyle compactNameStyle;
        private GUIStyle modNameStyle;
        private GUIStyle authorStyle;
        private GUIStyle descStyle;
        private GUIStyle sectionTitleStyle;
        private GUIStyle badgeStyle;
        private GUIStyle woodBtnTextStyle;
        private GUIStyle minimapLabelStyle;
        private GUIStyle richTooltipStyle;
        private GUIStyle textFieldStyle;
        private GUIStyle sourceStyle;

        public struct ModInfoEntry
        {
            public string Id;
            public string NameEn;
            public string NameEs;
            public string Version;
            public string Author;
            public string DescEn;
            public string DescEs;
            public string IconItemName;
            public Texture2D IconTexture;
            public string IconHint;
            public bool UseAetherCube;
            public Color AccentColor;
            public bool IsActive;

            // --- Real-mod catalog (populated from AetherBootstrap.LoadedMods) ---
            public string SourceModId;   // Aether mod id that owns this row ("" for a suite-only fallback row).
            public string SourcePath;    // Assembly path on disk, when the loader knows it.
            public string Dependencies;  // "modId range, modId range" for the details panel.
            public int LoadOrder;        // 1-based position in Aether's topological load order.
            public string[] FeatureIds;  // Native-suite settings panels reachable from this row.
            public bool IsFeatureRow;    // True when this row is a suite feature without a real mod behind it.
            public Aether.Loader.Hosting.ExternalMod External; // Third-party mod (hosted BepInEx plugin), if any.
        }

        /// <summary>
        /// The native suite features keep their rich settings panels even though they ship inside
        /// a handful of real Aether assemblies. This map groups each panel under its owning mod id.
        /// </summary>
        private static readonly Dictionary<string, string> SuiteFeatureOwners =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "lens", "aether_lens" },
                { "minimap", "aether_wayfinder" },
                { "blueprint", "aether_blueprint" },
                { "sodium", "aether_sodium" },
                { "lithium", "aether_lithium" },
                { "entityculling", "aether_entityculling" },
                { "ferritecore", "aether_ferritecore" },
                { "dusk", "aether_dusk" },
                { "durability", "aether_ui" },
                { "modmenu", "aether_ui" }
            };

        private const string HealthModId = "aether_health";
        private const string HealthFeatureId = "modhealth";

        /// <summary>
        /// Feature panels that belong to a mod assembly. They are only listed when that assembly
        /// is actually loaded; otherwise the shell would offer a settings page whose config
        /// entries are null. Wayfinder already owns its runtime; the rest follow in the split.
        /// </summary>
        private static readonly HashSet<string> OwnedFeatureMods =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "aether_wayfinder",
                "aether_blueprint",
                "aether_sodium",
                "aether_lithium",
                "aether_entityculling",
                "aether_ferritecore",
                "aether_dusk",
                "aether_lens"
            };

        private readonly List<ModInfoEntry> suiteFeatures = new List<ModInfoEntry>();
        private readonly List<ModInfoEntry> registeredMods = new List<ModInfoEntry>();
        private readonly Dictionary<string, string> selectedFeatureByMod =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string selectedFeatureId = string.Empty;

        public static bool IsSpanish() => Aether.Nano.Nano.IsSpanish();

        public static string Tr(string en, string es)
        {
            return IsSpanish() ? es : en;
        }

        private void Awake()
        {
            Instance = this;
            Logger = BepInEx.Logging.Logger.CreateLogSource("Aether.Suite");
            Log = Logger;
            string suiteCfg = System.IO.Path.Combine(Paths.ConfigPath, "com.ultimaibrahim.blockstory.suite.cfg");
            Aether.Core.Config.LegacyNames.MigrateConfigFile(suiteCfg); // 2026-11-05 mod rename: keep the user's settings
            Config = new ConfigFile(suiteCfg, true);

            // Config HUD Layout Presets & Repositioning
            HudLayoutPreset = Config.Bind("HUDLayout", "Preset", "ModernModpack", "Global HUD layout preset: ModernModpack, CompactLeft, BottomHUD, VanillaClassic, Custom.");
            MoveDiamondsUnderXP = Config.Bind("HUDLayout", "MoveDiamondsUnderXP", true, "Move native Diamond counter subtly right underneath the XP/Health/Mana bars at Top-Left.");
            MoveTopRightButtonsToBottomRight = Config.Bind("HUDLayout", "MoveTopRightButtonsToBottomRight", true, "Move native top-right button row to bottom-right above action buttons, freeing top-right for minimap.");

            // Config General
            MenuKey = Config.Bind("General", "MenuKey", KeyCode.F10, "Hotkey that opens the Block Story QoL suite menu.");
            Aether.UI.Diagnostics.ProblemReports.Bind(Config);

            // Config Lens
            // Lens owns its [Lens] section (Aether.Mods.Lens).

            // Config Durability & Hover Tooltip
            DurabilityEnabled = Config.Bind("Durability", "Enabled", true, "Show numeric durability and percentage on item tooltips.");
            HoverTooltipEnabled = Config.Bind("Durability", "HoverTooltipEnabled", true, "Show floating rich tooltip with item name, stats, durability and origin on slot hover.");

            // Attach the per-mod runtimes. Wayfinder's assembly registers itself from its
            // IClientModInitializer; the shell only forwards lifecycle and draws the shared chrome.
            AetherUiModuleRegistry.Attach(AttachUiModule);
            ConnectNano();

            InitKeybindManager();
            InitBlueprintAndSodium();
            HudCustomizerModule.InitHudCustomizer(Config);
            InitDusk();
            InitQaDiagnostics();

            try
            {
                var harmony = new Harmony("com.ultimaibrahim.blockstory.suite");
                harmony.PatchAll();
                HarmonyPatchesApplied = true;
                Log.LogInfo("Harmony patches applied successfully!");
            }
            catch (Exception ex)
            {
                Log.LogError("Error applying Harmony patches: " + ex);
            }

            InitModList();
            Log.LogInfo("Block Story QoL Suite v1.6.0 (Lens, Rich Hover Tooltips, Wayfinder, Dual ModMenu, HUD Repositioner, Blueprint, Keybinds, Dusk) initialized!");
        }

        /// <summary>Single per-frame update loop for every registered mod runtime.</summary>
        private void UpdateUiModules()
        {
            PerfMetrics.Tick();
            var modules = AetherUiModuleRegistry.All;
            for (int i = 0; i < modules.Count; i++)
            {
                IAetherUiModule module = modules[i];
                if (module == null || !module.IsInitialized) continue;
                try { module.OnUpdate(); }
                catch (Exception ex) { Log.LogWarning($"[Aether] UI module '{module.Id}' update failed: {ex.Message}"); }
            }
        }

        private void LateUpdateUiModules()
        {
            var modules = AetherUiModuleRegistry.All;
            for (int i = 0; i < modules.Count; i++)
            {
                IAetherUiModule module = modules[i];
                if (module == null || !module.IsInitialized) continue;
                try { module.OnLateUpdate(); }
                catch (Exception ex) { Log.LogWarning($"[Aether] UI module '{module.Id}' late-update failed: {ex.Message}"); }
            }
        }

        private void NotifyUiSceneChanged(string sceneName)
        {
            var modules = AetherUiModuleRegistry.All;
            for (int i = 0; i < modules.Count; i++)
            {
                IAetherUiModule module = modules[i];
                if (module == null) continue;
                try { module.OnSceneChanged(sceneName); }
                catch (Exception ex) { Log.LogWarning($"[Aether] UI module '{module.Id}' scene-change failed: {ex.Message}"); }
            }
        }

        /// <summary>
        /// Initializes one runtime module, isolating failures so a broken mod cannot abort the
        /// suite or the other modules.
        /// </summary>
        private void AttachUiModule(IAetherUiModule module)
        {
            try
            {
                module.Initialize();
            }
            catch (Exception ex)
            {
                Log.LogError($"[Aether] UI module '{module.Id}' failed to initialize: {ex.GetType().Name}: {ex.Message}");
            }
        }

        public GUIStyle GetWoodBtnTextStyle() => woodBtnTextStyle;
        public GUIStyle GetSmallStyle() => smallStyle;
        /// <summary>
        /// Single entry point for HUD drag mode. Every toggle (mod-menu button, [F9] hotkey,
        /// keybind/map modals) must go through here so input isolation is always symmetric.
        /// Setting <c>isDraggingHudElements</c> directly leaves NGUI cameras disabled or
        /// <c>Inventory.allowInput</c> stuck at false.
        /// </summary>
        public void SetDraggingHudElements(bool dragging)
        {
            if (isDraggingHudElements == dragging) return;

            if (dragging)
            {
                // Release the isolation of whatever modal we are coming from BEFORE enabling drag
                // mode, otherwise AcquireModalIsolation() would clear disabledCameras without
                // re-enabling the cameras it recorded (dead vanilla UI for the rest of the session).
                if (isModMenuOpen) SetModMenuOpen(false);
                if (isWayfinderOpen) SetWayfinderOpen(false);
                if (isKeybindsManagerOpen) SetKeybindsManagerOpen(false);

                isDraggingHudElements = true;
                EnforceNativeGameCursor();
                Inventory.allowInput = false;
                Inventory.isPaused = true;
            }
            else
            {
                isDraggingHudElements = false;
                RestoreGameplayInput(false);
            }
        }
        public void SetModMenuOpenState(bool open) { SetModMenuOpen(open); }

        public static void ApplyHudPreset(string preset)
        {
            HudCustomizerModule.ApplyHudPreset(preset);
        }

        private void InitModList()
        {
            InitSuiteFeatures();
            RefreshModCatalog();
        }

        /// <summary>
        /// Defines the native suite settings panels. These are *features* hosted by Aether
        /// assemblies; <see cref="RefreshModCatalog"/> groups them under their real loaded mod.
        /// </summary>
        private void InitSuiteFeatures()
        {
            suiteFeatures.Clear();

            suiteFeatures.Add(new ModInfoEntry
            {
                Id = "lens",
                NameEn = "Lens (WAILA)",
                NameEs = "Lens (WAILA)",
                Version = "1.6.0",
                Author = "UltimaIbrahim / Antigravity",
                IconItemName = "Snow",
                AccentColor = new Color(0.25f, 0.85f, 0.45f),
                IsActive = true,
                DescEn = "Displays an authentic info card showing the exact block or creature you are aiming at:\n\n" +
                         "• Respects the game's configured language (English, Spanish, etc.).\n" +
                         "• Renders authentic 2D block icons resolved by block name.\n" +
                         "• Shows optimal harvesting tool (Shovel, Axe, Pickaxe, or Hand).\n" +
                         "• Displays creature health bar and level.\n" +
                         "• Deterministic voxel raycasting via RayTracer.trace.\n" +
                         "• 4 Screen Anchors + 5 Design Themes + Pixel-perfect Offsets & HUD Drag Mode.",
                DescEs = "Muestra una tarjeta nativa con la información del bloque o criatura al que apuntas:\n\n" +
                         "• Respeta el idioma configurado en el juego (Inglés, Español, etc.).\n" +
                         "• Renderiza iconos 2D reales de bloques por nombre.\n" +
                         "• Muestra la herramienta óptima (Pala, Hacha, Pico o Mano).\n" +
                         "• Barra de salud y nivel de criaturas.\n" +
                         "• Raycast de vóxeles determinista con RayTracer.trace.\n" +
                         "• 4 Anclajes de Pantalla + 5 Temas de Diseño + Modo Arrastre de HUD."
            });

            suiteFeatures.Add(new ModInfoEntry
            {
                Id = "durability",
                NameEn = "Tooltips & Durability",
                NameEs = "Tooltips y Durabilidad",
                Version = "1.6.0",
                Author = "UltimaIbrahim / Antigravity",
                IconItemName = "Sword Iron",
                AccentColor = new Color(0.85f, 0.35f, 0.80f),
                IsActive = true,
                DescEn = "Adds a real-time floating RPG tooltip whenever you hover the cursor over any item slot (Inventory, Hotbar, Equipment, Chests, Crafting, Creative, Recipe Book):\n\n" +
                         "• Displays 2D Item Icon, translated Name, Category, and Equipment Slot.\n" +
                         "• Shows exact remaining durability (topStack / max) with percentage and visual progress bar.\n" +
                         "• Lists weapon damage, mining speed, armor, and magical effects.\n" +
                         "• Shows item origin ('Block Story') at the bottom.",
                DescEs = "Añade un tooltip flotante en tiempo real al pasar el cursor sobre cualquier ranura de ítem (Inventario, Hotbar, Equipo, Cofres, Crafteo, Creativo, Recetas):\n\n" +
                         "• Muestra icono 2D, nombre traducido, categoría y ranura de equipo.\n" +
                         "• Muestra la durabilidad restante real (topStack / max) con porcentaje y barra visual.\n" +
                         "• Lista daño de ataque, velocidad de minado, armadura y efectos mágicos.\n" +
                         "• Muestra el origen del ítem ('Block Story') abajo."
            });

            suiteFeatures.Add(new ModInfoEntry
            {
                Id = "minimap",
                NameEn = "Wayfinder",
                NameEs = "Wayfinder",
                Version = "1.6.0",
                Author = "UltimaIbrahim / Antigravity",
                IconItemName = "Compass",
                AccentColor = new Color(0.95f, 0.60f, 0.20f),
                IsActive = true,
                DescEn = "Full topographical Wayfinder with multi-layer cave & slice rendering, circular or square shapes, and directional player arrow:\n\n" +
                         "• Multi-Layer Mode: Auto/Surface/Cave/Slice with real-time underground ceiling detection, glowing lava/acid, and vibrant ore veins.\n" +
                         "• Directional Player Arrow (rotating North/East/South/West) on minimap and fullscreen map.\n" +
                         "• Real 69-Biome Detection via game environment engine.\n" +
                         "• Interactive Fullscreen Map (M): smooth mouse wheel zoom with anchored cursor, click+drag pan, and double-click waypoint creation!\n" +
                         "• Full Waypoint manager with 3D world beacons and coordinates readout.",
                DescEs = "Minimapa topográfico completo estilo Wayfinder con renderizado de cuevas multicapa, forma circular o cuadrada y flecha direccional:\n\n" +
                         "• Modo Multicapa: Auto/Superficie/Cueva/Corte con detección de techos sólidos, lava/ácido brillantes y vetas de minerales vivas.\n" +
                         "• Flecha direccional del jugador con rotación suave en minimapa y mapa completo.\n" +
                         "• Detección real de los 69 biomas mediante el motor de entorno.\n" +
                         "• Mapa Completo Interactivo (M): zoom suave con rueda del ratón anclado al cursor, arrastre para mover y doble clic para crear waypoints!\n" +
                         "• Gestor completo de waypoints con balizas 3D en el mundo y coordenadas."
            });

            suiteFeatures.Add(new ModInfoEntry
            {
                Id = "blueprint",
                NameEn = "Blueprint (Recipe Browser)",
                NameEs = "Blueprint (Navegador de Recetas)",
                Version = "1.6.0",
                Author = "UltimaIbrahim / Antigravity",
                IconItemName = "Crafting Table",
                AccentColor = new Color(0.90f, 0.40f, 0.25f),
                IsActive = true,
                DescEn = "Roughly Enough Items (Blueprint) complete recipe browser and item catalog overlay:\n\n" +
                         "• Automatically opens on the right side of the screen with Inventory/Crafting (or toggle with O).\n" +
                         "• Fast search bar with bilingual English/Spanish matching and category filter tabs.\n" +
                         "• Paginated item grid with smooth mouse wheel scrolling and 2D UIAtlas icons.\n" +
                         "• Recipe Lookup (R / Left-Click): 3x3 crafting grid with ingredient counts and recursive navigation!\n" +
                         "• Uses Lookup (U / Right-Click): lists all recipes that require the item as an ingredient.\n" +
                         "• Bookmarks (A / Pin button): quick-access strip for favorite recipes and materials.",
                DescEs = "Navegador completo de recetas y catálogo de ítems estilo Roughly Enough Items (Blueprint):\n\n" +
                         "• Se abre automáticamente a la derecha junto al Inventario/Mesa (o alternar con O).\n" +
                         "• Buscador rápido bilingüe con filtros por categorías (Bloques, Armas, Herramientas, etc.).\n" +
                         "• Cuadrícula paginada con scroll suave de ratón e iconos 2D nativos de UIAtlas.\n" +
                         "• Ver Recetas (R / Clic Izq): matriz de crafteo 3x3 con cantidades y navegación recursiva!\n" +
                         "• Ver Usos (U / Clic Der): lista todas las recetas que usan este ítem como ingrediente.\n" +
                         "• Marcadores (A / Botón Pin): barra de acceso rápido a recetas y materiales favoritos."
            });

            suiteFeatures.Add(new ModInfoEntry
            {
                Id = "sodium",
                NameEn = "Sodium Render",
                NameEs = "Render Sodium",
                Version = "1.6.0",
                Author = "UltimaIbrahim / Antigravity",
                IconItemName = "Diamond",
                AccentColor = new Color(0.20f, 0.85f, 0.85f),
                IsActive = true,
                DescEn = "Chunk render engine:\n\n" +
                         "• Chunk Shadow Culling: strips shadow casting from distant (>48m) and underground chunks, eliminating thousands of shadow pass draw calls per frame.\n" +
                         "• Vertical Cave Occlusion: culls surface chunks when deep in mines, and culls bedrock chunks when exploring the surface.\n" +
                         "• Camera Frustum Culling: rapid AABB chunk bounds testing against camera view planes.\n" +
                         "• Spherical Detail Culling: smooth fog distance matching preventing chunk popping.\n" +
                         "• Water Reflection Optimizer: optimizes or disables secondary reflection cameras in ReflectiveWater to eliminate double rendering.",
                DescEs = "Motor de renderizado vóxel de nueva generación para Block Story:\n\n" +
                         "• Culling de Sombras de Chunks: desactiva el render de sombras en chunks lejanos (>48m) y subterráneos, ahorrando miles de draw calls por frame.\n" +
                         "• Oclusión Vertical de Cuevas: oculta chunks de superficie al minar en cuevas profundas, y oculta el subsuelo al explorar la superficie.\n" +
                         "• Culling de Frustum de Cámara: descarte instantáneo de chunks fuera del cono de visión.\n" +
                         "• Culling Esférico de Detalle: sincronización suave con la niebla para evitar parpadeos.\n" +
                         "• Optimizador de Agua Reflectiva: optimiza o desactiva las cámaras secundarias de ReflectiveWater para evitar renderizar el mundo dos veces."
            });

            suiteFeatures.Add(new ModInfoEntry
            {
                Id = "lithium",
                NameEn = "Lithium Physics",
                NameEs = "Física Lithium",
                Version = "1.6.0",
                Author = "UltimaIbrahim / Antigravity",
                IconItemName = "Iron Ingot",
                AccentColor = new Color(0.95f, 0.40f, 0.20f),
                IsActive = true,
                DescEn = "Physics simulation and timestep optimization suite:\n\n" +
                         "• Crash-safe PhysX AutoSync: Physics.autoSyncTransforms is always forced ON so the player's CharacterController never falls through freshly generated chunk colliders.\n" +
                         "• Configurable Physics Timestep: 60Hz, 50Hz, 40Hz, or 30Hz fixed simulation rate to save CPU on laptops/mobile chips.\n\n" +
                         "Chunk-tick throttling and AI/spawner pacing are reserved features and are not implemented yet.",
                DescEs = "Suite de optimización de simulación física y paso de tiempo:\n\n" +
                         "• AutoSync PhysX Seguro: autoSyncTransforms se fuerza siempre a ON para que el CharacterController nunca atraviese los colliders de chunks recién generados.\n" +
                         "• Paso de Simulación Configurable: 60Hz, 50Hz, 40Hz o 30Hz para ahorrar CPU en procesadores portátiles.\n\n" +
                         "El limitador de ticks de chunks y el ritmo de IA/spawners son funciones reservadas y aún no están implementadas."
            });

            suiteFeatures.Add(new ModInfoEntry
            {
                Id = "entityculling",
                NameEn = "EntityCulling",
                NameEs = "EntityCulling",
                Version = "1.6.0",
                Author = "UltimaIbrahim / Antigravity",
                IconItemName = "Eye of Ender",
                AccentColor = new Color(0.35f, 0.85f, 0.45f),
                IsActive = true,
                DescEn = "Deep occlusion culling and off-screen animation freezer for mobs & entities:\n\n" +
                         "• Frustum Culling: disables SkinnedMeshRenderer bone recomputations and sets Animator.cullingMode = CullCompletely for off-screen mobs.\n" +
                         "• Solid Voxel Wall Occlusion: raycasts through voxel terrain to detect mobs occluded behind solid rock/cave walls and freezes their animators.\n" +
                         "• Extreme Distance Culler: completely disables mob renderers and animators beyond configurable distance (default 50m).\n" +
                         "• Particle & Effect Culler: pauses distant particle systems to preserve fillrate.",
                DescEs = "Culling de oclusión profunda y congelación de animaciones fuera de pantalla:\n\n" +
                         "• Culling de Frustum: desactiva el cálculo de huesos en SkinnedMeshRenderer y congela animadores de mobs fuera de pantalla.\n" +
                         "• Oclusión por Muros Vóxel: trazado de rayos por el terreno para congelar mobs ocultos tras paredes de piedra en cuevas.\n" +
                         "• Culling por Distancia Extrema: apaga renderizado y animaciones de mobs más allá de la distancia configurada (50m por defecto).\n" +
                         "• Culling de Partículas y Efectos: pausa sistemas de partículas lejanos para ahorrar fillrate en GPU."
            });

            suiteFeatures.Add(new ModInfoEntry
            {
                Id = "ferritecore",
                NameEn = "FerriteCore",
                NameEs = "FerriteCore",
                Version = "1.6.0",
                Author = "UltimaIbrahim / Antigravity",
                IconItemName = "Chest",
                AccentColor = new Color(0.75f, 0.45f, 0.95f),
                IsActive = true,
                DescEn = "Low-spike memory management, stale chunk cleanup, and clean telemetry HUD:\n\n" +
                         "• Low-Spike Menu GC: automatically runs garbage collection during Inventory and Pause screens so gameplay never stutters.\n" +
                         "• Stale Chunk Garbage Collector: unloads abandoned chunk references and frees unmanaged assets.\n" +
                         "• Suppress Vanilla HUDFPS: automatically hides Block Story's native green debug text (49 FPS / 678 Chunks).\n" +
                         "• Telemetry overlay (F8): semi-transparent metrics showing FPS, frame time, RAM, chunks, and culled entities.",
                DescEs = "Gestión de memoria sin tirones, limpieza de chunks y HUD de telemetría limpio:\n\n" +
                         "• GC Sin Tirones en Menús: ejecuta recolección de basura al abrir inventario o pausa para evitar caídas de FPS en combate.\n" +
                         "• Limpiador de Chunks Obsoletos: descarga chunks abandonados y libera memoria no utilizada.\n" +
                         "• Suprimir HUDFPS Vanilla: oculta automáticamente el texto verde nativo del juego (49 FPS / 678 Chunks).\n" +
                         "• Overlay de telemetría (F8): métricas translúcidas con FPS, tiempo de frame, RAM, chunks y entidades optimizadas."
            });

            suiteFeatures.Add(new ModInfoEntry
            {
                Id = "modmenu",
                NameEn = "HUD & Keybinds",
                NameEs = "HUD y Teclas",
                Version = "1.6.0",
                Author = "UltimaIbrahim / Antigravity",
                IconItemName = "Book",
                AccentColor = new Color(0.30f, 0.65f, 0.95f),
                IsActive = true,
                DescEn = "Complete HUD Customization and Keybind Manager suite:\n\n" +
                         "• Full Unified Keybind Manager (rebind any vanilla or mod suite key with conflict warnings).\n" +
                         "• Global HUD Layout Presets: ModernModpack, CompactLeft, BottomHUD, VanillaClassic.\n" +
                         "• Interactive HUD Drag Mode: drag Lens, Minimap, Stats, Diamonds and Actions anywhere.\n" +
                         "• UI Design Themes: Native Wood, Ancient Stone, Dwarven Gold, Arcane Glass.\n" +
                         "• Accessible from Pause Menu and Main Menu.",
                DescEs = "Suite completa de personalización de HUD y gestor de teclas:\n\n" +
                         "• Gestor de Teclas Unificado (reasigna cualquier tecla vanilla o de mods con avisos de conflictos).\n" +
                         "• Presets de Diseño de HUD: ModernModpack, CompactLeft, BottomHUD, VanillaClassic.\n" +
                         "• Modo de Arrastre Interactivo: mueve Lens, Minimapa, Estadísticas, Diamantes y Acciones.\n" +
                         "• Temas Visuales: Madera Nativa, Piedra Antigua, Oro Enano, Cristal Arcano.\n" +
                         "• Accesible desde el Menú de Pausa y Menú Principal."
            });

            suiteFeatures.Add(new ModInfoEntry
            {
                Id = "dusk",
                NameEn = "Dusk",
                NameEs = "Dusk",
                Version = "1.6.0",
                Author = "UltimaIbrahim / Antigravity",
                IconItemName = "Torch",
                AccentColor = new Color(1.0f, 0.70f, 0.20f),
                IsActive = true,
                DescEn = "Complete lighting, shader & atmosphere engine overhaul:\n\n" +
                         "• True Matte Terrain: Eliminates unnatural plastic specular highlights on cobblestone, stone, and dirt.\n" +
                         "• Underground Sunlight Suppression: Prevents directional sun/moon bleed-through in caves and dungeons.\n" +
                         "• Minecraft True Dark: Authentic pitch-black unlit caves with no artificial ambient halo when unarmed.\n" +
                         "• Dynamic Hand Torchlight (OptiFine/Iris): Radiates warm, atmospheric firelight when holding a torch or candle.\n" +
                         "• Fine-grained atmosphere tuning sliders & quick lighting presets.",
                DescEs = "Revisión completa de la iluminación, shaders y atmósfera del juego:\n\n" +
                         "• Bloques Mate Reales: Elimina el brillo plástico artificial en adoquines, piedra y tierra.\n" +
                         "• Supresión de Luz Solar en Cuevas: Evita que el sol y la luna atraviesen techos y montañas.\n" +
                         "• Oscuridad Total Minecraft: Cuevas 100% oscuras y atmosféricas sin halos artificiales sin antorcha.\n" +
                         "• Luz Dinámica en Mano (estilo OptiFine/Iris): Emite luz de fuego cálida al sostener antorchas o velas.\n" +
                         "• Controles deslizantes detallados y presets rápidos de iluminación."
            });

            // Attach ownership + settings-panel identity to every feature definition.
            for (int i = 0; i < suiteFeatures.Count; i++)
            {
                var feature = suiteFeatures[i];
                feature.FeatureIds = new[] { feature.Id };
                feature.IsFeatureRow = true;
                if (SuiteFeatureOwners.TryGetValue(feature.Id, out string owner))
                    feature.SourceModId = owner;
                suiteFeatures[i] = feature;
            }
        }

        // =====================================================================
        //                REAL MOD CATALOG (AetherBootstrap.LoadedMods)
        // =====================================================================

        /// <summary>
        /// Rebuilds the native menu list from the mods the Aether loader actually resolved.
        /// Built-in suite panels are grouped under the real assembly that ships them; third-party
        /// mods appear with their real name/version/author/description/path and no settings panel.
        /// </summary>
        private void RefreshModCatalog()
        {
            string previousId = null;
            if (selectedModIndex >= 0 && selectedModIndex < registeredMods.Count)
                previousId = registeredMods[selectedModIndex].Id;

            registeredMods.Clear();

            try
            {
                var loaded = AetherBootstrap.LoadedMods;
                if (loaded != null)
                {
                    for (int i = 0; i < loaded.Count; i++)
                    {
                        var meta = loaded[i];
                        if (meta == null) continue;
                        registeredMods.Add(BuildRealModEntry(meta, i));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[Aether] Native mod catalog could not read LoadedMods: {ex.Message}");
            }

            // Third-party & Nano mods Aether does not load through [AetherMod]: BepInEx plugins, hosted or started by BepInEx.
            // Ordered cleanly by origin tier (By Aether -> Nano -> Third-party) so official mods stay right near the top.
            try
            {
                var extList = new List<Aether.Loader.Hosting.ExternalMod>(Aether.Loader.Hosting.ExternalModCatalog.Snapshot());
                extList.Sort((a, b) =>
                {
                    int tierA = OriginSortTier(a);
                    int tierB = OriginSortTier(b);
                    int cmp = tierA.CompareTo(tierB);
                    if (cmp != 0) return cmp;
                    return string.Compare(
                        Aether.Loader.Hosting.ExternalModCatalog.DisplayName(a.Name),
                        Aether.Loader.Hosting.ExternalModCatalog.DisplayName(b.Name),
                        StringComparison.OrdinalIgnoreCase);
                });

                int order = registeredMods.Count;
                foreach (var ext in extList)
                    registeredMods.Add(BuildExternalEntry(ext, order++));

                // Safety net: any NanoPage registered outside ExternalModCatalog still appears in the menu.
                var pages = Aether.Nano.NanoPages.All;
                for (int p = 0; p < pages.Count; p++)
                {
                    var page = pages[p];
                    if (page == null || string.IsNullOrEmpty(page.Id)) continue;
                    bool alreadyListed = false;
                    for (int r = 0; r < registeredMods.Count; r++)
                    {
                        if (string.Equals(registeredMods[r].Id, page.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyListed = true;
                            break;
                        }
                    }
                    if (!alreadyListed)
                    {
                        var synthetic = new Aether.Loader.Hosting.ExternalMod
                        {
                            Id = page.Id,
                            Name = string.IsNullOrWhiteSpace(page.Name) ? page.Id : page.Name,
                            Version = string.IsNullOrWhiteSpace(page.Version) ? "1.0.0" : page.Version,
                            Author = page.Author ?? string.Empty,
                            Description = page.Description ?? string.Empty,
                            Source = "Aether Nano",
                            AssemblyName = page.Draw?.Method?.DeclaringType?.Assembly?.GetName().Name ?? string.Empty
                        };
                        registeredMods.Add(BuildExternalEntry(synthetic, order++));
                    }
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning($"[Aether] Native mod catalog could not read third-party mods: {ex.Message}");
            }

            // Keep every suite panel reachable even if its owning assembly is not loaded
            // (standalone use of the native suite, or a partial install).
            for (int i = 0; i < suiteFeatures.Count; i++)
            {
                var feature = suiteFeatures[i];
                bool owned = false;
                for (int r = 0; r < registeredMods.Count && !owned; r++)
                {
                    string[] ids = registeredMods[r].FeatureIds;
                    if (ids == null) continue;
                    for (int f = 0; f < ids.Length; f++)
                    {
                        if (string.Equals(ids[f], feature.Id, StringComparison.OrdinalIgnoreCase))
                        {
                            owned = true;
                            break;
                        }
                    }
                }

                if (!owned)
                {
                    if (feature.SourceModId != null && OwnedFeatureMods.Contains(feature.SourceModId))
                        continue;

                    feature.IsFeatureRow = true;
                    registeredMods.Add(feature);
                }
            }

            if (registeredMods.Count == 0)
                registeredMods.AddRange(suiteFeatures);

            // Synthetic suite-owned panel: always available, even if LoadedMods is empty.
            registeredMods.Add(BuildHealthEntry());

            int newIndex = -1;
            if (!string.IsNullOrEmpty(previousId))
            {
                for (int i = 0; i < registeredMods.Count; i++)
                {
                    if (string.Equals(registeredMods[i].Id, previousId, StringComparison.OrdinalIgnoreCase))
                    {
                        newIndex = i;
                        break;
                    }
                }
            }

            selectedModIndex = newIndex >= 0
                ? newIndex
                : (registeredMods.Count > 0 ? Mathf.Clamp(selectedModIndex, 0, registeredMods.Count - 1) : -1);

            SyncSelectedFeature();
        }

        private ModInfoEntry BuildRealModEntry(AetherModMetadata meta, int loadOrder)
        {
            var features = new List<ModInfoEntry>();
            for (int i = 0; i < suiteFeatures.Count; i++)
            {
                if (string.Equals(suiteFeatures[i].SourceModId, meta.Id, StringComparison.OrdinalIgnoreCase))
                    features.Add(suiteFeatures[i]);
            }

            ModInfoEntry primary = features.Count > 0 ? features[0] : default;
            string name = string.IsNullOrWhiteSpace(meta.Name) ? meta.Id : meta.Name;
            string author = string.IsNullOrWhiteSpace(meta.Author) ? Tr("Unknown", "Desconocido") : meta.Author;
            string desc = meta.Description;

            if (string.IsNullOrWhiteSpace(desc))
            {
                desc = string.Equals(meta.Id, "aether_ui", StringComparison.OrdinalIgnoreCase)
                    ? Tr("Native Block Story UI suite: Lens, Wayfinder, Blueprint, lighting (Dusk), HUD and keybinds.",
                         "Suite de interfaz nativa de Block Story: Lens, Wayfinder, Blueprint, iluminación (Dusk), HUD y teclas.")
                    : Tr("Aether module.", "Módulo de Aether.");
            }

            if (features.Count > 0)
            {
                desc += "\n\n" + Tr(
                    $"{features.Count} built-in settings module(s) available below.",
                    $"{features.Count} módulo(s) de ajustes integrados disponibles abajo.");
            }

            var deps = new StringBuilder();
            foreach (var kv in meta.Depends)
            {
                if (deps.Length > 0) deps.Append(", ");
                deps.Append(kv.Key).Append(' ').Append(kv.Value);
            }

            string[] featureIds = new string[features.Count];
            for (int i = 0; i < features.Count; i++) featureIds[i] = features[i].Id;

            return new ModInfoEntry
            {
                Id = meta.Id,
                NameEn = name,
                NameEs = name,
                Version = meta.Version != null ? meta.Version.ToString() : "1.0.0",
                Author = author,
                DescEn = desc,
                DescEs = desc,
                IconItemName = primary.Id != null ? primary.IconItemName : string.Empty,
                IconHint = meta.Icon ?? string.Empty,
                UseAetherCube = string.Equals(meta.Id, "aether", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(meta.Id, "aether_ui", StringComparison.OrdinalIgnoreCase),
                AccentColor = primary.Id != null ? primary.AccentColor : new Color(0.35f, 0.70f, 1.0f),
                IsActive = true,
                SourceModId = meta.Id,
                SourcePath = meta.SourcePath ?? string.Empty,
                Dependencies = deps.ToString(),
                LoadOrder = loadOrder + 1,
                FeatureIds = featureIds,
                IsFeatureRow = false
            };
        }

        private static int OriginSortTier(Aether.Loader.Hosting.ExternalMod ext)
        {
            if (ext == null) return 3;
            var origin = Aether.Loader.Hosting.ModOrigin.Of(ext.AssemblyName);
            if (origin == Aether.Loader.Hosting.ModOriginKind.ByAether) return 0;
            if (origin == Aether.Loader.Hosting.ModOriginKind.Nano || FindNanoPage(ext) != null) return 1;
            return 2;
        }

        private ModInfoEntry BuildExternalEntry(Aether.Loader.Hosting.ExternalMod ext, int loadOrder)
        {
            Aether.Nano.NanoPage page = FindNanoPage(ext);
            string name = (page != null && !string.IsNullOrWhiteSpace(page.Name))
                ? page.Name
                : Aether.Loader.Hosting.ExternalModCatalog.DisplayName(ext.Name);
            string author = (page != null && !string.IsNullOrWhiteSpace(page.Author))
                ? page.Author
                : (string.IsNullOrWhiteSpace(ext.Author) ? Tr("Third party", "Terceros") : ext.Author);
            string desc = (page != null && !string.IsNullOrWhiteSpace(page.Description))
                ? page.Description
                : (string.IsNullOrWhiteSpace(ext.Description)
                    ? Tr("Third-party mod running next to Aether. It is not an Aether mod.",
                         "Mod de terceros que corre junto a Aether. No es un mod de Aether.")
                    : ext.Description);
            return new ModInfoEntry
            {
                Id = ext.Id,
                NameEn = name,
                NameEs = name,
                Version = string.IsNullOrEmpty(ext.Version) ? "?" : ext.Version,
                Author = author,
                DescEn = desc,
                DescEs = desc,
                IconItemName = page != null ? (page.IconItemName ?? string.Empty) : string.Empty,
                IconTexture = page != null ? page.Icon : null,
                IconHint = string.Empty,
                UseAetherCube = false,
                AccentColor = new Color(0.95f, 0.65f, 0.25f),
                IsActive = true,
                SourceModId = string.Empty,
                SourcePath = ext.Location ?? string.Empty,
                Dependencies = string.Empty,
                LoadOrder = loadOrder + 1,
                FeatureIds = new string[0],
                IsFeatureRow = false,
                External = ext
            };
        }

        /// <summary>
        /// Synthetic system row, not a real mod: it reports the live health of the loader, the
        /// suite, Harmony and every loaded mod manifest.
        /// </summary>
        /// <summary>Short label for the narrow sidebar; the details header keeps the full name.</summary>
        private static string CompactListTitle(string id, string title)
        {
            if (string.Equals(id, "aether_ui", StringComparison.OrdinalIgnoreCase))
                return "Aether UI Suite";
            return title;
        }

        /// <summary>Prefer the skill-relative assembly path; fall back to the file name.</summary>
        private static string CompactSourcePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            try
            {
                string root = Paths.PluginPath;
                if (!string.IsNullOrEmpty(root) &&
                    path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    return path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                }
            }
            catch { }

            return Path.GetFileName(path);
        }

        private int RealModCount()
        {
            // Same number the bottom watermark shows: the loader is the single source of truth.
            if (AetherBootstrap.LoadedModCount > 0)
                return AetherBootstrap.LoadedModCount;

            int count = 0;
            for (int i = 0; i < registeredMods.Count; i++)
            {
                if (!registeredMods[i].IsFeatureRow && registeredMods[i].Id != HealthModId)
                    count++;
            }
            return count;
        }

        private ModInfoEntry BuildHealthEntry()
        {
            return new ModInfoEntry
            {
                Id = HealthModId,
                NameEn = "Mod Health",
                NameEs = "Salud de Mods",
                Version = AetherBootstrap.PluginVersion,
                Author = "Aether",
                DescEn = "Live health snapshot: loaded mod manifests, assembly paths, suite/Harmony state, FPS, frame time, memory and crash state.",
                DescEs = "Instantánea de salud: manifiestos cargados, rutas de ensamblado, estado de suite/Harmony, FPS, tiempo de frame, memoria y crashes.",
                IconItemName = "Book",
                AccentColor = new Color(0.35f, 0.85f, 0.45f),
                IsActive = true,
                SourceModId = "aether_ui",
                SourcePath = typeof(BlockStorySuitePlugin).Assembly.Location,
                LoadOrder = 0,
                FeatureIds = new[] { HealthFeatureId },
                IsFeatureRow = true
            };
        }

        private bool TryGetSuiteFeature(string featureId, out ModInfoEntry feature)
        {
            for (int i = 0; i < suiteFeatures.Count; i++)
            {
                if (string.Equals(suiteFeatures[i].Id, featureId, StringComparison.OrdinalIgnoreCase))
                {
                    feature = suiteFeatures[i];
                    return true;
                }
            }

            feature = default;
            return false;
        }

        private void SelectModRow(int index, string featureId = null)
        {
            if (index < 0 || index >= registeredMods.Count) return;

            selectedModIndex = index;
            // Always show the new mod's header instead of inheriting the previous scroll offset.
            scrollDetailPos = Vector2.zero;
            var mod = registeredMods[index];

            if (!string.IsNullOrEmpty(featureId) && mod.FeatureIds != null &&
                Array.IndexOf(mod.FeatureIds, featureId) >= 0)
            {
                selectedFeatureId = featureId;
                selectedFeatureByMod[mod.Id] = featureId;
                return;
            }

            SyncSelectedFeature();
        }

        private void SyncSelectedFeature()
        {
            if (selectedModIndex < 0 || selectedModIndex >= registeredMods.Count)
            {
                selectedFeatureId = string.Empty;
                return;
            }

            var mod = registeredMods[selectedModIndex];
            string[] ids = mod.FeatureIds;
            if (ids == null || ids.Length == 0)
            {
                selectedFeatureId = string.Empty;
                return;
            }

            if (selectedFeatureByMod.TryGetValue(mod.Id, out string stored) &&
                Array.IndexOf(ids, stored) >= 0)
            {
                selectedFeatureId = stored;
                return;
            }

            selectedFeatureId = ids[0];
            selectedFeatureByMod[mod.Id] = selectedFeatureId;
        }

        // =====================================================================
        //                NATIVE ATLAS & RENDERING ENGINE
        // =====================================================================
        public static UIAtlas GetGuiAtlas() => Aether.Nano.NanoUi.GuiAtlas;

        public static Texture2D GetColorTexture(Color col)
        {
            if (!colorTexCache.TryGetValue(col, out var tex) || tex == null)
            {
                tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, col);
                tex.Apply();
                colorTexCache[col] = tex;
            }
            return tex;
        }

        public static bool DrawNativeSprite(Rect screenRect, string spriteName, Color? tint = null)
            => Aether.Nano.NanoUi.DrawNativeSprite(screenRect, spriteName, tint);

        private void DrawWoodPanel(Rect rect, float opacity = 1.0f, Color? tintOverride = null)
        {
            if (tintOverride == null && HudCustomizerModule.UiDesignTheme != null)
            {
                HudCustomizerModule.DrawThemedFrame(rect, HudCustomizerModule.UiDesignTheme.Value, opacity);
                return;
            }
            Color tint = tintOverride ?? new Color(1f, 1f, 1f, opacity);
            GUI.DrawTexture(new Rect(rect.x - 2, rect.y - 2, rect.width + 4, rect.height + 4), GetColorTexture(new Color(0.10f, 0.06f, 0.03f, opacity * 0.95f)));
            if (!DrawNativeSprite(rect, "WoodPanel", tint))
            {
                DrawFallbackBox(rect, new Color(0.28f, 0.18f, 0.10f, opacity), new Color(0.52f, 0.36f, 0.20f, opacity));
            }
        }

        public void DrawThemedPanel(Rect rect, string theme, float opacity = 1.0f)
        {
            HudCustomizerModule.DrawThemedFrame(rect, theme, opacity);
        }

        private void DrawDarkSlotBox(Rect rect, bool selected = false, bool hovered = false)
        {
            Color tint = selected
                ? new Color(0.65f, 1.0f, 0.35f, 1.0f)
                : (hovered ? new Color(0.85f, 0.95f, 0.65f, 0.96f) : new Color(0.88f, 0.80f, 0.70f, 0.94f));

            if (!DrawNativeSprite(rect, "SetButtonSqareSl", tint))
            {
                if (!DrawNativeSprite(rect, "InvSlot", tint))
                {
                    DrawFallbackBox(rect, selected ? new Color(0.28f, 0.45f, 0.18f, 0.95f) : new Color(0.16f, 0.11f, 0.07f, 0.90f), new Color(0.48f, 0.35f, 0.22f, 0.95f));
                }
            }
        }

        private void DrawInvSlot(Rect rect, Color? tint = null)
        {
            if (!DrawNativeSprite(rect, "InvSlot", tint ?? Color.white))
            {
                DrawFallbackBox(rect, new Color(0.14f, 0.10f, 0.07f, 0.90f), new Color(0.45f, 0.32f, 0.20f, 0.95f));
            }
        }

        private bool DrawNativeWoodButton(Rect rect, string text, bool isSelected = false, Color? baseTint = null)
        {
            bool clicked = GUI.Button(rect, GUIContent.none, GUIStyle.none);

            if (Event.current.type == EventType.Repaint)
            {
                bool hovered = rect.Contains(Event.current.mousePosition);
                Color tint = (hovered || isSelected)
                    ? new Color(0.65f, 1.0f, 0.28f, 1.0f)
                    : (baseTint ?? Color.white);

                if (!DrawNativeSprite(rect, "Button", tint))
                {
                    DrawFallbackBox(rect, (hovered || isSelected) ? new Color(0.35f, 0.58f, 0.18f, 0.96f) : new Color(0.48f, 0.32f, 0.16f, 0.96f), new Color(0.20f, 0.12f, 0.05f, 1f));
                }

                Color textCol = (hovered || isSelected) ? new Color(1f, 1f, 0.85f, 1f) : Color.white;
                DrawShadowLabel(rect, text, woodBtnTextStyle, textCol, Color.black);
            }

            return clicked;
        }

        private static GUIStyle _badgeBrandStyle;

        /// <summary>
        /// The origin chip of a row in the mod list. "By Aether" needs Aether's strong-name signature, never a name
        /// (<see cref="Aether.Loader.Hosting.ModOrigin"/>); "Nano" marks mods built on Aether Nano. Not "official": that would
        /// read as official from the game's studio.
        /// </summary>
        private void DrawOriginBadge(Rect cardRect, ModInfoEntry mod)
        {
            string text;
            Color fill, border, ink;
            var origin = mod.External != null
                ? Aether.Loader.Hosting.ModOrigin.Of(mod.External.AssemblyName)
                : Aether.Loader.Hosting.ModOriginKind.Other;

            if (mod.IsFeatureRow)
            {
                text = Tr("SUITE", "SUITE");
                fill = new Color(0.16f, 0.12f, 0.08f, 0.92f);
                border = new Color(0.45f, 0.34f, 0.16f, 0.85f);
                ink = Aether.Nano.NanoBrand.Warn;
            }
            else if (mod.External == null)
            {
                text = Tr("CORE", "NÚCLEO");
                fill = new Color(0.07f, 0.16f, 0.11f, 0.94f);
                border = new Color(0.24f, 0.52f, 0.36f, 0.85f);
                ink = Aether.Nano.NanoBrand.Ok;
            }
            else if (origin == Aether.Loader.Hosting.ModOriginKind.ByAether)
            {
                text = Tr("BY AETHER", "POR AETHER");
                fill = new Color(0.20f, 0.10f, 0.05f, 0.96f);
                border = new Color(0.76f, 0.31f, 0.04f, 0.90f);
                ink = Aether.Nano.NanoBrand.EmberSoft;
            }
            else if (origin == Aether.Loader.Hosting.ModOriginKind.Nano)
            {
                text = "NANO";
                fill = new Color(0.14f, 0.11f, 0.088f, 0.94f);
                border = new Color(0.36f, 0.29f, 0.23f, 0.85f);
                ink = Aether.Nano.NanoBrand.Paper;
            }
            else
            {
                text = Tr("HOSTED", "EXTERNO");
                fill = new Color(0.16f, 0.12f, 0.07f, 0.92f);
                border = new Color(0.46f, 0.34f, 0.16f, 0.80f);
                ink = Aether.Nano.NanoBrand.Warn;
            }

            _badgeBrandStyle ??= Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 9, Color.white, TextAnchor.MiddleCenter);
            _menuMeasure.text = text;
            float w = Mathf.Max(52f, _badgeBrandStyle.CalcSize(_menuMeasure).x + 12f);
            var r = new Rect(cardRect.xMax - w - 8f, cardRect.y + (cardRect.height - 18f) * 0.5f, w, 18f);
            Aether.Nano.NanoSurface.DrawChip(r, text, _badgeBrandStyle, fill, border, ink, 4f);
        }

        private static GUIStyle _aetherTitleStyle, _aetherMetaStyle;

        /// <summary>
        /// The header of Aether's windows (F10, F7): open cube, title in Gloock and live metadata in JetBrains Mono.
        /// Drawn cleanly without any decorative top or side stripes.
        /// </summary>
        public static void DrawAetherHeader(Rect area, string title, string meta)
        {
            _aetherTitleStyle ??= Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Display, 21, Aether.Nano.NanoBrand.Paper);
            _aetherMetaStyle ??= Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 11, Aether.Nano.NanoBrand.Mute);
            const float pad = 12f, cubeH = 21f;
            bool hasCube = Aether.Nano.NanoBrand.Cube != null;
            _menuMeasure.text = title;
            float titleW = _aetherTitleStyle.CalcSize(_menuMeasure).x;
            float metaW = 0f;
            if (!string.IsNullOrEmpty(meta))
            {
                _menuMeasure.text = meta;
                metaW = _aetherMetaStyle.CalcSize(_menuMeasure).x;
            }
            float w = pad + (hasCube ? cubeH * 57f / 64f + 10f : 0f) + titleW + (metaW > 0f ? 14f + metaW : 0f) + pad;
            var plate = new Rect(area.x, area.y, Mathf.Min(w, area.width), area.height);
            Aether.Nano.NanoBrand.DrawCard(plate, plate.Contains(Event.current.mousePosition) ? 1f : 0.7f);
            float x = plate.x + pad;
            if (hasCube) x += Aether.Nano.NanoBrand.DrawCube(x, plate.y + (plate.height - cubeH) / 2f, cubeH) + 10f;
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(x, plate.y, titleW + 2f, plate.height), title, _aetherTitleStyle, Aether.Nano.NanoBrand.Paper, 0.4f);
            x += titleW + 14f;
            if (metaW > 0f && x < plate.xMax)
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(x, plate.y + 1f, plate.xMax - x - 6f, plate.height), meta, _aetherMetaStyle, Aether.Nano.NanoBrand.Mute, 0.3f);
        }

        public static void DrawShadowLabel(Rect rect, string text, GUIStyle style, Color textColor, Color shadowColor)
            => Aether.Nano.NanoUi.DrawShadowLabel(rect, text, style, textColor, shadowColor);

        private void DrawFallbackBox(Rect rect, Color bg, Color border)
        {
            GUI.DrawTexture(new Rect(rect.x - 1, rect.y - 1, rect.width + 2, rect.height + 2), GetColorTexture(border));
            GUI.DrawTexture(rect, GetColorTexture(bg));
        }

        // =====================================================================
        //        PAUSE MENU DETECTION & MODAL INPUT / CURSOR MANAGEMENT
        // =====================================================================
        private Inventory GetInventory()
        {
            if (cachedInventory == null)
            {
                cachedInventory = Inventory.Instance;

                // In the main menu Inventory.Instance is null, so this used to run a full-scene
                // FindObjectOfType several times per frame. Throttle it to twice per second.
                if (cachedInventory == null && Time.unscaledTime - lastInventorySearchTime > 0.5f)
                {
                    lastInventorySearchTime = Time.unscaledTime;
                    cachedInventory = FindObjectOfType<Inventory>();
                }
            }
            return cachedInventory;
        }

        public bool IsInGamePauseMenuOpen()
        {
            string scene = SceneManager.GetActiveScene().name;
            if (scene == "Menu" || scene == "StartScene") return false;

            Inventory inv = GetInventory();
            if (inv == null) return false;

            return (inv.mainSettingsButton != null && inv.mainSettingsButton.activeInHierarchy)
                || (inv.settingsCamera != null && inv.settingsCamera.gameObject.activeInHierarchy);
        }

        public bool IsMouseOverModsButton()
        {
            string scene = SceneManager.GetActiveScene().name;
            bool inMainMenu = (scene == "Menu" || scene == "StartScene");
            bool inPauseMenu = IsInGamePauseMenuOpen();
            if (!inMainMenu && !inPauseMenu) return false;
            if (isModMenuOpen || isWayfinderOpen || isDraggingHudElements) return false;

            Vector3 m = Input.mousePosition;
            float guiMouseX = m.x;
            float guiMouseY = Screen.height - m.y;
            Rect modsBtnRect = new Rect(18f, 18f, 286f, 42f);
            return modsBtnRect.Contains(new Vector2(guiMouseX, guiMouseY));
        }

        public void SetModMenuOpen(bool open)
        {
            if (isModMenuOpen == open) return;
            if (open && isWayfinderOpen)
                SetWayfinderOpen(false);
            if (open && isDraggingHudElements)
                SetDraggingHudElements(false);
            if (open && isKeybindsManagerOpen)
                SetKeybindsManagerOpen(false);

            isModMenuOpen = open;
            if (open)
            {
                modalOpenedTimestamp = Time.unscaledTime;
                // The loader resolves mods in frame 0; refresh here so the catalog always
                // reflects the live LoadedMods list (including late third-party installs).
                RefreshModCatalog();
                // Open on the selected mod's header, never on a stale scroll offset.
                scrollDetailPos = Vector2.zero;
                AcquireModalIsolation();
            }
            else
            {
                ReleaseModalIsolation();
            }
        }

        private bool shellHoldsIsolation;

        private void AcquireModalIsolation()
        {
            // One shared owner of the game's input (Aether Nano): Aether's menus and a mod's window (Wayfinder's map) can
            // both hold it. The shell holds at most one token, so repeated acquires never leak a hold.
            if (shellHoldsIsolation) { Aether.Nano.NanoInput.EnforceGameCursor(); return; }
            shellHoldsIsolation = true;
            Aether.Nano.NanoInput.AcquireIsolation();
        }

        private void ReleaseModalIsolation()
        {
            modalClosedTimestamp = Time.unscaledTime;
            if (!shellHoldsIsolation) return;
            shellHoldsIsolation = false;
            Aether.Nano.NanoInput.ReleaseIsolation();
        }

        /// <summary>Re-enables every UICamera this plugin disabled and forgets them.</summary>
        private void ReleaseDisabledCameras()
        {
            // Cameras are owned by Aether Nano now (NanoInput); nothing to do here.
        }

        /// <summary>
        /// Symmetric counterpart of AcquireModalIsolation(): restores Inventory input flags and
        /// either the native game cursor (menus / open windows) or the locked gameplay cursor.
        /// </summary>
        /// <summary>
        /// <c>Inventory.isWindowOpen()</c> dereferences ~25 window objects. In the main menu (or while a scene unloads) the
        /// Inventory can exist with those still null, which threw the "ReleaseModalIsolation warning: Object reference not
        /// set" seen on every launch. No window object means no window open.
        /// </summary>
        private static bool SafeIsWindowOpen(Inventory inv)
        {
            if (inv == null) return false;
            try { return inv.isWindowOpen(); }
            catch (NullReferenceException) { return false; }
        }

        private void RestoreGameplayInput(bool wasPauseMenu) => Aether.Nano.NanoInput.RestoreGameplayInput(wasPauseMenu);

        /// <summary>
        /// Hard reset used on scene changes / disable / quit. Clears every modal flag WITHOUT
        /// re-entering the modal setters, then releases isolation exactly once.
        /// </summary>
        public void ForceReleaseAllModals()
        {
            isModMenuOpen = false;
            isWayfinderOpen = false;
            isDraggingHudElements = false;
            isKeybindsManagerOpen = false;
            isRecipeCardOpen = false;
            rebindingEntry = null;
            Aether.Nano.NanoMotion.Snap("aether:mod_menu", false);
            Aether.Nano.NanoMotion.Snap("aether:keybinds", false);

            ReleaseModalIsolation();
        }

        private void OnDisable()
        {
            try { ForceReleaseAllModals(); }
            catch (Exception ex) { Log.LogWarning("OnDisable isolation cleanup warning: " + ex.Message); }

            try { RestoreAllChunkRenderers(); }
            catch { }
        }

        private void OnApplicationQuit()
        {
            try { ForceReleaseAllModals(); }
            catch { }
        }

        private static void EnforceNativeGameCursor() => Aether.Nano.NanoInput.EnforceGameCursor();

        private static void DrawNativeCursorOverlay() => Aether.Nano.NanoInput.DrawGameCursor();

        // =====================================================================
        //                 MAIN ENGINE UPDATE & VANILLA HUD HOOKS
        // =====================================================================
        private void Update()
        {
            ApplyNanoFont();
            string currentScene = SceneManager.GetActiveScene().name;
            if (currentScene != lastSceneName)
            {
                Log.LogInfo($"[BlockStorySuite] Active scene changed from '{lastSceneName}' to '{currentScene}'");

                // Persist the previous world before its references disappear.

                // Release modal isolation BEFORE dropping references: otherwise disabled UICameras
                // and hidden pause buttons are orphaned forever (dead NGUI input after any load).
                ForceReleaseAllModals();
                injectedMenuButton = null;
                lastSceneName = currentScene;
                cachedDigger = null;
                cachedCamera = null;
                cachedDiamondHud = null;
                cachedInventory = null;
                Aether.Nano.NanoItems.ClearCache();
                NotifyUiSceneChanged(currentScene);
            }

            UpdateUiModules();
            UpdateQaDiagnostics();
            UpdateModHealth();
            UpdateKeybindManager();
            EnsureSettingsModsTab();

            if (currentScene == "Menu" || currentScene == "StartScene")
            {
                TryInjectMainMenuButton();
                EnforceNativeGameCursor();

                if (isKeybindsManagerOpen && Input.GetKeyDown(KeyCode.Escape))
                {
                    if (rebindingEntry != null) rebindingEntry = null;
                    else SetKeybindsManagerOpen(false);
                    return;
                }

                if (isModMenuOpen && Input.GetKeyDown(KeyCode.Escape))
                {
                    SetModMenuOpen(false);
                    return;
                }
                return;
            }

            // HUD Drag Mode key handling
            if (isDraggingHudElements)
            {
                EnforceNativeGameCursor();
                Inventory.allowInput = false;
                Inventory.isPaused = true;

                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return))
                {
                    SetDraggingHudElements(false);
                    Config.Save();
                    SetModMenuOpen(true);
                    return;
                }
                return;
            }

            // Escape closes active modal first
            if ((isModMenuOpen || isWayfinderOpen || isKeybindsManagerOpen) && Input.GetKeyDown(KeyCode.Escape))
            {
                if (isKeybindsManagerOpen)
                {
                    if (rebindingEntry != null) rebindingEntry = null;
                    else SetKeybindsManagerOpen(false);
                }
                if (isModMenuOpen) SetModMenuOpen(false);
                if (isWayfinderOpen) SetWayfinderOpen(false);
                return;
            }

            // Hotkeys
            if (Input.GetKeyDown(MenuKey != null ? MenuKey.Value : KeyCode.F10))
            {
                SetModMenuOpen(!isModMenuOpen);
            }

            // Lens handles its own toggle key (it is a Nano mod since 1.1.0).

            // Wayfinder handles its own keys (M, N, L, B): it is a Nano mod since 1.1.0.

            // While any modal holds the input (Aether's menus or a mod's window), Aether Nano keeps it off every frame.

            // Periodic Scene Search
            if (Time.time - lastSearchTime > 1.5f)
            {
                lastSearchTime = Time.time;
                if (cachedCamera == null)
                    cachedCamera = Camera.main;
                if (cachedDigger == null)
                    cachedDigger = TerrainLoader.instance != null && TerrainLoader.instance.digger != null
                        ? TerrainLoader.instance.digger
                        : FindObjectOfType<Digger>();
                if (cachedDiamondHud == null)
                    cachedDiamondHud = FindObjectOfType<DisplayCurrency>();
            }

            cachedIsUnderground = Aether.Nano.Nano.IsUnderground; // computed once, by Nano, for every mod
        }

        private void LateUpdate()
        {
            ApplyVanillaHudLayout();
            LateUpdateUiModules();
        }

        // ---- "Mods" tab in the game's native settings sidebar (Controls / Graphics / Sound / ...) ---------
        private const string SettingsSidebarPath = "UI Root (2D)/SettingsCamera/SettingsScroll/MainSettingsButtons";
        private float nextSettingsTabCheck;

        /// <summary>
        /// Adds a "Mods" entry above Exit in the native settings sidebar. It is cloned from the Misc tab (same
        /// look and hover) but stripped of its checkbox behaviour: it is an action that opens the Aether mod
        /// menu, not a panel selector. The rows are re-spaced evenly because the board art has a fixed height.
        /// </summary>
        private void EnsureSettingsModsTab()
        {
            if (Time.unscaledTime < nextSettingsTabCheck) return;
            nextSettingsTabCheck = Time.unscaledTime + 1f;
            try
            {
                GameObject container = GameObject.Find(SettingsSidebarPath);
                if (container == null) return;
                Transform bar = container.transform;
                if (bar.Find("ModsSettingsButton") != null) return;

                Transform template = bar.Find("MiscSettingsButton");
                if (template == null) return;

                // Build the clone under an INACTIVE holder: Instantiate under an active parent runs Awake/OnEnable at
                // once, and the cloned UICheckboxControlledObject (still pointing at Misc's panel) would toggle that
                // panel on the first open (the "overlap" with the pause buttons). Inactive = nothing runs yet.
                GameObject holder = new GameObject("Aether_ModsTabHolder");
                holder.SetActive(false);
                GameObject btn = Instantiate(template.gameObject, holder.transform, false);
                btn.name = "ModsSettingsButton";

                // Action button, not a tab: drop the radio/panel behaviour inherited from Misc (safe while inactive).
                foreach (var c in btn.GetComponents<UICheckboxControlledObject>()) DestroyImmediate(c);
                foreach (var c in btn.GetComponents<UICheckbox>()) DestroyImmediate(c);
                foreach (var c in btn.GetComponents<UIAutoButtonKey>()) DestroyImmediate(c);
                foreach (var c in btn.GetComponents<UIButtonMessage>()) DestroyImmediate(c);

                btn.transform.SetParent(bar, false);
                btn.transform.localScale = template.localScale;
                btn.transform.localRotation = template.localRotation;
                btn.transform.localPosition = template.localPosition;
                btn.SetActive(true);
                Destroy(holder);

                UILabel label = btn.GetComponentInChildren<UILabel>(true);
                if (label != null) label.text = Tr("Mods", "Mods");

                UIEventListener.Get(btn).onClick = delegate(GameObject go) { SetModMenuOpen(true); };

                RelayoutSettingsSidebar(bar);
                Log.LogInfo("[Aether] Added 'Mods' to the native settings sidebar.");
            }
            catch (Exception ex)
            {
                Log.LogWarning("EnsureSettingsModsTab: " + ex.Message);
            }
        }

        /// <summary>
        /// Puts Mods in Exit's slot and moves Exit one row down, into the empty plank below it. Every other row keeps
        /// its original position: compressing the rows made the 50-unit tab backgrounds overlap and drew horizontal seams.
        /// </summary>
        private static void RelayoutSettingsSidebar(Transform bar)
        {
            Transform mods = bar.Find("ModsSettingsButton");
            Transform exit = bar.Find("ExitButton");
            Transform back = bar.Find("BackButton");
            Transform controls = bar.Find("ControlSettingsButton");
            if (mods == null || exit == null || back == null || controls == null) return;

            float step = back.localPosition.y - controls.localPosition.y; // the game's own row pitch (50)
            if (step <= 0f) return;

            Vector3 exitPos = exit.localPosition;
            Vector3 modsPos = mods.localPosition;
            modsPos.y = exitPos.y;
            mods.localPosition = modsPos;
            exitPos.y -= step;
            exit.localPosition = exitPos;
        }

        private void CheckMainMenuButtonInjection() => TryInjectMainMenuButton();

        public void TryInjectMainMenuButton()
        {
            if (injectedMenuButton != null) return;
            if (Time.unscaledTime - lastMenuInjectionAttemptTime < 0.5f) return;
            lastMenuInjectionAttemptTime = Time.unscaledTime;

            try
            {
                GameObject buttonsLower = GameObject.Find("Panel - Main/MainMenu/ButtonsLower");
                if (buttonsLower == null) return;

                Transform existing = buttonsLower.transform.Find("Button - Mods");
                if (existing != null)
                {
                    injectedMenuButton = existing.gameObject;
                    return;
                }

                Transform template = buttonsLower.transform.Find("Button Options");
                if (template == null) template = buttonsLower.transform.Find("Button - Options");
                if (template == null && buttonsLower.transform.childCount > 0)
                    template = buttonsLower.transform.GetChild(0);

                if (template != null)
                {
                    GameObject newBtn = Instantiate(template.gameObject);
                    newBtn.name = "Button - Mods";
                    newBtn.transform.parent = buttonsLower.transform;
                    newBtn.transform.localScale = template.localScale;
                    newBtn.transform.localPosition = template.localPosition;
                    newBtn.transform.localRotation = template.localRotation;

                    var msg = newBtn.GetComponent<UIButtonMessage>();
                    if (msg != null) Destroy(msg);

                    UILabel label = newBtn.GetComponentInChildren<UILabel>();
                    if (label != null)
                    {
                        label.text = Tr("Mods", "Mods");
                    }

                    UIEventListener.Get(newBtn).onClick = delegate(GameObject go)
                    {
                        SetModMenuOpen(true);
                    };

                    UITable table = buttonsLower.GetComponent<UITable>();
                    if (table != null) table.Reposition();
                    else
                    {
                        UIGrid grid = buttonsLower.GetComponent<UIGrid>();
                        if (grid != null) grid.Reposition();
                    }

                    injectedMenuButton = newBtn;
                    Log.LogInfo("Successfully injected native 'Button - Mods' into Main Menu ButtonsLower!");
                }
            }
            catch (Exception ex)
            {
                Log.LogWarning("CheckMainMenuButtonInjection notice: " + ex.Message);
            }
        }

        private void ApplyVanillaHudLayout()
        {
            HudCustomizerModule.ApplyVanillaHudLayout();
        }


        /// <summary>
        /// True when at least 7 of 8 horizontal directions have a wall at BOTH head heights
        /// (feet+1 and feet+2) within 32 blocks. The two heights are essential: a single
        /// overhang or a one-block fence would otherwise fake an enclosure. Igloos, houses,
        /// cave rooms, underworld and space structures have 2+ block walls and pass.
        /// </summary>

        /// <summary>Topmost non-air, non-passable block of a column (roof/structure/terrain).</summary>


        // =====================================================================
        //                 HUD ANCHOR & SCREEN RECT RESOLUTION
        // =====================================================================
        private float CalculateSafeTopOffset()
        {
            if (LensAnchor != null && LensAnchor.Value == "TopCenter" && MoveDiamondsUnderXP.Value)
            {
                // Leave room for the desktop/window top bar and the native top HUD.
                // 14 px clipped the WAILA card in windowed mode.
                return 56f * (Screen.height / 1080f);
            }

            float nguiScale = Screen.height / 480f;
            float baseSafeY = 48f * nguiScale + 6f; // ~114px on 1080p

            try
            {
                if (cachedDiamondHud != null && cachedDiamondHud.gameObject.activeInHierarchy)
                {
                    Collider col = cachedDiamondHud.GetComponentInChildren<Collider>();
                    Camera uiCam = UICamera.currentCamera ?? NGUITools.FindCameraForLayer(cachedDiamondHud.gameObject.layer);
                    if (col != null && uiCam != null)
                    {
                        Bounds b = col.bounds;
                        Vector3 minScreen = uiCam.WorldToScreenPoint(b.min);
                        Vector3 maxScreen = uiCam.WorldToScreenPoint(b.max);
                        float bottomPx = Screen.height - Mathf.Min(minScreen.y, maxScreen.y);
                        if (bottomPx > 20f && bottomPx < Screen.height * 0.35f)
                        {
                            baseSafeY = Mathf.Max(baseSafeY, bottomPx + 10f);
                        }
                    }
                }
            }
            catch { }

            return baseSafeY;
        }

        private void InitStyles()
        {
            if (titleStyle != null) return;

            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
            titleStyle.normal.textColor = Color.white;

            toolStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
            toolStyle.normal.textColor = new Color(0.95f, 0.92f, 0.82f);

            subStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleRight
            };
            subStyle.normal.textColor = new Color(0.55f, 0.90f, 1.0f);

            smallStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            smallStyle.normal.textColor = Color.white;

            sourceStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };
            sourceStyle.normal.textColor = new Color(0.72f, 0.76f, 0.82f);

            menuHeaderStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            menuHeaderStyle.normal.textColor = new Color(0.55f, 1.0f, 0.25f);

            menuTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
            menuTitleStyle.normal.textColor = new Color(0.65f, 1.0f, 0.30f);

            modNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
            modNameStyle.normal.textColor = Color.white;

            // Fits "Aether UI & Native Suite" in the 310 px sidebar without clipping.
            compactNameStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = false
            };
            compactNameStyle.normal.textColor = Color.white;

            authorStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Italic,
                alignment = TextAnchor.MiddleLeft
            };
            authorStyle.normal.textColor = new Color(0.88f, 0.82f, 0.72f);

            descStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Normal,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            descStyle.normal.textColor = new Color(0.96f, 0.94f, 0.88f);

            sectionTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            sectionTitleStyle.normal.textColor = new Color(1.0f, 0.85f, 0.25f);

            badgeStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            badgeStyle.normal.textColor = Color.white;

            woodBtnTextStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = false
            };
            woodBtnTextStyle.normal.textColor = Color.white;

            minimapLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
            minimapLabelStyle.normal.textColor = Color.white;

            richTooltipStyle = new GUIStyle(GUI.skin.label)
            {
                richText = true,
                wordWrap = true,
                fontSize = 12,
                alignment = TextAnchor.UpperLeft
            };
            richTooltipStyle.normal.textColor = new Color(0.95f, 0.94f, 0.90f, 1f);

            textFieldStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            textFieldStyle.normal.textColor = Color.white;

        }

        // =====================================================================
        //                BLOCK & ITEM ICON / NAME RESOLUTION
        // =====================================================================
        // Item lookup, icons and names live in Aether Nano, shared with the mods built on it.
        public static InvBaseItem SafeFindItemByName(string name) => Aether.Nano.NanoItems.FindByName(name);
        private static InvBaseItem ResolveBlockItem(string blockTypeName) => Aether.Nano.NanoItems.ResolveBlockItem(blockTypeName);
        private static bool DrawItemIcon(Rect iconRect, InvBaseItem baseItem, Color? overrideColor = null)
            => Aether.Nano.NanoItems.DrawIcon(iconRect, baseItem, overrideColor);
        private static string GetLocalizedBlockName(string rawBlockName, InvBaseItem baseItem)
            => Aether.Nano.NanoItems.LocalizedName(rawBlockName, baseItem);

        // =====================================================================
        //                           MAIN OnGUI LOOP
        // =====================================================================
        private void OnGUI()
        {
            float modMenuAnim = Aether.Nano.NanoMotion.Sample("aether:mod_menu", isModMenuOpen);
            bool modMenuDrawing = isModMenuOpen || modMenuAnim > 0.005f;
            float keybindsAnim = Aether.Nano.NanoMotion.Sample("aether:keybinds", isKeybindsManagerOpen || showKeybindsModal);
            bool keybindsDrawing = isKeybindsManagerOpen || showKeybindsModal || keybindsAnim > 0.005f;
            GUI.depth = (modMenuDrawing || isWayfinderOpen || keybindsDrawing || isDraggingHudElements) ? -100 : 10;
            InitStyles();

            string currentScene = SceneManager.GetActiveScene().name;
            bool isMenuScene = (currentScene == "Menu" || currentScene == "StartScene");
            bool pauseOpen = IsInGamePauseMenuOpen();
            Inventory inv = GetInventory();
            bool anyNativeWindowOpen = SafeIsWindowOpen(inv);
            bool isLoadingScreen = LoadingScreenState.IsLoadingOrMenuNotReady();

            if (!modMenuDrawing && !isWayfinderOpen && !isDraggingHudElements && !keybindsDrawing)
            {
                // Main Menu OR Pause Menu: the Mods entry now lives in the native settings sidebar and in the
                // bottom-left Aether badge; here we only keep the native cursor.
                // Never draw over Block Story's loading/fade screen.
                if ((isMenuScene || pauseOpen) && !isLoadingScreen)
                {
                    EnforceNativeGameCursor();
                    DrawNativeCursorOverlay();
                }
                else if (!anyNativeWindowOpen && !isLoadingScreen)
                {
                    // Active Gameplay HUDs


                    // Lens draws itself (Nano mod); Aether only tells Nano when its own menus cover the HUD.

                    // Wayfinder draws its minimap and beacons itself (Nano mod).
                }

                if (!isMenuScene && HoverTooltipEnabled.Value && (UICustomCursor.current == null || !UICustomCursor.current.Locked))
                {
                    DrawHoveredItemRichTooltip();
                }
            }
            else if (isDraggingHudElements)
            {
                EnforceNativeGameCursor();
                DrawHudDragModeGUI();
                DrawNativeCursorOverlay();
            }
            else if (modMenuDrawing)
            {
                if (isModMenuOpen) EnforceNativeGameCursor();
                DrawModMenuGUI(modMenuAnim);
                if (isModMenuOpen) DrawNativeCursorOverlay();
            }
            else if (keybindsDrawing)
            {
                if (isKeybindsManagerOpen || showKeybindsModal) EnforceNativeGameCursor();
                DrawKeybindManagerGUI(keybindsAnim);
                if (isKeybindsManagerOpen || showKeybindsModal) DrawNativeCursorOverlay();
            }

            DrawBlueprintAndSodiumGUI();
        }

        // =====================================================================
        //                     INTERACTIVE HUD DRAG MODE
        // =====================================================================
        private void DrawHudDragModeGUI()
        {
            HudCustomizerModule.DrawHudDragModeGUI(this);
        }

        // =====================================================================
        //          DETERMINISTIC LENS HUD (VOXEL RAYTRACER + ENTITIES)
        // =====================================================================
        private IWorld GetActiveWorld()
        {
            IWorld world = null;
            if (TerrainLoader.instance != null)
                world = TerrainLoader.instance.world ?? TerrainLoader.instance.getWorld();
            if (world == null && cachedDigger != null && cachedDigger.terrain != null)
                world = cachedDigger.terrain.world ?? cachedDigger.terrain.getWorld();
            return world;
        }

        // =====================================================================
        //        RICH FLOATING HOVER TOOLTIP (NAME, STATS, DURABILITY, ORIGIN)
        // =====================================================================
        private InvGameItem GetHoveredSlotItem()
        {
            if (UIItemSlot.draggedItem != null)
                return null;

            // Never show inventory tooltip if a modal is open or mouse is inside Blueprint
            if (IsModalOpen || IsMouseOverBlueprintPanel())
                return null;

            Inventory inv = GetInventory();
            if (inv == null || inv.inventoryGameObject == null || !inv.inventoryGameObject.activeInHierarchy)
                return null;

            GameObject hovered = UICamera.hoveredObject;
            if (hovered == null)
            {
                Vector2 cursorPos = UICustomCursor.current != null ? UICustomCursor.current.Position : (Vector2)Input.mousePosition;
                RaycastHit hit = default;
                if (UICamera.Raycast(cursorPos, ref hit) && hit.collider != null)
                {
                    hovered = hit.collider.gameObject;
                }
            }

            if (hovered == null) return null;

            // Prevent stuck tooltips when UICamera.Update is blocked/paused:
            // Physically verify mouse ray intersects the collider of hovered
            Collider col = hovered.GetComponent<Collider>();
            if (col != null)
            {
                Camera uiCam = UICamera.currentCamera;
                if (uiCam != null)
                {
                    Ray ray = uiCam.ScreenPointToRay(Input.mousePosition);
                    if (!col.Raycast(ray, out _, 1000f))
                    {
                        return null;
                    }
                }
            }

            UIItemSlot slot = hovered.GetComponentInParent<UIItemSlot>();
            if (slot != null && slot.observedItem != null)
                return slot.observedItem;

            DisplayCreativeItem creative = hovered.GetComponentInParent<DisplayCreativeItem>();
            if (creative != null && creativeItemField != null)
            {
                var cItem = creativeItemField.GetValue(creative) as InvGameItem;
                if (cItem != null) return cItem;
            }

            RecipeItemClick recipe = hovered.GetComponentInParent<RecipeItemClick>();
            if (recipe != null && recipeItemField != null)
            {
                var rItem = recipeItemField.GetValue(recipe) as InvGameItem;
                if (rItem != null) return rItem;
            }

            return null;
        }

        private static void EnsureHoverTooltipStyles()
        {
            if (_tipTitleStyle != null) return;
            _tipTitleStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Display, 17, Aether.Nano.NanoBrand.PaperLight);
            _tipMetaStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Mute);
            _tipBodyStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Body, 12, Aether.Nano.NanoBrand.Paper, TextAnchor.UpperLeft, true);
            _tipChipStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 9, Aether.Nano.NanoBrand.Paper, TextAnchor.MiddleCenter);
        }

        private void DrawHoveredItemRichTooltip()
        {
            bool blocked = IsModalOpen || IsMouseOverBlueprintPanel() || Aether.Nano.NanoHud.CanvasEditorOpen;
            InvGameItem item = blocked ? null : GetHoveredSlotItem();
            bool hasItem = item != null && item.baseItem != null;

            float tipAnim = Aether.Nano.NanoMotion.Sample("aether:hover_tooltip", hasItem, 0.11f, 0.08f);
            if (!hasItem || tipAnim <= 0.01f) return;

            EnsureHoverTooltipStyles();
            bool es = IsSpanish();

            InvBaseItem baseItem = item.baseItem;
            string itemName = item.itemName;
            if (string.IsNullOrEmpty(itemName))
                itemName = T.ItemNames(baseItem.name) ?? baseItem.name;

            int maxDur = baseItem.durability;
            bool hasDurabilityBar = maxDur > 1;
            int curDur = hasDurabilityBar ? item.topStack : 1;
            float durRatio = hasDurabilityBar ? Mathf.Clamp01((float)curDur / maxDur) : 1f;

            const float boxW = 316f;
            const float pad = 12f;

            // Cache formatted body markup per item state + language (zero per-frame StringBuilder/Regex allocs)
            string tooltipKey = $"v2|{baseItem.name}|{item.topStack}|{item.count}|{item.price}|{(es ? "es" : "en")}";
            if (!richTooltipCache.TryGetValue(tooltipKey, out var cachedTooltip))
            {
                string rawDesc = item.itemDesc;
                string cleaned = string.Empty;
                if (!string.IsNullOrEmpty(rawDesc))
                {
                    int durIdx = rawDesc.IndexOf("\n[", StringComparison.Ordinal);
                    if (durIdx >= 0) rawDesc = rawDesc.Substring(0, durIdx);
                    cleaned = Regex.Replace(rawDesc, @"\[([0-9a-fA-F]{6})\]", "<color=#$1>");
                    cleaned = cleaned.Replace("[-]", "</color>").Trim();
                }

                var content = new GUIContent(cleaned);
                float height = string.IsNullOrEmpty(cleaned) ? 0f : _tipBodyStyle.CalcHeight(content, boxW - pad * 2f);

                if (richTooltipCache.Count >= 96) richTooltipCache.Clear();
                cachedTooltip = (content, height);
                richTooltipCache[tooltipKey] = cachedTooltip;
            }

            GUIContent bodyContent = cachedTooltip.Content;
            float bodyH = cachedTooltip.Height;
            bool hasBody = bodyH > 1f;

            float headerH = 54f;
            float durSectionH = hasDurabilityBar ? 26f : 0f;
            float bodySectionH = hasBody ? (bodyH + 12f) : 0f;
            float footerH = 28f;
            float boxH = pad + headerH + durSectionH + bodySectionH + footerH + 6f;

            Vector2 mousePos = Input.mousePosition;
            float x = mousePos.x + 22f;
            float y = Screen.height - mousePos.y + 14f;

            if (x + boxW > Screen.width - 12f)
                x = mousePos.x - boxW - 14f;
            if (y + boxH > Screen.height - 12f)
                y = Screen.height - boxH - 12f;
            if (x < 12f) x = 12f;
            if (y < 12f) y = 12f;

            Rect boxRect = new Rect(x, y, boxW, boxH);

            using (Aether.Nano.NanoMotion.BeginSlide(tipAnim, 0f, 5f))
            {
                // Floating crystal-ink squircle surface (crystal translucency reserved for floating layers!)
                Aether.Nano.NanoSurface.DrawCardSurface(
                    boxRect,
                    new Color(0.092f, 0.074f, 0.058f, 0.93f),
                    new Color(0.045f, 0.037f, 0.029f, 0.95f),
                    new Color(Aether.Nano.NanoBrand.Ember.r, Aether.Nano.NanoBrand.Ember.g, Aether.Nano.NanoBrand.Ember.b, 0.42f),
                    Aether.Nano.NanoBrand.Ember,
                    new Color(1f, 0.96f, 0.90f, 0.14f),
                    11f, 1f, 0f, 0f);

                // Item icon recessed slot + editorial title + category/slot subtitle
                Rect slotRect = new Rect(x + pad, y + pad, 44f, 44f);
                Aether.Nano.NanoSurface.DrawDebossedSlot(slotRect, new Color(0.042f, 0.034f, 0.027f, 0.94f), Aether.Nano.NanoBrand.Line, 8f);
                DrawItemIcon(new Rect(slotRect.x + 4f, slotRect.y + 4f, 36f, 36f), baseItem, item.color);

                float tx = slotRect.xMax + 10f;
                float tw = boxRect.xMax - tx - pad;
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(tx, y + pad + 1f, tw, 24f),
                    itemName, _tipTitleStyle, Aether.Nano.NanoBrand.PaperLight, 0.4f);

                string subtitle = baseItem.category.ToString();
                if (baseItem.slot != InvBaseItem.Slot.None && baseItem.slot != InvBaseItem.Slot._LastDoNotUse)
                    subtitle += $"  ·  {baseItem.slot}";
                else if (!hasDurabilityBar && item.count > 1)
                    subtitle += $"  ·  ×{item.count}";
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(tx, y + pad + 26f, tw, 16f),
                    subtitle, _tipMetaStyle, Aether.Nano.NanoBrand.Mute, 0.25f);

                float curY = y + pad + headerH;

                // Beveled durability bar + tabular numeric readout
                if (hasDurabilityBar)
                {
                    Color barCol = durRatio > 0.55f
                        ? Aether.Nano.NanoBrand.Ok
                        : (durRatio > 0.25f ? Aether.Nano.NanoBrand.Warn : Aether.Nano.NanoBrand.Bad);
                    float barW = boxW - pad * 2f - 104f;
                    Aether.Nano.NanoSurface.DrawProgressBar(
                        new Rect(x + pad, curY + 4f, Mathf.Max(60f, barW), 9f),
                        durRatio, 0f,
                        new Color(0.04f, 0.032f, 0.026f, 0.92f),
                        Aether.Nano.NanoBrand.Line,
                        barCol, Color.clear, 4f);

                    string durText = $"{curDur}/{maxDur} ({durRatio * 100f:F0}%)";
                    Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(x + pad + barW + 8f, curY, 96f, 17f),
                        durText, _tipMetaStyle, barCol, 0.3f);
                    curY += durSectionH;
                }

                // Item description body
                if (hasBody)
                {
                    Aether.Nano.NanoSurface.FillRounded(new Rect(x + pad, curY, boxW - pad * 2f, 1f), Aether.Nano.NanoBrand.Line, 0.5f);
                    curY += 7f;
                    GUI.Label(new Rect(x + pad, curY, boxW - pad * 2f, bodyH + 4f), bodyContent, _tipBodyStyle);
                    curY += bodyH + 5f;
                }

                // Footer: subtle divider + value chip (left) + owner mod chip (right)
                float footerY = boxRect.yMax - footerH;
                Aether.Nano.NanoSurface.FillRounded(new Rect(x + pad, footerY - 2f, boxW - pad * 2f, 1f), Aether.Nano.NanoBrand.Line, 0.5f);

                if (item.price > 0)
                {
                    string priceText = $"◆ {item.price}";
                    _menuMeasure.text = priceText;
                    float pw = Mathf.Max(46f, _tipChipStyle.CalcSize(_menuMeasure).x + 12f);
                    Aether.Nano.NanoSurface.DrawChip(new Rect(x + pad, footerY + 4f, pw, 18f), priceText, _tipChipStyle,
                        new Color(0.14f, 0.11f, 0.055f, 0.90f),
                        new Color(Aether.Nano.NanoBrand.Warn.r, Aether.Nano.NanoBrand.Warn.g, Aether.Nano.NanoBrand.Warn.b, 0.50f),
                        Aether.Nano.NanoBrand.Warn, 5f);
                }

                string owner = Aether.Loader.Hosting.ExternalModCatalog.ResolveItemOwner(baseItem.name) ?? "Block Story";
                bool isThirdParty = !string.Equals(owner, "Block Story", StringComparison.OrdinalIgnoreCase);
                _menuMeasure.text = owner;
                float ow = Mathf.Min(boxW * 0.55f, Mathf.Max(58f, _tipChipStyle.CalcSize(_menuMeasure).x + 14f));
                Aether.Nano.NanoSurface.DrawChip(new Rect(boxRect.xMax - pad - ow, footerY + 4f, ow, 18f), owner, _tipChipStyle,
                    isThirdParty ? new Color(0.18f, 0.10f, 0.05f, 0.92f) : new Color(0.08f, 0.064f, 0.050f, 0.90f),
                    isThirdParty ? new Color(Aether.Nano.NanoBrand.Ember.r, Aether.Nano.NanoBrand.Ember.g, Aether.Nano.NanoBrand.Ember.b, 0.60f) : Aether.Nano.NanoBrand.Line,
                    isThirdParty ? Aether.Nano.NanoBrand.EmberSoft : Aether.Nano.NanoBrand.Mute, 5f);
            }
        }

        // =====================================================================
        //        WAYFINDER: CORNER MINIMAP, 3D WAYPOINTS & FULLSCREEN MAP
        // =====================================================================

        // =====================================================================
        //        NATIVE WOODEN MOD MENU (MAIN MENU & PAUSE MENU)
        // =====================================================================
        /// <summary>The Aether Nano settings page of a third-party mod (by plugin GUID, else by name), or null.</summary>
        private static Aether.Nano.NanoPage FindNanoPage(Aether.Loader.Hosting.ExternalMod ext)
            => Aether.Nano.NanoPages.Find(ext.Id) ?? Aether.Nano.NanoPages.Find(ext.Name);

        private static void EnsureModMenuStyles()
        {
            if (_menuRowTitleStyle != null) return;
            _menuRowTitleStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.BodySemi, 13, Aether.Nano.NanoBrand.Paper);
            _menuRowCompactStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.BodySemi, 12, Aether.Nano.NanoBrand.Paper);
            _menuTabStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.BodySemi, 12, Aether.Nano.NanoBrand.Paper, TextAnchor.MiddleCenter);
            _menuRowMetaStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Mute);
            _menuDetailTitleStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Display, 22, Aether.Nano.NanoBrand.Paper);
            _menuDetailMetaStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 11, Aether.Nano.NanoBrand.Mute);
            _menuDetailDescStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Body, 13, new Color(0.92f, 0.88f, 0.78f, 0.90f), TextAnchor.UpperLeft, true);
            _menuChipStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 10, Aether.Nano.NanoBrand.Mute, TextAnchor.MiddleCenter);
            _menuInitialStyle = Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Display, 19, Aether.Nano.NanoBrand.Paper, TextAnchor.MiddleCenter);
        }

        private void DrawModMenuShell(Rect winRect)
        {
            string uiTheme = HudCustomizerModule.UiDesignTheme != null ? HudCustomizerModule.UiDesignTheme.Value : "Aether";
            if (string.Equals(uiTheme, "NativeWood", StringComparison.OrdinalIgnoreCase))
            {
                DrawWoodPanel(winRect, 1.0f);
                return;
            }

            Color bgTop, bgBot, border, spec;
            switch (uiTheme)
            {
                case "AncientStone":
                    bgTop = new Color(0.085f, 0.100f, 0.125f, 0.97f);
                    bgBot = new Color(0.048f, 0.058f, 0.075f, 0.98f);
                    border = new Color(0.24f, 0.29f, 0.36f, 0.95f);
                    spec = new Color(0.86f, 0.92f, 0.98f, 0.12f);
                    break;
                case "DwarvenGold":
                    bgTop = new Color(0.110f, 0.082f, 0.042f, 0.97f);
                    bgBot = new Color(0.058f, 0.042f, 0.020f, 0.98f);
                    border = new Color(0.42f, 0.31f, 0.14f, 0.95f);
                    spec = new Color(1.0f, 0.90f, 0.66f, 0.15f);
                    break;
                case "ArcaneGlass":
                    bgTop = new Color(0.088f, 0.072f, 0.125f, 0.93f);
                    bgBot = new Color(0.044f, 0.036f, 0.068f, 0.95f);
                    border = new Color(0.32f, 0.26f, 0.44f, 0.85f);
                    spec = new Color(0.88f, 0.84f, 0.98f, 0.15f);
                    break;
                case "Aether":
                default:
                    bgTop = new Color(0.090f, 0.072f, 0.056f, 0.97f);
                    bgBot = new Color(0.047f, 0.039f, 0.031f, 0.98f);
                    border = Aether.Nano.NanoBrand.Line;
                    spec = new Color(0.98f, 0.93f, 0.85f, 0.11f);
                    break;
            }

            Aether.Nano.NanoSurface.DrawCardSurface(winRect, bgTop, bgBot, border, Aether.Nano.NanoBrand.Ember, spec, 14f, 1f, 0f, 0f);
        }

        /// <summary>Enable switch and settings button for a third-party mod, when the mod exposes them.</summary>
        private void DrawExternalModControls(Aether.Loader.Hosting.ExternalMod ext, float width, float startY)
        {
            float pad = 12f;
            float colW = Mathf.Max(220f, width - pad * 2f);
            float y = Aether.Nano.NanoSurface.DrawSectionHeader(new Rect(pad, startY, colW, 28f), startY,
                Tr("Third-party mod", "Mod de terceros"), ext.Source);

            if (ext.GetEnabled != null && ext.SetEnabled != null)
            {
                bool on = true;
                try { on = ext.GetEnabled(); } catch { }
                bool next = Aether.Nano.NanoSurface.DrawToggleRow(
                    new Rect(pad, y, Mathf.Min(340f, colW), 32f),
                    on ? Tr("Enabled", "Activado") : Tr("Disabled", "Desactivado"),
                    on);
                if (next != on)
                {
                    try { ext.SetEnabled(next); } catch { }
                }
                y += 40f;
            }

            if (ext.OpenConfig != null &&
                Aether.Nano.NanoSurface.DrawActionButton(new Rect(pad, y, Mathf.Min(240f, colW), 32f), Tr("Open mod settings", "Abrir ajustes del mod"), false))
            {
                SetModMenuOpen(false);
                try { ext.OpenConfig(); } catch { }
                return;
            }

            if (ext.OpenConfig != null) y += 42f;
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(pad, y, colW, 48f),
                Tr("Loaded by " + ext.Source + ". Aether lists it here; its own settings stay in the mod.",
                   "Cargado por " + ext.Source + ". Aether lo lista aquí; sus ajustes siguen en el mod."),
                _menuDetailDescStyle, Aether.Nano.NanoBrand.Mute, 0.3f);
        }

        private float lastCatalogRefresh;

        // Hidden motto: seven clicks on the F10 header within a few seconds show it for a moment.
        private int mottoClicks;
        private float mottoLastClick, mottoUntil;
        private const string MottoEn = "Everything is open, everything is for everyone, and we all rely on each other.";
        private const string MottoEs = "Todo es libre, todo es para todos y todos dependemos de todos.";

        private void DrawModMenuGUI(float anim = 1f)
        {
            EnsureModMenuStyles();

            // Hosted plugins start on frame 0 and can come and go; keep the list current while the menu is open.
            if (isModMenuOpen && Time.unscaledTime - lastCatalogRefresh > 2f)
            {
                lastCatalogRefresh = Time.unscaledTime;
                RefreshModCatalog();
            }

            Rect fullScreen = new Rect(0, 0, Screen.width, Screen.height);
            GUI.DrawTexture(fullScreen, GetColorTexture(new Color(0.02f, 0.015f, 0.012f, 0.74f * anim)));

            float winWidth = Mathf.Min(1020f, Screen.width - 40f);
            float winHeight = Mathf.Min(660f, Screen.height - 40f);
            float winX = (Screen.width - winWidth) * 0.5f;
            float winY = (Screen.height - winHeight) * 0.5f;
            Rect winRect = new Rect(winX, winY, winWidth, winHeight);

            using (Aether.Nano.NanoMotion.BeginModal(winRect, anim))
            {
            DrawModMenuShell(winRect);

            // Integrated top bar (no decorative top stripe)
            float topBarH = 54f;
            Rect headerHit = new Rect(winX + 16f, winY + 9f, winWidth - 220f, 36f);
            bool showMotto = Time.unscaledTime < mottoUntil;
            string metaText = showMotto
                ? Tr(MottoEn, MottoEs)
                : Tr($"{RealModCount()} loaded  ·  aether {AetherBootstrap.PluginVersion}", $"{RealModCount()} cargados  ·  aether {AetherBootstrap.PluginVersion}");

            float hx = headerHit.x + 2f;
            const float cubeH = 22f;
            if (Aether.Nano.NanoBrand.Cube != null)
                hx += Aether.Nano.NanoBrand.DrawCube(hx, headerHit.y + (headerHit.height - cubeH) * 0.5f, cubeH) + 10f;

            string titleWord = "aether";
            _aetherTitleStyle ??= Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Display, 21, Aether.Nano.NanoBrand.Paper);
            _aetherMetaStyle ??= Aether.Nano.NanoBrand.Style(Aether.Nano.NanoBrand.Mono, 11, Aether.Nano.NanoBrand.Mute);
            _menuMeasure.text = titleWord;
            float wordW = _aetherTitleStyle.CalcSize(_menuMeasure).x;
            bool headerHover = headerHit.Contains(Event.current.mousePosition);
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(hx, headerHit.y, wordW + 4f, headerHit.height),
                titleWord, _aetherTitleStyle, headerHover ? Aether.Nano.NanoBrand.Ember : Aether.Nano.NanoBrand.Paper, 0.4f);
            hx += wordW + 12f;
            Aether.Nano.NanoSurface.FillRounded(new Rect(hx, headerHit.y + 10f, 1f, 16f), Aether.Nano.NanoBrand.Line, 0.5f);
            hx += 12f;
            Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(hx, headerHit.y + 1f, Mathf.Max(40f, headerHit.xMax - hx), headerHit.height),
                metaText, _aetherMetaStyle, showMotto ? Aether.Nano.NanoBrand.EmberSoft : Aether.Nano.NanoBrand.Mute, 0.3f);

            if (GUI.Button(headerHit, GUIContent.none, GUIStyle.none))
            {
                mottoClicks = Time.unscaledTime - mottoLastClick < 1.5f ? mottoClicks + 1 : 1;
                mottoLastClick = Time.unscaledTime;
                if (mottoClicks >= 7) { mottoClicks = 0; mottoUntil = Time.unscaledTime + 6f; }
            }

            Rect dragHudBtnRect = new Rect(winX + winWidth - 188f, winY + 12f, 134f, 30f);
            if (Aether.Nano.NanoSurface.DrawActionButton(dragHudBtnRect, Tr("Edit HUD", "Editar HUD"), false))
            {
                if (Aether.Nano.NanoHud.OpenCanvasEditor != null)
                {
                    SetModMenuOpen(false);
                    Aether.Nano.NanoHud.OpenCanvasEditor();
                }
                else
                {
                    SetDraggingHudElements(true);
                }
                return;
            }

            Rect closeBtnRect = new Rect(winX + winWidth - 46f, winY + 12f, 32f, 30f);
            if (Aether.Nano.NanoSurface.DrawActionButton(closeBtnRect, "×", false))
            {
                SetModMenuOpen(false);
                return;
            }

            // Clean 1px hairline below top bar
            Aether.Nano.NanoSurface.FillRounded(new Rect(winX + 14f, winY + topBarH, winWidth - 28f, 1f), Aether.Nano.NanoBrand.Line, 0.5f);

            // Body layout: Left recessed mod sidebar + Right recessed detail/settings column
            float leftW = 316f;
            float contentY = winY + topBarH + 10f;
            float contentH = winHeight - topBarH - 24f;

            Rect leftPanelBg = new Rect(winX + 14f, contentY, leftW, contentH);
            Aether.Nano.NanoSurface.DrawDebossedSlot(leftPanelBg, new Color(0.038f, 0.030f, 0.024f, 0.88f), Aether.Nano.NanoBrand.Line, 10f);

            Rect listRect = new Rect(leftPanelBg.x + 6f, leftPanelBg.y + 6f, leftW - 12f, contentH - 12f);
            float cardStep = 62f;
            float totalListH = registeredMods.Count * cardStep;

            scrollListPos = Aether.Nano.NanoSurface.BeginScrollView(listRect, scrollListPos, totalListH, out float rowW);

            for (int i = 0; i < registeredMods.Count; i++)
            {
                var mod = registeredMods[i];
                bool isSelected = (i == selectedModIndex);
                string modTitle = IsSpanish() ? mod.NameEs : mod.NameEn;
                string listTitle = CompactListTitle(mod.Id, modTitle);

                Rect cardRect = new Rect(0f, i * cardStep, rowW, 56f);
                if (GUI.Button(cardRect, GUIContent.none, GUIStyle.none))
                {
                    SelectModRow(i);
                }

                if (Event.current.type == EventType.Repaint)
                {
                    bool hovered = cardRect.Contains(Event.current.mousePosition);
                    if (isSelected)
                    {
                        Aether.Nano.NanoSurface.FillRoundedGradient(cardRect,
                            new Color(0.155f, 0.120f, 0.094f, 0.98f),
                            new Color(0.100f, 0.078f, 0.062f, 0.98f), 9f);
                        Aether.Nano.NanoSurface.FillRounded(new Rect(cardRect.x + 7f, cardRect.y + 1f, cardRect.width - 14f, 1f),
                            new Color(1f, 0.95f, 0.88f, 0.10f), 0.5f);
                        Aether.Nano.NanoSurface.StrokeRounded(cardRect,
                            new Color(Aether.Nano.NanoBrand.Ember.r, Aether.Nano.NanoBrand.Ember.g, Aether.Nano.NanoBrand.Ember.b, 0.55f), 1f, 9f);
                    }
                    else
                    {
                        Color bg = hovered
                            ? new Color(0.115f, 0.090f, 0.072f, 0.92f)
                            : new Color(0.070f, 0.056f, 0.045f, 0.78f);
                        Color bd = hovered ? new Color(0.28f, 0.22f, 0.17f, 0.90f) : Aether.Nano.NanoBrand.Line;
                        Aether.Nano.NanoSurface.FillRounded(cardRect, bg, 9f);
                        Aether.Nano.NanoSurface.StrokeRounded(cardRect, bd, 1f, 9f);
                    }

                    // Debossed squircle logo slot (custom PNG icon -> Aether Cube -> game item -> monogram)
                    Rect logoSlot = new Rect(cardRect.x + 8f, cardRect.y + 8f, 40f, 40f);
                    DrawModLogoSlot(logoSlot, mod, listTitle, isSelected, false);

                    // Active state sign (filled Ember rhombus, zero side/top stripes)
                    float textX = cardRect.x + 56f;
                    float textMaxW = cardRect.width - 134f;
                    float nameX = textX;
                    if (isSelected)
                    {
                        Aether.Nano.NanoSurface.DrawRhombus(new Rect(nameX, cardRect.y + 15f, 7f, 7f), Aether.Nano.NanoBrand.Ember, true);
                        nameX += 11f;
                    }

                    GUIStyle rowNameStyle = (listTitle != null && listTitle.Length > 18) ? _menuRowCompactStyle : _menuRowTitleStyle;
                    Aether.Nano.NanoSurface.DrawCrispLabel(
                        new Rect(nameX, cardRect.y + 8f, Mathf.Max(40f, textX + textMaxW - nameX), 20f),
                        listTitle, rowNameStyle, isSelected ? Aether.Nano.NanoBrand.PaperLight : Aether.Nano.NanoBrand.Paper, 0.4f);

                    int moduleCount = mod.FeatureIds != null ? mod.FeatureIds.Length : 0;
                    string versionLine = $"v{mod.Version}";
                    if (moduleCount > 1)
                        versionLine += Tr($" · {moduleCount} mods", $" · {moduleCount} mód.");
                    Aether.Nano.NanoSurface.DrawCrispLabel(
                        new Rect(textX, cardRect.y + 30f, textMaxW, 16f),
                        versionLine, _menuRowMetaStyle, Aether.Nano.NanoBrand.Mute, 0.3f);

                    DrawOriginBadge(cardRect, mod);
                }
            }

            Aether.Nano.NanoSurface.EndScrollView();

            // Right Panel: Mod Details & Interactive Settings
            float rightX = leftPanelBg.xMax + 10f;
            float rightW = winX + winWidth - 14f - rightX;
            Rect rightPanelBg = new Rect(rightX, contentY, rightW, contentH);
            Aether.Nano.NanoSurface.DrawDebossedSlot(rightPanelBg, new Color(0.040f, 0.032f, 0.026f, 0.88f), Aether.Nano.NanoBrand.Line, 10f);

            Rect rightRect = new Rect(rightPanelBg.x + 6f, rightPanelBg.y + 6f, rightW - 12f, contentH - 12f);

            if (selectedModIndex >= 0 && selectedModIndex < registeredMods.Count)
            {
                var mod = registeredMods[selectedModIndex];
                string modTitle = IsSpanish() ? mod.NameEs : mod.NameEn;
                string modDesc = IsSpanish() ? mod.DescEs : mod.DescEn;

                bool hasModules = mod.FeatureIds != null && mod.FeatureIds.Length > 0;
                bool hasModulePicker = mod.FeatureIds != null && mod.FeatureIds.Length > 1;
                float pickerH = hasModulePicker ? 46f : 0f;
                float settingsBaseY = 178f + pickerH;

                Aether.Nano.NanoPage nanoPage = mod.External != null ? FindNanoPage(mod.External) : null;
                float panelHeight = nanoPage != null
                    ? nanoPage.ContentHeight
                    : selectedFeatureId == HealthFeatureId
                    ? 1240f
                    : 640f;
                float detailContentHeight = Mathf.Max(contentH - 16f, settingsBaseY + panelHeight);

                scrollDetailPos = Aether.Nano.NanoSurface.BeginScrollView(rightRect, scrollDetailPos, detailContentHeight, out float innerW);
                try
                {

                // Hero header for selected mod
                float pad = 12f;
                Rect bigLogo = new Rect(pad, 10f, 52f, 52f);
                DrawModLogoSlot(bigLogo, mod, modTitle, true, true);

                float hTextX = bigLogo.xMax + 14f;
                float hTextW = Mathf.Max(120f, innerW - hTextX - pad);
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(hTextX, 9f, hTextW, 28f), modTitle, _menuDetailTitleStyle, Aether.Nano.NanoBrand.Paper, 0.45f);

                string metaLine = $"v{mod.Version}  ·  {mod.Author}";
                if (mod.LoadOrder > 0) metaLine += $"  ·  #{mod.LoadOrder}";
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(hTextX, 39f, hTextW, 18f), metaLine, _menuDetailMetaStyle, Aether.Nano.NanoBrand.Mute, 0.3f);

                // Compact mono metadata chips (ID + Source file)
                float chipX = pad;
                float chipY = 72f;
                chipX = DrawInfoChip(chipX, chipY, innerW - pad, mod.Id);
                if (!string.IsNullOrEmpty(mod.SourcePath))
                    chipX = DrawInfoChip(chipX, chipY, innerW - pad, CompactSourcePath(mod.SourcePath));
                if (!string.IsNullOrEmpty(mod.Dependencies))
                    DrawInfoChip(chipX, chipY, innerW - pad, Tr("Requires: ", "Req: ") + mod.Dependencies);

                // Description
                Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(pad, 98f, innerW - pad * 2f, 62f),
                    modDesc, _menuDetailDescStyle, new Color(0.92f, 0.88f, 0.78f, 0.90f), 0.3f);

                // Hairline divider
                Aether.Nano.NanoSurface.FillRounded(new Rect(pad, 166f, innerW - pad * 2f, 1f), Aether.Nano.NanoBrand.Line, 0.5f);

                // Built-in suite module tabs (horizontal squircle bar, zero green wood buttons)
                if (hasModulePicker)
                {
                    float barY = 176f;
                    float barW = innerW - pad * 2f;
                    var barRect = new Rect(pad, barY, barW, 32f);
                    if (Event.current.type == EventType.Repaint)
                        Aether.Nano.NanoSurface.DrawDebossedSlot(barRect, new Color(0.055f, 0.044f, 0.036f, 0.90f), Aether.Nano.NanoBrand.Line, 8f);

                    int count = mod.FeatureIds.Length;
                    float segGap = 3f;
                    float segW = (barW - 6f - segGap * (count - 1)) / Mathf.Max(1, count);
                    for (int f = 0; f < count; f++)
                    {
                        if (!TryGetSuiteFeature(mod.FeatureIds[f], out ModInfoEntry feature)) continue;
                        bool isSel = string.Equals(selectedFeatureId, feature.Id, StringComparison.OrdinalIgnoreCase);
                        var segRect = new Rect(barRect.x + 3f + f * (segW + segGap), barRect.y + 3f, segW, barRect.height - 6f);
                        bool hov = segRect.Contains(Event.current.mousePosition);
                        if (GUI.Button(segRect, GUIContent.none, GUIStyle.none) && !isSel)
                            SelectModRow(selectedModIndex, feature.Id);

                        if (Event.current.type == EventType.Repaint)
                        {
                            if (isSel)
                            {
                                Aether.Nano.NanoSurface.FillRoundedGradient(segRect,
                                    new Color(0.19f, 0.148f, 0.115f, 0.98f),
                                    new Color(0.125f, 0.098f, 0.076f, 0.98f), 6f);
                                Aether.Nano.NanoSurface.StrokeRounded(segRect,
                                    new Color(Aether.Nano.NanoBrand.Ember.r, Aether.Nano.NanoBrand.Ember.g, Aether.Nano.NanoBrand.Ember.b, 0.52f), 1f, 6f);
                            }
                            else if (hov)
                            {
                                Aether.Nano.NanoSurface.FillRounded(segRect, new Color(0.13f, 0.10f, 0.08f, 0.65f), 6f);
                            }

                            string tabLabel = Tr(feature.NameEn, feature.NameEs);
                            Aether.Nano.NanoSurface.DrawCrispLabel(segRect, tabLabel,
                                _menuTabStyle,
                                isSel ? Aether.Nano.NanoBrand.PaperLight : (hov ? Aether.Nano.NanoBrand.Paper : Aether.Nano.NanoBrand.Mute), 0.3f);
                        }
                    }
                }
                else if (nanoPage != null)
                {
                    // A mod built on Aether Nano: its settings page is drawn right here, like Aether's own mods.
                    try { nanoPage.Draw(new Rect(0f, 0f, innerW, nanoPage.ContentHeight), settingsBaseY); }
                    catch (Exception ex) { Log.LogWarning($"[Aether] Settings page of {nanoPage.Name} failed: {ex.Message}"); }
                }
                else if (mod.External != null)
                {
                    DrawExternalModControls(mod.External, innerW, settingsBaseY);
                }
                else if (!hasModules)
                {
                    float ny = Aether.Nano.NanoSurface.DrawSectionHeader(new Rect(pad, settingsBaseY, innerW - pad * 2f, 28f), settingsBaseY,
                        Tr("No configurable settings", "Sin ajustes configurables"));
                    Aether.Nano.NanoSurface.DrawCrispLabel(new Rect(pad, ny, innerW - pad * 2f, 52f),
                        Tr("This mod is loaded and running. It does not register any native suite settings panel.",
                           "Este mod está cargado y en ejecución. No registra ningún panel de ajustes de la suite nativa."),
                        _menuDetailDescStyle, Aether.Nano.NanoBrand.Mute, 0.3f);
                }

                // Interactive Settings Section for built-in Aether shell features
                if (selectedFeatureId == "durability")
                {
                    float colW = innerW - pad * 2f;
                    float sy = Aether.Nano.NanoSurface.DrawSectionHeader(new Rect(pad, settingsBaseY, colW, 28f), settingsBaseY,
                        Tr("Item tooltips & durability", "Tooltips y durabilidad"));

                    float halfW = (colW - 8f) * 0.5f;
                    HoverTooltipEnabled.Value = Aether.Nano.NanoSurface.DrawToggleRow(
                        new Rect(pad, sy, halfW, 32f),
                        Tr("Rich Hover Tooltip", "Tooltip al pasar ratón"),
                        HoverTooltipEnabled.Value);
                    DurabilityEnabled.Value = Aether.Nano.NanoSurface.DrawToggleRow(
                        new Rect(pad + halfW + 8f, sy, halfW, 32f),
                        Tr("Numeric Durability", "Durabilidad numérica"),
                        DurabilityEnabled.Value);
                }
                else if (selectedFeatureId == "modmenu")
                {
                    bool es = IsSpanish();
                    float colW = innerW - pad * 2f;
                    float halfW = (colW - 8f) * 0.5f;
                    float sy = Aether.Nano.NanoSurface.DrawSectionHeader(new Rect(pad, settingsBaseY, colW, 28f), settingsBaseY,
                        es ? "Diseño del HUD" : "HUD layout", HudLayoutPreset.Value);

                    if (Aether.Nano.NanoSurface.DrawSegmented(new Rect(pad, sy, colW, 30f),
                            es ? HudPresetSegmentsEs : HudPresetSegmentsEn, HudLayoutPreset.Value, out string preset))
                    {
                        ApplyHudPreset(preset);
                    }
                    sy += 38f;

                    bool nextDia = Aether.Nano.NanoSurface.DrawToggleRow(
                        new Rect(pad, sy, halfW, 32f),
                        es ? "Diamantes bajo XP" : "Diamonds Under XP",
                        MoveDiamondsUnderXP.Value);
                    if (nextDia != MoveDiamondsUnderXP.Value)
                    {
                        MoveDiamondsUnderXP.Value = nextDia;
                        HudLayoutPreset.Value = "Custom";
                    }

                    bool nextBtns = Aether.Nano.NanoSurface.DrawToggleRow(
                        new Rect(pad + halfW + 8f, sy, halfW, 32f),
                        es ? "Botones arriba → abajo" : "Top Buttons → Bottom",
                        MoveTopRightButtonsToBottomRight.Value);
                    if (nextBtns != MoveTopRightButtonsToBottomRight.Value)
                    {
                        MoveTopRightButtonsToBottomRight.Value = nextBtns;
                        HudLayoutPreset.Value = "Custom";
                    }
                    sy += 44f;

                    sy = Aether.Nano.NanoSurface.DrawSectionHeader(new Rect(pad, sy, colW, 28f), sy,
                        es ? "Estilo de ventanas" : "Window style", HudCustomizerModule.UiDesignTheme.Value);

                    if (Aether.Nano.NanoSurface.DrawSegmented(new Rect(pad, sy, colW, 30f),
                            es ? UiThemeRow1Es : UiThemeRow1En, HudCustomizerModule.UiDesignTheme.Value, out string t1))
                    {
                        HudCustomizerModule.UiDesignTheme.Value = t1;
                        Aether.Nano.NanoThemes.Default = t1;
                        Config.Save();
                    }
                    sy += 34f;
                    if (Aether.Nano.NanoSurface.DrawSegmented(new Rect(pad, sy, colW, 30f),
                            es ? UiThemeRow2Es : UiThemeRow2En, HudCustomizerModule.UiDesignTheme.Value, out string t2))
                    {
                        HudCustomizerModule.UiDesignTheme.Value = t2;
                        Aether.Nano.NanoThemes.Default = t2;
                        Config.Save();
                    }
                    sy += 44f;

                    sy = Aether.Nano.NanoSurface.DrawSectionHeader(new Rect(pad, sy, colW, 28f), sy,
                        es ? "Herramientas" : "Tools");

                    float btn3W = (colW - 16f) / 3f;
                    if (Aether.Nano.NanoSurface.DrawActionButton(new Rect(pad, sy, btn3W, 32f),
                            es ? "Editar posiciones HUD" : "Edit HUD Layout", true))
                    {
                        if (Aether.Nano.NanoHud.OpenCanvasEditor != null)
                        {
                            SetModMenuOpen(false);
                            Aether.Nano.NanoHud.OpenCanvasEditor();
                            return;
                        }
                        isModMenuOpen = false;
                        isDraggingHudElements = true;
                    }
                    if (Aether.Nano.NanoSurface.DrawActionButton(new Rect(pad + btn3W + 8f, sy, btn3W, 32f),
                            es ? "Gestor de teclas (F7)" : "Keybinds (F7)", false))
                    {
                        SetModMenuOpen(false);
                        SetKeybindsManagerOpen(true);
                        return;
                    }
                    if (Aether.Nano.NanoSurface.DrawActionButton(new Rect(pad + (btn3W + 8f) * 2f, sy, btn3W, 32f),
                            es ? "Restablecer HUD" : "Reset HUD", false))
                    {
                        HudCustomizerModule.ResetAllHudToDefaults();
                    }
                }
                else if (selectedFeatureId == "blueprint")
                {
                    DrawBlueprintModMenuSettings(rightRect, settingsBaseY);
                }
                else if (selectedFeatureId == "sodium")
                {
                    DrawSodiumSettings(rightRect, settingsBaseY);
                }
                else if (selectedFeatureId == "lithium")
                {
                    DrawLithiumSettings(rightRect, settingsBaseY);
                }
                else if (selectedFeatureId == "entityculling")
                {
                    DrawEntityCullingSettings(rightRect, settingsBaseY);
                }
                else if (selectedFeatureId == "ferritecore")
                {
                    DrawFerriteCoreSettings(rightRect, settingsBaseY);
                }
                else if (selectedFeatureId == "dusk")
                {
                    DrawDuskModMenuSettings(rightRect, settingsBaseY);
                }
                else if (selectedFeatureId == HealthFeatureId)
                {
                    DrawModHealthPanel(rightRect, settingsBaseY);
                }

                }
                finally
                {
                    Aether.Nano.NanoSurface.EndScrollView();
                }
            }

            if (isModMenuOpen && Event.current.type == EventType.MouseDown && (Time.unscaledTime - modalOpenedTimestamp > 0.35f) && !winRect.Contains(Event.current.mousePosition))
            {
                SetModMenuOpen(false);
                Event.current.Use();
            }
            }
        }

        private static readonly Dictionary<string, Assembly> _loadedAsmByName = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        private static Assembly FindLoadedAssembly(string assemblyName, string sourcePath)
        {
            string key = !string.IsNullOrEmpty(assemblyName) ? assemblyName : (sourcePath ?? string.Empty);
            if (string.IsNullOrEmpty(key)) return null;
            if (_loadedAsmByName.TryGetValue(key, out Assembly cached)) return cached;

            Assembly found = null;
            try
            {
                string stem = !string.IsNullOrEmpty(assemblyName)
                    ? assemblyName
                    : Path.GetFileNameWithoutExtension(sourcePath);
                foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm == null || asm.IsDynamic) continue;
                    if (string.Equals(asm.GetName().Name, stem, StringComparison.OrdinalIgnoreCase))
                    {
                        found = asm;
                        break;
                    }
                }
            }
            catch { }

            _loadedAsmByName[key] = found;
            return found;
        }

        private static Texture2D ResolveModIcon(ModInfoEntry mod)
        {
            if (mod.IconTexture != null) return mod.IconTexture;

            if (mod.External != null)
            {
                Aether.Nano.NanoPage page = FindNanoPage(mod.External);
                if (page != null && page.Icon != null) return page.Icon;
                Assembly extAsm = FindLoadedAssembly(mod.External.AssemblyName, mod.SourcePath);
                return Aether.Nano.NanoIcons.Resolve(extAsm, mod.SourcePath, mod.IconHint);
            }

            if (!mod.IsFeatureRow && !string.IsNullOrEmpty(mod.SourcePath))
            {
                Assembly modAsm = FindLoadedAssembly(null, mod.SourcePath);
                return Aether.Nano.NanoIcons.Resolve(modAsm, mod.SourcePath, mod.IconHint);
            }

            return null;
        }

        private void DrawModLogoSlot(Rect slotRect, ModInfoEntry mod, string title, bool isSelected, bool isHero)
        {
            float slotRadius = isHero ? 10f : 7f;
            Color bg = isHero ? new Color(0.055f, 0.044f, 0.034f, 0.94f) : new Color(0.045f, 0.036f, 0.028f, 0.92f);
            Aether.Nano.NanoSurface.DrawDebossedSlot(slotRect, bg, Aether.Nano.NanoBrand.Line, slotRadius);

            float inset = isHero ? 5f : 4f;
            var inner = new Rect(slotRect.x + inset, slotRect.y + inset, slotRect.width - inset * 2f, slotRect.height - inset * 2f);

            // 1. Custom mod PNG icon (NanoPage.Icon, embedded icon.png in DLL, or sidecar <Mod>.png)
            Texture2D customIcon = ResolveModIcon(mod);
            if (customIcon != null)
            {
                Aether.Nano.NanoIcons.Draw(inner, customIcon, isHero ? 6f : 4f);
                return;
            }

            // 2. Aether brand cube for Aether Core & Aether UI Suite
            if (mod.UseAetherCube && Aether.Nano.NanoBrand.Cube != null)
            {
                float ch = inner.height * 0.88f;
                float cw = ch * Aether.Nano.NanoBrand.Cube.width / Mathf.Max(1f, Aether.Nano.NanoBrand.Cube.height);
                Aether.Nano.NanoBrand.DrawCube(slotRect.x + (slotRect.width - cw) * 0.5f, slotRect.y + (slotRect.height - ch) * 0.5f, ch);
                return;
            }

            // 3. Mod Health distinctive emblem (emerald nested rhombus)
            if (string.Equals(mod.Id, HealthModId, StringComparison.OrdinalIgnoreCase))
            {
                float outer = isHero ? 20f : 15f;
                float innerSz = isHero ? 10f : 7f;
                Aether.Nano.NanoSurface.DrawRhombus(
                    new Rect(slotRect.center.x - outer * 0.5f, slotRect.center.y - outer * 0.5f, outer, outer),
                    Aether.Nano.NanoBrand.Ok, false);
                Aether.Nano.NanoSurface.DrawRhombus(
                    new Rect(slotRect.center.x - innerSz * 0.5f, slotRect.center.y - innerSz * 0.5f, innerSz, innerSz),
                    Aether.Nano.NanoBrand.Ok, true);
                return;
            }

            // 4. Optional Block Story item icon
            if (!string.IsNullOrEmpty(mod.IconItemName))
            {
                InvBaseItem iconItem = SafeFindItemByName(mod.IconItemName);
                if (DrawItemIcon(inner, iconItem))
                    return;
            }

            // 5. Crisp Gloock monogram fallback
            string initial = string.IsNullOrEmpty(title) ? "?" : title.Substring(0, 1).ToUpperInvariant();
            GUIStyle monogramStyle = isHero ? _menuDetailTitleStyle : _menuInitialStyle;
            Color ink = isSelected ? Aether.Nano.NanoBrand.Ember : Aether.Nano.NanoBrand.Paper;
            Aether.Nano.NanoSurface.DrawCrispLabel(slotRect, initial, monogramStyle, ink, isHero ? 0.4f : 0.35f);
        }

        private static float DrawInfoChip(float x, float y, float maxX, string text)
        {
            if (string.IsNullOrEmpty(text)) return x;
            _menuMeasure.text = text;
            float w = Mathf.Min(maxX - x, _menuChipStyle.CalcSize(_menuMeasure).x + 14f);
            if (w < 28f) return x;
            Aether.Nano.NanoSurface.DrawChip(new Rect(x, y, w, 18f), text, _menuChipStyle,
                new Color(0.075f, 0.060f, 0.048f, 0.92f), Aether.Nano.NanoBrand.Line, Aether.Nano.NanoBrand.Mute, 4f);
            return x + w + 6f;
        }
    }

    // =========================================================================
    //                            HARMONY PATCHES
    // =========================================================================

    // Harmony Patch 1: Block NGUI UICamera.Update while ModMenu, Map, or HUD Dragging is open!
    [HarmonyPatch(typeof(UICamera), "Update")]
    public static class UICameraUpdateBlockerPatch
    {
        public static bool Prefix()
        {
            if (BlockStorySuitePlugin.Instance != null && BlockStorySuitePlugin.Instance.IsModalOpen)
                return false;
            return true;
        }
    }

    // Harmony Patch 2: Block NGUI touches while modal is open OR when clicking MODS button!
    [HarmonyPatch(typeof(UICamera), "ProcessTouch")]
    public static class UICameraProcessTouchBlockerPatch
    {
        public static bool Prefix(bool pressed, bool unpressed)
        {
            if (BlockStorySuitePlugin.Instance != null)
            {
                if (BlockStorySuitePlugin.Instance.IsModalOpen)
                    return false;
                if (BlockStorySuitePlugin.Instance.IsMouseOverModsButton())
                    return false;
            }
            return true;
        }
    }

    // Harmony Patch 3: Block Inventory.Update (Escape/E keys) while ModMenu or Wayfinder is open!
    [HarmonyPatch(typeof(Inventory), "Update")]
    public static class InventoryUpdateModalBlockerPatch
    {
        public static bool Prefix()
        {
            if (BlockStorySuitePlugin.Instance != null && BlockStorySuitePlugin.Instance.IsModalOpen)
                return false;
            return true;
        }
    }

    // Harmony Patch 4: Append TRUE remaining durability (topStack / durability) to vanilla itemDesc
    [HarmonyPatch(typeof(InvGameItem), "get_itemDesc")]
    public static class DurabilityTooltipPatch
    {
        public static void Postfix(InvGameItem __instance, ref string __result)
        {
            if (!BlockStorySuitePlugin.DurabilityEnabled.Value || __instance == null) return;
            try
            {
                var baseItem = __instance.baseItem;
                if (baseItem != null && baseItem.durability > 1)
                {
                    int maxDur = baseItem.durability;
                    int curDur = __instance.topStack;
                    float pct = Mathf.Clamp01((float)curDur / maxDur) * 100f;
                    string col = pct > 55f ? "[00FF00]" : (pct > 25f ? "[FFFF00]" : "[FF4444]");
                    string label = BlockStorySuitePlugin.Tr("Durability", "Durabilidad");
                    __result += $"\n{col}{label}: {curDur} / {maxDur} ({pct:F0}%)[-]";
                }
            }
            catch { }
        }
    }

    // Harmony Patch 5: Ensure native UICustomCursor always renders in front of all mod modals
    [HarmonyPatch(typeof(UICustomCursor), "OnGUI")]
    public static class UICustomCursor_OnGUI_Patch
    {
        public static void Prefix()
        {
            if (BlockStorySuitePlugin.Instance != null && BlockStorySuitePlugin.Instance.IsModalOpen)
            {
                GUI.depth = -5000;
            }
        }
    }
}
