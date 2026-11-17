using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NfoMirror.Renames;

internal sealed class RemovalListener : IHostedService
{
    private readonly ILibraryManager _libraryManager;
    private readonly RenameTracker _tracker;
    private readonly ILogger<RemovalListener> _logger;

    public RemovalListener(ILibraryManager libraryManager, RenameTracker tracker, ILogger<RemovalListener> logger)
    {
        _libraryManager = libraryManager;
        _tracker = tracker;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemRemoved += OnItemRemoved;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _libraryManager.ItemRemoved -= OnItemRemoved;
        return Task.CompletedTask;
    }

    // Runs on Jellyfin's scan thread, so it must never throw.
    private void OnItemRemoved(object? sender, ItemChangeEventArgs e)
    {
        try
        {
            var item = e.Item;
            MediaKind kind;
            switch (item)
            {
                case Movie:
                    kind = MediaKind.Movie;
                    break;
                case Series:
                    kind = MediaKind.Series;
                    break;
                case Season:
                    kind = MediaKind.Season;
                    break;
                case Episode:
                    kind = MediaKind.Episode;
                    break;
                default:
                    return;
            }

            if (string.IsNullOrEmpty(item.Path))
            {
                return;
            }

            var root = NfoRoot.TryGet(_libraryManager, out _);
            if (root is null)
            {
                return;
            }

            var mirror = MirrorPaths.GetMirrorPath(root, new ItemInfo(item));
            if (mirror is null)
            {
                return;
            }

            var exists = kind is MediaKind.Movie or MediaKind.Episode
                ? File.Exists(mirror) || File.Exists(MirrorPaths.GetOrphanPath(root, mirror))
                : Directory.Exists(Path.GetDirectoryName(mirror)) || Directory.Exists(MirrorPaths.GetOrphanPath(root, Path.GetDirectoryName(mirror)!));
            if (!exists)
            {
                return;
            }

            var ids = new Dictionary<string, string>(item.ProviderIds, StringComparer.OrdinalIgnoreCase);
            _tracker.Record(new RemovalRecord(kind, item.Path, mirror, item.Size, ids, DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "NFO Mirror could not record a removed item");
        }
    }
}
