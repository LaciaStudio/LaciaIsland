#!/usr/bin/env bash
# Builds a self-contained single-file LaciaIsland.exe (Windows x64) into ./publish
set -euo pipefail
cd "$(dirname "$0")"
dotnet publish src/LaciaIsland/LaciaIsland.csproj -c Release -f net10.0-windows10.0.19041.0 -r win-x64 --self-contained \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=none -o publish
echo "Done: $(pwd)/publish/LaciaIsland.exe"
