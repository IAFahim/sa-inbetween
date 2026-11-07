#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
for round in 1 2 3 4 5; do
  out=$(dotnet build SA.Inbetween.sln -p:TargetFramework=net10.0 2>&1 || true)
  missing=$(printf '%s\n' "$out" | grep -E "error RS0016" || true)
  [ -z "$missing" ] && break
  printf '%s\n' "$missing" | python3 -c '
import re, sys, collections
found = collections.defaultdict(set)
for line in sys.stdin:
    m = re.search(r"Symbol \x27(.*)\x27 is not part of the declared public API.*\[(.*?\.csproj)", line)
    if m:
        found[m.group(2)].add(m.group(1))
for proj, symbols in found.items():
    path = proj.rsplit("/", 1)[0] + "/PublicAPI.Shipped.txt"
    lines = set(l.rstrip("\n") for l in open(path) if l.strip() and not l.startswith("#"))
    lines |= symbols
    open(path, "w").write("#nullable enable\n" + "".join(s + "\n" for s in sorted(lines, key=str.casefold)))
'
done
dotnet build SA.Inbetween.sln 2>&1 | grep -E " error | warning " | sed 's/\[.*//' | sort -u || true
