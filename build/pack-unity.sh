#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
out=unity/com.sa.inbetween/Plugins
mkdir -p "$out"
dotnet build SA.Inbetween.sln -c Release -warnaserror
for p in Soma Contract Engine Generators; do
  cp "src/SA.Inbetween.$p/bin/Release/netstandard2.1/SA.Inbetween.$p.dll" "$out/"
done
(cd "$out" && sha256sum SA.Inbetween.*.dll > SHA256SUMS)
cat "$out/SHA256SUMS"
