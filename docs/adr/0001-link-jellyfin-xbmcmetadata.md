# 1. Link against Jellyfin's XbmcMetadata

Date: 2026-10-30

## Status

Accepted

## Context

Jellyfin already ships Kodi-compatible NFO savers and parsers in `MediaBrowser.XbmcMetadata.dll`. They write only next to the media, so they cannot be used as-is here. The DLL is not published on NuGet.

## Decision

Take a compile-time reference to `MediaBrowser.XbmcMetadata.dll` from the official Jellyfin release tarball. `scripts/fetch-jellyfin-refs.sh` downloads it into `lib/` (gitignored). The reference uses `Private=false`, so the DLL is never bundled; the server provides it at runtime.

For writing, subclass the public `MovieNfoSaver`, `SeriesNfoSaver`, `SeasonNfoSaver` and `EpisodeNfoSaver` and override `GetLocalSavePath`. For reading, parse the mirror file with the public `MovieNfoParser`, `SeriesNfoParser`, `SeasonNfoParser` and `EpisodeNfoParser`.

## Consequences

- Much less code than copying the savers and parsers.
- The XML output tracks upstream Jellyfin automatically.
- The plugin breaks on major Jellyfin releases (ABI change), which is expected and handled by the release update steps.
- `MovieNfoSaver.GetMovieSavePaths` is internal, so the naming rule is mirrored in `MirrorPaths`. `SaverDriftTests` fail if Jellyfin changes its NFO naming.
- The subclasses are `internal`, so Jellyfin does not discover them as extra "Nfo" savers.
