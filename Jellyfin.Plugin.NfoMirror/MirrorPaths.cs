using System;
using System.Collections.Generic;
using System.IO;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.NfoMirror;

public static class MirrorPaths
{
    public const string OrphansFolder = ".orphans";

    public static bool TryValidateRoot(string? configuredRoot, IEnumerable<string> libraryPaths, out string root, out string error)
    {
        root = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(configuredRoot) || !Path.IsPathFullyQualified(configuredRoot))
        {
            error = $"NFO root '{configuredRoot}' must be an absolute path.";
            return false;
        }

        var normalized = Normalize(configuredRoot);
        if (string.Equals(normalized, Path.GetPathRoot(normalized), StringComparison.Ordinal))
        {
            error = "NFO root must not be the filesystem root.";
            return false;
        }

        foreach (var libraryPath in libraryPaths)
        {
            if (string.IsNullOrWhiteSpace(libraryPath))
            {
                continue;
            }

            var library = Normalize(libraryPath);
            if (IsUnder(library, normalized))
            {
                error = $"NFO root '{normalized}' is inside library path '{library}'.";
                return false;
            }

            // A library under the root would mirror into itself.
            if (IsUnder(normalized, library))
            {
                error = $"Library path '{library}' is inside NFO root '{normalized}'.";
                return false;
            }
        }

        root = normalized;
        return true;
    }

    // Same naming as Jellyfin's own savers' GetLocalSavePath; SaverDriftTests keeps it in sync.
    public static string? GetSourceNfoPath(ItemInfo item)
    {
        if (string.IsNullOrEmpty(item.Path))
        {
            return null;
        }

        if (item.ItemType == typeof(Series))
        {
            return Path.Combine(item.Path, "tvshow.nfo");
        }

        if (item.ItemType == typeof(Season))
        {
            return Path.Combine(item.Path, "season.nfo");
        }

        if (item.ItemType == typeof(Episode))
        {
            return Path.ChangeExtension(item.Path, ".nfo");
        }

        if (item.ItemType == typeof(Movie))
        {
            // First candidate of MovieNfoSaver.GetMovieSavePaths (internal upstream).
            var folder = item.ContainingFolderPath;
            if (item.VideoType == VideoType.Dvd && !item.IsPlaceHolder)
            {
                return Path.Combine(folder, "VIDEO_TS", "VIDEO_TS.nfo");
            }

            if (!item.IsInMixedFolder)
            {
                return Path.Combine(folder, "movie.nfo");
            }

            if (!item.IsPlaceHolder && item.VideoType == VideoType.BluRay)
            {
                return Path.Combine(folder, Path.GetFileName(folder) + ".nfo");
            }

            return Path.ChangeExtension(item.Path, ".nfo");
        }

        return null;
    }

    public static string? GetMirrorPath(string root, ItemInfo item)
    {
        var source = GetSourceNfoPath(item);
        return source is null ? null : ToMirror(root, source);
    }

    public static string ToMirror(string root, string sourcePath)
    {
        var full = Path.GetFullPath(sourcePath);
        var relative = full[Path.GetPathRoot(full)!.Length..];
        return EnsureUnderRoot(root, Path.Combine(root, relative));
    }

    // Single guard for every write, move and mkdir.
    public static string EnsureUnderRoot(string root, string path)
    {
        var normalizedRoot = Normalize(root);
        var target = Normalize(path);
        if (string.Equals(target, normalizedRoot, StringComparison.Ordinal) || !IsUnder(normalizedRoot, target))
        {
            throw new InvalidOperationException($"Refusing to touch '{path}': outside NFO root '{normalizedRoot}'.");
        }

        return target;
    }

    public static string GetOrphanPath(string root, string nfoPath)
    {
        var relative = Path.GetRelativePath(root, EnsureUnderRoot(root, nfoPath));
        return EnsureUnderRoot(root, Path.Combine(root, OrphansFolder, relative));
    }

    internal static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static bool IsUnder(string parent, string path)
    {
        if (string.Equals(parent, path, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = Path.EndsInDirectorySeparator(parent) ? parent : parent + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.Ordinal);
    }
}
