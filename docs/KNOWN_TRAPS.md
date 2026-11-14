# ✦ Known Traps — Block Story / Aether

> **Purpose:** fast checklist of things that have already caused bugs or wasted QA time.
> **Rule:** if a trap here contradicts old comments in code, this file wins; update the code comments too.

---

## Native UI / NGUI

| Trap | Symptom | Correct behavior |
| :--- | :--- | :--- |
| Preferring `BlockStoryInfinity` | Card looks sans-serif while the log says the font was applied | Prefer `Zantroke`; then dynamic `UILabel` fonts; use `BlockStoryInfinity` only as fallback |
| Trusting the “font applied” log | Invisible or wrong font | Verify visually or extract/inspect the font |
| Matching heart sprites by substring `life` | `Life Magic` or `NecklaceOfLife` selected | Match exact `HeartFull` / `HeartEmpty` and require the same atlas |
| Tinting native `HeartEmpty` dark | Empty hearts look invisible | Draw the native empty sprite untinted |
| Positional card reveal | Card “jumps” on every target change | No positional reveal; instant target change |
| Animating mob reveal by world position | Moving mob restarts the reveal forever | Never use target position in a motion key |
| Tool badge looks mirrored | Check/X reads backwards | Rotate 180° and mirror horizontally in the generated badge texture |
| HP text clipped or off-center | HP numbers look blurry/cut | Use 16 px bar height (12 compact) and the full inner text width; center the bar |
| Allocating in `OnGUI` | Frame spikes, GC churn | Cache textures, styles, fonts and atlas UVs |
| Trusting item translations directly | UI shows `{0} Antique Spawner` | If a translation contains `{0}`, use the concrete item name |
| Letting an OnGUI exception escape | Unity's Mono aborts while formatting the stack trace (no message, game closes) | Wrap atlas draws in try/catch and log `ex.GetType().Name + ex.Message` only; never `ToString()` |

## Wayfinder

