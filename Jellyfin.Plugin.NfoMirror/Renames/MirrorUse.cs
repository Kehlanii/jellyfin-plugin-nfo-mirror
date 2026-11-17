using System;
using System.IO;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;

namespace Jellyfin.Plugin.NfoMirror.Renames;

internal static class MirrorUse
{
    // True when a current library item still maps to the record's mirror file, or when unsure.
    public static bool InUse(ILibraryManager libraryManager, string root, RemovalRecord record)
    {
        try
        {
            return record.Kind is MediaKind.Series or MediaKind.Season
                ? FolderInUse(libraryManager, record)
                : FileInUse(libraryManager, root, record);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return true;
        }
    }

    private static bool FolderInUse(ILibraryManager libraryManager, RemovalRecord record)
    {
        if (!Directory.Exists(record.MediaPath))
        {
            return false;
        }

        var kind = record.Kind == MediaKind.Series ? BaseItemKind.Series : BaseItemKind.Season;
        return libraryManager.GetItemList(new InternalItemsQuery { Path = record.MediaPath, IncludeItemTypes = [kind], IncludeOwnedItems = true }).Count > 0;
    }

    private static bool FileInUse(ILibraryManager libraryManager, string root, RemovalRecord record)
    {
        var dir = Path.GetDirectoryName(record.MediaPath);
        if (dir is null || !Directory.Exists(dir))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(record.MediaPath);
        var perFile = string.Equals(Path.GetFileName(record.MirrorPath), name + ".nfo", StringComparison.Ordinal);
        var files = Directory.EnumerateFiles(dir)
            .Where(f => !string.Equals(Path.GetExtension(f), ".nfo", StringComparison.OrdinalIgnoreCase))
            .Where(f => !perFile || string.Equals(Path.GetFileNameWithoutExtension(f), name, StringComparison.Ordinal));

        var mirror = MirrorPaths.Normalize(record.MirrorPath);
        foreach (var file in files)
        {
            var items = libraryManager.GetItemList(new InternalItemsQuery
            {
                Path = file,
                IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Episode],
                IncludeOwnedItems = true
            });
            if (items.Any(i => MirrorPaths.GetMirrorPath(root, new ItemInfo(i)) is { } m && string.Equals(MirrorPaths.Normalize(m), mirror, StringComparison.Ordinal)))
            {
                return true;
            }
        }

        return false;
    }
}
