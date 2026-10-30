using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Jellyfin.Plugin.NfoMirror.Sync;

public static class OrphanSweep
{
    public const double MaxOrphanShare = 0.20;

    public static SweepResult Run(string root, IReadOnlySet<string> expected, IEnumerable<string> libraryRoots)
    {
        foreach (var libraryRoot in libraryRoots)
        {
            if (!Directory.Exists(libraryRoot) || !Directory.EnumerateFileSystemEntries(libraryRoot).Any())
            {
                return SweepResult.Skip($"library root '{libraryRoot}' is unreachable or empty");
            }
        }

        var orphansDir = Path.Combine(root, MirrorPaths.OrphansFolder);
        var all = Directory.EnumerateFiles(root, "*.nfo", SearchOption.AllDirectories)
            .Where(p => !MirrorPaths.IsUnder(orphansDir, p))
            .ToList();
        var orphans = all.Where(p => !expected.Contains(p)).ToList();

        if (orphans.Count == 0)
        {
            return new SweepResult([], all.Count, null);
        }

        if (orphans.Count > all.Count * MaxOrphanShare)
        {
            return SweepResult.Skip($"{orphans.Count} of {all.Count} NFOs would be moved (limit {MaxOrphanShare:P0})", orphans);
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

        return new SweepResult(moved, all.Count, null);
    }
}

public sealed record MovedOrphan(string From, string To);

public sealed record SweepResult(IReadOnlyList<MovedOrphan> MovedFiles, int Total, string? SkippedReason)
{
    public int Moved => MovedFiles.Count;

    // Files the sweep would have moved, kept so a skipped sweep can still be inspected in the log.
    public IReadOnlyList<string> Candidates { get; init; } = [];

    internal static SweepResult Skip(string reason, IReadOnlyList<string>? candidates = null)
        => new([], 0, reason) { Candidates = candidates ?? [] };
}