| Trap | Symptom | Correct behavior |
| :--- | :--- | :--- |
| Projecting markers from a different span than the terrain | Mob/waypoint dots drift off terrain features | Use the same view centre and `pxPerBlock` that `DrawView` receives |
| Different zoom clamps per control | Bottom bar reaches 800 %, toolbar snaps back to 400 % | Use `WayfinderMath.ClampZoom` in wheel, toolbar and bottom bar |
| Showing Y controls in Cave mode | `-1/+1` appear to do nothing | Auto/Cave follow player Y; only Slice reads `SliceY` |
| Rendering terrain from every OnGUI event | Texture uploads occur 2–3× per frame | Upload only on `EventType.Repaint`; throttle drag to 0.05 s |
| Fixed-width minimap control bar | `+`/`J` drawn outside the card at 180 px | Hide optional groups by measured width; keep mode and `J` |
| Pinning the fullscreen layer badge at `canvas - 116` | Badge covers zoom at 1280×720 | Draw it only when the canvas can hold controls + badge |
| Caching unsampled/unloaded columns | Dark tiles never fill in when chunks stream | Only cache successful live samples; show store data or retry |
| Invalidating the tile cache on mode/slice/grid change | Re-sampling hitch on every click | Keys already encode mode/slice; invalidate only on world change |
| Rendering every missing chunk in one frame | Frame hitch on map open or a big zoom-out | At most 4 missing/stale chunks per Repaint (2.5 ms, nearest to centre) |
| Storing waypoints in the global `.cfg` | Every world shares the same list | Per-world `aether_wayfinder/waypoints.txt`; migrate once |
| Writing map data to `BepInEx/config` | Data follows the user across worlds | Use the native world directory (`FSWorldPersistence.directory`) |
| Trusting `Radar.points` for mobs | Duplicate dots with the Health scan | Use the registry only for POIs; mobs come from `MOBManagement` children |
| Calling `FindObjectsOfType<Health>` in a timed scan | Periodic hitch with many mobs | Iterate `MOBManagement.transform` children |
| Resampling terrain on every pan/zoom step | Distortion, flicker, bottom-to-top sweep | Pan/zoom only move/scale world-anchored chunk quads |
| Treating black map areas as missing chunks | False “map broken” report | Black = unexplored / no persisted cell; the panel says so |
| Retrying failed columns every frame | Cave mode sweeps forever when a slice has no data | Mark the pixel unexplored; the whole chunk is retried only after `StaleSeconds` (5 s) |
| Recording surface while underground/indoors | Reveals terrain the player never visited (x-ray) | Require `!underground && !indoors` before `RecordSurface`; `AlwaysMapSurface` is deprecated/no-op |
| Assuming Cave/Slice are live-only | Map looks inconsistent across layers | Surface is saved in `r.*.bin`; cave/slice are saved per band in `cave/b*/` and read as fallback |
| Re-rendering one full map texture on every refresh | Minimap flicker, zoom distortion, bottom-to-top sweep | Chunk atlas: per-chunk 32×32 textures drawn as world-anchored quads |
| `ScaleMode.ScaleToFit` on a rounded chunk rect | 1 px near-black seam line on chunk borders | `StretchToFill`; a 179×180 rect must cover its full area |
| Shading a chunk from interior pixels only | Bright/dark seams on every chunk edge | Sample a 1-block halo (stride 34) for neighbours; upload only the interior |
| Encoding “already drawn” in `BlockId` | Real blocks vanish or colours corrupt (`BlockType.id` is a `ushort`) | Parallel `bool[] finalPixel` + `TerrainSample.FromCache` |
| Sampling cave chunks at the raw `sliceY` | Every small Y move clears/re-renders all chunks | Quantize to the 16-block band floor; key the atlas `mode:band` |
| Drawing square chunk quads in Circle shape | Terrain spills past the round bezel | CPU-compose the circle texture and alpha-cut outside the bezel's inner radius |
| Compositing the circle on every OnGUI event | Texture upload runs on Layout and Repaint (double cost) | Compose/apply only on `Repaint`; draw the last texture otherwise |
| Expecting the atlas to fill at once after a big zoom-out | Dark area lingers at 25 % zoom | Budget is 4 chunks / 2.5 ms per Repaint; it is not a render bug |
| Drawing radar POIs/mobs without a fog test | Beds/NPCs visible through unexplored black (x-ray) | Require `WayfinderChunkAtlas.IsRenderedExplored` (rendered pixel + half fade) |
| Trusting isolated surface store cells | A dotted grid of 4×4 cells appears in black areas | Density test: at least 3 known neighbours in the 3×3 cell ring |
| Rendering a new chunk at full alpha | Hard “pop” during cold fill / layer switch | 0.28 s smoothstep fade on first render (quads and circle compositor) |
| Markers popping before their terrain | Dots float on a still-fading chunk | `IsRenderedExplored` waits for 50 % of the fade |
| Toggling the grid without keying it | Map shows stale colours with/without lines | Put the chunk/region bits (16/64) in the atlas mode and tile-cache key |
| Long grid labels in the 80 px button | “# Regions”/“# Apagada” clip | Use `# Off`, `# Chunk`, `# Region`, `# Both` |
| Drawing the info card at 720p without checking | Covers terrain or waypoint labels | Keep 208×38 top-left; verify at 720p/800×600 |
| Reading `DayTimeManager.instance` or block light unguarded | Menu/transition NullReference | Guard the instance and `GetBlockSafe`; fall back to `--` |
| Detecting “indoors” from one column | Semi-open halls/bridges are treated as closed; Auto shows Cave | 8 rays at feet+1 and feet+2; ≥7/8 directions with walls at BOTH heights + roof depth; debounce 0.45 s |
| Counting a single overhang or 1-high fence as a wall | Standing under one block triggers the cave cutaway | Require the wall at feet+1 **and** feet+2; a roof alone is Surface |
| Waiting for Auto to detect a skylit iceberg/room | Auto stays Surface because the sky is visible | Select the manual **Interior** layer (current-Y slice + dim surface) |
| Tying beacons to `ShowWorldWaypoints` | Turning off 3D markers also hides the beams | Keep `ShowWaypointBeacons` independent and call it separately |
| Expecting every waypoint to have a beam | Beams are capped at 96 blocks; 96–384 blocks show a name pill only | Distant WPs still appear as fullscreen-map edge markers |
| Expecting beacons to be depth-occluded | Beams show through terrain (IMGUI has no depth test) | Opt-in default off; a true depth test needs a throttled voxel ray, not `Physics.Linecast` on unloaded chunks |
| Assuming waypoint Y is feet for old entries | Old quick/form waypoints stored the camera/head Y; new ones store feet Y | Beacons resolve the floor for display |
| Applying the interior cutaway inside a cave | A shallow roof/wall made Auto show the overworld above the cave | Auto/Cave require the explored surface within 8 blocks; deep caves stay cave-only, `Interior` forces |
| Exporting with live world sampling | Reveals unvisited terrain; multi-second region churn | Read only the surface store, on demand, cell scan + next-frame request |
| Assuming saved cave cells are 1:1 | Cave store is 4×4-block; reloaded caves are blockier than live | Accept fallback precision; a 1-block store would multiply files/cells by 16 |
| Expecting block edits to update cave cells immediately | Only live samples update; non-canonical samples merely seed empty cells | Let live data win when the chunk is loaded; hook place/destroy for persisted invalidation |
| Using `HasAnyCaveCell` with sparse hand-made cells | The lazy fog assumes whole rendered chunks were recorded densely | Keep recording whole chunks; add a density threshold if external cells are ever imported |
| Enabling cave persistence mid-session | Tile-cache hits have no live block id and cannot be recorded | Opening the cave store invalidates the atlas once; do not remove that invalidation |
| Expecting `Interior` persistence to be Y-exact | The cave store writes one file per 16-block band | Band-quantize expectations; a per-Y store is future work |
| Assuming off-screen marker distance is 3D | It is map-plane (X/Z) distance in blocks | By design on the top-down map; world markers show 3D distance |
| Using a fixed 13×13 coverage box | Small igloos sample the outside and look open | Follow the actual walls with rays; fixed boxes fail on small/large rooms |
| Rendering every loaded cave chunk | Terrain the player never visited appears on the Cave layer | Mark a 3×3 chunk visited set per band; unvisited chunks stay black |
| Creating deathpoints from the corpse position | The death animation/respawn moves the player | Cache the last alive position and use it when `IsDead` flips true |
| Adding a waypoint field without a fallback | Old 6-field files fail/are skipped | Parse `IsDeath` as an optional 7th field; write `false` for normal waypoints |
| Leaving a QA-only kill trigger in the build | Hidden backdoor | Remove the trigger file/route after verifying deathpoints |
| Caching only colour+height from a cave render | Walls brighten at chunk borders and open rooms lose the cutaway (cached wall read as floor, cached room read as rock) | Store `FlagCaveWall` **and** `FlagOpenRoom` and restore both on cache hits |
| Sampling the surface above the player while in Cave/Slice | Reveals terrain the player never visited (x-ray) | `AlwaysMapSurface` off by default; only read already-stored surface cells |
| Adding a render-only “indoors” special case | The tile cache/mode key reuses colours from the other context | Put the indoor bit in the atlas mode key (and therefore the cache key) |
| Using a tall shelter/hall as “not indoors” | Auto stays on Surface and the cutaway never triggers | Detect a roof by the topmost solid in the player's column, not only the 45-block underground probe |

