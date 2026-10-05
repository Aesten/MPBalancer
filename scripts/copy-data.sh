#!/usr/bin/env bash
# Copies the Native and Multiplayer modules' XML data (ModuleData etc.) into ref/ModuleData/
# from both the client and the dedicated server installs. Only text data, no assets.
#
# Usage: scripts/copy-data.sh [game-folder] [dedicated-server-folder]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

GAME_FOLDER="${1:-C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord}"
SERVER_FOLDER="${2:-C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Dedicated Server}"
OUT_DIR="$REPO_ROOT/ref/ModuleData"

copy_module () {
  local src_root="$1" label="$2" module="$3"
  local src="$src_root/Modules/$module"
  local dst="$OUT_DIR/$label/$module"
  if [ ! -d "$src" ]; then
    echo "  SKIP (not found): $src"
    return
  fi
  echo "  $label/$module"
  mkdir -p "$dst"
  [ -f "$src/SubModule.xml" ] && cp "$src/SubModule.xml" "$dst/"
  if [ -d "$src/ModuleData" ]; then
    (cd "$src" && find ModuleData -type f \( -iname '*.xml' -o -iname '*.xsd' -o -iname '*.json' -o -iname '*.txt' -o -iname '*.csv' \) -print0 \
      | while IFS= read -r -d '' f; do mkdir -p "$dst/$(dirname "$f")"; cp "$f" "$dst/$f"; done)
  fi
}

echo "Copying module data ..."
for module in Native Multiplayer; do
  copy_module "$GAME_FOLDER" Client "$module"
  copy_module "$SERVER_FOLDER" DedicatedServer "$module"
done

# Dedicated server sample configs (game mode/option settings) live next to Native's SubModule.xml.
mkdir -p "$OUT_DIR/DedicatedServer/Native"
cp "$SERVER_FOLDER/Modules/Native/"ds_config_*.txt "$OUT_DIR/DedicatedServer/Native/" 2>/dev/null || true

echo
echo "Done. Browse under: $OUT_DIR"
