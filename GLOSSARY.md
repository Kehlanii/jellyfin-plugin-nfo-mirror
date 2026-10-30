# Glossary

## Media root

A library folder as mounted in the container, for example `/movies`. It is the path you add as a library location in Jellyfin, and in a read-only setup it is mounted with `:ro`.

## NFO root

The one writable folder that receives the mirrored `.nfo` files. The default is `/NFO`. It must not be inside a media root, and the plugin never touches anything outside it.

## Mirror path

The full `.nfo` path for an item: the NFO root plus the absolute media path with Jellyfin's NFO naming. For example `/movies/Shrek (2001).mkv` maps to `/NFO/movies/Shrek (2001).nfo`.

## Flat library

A library where video files sit directly in the media root, with no per-movie folders. Jellyfin marks such items `IsInMixedFolder`, and the NFO is written next to the media file name (`Shrek (2001).nfo`).

## Nested library

A library organised in folders, such as a show folder with season folders and episode files. Its NFOs are the fixed names `movie.nfo`, `tvshow.nfo` and `season.nfo`, plus one `.nfo` per episode file.

## Orphan NFO

An `.nfo` under the NFO root that no current item maps to, usually because media was moved, renamed or removed. The orphan sweep moves it, never deletes it, to `/NFO/.orphans/<same relative path>`.

## Alternate version

Two or more media files that Jellyfin groups as one item, for example `Show S01E03.mkv` and `Show S01E03 - 720p.mkv`. The extra versions are hidden from Jellyfin's default item queries but still get their own NFO. Versions inside one movie folder share `movie.nfo`; the primary version writes it.

## Full sync

The scheduled task that saves the NFO of every Movies/Shows item, then runs the orphan sweep.

## Changed sync

The scheduled task that saves only items whose metadata was saved in Jellyfin since the previous run started.

## Orphan sweep

The step that finds orphan NFOs. Its mode is Off, Preview (list them, move nothing) or Move (move them to `.orphans`).

## Baseline sweep

The first orphan sweep after the plugin is installed or upgraded. It always runs as a preview, whatever the sweep mode says.

## Auto-restore

Moving an NFO back out of `.orphans` to its mirror path when a current item maps to that path again and no NFO exists there.
