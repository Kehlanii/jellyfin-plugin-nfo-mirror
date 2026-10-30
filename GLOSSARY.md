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
