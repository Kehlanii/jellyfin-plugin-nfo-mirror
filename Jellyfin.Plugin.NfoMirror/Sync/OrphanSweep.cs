using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.NfoMirror.Sync;

public static class OrphanSweep
{
    public const double MaxOrphanShare = 0.20;

    public static SweepResult Run(string root, IReadOnlySet<string> expected, IEnumerable<string> libraryRoots, bool move)
    {
        foreach (var libraryRoot in libraryRoots)
        {
            if (!Directory.Exists(libraryRoot) || !Directory.EnumerateFileSystemEntries(libraryRoot).Any())
            {
                return new SweepResult(0, [], [], $"library root '{libraryRoot}' is unreachable or empty");
            }
        }

        var orphansDir = Path.Combine(root, MirrorPaths.OrphansFolder);
        var all = Directory.EnumerateFiles(root, "*.nfo", SearchOption.AllDirectories)
            .Where(p => !MirrorPaths.IsUnder(orphansDir, p))
            .ToList();
        var orphans = all.Where(p => !expected.Contains(p)).ToList();

        if (!move || orphans.Count == 0)
        {
            return new SweepResult(all.Count, orphans, [], null);
        }

        if (orphans.Count > all.Count * MaxOrphanShare)
        {
            return new SweepResult(all.Count, orphans, [], $"{orphans.Count} of {all.Count} NFOs would be moved (limit {MaxOrphanShare:P0})");
        }

        var moved = new List<MovedOrphan>(orphans.Count);
        foreach (var orphan in orphans)
        {
            var target = MirrorPaths.GetOrphanPath(root, orphan);
            if (File.Exists(target))
            {
                target = MirrorPaths.EnsureUnderRoot(root, $"{target}.{DateTime.UtcNow:yyyyMMddHHmmss}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(MirrorPaths.EnsureUnderRoot(root, orphan), target);
            moved.Add(new MovedOrphan(orphan, target));
        }

        return new SweepResult(all.Count, orphans, moved, null);
    }

    // Never overwrites a live NFO.
    public static IReadOnlyList<string> Restore(string root, IEnumerable<string> expected)
    {
        var restored = new List<string>();
        foreach (var path in expected)
        {
            var target = MirrorPaths.EnsureUnderRoot(root, path);
            var source = MirrorPaths.GetOrphanPath(root, target);
            if (File.Exists(target) || !File.Exists(source))
            {
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(source, target);
            restored.Add(target);
        }

        return restored;
    }
}

public sealed record MovedOrphan(string From, string To);

public sealed record SweepResult(int Total, IReadOnlyList<string> Orphans, IReadOnlyList<MovedOrphan> MovedFiles, string? SkippedReason)
{
    public int Moved => MovedFiles.Count;
}
