using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.NfoMirror.Configuration;

namespace Jellyfin.Plugin.NfoMirror.Renames;

public enum MediaKind
{
    Movie,
    Series,
    Season,
    Episode
}

public sealed record RemovalRecord(
    MediaKind Kind,
    string MediaPath,
    string MirrorPath,
    long? Size,
    IReadOnlyDictionary<string, string> ProviderIds,
    DateTime RemovedUtc);

public sealed record NewItem(
    MediaKind Kind,
    string MediaPath,
    string MirrorPath,
    long? Size,
    IReadOnlyDictionary<string, string> ProviderIds);

public sealed record FollowResult(string From, string To, string Reason, int Files, int Conflicts, bool Moved);

public sealed class RenameTracker
{
    public static readonly TimeSpan MaxAge = TimeSpan.FromHours(6);
    public const int MaxRecords = 100_000;

    private readonly object _lock = new();
    private readonly List<RemovalRecord> _records = [];
    private readonly Func<DateTime> _clock;

    public RenameTracker() : this(() => DateTime.UtcNow)
    {
    }

    public RenameTracker(Func<DateTime> clock) => _clock = clock;

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _records.Count;
            }
        }
    }

    public void Record(RemovalRecord record)
    {
        lock (_lock)
        {
            _records.Add(record);
            DropExpired();
            if (_records.Count > MaxRecords)
            {
                _records.RemoveRange(0, _records.Count - MaxRecords);
            }
        }
    }

    public FollowResult? TryFollow(string root, NewItem item, FollowMode mode)
    {
        if (mode == FollowMode.Off)
        {
            return null;
        }

        lock (_lock)
        {
            DropExpired();
            if (File.Exists(item.MirrorPath))
            {
                return null;
            }

            var candidates = _records.Where(r => r.Kind == item.Kind && !string.Equals(r.MediaPath, item.MediaPath, StringComparison.Ordinal)).ToList();
            var (match, reason) = Match(candidates, item);
            if (match is null)
            {
                return null;
            }

            var isFile = item.Kind is MediaKind.Movie or MediaKind.Episode;
            return isFile ? FollowFile(root, item, match, reason, mode) : FollowFolder(root, item, match, reason, mode);
        }
    }

    private void DropExpired()
    {
        var cutoff = _clock() - MaxAge;
        _records.RemoveAll(r => r.RemovedUtc < cutoff);
    }

    private (RemovalRecord? Match, string Reason) Match(List<RemovalRecord> candidates, NewItem item)
    {
        var byId = candidates.Where(r => SharesProviderId(r.ProviderIds, item.ProviderIds)).ToList();
        if (byId.Count > 0)
        {
            return (byId.Count == 1 ? byId[0] : null, "provider id");
        }

        if (item.Kind is MediaKind.Movie or MediaKind.Episode)
        {
            if (item.Size > 0)
            {
                var bySize = candidates.Where(r => r.Size == item.Size).ToList();
                if (bySize.Count > 0)
                {
                    return (bySize.Count == 1 ? bySize[0] : null, "size");
                }
            }

            var name = Path.GetFileName(item.MediaPath);
            var byName = candidates.Where(r => string.Equals(Path.GetFileName(r.MediaPath), name, StringComparison.Ordinal)).ToList();
            return byName.Count > 0 ? (byName.Count == 1 ? byName[0] : null, "file name") : (null, string.Empty);
        }

        var byContents = candidates.Where(r =>
        {
            var relatives = FolderContents(r);
            return relatives.Count > 0 && relatives.All(rel => File.Exists(Path.Combine(item.MediaPath, rel)));
        }).ToList();
        return byContents.Count > 0 ? (byContents.Count == 1 ? byContents[0] : null, "folder contents") : (null, string.Empty);
    }

    private List<string> FolderContents(RemovalRecord folder)
        => ChildRecords(folder).Select(d => Path.GetRelativePath(folder.MediaPath, d.MediaPath)).ToList();

    private List<RemovalRecord> ChildRecords(RemovalRecord folder)
    {
        var parent = MirrorPaths.Normalize(folder.MediaPath);
        return _records
            .Where(d => d.Kind is MediaKind.Episode or MediaKind.Movie && MirrorPaths.IsUnder(parent, MirrorPaths.Normalize(d.MediaPath)))
            .ToList();
    }

    private static bool SharesProviderId(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
        => ProviderIdMatch.Shares(a, b);

    private FollowResult? FollowFile(string root, NewItem item, RemovalRecord record, string reason, FollowMode mode)
    {
        string source;
        if (File.Exists(record.MirrorPath))
        {
            source = record.MirrorPath;
        }
        else
        {
            var orphan = MirrorPaths.GetOrphanPath(root, record.MirrorPath);
            if (!File.Exists(orphan))
            {
                return null;
            }

            source = orphan;
        }

        if (mode == FollowMode.Preview)
        {
            return new FollowResult(source, item.MirrorPath, reason, 1, 0, false);
        }

        var from = MirrorPaths.EnsureUnderRoot(root, source);
        var to = MirrorPaths.EnsureUnderRoot(root, item.MirrorPath);
        CreateParent(root, to);
        File.Move(from, to);
        PruneUpwards(root, Path.GetDirectoryName(from)!);
        _records.Remove(record);
        return new FollowResult(from, to, reason, 1, 0, true);
    }

    private FollowResult? FollowFolder(string root, NewItem item, RemovalRecord record, string reason, FollowMode mode)
    {
        var sourceDir = Path.GetDirectoryName(record.MirrorPath)!;
        var targetDir = Path.GetDirectoryName(item.MirrorPath)!;
        if (!Directory.Exists(sourceDir))
        {
            sourceDir = MirrorPaths.GetOrphanPath(root, sourceDir);
            if (!Directory.Exists(sourceDir))
            {
                return null;
            }
        }

        var files = Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories).ToList();
        if (mode == FollowMode.Preview)
        {
            var wouldConflict = files.Count(f => File.Exists(Path.Combine(targetDir, Path.GetRelativePath(sourceDir, f))));
            return new FollowResult(sourceDir, targetDir, reason, files.Count - wouldConflict, wouldConflict, false);
        }

        var moved = 0;
        var conflicts = 0;
        foreach (var file in files)
        {
            var target = Path.Combine(targetDir, Path.GetRelativePath(sourceDir, file));
            var from = MirrorPaths.EnsureUnderRoot(root, file);
            var to = MirrorPaths.EnsureUnderRoot(root, target);
            if (File.Exists(to))
            {
                conflicts++;
                continue;
            }

            CreateParent(root, to);
            File.Move(from, to);
            moved++;
        }

        PruneBelow(root, sourceDir);
        PruneUpwards(root, sourceDir);

        var children = ChildRecords(record);
        _records.Remove(record);
        foreach (var child in children)
        {
            _records.Remove(child);
        }

        return new FollowResult(sourceDir, targetDir, reason, moved, conflicts, true);
    }

    private static void CreateParent(string root, string file)
    {
        var dir = Path.GetDirectoryName(file)!;
        if (!string.Equals(MirrorPaths.Normalize(dir), MirrorPaths.Normalize(root), StringComparison.Ordinal))
        {
            Directory.CreateDirectory(MirrorPaths.EnsureUnderRoot(root, dir));
        }
    }

    private static void PruneBelow(string root, string dir)
    {
        var subDirs = Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length);
        foreach (var sub in subDirs)
        {
            DeleteIfEmpty(root, sub);
        }
    }

    // Stops at the root and the orphans folder, which are never deleted.
    private static void PruneUpwards(string root, string start)
    {
        var rootDir = MirrorPaths.Normalize(root);
        var orphansDir = Path.Combine(rootDir, MirrorPaths.OrphansFolder);
        var dir = MirrorPaths.Normalize(start);
        while (!string.Equals(dir, rootDir, StringComparison.Ordinal) && !string.Equals(dir, orphansDir, StringComparison.Ordinal))
        {
            if (!DeleteIfEmpty(root, dir))
            {
                return;
            }

            dir = Path.GetDirectoryName(dir)!;
        }
    }

    private static bool DeleteIfEmpty(string root, string dir)
    {
        var guarded = MirrorPaths.EnsureUnderRoot(root, dir);
        if (!Directory.Exists(guarded) || Directory.EnumerateFileSystemEntries(guarded).Any())
        {
            return false;
        }

        Directory.Delete(guarded);
        return true;
    }
}

internal static class ProviderIdMatch
{
    public static bool Shares(IReadOnlyDictionary<string, string> a, IReadOnlyDictionary<string, string> b)
    {
        foreach (var (key, value) in a)
        {
            var left = value?.Trim();
            if (string.IsNullOrEmpty(left))
            {
                continue;
            }

            foreach (var (otherKey, otherValue) in b)
            {
                if (string.Equals(key, otherKey, StringComparison.OrdinalIgnoreCase) && string.Equals(left, otherValue?.Trim(), StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
