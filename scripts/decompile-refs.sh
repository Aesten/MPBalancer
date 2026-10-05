#!/usr/bin/env bash
# Decompiles only the game assemblies relevant to multiplayer into ref/GameSource/<Assembly>/:
# the shared engine/gameplay assemblies from the game's bin folder (which the Native module
# runs on), plus the Native and Multiplayer modules' own assemblies. Campaign, SandBox,
# StoryMode and NavalDLC are deliberately excluded.
#
# Usage: scripts/decompile-refs.sh [path-to-game-folder]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

GAME_FOLDER="${1:-C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Bannerlord}"
if [ ! -d "$GAME_FOLDER" ]; then
  echo "Game folder not found: $GAME_FOLDER" >&2
  exit 1
fi

GAME_BIN="$GAME_FOLDER/bin/Win64_Shipping_Client"
GAME_MODULES="$GAME_FOLDER/Modules"
OUT_DIR="$REPO_ROOT/ref/GameSource"

export PATH="$PATH:$HOME/.dotnet/tools"
if ! command -v ilspycmd >/dev/null 2>&1; then
  dotnet tool install -g ilspycmd --version 8.2.0.7535 || dotnet tool update -g ilspycmd
fi

decompile () {
  local dll_path="$1"
  local name
  name="$(basename "$dll_path" .dll)"
  if [ ! -f "$dll_path" ]; then
    echo "  SKIP (not found): $dll_path"
    return
  fi
  echo "  $name"
  mkdir -p "$OUT_DIR/$name"
  ilspycmd -p -o "$OUT_DIR/$name" "$dll_path" >/dev/null 2>&1 || echo "    (decompile failed: $name)"
}

pids=()

echo "Shared engine/gameplay assemblies (game bin, used by Native):"
for dll in \
  TaleWorlds.Core TaleWorlds.Library TaleWorlds.Engine TaleWorlds.ObjectSystem \
  TaleWorlds.Localization TaleWorlds.ModuleManager TaleWorlds.DotNet TaleWorlds.InputSystem \
  TaleWorlds.NavigationSystem TaleWorlds.Network TaleWorlds.Diamond TaleWorlds.PlayerServices \
  TaleWorlds.MountAndBlade TaleWorlds.MountAndBlade.Diamond TaleWorlds.MountAndBlade.Helpers \
  TaleWorlds.MountAndBlade.ViewModelCollection TaleWorlds.Core.ViewModelCollection
do
  decompile "$GAME_BIN/$dll.dll" & pids+=($!)
done

echo "Native module assemblies:"
for dll in TaleWorlds.MountAndBlade.View TaleWorlds.MountAndBlade.GauntletUI TaleWorlds.MountAndBlade.Platform.PC; do
  decompile "$GAME_MODULES/Native/bin/Win64_Shipping_Client/$dll.dll" & pids+=($!)
done

echo "Multiplayer module assemblies:"
for dll in TaleWorlds.MountAndBlade.Multiplayer TaleWorlds.MountAndBlade.Multiplayer.View \
           TaleWorlds.MountAndBlade.Multiplayer.ViewModelCollection TaleWorlds.MountAndBlade.Multiplayer.GauntletUI \
           TaleWorlds.MountAndBlade.DedicatedCustomServer.ClientHelper; do
  decompile "$GAME_MODULES/Multiplayer/bin/Win64_Shipping_Client/$dll.dll" & pids+=($!)
done

for p in "${pids[@]}"; do wait "$p"; done
echo
echo "Done. Browse/grep under: $OUT_DIR"
