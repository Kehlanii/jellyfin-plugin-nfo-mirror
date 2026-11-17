using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.NfoMirror.Activity;
using Jellyfin.Plugin.NfoMirror.Sync;
using MediaBrowser.Common.Api;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.NfoMirror.Api;

[ApiController]
[Route("NfoMirror")]
[Authorize(Policy = Policies.RequiresElevation)]
public class NfoMirrorController : ControllerBase
{
    private const int MaxListed = 100;

    private readonly ILibraryManager _libraryManager;

    public NfoMirrorController(ILibraryManager libraryManager) => _libraryManager = libraryManager;

    [HttpGet("Activity")]
    public ActionResult<ActivityResponse> GetActivity([FromQuery] long after = 0)
        => new ActivityResponse(
            ActivityLog.After(after).Select(e => new ActivityLine(e.Seq, e.TimeUtc, e.Kind.ToString(), e.Message)).ToList(),
            SyncRunner.IsRunning,
            ActivityLog.LastSeq);

    [HttpGet("Check")]
    public ActionResult<CheckResponse> Check()
    {
        var root = NfoRoot.TryGet(_libraryManager, out var error);
        if (root is null)
        {
            return new CheckResponse(error, 0, 0, [], 0, [], 0);
        }

        try
        {
            var inventory = MirrorInventory.Build(_libraryManager, root, null);
            var expected = inventory.ExpectedMirrors();
            var missing = inventory.Items
                .Where(i => i.Mirror is not null && !System.IO.File.Exists(i.Mirror))
                .Select(i => i.Mirror!)
                .ToList();

            var orphansDir = Path.Combine(root, MirrorPaths.OrphansFolder);
            var unmapped = Directory.EnumerateFiles(root, "*.nfo", SearchOption.AllDirectories)
                .Where(p => !MirrorPaths.IsUnder(orphansDir, p) && !expected.Contains(p))
                .ToList();
            var orphans = Directory.Exists(orphansDir)
                ? Directory.EnumerateFiles(orphansDir, "*.nfo", SearchOption.AllDirectories).Count()
                : 0;

            return new CheckResponse(null, inventory.Items.Count, missing.Count, missing.Take(MaxListed).ToList(), unmapped.Count, unmapped.Take(MaxListed).ToList(), orphans);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new CheckResponse(ex.Message, 0, 0, [], 0, [], 0);
        }
    }
}

public sealed record ActivityLine(long Seq, DateTime TimeUtc, string Kind, string Message);

// LastSeq drops below the client's cursor after a Jellyfin restart.
public sealed record ActivityResponse(IReadOnlyList<ActivityLine> Lines, bool Running, long LastSeq);

public sealed record CheckResponse(string? Error, int Items, int MissingCount, IReadOnlyList<string> Missing, int UnmappedCount, IReadOnlyList<string> Unmapped, int Orphans);
