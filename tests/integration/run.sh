#!/usr/bin/env bash
# End-to-end test against jellyfin/jellyfin in Podman. Needs: podman, curl, jq, unzip.
# Usage: tests/integration/run.sh [path/to/plugin.zip]
set -euo pipefail

repo="$(cd "$(dirname "$0")/../.." && pwd)"
zip="${1:-$(ls "$repo"/dist/nfo-mirror_*.zip 2>/dev/null | sort -V | tail -1)}"
[ -f "$zip" ] || { echo "No plugin zip; run scripts/package.sh first."; exit 1; }
image="${IT_IMAGE:-docker.io/jellyfin/jellyfin:12.4}"
work="${IT_DIR:-$repo/artifacts/it}"
name=nfo-mirror-it
port=18096
api="http://127.0.0.1:$port"
auth_header='MediaBrowser Client="it", Device="it", DeviceId="nfo-mirror-it", Version="1.0"'
token=""
fails=0

pass() { echo "PASS: $*"; }
fail() { echo "FAIL: $*"; fails=$((fails + 1)); }
check() { if eval "$2"; then pass "$1"; else fail "$1"; fi; }

call() {
    local method=$1 path=$2 body=${3:-}
    local h="$auth_header"
    [ -n "$token" ] && h="$h, Token=\"$token\""
    curl -fsS -X "$method" "$api$path" -H "Authorization: $h" -H 'Content-Type: application/json' ${body:+-d "$body"}
}

make_media() {
    local ff="/usr/lib/jellyfin-ffmpeg/ffmpeg"
    mkdir -p "$work/media/movies" "$work/media/shows/Testshow Qzx/Season 01"
    podman run --rm --entrypoint "$ff" -v "$work/media:/m:Z" "$image" \
        -loglevel error -f lavfi -i color=black:s=64x64:d=1 -c:v libx264 -t 1 /m/sample.mkv
    for f in "movies/Qzxv Testmovie One (2001).mkv" "movies/Qzxv Testmovie Two (2002).mkv" \
             "movies/Zażółć gęślą Testfilm (2020).mkv" \
             "shows/Testshow Qzx/Season 01/Testshow Qzx S01E01.mkv" \
             "shows/Testshow Qzx/Season 01/Testshow Qzx S01E02.mkv" \
             "shows/Testshow Qzx/Season 01/Testshow Qzx S01E03.mkv"; do
        cp "$work/media/sample.mkv" "$work/media/$f"
    done
    rm "$work/media/sample.mkv"
}

install_plugin() {
    mkdir -p "$work/config/plugins/NFO Mirror"
    unzip -qo "$zip" -d "$work/config/plugins/NFO Mirror"
}

start() {
    podman run -d --name "$name" -p "127.0.0.1:$port:8096" \
        -v "$work/config:/config:Z" -v "$work/cache:/cache:Z" \
        -v "$work/media/movies:/movies:ro,Z" -v "$work/media/shows:/shows:ro,Z" \
        -v "$work/nfo:/NFO:Z" "$image" >/dev/null
    for _ in $(seq 120); do
        # Info/Public answers before startup finishes; the wizard endpoints 503 until then.
        curl -fsS "$api/Startup/Configuration" -H "Authorization: $auth_header" >/dev/null 2>&1 && return 0
        sleep 1
    done
    echo "Jellyfin did not start"; podman logs --tail 50 "$name"; exit 1
}

stop() { podman rm -f "$name" >/dev/null 2>&1 || true; }

setup_server() {
    token=""
    call POST /Startup/Configuration '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
    call GET /Startup/User >/dev/null
    call POST /Startup/User '{"Name":"admin","Password":"admin"}' >/dev/null
    call POST /Startup/Complete >/dev/null
    token="$(call POST /Users/AuthenticateByName '{"Username":"admin","Pw":"admin"}' | jq -r .AccessToken)"

    # Library settings exactly as in the README.
    local opts='{"LibraryOptions":{"MetadataSavers":["NFO Mirror"],"DisabledLocalMetadataReaders":["Nfo"],"LocalMetadataReaderOrder":["NFO Mirror","Nfo"],"PathInfos":[{"Path":"PATH"}]}}'
    call POST "/Library/VirtualFolders?name=movies&collectionType=movies&refreshLibrary=false" "${opts/PATH//movies}" >/dev/null
    call POST "/Library/VirtualFolders?name=shows&collectionType=tvshows&refreshLibrary=false" "${opts/PATH//shows}" >/dev/null
}

task_id() { call GET /ScheduledTasks | jq -r --arg k "$1" '.[] | select(.Key == $k) | .Id'; }
task_end() { call GET "/ScheduledTasks/$1" | jq -r '.LastExecutionResult.EndTimeUtc // ""'; }

