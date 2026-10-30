# 2. Write NFO files outside the read-only media

Date: 2026-10-30

## Status

Accepted

## Context

Users mount media folders read-only (`:ro`) in Docker. Jellyfin's built-in Nfo saver always writes `.nfo` next to the media file, so saving metadata fails. Metadata still has to survive a Jellyfin database rebuild, which means it must live in a file the plugin can write and read.

## Decision

Use one writable NFO root (default `/NFO`) that mirrors the full media path. The mirror path is the NFO root plus the media path with Jellyfin's NFO naming. Only `.nfo` files are written, no images or other files. The path for an item is deterministic and needs no per-folder rules. The media stays read-only.

Rejected alternatives:

- Make the media writable. It defeats the point of the `:ro` mount and lets any process write into the media tree.
- Store metadata only in the Jellyfin database. It is lost when the database is rebuilt, which is the case this plugin exists for.
- Use a flat NFO root keyed by item id. Item ids change on a database rebuild, so the files could not be found again.

## Consequences

- Media stays read-only and the NFO root is a single folder to back up.
- When media is moved or renamed, its old NFO becomes an orphan. The orphan sweep moves such files to `/NFO/.orphans/<same relative path>` and never deletes them. It is skipped if a library root is unreachable or empty, or if more than 20% of NFOs would move.
- The NFO root must not overlap a library path. The plugin refuses to run if the root is inside a library, or a library is inside the root.
- The NFO root must be writable by the Jellyfin process and mounted separately from the media.
