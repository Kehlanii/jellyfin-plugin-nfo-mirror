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
