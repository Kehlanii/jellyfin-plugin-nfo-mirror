using System;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.XbmcMetadata.Savers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.NfoMirror.Tests;

public class SaverDriftTests
{
    [Theory]
    [InlineData("flat movie")]
    [InlineData("movie in own folder")]
    [InlineData("dvd folder")]
    [InlineData("bluray own folder")]
    [InlineData("bluray mixed folder")]
    [InlineData("series")]
    [InlineData("season")]
    [InlineData("episode")]
    [InlineData("dvd placeholder")]
    [InlineData("bluray placeholder mixed folder")]
    public void GetSourceNfoPathMatchesJellyfinSaver(string caseName)
    {
        var item = CreateItem(caseName);
        var fromMirror = MirrorPaths.GetSourceNfoPath(new ItemInfo(item));

        Assert.NotNull(fromMirror);
        Assert.Equal(SaverPath(item), fromMirror);
    }

    private static BaseItem CreateItem(string caseName) => caseName switch
    {
        "flat movie" => new Movie { Path = "/movies/Shrek (2001).mkv", IsInMixedFolder = true, VideoType = VideoType.VideoFile },
        "movie in own folder" => new Movie { Path = "/movies/Alien (1979)/Alien (1979).mkv", IsInMixedFolder = false, VideoType = VideoType.VideoFile },
        "dvd folder" => new Movie { Path = "/movies/Heat (1995)", IsInMixedFolder = false, VideoType = VideoType.Dvd },
        "bluray own folder" => new Movie { Path = "/movies/Dune (2021)", IsInMixedFolder = false, VideoType = VideoType.BluRay },
        "bluray mixed folder" => new Movie { Path = "/movies/Dune (2021)", IsInMixedFolder = true, VideoType = VideoType.BluRay },
        "dvd placeholder" => new Movie { Path = "/movies/Heat (1995).disc", IsInMixedFolder = false, VideoType = VideoType.Dvd, IsPlaceHolder = true },
        "bluray placeholder mixed folder" => new Movie { Path = "/movies/Dune (2021).disc", IsInMixedFolder = true, VideoType = VideoType.BluRay, IsPlaceHolder = true },
        "series" => new Series { Path = "/shows/Show" },
        "season" => new Season { Path = "/shows/Show/Season 01" },
        "episode" => new Episode { Path = "/shows/Show/Season 01/Show S01E01.mkv" },
        _ => throw new ArgumentOutOfRangeException(nameof(caseName), caseName, "Unknown saver drift case")
    };

    private static string SaverPath(BaseItem item) => item switch
    {
        Movie => new MovieSaverProbe().Path(item),
        Series => new SeriesSaverProbe().Path(item),
        Season => new SeasonSaverProbe().Path(item),
        Episode => new EpisodeSaverProbe().Path(item),
        _ => throw new ArgumentOutOfRangeException(nameof(item), item.GetType(), "No NFO saver for this item type")
    };

    private sealed class MovieSaverProbe : MovieNfoSaver
    {
        public MovieSaverProbe()
            : base(
                Mock.Of<IFileSystem>(),
                Mock.Of<IServerConfigurationManager>(),
                Mock.Of<ILibraryManager>(),
                Mock.Of<IUserManager>(),
                Mock.Of<IUserDataManager>(),
                Mock.Of<ILogger<MovieNfoSaver>>())
        {
        }

        public string Path(BaseItem item) => GetLocalSavePath(item);
    }

    private sealed class SeriesSaverProbe : SeriesNfoSaver
    {
        public SeriesSaverProbe()
            : base(
                Mock.Of<IFileSystem>(),
                Mock.Of<IServerConfigurationManager>(),
                Mock.Of<ILibraryManager>(),
                Mock.Of<IUserManager>(),
                Mock.Of<IUserDataManager>(),
                Mock.Of<ILogger<SeriesNfoSaver>>())
        {
        }

        public string Path(BaseItem item) => GetLocalSavePath(item);
    }

    private sealed class SeasonSaverProbe : SeasonNfoSaver
    {
        public SeasonSaverProbe()
            : base(
                Mock.Of<IFileSystem>(),
                Mock.Of<IServerConfigurationManager>(),
                Mock.Of<ILibraryManager>(),
                Mock.Of<IUserManager>(),
                Mock.Of<IUserDataManager>(),
                Mock.Of<ILogger<SeasonNfoSaver>>())
        {
        }

        public string Path(BaseItem item) => GetLocalSavePath(item);
    }

    private sealed class EpisodeSaverProbe : EpisodeNfoSaver
    {
        public EpisodeSaverProbe()
            : base(
                Mock.Of<IFileSystem>(),
                Mock.Of<IServerConfigurationManager>(),
                Mock.Of<ILibraryManager>(),
                Mock.Of<IUserManager>(),
                Mock.Of<IUserDataManager>(),
                Mock.Of<ILogger<EpisodeNfoSaver>>())
        {
        }

        public string Path(BaseItem item) => GetLocalSavePath(item);
    }
}