| Assuming the Aether map key is `J` | The game native "Toggle Minimap" is also `J` in the saved prefs; it opened the native pause/minimap | Default is now `M`; an old `J` value is migrated once. Use `[Wayfinder] FullscreenMapKey` to rebind. |

## Held tool resolution

| Trap | Symptom | Correct behavior |
| :--- | :--- | :--- |
| Using `TerrainLoader.digger` | Empty hand or block reads as `Shovel` | It is a default hand Digger; never use it as the held tool |
| Assuming the weapon slot contains only weapons | Blocks/food/spawners are read as tools | Use a whitelist of real digging items |
| Treating `CreativeCategory.Tools` as digger | Cauldron, Bucket, controllers and sensors look like Pickaxe/Shovel | Whitelist exact real digger item names |
| Treating fancy axes as diggers | `Battle Axe`, `Dark Axe`, `Fire Axe`, `Ice Axe` look like tools | They are weapons; resolve to `Hand` |
| Trusting a default `Digger` on non-tool prefabs | `Cauldron` → Pickaxe, `Bucket` → Shovel | Ignore `Digger` unless the item name is whitelisted |

Real digger whitelist:

- `Axe`
- `Iron Axe`
- `Dig`
- `Iron Dig`
- `Goblin PickAxe`
- `Shovel`
- `Iron Shovel`
- `Hammer`
- `Iron Hammer`
- `Multitool`

