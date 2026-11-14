AETHER 1.0.4-beta — release package
====================================

BETA: APIs and mod contracts may still change. Tested on Linux and Windows 11.
On Windows, SmartScreen may warn because the executables are not code-signed yet: choose "More info", then "Run anyway"
(check the SHA-256 in SHA256SUMS.txt first).

WHICH DOWNLOAD
  aether-<version>-<system>.zip        light: small, needs the free .NET 9 runtime (https://dotnet.microsoft.com/download/dotnet/9.0 ,
                                       "Runtime"). On Windows, if it is missing, the program says so and points
                                       there. On Linux it does not open at all without it: take the full one.
  aether-<version>-<system>-full.zip   full: carries its own .NET, nothing to install, larger download.

INSTALL
  Run  aether-installer  (aether-installer.exe on Windows). Pick the Block Story folder and press Install.
  At the end it offers Aether Monitor, a small window with your crashes, mods and logs, where you can also add mods.

  This installs the Aether loader only. It installs NO mods: the mods are your choice (see MODS below).
  The installer finds Block Story in the default Steam folders; if yours is elsewhere, use "Browse…".
  If BepInEx 5.x is missing it downloads the tested version (5.4.23.6) from its official GitHub release and checks its
  SHA-256 before installing anything.

  Linux + Steam: set the game's launch option to   ./run_bepinex.sh %command%

MODS
  Aether's official mods (Blueprint, Dusk, Lens, Wayfinder) are separate downloads: aether-<mod>-<version>.zip.
  Each mod is also a bare .dll (Aether.Mods.<Name>.dll): drop it into <game>/Aether/Mods. Or open Aether Monitor, press "Add mod…" and pick the zip (or the .dll). It reads the mod, tells you where it will go
  and whether a dependency is missing, then asks before copying. Or copy the .dll into <game>/Aether/Mods by hand.

WHERE THINGS GO
  <game>/BepInEx/plugins/Aether/   Aether core. Installed for you; do not put mods here.
  <game>/Aether/Mods/              Aether mods (the ones you choose to add) AND third-party
                                   BepInEx plugins: Aether starts them on frame 0.
  <game>/BepInEx/plugins/          Do not put plugins here: some crash Unity when started at boot.

REMOVING AETHER
  Delete <game>/BepInEx/plugins/Aether and <game>/Aether (your mods and logs are inside it). The command line tool
  (separate download, see below) also has 'aether uninstall'.

COMMAND LINE TOOL (optional, for mod makers)
  Not needed to play. It is a separate download (aether-cli-<version>-<system>.zip) with 'aether doctor', 'aether add',
  'aether new' (creates a mod project) and the Lattice commands.

Replaced files are backed up under <game>/Aether/backup/<timestamp>/.
