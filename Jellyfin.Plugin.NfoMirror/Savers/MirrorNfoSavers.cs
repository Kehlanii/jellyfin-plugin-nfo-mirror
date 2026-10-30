using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.XbmcMetadata.Savers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NfoMirror.Savers;

// Internal so ApplicationHost (GetExportedTypes) never registers them as extra "Nfo" savers.
internal sealed class MirrorMovieNfoSaver(
    IFileSystem fileSystem,
    IServerConfigurationManager configurationManager,
    ILibraryManager libraryManager,
    IUserManager userManager,
    IUserDataManager userDataManager,
    ILogger<MovieNfoSaver> logger)
    : MovieNfoSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, logger)
{
    protected override string GetLocalSavePath(BaseItem item) => NfoMirrorSaver.MirrorPath(LibraryManager, item);
}

internal sealed class MirrorSeriesNfoSaver(
    IFileSystem fileSystem,
    IServerConfigurationManager configurationManager,
    ILibraryManager libraryManager,
    IUserManager userManager,
    IUserDataManager userDataManager,
    ILogger<SeriesNfoSaver> logger)
    : SeriesNfoSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, logger)
{
    protected override string GetLocalSavePath(BaseItem item) => NfoMirrorSaver.MirrorPath(LibraryManager, item);
}

internal sealed class MirrorSeasonNfoSaver(
    IFileSystem fileSystem,
    IServerConfigurationManager configurationManager,
    ILibraryManager libraryManager,
    IUserManager userManager,
    IUserDataManager userDataManager,
    ILogger<SeasonNfoSaver> logger)
    : SeasonNfoSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, logger)
{
    protected override string GetLocalSavePath(BaseItem item) => NfoMirrorSaver.MirrorPath(LibraryManager, item);
}

internal sealed class MirrorEpisodeNfoSaver(
    IFileSystem fileSystem,
    IServerConfigurationManager configurationManager,
    ILibraryManager libraryManager,
    IUserManager userManager,
    IUserDataManager userDataManager,
    ILogger<EpisodeNfoSaver> logger)
    : EpisodeNfoSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, logger)
{
    protected override string GetLocalSavePath(BaseItem item) => NfoMirrorSaver.MirrorPath(LibraryManager, item);
}
