using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Kursname aus den rohen getGame-Antworten: Chessable schreibt ihn in JEDE Linie (<c>game.name</c>), der
/// Seitentext der Extension lieferte dagegen „Short &amp; Sweet0%Priority0/15variations✓ 0/15" (2026-09-19).
/// </summary>
public class ChessableLineJsonTests
{
    // Echte Form aus dem Prod-Linien-Cache (oid 9960377): game.name = Kurs, game.title = Linie.
    private const string Line = "{\"game\":{\"owned\":true,\"bid\":55720,\"oid\":9960377,\"name\":\"  Chessable  Challenge \",\"title\":\"Magnus Carlsen – Sergey Karjakin, New York (rapid 4) 2016\"},\"hash\":\"x\"}";

    [Fact]
    public void CourseNameOf_ReadsGameName_NotTheLineTitle()
    {
        Assert.Equal("Chessable Challenge", ChessableLineJson.CourseNameOf(Line));
    }

    [Fact]
    public void CourseNameOf_ToleratesCasing_AndJunk()
    {
        Assert.Equal("Lifetime Repertoires", ChessableLineJson.CourseNameOf("{\"Game\":{\"Name\":\"Lifetime Repertoires\"}}"));
        Assert.Null(ChessableLineJson.CourseNameOf("{\"game\":{\"name\":\"   \"}}"));
        Assert.Null(ChessableLineJson.CourseNameOf("{\"game\":{\"name\":42}}"));
        Assert.Null(ChessableLineJson.CourseNameOf("{\"error\":{\"message\":\"User is banned or deleted\"}}"));
        Assert.Null(ChessableLineJson.CourseNameOf("{}"));
        Assert.Null(ChessableLineJson.CourseNameOf("not json"));
        Assert.Null(ChessableLineJson.CourseNameOf(null));
        Assert.Null(ChessableLineJson.CourseNameOf("[1,2]"));
    }

    [Fact]
    public void CourseNameOf_CapsAt200Characters()
    {
        var json = "{\"game\":{\"name\":\"" + new string('x', 300) + "\"}}";
        Assert.Equal(ChessableLineJson.MaxNameLength, ChessableLineJson.CourseNameOf(json)!.Length);
    }

    [Fact]
    public void CourseNameOf_Lines_SkipsCachePlaceholdersAndNamelessLines()
    {
        // null = Inhalt aus dem geteilten Cache (LineOids); die erste Linie MIT Namen zaehlt.
        var lines = new List<string?> { null, "{\"game\":{\"data\":[]}}", Line, "{\"game\":{\"name\":\"Other\"}}" };
        Assert.Equal("Chessable Challenge", ChessableLineJson.FirstCourseNameOf(lines));
        Assert.Null(ChessableLineJson.FirstCourseNameOf(new List<string?> { null, "{\"game\":{}}" }));
        Assert.Null(ChessableLineJson.FirstCourseNameOf(null));
    }

    [Fact]
    public void ResolveCourseName_PrefersLines_ThenExtension_ThenFallback()
    {
        const string garbage = "Short & Sweet0%Priority0/15variations✓ 0/15";
        Assert.Equal("Chessable Challenge", ChessableLineJson.ResolveCourseName(garbage, new[] { Line }, "Kapitel 1"));
        Assert.Equal(garbage, ChessableLineJson.ResolveCourseName(garbage, new[] { "{\"game\":{}}" }, "Kapitel 1"));
        Assert.Equal("Kapitel 1", ChessableLineJson.ResolveCourseName("  ", new string?[] { null }, "Kapitel 1"));
        Assert.Null(ChessableLineJson.ResolveCourseName(null, null));
    }
}
