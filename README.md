# NFO Mirror

NFO Mirror is a Jellyfin plugin for servers whose media folders are mounted read-only. Jellyfin's built-in NFO saver always writes `.nfo` files next to the media, which fails on a read-only mount. NFO Mirror writes the same Kodi-compatible `.nfo` files into one writable folder, mirroring the media path, and reads them back so a rebuilt Jellyfin database gets its saved and edited metadata back.

## Installation

This plugin is based on Jellyfin Version 12.4
1. Open your Jellyfin Dashboard.
2. Navigate to Plugins > Repositories.
3. Click the + button to add a new repository.
4. Enter a name for the repo and paste the following URL:
   https://raw.githubusercontent.com/Kehlanii/jellyfin-plugin-nfo-mirror/main/manifest.json
5. Go to Catalog, install "NFO Mirror", and restart Jellyfin.

## Docker compose

Mount the media folders read-only and add one writable folder for the NFO files. Paths below are placeholders.

```yaml
services:
  jellyfin:
    image: jellyfin/jellyfin:12.4
    volumes:
      - /path/to/config:/config
      - /path/to/cache:/cache
      - /path/to/movies:/movies:ro
      - /path/to/shows:/shows:ro
      - /path/to/nfo:/NFO
```

The NFO folder must not be inside any media folder.

## Library setup

Do this for every Movies, Shows, and Mixed Movies and Shows library.

1. Open Dashboard > Libraries.
2. Click the ... button next to the library and choose Manage library.
3. Under Metadata savers, untick "Nfo".
4. Under Metadata savers, tick "NFO Mirror".
5. Under Metadata readers, move "NFO Mirror" to the top of the list, above "Nfo". Jellyfin 12.4 has no checkbox here, only the order.
6. Save.

The saver tick is required because Jellyfin only calls a metadata saver for a library when that saver is in the library's Metadata savers list. The reader must be at the top because the first local reader that finds a file wins.

## Syncing

Open the config page: Dashboard > Plugins > NFO Mirror. It has two buttons.

"Sync changed" queues the task "NFO Mirror: sync changed". It writes NFOs only for items Jellyfin saved since the previous changed sync. The first run has no previous sync, so it covers everything. It does not run the orphan sweep. The result appears on the "Last changed sync" line.

"Full sync" queues the task "NFO Mirror: full sync". It writes every item in Movies, Shows and Mixed Movies and Shows libraries, then runs the orphan sweep. The result appears on the "Last full sync" line.

Both tasks run under Dashboard > Scheduled Tasks, category Library. They never run at the same time; the second waits for the first to finish. Neither task has a default trigger. Add a trigger there if you want one, for example "sync changed" every 6 hours and "full sync" weekly.

After installing: press "Full sync" once, read the "Last full sync" line, and watch the Activity console.

The config page also warns for libraries where the built-in Nfo saver is still ticked or the Nfo reader is ranked above NFO Mirror, and shows an error if the NFO root overlaps a library. The plugin never changes library settings itself.

## Orphan sweep

An orphan NFO is an `.nfo` under the NFO root that no current item maps to, usually because media was moved, renamed or removed. Set the mode on the config page:

- Off: the sweep does not run.
- Preview (default): the sweep lists the NFOs that no item maps to and moves nothing. The config page shows the count and the first 100 paths.
- Move: the sweep moves each orphan to `/NFO/.orphans/<same path>`. The plugin never deletes an NFO.

The first full sync after an install or upgrade always previews, whatever the mode says. That is the baseline, and it runs once per plugin version.

The sweep is skipped entirely if a root folder of any Movies, Shows, or Mixed Movies and Shows library is unreachable or empty, or if more than 20% of the NFOs would move. This protects against a NAS outage.

Orphans are auto-restored. If an item maps to a path again and no NFO is there, the plugin moves the NFO back from `/NFO/.orphans`, so media that comes back keeps its old metadata. The reader does this when Jellyfin scans the item, and every sync checks again.

