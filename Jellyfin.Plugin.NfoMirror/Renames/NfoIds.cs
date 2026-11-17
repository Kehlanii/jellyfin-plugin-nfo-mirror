using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Jellyfin.Plugin.NfoMirror.Renames;

public static class NfoIds
{
    public static IReadOnlyDictionary<string, string> Read(string nfoPath) => Read(nfoPath, out _);

    public static IReadOnlyDictionary<string, string> Read(string nfoPath, out string? rootElement)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        rootElement = null;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(nfoPath, settings);
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element)
            {
                return ids;
            }

            rootElement = reader.LocalName;
            if (reader.IsEmptyElement)
            {
                return ids;
            }

            var depth = reader.Depth;
            reader.Read();
            while (!reader.EOF && reader.Depth > depth)
            {
                if (reader.NodeType != XmlNodeType.Element || reader.Depth != depth + 1)
                {
                    reader.Read();
                    continue;
                }

                var type = reader.GetAttribute("type");
                var key = reader.LocalName switch
                {
                    "uniqueid" when !string.IsNullOrWhiteSpace(type) => Capitalize(type.Trim()),
                    "imdbid" => "Imdb",
                    "tmdbid" => "Tmdb",
                    "tvdbid" => "Tvdb",
                    _ => null
                };
                if (key is null)
                {
                    reader.Skip();
                    continue;
                }

                try
                {
                    // Also advances past the element.
                    var value = reader.ReadElementContentAsString().Trim();
                    if (value.Length > 0)
                    {
                        ids[key] = value;
                    }
                }
                catch (XmlException)
                {
                    // An id element with child elements: skip it, keep the ids already read.
                    if (reader.ReadState != ReadState.Interactive)
                    {
                        return ids;
                    }

                    reader.Skip();
                }
            }

            return ids;
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            // A broken document keeps what was read before the break.
            return ids;
        }
    }

    private static string Capitalize(string text)
        => char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
