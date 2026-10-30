using System;
using System.IO;
using System.Linq;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.NfoMirror;

internal static class NfoRoot
{
    private static readonly TimeSpan _cacheFor = TimeSpan.FromSeconds(30);
    private static readonly object _lock = new();
    private static (string Configured, DateTime At, string? Root, string Error) _cache;

    public static string? TryGet(ILibraryManager libraryManager, out string error)
    {
        var configured = Plugin.Instance?.Configuration.NfoRoot ?? string.Empty;
        lock (_lock)
        {
            if (_cache.Configured == configured && DateTime.UtcNow - _cache.At < _cacheFor)
            {
                error = _cache.Error;
                return _cache.Root;
            }
        }

        var libraryPaths = libraryManager.GetVirtualFolders().SelectMany(f => f.Locations ?? []);
        var ok = MirrorPaths.TryValidateRoot(configured, libraryPaths, out var root, out error);

        // A missing mount would otherwise get created inside the container and silently lost.
        if (ok && !Directory.Exists(root))
        {
            ok = false;
            error = $"NFO root '{root}' does not exist.";
        }

        lock (_lock)
        {
            _cache = (configured, DateTime.UtcNow, ok ? root : null, error);
        }

        return ok ? root : null;
    }
}
