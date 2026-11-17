using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.NfoMirror.Activity;
using Jellyfin.Plugin.NfoMirror.Configuration;
using Jellyfin.Plugin.NfoMirror.Renames;
using Jellyfin.Plugin.NfoMirror.Sync;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.XbmcMetadata.Parsers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NfoMirror.Providers;

public abstract class NfoMirrorProvider<T> : ILocalMetadataProvider<T>, IHasItemChangeMonitor
    where T : BaseItem, new()
{
    private readonly ILibraryManager _libraryManager;
    private readonly RenameTracker _renameTracker;

    protected NfoMirrorProvider(
        ILogger logger,
        IConfigurationManager config,
        IProviderManager providerManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILibraryManager libraryManager,
        RenameTracker renameTracker)
    {
        Logger = logger;
        Config = config;
        ProviderManager = providerManager;
        UserManager = userManager;
        UserDataManager = userDataManager;
        _libraryManager = libraryManager;
        _renameTracker = renameTracker;
    }

    public string Name => Plugin.ProviderName;

    protected ILogger Logger { get; }

    protected IConfigurationManager Config { get; }

    protected IProviderManager ProviderManager { get; }

    protected IUserManager UserManager { get; }

    protected IUserDataManager UserDataManager { get; }

    public Task<MetadataResult<T>> GetMetadata(ItemInfo info, IDirectoryService directoryService, CancellationToken cancellationToken)
    {
        var result = new MetadataResult<T>();
        var (root, path) = GetMirrorFile(info);
        if (root is null || path is null)
        {
            return Task.FromResult(result);
        }

        // Restore here, before Jellyfin fetches fresh metadata and the saver takes the path.
        try
        {
            if (!File.Exists(path) && OrphanSweep.Restore(root, [path]).Count > 0)
            {
                Logger.LogInformation("NFO Mirror restored {Path} from {Orphans}", path, MirrorPaths.OrphansFolder);
                ActivityLog.Add(ActivityKind.Restored, $"{path} (item is back)");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Logger.LogWarning(ex, "NFO Mirror could not restore {Path}", path);
            ActivityLog.Add(ActivityKind.Failed, $"{path}: restore failed: {ex.Message}");
        }

        if (!File.Exists(path))
        {
            TryFollow(root, info, path);
        }

        if (!File.Exists(path))
        {
            return Task.FromResult(result);
        }

        try
        {
            result.Item = new T { IndexNumber = info.IndexNumber };
            Fetch(result, path, directoryService, cancellationToken);
            result.HasMetadata = true;
        }
        catch (IOException ex)
        {
            Logger.LogWarning(ex, "Could not read {Path}", path);
            result.HasMetadata = false;
        }

        return Task.FromResult(result);
    }

    public bool HasChanged(BaseItem item, IDirectoryService directoryService)
    {
        var (_, path) = GetMirrorFile(new ItemInfo(item));
        if (path is null || !File.Exists(path))
        {
            return false;
        }

        // 1 min tolerance, as in Jellyfin's BaseNfoProvider.
        return File.GetLastWriteTimeUtc(path) - item.DateLastSaved > TimeSpan.FromMinutes(1);
    }

    protected abstract void Fetch(MetadataResult<T> result, string path, IDirectoryService directoryService, CancellationToken cancellationToken);

    private void TryFollow(string root, ItemInfo info, string path)
    {
        var mode = Plugin.Instance?.Configuration.FollowRenames ?? FollowMode.On;
        var kind = typeof(T) == typeof(Movie) ? MediaKind.Movie
            : typeof(T) == typeof(Series) ? MediaKind.Series
            : typeof(T) == typeof(Season) ? MediaKind.Season
            : MediaKind.Episode;

        long? size = null;
        if (kind is MediaKind.Movie or MediaKind.Episode)
        {
            try
            {
                if (File.Exists(info.Path))
                {
                    size = new FileInfo(info.Path).Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                size = null;
            }
        }

        try
        {
            var item = new NewItem(kind, info.Path, path, size, new Dictionary<string, string>());
            var followed = _renameTracker.TryFollow(root, item, mode, r => MirrorUse.InUse(_libraryManager, root, r));
            if (followed is null)
            {
                return;
            }

            var conflicts = followed.Conflicts > 0 ? $", {followed.Conflicts} kept at target" : string.Empty;
            var text = $"{followed.From} -> {followed.To} ({followed.Reason}, {followed.Files} file(s){conflicts})";
            if (followed.Moved)
            {
                Logger.LogInformation("NFO Mirror followed {From} to {To} ({Reason})", followed.From, followed.To, followed.Reason);
                ActivityLog.Add(ActivityKind.Followed, text);
            }
            else
            {
                ActivityLog.Add(ActivityKind.WouldFollow, text);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Logger.LogWarning(ex, "NFO Mirror could not follow a rename to {Path}", path);
            ActivityLog.Add(ActivityKind.Failed, $"{path}: rename-follow failed: {ex.Message}");
        }
    }

    private (string? Root, string? Path) GetMirrorFile(ItemInfo info)
    {
        var root = NfoRoot.TryGet(_libraryManager, out _);
        return (root, root is null ? null : MirrorPaths.GetMirrorPath(root, info));
    }
}

public class NfoMirrorMovieProvider(
    ILogger<NfoMirrorMovieProvider> logger,
    IConfigurationManager config,
    IProviderManager providerManager,
    IUserManager userManager,
    IUserDataManager userDataManager,
    ILibraryManager libraryManager,
    RenameTracker renameTracker)
    : NfoMirrorProvider<Movie>(logger, config, providerManager, userManager, userDataManager, libraryManager, renameTracker)
{
    protected override void Fetch(MetadataResult<Movie> result, string path, IDirectoryService directoryService, CancellationToken cancellationToken)
    {
        // MovieNfoParser works on Video.
        var video = new MetadataResult<Video> { Item = result.Item };
        new MovieNfoParser(Logger, Config, ProviderManager, UserManager, UserDataManager, directoryService).Fetch(video, path, cancellationToken);
        result.Item = (Movie)video.Item;
        result.People = video.People;
        result.Images = video.Images;
        result.RemoteImages = video.RemoteImages;
    }
}

public class NfoMirrorSeriesProvider(
    ILogger<NfoMirrorSeriesProvider> logger,
    IConfigurationManager config,
    IProviderManager providerManager,
    IUserManager userManager,
    IUserDataManager userDataManager,
    ILibraryManager libraryManager,
    RenameTracker renameTracker)
    : NfoMirrorProvider<Series>(logger, config, providerManager, userManager, userDataManager, libraryManager, renameTracker)
{
    protected override void Fetch(MetadataResult<Series> result, string path, IDirectoryService directoryService, CancellationToken cancellationToken)
        => new SeriesNfoParser(Logger, Config, ProviderManager, UserManager, UserDataManager, directoryService).Fetch(result, path, cancellationToken);
}

public class NfoMirrorSeasonProvider(
    ILogger<NfoMirrorSeasonProvider> logger,
    IConfigurationManager config,
    IProviderManager providerManager,
    IUserManager userManager,
    IUserDataManager userDataManager,
    ILibraryManager libraryManager,
    RenameTracker renameTracker)
    : NfoMirrorProvider<Season>(logger, config, providerManager, userManager, userDataManager, libraryManager, renameTracker)
{
    protected override void Fetch(MetadataResult<Season> result, string path, IDirectoryService directoryService, CancellationToken cancellationToken)
        => new SeasonNfoParser(Logger, Config, ProviderManager, UserManager, UserDataManager, directoryService).Fetch(result, path, cancellationToken);
}

public class NfoMirrorEpisodeProvider(
    ILogger<NfoMirrorEpisodeProvider> logger,
    IConfigurationManager config,
    IProviderManager providerManager,
    IUserManager userManager,
    IUserDataManager userDataManager,
    ILibraryManager libraryManager,
    RenameTracker renameTracker)
    : NfoMirrorProvider<Episode>(logger, config, providerManager, userManager, userDataManager, libraryManager, renameTracker)
{
    protected override void Fetch(MetadataResult<Episode> result, string path, IDirectoryService directoryService, CancellationToken cancellationToken)
        => new EpisodeNfoParser(Logger, Config, ProviderManager, UserManager, UserDataManager, directoryService).Fetch(result, path, cancellationToken);
}
