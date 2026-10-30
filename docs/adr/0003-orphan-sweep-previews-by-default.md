# 3. Orphan sweep previews by default

Date: 2026-10-30

## Status

Accepted

## Context

Version 1.0.0.0 moved 240 of about 30,000 NFOs to `.orphans` on a real server. They belonged to alternate versions that Jellyfin hides from its default item query. The existing safeguards (skip when a library root is unreachable or empty, skip when more than 20% would move) did not trigger, because the wrong share was under 1%. Mirror and backup tools stop small logic bugs like this with a preview, a first-run baseline and easy restore, not with percentage caps.

## Decision

- The orphan sweep has a mode: Off, Preview or Move. The default is Preview.
- The first sweep after an install or upgrade is a baseline sweep and only previews, whatever the mode.
- The preview list goes to the Jellyfin log and the config page. Nothing except `.nfo` files is written to the NFO root.
- Each sync auto-restores an NFO from `.orphans` when a current item maps to its path again and that path is empty.
- The plugin never deletes NFOs. There is no purge of `.orphans`.
- A changed sync joins the full sync. Both tasks share a lock and ship without default triggers.

## Consequences

- Orphans are not moved until the user has seen a preview and switched the mode to Move.
- A plugin bug that misclassifies NFOs costs a preview, not moved files; files moved by such a bug come back on the next sync once fixed.
- `.orphans` can grow without bound; cleaning it is a manual job.
