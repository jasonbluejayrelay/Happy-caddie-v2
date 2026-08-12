#!/usr/bin/env bash
# Publish the Harvestline playtest build as self-contained single-file executables.
# Each output needs no .NET install on the target machine — just download and run.
#
# Usage:  ./tools/publish-playable.sh [rid ...]
# Default rids: linux-x64 win-x64 osx-x64 osx-arm64
set -euo pipefail

cd "$(dirname "$0")/.."
PROJ="src/Harvestline.Play/Harvestline.Play.csproj"
OUT="dist"
RIDS=("${@:-linux-x64 win-x64 osx-x64 osx-arm64}")
# shellcheck disable=SC2206
RIDS=(${RIDS[@]})

rm -rf "$OUT"
for rid in "${RIDS[@]}"; do
  echo "==> publishing $rid"
  dotnet publish "$PROJ" -c Release -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o "$OUT/$rid" >/dev/null
  # Zip each so the executable bit / extension survives download.
  ( cd "$OUT" && zip -qr "harvestline-$rid.zip" "$rid" )
  echo "    -> $OUT/harvestline-$rid.zip"
done
echo "Done. Artifacts in $OUT/"
