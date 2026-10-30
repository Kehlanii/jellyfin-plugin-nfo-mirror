using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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

    protected NfoMirrorProvider(
        ILogger logger,
        IConfigurationManager config,
        IProviderManager providerManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILibraryManager libraryManager)
    {
        Logger = logger;
        Config = config;
        ProviderManager = providerManager;
        UserManager = userManager;
        UserDataManager = userDataManager;
        _libraryManager = libraryManager;
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
        var path = GetMirrorFile(info);
        if (path is null || !File.Exists(path))
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
        var path = GetMirrorFile(new ItemInfo(item));
        if (path is null || !File.Exists(path))
        {
            return false;
        }

        // 1 min tolerance, as in Jellyfin's BaseNfoProvider.
        return File.GetLastWriteTimeUtc(path) - item.DateLastSaved > TimeSpan.FromMinutes(1);
    }

    protected abstract void Fetch(MetadataResult<T> result, string path, IDirectoryService directoryService, CancellationToken cancellationToken);

    private string? GetMirrorFile(ItemInfo info)
    {
        var root = NfoRoot.TryGet(_libraryManager, out _);
        return root is null ? null : MirrorPaths.GetMirrorPath(root, info);
    }
}

public class NfoMirrorMovieProvider(
    ILogger<NfoMirrorMovieProvider> logger,
    IConfigurationManager config,
    IProviderManager providerManager,
    IUserManager userManager,
    IUserDataManager userDataManager,
    ILibraryManager libraryManager)
    : NfoMirrorProvider<Movie>(logger, config, providerManager, userManager, userDataManager, libraryManager)
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
    ILibraryManager libraryManager)
    : NfoMirrorProvider<Series>(logger, config, providerManager, userManager, userDataManager, libraryManager)
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
    ILibraryManager libraryManager)
    : NfoMirrorProvider<Season>(logger, config, providerManager, userManager, userDataManager, libraryManager)
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
    ILibraryManager libraryManager)
    : NfoMirrorProvider<Episode>(logger, config, providerManager, userManager, userDataManager, libraryManager)
{
    protected override void Fetch(MetadataResult<Episode> result, string path, IDirectoryService directoryService, CancellationToken cancellationToken)
        => new EpisodeNfoParser(Logger, Config, ProviderManager, UserManager, UserDataManager, directoryService).Fetch(result, path, cancellationToken);
}