## Native health panels

| Trap | Symptom | Correct behavior |
| :--- | :--- | :--- |
| Calling `MOBManagement.SetHealthBarsVisible(false)` | Name, level and HP all disappear | Toggle only `Health.barObject` and `HPLabel`; keep `NameLabel` and `MobLvl` |
| Restoring native HP by panel-only logic | Stale UI after toggle | Call `Health.RefreshHealthBarUI()` when un-hiding |

## Runtime / architecture

| Trap | Symptom | Correct behavior |
| :--- | :--- | :--- |
| Editing `NativeSuite` destructively | Large native UI regresses | Additive patches only |
| Adding runtime code to `Aether.Mods.*` **before the split** | Two Lens/Wayfinder implementations return | Temporary single runtime: `Aether.UI/NativeSuite`; after the modularization plan, the mod assembly becomes the canonical runtime |
| Direct `world[x, y, z]` access | Crashes on unloaded chunks | Use the safe chunk helpers |
| `Physics.autoSyncTransforms = false` | Physics desync | Keep it `true` |
| Assuming DLL hot reload | Old behavior persists | Restart Block Story after every deploy |
| Pattern-killing Steam processes | Kills the wrong process | Use the exact PID from `hyprctl clients -j` |

## Unattended QA

| Trap | Symptom | Correct behavior |
| :--- | :--- | :--- |
| Sending `ctrl+a` to the game | Opens HUD Edit Mode | Avoid control chords while the game is focused |
| Trying to close HUD Edit Mode with `Escape` | Mode stays active | Click its own `Done` button |
| One-second timeout on `steam -applaunch` | Looks like launch failure | Run in background or allow several seconds |
| Trusting a single screenshot after a click | Click may not register | Use press/release with a short delay and re-capture |

## Third-party BepInEx plugins crash at boot (R1 extended)

- Verified 2026-11-04 on Unity `6000.5.6f1` (Linux): any BepInEx plugin in `BepInEx/plugins` whose `Awake`
  touches Unity (ConfigurationManager 19.1; a probe calling `Time.frameCount`) kills the game with SIGSEGV
  inside `Chainloader.Start -> GameObject.AddComponent`. The same plugins run fine when hosted by Aether on
  frame 0 (`[Hosting] HostBepInExPlugins = true`, plugin in `Aether/Mods`). See `docs/MODDING_GUIDE.md` §2.
- Symptom: `LogOutput.log` is empty / game window never appears; `Player.log` ends with "Loading [Plugin]"
  followed by "Caught fatal signal - signo:11". `aether_conflicts.txt` is then stale (previous run).

## Compositor crash when the fullscreen game is killed/relaunched (Hyprland 0.56.2 + AMD)

- Verified timeline 2026-11-04 (journal + coredumps): amdgpu `[gfxhub] page fault` in the **Hyprland** process,
  `ring gfx_0.1.0 timeout`, reset OK, then Hyprland aborts by design (`glGetGraphicsResetStatus`, no GPU-reset
  recovery) and takes every Wayland client with it (terminals exit 137). Each incident was within seconds of
  killing/relaunching the fullscreen Unity game. A second, separate crash is a `libaquamarine`
  `SDRMConnector::disconnect` segfault when the watchdog restarts the backend.
