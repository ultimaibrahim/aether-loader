$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
Write-Host "✦ Aether Modding Framework — Universal Windows Installer" -ForegroundColor Magenta
Write-Host "  Repository Root: $RepoRoot"

dotnet build "$RepoRoot\Aether.sln" -c Release --nologo -v q
dotnet run --project "$RepoRoot\tools\Aether.CLI\Aether.CLI.csproj" -c Release --no-build -- install --source-dir "$RepoRoot" @args
