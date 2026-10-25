using RookHub.Tools.LibraryImport.Commands;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Api.Tests.Library;

/// <summary>
/// Die Kommandozeile von <c>tools/LibraryImport</c> (Zerlegung Z17): dieselbe Lesart wie vorher <c>IntArg</c>/<c>StringArg</c>
/// in Program.cs — Wert = Element nach dem Namen, erstes Vorkommen gilt, keine Zahl = nicht angegeben.
/// </summary>
public class LibraryImportToolOptionsTests
{
    [Fact]
    public void IntAndString_ReadTheElementAfterTheName_FirstOccurrenceWins()
    {
        var o = new ToolOptions(["queue", "25", "--user", "7", "--depth", "x", "--user", "9", "--to", "de"]);

        Assert.Equal(7, o.Int("--user"));
        Assert.Null(o.Int("--depth"));          // keine Zahl → Vorgabe des Befehls greift
        Assert.Equal("de", o.String("--to"));
        Assert.Equal("25", o.Positional(1));
        Assert.Null(o.Positional(10));
    }

    [Fact]
    public void Option_AtTheEnd_WithoutValue_IsAbsent_ButCountsAsFlag()
    {
        var o = new ToolOptions(["translate", "--to"]);

        Assert.Null(o.String("--to"));
        Assert.Null(o.Int("--to"));
        Assert.True(o.Has("--to"));
        Assert.False(o.Has("--force"));
    }

    [Theory]
    [InlineData(null, true, 0, 1)]
    [InlineData("0/4", true, 0, 4)]
    [InlineData("3/4", true, 3, 4)]
    [InlineData("4/4", false, 0, 0)]
    [InlineData("1", false, 0, 0)]
    [InlineData("x/2", false, 0, 0)]
    [InlineData("0/0", false, 0, 0)]
    [InlineData("-1/2", false, 0, 0)]
    public void TryShard_AcceptsOnlyIndexBelowCount(string? shard, bool ok, int index, int count)
    {
        string[] args = shard is null ? ["translate", "--to", "de"] : ["translate", "--to", "de", "--shard", shard];

        var parsed = new ToolOptions(args).TryShard(out var i, out var n, out var raw);

        Assert.Equal(ok, parsed);
        Assert.Equal(shard, raw);
        if (ok) Assert.Equal((index, count), (i, n));
    }

    [Fact]
    public void CommandTable_KnowsExactlyTheEighteenCommands()
    {
        string[] expected =
        [
            "analysis-openings", "comments", "dedupe", "embed", "import", "languages", "league-import", "league-train",
            "migrate", "openings", "pat-cap", "queue", "rehash-results", "resplit", "score", "stats", "translate", "tree",
        ];
        Assert.Equal(expected, CommandTable.All.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("stats", CommandTable.Default);
        Assert.False(CommandTable.All.ContainsKey("Stats"));   // Gross-/Kleinschreibung zaehlt wie beim alten switch
    }
}
