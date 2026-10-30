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

public class FullSyncTask : IScheduledTask
{
    public const string TaskKey = "NfoMirrorFullSync";

    private readonly SyncRunner _runner;

    public FullSyncTask(
        IFileSystem fileSystem,
        IServerConfigurationManager configurationManager,
        ILibraryManager libraryManager,
        IUserManager userManager,
        IUserDataManager userDataManager,
        ILoggerFactory loggerFactory)
    {
        var saver = new NfoMirrorSaver(fileSystem, configurationManager, libraryManager, userManager, userDataManager, loggerFactory);
        _runner = new SyncRunner(libraryManager, saver, loggerFactory.CreateLogger<FullSyncTask>());
    }

    public string Name => "NFO Mirror: full sync";

    public string Key => TaskKey;

    public string Description => "Writes missing or outdated .nfo files for every item in Movies, Shows and Mixed Movies and Shows libraries, restores matches from .orphans, then runs the orphan sweep (Off, Preview or Move).";

    public string Category => "Library";

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => [];

    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
        => _runner.RunAsync(SyncKind.Full, progress, cancellationToken);
}
