AETHER COMMAND LINE TOOL — optional, for mod makers and terminal fans
=====================================================================

You do not need this to play. The graphical installer (aether-installer, in the main Aether download) does the same
installation, and Aether Monitor shows status, mods and logs.

INSTALL FROM A TERMINAL
  Linux:    ./install.sh            (add --dry-run to preview, --game-dir <path> if the game is elsewhere)
  Windows:  right-click install.ps1 > Run with PowerShell

COMMANDS
  aether status                what is installed
  aether list                  installed assemblies
  aether doctor                checks the install and prints the conflict report from the last launch
  aether add <mod.zip|.dll>    installs a mod into the right folder (--dry-run to preview)
  aether new <Name>            creates a mod project (Aether Nano by default)
  aether lattice install <mod> | list | check    the Lattice catalog (signature, SHA-256, revocations, required mods)
  aether uninstall             removes Aether core and official mods; keeps third-party mods (--purge also removes configs)
  aether install --with-cli    also keeps this tool in <game>/Aether

Add --game-dir <path> to any command when the game is not in the default Steam folder.
