using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.NfoMirror.Activity;
using Jellyfin.Plugin.NfoMirror.Configuration;
using Jellyfin.Plugin.NfoMirror.Savers;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NfoMirror.Sync;

public enum SyncKind
{
    Full,
    Changed
}

public sealed class SyncRunner
{
    private const int MaxLoggedPerReason = 50;
    private const int MaxActivityPerList = 1000;
    private const int MaxStoredPreview = 100;

    private static readonly SemaphoreSlim _lock = new(1, 1);

    private readonly ILibraryManager _libraryManager;
    private readonly NfoMirrorSaver _saver;
    private readonly ILogger _logger;

    public SyncRunner(ILibraryManager libraryManager, NfoMirrorSaver saver, ILogger logger)
    {
        _libraryManager = libraryManager;
        _saver = saver;
        _logger = logger;
    }

    public static bool IsRunning => _lock.CurrentCount == 0;

    public async Task RunAsync(SyncKind kind, IProgress<double> progress, CancellationToken cancellationToken)
    {
        if (IsRunning)
        {
            Info($"Waiting for the other NFO Mirror sync to finish before the {Label(kind)} starts.");
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await RunLockedAsync(kind, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task RunLockedAsync(SyncKind kind, IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration ?? new PluginConfiguration();
        var root = NfoRoot.TryGet(_libraryManager, out var error);
        if (root is null)
        {
            _logger.LogError("NFO Mirror refused to run: {Error}", error);
            ActivityLog.Add(ActivityKind.Failed, $"Refused: {error}");
            SaveResult(kind, $"Refused: {error}");
            return;
        }

        var startedUtc = DateTime.UtcNow;
        var libraryRoots = _libraryManager.GetVirtualFolders()
            .Where(f => f.CollectionType is CollectionTypeOptions.movies or CollectionTypeOptions.tvshows)
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
        var since = kind == SyncKind.Changed ? config.LastChangedSyncStartUtc : null;
        if (since is not null)
        {
            query.MinDateLastSaved = since;
        }

        var items = _libraryManager.GetItemList(query)
            .Where(i => !string.IsNullOrEmpty(i.Path))
            .OrderBy(i => IsSecondaryVersion(i) ? 1 : 0)
            .Select(i => (Item: i, Mirror: MirrorPaths.GetMirrorPath(root, new ItemInfo(i))))
            .ToList();

        var scope = since is null ? $"{items.Count} items" : $"{items.Count} items saved since {since:yyyy-MM-dd HH:mm:ss} UTC";
        _logger.LogInformation("NFO Mirror {Kind} started: root {Root}, {Scope}, Movies/Shows library roots: {Libraries}", Label(kind), root, scope, string.Join(", ", libraryRoots));
        Info($"{Capitalize(Label(kind))} started: {scope}, NFO root {root}.");

        // Expected covers every library, so NFOs the saver wrote elsewhere are never swept.
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, mirror) in items)
        {
            if (mirror is not null)
            {
                expected.Add(mirror);
            }
        }

        var restored = OrphanSweep.Restore(root, expected);
        foreach (var path in restored)
        {
            _logger.LogInformation("NFO Mirror restored {Path} from {Orphans}", path, MirrorPaths.OrphansFolder);
            ActivityLog.Add(ActivityKind.Restored, path);
        }

        var claimed = new HashSet<string>(StringComparer.Ordinal);
        int created = 0, updated = 0, unchanged = 0, shared = 0, outsideLibraries = 0, notSaveable = 0, failed = 0;
        for (var i = 0; i < items.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (item, mirror) = items[i];
            var firstForPath = mirror is null || claimed.Add(mirror);
            var path = MirrorPaths.Normalize(item.Path);

            if (!libraryRoots.Any(r => MirrorPaths.IsUnder(r, path)))
            {
                Skip(++outsideLibraries, item, "not in a Movies/Shows library");
            }
            else if (!_saver.IsEnabledFor(item, ItemUpdateType.MetadataEdit))
            {
                Skip(++notSaveable, item, "not handled by Jellyfin's NFO saver (extra or non-file item)");
            }
            else if (!firstForPath)
            {
                // Versions in one movie folder share movie.nfo; the primary version, sorted first, owns it.
                shared++;
                ActivityLog.Add(ActivityKind.Skipped, $"{item.Path}: shares {mirror} with its primary version");
            }
            else
            {
                try
                {
                    var outcome = await _saver.SaveWithOutcomeAsync(item, cancellationToken).ConfigureAwait(false);
                    if (outcome == SaveOutcome.Unchanged)
                    {
                        unchanged++;
                    }
                    else
                    {
                        if (outcome == SaveOutcome.Created)
                        {
                            created++;
                        }
                        else
                        {
                            updated++;
                        }

                        var target = _saver.GetSavePath(item);
                        _logger.LogDebug("NFO Mirror {Outcome} {Path}", outcome, target);
                        ActivityLog.Add(outcome == SaveOutcome.Created ? ActivityKind.Created : ActivityKind.Updated, target);

                        // Stops HasChanged from re-importing our own write.
                        item.DateLastSaved = DateTime.UtcNow;
                        await item.UpdateToRepositoryAsync(ItemUpdateType.None, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    failed++;
                    _logger.LogError(ex, "NFO Mirror could not write NFO for {Path}", item.Path);
                    ActivityLog.Add(ActivityKind.Failed, $"{item.Path}: {ex.Message}");
                }
            }

            progress.Report(90.0 * (i + 1) / Math.Max(items.Count, 1));
        }

        var summary = $"{items.Count} items: {created} created, {updated} updated, {unchanged} unchanged, {restored.Count} restored from {MirrorPaths.OrphansFolder}, "
            + $"{shared} sharing another version's NFO, {outsideLibraries} skipped (not in a Movies/Shows library), "
            + $"{notSaveable} skipped (extra or non-file item), {failed} failed";

        if (kind == SyncKind.Full)
        {
            summary += "; " + Sweep(root, expected, libraryRoots, config);
        }
        else if (failed == 0)
        {
            config.LastChangedSyncStartUtc = startedUtc;
        }

        progress.Report(100);
        Info($"{Capitalize(Label(kind))} finished: {summary}.");
        SaveResult(kind, summary + ".");
    }

    private string Sweep(string root, HashSet<string> expected, List<string> libraryRoots, PluginConfiguration config)
    {
        if (config.SweepMode == SweepMode.Off)
        {
            return "orphan sweep off";
        }

        var version = Plugin.Instance?.Version.ToString() ?? string.Empty;
        var baseline = config.BaselineVersion != version;
        var move = config.SweepMode == SweepMode.Move && !baseline;

        var sweep = OrphanSweep.Run(root, expected, libraryRoots, move);
        if (sweep.SkippedReason is not null)
        {
            _logger.LogWarning("NFO Mirror skipped orphan sweep: {Reason}", sweep.SkippedReason);
            ActivityLog.Add(ActivityKind.Info, $"Orphan sweep skipped: {sweep.SkippedReason}");
        }

        foreach (var moved in sweep.MovedFiles)
        {
            _logger.LogInformation("NFO Mirror moved orphan {From} to {To}", moved.From, moved.To);
            ActivityLog.Add(ActivityKind.Moved, $"{moved.From} -> {moved.To}");
        }

        if (sweep.MovedFiles.Count == 0)
        {
            var listed = 0;
            foreach (var orphan in sweep.Orphans)
            {
                if (listed < MaxLoggedPerReason)
                {
                    _logger.LogInformation("NFO Mirror orphan (not moved): {Path}", orphan);
                }

                if (listed++ < MaxActivityPerList)
                {
                    ActivityLog.Add(ActivityKind.WouldMove, orphan);
                }
            }

            config.LastPreviewCount = sweep.Orphans.Count;
            config.LastPreview = sweep.Orphans.Take(MaxStoredPreview).ToArray();
        }
        else
        {
            config.LastPreviewCount = 0;
            config.LastPreview = [];
        }

        if (baseline && sweep.SkippedReason is null)
        {
            config.BaselineVersion = version;
            return $"baseline preview after install/upgrade: {sweep.Orphans.Count} of {sweep.Total} NFOs would move to {MirrorPaths.OrphansFolder}";
        }

        if (sweep.SkippedReason is not null)
        {
            return $"orphan sweep skipped ({sweep.SkippedReason})";
        }

        return move
            ? $"{sweep.Moved} of {sweep.Total} NFOs moved to {MirrorPaths.OrphansFolder}"
            : $"preview: {sweep.Orphans.Count} of {sweep.Total} NFOs would move to {MirrorPaths.OrphansFolder}";
    }

    private static bool IsSecondaryVersion(BaseItem item)
        => !item.OwnerId.Equals(Guid.Empty) || item is Video { PrimaryVersionId: not null };

    private static string Label(SyncKind kind) => kind == SyncKind.Full ? "full sync" : "changed sync";

    private static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    private void Info(string message)
    {
        _logger.LogInformation("NFO Mirror: {Message}", message);
        ActivityLog.Add(ActivityKind.Info, message);
    }

    private void Skip(int count, BaseItem item, string reason)
    {
        ActivityLog.Add(ActivityKind.Skipped, $"{item.Path}: {reason}");
        if (count <= MaxLoggedPerReason)
        {
            _logger.LogInformation("NFO Mirror skipped {Path}: {Reason}", item.Path, reason);
        }
        else if (count == MaxLoggedPerReason + 1)
        {
            _logger.LogInformation("NFO Mirror: more items skipped ({Reason}), see the plugin page console", reason);
        }
    }

    private void SaveResult(SyncKind kind, string summary)
    {
        var line = $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC: {summary}";
        if (Plugin.Instance is not { } plugin)
        {
            return;
        }

        if (kind == SyncKind.Full)
        {
            plugin.Configuration.LastSyncResult = line;
        }
        else
        {
            plugin.Configuration.LastChangedSyncResult = line;
        }

        plugin.SaveConfiguration();
    }
}
