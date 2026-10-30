using System;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.NfoMirror.Configuration;

public enum SweepMode
{
    Off,
    Preview,
    Move
}

public class PluginConfiguration : BasePluginConfiguration
{
    public string NfoRoot { get; set; } = "/NFO";

    public SweepMode SweepMode { get; set; } = SweepMode.Preview;

    // Plugin version whose baseline (preview-only) sweep already ran.
    public string BaselineVersion { get; set; } = string.Empty;

    public string LastSyncResult { get; set; } = string.Empty;

    public string LastChangedSyncResult { get; set; } = string.Empty;

    // Start of the last successful changed sync; the next one picks up items saved after it.
    public DateTime? LastChangedSyncStartUtc { get; set; }

    public int LastPreviewCount { get; set; }

    public string[] LastPreview { get; set; } = [];
}
