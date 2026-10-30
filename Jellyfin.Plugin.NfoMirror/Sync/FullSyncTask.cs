using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.NfoMirror.Savers;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NfoMirror.Sync;

public class FullSyncTask : IScheduledTask
{
    public const string TaskKey = "NfoMirrorFullSync";

    private readonly ILibraryManager _libraryManager;
    private readonly ILogger<FullSyncTask> _logger;
    private readonly NfoMirrorSaver _saver;

    public FullSyncTask(
        IFileSystem fileSystem,
        IServerConfigurationManager configurationManager,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILoggerFactory loggerFactory)
    {
        _libraryManager = libraryManager;
        _logger = loggerFactory.CreateLogger<FullSyncTask>();
        _saver = new NfoMirrorSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, loggerFactory);
    }

    public string Name => "NFO Mirror: full sync";

    public string Key => TaskKey;

    public string Description => "Writes missing or outdated .nfo files into the NFO root and moves orphaned ones to .orphans.";

    public string Category => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
        => [];

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var root = NfoRoot.TryGet(_libraryManager, out var error);
        if (root is null)
        {
            _logger.LogError("NFO Mirror refused to run: {Error}", error);
            SaveResult($"Refused: {error}");
            return;
        }

        var libraryRoots = _libraryManager.GetVirtualFolders()
            .Where(f => f.CollectionType is CollectionTypeOptions.movies or CollectionTypeOptions.tvshows)
            .SelectMany(f => f.Locations ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(MirrorPaths.Normalize)
            .ToList();

        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            IncludeItemTypes = [BaseItemKind.Movie, BaseItemKind.Series, BaseItemKind.Season, BaseItemKind.Episode],
            IsVirtualItem = false,
            Recursive = true
        })
            .Where(i => !string.IsNullOrEmpty(i.Path))
            .ToList();

        var expected = new HashSet<string>(StringComparer.Ordinal);
        int created = 0, updated = 0, unchanged = 0, skipped = 0, failed = 0;

        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var item = items[i];

            // Expected covers every library, so NFOs the saver wrote elsewhere are never swept.
            if (MirrorPaths.GetMirrorPath(root, new ItemInfo(item)) is { } mirror)
            {
                expected.Add(mirror);
            }

            var path = MirrorPaths.Normalize(item.Path);
            if (!libraryRoots.Any(r => MirrorPaths.IsUnder(r, path)) || !_saver.IsEnabledFor(item, ItemUpdateType.MetadataEdit))
            {
                skipped++;
            }
            else
            {
                try
                {
                    var nfo = _saver.GetSavePath(item);
                    var before = File.Exists(nfo) ? File.GetLastWriteTimeUtc(nfo) : (DateTime?)null;
                    await _saver.SaveAsync(item, cancellationToken).ConfigureAwait(false);

                    if (before is null || File.GetLastWriteTimeUtc(nfo) != before)
                    {
                        if (before is null)
                        {
                            created++;
                        }
                        else
                        {
                            updated++;
                        }

                        // Stops HasChanged from re-importing our own write.
                        item.DateLastSaved = DateTime.UtcNow;
                        await item.UpdateToRepositoryAsync(ItemUpdateType.None, cancellationToken).ConfigureAwait(false);
                    }
                    else
                    {
                        unchanged++;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    failed++;
                    _logger.LogError(ex, "Could not write NFO for {Path}", item.Path);
                }
            }

            progress.Report(90.0 * (i + 1) / Math.Max(items.Count, 1));
        }

        var sweep = OrphanSweep.Run(root, expected, libraryRoots);
        if (sweep.SkippedReason is not null)
        {
            _logger.LogWarning("NFO Mirror skipped orphan sweep: {Reason}", sweep.SkippedReason);
        }

        progress.Report(100);
        var orphanText = sweep.SkippedReason is null ? $"{sweep.Moved} orphans moved" : $"orphan sweep skipped ({sweep.SkippedReason})";
        SaveResult($"{created} created, {updated} updated, {unchanged} unchanged, {skipped} skipped, {failed} failed; {orphanText}.");
    }

    private void SaveResult(string summary)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC: {summary}";
        _logger.LogInformation("NFO Mirror full sync: {Summary}", summary);
        if (Plugin.Instance is { } plugin)
        {
            plugin.Configuration.LastSyncResult = line;
            plugin.SaveConfiguration();
        }
    }
}
