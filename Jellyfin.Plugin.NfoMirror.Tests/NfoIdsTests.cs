using System;
using System.Globalization;
using System.IO;
using Jellyfin.Plugin.NfoMirror.Renames;
using Xunit;

namespace Jellyfin.Plugin.NfoMirror.Tests;

public sealed class NfoIdsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

    public NfoIdsTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, true);

    [Fact]
    public void ParsesUniqueIdsAndLegacyTags()
    {
        var ids = NfoIds.Read(Write("""
            <movie>
              <title>Shrek</title>
              <uniqueid type="imdb" default="true"> tt0126029 </uniqueid>
              <uniqueid type="tmdb">808</uniqueid>
              <uniqueid type="custom">abc</uniqueid>
              <uniqueid type="tvdb"></uniqueid>
              <tvdbid>555</tvdbid>
              <actor><name>X</name><uniqueid type="imdb">nm1</uniqueid></actor>
            </movie>
            """));

        Assert.Equal("tt0126029", ids["Imdb"]);
        Assert.Equal("808", ids["Tmdb"]);
        Assert.Equal("abc", ids["Custom"]);
        Assert.Equal("555", ids["Tvdb"]);
        Assert.Equal(4, ids.Count);
    }

    [Fact]
    public void IdElementWithChildKeepsTheOtherIdsAndRootNameIsReturned()
    {
        var ids = NfoIds.Read(Write("""
            <movie>
              <uniqueid type="imdb">tt1</uniqueid>
              <uniqueid type="tmdb"><b>x</b></uniqueid>
              <tvdbid>5</tvdbid>
            </movie>
            """), out var root);

        Assert.Equal("movie", root);
        Assert.Equal("tt1", ids["Imdb"]);
        Assert.Equal("5", ids["Tvdb"]);
        Assert.False(ids.ContainsKey("Tmdb"));
    }

    [Fact]
    public void LegacyImdbIdAndCaseInsensitiveKeys()
    {
        var ids = NfoIds.Read(Write("<movie><imdbid>tt1</imdbid><tmdbid>2</tmdbid></movie>"));

        Assert.Equal("tt1", ids["IMDB"]);
        Assert.Equal("2", ids["tmdb"]);
    }

    [Fact]
    public void MalformedXmlGivesEmpty()
    {
        Assert.Empty(NfoIds.Read(Write("<movie><uniqueid type=\"imdb\">tt1")));
        Assert.Empty(NfoIds.Read(Path.Combine(_dir, "missing.nfo")));
    }

    private string Write(string content)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".nfo");
        File.WriteAllText(path, content);
        return path;
    }
}
