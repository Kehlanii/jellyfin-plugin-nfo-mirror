using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.NfoMirror.Savers;

namespace Jellyfin.Plugin.NfoMirror.Activity;

public enum ActivityKind
{
    Info,
    Created,
    Updated,
    Restored,
    Moved,
    WouldMove,
    Skipped,
    Failed,
    Finished
}

public sealed record ActivityEntry(long Seq, DateTime TimeUtc, ActivityKind Kind, string Message);

// Memory only: the plugin writes nothing outside the NFO root.
public static class ActivityLog
{
    public const int Capacity = 5000;

    private static readonly object _lock = new();
    private static readonly Queue<ActivityEntry> _entries = new();
    private static long _seq;

    public static long LastSeq
    {
        get
        {
            lock (_lock)
            {
                return _seq;
            }
        }
    }

    public static ActivityKind For(SaveOutcome outcome) => outcome == SaveOutcome.Created ? ActivityKind.Created : ActivityKind.Updated;

    public static void Add(ActivityKind kind, string message)
    {
        lock (_lock)
        {
            _entries.Enqueue(new ActivityEntry(++_seq, DateTime.UtcNow, kind, message));
            while (_entries.Count > Capacity)
            {
                _entries.Dequeue();
            }
        }
    }

    public static IReadOnlyList<ActivityEntry> After(long seq, int max = 1000)
    {
        lock (_lock)
        {
            return _entries.Where(e => e.Seq > seq).Take(max).ToList();
        }
    }

    internal static void Clear()
    {
        lock (_lock)
        {
            _entries.Clear();
            _seq = 0;
        }
    }
}
