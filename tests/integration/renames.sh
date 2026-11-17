#!/usr/bin/env bash
# End-to-end test of rename-follow (and retention, Check, DB-rebuild oracle) against jellyfin/jellyfin in Podman.
# Usage: tests/integration/renames.sh [path/to/plugin.zip]   (needs: podman, curl, jq, unzip)
set -euo pipefail

repo="$(cd "$(dirname "$0")/../.." && pwd)"
zip="${1:-$(ls "$repo"/dist/nfo-mirror_*.zip 2>/dev/null | sort -V | tail -1)}"
[ -f "$zip" ] || { echo "No plugin zip; run scripts/package.sh first."; exit 1; }
image="${IT_IMAGE:-docker.io/jellyfin/jellyfin:12.4}"
work="${IT_DIR:-$repo/artifacts/it-renames}"
name=nfo-mirror-it-renames
port=18097
api="http://127.0.0.1:$port"
auth_header='MediaBrowser Client="it", Device="it", DeviceId="nfo-mirror-it-renames", Version="1.0"'
token=""
fails=0
plugin_id=03136b88-4248-4296-a0ea-29bf7e8a9c00

. "$(dirname "$0")/lib.sh"

# $work is wiped below, so only accept a dedicated scratch folder.
case "$work" in
    "$repo"/artifacts/*|/tmp/*) ;;
    *) echo "Refusing IT_DIR=$work: must be under $repo/artifacts or /tmp"; exit 1 ;;
esac

nfo="$work/nfo"
media="$work/media"

make_media() {
    local ff="/usr/lib/jellyfin-ffmpeg/ffmpeg" i=0 f
    mkdir -p "$media/movies/Own Folder (2005)" "$media/movies2" \
        "$media/shows/Show One/Season 01" "$media/shows/Show Two/Season 01" "$media/shows/Show Three/Season 01"
    podman run --rm --entrypoint "$ff" -v "$media:/m:Z" "$image" \
        -loglevel error -f lavfi -i color=black:s=64x64:d=1 -c:v libx264 -t 1 /m/sample.mkv
    # Distinct sizes, so a file can be told apart from its copies after a move.
    for f in "movies/Alpha Movie (2001).mkv" "movies/Beta Movie (2002).mkv" "movies/Gamma Movie (2003).mkv" \
             "movies/Delta Movie (2004).mkv" "movies/Own Folder (2005)/Own Folder (2005).mkv" \
             "movies/Epsilon Movie (2006).mkv" "movies/Zeta Movie (2007).mkv" "movies2/Anchor Movie (2010).mkv" \
             "shows/Show One/Season 01/Show One S01E01.mkv" "shows/Show One/Season 01/Show One S01E02.mkv" \
             "shows/Show Two/Season 01/Show Two S01E01.mkv" "shows/Show Two/Season 01/Show Two S01E02.mkv" \
             "shows/Show Three/Season 01/Show Three S01E01.mkv"; do
        i=$((i + 1))
        cp "$media/sample.mkv" "$media/$f"
        head -c $((i * 4096)) /dev/zero >> "$media/$f"
    done
    rm "$media/sample.mkv"
    if [ -n "$(find "$media" -type f -printf '%s\n' | sort | uniq -d)" ]; then
        echo "Sample files do not have distinct sizes"; exit 1
    fi
}

start() {
    start_container -v "$media/movies:/movies:ro,Z" -v "$media/movies2:/movies2:ro,Z" -v "$media/shows:/shows:ro,Z"
}

setup_server() {
    wizard
    add_library movies movies /movies no-fetchers
    add_library movies2 movies /movies2 no-fetchers
    add_library shows tvshows /shows no-fetchers
}

items_json() {
    local uid
    uid="$(call GET /Users/Me | jq -r .Id)"
    call GET "/Items?userId=$uid&recursive=true&includeItemTypes=Movie,Series,Season,Episode&fields=Path,ProviderIds" \
        | jq '[.Items[] | {Id, Type, Name, Path, ProviderIds}]'
}

item_name() { items_json | jq -r --arg p "$1" '[.[] | select(.Path == $p) | .Name][0] // ""'; }

nfo_title() {
    [ -f "$1" ] || return 0
    grep -m1 -o '<title>[^<]*</title>' "$1" | sed 's:<title>\(.*\)</title>:\1:' || true
}

count_nfos() { find "$nfo" -name '*.nfo' | wc -l; }

# $1 dir: true when it is missing or holds no files.
none_under() { [ ! -d "$1" ] || [ -z "$(find "$1" -type f)" ]; }

# annotate <container path> <title> <provider ids json> <mirror nfo path>
annotate() {
    local id uid dto
    id="$(items_json | jq -r --arg p "$1" '[.[] | select(.Path == $p) | .Id][0] // ""')"
    [ -n "$id" ] || { echo "No item at $1"; exit 1; }
    uid="$(call GET /Users/Me | jq -r .Id)"
    dto="$(call GET "/Items/$id?userId=$uid" | jq --arg n "$2" --argjson p "$3" '.Name = $n | .ProviderIds = $p')"
    call POST "/Items/$id" "$dto" >/dev/null
    for _ in $(seq 30); do
        grep -qF "<title>$2</title>" "$4" 2>/dev/null && return 0
        sleep 1
    done
    echo "Mirror NFO $4 never got title '$2'"; exit 1
}

trap stop EXIT
stop
rm -rf "$work" && mkdir -p "$work/config" "$work/cache" "$nfo"

echo "== setup"
make_media
install_plugin
start
setup_server
set_config '.SweepMode = "Move"'
scan_and_sync
run_task NfoMirrorFullSync
echo "last sync: $(config .LastSyncResult)"

expected_nfos=(
    "movies/Alpha Movie (2001).nfo"
    "movies/Beta Movie (2002).nfo"
    "movies/Gamma Movie (2003).nfo"
    "movies/Delta Movie (2004).nfo"
    "movies/Own Folder (2005)/movie.nfo"
    "movies/Epsilon Movie (2006).nfo"
    "movies/Zeta Movie (2007).nfo"
    "movies2/Anchor Movie (2010).nfo"
    "shows/Show One/tvshow.nfo"
    "shows/Show One/Season 01/season.nfo"
    "shows/Show One/Season 01/Show One S01E01.nfo"
    "shows/Show One/Season 01/Show One S01E02.nfo"
    "shows/Show Two/tvshow.nfo"
    "shows/Show Two/Season 01/season.nfo"
    "shows/Show Two/Season 01/Show Two S01E01.nfo"
    "shows/Show Two/Season 01/Show Two S01E02.nfo"
    "shows/Show Three/tvshow.nfo"
    "shows/Show Three/Season 01/season.nfo"
    "shows/Show Three/Season 01/Show Three S01E01.nfo"
)
for f in "${expected_nfos[@]}"; do
    check "exists /NFO/$f" '[ -f "$nfo/$f" ]'
done

echo "== annotate"
annotate "/movies/Alpha Movie (2001).mkv" "EDITED Alpha" '{"Imdb":"tt0000001","Tmdb":"1001"}' "$nfo/movies/Alpha Movie (2001).nfo"
annotate "/movies/Beta Movie (2002).mkv" "EDITED Beta" '{"Imdb":"tt0000002"}' "$nfo/movies/Beta Movie (2002).nfo"
annotate "/movies/Gamma Movie (2003).mkv" "EDITED Gamma" '{"Imdb":"tt0000003"}' "$nfo/movies/Gamma Movie (2003).nfo"
annotate "/movies/Own Folder (2005)/Own Folder (2005).mkv" "EDITED Own Folder" '{"Imdb":"tt0000005"}' "$nfo/movies/Own Folder (2005)/movie.nfo"
annotate "/movies/Epsilon Movie (2006).mkv" "EDITED Epsilon" '{}' "$nfo/movies/Epsilon Movie (2006).nfo"
annotate "/movies/Zeta Movie (2007).mkv" "EDITED Zeta" '{"Imdb":"tt0000007"}' "$nfo/movies/Zeta Movie (2007).nfo"
annotate "/shows/Show One/Season 01/Show One S01E01.mkv" "EDITED S1E1" '{"Tvdb":"9101"}' "$nfo/shows/Show One/Season 01/Show One S01E01.nfo"
annotate "/shows/Show Two" "EDITED Show Two" '{"Tvdb":"9200"}' "$nfo/shows/Show Two/tvshow.nfo"
annotate "/shows/Show Two/Season 01/Show Two S01E01.mkv" "EDITED Show Two E1" '{"Tvdb":"9201"}' "$nfo/shows/Show Two/Season 01/Show Two S01E01.nfo"
annotate "/shows/Show Three" "EDITED Show Three" '{"Tvdb":"9300"}' "$nfo/shows/Show Three/tvshow.nfo"
annotate "/shows/Show Three/Season 01/Show Three S01E01.mkv" "EDITED Show Three E1" '{}' "$nfo/shows/Show Three/Season 01/Show Three S01E01.nfo"
titles=("EDITED Alpha" "EDITED Beta" "EDITED Gamma" "EDITED Own Folder" "EDITED Epsilon" "EDITED Zeta" "EDITED S1E1"
        "EDITED Show Two" "EDITED Show Two E1" "EDITED Show Three" "EDITED Show Three E1")
n0="$(count_nfos)"
echo "NFOs after annotate: $n0"

echo "== renames"
check "FollowRenames is On" '[ "$(config .FollowRenames)" = On ]'
before_media="$(snapshot_media)"
mv "$media/movies/Alpha Movie (2001).mkv" "$media/movies/Alpha Movie Renamed (2001).mkv"
mv "$media/movies/Beta Movie (2002).mkv" "$media/movies/Beta Movie (2002).mp4"
mv "$media/movies/Own Folder (2005)" "$media/movies/Own Folder Renamed (2005)"
mv "$media/movies/Gamma Movie (2003).mkv" "$media/movies2/Gamma Movie (2003).mkv"
cp "$media/movies/Delta Movie (2004).mkv" "$work/delta.mkv"
rm "$media/movies/Delta Movie (2004).mkv"
mv "$media/shows/Show One/Season 01/Show One S01E01.mkv" "$media/shows/Show One/Season 01/Show One - S01E01 - Pilot.mkv"
mv "$media/shows/Show Two/Season 01" "$media/shows/Show Two/Season 1"
mv "$media/shows/Show Three" "$media/shows/Show Three Renamed"
after_rename_media="$(snapshot_media)"
run_task RefreshLibrary
echo "last sync: $(config .LastSyncResult)"

check "file rename: NFO followed" '[ "$(nfo_title "$nfo/movies/Alpha Movie Renamed (2001).nfo")" = "EDITED Alpha" ]'
check "file rename: old NFO gone" '[ ! -e "$nfo/movies/Alpha Movie (2001).nfo" ] && [ -z "$(find "$nfo/.orphans" -name "Alpha Movie (2001).nfo" 2>/dev/null)" ]'
check "file rename: item name kept" '[ "$(item_name "/movies/Alpha Movie Renamed (2001).mkv")" = "EDITED Alpha" ]'
check "extension change: NFO title kept" '[ "$(nfo_title "$nfo/movies/Beta Movie (2002).nfo")" = "EDITED Beta" ]'
check "extension change: item name kept" '[ "$(item_name "/movies/Beta Movie (2002).mp4")" = "EDITED Beta" ]'
check "own-folder rename: NFO followed" '[ "$(nfo_title "$nfo/movies/Own Folder Renamed (2005)/movie.nfo")" = "EDITED Own Folder" ]'
check "own-folder rename: old folder has no NFO" 'none_under "$nfo/movies/Own Folder (2005)"'
check "own-folder rename: item name kept" '[ "$(item_name "/movies/Own Folder Renamed (2005)/Own Folder (2005).mkv")" = "EDITED Own Folder" ]'
check "episode rename: NFO followed" '[ "$(nfo_title "$nfo/shows/Show One/Season 01/Show One - S01E01 - Pilot.nfo")" = "EDITED S1E1" ]'
check "episode rename: old NFO gone" '[ ! -e "$nfo/shows/Show One/Season 01/Show One S01E01.nfo" ]'
check "episode rename: item name kept" '[ "$(item_name "/shows/Show One/Season 01/Show One - S01E01 - Pilot.mkv")" = "EDITED S1E1" ]'
check "season folder rename: episode NFO followed" '[ "$(nfo_title "$nfo/shows/Show Two/Season 1/Show Two S01E01.nfo")" = "EDITED Show Two E1" ]'
check "season folder rename: season.nfo followed" '[ -f "$nfo/shows/Show Two/Season 1/season.nfo" ]'
check "season folder rename: nothing left in old folder" 'none_under "$nfo/shows/Show Two/Season 01"'
check "season folder rename: series NFO title kept" '[ "$(nfo_title "$nfo/shows/Show Two/tvshow.nfo")" = "EDITED Show Two" ]'
check "show folder rename: tvshow.nfo followed" '[ "$(nfo_title "$nfo/shows/Show Three Renamed/tvshow.nfo")" = "EDITED Show Three" ]'
check "show folder rename: episode NFO followed" '[ "$(nfo_title "$nfo/shows/Show Three Renamed/Season 01/Show Three S01E01.nfo")" = "EDITED Show Three E1" ]'
check "show folder rename: nothing left in old folder" 'none_under "$nfo/shows/Show Three"'
check "show folder rename: series item name kept" '[ "$(item_name "/shows/Show Three Renamed")" = "EDITED Show Three" ]'
check "real deletion: NFO stays at old path" '[ -f "$nfo/movies/Delta Movie (2004).nfo" ]'

# Moves between libraries are a documented limitation (ADR 0004); either outcome is fine, losing the edit is not.
gamma_followed=0
if [ "$(nfo_title "$nfo/movies2/Gamma Movie (2003).nfo")" = "EDITED Gamma" ]; then
    gamma_followed=1
    echo "gamma: followed"
    check "gamma: old NFO gone" '[ ! -e "$nfo/movies/Gamma Movie (2003).nfo" ]'
else
    echo "gamma: not followed (other scan order)"
    check "gamma: edit kept at old NFO" '[ "$(nfo_title "$nfo/movies/Gamma Movie (2003).nfo")" = "EDITED Gamma" ]'
fi

check "activity lists followed renames" '[ "$(activity_count Followed)" -ge 6 ]'
check "no NFO written into media" '[ -z "$(find "$media" -name "*.nfo")" ]'
check "media untouched apart from the renames" '[ "$(snapshot_media)" = "$after_rename_media" ]'
dups=""
for t in "${titles[@]}"; do
    c="$(grep -rlF "<title>$t</title>" "$nfo" | wc -l)"
    [ "$c" -le 1 ] || dups="$dups|$t=$c"
done
check "followed NFOs were moved, not copied" '[ -z "$dups" ]'
[ -z "$dups" ] || echo "duplicated titles: $dups"
echo "NFOs after renames: $(count_nfos) (was $n0)"

echo "== full sync"
run_task NfoMirrorFullSync
last_sync="$(config .LastSyncResult)"
echo "last sync: $last_sync"
check "Delta NFO moved to .orphans" '[ -f "$nfo/.orphans/movies/Delta Movie (2004).nfo" ]'
expected_orphans="movies/Delta Movie (2004).nfo"
[ "$gamma_followed" -eq 1 ] || expected_orphans="$expected_orphans"$'\n'"movies/Gamma Movie (2003).nfo"
expected_orphans="$(sort <<<"$expected_orphans")"
orphans="$(cd "$nfo/.orphans" 2>/dev/null && find . -type f -name '*.nfo' | sed 's:^\./::' | sort || true)"
check ".orphans holds only the expected NFOs" '[ "$orphans" = "$expected_orphans" ]'
[ "$orphans" = "$expected_orphans" ] || echo "orphans:"$'\n'"$orphans"
check "sweep was not skipped" '[[ "${last_sync,,}" != *"sweep skipped"* ]]'
chk="$(call GET /NfoMirror/Check || echo '{}')"
check "Check endpoint is clean" '[ "$(jq ".Error == null and .MissingCount == 0 and .UnmappedCount == 0" <<<"$chk")" = true ]'
[ "$(jq ".Error == null and .MissingCount == 0 and .UnmappedCount == 0" <<<"$chk")" = true ] || echo "$chk"
check "check-invariants.sh passes" '(cd "$repo" && JELLYFIN_URL="$api" JELLYFIN_API_KEY="$token" scripts/check-invariants.sh)'

echo "== preview mode"
set_config '.FollowRenames = "Preview"'
mv "$media/movies/Zeta Movie (2007).mkv" "$media/movies/Zeta Movie Renamed (2007).mkv"
run_task RefreshLibrary
check "activity lists would-follow" '[ "$(activity_count WouldFollow)" -ge 1 ]'
check "preview: old NFO untouched" '[ "$(nfo_title "$nfo/movies/Zeta Movie (2007).nfo")" = "EDITED Zeta" ]'
check "preview: new NFO is a fresh one" '[ "$(nfo_title "$nfo/movies/Zeta Movie Renamed (2007).nfo")" != "EDITED Zeta" ]'
set_config '.FollowRenames = "On"'

echo "== retention"
rm "$media/movies/Epsilon Movie (2006).mkv"
set_config '.OrphanRetentionDays = 1'
scan_and_sync
check "Delta orphan present before purge" '[ -f "$nfo/.orphans/movies/Delta Movie (2004).nfo" ]'
touch -d '3 days ago' "$nfo/.orphans/movies/Delta Movie (2004).nfo" 2>/dev/null || true
run_task NfoMirrorFullSync
check "expired orphan purged" '[ ! -e "$nfo/.orphans/movies/Delta Movie (2004).nfo" ]'
check "fresh orphan kept" '[ -f "$nfo/.orphans/movies/Epsilon Movie (2006).nfo" ]'
check "activity lists purged" '[ "$(activity_count Purged)" -ge 1 ]'
set_config '.OrphanRetentionDays = 0'

echo "== oracle"
stop
rm -rf "$work/config" "$work/cache"
mkdir -p "$work/config" "$work/cache"
install_plugin
start
setup_server
run_task RefreshLibrary
check "oracle: file rename" '[ "$(item_name "/movies/Alpha Movie Renamed (2001).mkv")" = "EDITED Alpha" ]'
check "oracle: extension change" '[ "$(item_name "/movies/Beta Movie (2002).mp4")" = "EDITED Beta" ]'
check "oracle: own-folder rename" '[ "$(item_name "/movies/Own Folder Renamed (2005)/Own Folder (2005).mkv")" = "EDITED Own Folder" ]'
check "oracle: episode rename" '[ "$(item_name "/shows/Show One/Season 01/Show One - S01E01 - Pilot.mkv")" = "EDITED S1E1" ]'
check "oracle: season folder rename" '[ "$(item_name "/shows/Show Two/Season 1/Show Two S01E01.mkv")" = "EDITED Show Two E1" ]'
check "oracle: show folder rename (series)" '[ "$(item_name "/shows/Show Three Renamed")" = "EDITED Show Three" ]'
check "oracle: show folder rename (episode)" '[ "$(item_name "/shows/Show Three Renamed/Season 01/Show Three S01E01.mkv")" = "EDITED Show Three E1" ]'
if [ "$gamma_followed" -eq 1 ]; then
    check "oracle: Gamma moved between libraries" '[ "$(item_name "/movies2/Gamma Movie (2003).mkv")" = "EDITED Gamma" ]'
else
    echo "gamma: skipped in oracle"
fi

check "no .nfo files in media" '[ -z "$(find "$media" -name "*.nfo")" ]'

echo
[ "$fails" -eq 0 ] && echo "ALL RENAME CHECKS PASSED" || { echo "$fails CHECK(S) FAILED"; exit 1; }
