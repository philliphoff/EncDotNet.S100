#!/usr/bin/env bash
# Checks that the packed NuGet packages can be restored by a consumer
# (issue #1702). A packable project that references an IsPackable=false
# project packs fine, but its nuspec then lists a package that is never
# published, and consumers fail to restore from nuget.org with NU1101.
#
# Two checks, both against a folder of freshly packed .nupkg files:
#
#   1. Every EncDotNet.S100* dependency in every package's nuspec is itself
#      in the folder. Covers all packages and needs no network.
#   2. A small consumer project referencing EncDotNet.S100,
#      EncDotNet.S100.Collections and EncDotNet.S100.Routes.Rtz restores from
#      the folder plus nuget.org. Package source mapping sends EncDotNet.S100*
#      to the folder only, so a missing package can't be found on nuget.org
#      instead; a private packages folder means no cached copy hides one.
#
# Usage: tools/package-check/check-package-restore.sh <feed-dir> <version>
#   e.g. dotnet pack EncDotNet.S100.slnx -c Release -o nupkgs -p:Version=0.0.0-dev
#        tools/package-check/check-package-restore.sh nupkgs 0.0.0-dev
set -euo pipefail

if [ $# -ne 2 ]; then
    echo "usage: $0 <feed-dir> <version>" >&2
    exit 2
fi

FEED="$(cd "$1" && pwd)"
VERSION="$2"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

shopt -s nullglob
packages=("$FEED"/*.nupkg)
if [ ${#packages[@]} -eq 0 ]; then
    echo "error: no .nupkg files in $FEED" >&2
    exit 1
fi

# Package ids in the feed, lower-cased (NuGet ids are case-insensitive).
declare -A in_feed
for nupkg in "${packages[@]}"; do
    name="$(basename "$nupkg" ".$VERSION.nupkg")"
    in_feed["${name,,}"]=1
done

# 1. Every first-party dependency is in the feed.
missing=0
for nupkg in "${packages[@]}"; do
    nuspec="$(unzip -Z1 "$nupkg" '*.nuspec')"
    deps="$(unzip -p "$nupkg" "$nuspec" |
        grep -oE '<dependency id="EncDotNet\.S100[^"]*"' |
        sed -E 's/<dependency id="([^"]+)"/\1/' | sort -u || true)"
    for dep in $deps; do
        if [ -z "${in_feed[${dep,,}]:-}" ]; then
            echo "::error::$(basename "$nupkg") depends on $dep, which isn't packed. Make $dep packable or drop the reference."
            missing=1
        fi
    done
done
if [ "$missing" -ne 0 ]; then
    exit 1
fi
echo "All EncDotNet.S100 dependencies of ${#packages[@]} packages are in the feed."

# 2. Restore a consumer from the feed plus nuget.org, outside the repository so
#    none of its Directory.Build.* or Directory.Packages.props apply.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cp "$HERE/PackageConsumer.csproj" "$work/"
cat > "$work/nuget.config" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="local" value="$FEED" />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
  <packageSourceMapping>
    <packageSource key="local">
      <package pattern="EncDotNet.S100" />
      <package pattern="EncDotNet.S100.*" />
    </packageSource>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
  </packageSourceMapping>
</configuration>
EOF

log="$work/restore.log"
if ! dotnet restore "$work/PackageConsumer.csproj" \
        --configfile "$work/nuget.config" \
        --packages "$work/packages" \
        -p:EncDotNetVersion="$VERSION" >"$log" 2>&1; then
    cat "$log"
    if grep -q 'NU1101' "$log"; then
        echo "::error::A packed package depends on a package that isn't published (NU1101)."
    else
        echo "::error::The package consumer failed to restore."
    fi
    exit 1
fi
echo "Package consumer restored EncDotNet.S100 $VERSION from $FEED."
