#!/usr/bin/env bash
# Builds dist/LabelBridge-Setup.exe (Windows x64) on macOS/Linux/Windows.
set -euo pipefail
cd "$(dirname "$0")"

PUB="$PWD/publish"
COMMON=(-c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none)

rm -rf "$PUB" dist
dotnet publish src/LabelBridge "${COMMON[@]}" -p:IsTransformWebConfigDisabled=true -o "$PUB/service"
dotnet publish src/LabelBridge.Tray "${COMMON[@]}" -o "$PUB/tray"
dotnet publish src/LabelBridge.Setup "${COMMON[@]}" -p:PayloadDir="$PUB" -o "$PUB/setup"

mkdir -p dist
cp "$PUB/setup/LabelBridge-Setup.exe" dist/
ls -lh dist/LabelBridge-Setup.exe
