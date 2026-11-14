# Aether one-click installer (Windows). Right-click > "Run with PowerShell", or:  .\install.ps1
# Options are passed to the CLI: --game-dir <path>, --dry-run (mods are not installed by default; see README.txt)
$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
Write-Host "Aether installer - this will install Aether (and BepInEx if it is missing) into Block Story."
Write-Host "Tip: add --dry-run to preview, or --game-dir <path> if the game is not in the default Steam folder."
& "$here\aether.exe" install --source-dir "$here\payload" --yes @args
exit $LASTEXITCODE
