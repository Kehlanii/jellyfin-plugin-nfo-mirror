using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FsCheck.Xunit;
using Jellyfin.Plugin.NfoMirror.Configuration;
using Jellyfin.Plugin.NfoMirror.Renames;

namespace Jellyfin.Plugin.NfoMirror.Tests;

public class RenameTrackerProperties
{
    private static readonly IReadOnlyDictionary<string, string> Ids = new Dictionary<string, string> { ["Imdb"] = "tt1" };

    // The seed drives a random tree and a random follow, so a failing seed reproduces the case.
    [Property(MaxTest = 60)]
    public bool FollowNeverLosesOrLeaksFiles(int seed)
    {
        var random = new Random(seed);
        var baseDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var root = Path.Combine(baseDir, "nfo");
        try
        {
            var files = new HashSet<string>(StringComparer.Ordinal);
            var count = random.Next(1, 31);
            for (var i = 0; i < count; i++)
            {
                var depth = random.Next(1, 4);
                var parts = Enumerable.Range(0, depth - 1).Select(_ => "d" + random.Next(0, 4)).Append($"f{i}.nfo");
                files.Add(Path.Combine(root, Path.Combine(parts.ToArray())));
            }

            foreach (var file in files)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "x");
            }

            var tracker = new RenameTracker();
            var dirs = files.Select(f => Path.GetDirectoryName(f)!).Where(d => d != root).Distinct().ToList();
            var tag = random.Next(1000, 9999);
            if (dirs.Count > 0 && random.Next(2) == 0)
            {
                var dir = dirs[random.Next(dirs.Count)];
                tracker.Record(new RemovalRecord(MediaKind.Series, "/old/show", Path.Combine(dir, "tvshow.nfo"), null, Ids, DateTime.UtcNow));
                var target = Path.Combine(root, $"new{tag}", "tvshow.nfo");
                tracker.TryFollow(root, new NewItem(MediaKind.Series, "/new/show", target, null, Ids), FollowMode.On);
            }
            else
            {
                var file = files.ElementAt(random.Next(files.Count));
                tracker.Record(new RemovalRecord(MediaKind.Movie, "/old/movie.mkv", file, 100, Ids, DateTime.UtcNow));
                var target = Path.Combine(root, $"new{tag}.nfo");
                tracker.TryFollow(root, new NewItem(MediaKind.Movie, "/new/movie.mkv", target, 100, Ids), FollowMode.On);
            }

            var after = Directory.EnumerateFiles(root, "*.nfo", SearchOption.AllDirectories).ToList();
            var everything = Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories).Count();
            return after.Count == files.Count && everything == files.Count && after.All(f => MirrorPaths.IsUnder(root, f));
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, true);
            }
        }
    }

    [Property]
    public bool EnsureUnderRootThrowsOrStaysStrictlyUnderRoot(string? s)
    {
        // Embedded NUL is rejected by GetFullPath before the guard runs; paths from Jellyfin cannot contain it.
        if (string.IsNullOrEmpty(s) || s.Contains('\0', StringComparison.Ordinal))
        {
            return true;
        }

        try
        {
            var result = MirrorPaths.EnsureUnderRoot("/NFO", s);
            return MirrorPaths.IsUnder("/NFO", result) && result != "/NFO";
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
