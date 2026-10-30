using System.Linq;
using Jellyfin.Plugin.NfoMirror.Activity;
using Xunit;

namespace Jellyfin.Plugin.NfoMirror.Tests;

[Collection("ActivityLog")]
public sealed class ActivityLogTests
{
    [Fact]
    public void AfterZeroReturnsEntriesInOrder()
    {
        ActivityLog.Clear();
        ActivityLog.Add(ActivityKind.Created, "first");
        ActivityLog.Add(ActivityKind.Moved, "second");
        ActivityLog.Add(ActivityKind.Skipped, "third");

        var entries = ActivityLog.After(0);

        Assert.Equal(3, entries.Count);
        Assert.Equal("first", entries[0].Message);
        Assert.Equal("second", entries[1].Message);
        Assert.Equal("third", entries[2].Message);
        Assert.Equal(ActivityKind.Created, entries[0].Kind);
        Assert.Equal(ActivityKind.Moved, entries[1].Kind);
        Assert.Equal(ActivityKind.Skipped, entries[2].Kind);
        Assert.Equal(1L, entries[0].Seq);
        Assert.True(entries[0].Seq < entries[1].Seq);
        Assert.True(entries[1].Seq < entries[2].Seq);
    }

    [Fact]
    public void AfterSeqReturnsOnlyNewerEntries()
    {
        ActivityLog.Clear();
        ActivityLog.Add(ActivityKind.Info, "first");
        ActivityLog.Add(ActivityKind.Info, "second");
        var last = ActivityLog.After(0).Last().Seq;
        ActivityLog.Add(ActivityKind.Info, "third");

        var entries = ActivityLog.After(last);

        var entry = Assert.Single(entries);
        Assert.Equal("third", entry.Message);
        Assert.True(entry.Seq > last);
    }

    [Fact]
    public void KeepsOnlyTheNewestCapacityEntries()
    {
        ActivityLog.Clear();
        for (var i = 0; i < ActivityLog.Capacity + 10; i++)
        {
            ActivityLog.Add(ActivityKind.Info, "entry " + i);
        }

        var entries = ActivityLog.After(0, max: ActivityLog.Capacity + 20);

        Assert.Equal(ActivityLog.Capacity, entries.Count);
        Assert.Equal(11L, entries[0].Seq);
        Assert.Equal(ActivityLog.Capacity + 10L, entries[entries.Count - 1].Seq);
    }

    [Fact]
    public void AfterHonoursMax()
    {
        ActivityLog.Clear();
        for (var i = 0; i < 10; i++)
        {
            ActivityLog.Add(ActivityKind.Info, "entry " + i);
        }

        var entries = ActivityLog.After(0, max: 3);

        Assert.Equal(3, entries.Count);
        Assert.Equal(1L, entries[0].Seq);
        Assert.Equal(2L, entries[1].Seq);
        Assert.Equal(3L, entries[2].Seq);
    }
}
