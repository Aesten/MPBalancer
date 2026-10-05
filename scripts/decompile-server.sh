#!/usr/bin/env bash
# Decompiles the Dedicated Server's assemblies into ref/DedicatedServerSource/<Assembly>/.
# The server ships all of its DLLs in bin/Win64_Shipping_Server (no per-module bin folders).
# Shared assemblies are its own builds, decompiled separately from the client's
# ref/GameSource so the two can be diffed; the server-only assemblies (DedicatedCustomServer,
# ListedServer, WebPanel, DotNetCore starters) exist only here.
#
# Usage: scripts/decompile-server.sh [path-to-dedicated-server-folder]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

SERVER_FOLDER="${1:-C:/Program Files (x86)/Steam/steamapps/common/Mount & Blade II Dedicated Server}"
SERVER_BIN="$SERVER_FOLDER/bin/Win64_Shipping_Server"
if [ ! -d "$SERVER_BIN" ]; then
  echo "Dedicated server bin folder not found: $SERVER_BIN" >&2
  exit 1
fi

OUT_DIR="$REPO_ROOT/ref/DedicatedServerSource"

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
for dll in \
  TaleWorlds.Core TaleWorlds.Library TaleWorlds.Engine TaleWorlds.ObjectSystem \
  TaleWorlds.Localization TaleWorlds.ModuleManager TaleWorlds.DotNet TaleWorlds.InputSystem \
  TaleWorlds.Network TaleWorlds.Diamond TaleWorlds.PlayerServices \
  TaleWorlds.MountAndBlade TaleWorlds.MountAndBlade.Diamond TaleWorlds.MountAndBlade.Helpers \
  TaleWorlds.MountAndBlade.Multiplayer \
  TaleWorlds.MountAndBlade.DedicatedCustomServer TaleWorlds.MountAndBlade.DedicatedCustomServer.WebPanel \
  TaleWorlds.MountAndBlade.DedicatedCommunityServer \
  TaleWorlds.MountAndBlade.ListedServer TaleWorlds.MountAndBlade.ListedServer.MapServer \
  TaleWorlds.MountAndBlade.DotNetCore TaleWorlds.Starter.DotNetCore TaleWorlds.Starter.Library
do
  decompile "$SERVER_BIN/$dll.dll" & pids+=($!)
done
for p in "${pids[@]}"; do wait "$p"; done

echo
echo "Done. Browse/grep under: $OUT_DIR"
