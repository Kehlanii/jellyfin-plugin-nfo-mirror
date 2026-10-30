#!/usr/bin/env bash
# Downloads MediaBrowser.XbmcMetadata.dll from the official Jellyfin server release into lib/.
set -euo pipefail

version="${1:-$(sed -n 's:.*<JellyfinVersion>\(.*\)</JellyfinVersion>.*:\1:p' "$(dirname "$0")/../Directory.Build.props")}"
# Release folders drop a trailing ".0" (12.4.0 -> v12.4).
tag="${version%.0}"
url="https://repo.jellyfin.org/files/server/linux/stable/v${tag}/amd64/jellyfin_${tag}-amd64.tar.gz"
lib="$(cd "$(dirname "$0")/.." && pwd)/lib"

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT

echo "Fetching $url"
curl -fsSL "$url" -o "$tmp/jellyfin.tar.gz"
tar -xzf "$tmp/jellyfin.tar.gz" -C "$tmp" --wildcards '*/MediaBrowser.XbmcMetadata.dll'
mkdir -p "$lib"
find "$tmp" -name MediaBrowser.XbmcMetadata.dll -exec cp {} "$lib/" \;
echo "$version" > "$lib/VERSION"
echo "Wrote $lib/MediaBrowser.XbmcMetadata.dll ($version)"
