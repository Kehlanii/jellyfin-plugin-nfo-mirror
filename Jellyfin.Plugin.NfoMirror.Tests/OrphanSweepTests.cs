using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.NfoMirror.Sync;
using Xunit;

namespace Jellyfin.Plugin.NfoMirror.Tests;

public sealed class OrphanSweepTests : IDisposable
{
    private readonly string _baseDir;
    private readonly string _root;
    private readonly string _libraryRoot;

    public OrphanSweepTests()
    {
        _baseDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        _root = Path.Combine(_baseDir, "nfo");
        _libraryRoot = Path.Combine(_baseDir, "media", "movies");

        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_libraryRoot);
        File.WriteAllText(Path.Combine(_libraryRoot, "Shrek (2001).mkv"), string.Empty);
    }

    public void Dispose()
    {
        if (Directory.Exists(_baseDir))
        {
            Directory.Delete(_baseDir, true);
        }
    }

    [Fact]
    public void MovesSingleOrphanAndLeavesExpectedFilesAlone()
    {
        var expected = CreateExpectedFiles();
        var orphan = WriteNfo("shows/Stary Serial/tvshow.nfo");

        var result = OrphanSweep.Run(_root, expected, new[] { _libraryRoot }, move: true);

        Assert.Equal(1, result.Moved);
        Assert.Equal(6, result.Total);
        Assert.Null(result.SkippedReason);
        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(Path.Combine(_root, MirrorPaths.OrphansFolder, "shows", "Stary Serial", "tvshow.nfo")));
        var move = Assert.Single(result.MovedFiles);
        Assert.Equal(orphan, move.From);
        Assert.Equal(Path.Combine(_root, MirrorPaths.OrphansFolder, "shows", "Stary Serial", "tvshow.nfo"), move.To);
        Assert.All(expected, path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void SkipsWhenLibraryRootDoesNotExist()
    {
        var expected = CreateExpectedFiles();
        var orphan = WriteNfo("movies/Orphan.nfo");

        var result = OrphanSweep.Run(_root, expected, new[] { Path.Combine(_baseDir, "missing") }, move: true);

        Assert.Equal(0, result.Moved);
        Assert.NotNull(result.SkippedReason);
        Assert.True(File.Exists(orphan));
        Assert.All(expected, path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void SkipsWhenLibraryRootIsEmpty()
    {
        var emptyLibrary = Path.Combine(_baseDir, "media", "Empty");
        Directory.CreateDirectory(emptyLibrary);
        var expected = CreateExpectedFiles();
        var orphan = WriteNfo("movies/Orphan.nfo");

        var result = OrphanSweep.Run(_root, expected, new[] { emptyLibrary }, move: true);

        Assert.Equal(0, result.Moved);
        Assert.NotNull(result.SkippedReason);
        Assert.True(File.Exists(orphan));
        Assert.All(expected, path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void SkipsWhenMoreThanTwentyPercentWouldMove()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            WriteNfo("movies/a.nfo"),
            WriteNfo("movies/b.nfo"),
            WriteNfo("movies/c.nfo")
        };
        var firstOrphan = WriteNfo("movies/Orphan1.nfo");
        var secondOrphan = WriteNfo("movies/Orphan2.nfo");

        var result = OrphanSweep.Run(_root, expected, new[] { _libraryRoot }, move: true);

        Assert.Equal(0, result.Moved);
        Assert.NotNull(result.SkippedReason);
        Assert.Equal(new[] { firstOrphan, secondOrphan }, result.Orphans.Order());
        Assert.True(File.Exists(firstOrphan));
        Assert.True(File.Exists(secondOrphan));
        Assert.False(Directory.Exists(Path.Combine(_root, MirrorPaths.OrphansFolder)));
    }

    [Fact]
    public void IgnoresFilesAlreadyUnderOrphansFolder()
    {
        var expected = CreateExpectedFiles();
        var archived = WriteNfo($"{MirrorPaths.OrphansFolder}/Old/keep.nfo");

        var result = OrphanSweep.Run(_root, expected, new[] { _libraryRoot }, move: true);

        Assert.Equal(0, result.Moved);
        Assert.Equal(5, result.Total);
        Assert.Null(result.SkippedReason);
        Assert.True(File.Exists(archived));
        Assert.All(expected, path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void PreviewReportsSingleOrphanWithoutMovingIt()
    {
        var expected = CreateExpectedFiles();
        var orphan = WriteNfo("shows/Stary Serial/tvshow.nfo");

        var result = OrphanSweep.Run(_root, expected, new[] { _libraryRoot }, move: false);

        Assert.Equal(0, result.Moved);
        Assert.Empty(result.MovedFiles);
        Assert.Equal(orphan, Assert.Single(result.Orphans));
        Assert.Equal(6, result.Total);
        Assert.Null(result.SkippedReason);
        Assert.True(File.Exists(orphan));
        Assert.False(Directory.Exists(Path.Combine(_root, MirrorPaths.OrphansFolder)));
        Assert.All(expected, path => Assert.True(File.Exists(path), path));
    }

    [Fact]
    public void PreviewListsAllOrphansEvenAboveTheMoveLimit()
    {
        var expected = new HashSet<string>(StringComparer.Ordinal)
        {
            WriteNfo("movies/a.nfo"),
            WriteNfo("movies/b.nfo"),
            WriteNfo("movies/c.nfo")
        };
        var firstOrphan = WriteNfo("movies/Orphan1.nfo");
        var secondOrphan = WriteNfo("movies/Orphan2.nfo");

        var result = OrphanSweep.Run(_root, expected, new[] { _libraryRoot }, move: false);

        Assert.Equal(0, result.Moved);
        Assert.Null(result.SkippedReason);
        Assert.Equal(new[] { firstOrphan, secondOrphan }, result.Orphans.Order());
        Assert.True(File.Exists(firstOrphan));
        Assert.True(File.Exists(secondOrphan));
        Assert.False(Directory.Exists(Path.Combine(_root, MirrorPaths.OrphansFolder)));
    }

    [Fact]
    public void RestorePutsArchivedNfoBackAtItsTarget()
    {
        var target = Path.GetFullPath(Path.Combine(_root, "movies", "x.nfo"));
        var archived = WriteNfo($"{MirrorPaths.OrphansFolder}/movies/x.nfo");

        var restored = OrphanSweep.Restore(_root, new[] { target });

        Assert.Equal(target, Assert.Single(restored));
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(archived));
    }

    [Fact]
    public void RestoreNeverOverwritesAnExistingNfo()
    {
        var target = WriteNfo("movies/x.nfo");
        File.WriteAllText(target, "live");
        var archived = WriteNfo($"{MirrorPaths.OrphansFolder}/movies/x.nfo");
        File.WriteAllText(archived, "old");

        var restored = OrphanSweep.Restore(_root, new[] { target });

        Assert.Empty(restored);
        Assert.Equal("live", File.ReadAllText(target));
        Assert.True(File.Exists(archived));
        Assert.Equal("old", File.ReadAllText(archived));
    }

    [Fact]
    public void RestoreWithoutArchivedCopyDoesNothing()
    {
        var target = Path.GetFullPath(Path.Combine(_root, "movies", "x.nfo"));

        var restored = OrphanSweep.Restore(_root, new[] { target });

        Assert.Empty(restored);
        Assert.False(File.Exists(target));
        Assert.False(Directory.Exists(Path.Combine(_root, MirrorPaths.OrphansFolder)));
    }

    [Fact]
    public void MovedOrphanGetsFreshModifiedTime()
    {
        var expected = CreateExpectedFiles();
        var orphan = WriteNfo("shows/Stary Serial/tvshow.nfo");
        File.SetLastWriteTimeUtc(orphan, DateTime.UtcNow.AddDays(-30));

        var result = OrphanSweep.Run(_root, expected, new[] { _libraryRoot }, move: true);

        var moved = Assert.Single(result.MovedFiles).To;
        Assert.True(DateTime.UtcNow - File.GetLastWriteTimeUtc(moved) < TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void PurgeDeletesOnlyOldFilesUnderOrphans()
    {
        var old = WriteNfo($"{MirrorPaths.OrphansFolder}/movies/old.nfo");
        var fresh = WriteNfo($"{MirrorPaths.OrphansFolder}/movies/fresh.nfo");
        var live = WriteNfo("movies/live.nfo");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));
        File.SetLastWriteTimeUtc(live, DateTime.UtcNow.AddDays(-40));

        var deleted = OrphanSweep.Purge(_root, TimeSpan.FromDays(30));

        Assert.Equal(old, Assert.Single(deleted));
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(live));
    }

    [Fact]
    public void PurgeWithNothingOldDeletesNothing()
    {
        var fresh = WriteNfo($"{MirrorPaths.OrphansFolder}/movies/fresh.nfo");

        Assert.Empty(OrphanSweep.Purge(_root, TimeSpan.FromDays(30)));
        Assert.Empty(OrphanSweep.Purge(Path.Combine(_baseDir, "nfo-none"), TimeSpan.FromDays(1)));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void PurgePrunesEmptyDirectoriesButKeepsOrphansFolder()
    {
        var old = WriteNfo($"{MirrorPaths.OrphansFolder}/shows/Show/Season 1/e1.nfo");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-2));
        Directory.CreateDirectory(Path.Combine(_root, MirrorPaths.OrphansFolder, "empty"));

        OrphanSweep.Purge(_root, TimeSpan.FromDays(1));

        var orphans = Path.Combine(_root, MirrorPaths.OrphansFolder);
        Assert.True(Directory.Exists(orphans));
        Assert.Empty(Directory.EnumerateFileSystemEntries(orphans));
    }

    [Fact]
    public void StampAllGivesEveryOrphanAFreshTime()
    {
        var a = WriteNfo($"{MirrorPaths.OrphansFolder}/movies/a.nfo");
        var b = WriteNfo($"{MirrorPaths.OrphansFolder}/shows/Show/b.nfo.20260101000000");
        File.SetLastWriteTimeUtc(a, DateTime.UtcNow.AddDays(-90));
        File.SetLastWriteTimeUtc(b, DateTime.UtcNow.AddDays(-90));

        Assert.Equal(2, OrphanSweep.StampAll(_root));

        Assert.True(DateTime.UtcNow - File.GetLastWriteTimeUtc(a) < TimeSpan.FromMinutes(5));
        Assert.True(DateTime.UtcNow - File.GetLastWriteTimeUtc(b) < TimeSpan.FromMinutes(5));
        Assert.Equal(0, OrphanSweep.StampAll(Path.Combine(_baseDir, "nfo-none")));
    }

    [Fact]
    public void PurgeDeletesAgedTimestampedCopyAndKeepsFreshOne()
    {
        var aged = WriteNfo($"{MirrorPaths.OrphansFolder}/movies/a.nfo.20260101000000");
        var fresh = WriteNfo($"{MirrorPaths.OrphansFolder}/movies/b.nfo.20260102000000");
        var other = WriteNfo($"{MirrorPaths.OrphansFolder}/movies/c.txt");
        File.SetLastWriteTimeUtc(aged, DateTime.UtcNow.AddDays(-40));
        File.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddDays(-40));

        var deleted = OrphanSweep.Purge(_root, TimeSpan.FromDays(30));

        Assert.Equal(aged, Assert.Single(deleted));
        Assert.True(File.Exists(fresh));
        Assert.True(File.Exists(other));
    }

    private HashSet<string> CreateExpectedFiles()
        => new(StringComparer.Ordinal)
        {
            WriteNfo("movies/a.nfo"),
            WriteNfo("movies/b.nfo"),
            WriteNfo("movies/c.nfo"),
            WriteNfo("shows/Show/tvshow.nfo"),
            WriteNfo("shows/Show/Season 01/season.nfo")
        };

    private string WriteNfo(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return Path.GetFullPath(path);
    }
}
