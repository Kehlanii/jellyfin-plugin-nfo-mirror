using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Xunit;

namespace Jellyfin.Plugin.NfoMirror.Tests;

public class MirrorPathsTests
{
    private const string Root = "/NFO";

    [Theory]
    [InlineData("/movies/Shrek (2001).mkv", true, VideoType.VideoFile, "/NFO/movies/Shrek (2001).nfo")]
    [InlineData("/movies/Alien (1979)/Alien (1979).mkv", false, VideoType.VideoFile, "/NFO/movies/Alien (1979)/movie.nfo")]
    [InlineData("/movies/Heat (1995)", false, VideoType.Dvd, "/NFO/movies/Heat (1995)/VIDEO_TS/VIDEO_TS.nfo")]
    [InlineData("/movies/Dune (2021)", false, VideoType.BluRay, "/NFO/movies/Dune (2021)/movie.nfo")]
    [InlineData("/movies/Dune (2021)", true, VideoType.BluRay, "/NFO/movies/Dune (2021)/Dune (2021).nfo")]
    [InlineData("/movies/Zażółć gęślą jaźń (2020).mkv", true, VideoType.VideoFile, "/NFO/movies/Zażółć gęślą jaźń (2020).nfo")]
    public void GetMirrorPathMapsMovies(string path, bool isInMixedFolder, VideoType videoType, string expected)
    {
        var movie = new Movie
        {
            Path = path,
            IsInMixedFolder = isInMixedFolder,
            VideoType = videoType
        };

        Assert.Equal(expected, MirrorPaths.GetMirrorPath(Root, new ItemInfo(movie)));
    }

    [Theory]
    [InlineData("/shows/Show", "/NFO/shows/Show/tvshow.nfo")]
    [InlineData("/cartoons/Pszczółka Maja", "/NFO/cartoons/Pszczółka Maja/tvshow.nfo")]
    public void GetMirrorPathMapsSeries(string path, string expected)
        => Assert.Equal(expected, MirrorPaths.GetMirrorPath(Root, new ItemInfo(new Series { Path = path })));

    [Fact]
    public void GetMirrorPathMapsSeason()
    {
        var season = new Season { Path = "/shows/Show/Season 01" };

        Assert.Equal("/NFO/shows/Show/Season 01/season.nfo", MirrorPaths.GetMirrorPath(Root, new ItemInfo(season)));
    }

    [Fact]
    public void GetMirrorPathMapsEpisode()
    {
        var episode = new Episode { Path = "/shows/Show/Season 01/Show S01E01.mkv" };

        Assert.Equal("/NFO/shows/Show/Season 01/Show S01E01.nfo", MirrorPaths.GetMirrorPath(Root, new ItemInfo(episode)));
    }

    [Theory]
    [InlineData("/NFO")]
    [InlineData("/NFO/")]
    public void TryValidateRootAcceptsRootWithOrWithoutTrailingSlash(string configured)
    {
        var ok = MirrorPaths.TryValidateRoot(configured, ["/movies", "/shows"], out var root, out var error);

        Assert.True(ok, error);
        Assert.Equal("/NFO", root);
        Assert.Empty(error);

        var movie = new Movie { Path = "/movies/Shrek (2001).mkv", IsInMixedFolder = true, VideoType = VideoType.VideoFile };
        Assert.Equal("/NFO/movies/Shrek (2001).nfo", MirrorPaths.GetMirrorPath(root, new ItemInfo(movie)));
    }

    [Fact]
    public void TryValidateRootAcceptsRootOutsideLibraries()
    {
        var ok = MirrorPaths.TryValidateRoot("/NFO", ["/movies", "/shows"], out var root, out var error);

        Assert.True(ok, error);
        Assert.Equal("/NFO", root);
    }

    [Fact]
    public void TryValidateRootAcceptsSiblingPathWithSharedPrefix()
    {
        var ok = MirrorPaths.TryValidateRoot("/movie", ["/movies"], out var root, out var error);

        Assert.True(ok, error);
        Assert.Equal("/movie", root);
    }

    [Fact]
    public void TryValidateRootRejectsRelativePath()
    {
        var ok = MirrorPaths.TryValidateRoot("NFO", ["/movies"], out var root, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, root);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryValidateRootRejectsFilesystemRoot()
    {
        var ok = MirrorPaths.TryValidateRoot("/", [], out var root, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, root);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryValidateRootRejectsRootInsideLibrary()
    {
        var ok = MirrorPaths.TryValidateRoot("/movies/nfo", ["/movies"], out var root, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, root);
        Assert.NotEmpty(error);
    }

    [Fact]
    public void TryValidateRootRejectsLibraryInsideRoot()
    {
        var ok = MirrorPaths.TryValidateRoot("/data", ["/data/movies"], out var root, out var error);

        Assert.False(ok);
        Assert.Equal(string.Empty, root);
        Assert.NotEmpty(error);
    }

    [Theory]
    [InlineData("/movies/x.nfo")]
    [InlineData("/NFO/../movies/x.nfo")]
    [InlineData("/NFOther/x.nfo")]
    [InlineData("/NFO")]
    public void EnsureUnderRootRejectsPathsOutsideRoot(string path)
        => Assert.Throws<InvalidOperationException>(() => MirrorPaths.EnsureUnderRoot(Root, path));

    [Fact]
    public void EnsureUnderRootReturnsNormalizedPath()
    {
        Assert.Equal("/NFO/a/b.nfo", MirrorPaths.EnsureUnderRoot(Root, "/NFO/a/b.nfo"));
        Assert.Equal("/NFO/a/b.nfo", MirrorPaths.EnsureUnderRoot("/NFO/", "/NFO/a/b.nfo"));
    }

    [Fact]
    public void GetOrphanPathMovesFileIntoOrphansFolder()
        => Assert.Equal("/NFO/.orphans/movies/x.nfo", MirrorPaths.GetOrphanPath(Root, "/NFO/movies/x.nfo"));

    [Fact]
    public void GetSourceNfoPathReturnsNullForUnsupportedItem()
    {
        var video = new Video { Path = "/x/y.mkv" };

        Assert.Null(MirrorPaths.GetSourceNfoPath(new ItemInfo(video)));
        Assert.Null(MirrorPaths.GetMirrorPath(Root, new ItemInfo(video)));
    }
}
