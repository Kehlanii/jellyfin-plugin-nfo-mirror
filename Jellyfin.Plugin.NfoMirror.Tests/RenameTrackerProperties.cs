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
    [Property(MaxTest = 100)]
    public bool FollowNeverLosesOrLeaksFiles(int seed)
    {
        var random = new Random(seed);
        var baseDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        var root = Path.Combine(baseDir, "nfo");
        try
        {
            var relatives = new HashSet<string>(StringComparer.Ordinal);
            var count = random.Next(1, 31);
            for (var i = 0; i < count; i++)
            {
                var depth = random.Next(1, 4);
                var parts = Enumerable.Range(0, depth - 1).Select(_ => "d" + random.Next(0, 4)).Append($"f{i}.nfo");
                relatives.Add(Path.Combine(parts.ToArray()));
            }

            // A quarter of the cases start with everything already swept to .orphans.
            var swept = random.Next(4) == 0;
            var storage = swept ? Path.Combine(root, MirrorPaths.OrphansFolder) : root;
            foreach (var rel in relatives)
            {
                var file = Path.Combine(storage, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, rel);
            }

            var tracker = new RenameTracker();
            var dirs = relatives.Select(f => Path.GetDirectoryName(f)!).Where(d => d.Length > 0).Distinct().ToList();
            var tag = random.Next(1000, 9999);
            FollowResult? result;
            if (dirs.Count > 0 && random.Next(2) == 0)
            {
                var dir = dirs[random.Next(dirs.Count)];
                var expected = relatives
                    .Where(r => r.StartsWith(dir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    .Select(r => Path.GetRelativePath(dir, r))
                    .ToHashSet(StringComparer.Ordinal);
                var targetDir = Path.Combine(root, $"new{tag}");
                string? conflict = null;
                if (random.Next(2) == 0)
                {
                    conflict = expected.ElementAt(random.Next(expected.Count));
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(targetDir, conflict))!);
                    File.WriteAllText(Path.Combine(targetDir, conflict), "keep");
                }

                tracker.Record(new RemovalRecord(MediaKind.Series, "/old/show", Path.Combine(root, dir, "tvshow.nfo"), null, Ids, DateTime.UtcNow));
                result = tracker.TryFollow(root, new NewItem(MediaKind.Series, "/new/show", Path.Combine(targetDir, "tvshow.nfo"), null, Ids), FollowMode.On, _ => false);

                var atTarget = Directory.EnumerateFiles(targetDir, "*", SearchOption.AllDirectories)
                    .Select(f => Path.GetRelativePath(targetDir, f))
                    .ToHashSet(StringComparer.Ordinal);
                if (result is not { Moved: true } || !atTarget.SetEquals(expected))
                {
                    return false;
                }

                foreach (var rel in expected.Where(r => r != conflict))
                {
                    if (File.ReadAllText(Path.Combine(targetDir, rel)) != Path.Combine(dir, rel))
                    {
                        return false;
                    }
                }

                if (conflict is not null
                    && (File.ReadAllText(Path.Combine(targetDir, conflict)) != "keep"
                        || File.ReadAllText(Path.Combine(storage, dir, conflict)) != Path.Combine(dir, conflict)))
                {
                    return false;
                }
            }
            else
            {
                var rel = relatives.ElementAt(random.Next(relatives.Count));
                var target = Path.Combine(root, $"new{tag}.nfo");
                tracker.Record(new RemovalRecord(MediaKind.Movie, "/old/movie.mkv", Path.Combine(root, rel), 100, Ids, DateTime.UtcNow));
                result = tracker.TryFollow(root, new NewItem(MediaKind.Movie, "/new/movie.mkv", target, 100, Ids), FollowMode.On, _ => false);
                if (result is not { Moved: true } || File.ReadAllText(target) != rel || File.Exists(Path.Combine(storage, rel)))
                {
                    return false;
                }
            }

            var everything = Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories).Count();
            var nfos = Directory.EnumerateFiles(root, "*.nfo", SearchOption.AllDirectories).ToList();
            var extra = result.Conflicts > 0 ? 1 : 0;
            return nfos.Count == relatives.Count + extra
                && everything == nfos.Count
                && nfos.All(f => MirrorPaths.IsUnder(root, f));
        }
        finally
        {
            if (Directory.Exists(baseDir))
            {
                Directory.Delete(baseDir, true);
            }
        }
    }

    [Property(MaxTest = 200)]
    public bool EnsureUnderRootThrowsOrStaysStrictlyUnderRoot(int seed)
    {
        var random = new Random(seed);
        string[] segments = ["..", ".", "", "a", "b c", "/"];
        var parts = new List<string> { random.Next(2) == 0 ? "/NFO" : "/NFOx" };
        parts.AddRange(Enumerable.Range(0, random.Next(0, 6)).Select(_ => segments[random.Next(segments.Length)]));

        try
        {
            var result = MirrorPaths.EnsureUnderRoot("/NFO", string.Join('/', parts));
            return result.StartsWith("/NFO/", StringComparison.Ordinal) && result != "/NFO";
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
