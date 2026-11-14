#!/usr/bin/env bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
echo "✦ Aether Modding Framework — Universal Linux/macOS Installer"
echo "  Repository Root: $REPO_ROOT"

dotnet build "$REPO_ROOT/Aether.sln" -c Release --nologo -v q
dotnet run --project "$REPO_ROOT/tools/Aether.CLI/Aether.CLI.csproj" -c Release --no-build -- install --source-dir "$REPO_ROOT" "$@"
