using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Kleinhelfer fuer Crawler-Antworten, die vorher je Datei kopiert waren (Codereview A5-013)
/// — zwei der Kuerzungs-Kopien machten aus <c>null</c> einen Leerstring.
/// </summary>
public class DirectoryTextTests
{
    [Fact]
    public void Truncate_NullBleibtNull()
    {
        Assert.Null(DirectoryText.Truncate(null, 10));
        Assert.Null(ExternalDirectorySource.Truncate(null, 10));
    }

    [Theory]
    [InlineData("", 3, "")]
    [InlineData("abc", 3, "abc")]
    [InlineData("abcdef", 3, "abc")]
    public void Truncate_KuerztAufMax(string value, int max, string expected)
    {
        Assert.Equal(expected, DirectoryText.Truncate(value, max));
        Assert.Equal(expected, ExternalDirectorySource.Truncate(value, max));
    }

    [Theory]
    [InlineData("2026-10-03", 2026, 10, 3)]
    [InlineData(" 2026-10-03 ", 2026, 10, 3)]
    public void ParseDate_LiestIsoDatum(string text, int y, int m, int d)
    {
        Assert.Equal(new DateOnly(y, m, d), DirectoryText.ParseDate(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("demnaechst")]
    public void ParseDate_UnlesbarIstNull(string? text)
    {
        Assert.Null(DirectoryText.ParseDate(text));
    }
}
