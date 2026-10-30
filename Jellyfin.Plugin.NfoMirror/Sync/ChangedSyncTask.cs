using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.NfoMirror.Savers;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.NfoMirror.Sync;

public class ChangedSyncTask : IScheduledTask
{
    public const string TaskKey = "NfoMirrorChangedSync";

    private readonly SyncRunner _runner;

    public ChangedSyncTask(
        IFileSystem fileSystem,
        IServerConfigurationManager configurationManager,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILoggerFactory loggerFactory)
    {
        var saver = new NfoMirrorSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, loggerFactory);
        _runner = new SyncRunner(libraryManager, saver, loggerFactory.CreateLogger<ChangedSyncTask>());
    }

    public string Name => "NFO Mirror: sync changed";

    public string Key => TaskKey;

    public string Description => "Writes .nfo files only for items Jellyfin saved since the last changed sync. No orphan sweep.";

    public string Category => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _runner.RunAsync(SyncKind.Changed, progress, cancellationToken);
}
