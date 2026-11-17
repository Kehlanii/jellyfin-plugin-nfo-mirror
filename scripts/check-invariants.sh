#!/usr/bin/env bash
# Usage: JELLYFIN_URL=http://host:8096 JELLYFIN_API_KEY=... scripts/check-invariants.sh
set -euo pipefail

: "${JELLYFIN_URL:?set JELLYFIN_URL}"
: "${JELLYFIN_API_KEY:?set JELLYFIN_API_KEY}"

result=$(curl -fsS "$JELLYFIN_URL/NfoMirror/Check?api_key=$JELLYFIN_API_KEY")
echo "$result" | jq

echo "$result" | jq -r '"items=\(.Items // .items) missing=\(.MissingCount // .missingCount) unmapped=\(.UnmappedCount // .unmappedCount) orphans=\(.Orphans // .orphans)"'

echo "$result" | jq -e '
  (.Error // .error) == null and ((.MissingCount // .missingCount) == 0) and ((.UnmappedCount // .unmappedCount) == 0)
' >/dev/null
