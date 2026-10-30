using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.NfoMirror.Activity;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.XbmcMetadata.Savers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NfoMirror.Savers;

public sealed class NfoMirrorSaver : IMetadataFileSaver
{
    private readonly ILibraryManager _libraryManager;
    private readonly MirrorMovieNfoSaver _movie;
    private readonly MirrorSeriesNfoSaver _series;
    private readonly MirrorSeasonNfoSaver _season;
    private readonly MirrorEpisodeNfoSaver _episode;

    public NfoMirrorSaver(
        IFileSystem fileSystem,
        IServerConfigurationManager configurationManager,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILoggerFactory loggerFactory)
    {
        _libraryManager = libraryManager;
        _movie = new MirrorMovieNfoSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, loggerFactory.CreateLogger<MovieNfoSaver>());
        _series = new MirrorSeriesNfoSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, loggerFactory.CreateLogger<SeriesNfoSaver>());
        _season = new MirrorSeasonNfoSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, loggerFactory.CreateLogger<SeasonNfoSaver>());
        _episode = new MirrorEpisodeNfoSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, loggerFactory.CreateLogger<EpisodeNfoSaver>());
    }

    public string Name => Plugin.ProviderName;

    public bool IsEnabledFor(BaseItem item, ItemUpdateType updateType)
        => SaverFor(item) is { } saver
            && NfoRoot.TryGet(_libraryManager, out _) is not null
            && saver.IsEnabledFor(item, updateType);

    public string GetSavePath(BaseItem item)
        => RequireSaver(item).GetSavePath(item);

    public async Task SaveAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var path = GetSavePath(item);
        try
        {
            var outcome = await SaveWithOutcomeAsync(item, cancellationToken).ConfigureAwait(false);
            if (outcome != SaveOutcome.Unchanged)
            {
                ActivityLog.Add(ActivityLog.For(outcome), $"{path} (metadata change)");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Jellyfin logs and swallows saver errors; this only surfaces them on the console.
            ActivityLog.Add(ActivityKind.Failed, $"{path} (metadata change): {ex.Message}");
            throw;
        }
    }

    internal async Task<SaveOutcome> SaveWithOutcomeAsync(BaseItem item, CancellationToken cancellationToken)
    {
        var path = GetSavePath(item);
        var before = File.Exists(path) ? File.GetLastWriteTimeUtc(path) : (DateTime?)null;

        // BaseNfoSaver skips the write when the bytes are identical, so an unchanged mtime means unchanged content.
        await RequireSaver(item).SaveAsync(item, cancellationToken).ConfigureAwait(false);

        if (before is null)
        {
            return SaveOutcome.Created;
        }

        return File.GetLastWriteTimeUtc(path) == before ? SaveOutcome.Unchanged : SaveOutcome.Updated;
    }

    private BaseNfoSaver RequireSaver(BaseItem item)
        => SaverFor(item) ?? throw new ArgumentException($"Unsupported item type {item.GetType().Name}", nameof(item));

    private BaseNfoSaver? SaverFor(BaseItem item) => item switch
    {
        Movie => _movie,
        Series => _series,
        Season => _season,
        Episode => _episode,
        _ => null
    };

    internal static string MirrorPath(ILibraryManager libraryManager, BaseItem item)
    {
        var root = NfoRoot.TryGet(libraryManager, out var error) ?? throw new InvalidOperationException(error);
        return MirrorPaths.GetMirrorPath(root, new MediaBrowser.Controller.Providers.ItemInfo(item))
            ?? throw new InvalidOperationException($"No mirror path for '{item.Path}'.");
    }
}

public enum SaveOutcome
{
    Created,
    Updated,
    Unchanged
}
