# 4. Rename-follow keeps an NFO with its media

Date: 2026-11-17

## Status

Accepted

## Context

Jellyfin derives an item Id from MD5(type + path) and never updates an existing item's path, so any rename or move is a removal of the old item followed by a new item. A Podman recon on Jellyfin 12.4 with NFO Mirror 1.1.1.0 showed what that does to the mirror:

- A renamed movie file, a renamed movie folder, a move to another library, a renamed episode, a renamed season folder and a renamed show folder all lost the user's edited title and provider ids. The new item got a fresh NFO; the old NFO was left behind as an orphan.
- A container change (`.mkv` to `.mp4`) kept its edits, because the mirror path does not change.
- One show-folder rename produced 11 of 28 NFOs as orphans (39%), above the sweep's 20% cap.

Jellyfin removes the old item before it adds the new one within a folder scan. The removed item still carries its provider ids and file size (`BaseItem.Size`) when `ItemRemoved` fires. A brand-new item has no provider ids yet when the local reader runs, because it has not been scraped.

## Decision

- **Removal record**: on `ILibraryManager.ItemRemoved`, the plugin remembers, in memory, every removed Movie, Series, Season and Episode whose mirror NFO exists: its old mirror path, provider ids, file size and media path.
- **Rename-follow**: when the reader finds no NFO at a new item's mirror path, it looks for one matching removal record and moves the old NFO (for a Series or Season, every file in the old mirror folder) to the new mirror path, then reads it. Jellyfin then keeps the user's edits instead of scraping fresh metadata.
- **Matching order**: same provider id (in practice never, since the new item has no ids at read time; kept for when it does); else same exact file size in bytes for a Movie or Episode; else, for a Series or Season, the same relative file names under the old and the new folder, and for a Movie or Episode the same file name under a renamed folder. More than one candidate is no match.
- **Never overwrite**: a follow moves a file only when its target path is empty. Folder follows move file by file, so a partly followed folder still completes.
- **NAS outage**: no follow while the old item's library root is unreachable or empty, because an offline share makes old folders look deleted.
- **Never take a used NFO**: before moving, the plugin checks the library for a current item that still maps to the old mirror path (another version sharing `movie.nfo`, or the same file with a new extension). If there is one, the record is dropped and nothing moves.
- **Name and folder-contents matches need the old folder gone**, so a deleted show and a new show with the same generic episode names only match when the old show's folder no longer exists.
- **No journal**: each follow is a single rename inside the NFO root, which is atomic on one filesystem; a crash cannot leave two copies or none.
- **Setting**: Follow renames is On, Preview (list what would follow) or Off. The default is On, because following moves an NFO to its new home and deletes nothing.
- **Late matches** (the new item was read before the old one was removed, for example a move between libraries scanned in the other order) are not followed. Full sync reports them as possible renames by matching `<uniqueid>` values in orphaned NFOs to current items' provider ids; it never replaces an existing NFO.

## Consequences

- Renames and moves inside or between Movies, Shows and Mixed Movies and Shows libraries keep their NFO and edits, as long as the old item is removed before the new one is first read.
- Removal records live in memory only; a restart between the removal and the first read of the new item loses the match, which then shows up as a possible rename.
- Followed NFOs are not orphans, so folder renames no longer trip the 20% sweep cap.
- Test media in automated tests must have distinct file sizes, or size matching becomes ambiguous.
- A show deleted and a different show added within six hours, with identical relative episode file names, can still be matched by folder contents. This is rare enough to accept; the moved NFOs can be fixed by refreshing the show's metadata.