Recommendation: run a full sync in Preview, check the list, then switch to Move.

## Activity console

The config page shows a live list of what the plugin did: files created, updated, restored, moved, would-move, skipped (with the reason) and failed. It also shows the saves that Jellyfin itself triggers on a metadata change. Unchanged files are only counted. The list can be filtered per kind.

The console is kept in memory only: the last 5000 events, cleared when Jellyfin restarts. The Jellyfin log has the permanent record. Moved and restored files, the first 50 skipped files per reason and the first 100 orphans of a preview are logged at Information level, failures at Error level, and each written file at Debug level.

## How it works

NFO Mirror writes only `.nfo` files, nothing else. It handles Movie, Series, Season and Episode items. The mirror path is the NFO root plus the full media path, using Jellyfin's NFO naming.

| Media path | Mirror path |
|---|---|
| `/movies/Shrek (2001).mkv` | `/NFO/movies/Shrek (2001).nfo` |
| `/movies/Alien (1979)/Alien (1979).mkv` | `/NFO/movies/Alien (1979)/movie.nfo` |
| `/shows/Show/` | `/NFO/shows/Show/tvshow.nfo` |
| `/shows/Show/Season 01/` | `/NFO/shows/Show/Season 01/season.nfo` |
| `/shows/Show/Season 01/Show S01E01.mkv` | `/NFO/shows/Show/Season 01/Show S01E01.nfo` |

The plugin is both a saver and a reader, both named "NFO Mirror". Jellyfin calls the saver itself on every metadata change, in libraries where it is ticked. The reader loads metadata back from the mirrored file.

See "Syncing" for what each sync writes and when, "Orphan sweep" for NFOs that no item maps to, and "Activity console" for the console on the config page.

Safety: the plugin never writes, renames or deletes anything outside the NFO root. One guard checks every target path. It refuses to run if the NFO root is inside a library path, or a library path is inside the NFO root.

Limitation: watched state changes alone are not re-mirrored immediately, because Jellyfin only pushes user-data saves to the built-in "Nfo" saver. They are written on the next metadata save or full sync; the changed sync does not see them.

## Building

Everything is built locally. There is no CI. You need bash, curl, zip, python3 with PyYAML, and either the .NET 10 SDK or podman.

```shell
./scripts/fetch-jellyfin-refs.sh
./scripts/dotnet.sh build -c Release
./scripts/dotnet.sh test
./scripts/package.sh
tests/integration/run.sh   # Podman end-to-end test against jellyfin/jellyfin:12.4
```

- `fetch-jellyfin-refs.sh` downloads `MediaBrowser.XbmcMetadata.dll` from the official Jellyfin release tarball into `lib/`. The file is gitignored, is not on NuGet, and is never bundled.
- `dotnet.sh` uses the host `dotnet` if present, otherwise the `mcr.microsoft.com/dotnet/sdk:10.0` container via podman.
- `package.sh` builds the release zip `dist/nfo-mirror_<version>.zip` with the plugin DLL and `meta.json`.

## Updating for a new Jellyfin release

1. Bump `JellyfinVersion` in `Directory.Build.props`.
2. Run `./scripts/fetch-jellyfin-refs.sh` to get the matching DLL.
3. Bump `targetAbi` (for example `"12.5.0.0"`) and `version` in `build.yaml`.
4. Bump `Version`, `AssemblyVersion` and `FileVersion` in `Directory.Build.props`.
5. Run `./scripts/dotnet.sh build -c Release` and `./scripts/dotnet.sh test`. `SaverDriftTests` fail if Jellyfin changed its NFO naming.
6. Run `./scripts/package.sh` to build the release zip.
7. Create the GitHub release.
8. Add a new entry to `manifest.json` with the version, changelog, targetAbi, sourceUrl, checksum (MD5 of the zip) and timestamp (ISO-8601 UTC).

## License

GPLv3. See `LICENSE`. The license is kept from the Jellyfin plugin template because the plugin links Jellyfin GPL code.
