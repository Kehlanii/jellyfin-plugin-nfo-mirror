using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Data.Enums;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.NfoMirror.Sync;

internal sealed record InventoryItem(BaseItem Item, string? Mirror);

internal sealed record MirrorInventory(IReadOnlyList<InventoryItem> Items, IReadOnlyList<string> LibraryRoots)
{
    public static MirrorInventory Build(ILibraryManager libraryManager, string root, DateTime? savedSince)
    {
        var libraryRoots = libraryManager.GetVirtualFolders()
            // Mixed Movies and Shows libraries report "mixed", or null when created without a type.
            .Where(f => f.CollectionType is CollectionTypeOptions.movies or CollectionTypeOptions.tvshows or CollectionTypeOptions.mixed or null)
            .SelectMany(f => f.Locations ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(MirrorPaths.Normalize)
            .ToList();

        // Default queries hide alternate versions and other owned items, but the saver writes NFOs for them too.
        var query = new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode],
            IsVirtualItem = false,
            Recursive = true,
            IncludeOwnedItems = true
        };
        if (savedSince is not null)
        {
            query.MinDateLastSaved = savedSince;
        }

        var items = libraryManager.GetItemList(query)
            .Where(i => !string.IsNullOrEmpty(i.Path))
            .OrderBy(i => IsSecondaryVersion(i) ? 1 : 0)
            .Select(i => new InventoryItem(i, MirrorPaths.GetMirrorPath(root, new ItemInfo(i))))
            .ToList();

        return new MirrorInventory(items, libraryRoots);
    }

    public HashSet<string> ExpectedMirrors()
        => Items.Where(i => i.Mirror is not null).Select(i => i.Mirror!).ToHashSet(StringComparer.Ordinal);

    private static bool IsSecondaryVersion(BaseItem item)
        => !item.OwnerId.Equals(Guid.Empty) || item is Video { PrimaryVersionId: not null };
}
