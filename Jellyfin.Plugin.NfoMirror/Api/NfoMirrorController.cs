using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.NfoMirror.Activity;
using Jellyfin.Plugin.NfoMirror.Sync;
using MediaBrowser.Common.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.NfoMirror.Api;

[ApiController]
[Route("NfoMirror")]
[Authorize(Policy = Policies.RequiresElevation)]
public class NfoMirrorController : ControllerBase
{
    [HttpGet("Activity")]
    public ActionResult<ActivityResponse> GetActivity([FromQuery] long after = 0)
        => new ActivityResponse(
            ActivityLog.After(after).Select(e => new ActivityLine(e.Seq, e.TimeUtc, e.Kind.ToString(), e.Message)).ToList(),
            SyncRunner.IsRunning);
}

public sealed record ActivityLine(long Seq, DateTime TimeUtc, string Kind, string Message);

public sealed record ActivityResponse(IReadOnlyList<ActivityLine> Lines, bool Running);
