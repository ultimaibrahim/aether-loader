#!/usr/bin/env bash
# Aether one-click installer (Linux). Run from the extracted release folder:  ./install.sh
# Options are passed to the CLI: --game-dir <path>, --dry-run (mods are not installed by default; see README.txt)
set -euo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
chmod +x "$HERE/aether"
echo "Aether installer — this will install Aether (and BepInEx if it is missing) into Block Story."
echo "Tip: add --dry-run to preview, or --game-dir <path> if the game is not in the default Steam folder."
echo
exec "$HERE/aether" install --source-dir "$HERE/payload" --yes "$@"
