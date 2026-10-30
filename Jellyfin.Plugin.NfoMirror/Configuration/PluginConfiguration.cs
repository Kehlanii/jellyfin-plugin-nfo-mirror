using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.NfoMirror.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string NfoRoot { get; set; } = "/NFO";

    public string LastSyncResult { get; set; } = string.Empty;
}
