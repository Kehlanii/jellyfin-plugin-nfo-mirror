using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Jellyfin.Plugin.NfoMirror.Renames;

public static class NfoIds
{
    public static IReadOnlyDictionary<string, string> Read(string nfoPath)
    {
        var ids = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(nfoPath, settings);
            reader.MoveToContent();
            if (reader.NodeType != XmlNodeType.Element || reader.IsEmptyElement)
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

                // Also advances past the element.
                var value = reader.ReadElementContentAsString().Trim();
                if (value.Length > 0)
                {
                    ids[key] = value;
                }
            }

            return ids;
        }
        catch (Exception ex) when (ex is XmlException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string Capitalize(string text)
        => char.ToUpper(text[0], CultureInfo.InvariantCulture) + text[1..];
}
