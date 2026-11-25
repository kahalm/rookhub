using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Scoresheet;

namespace RookHub.Api.Tests.Scoresheet;

/// <summary>Notierte Restzeiten auf dem Partieformular (1.015.0): lesen, prüfen, an die Halbzüge hängen und als
/// <c>[%clk]</c> ins PGN schreiben — ohne Modell-Aufruf, nur mit fertigen Lesungen.</summary>
public class ScoresheetClockTests
{
    [Theory]
    [InlineData("1:12", 72 * 60)]
    [InlineData("1.12", 72 * 60)]
    [InlineData("1,12", 72 * 60)]
    [InlineData("1h12", 72 * 60)]
    [InlineData(" 1:12' ", 72 * 60)]
    [InlineData("0:05:30", 5 * 60 + 30)]
    [InlineData("1:30:00", 90 * 60)]
    [InlineData("72", 72 * 60)]
    [InlineData("72'", 72 * 60)]
    [InlineData("72’", 72 * 60)]
    [InlineData("72min", 72 * 60)]
    [InlineData("72'30", 72 * 60 + 30)]
    [InlineData("72'30''", 72 * 60 + 30)]
    [InlineData("45:30", 45 * 60 + 30)]
    [InlineData("0", 0)]
    public void Parse_CommonHandwrittenForms(string raw, int seconds) => Assert.Equal(seconds, ScoresheetClock.Parse(raw));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Sf3")]
    [InlineData("1:75")]       // 75 Minuten nach einer Stunde gibt es nicht
    [InlineData("72'75")]      // Sekunden über 59
    [InlineData("999")]        // über 6 Stunden
    [InlineData("12.05.2026")] // ein Datum
    [InlineData("?")]
    public void Parse_NotAClock_IsNull(string? raw) => Assert.Null(ScoresheetClock.Parse(raw));

    [Fact]
    public void Normalize_WritesHmmss() => Assert.Equal("1:12:00", ScoresheetClock.Normalize("72"));

    [Fact]
    public void Plausible_DropsTheOutlier_KeepsTheGoodValuesAfterIt()
    {
        // Weiß: 85, 80, 12 (verlesen statt 72), 70, 66 Minuten — Schwarz: 88, 84, 81
        var perPly = new List<int?> { 85 * 60, 88 * 60, 80 * 60, 84 * 60, 12 * 60, null, 70 * 60, 81 * 60, 66 * 60 };
        var ok = ScoresheetClock.Plausible(perPly);
        // 12 → 70 wäre ein Sprung um 58 min: die längste stimmige Folge ist 85, 80, 70, 66 — der Ausreißer fällt.
        Assert.Null(ok[4]);
        Assert.Equal(70 * 60, ok[6]);
        Assert.Equal(66 * 60, ok[8]);
        Assert.Equal(new int?[] { 88 * 60, 84 * 60, 81 * 60 }, new[] { ok[1], ok[3], ok[7] });
    }

    [Fact]
    public void Plausible_TimeControlBonus_IsKept()
    {
        // Nach Zug 40: +30 min (+30 s Zuschlag) — kein Lesefehler.
        var ok = ScoresheetClock.Plausible(new List<int?> { 5 * 60, null, 35 * 60 + 30, null });
        Assert.Equal(35 * 60 + 30, ok[2]);
    }

    private static ScoresheetTranscription Sheet(params (string Written, string? Clock)[] entries) => new()
    {
        Moves = entries.Select((e, i) => new ScoresheetTranscription.Entry
        {
            MoveNumber = i / 2 + 1, Color = i % 2 == 0 ? "w" : "b", Written = e.Written, San = e.Written, Clock = e.Clock,
        }).ToList(),
    };

    [Fact]
    public void Attach_GivesEachPlyTheClockOfItsEntry_InsertedPlyHasNone()
    {
        var t = Sheet(("e4", "1:29"), ("e5", ""), ("Nf3", "88"), ("Nc6", "1.28"));
        var r = ScoresheetResolver.Resolve(t.Scanned(), new ScoresheetResolver.Options(null));
        r.Plies.Insert(2, new ScoresheetPly { W = null, San = "x", Match = ScoresheetResolver.Matches.Inserted });

        ScoresheetClock.Attach(r, t);

        Assert.Equal("1:29:00", r.Plies[0].Clock);
        Assert.Null(r.Plies[1].Clock);
        Assert.Null(r.Plies[2].Clock);
        Assert.Equal("1:28:00", r.Plies[3].Clock);   // „88" Minuten
        Assert.Equal("1:28:00", r.Plies[4].Clock);
    }

    [Fact]
    public void Transcription_ReadsTheClockField_OldReadingsHaveNone()
    {
        var t = ScoresheetTranscription.Parse("""{"moves":[{"moveNumber":1,"color":"w","written":"e4","san":"e4","clock":"1:12"},{"moveNumber":1,"color":"b","written":"e5","san":"e5"}]}""");
        Assert.Equal("1:12", t!.Moves[0].Clock);
        Assert.Null(t.Moves[1].Clock);
    }

    [Fact]
    public void Transcription_ClockWrittenIntoTheMove_IsSplitOff()
    {
        // Format der Kufstein-Vorlage: Restminuten mit Apostroph direkt hinter dem Zug.
        var t = ScoresheetTranscription.Parse("""{"moves":[{"moveNumber":1,"color":"w","written":"Sf3 81'","san":"Nf3 81'"},{"moveNumber":1,"color":"b","written":"d5","san":"d5","clock":"68'"},{"moveNumber":2,"color":"w","written":"c4 1:05","san":"c4","clock":"4'"}]}""");
        Assert.Equal(("Sf3", "Nf3", "81'"), (t!.Moves[0].Written, t.Moves[0].San, t.Moves[0].Clock));
        Assert.Equal("68'", t.Moves[1].Clock);
        Assert.Equal(("c4 1:05", "4'"), (t.Moves[2].Written, t.Moves[2].Clock));   // Uhr schon da: nichts umdeuten
        Assert.Equal(81 * 60, ScoresheetClock.Parse(t.Moves[0].Clock));
    }

