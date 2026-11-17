#!/usr/bin/env bash
# Shared helpers for the integration scripts. Sourced after the caller sets
# $api, $auth_header, $token, $fails, $plugin_id, $name, $port, $image, $work, $zip.

config() { call GET "/Plugins/$plugin_id/Configuration" | jq -r "$1"; }
set_config() { call POST "/Plugins/$plugin_id/Configuration" "$(call GET "/Plugins/$plugin_id/Configuration" | jq "$1")" >/dev/null; }
set_sweep_mode() { set_config ".SweepMode = \"$1\""; }
activity_count() { call GET "/NfoMirror/Activity?after=0" | jq --arg k "$1" '[.Lines[] | select(.Kind == $k)] | length'; }

pass() { echo "PASS: $*"; }
fail() { echo "FAIL: $*"; fails=$((fails + 1)); }
check() { if eval "$2"; then pass "$1"; else fail "$1"; fi; }

call() {
    local method=$1 path=$2 body=${3:-}
    local h="$auth_header"
    [ -n "$token" ] && h="$h, Token=\"$token\""
    curl -fsS -X "$method" "$api$path" -H "Authorization: $h" -H 'Content-Type: application/json' ${body:+-d "$body"}
}

install_plugin() {
    mkdir -p "$work/config/plugins/NFO Mirror"
    unzip -qo "$zip" -d "$work/config/plugins/NFO Mirror"
}

# Extra args are podman -v mounts for the media.
start_container() {
    podman run -d --name "$name" -p "127.0.0.1:$port:8096" \
        -v "$work/config:/config:Z" -v "$work/cache:/cache:Z" \
        "$@" \
        -v "$work/nfo:/NFO:Z" "$image" >/dev/null
    for _ in $(seq 120); do
        # Info/Public answers before startup finishes; the wizard endpoints 503 until then.
        curl -fsS "$api/Startup/Configuration" -H "Authorization: $auth_header" >/dev/null 2>&1 && return 0
        sleep 1
    done
    echo "Jellyfin did not start"; podman logs --tail 50 "$name"; exit 1
}

stop() { podman rm -f "$name" >/dev/null 2>&1 || true; }

wizard() {
    token=""
    call POST /Startup/Configuration '{"UICulture":"en-US","MetadataCountryCode":"US","PreferredMetadataLanguage":"en"}' >/dev/null
    call GET /Startup/User >/dev/null
    call POST /Startup/User '{"Name":"admin","Password":"admin"}' >/dev/null
    call POST /Startup/Complete >/dev/null
    token="$(call POST /Users/AuthenticateByName '{"Username":"admin","Pw":"admin"}' | jq -r .AccessToken)"
}

# Library settings exactly as in the README. With no-fetchers, an empty
# TypeOptions entry per type is a full allow-list, so online scrapers stay off.
add_library() {
    local lname=$1 ctype=$2 path=$3 types='[]'
    if [ "${4:-}" = no-fetchers ]; then
        types='[{"Type":"Movie","MetadataFetchers":[],"ImageFetchers":[]},{"Type":"Series","MetadataFetchers":[],"ImageFetchers":[]},{"Type":"Season","MetadataFetchers":[],"ImageFetchers":[]},{"Type":"Episode","MetadataFetchers":[],"ImageFetchers":[]}]'
    fi
    local body
    body="$(jq -n --arg p "$path" --argjson t "$types" \
        '{LibraryOptions: ({MetadataSavers: ["NFO Mirror"], LocalMetadataReaderOrder: ["NFO Mirror", "Nfo"], PathInfos: [{Path: $p}]} + (if ($t | length) > 0 then {TypeOptions: $t} else {} end))}')"
    call POST "/Library/VirtualFolders?name=$lname&collectionType=$ctype&refreshLibrary=false" "$body" >/dev/null
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
