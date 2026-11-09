#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
version="${1:?usage: package-release.sh <version>}"
rm -rf dist stage
mkdir -p dist stage
for rid in linux-x64 win-x64; do
  dotnet publish apps/SA.Inbetween.Cli -c Release -r "$rid" --self-contained true -p:PublishSingleFile=false -o "stage/cli-$rid"
  cp LICENSE NOTICE README.md "stage/cli-$rid/"
  (cd stage && if [ "$rid" = win-x64 ]; then zip -qr "../dist/sa-inbetween-$version-$rid.zip" "cli-$rid"; else tar -czf "../dist/sa-inbetween-$version-$rid.tar.gz" "cli-$rid"; fi)
done
motion="${SA_MOTION_DIR:-../sa-motion}"
if [ -d "$motion/tools/SA.Soma.Bake" ]; then
  for rid in linux-x64 win-x64; do
    tools="stage/SaInbetween-$rid"
    mkdir -p "$tools/cli" "$tools/bake" "$tools/model"
    cp -r "stage/cli-$rid/." "$tools/cli/"
    dotnet publish "$motion/tools/SA.Soma.Bake" -c Release -r "$rid" --self-contained true -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false -o "$tools/bake"
    cp "$motion/families/humanoid-v1.safamily" "$tools/"
    cp build/tools-readme.md "$tools/README.md"
    (cd stage && if [ "$rid" = win-x64 ]; then zip -qr "../dist/SaInbetween-tools-$version-$rid.zip" "SaInbetween-$rid"; else tar -czf "../dist/SaInbetween-tools-$version-$rid.tar.gz" "SaInbetween-$rid"; fi)
  done
fi
build/pack-unity.sh >/dev/null
tar -czf "dist/com.sa.inbetween-$version.tgz" -C unity com.sa.inbetween
tar -czf "dist/com.sa.inbetween.sa-$version.tgz" -C unity com.sa.inbetween.sa
tar -czf "dist/com.sa.inbetween.sa.timeline-$version.tgz" -C unity com.sa.inbetween.sa.timeline
(cd dist && sha256sum * > SHA256SUMS)
cat dist/SHA256SUMS