run_task() {
    local id before
    id="$(task_id "$1")"
    before="$(task_end "$id")"
    call POST "/ScheduledTasks/Running/$id" >/dev/null
    for _ in $(seq 300); do
        sleep 1
        local now state
        now="$(task_end "$id")"; state="$(call GET "/ScheduledTasks/$id" | jq -r .State)"
        [ "$now" != "$before" ] && [ "$state" = Idle ] && return 0
    done
    echo "Task $1 did not finish"; return 1
}

scan_and_sync() {
    run_task RefreshLibrary
    run_task NfoMirrorFullSync
}

snapshot_media() { (cd "$work/media" && find . -printf '%p %s %T@\n' | sort); }

expected_nfos=(
    "movies/Qzxv Testmovie One (2001).nfo"
    "movies/Qzxv Testmovie Two (2002).nfo"
    "movies/Zażółć gęślą Testfilm (2020).nfo"
    "shows/Testshow Qzx/tvshow.nfo"
    "shows/Testshow Qzx/Season 01/season.nfo"
    "shows/Testshow Qzx/Season 01/Testshow Qzx S01E01.nfo"
    "shows/Testshow Qzx/Season 01/Testshow Qzx S01E02.nfo"
    "shows/Testshow Qzx/Season 01/Testshow Qzx S01E03.nfo"
)

# $work is wiped below, so only accept a dedicated scratch folder.
case "$work" in
    "$repo"/artifacts/*|/tmp/*) ;;
    *) echo "Refusing IT_DIR=$work: must be under $repo/artifacts or /tmp"; exit 1 ;;
esac

trap stop EXIT
stop
rm -rf "$work" && mkdir -p "$work/config" "$work/cache" "$work/nfo"
make_media
install_plugin
before_media="$(snapshot_media)"

echo "== first run"
start
setup_server
check "plugin loaded" '[ "$(call GET /Plugins | jq -r ".[] | select(.Name == \"NFO Mirror\") | .Status")" = Active ]'
scan_and_sync
echo "last sync: $(call GET /Plugins/03136b88-4248-4296-a0ea-29bf7e8a9c00/Configuration | jq -r .LastSyncResult)"

for f in "${expected_nfos[@]}"; do
    check "exists /NFO/$f" '[ -f "$work/nfo/$f" ]'
done
check "only expected .nfo files in /NFO" '[ "$(cd "$work/nfo" && find . -type f | wc -l)" -eq ${#expected_nfos[@]} ]'
check "nfo has movie root element" 'grep -q "<movie>" "$work/nfo/movies/Qzxv Testmovie One (2001).nfo"'
check "nothing written to media mounts (first run)" '[ "$(snapshot_media)" = "$before_media" ]'

echo "== edit title"
uid="$(call GET /Users/Me | jq -r .Id)"
item_id="$(call GET "/Items?userId=$uid&recursive=true&includeItemTypes=Movie&searchTerm=Testmovie%20One" | jq -r '.Items[0].Id')"
dto="$(call GET "/Items/$item_id?userId=$uid" | jq '.Name = "Edited Title Łódź"')"
call POST "/Items/$item_id" "$dto" >/dev/null
for _ in $(seq 30); do
    grep -q "<title>Edited Title Łódź</title>" "$work/nfo/movies/Qzxv Testmovie One (2001).nfo" && break
    sleep 1
done
check "edited title written to mirror NFO" 'grep -q "<title>Edited Title Łódź</title>" "$work/nfo/movies/Qzxv Testmovie One (2001).nfo"'

echo "== rebuild database"
stop
rm -rf "$work/config" "$work/cache"
mkdir -p "$work/config" "$work/cache"
install_plugin
start
setup_server
run_task RefreshLibrary
uid="$(call GET /Users/Me | jq -r .Id)"
titles="$(call GET "/Items?userId=$uid&recursive=true&includeItemTypes=Movie" | jq -r '.Items[].Name')"
echo "movies after rebuild: $(echo "$titles" | tr '\n' '|')"
check "edited title restored from /NFO" 'grep -qx "Edited Title Łódź" <<<"$titles"'

echo "== orphan"
rm "$work/media/movies/Qzxv Testmovie Two (2002).mkv"
before_media="$(snapshot_media)"
scan_and_sync
echo "last sync: $(call GET /Plugins/03136b88-4248-4296-a0ea-29bf7e8a9c00/Configuration | jq -r .LastSyncResult)"
check "orphan moved to .orphans" '[ -f "$work/nfo/.orphans/movies/Qzxv Testmovie Two (2002).nfo" ] && [ ! -f "$work/nfo/movies/Qzxv Testmovie Two (2002).nfo" ]'
check "other NFOs kept" '[ -f "$work/nfo/movies/Qzxv Testmovie One (2001).nfo" ]'

check "nothing written to media mounts" '[ "$(snapshot_media)" = "$before_media" ]'
check "no .nfo files in media" '[ -z "$(find "$work/media" -name "*.nfo")" ]'

echo
[ "$fails" -eq 0 ] && echo "ALL INTEGRATION CHECKS PASSED" || { echo "$fails CHECK(S) FAILED"; exit 1; }
