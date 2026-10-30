using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.NfoMirror.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.NfoMirror;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string ProviderName = "NFO Mirror";

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public override string Name => ProviderName;

    public override Guid Id => Guid.Parse("03136b88-4248-4296-a0ea-29bf7e8a9c00");

    public static Plugin? Instance { get; private set; }

    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }
}