    [Fact]
    public void Schema_AsksForTheClock_PromptsNoLongerIgnoreIt()
    {
        var items = ScoresheetPrompt.Schema()["properties"].GetProperty("moves").GetProperty("items");
        Assert.Contains("clock", items.GetProperty("required").EnumerateArray().Select(e => e.GetString()));
        Assert.True(items.GetProperty("properties").TryGetProperty("clock", out _));
        Assert.DoesNotContain("Ignore clock times", ScoresheetPrompt.System);
        Assert.DoesNotContain("Ignore clock times", ScoresheetPrompt.TranscribeSystem);
        Assert.Contains("\"clock\"", ScoresheetPrompt.System);
        Assert.Contains("\"clock\"", ScoresheetPrompt.TranscribeSystem);
    }

    [Fact]
    public void Comments_PutTheClockFirst_NextToTheSheetNote()
    {
        var r = new ScoresheetResolution
        {
            Plies =
            {
                new ScoresheetPly { W = 0, Written = "e4", San = "e4", Match = ScoresheetResolver.Matches.Exact, Clock = "1:29:00" },
                new ScoresheetPly { W = 1, Written = "e6", San = "e5", Match = ScoresheetResolver.Matches.Fuzzy, Clock = "1:28:30" },
                new ScoresheetPly { W = 2, Written = "Sf3", San = "Nf3", Match = ScoresheetResolver.Matches.Written },
            },
        };
        var c = ScoresheetComments.For(r);
        Assert.Equal("[%clk 1:29:00]", c[0]);
        Assert.Equal("[%clk 1:28:30] sheet: e6", c[1]);
        Assert.False(c.ContainsKey(2));
        var text = PgnWriter.MoveText(r.Plies.Select(p => p.San).ToList(), comments: c);
        Assert.Equal("1. e4 {[%clk 1:29:00]} e5 {[%clk 1:28:30] sheet: e6} 2. Nf3 *", text);
    }

    [Fact]
    public void PgnClock_FormatReadCombineStrip()
    {
        Assert.Equal("0:05:07", PgnClock.Format(307));
        Assert.Equal("[%clk 1:12:00]", PgnClock.Tag(4320));
        Assert.Equal(4320, PgnClock.SecondsOf("gut [%clk 1:12:00] gespielt"));
        Assert.Equal("gut gespielt", PgnClock.Strip("gut [%clk 1:12:00] gespielt"));
        Assert.Equal("[%clk 0:10:00] Idee", PgnClock.Combine(600, "[%clk 1:12:00] Idee"));
        Assert.Equal("Idee", PgnClock.Combine(null, "Idee"));
        Assert.Equal(new int?[] { 60, null, 50 }, PgnClock.Mainline("1. e4 {[%clk 0:01:00]} e5 (1... c5 {[%clk 0:00:59]}) 2. Nf3 {[%clk 0:00:50]} *"));
        Assert.Null(PgnClock.Aligned("1. e4 {[%clk 0:01:00]} e5 *", 3));
    }

    [Fact]
    public void CountPlies_IgnoresComments()
    {
        Assert.Equal(3, SavedGamePgn.CountPlies("[Event \"x\"]\n\n1. e4 {[%clk 1:29:00] sheet: e6} e5 {[%clk 1:28:00]} 2. Nf3 *"));
    }

    [Fact]
    public void ClubPgnOf_WritesClocks_AndKeepsThemWhenRewritten()
    {
        var g = new LeagueClubGame { White = "a", Black = "b", Result = "*" };
        var sans = new List<string> { "e4", "e5", "Nf3" };
        g.Pgn = ClubPgnText.PgnOf(g, sans, new int?[] { 90 * 60, null, 89 * 60 });
        Assert.Contains("1. e4 {[%clk 1:30:00]} e5 2. Nf3 {[%clk 1:29:00]} *", g.Pgn);

        // Namen/Ergebnis neu geschrieben, Züge gleich: die Uhren bleiben.
        g.Result = "1-0";
        var rewritten = ClubPgnText.PgnOf(g, sans);
        Assert.Contains("1. e4 {[%clk 1:30:00]} e5 2. Nf3 {[%clk 1:29:00]} 1-0", rewritten);

        // Andere Züge ab Halbzug 2: nur die Uhr davor bleibt.
        var changed = ClubPgnText.PgnOf(g, new List<string> { "e4", "c5", "Nf3" });
        Assert.Contains("1. e4 {[%clk 1:30:00]} c5 2. Nf3 1-0", changed);
        Assert.Equal(new int?[] { 90 * 60, null, 89 * 60 }, ClubPgnText.ClocksOf(g.Pgn));
    }

    [Fact]
    public void ClubClocksOfPlies_OnlyWhenTheyMatchTheMoves()
    {
        var plies = new List<ScoresheetPly> { new() { San = "e4", Clock = "1:30:00" }, new() { San = "e5", Clock = null } };
        Assert.Equal(new int?[] { 5400, null }, ClubPgnText.ClocksOf(plies, 2));
        Assert.Null(ClubPgnText.ClocksOf(plies, 3));
    }
}
