using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.NfoMirror.Configuration;
using Jellyfin.Plugin.NfoMirror.Renames;
using Xunit;

namespace Jellyfin.Plugin.NfoMirror.Tests;

public sealed class RenameTrackerTests : IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> NoIds = new Dictionary<string, string>();

    private readonly string _baseDir;
    private readonly string _root;
    private DateTime _now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private readonly RenameTracker _tracker;

    public RenameTrackerTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        _root = Path.Combine(_baseDir, "nfo");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(Path.Combine(_baseDir, "media"));
        _tracker = new RenameTracker(() => _now);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, true);
        }
    }

    [Fact]
    public void MovieRenamedIsFollowedBySize()
    {
        var oldNfo = Nfo("movies/Old.nfo", "edited");
        Record(MediaKind.Movie, "movies/Old.mkv", oldNfo, size: 1234);
        var newMedia = Media("movies/New.mkv", 1234);

        var result = _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia, size: 1234), FollowMode.On, _ => false);

        Assert.NotNull(result);
        Assert.True(result.Moved);
        Assert.Equal("size", result.Reason);
        Assert.Equal("edited", File.ReadAllText(MirrorFor(newMedia)));
        Assert.False(File.Exists(oldNfo));
        Assert.Equal(0, _tracker.Count);
    }

    [Fact]
    public void MovieMovedToAnotherFolderIsFollowedByFileName()
    {
        var oldNfo = Nfo("movies/Old Folder/Shrek.nfo", "edited");
        Record(MediaKind.Movie, "movies/Old Folder/Shrek.mkv", oldNfo, size: 10);
        var newMedia = Media("movies/New Folder/Shrek.mkv", 20);

        var result = _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia, size: null), FollowMode.On, _ => false);

        Assert.NotNull(result);
        Assert.Equal("file name", result.Reason);
        Assert.Equal("edited", File.ReadAllText(MirrorFor(newMedia)));
        Assert.False(File.Exists(oldNfo));
        Assert.False(Directory.Exists(Path.GetDirectoryName(oldNfo)));
    }

    [Fact]
    public void ProviderIdWinsOverSize()
    {
        var sizeNfo = Nfo("movies/BySize.nfo", "size");
        var idNfo = Nfo("movies/ById.nfo", "id");
        Record(MediaKind.Movie, "movies/BySize.mkv", sizeNfo, size: 500);
        Record(MediaKind.Movie, "movies/ById.mkv", idNfo, size: 999, ids: new Dictionary<string, string> { ["Imdb"] = "tt1" });
        var newMedia = Media("movies/New.mkv", 500);
        var ids = new Dictionary<string, string> { ["imdb"] = " tt1 " };

        var result = _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia, size: 500, ids: ids), FollowMode.On, _ => false);

        Assert.NotNull(result);
        Assert.Equal("provider id", result.Reason);
        Assert.Equal("id", File.ReadAllText(MirrorFor(newMedia)));
        Assert.True(File.Exists(sizeNfo));
    }

    [Fact]
    public void AmbiguousSizeMovesNothing()
    {
        var first = Nfo("movies/A.nfo", "a");
        var second = Nfo("movies/B.nfo", "b");
        Record(MediaKind.Movie, "movies/A.mkv", first, size: 700);
        Record(MediaKind.Movie, "movies/B.mkv", second, size: 700);
        var newMedia = Media("movies/C.mkv", 700);

        var result = _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia, size: 700), FollowMode.On, _ => false);

        Assert.Null(result);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.False(File.Exists(MirrorFor(newMedia)));
        Assert.Equal(2, _tracker.Count);
    }

    [Fact]
    public void SeasonFolderRenamedMovesSeasonAndEpisodeNfos()
    {
        var oldDir = "shows/Show/Season 1";
        var seasonNfo = Nfo($"{oldDir}/season.nfo", "season");
        var e1 = Nfo($"{oldDir}/E01.nfo", "e1");
        var e2 = Nfo($"{oldDir}/E02.nfo", "e2");
        Record(MediaKind.Season, oldDir, seasonNfo);
        Record(MediaKind.Episode, $"{oldDir}/E01.mkv", e1, size: 1);
        Record(MediaKind.Episode, $"{oldDir}/E02.mkv", e2, size: 2);
        var newDir = Path.Combine(_baseDir, "media", "shows/Show/Season 01");
        Media("shows/Show/Season 01/E01.mkv", 11);
        Media("shows/Show/Season 01/E02.mkv", 12);

        var result = _tracker.TryFollow(_root, Item(MediaKind.Season, newDir), FollowMode.On, _ => false);

        Assert.NotNull(result);
        Assert.Equal("folder contents", result.Reason);
        Assert.Equal(3, result.Files);
        var newMirrorDir = Path.Combine(_root, "media", "shows/Show/Season 01");
        Assert.Equal("season", File.ReadAllText(Path.Combine(newMirrorDir, "season.nfo")));
        Assert.Equal("e1", File.ReadAllText(Path.Combine(newMirrorDir, "E01.nfo")));
        Assert.Equal("e2", File.ReadAllText(Path.Combine(newMirrorDir, "E02.nfo")));
        Assert.False(Directory.Exists(Path.GetDirectoryName(seasonNfo)));
        Assert.Equal(0, _tracker.Count);
    }

    [Fact]
    public void ShowFolderRenamedMovesWholeSubtree()
    {
        var oldRoot = "shows/Old";
        var tvshow = Nfo($"{oldRoot}/tvshow.nfo", "show");
        Nfo($"{oldRoot}/Season 1/season.nfo", "season");
        Nfo($"{oldRoot}/Season 1/E01.nfo", "e1");
        Nfo($"{oldRoot}/Season 1/E02.nfo", "e2");
        Record(MediaKind.Series, oldRoot, tvshow);
        Record(MediaKind.Episode, $"{oldRoot}/Season 1/E01.mkv", Path.Combine(_root, "media", oldRoot, "Season 1", "E01.nfo"), size: 1);
        Record(MediaKind.Episode, $"{oldRoot}/Season 1/E02.mkv", Path.Combine(_root, "media", oldRoot, "Season 1", "E02.nfo"), size: 2);
        var newDir = Path.Combine(_baseDir, "media", "shows/New");
        Media("shows/New/Season 1/E01.mkv", 31);
        Media("shows/New/Season 1/E02.mkv", 32);

        var result = _tracker.TryFollow(_root, Item(MediaKind.Series, newDir), FollowMode.On, _ => false);

        Assert.NotNull(result);
        Assert.Equal(4, result.Files);
        var newMirror = Path.Combine(_root, "media", "shows/New");
        Assert.Equal("show", File.ReadAllText(Path.Combine(newMirror, "tvshow.nfo")));
        Assert.Equal("season", File.ReadAllText(Path.Combine(newMirror, "Season 1", "season.nfo")));
        Assert.Equal("e1", File.ReadAllText(Path.Combine(newMirror, "Season 1", "E01.nfo")));
        Assert.Equal("e2", File.ReadAllText(Path.Combine(newMirror, "Season 1", "E02.nfo")));
        Assert.False(Directory.Exists(Path.Combine(_root, "media", oldRoot)));
        Assert.Equal(0, _tracker.Count);
    }

    [Fact]
    public void OccupiedTargetIsNeverOverwritten()
    {
        var oldNfo = Nfo("movies/Old.nfo", "old");
        Record(MediaKind.Movie, "movies/Old.mkv", oldNfo, size: 50);
        var newMedia = Media("movies/New.mkv", 50);
        var occupied = Nfo("media/movies/New.nfo", "live");

        var result = _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia, size: 50), FollowMode.On, _ => false);

        Assert.Null(result);
        Assert.Equal("live", File.ReadAllText(occupied));
        Assert.Equal("old", File.ReadAllText(oldNfo));
    }

    [Fact]
    public void FolderFollowKeepsConflictingFileAtSource()
    {
        var oldDir = "shows/Show/Season 1";
        var seasonNfo = Nfo($"{oldDir}/season.nfo", "season");
        var e1 = Nfo($"{oldDir}/E01.nfo", "e1");
        var e2 = Nfo($"{oldDir}/E02.nfo", "e2");
        Record(MediaKind.Season, oldDir, seasonNfo);
        Record(MediaKind.Episode, $"{oldDir}/E01.mkv", e1, size: 1);
        Record(MediaKind.Episode, $"{oldDir}/E02.mkv", e2, size: 2);
        var newDir = Path.Combine(_baseDir, "media", "shows/Show/Season 01");
        Media("shows/Show/Season 01/E01.mkv", 11);
        Media("shows/Show/Season 01/E02.mkv", 12);
        var existing = Nfo("media/shows/Show/Season 01/E01.nfo", "live");

        var result = _tracker.TryFollow(_root, Item(MediaKind.Season, newDir), FollowMode.On, _ => false);

        Assert.NotNull(result);
        Assert.Equal(1, result.Conflicts);
        Assert.Equal(2, result.Files);
        Assert.Equal("live", File.ReadAllText(existing));
        Assert.Equal("e1", File.ReadAllText(e1));
        Assert.Equal("e2", File.ReadAllText(Path.Combine(_root, "media", "shows/Show/Season 01", "E02.nfo")));
    }

    [Fact]
    public void PreviewChangesNothingAndOffReturnsNull()
    {
        var oldNfo = Nfo("movies/Old.nfo", "old");
        Record(MediaKind.Movie, "movies/Old.mkv", oldNfo, size: 42);
        var newMedia = Media("movies/New.mkv", 42);
        var item = Item(MediaKind.Movie, newMedia, size: 42);

        var preview = _tracker.TryFollow(_root, item, FollowMode.Preview, _ => false);

        Assert.NotNull(preview);
        Assert.False(preview.Moved);
        Assert.Equal(1, preview.Files);
        Assert.True(File.Exists(oldNfo));
        Assert.False(File.Exists(MirrorFor(newMedia)));
        Assert.Equal(1, _tracker.Count);
        Assert.Null(_tracker.TryFollow(_root, item, FollowMode.Off, _ => false));
        Assert.True(File.Exists(oldNfo));
    }

    [Fact]
    public void FollowsFromOrphansWhenAlreadySwept()
    {
        var orphan = Nfo(".orphans/media/movies/Old.nfo", "archived");
        Record(MediaKind.Movie, "movies/Old.mkv", Path.Combine(_root, "media", "movies", "Old.nfo"), size: 77);
        var newMedia = Media("movies/New.mkv", 77);

        var result = _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia, size: 77), FollowMode.On, _ => false);

        Assert.NotNull(result);
        Assert.True(result.Moved);
        Assert.Equal("archived", File.ReadAllText(MirrorFor(newMedia)));
        Assert.False(File.Exists(orphan));
        Assert.True(Directory.Exists(Path.Combine(_root, MirrorPaths.OrphansFolder)));
    }

    [Fact]
    public void ExpiredRecordsAreIgnored()
    {
        var oldNfo = Nfo("movies/Old.nfo", "old");
        Record(MediaKind.Movie, "movies/Old.mkv", oldNfo, size: 5);
        var newMedia = Media("movies/New.mkv", 5);
        _now += RenameTracker.MaxAge + TimeSpan.FromMinutes(1);

        var result = _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia, size: 5), FollowMode.On, _ => false);

        Assert.Null(result);
        Assert.True(File.Exists(oldNfo));
        Assert.Equal(0, _tracker.Count);
    }

    [Fact]
    public void MirrorPathOutsideRootIsRefused()
    {
        var outside = Path.Combine(_baseDir, "outside", "Old.nfo");
        Directory.CreateDirectory(Path.GetDirectoryName(outside)!);
        File.WriteAllText(outside, "x");
        Record(MediaKind.Movie, "movies/Old.mkv", outside, size: 9);
        var newMedia = Media("movies/New.mkv", 9);

        Assert.Throws<InvalidOperationException>(() => _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia, size: 9), FollowMode.On, _ => false));

        Assert.True(File.Exists(outside));
        Assert.False(File.Exists(MirrorFor(newMedia)));
    }

    [Fact]
    public void InUseRecordIsNotFollowedAndIsDropped()
    {
        var oldNfo = Nfo("movies/Old.nfo", "old");
        Record(MediaKind.Movie, "movies/Old.mkv", oldNfo, size: 61);
        var newMedia = Media("movies/New.mkv", 61);

        var result = _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia, size: 61), FollowMode.On, _ => true);

        Assert.Null(result);
        Assert.Equal("old", File.ReadAllText(oldNfo));
        Assert.False(File.Exists(MirrorFor(newMedia)));
        Assert.Equal(0, _tracker.Count);
    }

    [Fact]
    public void ExtensionChangeRecordIsDroppedWhenTargetExists()
    {
        Nfo("media/movies/B.nfo", "live");
        Record(MediaKind.Movie, "movies/B.mkv", Path.Combine(_root, "media", "movies", "B.nfo"), size: 8);
        var newMedia = Media("movies/B.mp4", 8);

        var result = _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia, size: 8), FollowMode.On, _ => false);

        Assert.Null(result);
        Assert.Equal(0, _tracker.Count);
        Assert.Equal("live", File.ReadAllText(MirrorFor(newMedia)));
    }

    [Fact]
    public void NameMatchNeedsTheOldFolderGone()
    {
        var oldNfo = Nfo("movies/Old Folder/Shrek.nfo", "edited");
        Record(MediaKind.Movie, "movies/Old Folder/Shrek.mkv", oldNfo, size: 10);
        var oldDir = Path.Combine(_baseDir, "media", "movies", "Old Folder");
        Directory.CreateDirectory(oldDir);
        var newMedia = Media("movies/New Folder/Shrek.mkv", 20);

        Assert.Null(_tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia), FollowMode.On, _ => false));
        Assert.True(File.Exists(oldNfo));

        Directory.Delete(oldDir);
        var result = _tracker.TryFollow(_root, Item(MediaKind.Movie, newMedia), FollowMode.On, _ => false);

        Assert.NotNull(result);
        Assert.Equal("file name", result.Reason);
    }

    [Fact]
    public void FolderContentsNeedsTheOldFolderGone()
    {
        var oldDir = "shows/Show/Season 1";
        var seasonNfo = Nfo($"{oldDir}/season.nfo", "season");
        Record(MediaKind.Season, oldDir, seasonNfo);
        Record(MediaKind.Episode, $"{oldDir}/E01.mkv", Nfo($"{oldDir}/E01.nfo", "e1"), size: 1);
        var oldMedia = Path.Combine(_baseDir, "media", oldDir);
        Directory.CreateDirectory(oldMedia);
        var newDir = Path.Combine(_baseDir, "media", "shows/Show/Season 01");
        Media("shows/Show/Season 01/E01.mkv", 11);

        Assert.Null(_tracker.TryFollow(_root, Item(MediaKind.Season, newDir), FollowMode.On, _ => false));
        Assert.True(File.Exists(seasonNfo));

        Directory.Delete(oldMedia);
        var result = _tracker.TryFollow(_root, Item(MediaKind.Season, newDir), FollowMode.On, _ => false);

        Assert.NotNull(result);
        Assert.Equal("folder contents", result.Reason);
    }

    [Fact]
    public void FolderFollowMovesOnlyNfoFiles()
    {
        var oldDir = "shows/Show/Season 1";
        var seasonNfo = Nfo($"{oldDir}/season.nfo", "season");
        var copy = Nfo($"{oldDir}/tvshow.nfo.20261117", "copy");
        Record(MediaKind.Season, oldDir, seasonNfo);
        Record(MediaKind.Episode, $"{oldDir}/E01.mkv", Nfo($"{oldDir}/E01.nfo", "e1"), size: 1);
        var newDir = Path.Combine(_baseDir, "media", "shows/Show/Season 01");
        Media("shows/Show/Season 01/E01.mkv", 11);

        var result = _tracker.TryFollow(_root, Item(MediaKind.Season, newDir), FollowMode.On, _ => false);

        Assert.NotNull(result);
        Assert.Equal(2, result.Files);
        Assert.Equal("copy", File.ReadAllText(copy));
    }

    [Fact]
    public void ManyRecordsStayFast()
    {
        for (var show = 0; show < 500; show++)
        {
            var folder = Path.Combine(_baseDir, "media", "shows", $"Show {show}");
            _tracker.Record(new RemovalRecord(MediaKind.Series, folder, Path.Combine(_root, "media", "shows", $"Show {show}", "tvshow.nfo"), null, NoIds, _now));
            for (var e = 0; e < 100; e++)
            {
                var media = Path.Combine(folder, $"Season {e % 4}", $"E{e}.mkv");
                _tracker.Record(new RemovalRecord(MediaKind.Episode, media, Path.ChangeExtension(Path.Combine(_root, Path.GetRelativePath(_baseDir, media)), ".nfo"), e, NoIds, _now));
            }
        }

        var newDir = Path.Combine(_baseDir, "media", "shows", "Brand New");
        Directory.CreateDirectory(newDir);
        var watch = Stopwatch.StartNew();
        var result = _tracker.TryFollow(_root, Item(MediaKind.Series, newDir), FollowMode.On, _ => false);
        watch.Stop();

        Assert.Null(result);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"took {watch.Elapsed}");
    }

    private string Media(string relative, int length)
    {
        var path = Path.Combine(_baseDir, "media", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
        return path;
    }

    // Relative to the NFO root.
    private string Nfo(string relative, string content)
    {
        var path = Path.Combine(_root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string MirrorFor(string mediaPath)
        => Path.ChangeExtension(Path.Combine(_root, Path.GetRelativePath(_baseDir, mediaPath)), ".nfo");

    private void Record(MediaKind kind, string mediaRelative, string mirrorPath, long? size = null, IReadOnlyDictionary<string, string>? ids = null)
        => _tracker.Record(new RemovalRecord(kind, Path.Combine(_baseDir, "media", mediaRelative), mirrorPath, size, ids ?? NoIds, _now));

    private NewItem Item(MediaKind kind, string mediaPath, long? size = null, IReadOnlyDictionary<string, string>? ids = null)
    {
        var mirror = kind switch
        {
            MediaKind.Series => Path.Combine(_root, Path.GetRelativePath(_baseDir, mediaPath), "tvshow.nfo"),
            MediaKind.Season => Path.Combine(_root, Path.GetRelativePath(_baseDir, mediaPath), "season.nfo"),
            _ => MirrorFor(mediaPath)
        };
        return new NewItem(kind, mediaPath, mirror, size, ids ?? NoIds);
    }
}
