#!/usr/bin/env bash
# Runs dotnet from the host if installed, otherwise from the official SDK container.
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
if command -v dotnet >/dev/null 2>&1; then
    exec dotnet "$@"
fi
mkdir -p "$root/.nuget-cache"
exec podman run --rm -v "$root:/src:Z" -v "$root/.nuget-cache:/root/.nuget/packages:Z" -w /src \
    mcr.microsoft.com/dotnet/sdk:10.0 dotnet "$@"