- Not a new package regression: the graphics stack (kernel 7.2.8, Mesa 26.2.5, Hyprland 0.56.2-4,
  Aquamarine 0.15.1) was unchanged since 2026-10-28 and had zero GPU faults over five boots before. Same
  signature is reported upstream (hyprwm/Hyprland #16513, #9746); not fixed there.
- Mitigation we apply: graceful close (in-game Exit, never `kill`), 15 s gap before relaunch, windowed mode for
  unattended restarts, running the session inside `tmux`. `render:direct_scanout = 0` is harmless but did **not** prevent the
  crash in the upstream report, so it is not a fix.

## Compiled-but-unwired UI wastes debugging time

- On 2026-11-04 a full mod-menu rewrite went into `AetherModMenuModal`, which nothing could open, so the game looked
  unchanged. Rule: find the live entry point first and keep `DeadCodeTests` green.

## Occasional SIGSEGV when the game window is closed (observed once in ~5 closes, 2026-11-05)

- `hyprctl dispatch 'hl.dsp.window.close(...)'` ended the process cleanly four times and crashed it once (coredump
  `Unity Main Thre`, SIGSEGV/SEGV_ACCERR, frame #0 at an unmapped address inside Mono, no symbolizable Aether frame).
  That session had F7 rebinds and third-party mods running. Desktop shows "Process crashed: BlockStoryLinux.x86_64".
- Not reproduced, cause unproven. It happens at exit, after the world is already saved/abandoned. If it ever shows up
  during play, capture `coredumpctl info` and `Player.log` before assuming it is ours.
- `aether` on this machine's PATH is an unrelated desktop theming app. In a checkout use `./scripts/aether-cli.sh`.
- F7 rebinding takes the **next key press** as the new key. Scripted clicks that miss a row can rebind a real key
  (vanilla ones are persisted in the game's prefs): screenshot before sending keys.

## Many mods patching the same game methods (2026-11-05)

- `aether_conflicts.txt` (and `aether doctor`) now folds Harmony overlaps into one line per set of owners ("A and B on 9
  methods: ..."). A prefix only counts as a conflict when it returns `bool` (it can skip the original); `void` prefixes,
  postfixes and finalizers compose and are Info.
- Aether never decides which prefix wins: that changes what a mod does. To settle a known overlap by hand, set
  `[Conflicts] DisablePatches = owner@Type.Method;owner2@Type.Method` in `dev.ultimaibrahim.aether.cfg`. It only
  removes the owner when another mod patches the same method; names come from the report.

## Logs of a session that crashed natively (2026-11-05)

- BepInEx recreates `LogOutput.log` before any plugin runs and Unity keeps a single `Player-prev.log`, so a native crash
  (SIGSEGV) used to lose its log after the next launch. Aether now copies both into `Aether/Logs/<yyyyMMdd-HHmmss>/` every
  15 s, on quit and when a crash report is created; the newest `[Logs] KeepSessions` (10) folders are kept. A crash loses at
  most the last ~15 s. Managed crashes also leave a report in `BepInEx/crash-reports/`.
- Other folders inside `Aether/Logs` (for example future telemetry) are never pruned: only timestamp-named ones are.

## Aether mod assemblies whose dependency is missing can break other plugins' type scans

Seen 2026-11-06 with `Aether.UI.dll` removed while the four official mods stayed installed. Aether loads every DLL in
`Aether/Mods` to read its `[AetherMod]` attribute, and a .NET assembly cannot be unloaded. The mods that need the missing
`aether_ui` stay in the AppDomain half-broken, and a third-party plugin that walks every loaded type throws `TypeLoadException: Invalid type Aether.Mods.Wayfinder.WayfinderModule`, so that one
Harmony patch fails. Aether itself reports the excluded mods clearly (one warning each) and everything else loads.
Remedy: do not run in that state. The pre-launch check (`aether preflight`) is meant to catch it before the game starts; a
later option is to read manifests with `System.Reflection.Metadata` so a mod with a missing dependency is never loaded.

## Two mods bound to the same hotkey

Nothing stops two mods from using the same key (seen 2026-11-06: Aether's suite menu `F10` and Potato Mode's `PanelKey`; also `F7`, `F8`).
Both react to one press. Aether now reports this: the loader adds one warning per shared key to `aether_conflicts.txt` and the log,
and `aether doctor` lists them without launching the game. It reads the BepInEx config files (settings typed `KeyCode` or
`KeyboardShortcut`, plus any `...Key` setting holding a plain key name), so it covers plugins Aether does not host. It cannot tell
whether the two actions apply in different situations; the fix is to rebind one of them in its own `.cfg`.
