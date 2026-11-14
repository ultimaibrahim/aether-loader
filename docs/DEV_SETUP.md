# Developing Aether on another machine

1. **Prerequisites:** .NET 9 SDK, git, Block Story from Steam (App ID `270110`), BepInEx 5.4.23.6 in the game
   folder (the installer can fetch it, SHA-256 verified).
2. **Clone and build**

       git clone <repository-url> && cd aether
       git checkout main                           # the only branch
       dotnet build Aether.sln -c Release          # needs 0 warnings / 0 errors

   `Directory.Build.props` defaults `GameManagedPath` / `BepInExCorePath` to the original Linux machine's Steam
   path, so on another machine pass your own (they are only used when the folders exist):

       dotnet build Aether.sln -c Release \
         -p:GameManagedPath="<game>/BlockStoryLinux_Data/Managed" -p:BepInExCorePath="<game>/BepInEx/core"
       # Windows: <game>\BlockStory_Data\Managed (folder name = the *_Data folder next to the .exe)
3. **Install into the game:** `./scripts/install.sh` (Linux/macOS) or `scripts/install.ps1` (Windows); add
   `--game-dir <path>` when the game is not in the default Steam location, `--dry-run` to preview. It installs the
   loader only; add `--mods lens,dusk` or `--with-mods` to also install official mods from your build.
   Release zips (the loader plus one zip per official mod): `./scripts/package.sh` (export `GameManagedPath` and
   `BepInExCorePath` first if the game is not in the default folder; `RIDS=linux-x64` builds a single platform).
4. **Third-party mods:** drop BepInEx plugins and Aether mods into `<game>/Aether/Mods` (hosting is on by
   default) or use `./scripts/aether-cli.sh add <file>`.
5. **Reports after a launch:** `<game>/BepInEx/config/aether_conflicts.txt` (or `./scripts/aether-cli.sh doctor`);
   crash logs in `<game>/BepInEx/crash-reports/` and `Player.log`.
6. **Read first:** `docs/KNOWN_TRAPS.md`.
7. **Live-game rules:** close the game from its own menu (never `kill`), and back up your world and character before testing.

Not in git on purpose: game files, third-party DLLs, saves and decompiled sources.
