using System.Text;

namespace Aether.Installer;

/// <summary>
/// Builds the files of a new Aether mod project. Pure (no disk access) so it can be unit-tested.
/// The generated project compiles against the Aether DLLs installed in the game folder
/// (<c>BepInEx/plugins/Aether</c>), not against a checkout of this repository.
/// </summary>
/// <summary>Which kind of project <c>aether new</c> writes.</summary>
public enum ModTemplate
{
    /// <summary>A plain BepInEx plugin with Aether Nano compiled in: needs no Aether assembly, runs with or without Aether.</summary>
    Nano,
    /// <summary>A mod written against the Aether API (blocks, items, Aether UI). Needs Aether installed to load.</summary>
    Aether
}

public static class ModScaffold
{
    public const string CodeFile = "Mod.cs";

    // Accents are folded by hand: string.Normalize is not reliable in invariant-globalization builds.
    private const string Accented = "áàäâãåéèëêíìïîóòöôõúùüûñçÁÀÄÂÃÅÉÈËÊÍÌÏÎÓÒÖÔÕÚÙÜÛÑÇ";
    private const string Plain    = "aaaaaaeeeeiiiiooooouuuuncAAAAAAEEEEIIIIOOOOOUUUUNC";

    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            int i = Accented.IndexOf(c);
            sb.Append(i >= 0 ? Plain[i] : c);
        }
        return sb.ToString();
    }

    private static bool AsciiLetterOrDigit(char c) => c < 128 && char.IsLetterOrDigit(c);

    /// <summary>C# identifier for namespaces and assembly names: ASCII letters and digits, never starting with a digit.</summary>
    public static string SafeName(string rawName)
    {
        string s = new string(Fold(rawName ?? "").Where(AsciiLetterOrDigit).ToArray());
        if (s.Length == 0) return "";
        return char.IsDigit(s[0]) ? "Mod" + s : s;
    }

    /// <summary>Stable mod id in snake_case ("My Cool Mod" -> "my_cool_mod").</summary>
    public static string ModId(string rawName)
    {
        var sb = new StringBuilder();
        bool pendingSep = false;
        foreach (char c in Fold((rawName ?? "").Trim()).ToLowerInvariant())
        {
            if (AsciiLetterOrDigit(c))
            {
                if (pendingSep && sb.Length > 0) sb.Append('_');
                sb.Append(c);
                pendingSep = false;
            }
            else pendingSep = true;
        }
        string id = sb.ToString();
        return id.Length == 0 || char.IsDigit(id[0]) ? "mod_" + id : id;
    }

    /// <summary>Relative file name -> content. Returns an empty map when the name has no usable characters.</summary>
    public static IReadOnlyDictionary<string, string> Generate(string rawName, string? gameDir,
        ModTemplate template = ModTemplate.Nano)
    {
        string safe = SafeName(rawName), id = ModId(rawName);
        if (safe.Length == 0) return new Dictionary<string, string>();
        string display = (rawName ?? "").Trim().Replace("\"", "'");
        if (template == ModTemplate.Nano) return GenerateNano(safe, id, display, gameDir);

        string aetherDir = string.IsNullOrWhiteSpace(gameDir)
            ? "PUT_YOUR_BLOCK_STORY_FOLDER_HERE/BepInEx/plugins/Aether"
            : gameDir.TrimEnd('/', '\\').Replace('\\', '/') + "/BepInEx/plugins/Aether";

        var files = new Dictionary<string, string>
        {
            [safe + ".csproj"] = $$"""
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>12.0</LangVersion>
    <Nullable>enable</Nullable>
    <AssemblyName>{{safe}}</AssemblyName>
    <RootNamespace>{{safe}}</RootNamespace>
  </PropertyGroup>

  <!-- Where Aether lives inside your game folder. Change it here, or pass -p:AetherDir=<path> when building. -->
  <PropertyGroup>
    <AetherDir Condition="'$(AetherDir)' == ''">{{aetherDir}}</AetherDir>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="Aether.Core"><HintPath>$(AetherDir)/Aether.Core.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="Aether.API"><HintPath>$(AetherDir)/Aether.API.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="Aether.Loader"><HintPath>$(AetherDir)/Aether.Loader.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="Aether.Registry"><HintPath>$(AetherDir)/Aether.Registry.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="Aether.UI"><HintPath>$(AetherDir)/Aether.UI.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>

  <Target Name="CheckAetherDir" BeforeTargets="BeforeBuild" Condition="!Exists('$(AetherDir)/Aether.Loader.dll')">
    <Error Text="Aether was not found in '$(AetherDir)'. Install Aether first, then set AetherDir in this file or build with -p:AetherDir=&lt;game&gt;/BepInEx/plugins/Aether" />
  </Target>

  <!-- Optional: build with -p:InstallToGame=true to copy the result into <game>/Aether/Mods. -->
  <Target Name="InstallToGame" AfterTargets="Build" Condition="'$(InstallToGame)' == 'true'">
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(AetherDir)/../../../Aether/Mods" />
    <Message Importance="high" Text="Installed {{safe}}.dll into the game's Aether/Mods folder." />
  </Target>

</Project>
""",
            [CodeFile] = $$"""
using System;
using Aether.Core.Config;
using Aether.Loader.Discovery;
using Aether.Loader.Entrypoints;
using Aether.Registry;

// This line is how Aether recognises your mod. It must stay at assembly level.
[assembly: AetherMod("{{id}}", "1.0.0",
    Name = "{{display}}",
    Author = "You",
    Description = "A starter mod for Aether.")]

namespace {{safe}};

public sealed class ModEntry : IModInitializer, IClientModInitializer
{
    // 1. An option the player can change (BepInEx/config/aether/{{id}}.json).
    public static readonly ConfigEntry<bool> EnableCustomBlock =
        AetherConfig.Bind("{{id}}", "Features", "EnableCustomBlock", true, "Register the example block.");

    // 2. Runs when mods load: the place to register blocks, items and recipes.
    public void OnInitialize()
    {
        if (!EnableCustomBlock.Value) return;

        var definition = new BlockDefinition(new Identifier("{{id}}", "gem_block"), "Gem Block")
        {
            Hardness = 3.0f,
            IsSolid = true
        };
        DynamicBlockRegistry.Instance.Register(definition);
    }

    // 3. Runs on the first frame, with the game alive: UI, keys, sound and scene objects go here.
    public void OnInitializeClient()
    {
        Console.WriteLine("[{{safe}}] ready");
    }
}
""",
            ["README.md"] = $$"""
# {{display}}

A mod for Aether. Build it and drop the DLL into `<game>/Aether/Mods`.

    dotnet build {{safe}}.csproj -c Release -p:InstallToGame=true

Without `-p:InstallToGame=true`, copy `bin/Release/netstandard2.1/{{safe}}.dll` into `<game>/Aether/Mods` yourself.
Then start Block Story: the mod should appear in the F10 menu and in `BepInEx/LogOutput.log`.

If the build says Aether was not found, open `{{safe}}.csproj` and set `AetherDir` to `<game>/BepInEx/plugins/Aether`.
"""
        };
        return files;
    }

    private static IReadOnlyDictionary<string, string> GenerateNano(string safe, string id, string display, string? gameDir)
    {
        string game = string.IsNullOrWhiteSpace(gameDir)
            ? "PUT_YOUR_BLOCK_STORY_FOLDER_HERE"
            : gameDir.TrimEnd('/', '\\').Replace('\\', '/');

        return new Dictionary<string, string>
        {
            [safe + ".csproj"] = $$"""
<Project Sdk="Microsoft.NET.Sdk">

  <!-- A plain BepInEx plugin on Aether Nano (Aether.Nano.dll, about 300 KB): it loads with BepInEx alone or with Aether,
       and its settings page shows up in Aether's menu or in Nano's own. -->
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <Nullable>disable</Nullable>
    <AssemblyName>{{safe}}</AssemblyName>
    <RootNamespace>{{safe}}</RootNamespace>
  </PropertyGroup>

  <!-- Your Block Story folder. Change it here, or pass -p:GameDir=<path> when building. -->
  <PropertyGroup>
    <GameDir Condition="'$(GameDir)' == ''">{{game}}</GameDir>
    <ManagedDir>$(GameDir)/BlockStoryLinux_Data/Managed</ManagedDir>
    <ManagedDir Condition="!Exists('$(ManagedDir)')">$(GameDir)/BlockStory_Data/Managed</ManagedDir>
    <!-- Aether.Nano.dll: in Aether's folder when Aether is installed, else next to your plugins. -->
    <NanoDll>$(GameDir)/BepInEx/plugins/Aether/Aether.Nano.dll</NanoDll>
    <NanoDll Condition="!Exists('$(NanoDll)')">$(GameDir)/BepInEx/plugins/Aether.Nano.dll</NanoDll>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="Aether.Nano"><HintPath>$(NanoDll)</HintPath><Private>false</Private></Reference>
    <Reference Include="BepInEx"><HintPath>$(GameDir)/BepInEx/core/BepInEx.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine"><HintPath>$(ManagedDir)/UnityEngine.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.CoreModule"><HintPath>$(ManagedDir)/UnityEngine.CoreModule.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.IMGUIModule"><HintPath>$(ManagedDir)/UnityEngine.IMGUIModule.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.TextRenderingModule"><HintPath>$(ManagedDir)/UnityEngine.TextRenderingModule.dll</HintPath><Private>false</Private></Reference>
    <Reference Include="UnityEngine.InputLegacyModule"><HintPath>$(ManagedDir)/UnityEngine.InputLegacyModule.dll</HintPath><Private>false</Private></Reference>
  </ItemGroup>

  <Target Name="CheckGameDir" BeforeTargets="BeforeBuild" Condition="!Exists('$(GameDir)/BepInEx/core/BepInEx.dll')">
    <Error Text="BepInEx was not found in '$(GameDir)'. Install Block Story with BepInEx first, then set GameDir in this file or build with -p:GameDir=&lt;your Block Story folder&gt;" />
  </Target>
  <Target Name="CheckNano" BeforeTargets="BeforeBuild" Condition="Exists('$(GameDir)/BepInEx/core/BepInEx.dll') And !Exists('$(NanoDll)')">
    <Error Text="Aether.Nano.dll was not found. Install Aether, or put Aether.Nano.dll (from the Aether release or Lattice) into $(GameDir)/BepInEx/plugins." />
  </Target>

  <!-- Optional: build with -p:InstallToGame=true to copy the result into <game>/BepInEx/plugins. -->
  <Target Name="InstallToGame" AfterTargets="Build" Condition="'$(InstallToGame)' == 'true'">
    <Copy SourceFiles="$(TargetPath)" DestinationFolder="$(GameDir)/BepInEx/plugins" />
    <Message Importance="high" Text="Installed {{safe}}.dll into the game's BepInEx/plugins folder." />
  </Target>

</Project>
""",
            [CodeFile] = $$"""
using Aether.Nano;
using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace {{safe}}
{
    // The GUID identifies your mod to BepInEx and Aether. Keep it unique and never change it.
    [BepInPlugin("com.example.{{id}}", "{{display}}", "1.0.0")]
    [BepInDependency(Nano.Guid)]
    public sealed class ModPlugin : NanoMod
    {
        private ConfigEntry<string> _greeting;
        private ConfigEntry<KeyCode> _helloKey;

        // Runs once, on the first frame. Touch Unity here, never in Awake (see the Aether docs, KNOWN_TRAPS).
        protected override void OnReady()
        {
            _greeting = Config.Bind("General", "Greeting", "hello", "Text this mod logs on start.");
            Logger.LogInfo($"[{{safe}}] {_greeting.Value} from frame {Time.frameCount}.");

            // A key the player can change in the game's own keybinding window (Pause > Controls), or in Aether's F7.
            // None = unbound until the player picks one, so it never clashes with the game or other mods.
            _helloKey = Config.Bind("General", "HelloKey", KeyCode.None, "Key that logs the greeting.");
            NanoKeys.Register(Info.Metadata.GUID, "{{display}}", _helloKey, "Say hello", "Saludar");

            // Your settings page: shown in Aether's F10 menu, or in Nano's own Mods button.
            NanoPages.Register(new NanoPage
            {
                Id = Info.Metadata.GUID,
                Name = "{{display}}",
                Description = Nano.Tr("A new mod.", "Un mod nuevo."),
                Draw = (area, y) =>
                {
                    NanoUi.DrawShadowLabel(new Rect(12, y, area.width - 24, 22), Nano.Tr("Greeting: ", "Saludo: ") + _greeting.Value,
                        NanoStyles.Tool, Color.white, Color.black);
                    if (NanoUi.DrawNativeWoodButton(new Rect(12, y + 30, 220, 32), Nano.Tr("Say hello", "Saludar")))
                        Logger.LogInfo(_greeting.Value);
                },
                ContentHeight = 120f,
            });
        }

        // Runs every frame after OnReady. Delete it if you do not need it.
        protected override void OnTick()
        {
            if (_helloKey.Value != KeyCode.None && Input.GetKeyDown(_helloKey.Value)) Logger.LogInfo(_greeting.Value);
        }
    }
}
""",
            ["README.md"] = $$"""
# {{display}}

A BepInEx mod on **Aether Nano**. It needs only `Aether.Nano.dll` next to it: it loads with BepInEx alone or with Aether,
and its settings page appears in Aether's menu or in Nano's own.

    dotnet build {{safe}}.csproj -c Release -p:InstallToGame=true

Without `-p:InstallToGame=true`, copy `bin/Release/netstandard2.1/{{safe}}.dll` into `<game>/BepInEx/plugins` yourself.
Then start Block Story: `BepInEx/LogOutput.log` should show your greeting.

When you share it, ship `Aether.Nano.dll` with it (players with Aether already have it), or list Aether Nano as a
requirement on Lattice. If the build says BepInEx or Aether.Nano.dll was not found, set `GameDir` in `{{safe}}.csproj`.

Need blocks, items or Aether's UI? Create the project with `aether new {{safe}} --aether` instead.
"""
        };
    }
}
