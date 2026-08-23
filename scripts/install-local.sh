#!/usr/bin/env bash
# Packs CodeRail.Cli into a local .nupkg and installs it as the `coderail` dotnet tool,
# so you can test the version currently checked out instead of what's on nuget.org.
#
# Usage: scripts/install-local.sh
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cli_project="$repo_root/src/CodeRail.Cli/CodeRail.Cli.csproj"
pack_dir="$repo_root/artifacts/local-pack"

rm -rf "$pack_dir"
mkdir -p "$pack_dir"

echo "Packing $cli_project -> $pack_dir"
dotnet pack "$cli_project" -c Release -o "$pack_dir"

nupkg=$(find "$pack_dir" -maxdepth 1 -name 'CodeRail.*.nupkg' | head -n1)
if [ -z "$nupkg" ]; then
    echo "error: no CodeRail .nupkg produced in $pack_dir" >&2
    exit 1
fi
echo "Built $(basename "$nupkg")"

# MinVer versions local builds as prerelease (e.g. 0.1.2-alpha.0.3). Without a pinned
# --version, `dotnet tool install` prefers a stable release from nuget.org over our
# local prerelease, silently installing the wrong build - so extract the version from
# the produced filename and pin it explicitly.
version=$(basename "$nupkg" .nupkg | sed 's/^CodeRail\.//')
echo "Local package version: $version"

if dotnet tool list -g | grep -q '^coderail '; then
    echo "Uninstalling existing global coderail tool"
    dotnet tool uninstall -g coderail
fi

echo "Installing coderail from local package feed"
dotnet tool install -g coderail --add-source "$pack_dir" --no-cache --version "$version"

echo
echo "Installed. Try: coderail --help"
