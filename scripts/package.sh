#!/usr/bin/env bash
# Builds dist/nfo-mirror_<version>.zip (plugin DLL + meta.json) from build.yaml.
set -euo pipefail
cd "$(dirname "$0")/.."

[ -f lib/MediaBrowser.XbmcMetadata.dll ] || ./scripts/fetch-jellyfin-refs.sh
./scripts/dotnet.sh build Jellyfin.Plugin.NfoMirror/Jellyfin.Plugin.NfoMirror.csproj -c Release -o artifacts/bin

version="$(python3 -c 'import yaml; print(yaml.safe_load(open("build.yaml"))["version"])')"
stage="artifacts/stage"
rm -rf "$stage" && mkdir -p "$stage" dist
cp artifacts/bin/Jellyfin.Plugin.NfoMirror.dll "$stage/"

python3 - "$stage/meta.json" <<'EOF'
import json, sys, yaml, datetime
b = yaml.safe_load(open("build.yaml"))
meta = {
    "category": b["category"],
    "changelog": b["changelog"].strip(),
    "description": b["description"].strip(),
    "guid": b["guid"],
    "name": b["name"],
    "overview": b["overview"],
    "owner": b["owner"],
    "targetAbi": b["targetAbi"],
    "timestamp": datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    "version": b["version"],
    "status": "Active",
    "autoUpdate": True,
    "imagePath": "",
    "assemblies": b["artifacts"],
}
json.dump(meta, open(sys.argv[1], "w"), indent=2)
EOF

zip="dist/nfo-mirror_${version}.zip"
rm -f "$zip"
(cd "$stage" && zip -qX "../../$zip" Jellyfin.Plugin.NfoMirror.dll meta.json)
echo "$zip $(md5sum "$zip" | cut -d' ' -f1)"
