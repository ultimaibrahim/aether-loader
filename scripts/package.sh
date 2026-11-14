#!/usr/bin/env bash
# Builds the release zips in dist/:
#   aether-<version>-<rid>.zip        the loader (no mods) + the installer window, light (needs .NET 9 installed)
#   aether-<version>-<rid>-full.zip   the same with its own .NET runtime (nothing to install, larger)
#   aether-cli-<version>-<rid>.zip                                  the optional command line tool for mod makers
#   aether-<mod>-<version>.zip                                      one zip per official mod (blueprint, dusk, lens, wayfinder)
# Mods are separate on purpose: the loader installs none by default, and 'aether add <zip>' installs the ones you pick.
# Usage: ./scripts/package.sh [version]        (RIDS="linux-x64" limits the loader zips, handy for a quick local run)
# If the game is not in the default Steam folder, export GameManagedPath and BepInExCorePath first (see docs/DEV_SETUP.md).
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
VERSION="${1:-$(grep -oP '(?<=<AetherVersion>)[^<]+' "$ROOT/Directory.Build.props" | head -1)}"
RIDS="${RIDS:-linux-x64 win-x64}"
# The official mods have their own version (their [BepInPlugin]); their zips are named with it, not with the loader's.
MOD_VERSION="${MOD_VERSION:-1.1.1}"
for P in "$ROOT"/src/Aether.Mods/Aether.Mods.*/*Plugin.cs; do
  V="$(grep -oP '\[BepInPlugin\([^,]+,\s*"[^"]+",\s*"\K[^"]+' "$P" | head -1)"
  if [[ "$V" != "$MOD_VERSION" ]]; then echo "package.sh: $(basename "$P") declares $V, zips say $MOD_VERSION. Set MOD_VERSION or fix the plugin." >&2; exit 1; fi
done
# Signed with Aether's strong-name key the mods show "By Aether" in the F10 list; without it they are plain Nano mods.
SNK="${AETHER_MODS_KEY:-$HOME/aether/keys/aether-mods.snk}"
if [[ ! -f "$SNK" ]]; then echo "package.sh: no strong-name key at $SNK: the official mods will be unsigned (shown as Nano)." >&2; fi
DIST="$ROOT/dist"
STAGE="$DIST/.stage"
rm -rf "$STAGE" "$DIST"/Aether.Mods.*.dll "$DIST"/Aether.Nano.dll "$DIST"/aether-"$VERSION"-*.zip "$DIST"/aether-cli-"$VERSION"-*.zip "$DIST"/aether-*-"$MOD_VERSION".zip
mkdir -p "$STAGE"

# zip_dir <folder> <out.zip>: zips the folder keeping its name as the top-level directory, with a fixed order.
zip_dir() {
  python3 - "$1" "$2" <<'PY'
import os, sys, zipfile
src, out = sys.argv[1], sys.argv[2]
base = os.path.dirname(src)
with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as z:
    for dp, dn, fs in os.walk(src):
        dn.sort()
        for f in sorted(fs):
            p = os.path.join(dp, f)
            zi = zipfile.ZipInfo.from_file(p, os.path.relpath(p, base))
            zi.compress_type = zipfile.ZIP_DEFLATED
            with open(p, "rb") as fh: z.writestr(zi, fh.read())
PY
}

echo "== Build (Release) =="
dotnet build "$ROOT/Aether.sln" -c Release --nologo -v q

# Loader payload: let the CLI lay the DLLs out exactly as they will be installed. The default install has no mods.
PAYLOAD="$STAGE/payload"; mkdir -p "$PAYLOAD"
dotnet run --project "$ROOT/tools/Aether.CLI/Aether.CLI.csproj" -c Release --no-build -- \
  install --game-dir "$PAYLOAD" --source-dir "$ROOT" --skip-bepinex --yes
rm -rf "$PAYLOAD/Aether/backup"
find "$PAYLOAD/BepInEx" -type d -empty -not -path "$PAYLOAD/BepInEx/plugins/Aether" -delete 2>/dev/null || true
if find "$PAYLOAD" -name 'Aether.Mods.*.dll' | grep -q .; then echo "ERROR: the loader payload must not contain mods" >&2; exit 1; fi

for RID in $RIDS; do
  echo "== Publish CLI ($RID) =="
  OUT="$STAGE/$RID"
  rm -rf "$OUT"; mkdir -p "$OUT"
  dotnet publish "$ROOT/tools/Aether.CLI/Aether.CLI.csproj" -c Release -r "$RID" --self-contained \
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o "$OUT/bin" --nologo -v q

  # The installer window, in two flavors: "light" needs the .NET 9 runtime installed (small download, recommended);
  # "full" carries its own runtime (nothing to install, larger). The Windows build leaves out ANGLE (software rendering only).
  for FLAVOR in light full; do
    echo "== Publish installer window ($RID, $FLAVOR) =="
    APP="$STAGE/$RID-app-$FLAVOR"
    rm -rf "$APP"; mkdir -p "$APP"
    dotnet publish "$ROOT/tools/Aether.Installer.App/Aether.Installer.App.csproj" -c Release -r "$RID" -p:Flavor=$FLAVOR -o "$APP" --nologo -v q
    if [[ "$FLAVOR" == "full" ]]; then NAME="aether-$VERSION-$RID-full"; else NAME="aether-$VERSION-$RID"; fi
    PKG="$STAGE/$NAME"
    mkdir -p "$PKG"
    cp -r "$PAYLOAD" "$PKG/payload"
    cp "$ROOT/packaging/README.txt" "$PKG/"
    cp "$ROOT/LICENSE" "$PKG/" 2>/dev/null || true
    cp "$ROOT/NOTICE" "$PKG/" 2>/dev/null || true
    if [[ "$RID" == linux-* ]]; then
      cp "$APP/aether-installer" "$APP"/*.so "$PKG/"; chmod +x "$PKG/aether-installer"
    else
      cp "$APP/aether-installer.exe" "$PKG/"
      for f in "$APP"/*.dll; do [[ "$(basename "$f")" == "av_libglesv2.dll" ]] || cp "$f" "$PKG/"; done
    fi
    zip_dir "$PKG" "$DIST/$NAME.zip"
  done

  # Command line package: the tool + the terminal install scripts + the same payload.
  CLI="$STAGE/aether-cli-$VERSION-$RID"
  mkdir -p "$CLI"
  cp -r "$PAYLOAD" "$CLI/payload"
  cp "$ROOT/packaging/README-CLI.txt" "$CLI/README.txt"
  cp "$ROOT/LICENSE" "$CLI/" 2>/dev/null || true
  cp "$ROOT/NOTICE" "$CLI/" 2>/dev/null || true
  if [[ "$RID" == linux-* ]]; then
    cp "$OUT/bin/aether" "$CLI/aether"; cp "$ROOT/packaging/install.sh" "$CLI/install.sh"; chmod +x "$CLI/aether" "$CLI/install.sh"
  else
    cp "$OUT/bin/aether.exe" "$CLI/aether.exe"; cp "$ROOT/packaging/install.ps1" "$CLI/install.ps1"
  fi
  zip_dir "$CLI" "$DIST/aether-cli-$VERSION-$RID.zip"
done

echo "== Mod packages =="
# Mods already moved to Aether Nano: plain BepInEx plugins that run without Aether. Their zip carries Aether.Nano.dll.
NANO_MODS=" dusk lens blueprint wayfinder "
NANO_DLL="$ROOT/src/Aether.Nano/bin/Release/netstandard2.1/Aether.Nano.dll"
[[ -f "$NANO_DLL" ]] || { echo "ERROR: missing $NANO_DLL" >&2; exit 1; }
cp "$NANO_DLL" "$DIST/"
# key:Assembly name. The DLL is netstandard2.1, so one zip serves every platform.
for entry in blueprint:Blueprint dusk:Dusk lens:Lens wayfinder:Wayfinder; do
  KEY="${entry%%:*}"; NAME="${entry##*:}"
  DLL="$ROOT/src/Aether.Mods/Aether.Mods.$NAME/bin/Release/netstandard2.1/Aether.Mods.$NAME.dll"
  [[ -f "$DLL" ]] || { echo "ERROR: missing $DLL (did the build succeed?)" >&2; exit 1; }
  PKG="$STAGE/aether-$KEY-$MOD_VERSION"
  mkdir -p "$PKG"
  cp "$DLL" "$PKG/"
  cp "$DLL" "$DIST/"        # the bare .dll too, for drag and drop into Aether/Mods (its name must stay the same)
  cp "$ROOT/LICENSE" "$PKG/" 2>/dev/null || true
  cp "$ROOT/NOTICE" "$PKG/" 2>/dev/null || true
  if [[ "$NANO_MODS" == *" $KEY "* ]]; then
    cp "$NANO_DLL" "$PKG/"
    cat > "$PKG/README.txt" <<TXT
Aether $NAME $MOD_VERSION
$(printf '=%.0s' $(seq 1 $((${#NAME} + ${#MOD_VERSION} + 8))))
A BepInEx plugin built on Aether Nano: it runs with Aether or with BepInEx alone.
- With Aether: open Aether Monitor, press "Add mod…" and pick this zip. Its settings are in the F10 menu.
- Without Aether: copy both DLLs (Aether.Mods.$NAME.dll and Aether.Nano.dll) into <game>/BepInEx/plugins.
  Its settings are in the "Mods" button Aether Nano adds to the main menu.
To remove it, delete Aether.Mods.$NAME.dll (keep Aether.Nano.dll if other mods use it).
TXT
  else
  cat > "$PKG/README.txt" <<TXT
Aether mod: $NAME $MOD_VERSION
$(printf '=%.0s' $(seq 1 $((${#NAME} + ${#MOD_VERSION} + 14))))

This mod needs the Aether loader, installed first.
Open Aether Monitor (it comes with the loader), press "Add mod…" and pick this zip
(or copy Aether.Mods.$NAME.dll into <game>/Aether/Mods by hand). Restart Block Story: the mod shows up in the F10 menu.
To remove it, delete that DLL from <game>/Aether/Mods.
TXT
  fi
  zip_dir "$PKG" "$DIST/aether-$KEY-$MOD_VERSION.zip"
done

rm -rf "$STAGE"
( cd "$DIST" && sha256sum aether-"$VERSION"-*.zip aether-cli-"$VERSION"-*.zip aether-*-"$MOD_VERSION".zip Aether.Mods.*.dll Aether.Nano.dll | sort -u | tee SHA256SUMS.txt )
echo "Done: $DIST"
