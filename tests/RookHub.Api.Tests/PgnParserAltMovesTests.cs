using System.Diagnostics;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// <see cref="PgnParser.ExtractAltMoves"/> spielt die Hauptlinie seit dem Codereview 2026-09-29 (N3-003)
/// EINMAL inkrementell nach und nimmt jede Alternative per <c>Cancel()</c> zurück — vorher je Key und je
/// Alt-SAN von der FEN aus neu, also quadratisch viele Züge je Linie. Die Golden-Fälle unten sind die
/// Ausgaben der ALTEN Fassung (vor dem Umbau erfasst) und müssen Zeichen für Zeichen gleich bleiben.
/// </summary>
public class PgnParserAltMovesTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    /// <summary>Ergebnis kanonisch: „key:uci,uci|key:…" in Einfüge-Reihenfolge, <c>null</c> als „null".</summary>
    private static string Canon(Dictionary<int, List<string>>? d)
        => d == null ? "null" : string.Join("|", d.Select(kv => kv.Key + ":" + string.Join(",", kv.Value)));

    public static IEnumerable<object[]> Corpus() => new[]
    {
        // Mehrere Alternativen je Halbzug, jede Seite.
        new object[] { "several", Start,
            "1. e4 {[%alt d4 c4]} e5 {[%alt c5 e6]} 2. Nf3 {[%alt Nc3 Bc4 Qh5]} Nc6 3. Bb5 {[%alt Bc4 d4]} a6 *" },
        // Umwandlung als Alternative (Chessable-Beispiel aus PgnImportServiceTests).
        new object[] { "promotion", "8/8/p5p1/2k5/P7/2nK1P2/1r1pB3/7R b - - 0 1",
            "1... Nxe2 2. Kxe2 d1=Q {[%alt d1=R+ d1=N]} 3. Kxd1 Rb1 *" },
        // Rochade als Alternative und als Hauptzug; „0-0" wird zu „O-O".
        new object[] { "castling", Start,
            "1. e4 e5 2. Nf3 Nc6 3. Bc4 Bc5 4. c3 {[%alt O-O d3]} Nf6 {[%alt d6]} 5. O-O {[%alt 0-0 d4]} O-O {[%alt d6]} *" },
        // Rochade nach König-hin-und-zurück ist ILLEGAL — hängt an der Zug-HISTORIE, nicht an der Stellung.
        new object[] { "castlingAfterKingMoved", Start,
            "1. e4 e5 2. Ke2 Ke7 3. Ke1 Ke8 4. Nf3 Nf6 5. Bc4 Bc5 6. d3 {[%alt O-O c3]} d6 {[%alt O-O]} *" },
        // En passant als Alternative (Recht aus dem Zug davor).
        new object[] { "enPassantAlt", Start,
            "1. e4 Nf6 2. e5 d5 3. Nf3 {[%alt exd6 d4]} Nd7 *" },
        // En passant als Hauptzug, danach weitere Alternativen.
        new object[] { "enPassantMain", Start,
            "1. e4 d5 2. e5 f5 3. exf6 {[%alt Nf3 d4]} Nxf6 {[%alt exf6 gxf6]} 4. d4 {[%alt Nf3]} *" },
        // En passant aus dem e.p.-Feld der FEN.
        new object[] { "enPassantFromFen", "rnbqkbnr/ppp1p1pp/8/3pPp2/8/8/PPPP1PPP/RNBQKBNR w KQkq f6 0 3",
            "3. Nf3 {[%alt exf6 d4]} Nc6 *" },
        // Matt als Alternative — danach muss das Brett weiter ziehen können.
        new object[] { "mateAlt", Start,
            "1. f3 e5 2. g4 Nc6 {[%alt Qh4# d6]} 3. Nc3 {[%alt d3]} Nf6 {[%alt Qh4]} 4. d3 {[%alt e4]} d6 {[%alt Qh4]} *" },
        // Hauptlinie bricht bei Halbzug 2 (Ke3 illegal): Keys ≤ 2 bleiben, spätere entfallen.
        new object[] { "illegalMainline", Start,
            "1. e4 {[%alt d4]} e5 {[%alt c5]} 2. Ke3 {[%alt Nf3]} Nc6 {[%alt d6]} 3. Nf3 {[%alt d4]} *" },
        // Alt vor dem ersten Zug (Key -1) und Alts in Varianten zählen nicht.
        new object[] { "rootAndVariation", Start,
            "{[%alt e4]} 1. d4 (1. e4 {[%alt c4]} e5) d5 {[%alt Nf6]} 2. c4 {[%alt Nf3]} *" },
        // Ungültige, illegale und doppelte Alternativen, Langschrift.
        new object[] { "invalidAndDuplicates", Start,
            "1. e4 {[%alt Ke2 Qz9 d4 d4 d2d4 e4]} e5 {[%alt e5 c5]} *" },
        // Zwei [%alt]-Kommentare am selben Halbzug werden zusammengelegt.
        new object[] { "twoComments", Start,
            "1. e4 {[%alt d4]} {Text [%alt c4] mehr} e5 *" },
        // Key über das Ende der Hauptlinie hinaus (Alt hinter dem Ergebnis) fällt weg.
        new object[] { "pastEnd", Start,
            "1. e4 e5 {[%alt c5]} 1-0 {[%alt d5]}" },
        // Ungültige FEN ⇒ null.
        new object[] { "badFen", "not a fen", "1. e4 {[%alt d4]} e5 *" },
        // Keine Alternativen ⇒ null.
        new object[] { "none", Start, "1. e4 e5 2. Nf3 *" },
    };

    /// <summary>Ausgaben der alten Fassung (vor N3-003), je Fall aus <see cref="Corpus"/>.</summary>
    private static readonly Dictionary<string, string> Golden = new()
    {
        ["several"] = "0:d2d4,c2c4|1:c7c5,e7e6|2:b1c3,f1c4,d1h5|4:f1c4,d2d4",
        ["promotion"] = "2:d2d1r,d2d1n",
        ["castling"] = "6:e1g1,d2d3|7:d7d6|8:e1g1,d2d4|9:d7d6",
        ["castlingAfterKingMoved"] = "10:c2c3",
        ["enPassantAlt"] = "4:e5d6,d2d4",
        ["enPassantMain"] = "4:g1f3,d2d4|5:e7f6,g7f6|6:g1f3",
        ["enPassantFromFen"] = "0:e5f6,d2d4",
        ["mateAlt"] = "3:d8h4,d7d6|4:d2d3|5:d8h4|6:e2e4",
        ["illegalMainline"] = "0:d2d4|1:c7c5|2:g1f3",
        ["rootAndVariation"] = "1:g8f6|2:g1f3",
        ["invalidAndDuplicates"] = "0:d2d4,e2e4|1:e7e5,c7c5",
        ["twoComments"] = "0:d2d4,c2c4",
        ["pastEnd"] = "1:c7c5,d7d5",
        ["badFen"] = "null",
        ["none"] = "null",
    };

    [Theory]
    [MemberData(nameof(Corpus))]
    public void ExtractAltMoves_SameResultAsTheOldReplayFromScratch(string name, string fen, string moveText)
        => Assert.Equal(Golden[name], Canon(PgnParser.ExtractAltMoves(fen, moveText)));

    /// <summary>Pendelfolge (Springer hin und her — ohne AutoEndgameRules endet sie nie) mit einem [%alt]
    /// an JEDEM Halbzug.</summary>
    private static string PendulumWithAlts(int plies)
    {
        var main = new[] { "Nf3", "Nf6", "Ng1", "Ng8" };
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < plies; i++)
        {
            if (i % 2 == 0) sb.Append(i / 2 + 1).Append(". ");
            sb.Append(main[i % 4]).Append(" {[%alt ").Append(i % 2 == 0 ? "Nc3" : "Nc6").Append("]} ");
        }
        return sb.Append('*').ToString();
    }

    /// <summary>
    /// Vorher spielte jeder Key die Hauptlinie ab der FEN neu: bei n Halbzügen mit [%alt] n²/2 Züge statt n —
    /// und das vor jeder Längenprüfung des Kurs-Uploads. Gemessen wird GEGEN das einmalige Nachspielen derselben
    /// Linie (<see cref="PgnParser.TryExtractUciMainline"/>), damit der Test nicht an der Geschwindigkeit der
    /// Maschine hängt: inkrementell kostet es ein kleines Vielfaches davon, von vorn ein Vielfaches in der
    /// Größenordnung n/3.
    /// </summary>
    [Fact]
    public void ExtractAltMoves_AltOnEveryPly_CostsAboutOneReplayNotOnePerPly()
    {
        const int plies = 1200;
        var text = PendulumWithAlts(plies);
        // Aufwärmen (JIT), damit die erste Messung nicht die Übersetzung mitzählt.
        PgnParser.TryExtractUciMainline(Start, PendulumWithAlts(8));
        PgnParser.ExtractAltMoves(Start, PendulumWithAlts(8));

        var sw = Stopwatch.StartNew();
        var uci = PgnParser.TryExtractUciMainline(Start, text);
        var mainline = sw.Elapsed;
        sw.Restart();
        var alts = PgnParser.ExtractAltMoves(Start, text);
        var altTime = sw.Elapsed;

        Assert.NotNull(uci);
        Assert.Equal(plies, uci!.Count);
        Assert.NotNull(alts);
        Assert.Equal(plies, alts!.Count);
        Assert.Equal(new[] { "b1c3" }, alts[0]);
        Assert.Equal(new[] { "b8c6" }, alts[plies - 1]);
        Assert.True(altTime < mainline * 10 + TimeSpan.FromSeconds(1),
            $"ExtractAltMoves {altTime.TotalMilliseconds:F0} ms, einmal nachspielen {mainline.TotalMilliseconds:F0} ms");
    }

    /// <summary>Eine Linie als Kurs-PGN (Grundstellung, wie ein eigener Kurs-Upload).</summary>
    private static string CoursePgn(string moveText)
        => "[Event \"Kurs\"]\n[Round \"1\"]\n[White \"Pendel\"]\n[FEN \"" + Start + "\"]\n\n" + moveText + "\n";

    private static PgnImportService.ParseResult ParseCourse(string pgn)
        => PgnImportService.ParsePgn("kurs.pgn", pgn, keepCommentOnlyAsInfo: true, playFromStartPosition: true);

    /// <summary>
    /// Auch inkrementell kostet das Nachspielen quadratisch in der Linienlänge, weil Gera.Chess' <c>Move()</c>
    /// selbst O(Historie) kostet: eine Pendellinie mit 20 000 Halbzügen und [%alt] an jedem Halbzug belegte
    /// <see cref="PgnImportService.ParsePgn"/> eine halbe Minute, auf einer ausgelasteten Maschine Minuten. Über
    /// <see cref="PgnImportService.MaxMainlinePlies"/> zählt eine Linie deshalb als ungültig, BEVOR ein Brett
    /// aufgebaut wird. Gemessen gegen eine Linie genau AM Deckel (die ganz nachgespielt wird), damit der Test
    /// nicht an der Geschwindigkeit der Maschine hängt.
    /// </summary>
    [Fact]
    public void ParsePgn_LineOverThePlyCap_IsInvalidBeforeAnyReplay()
    {
        const int cap = PgnImportService.MaxMainlinePlies;
        var atCapPgn = CoursePgn(PendulumWithAlts(cap));
        var hugePgn = CoursePgn(PendulumWithAlts(20_000));
        ParseCourse(CoursePgn(PendulumWithAlts(8)));           // Aufwärmen (JIT)

        var sw = Stopwatch.StartNew();
        var atCap = ParseCourse(atCapPgn);
        var atCapTime = sw.Elapsed;
        sw.Restart();
        var huge = ParseCourse(hugePgn);
        var hugeTime = sw.Elapsed;

        // Am Deckel: noch eine ganz normale Linie, samt Alternativen.
        Assert.Equal(0, atCap.Invalid);
        var line = Assert.Single(atCap.Puzzles);
        Assert.Equal(cap, line.Moves.Split(' ').Length);
        Assert.Equal(cap, line.AltMoves!.Count);

        // Ein Halbzug darüber: ungültig.
        var overByOne = ParseCourse(CoursePgn(PendulumWithAlts(cap + 1)));
        Assert.Empty(overByOne.Puzzles);
        Assert.Equal(1, overByOne.Invalid);

        // 20 000 Halbzüge: ungültig, und schneller als die Linie am Deckel (ohne Deckel das ~60-Fache).
        Assert.Empty(huge.Puzzles);
        Assert.Equal(1, huge.Invalid);
        Assert.True(hugeTime < atCapTime * 2 + TimeSpan.FromSeconds(1),
            $"20 000 Halbzüge {hugeTime.TotalMilliseconds:F0} ms, {cap} Halbzüge {atCapTime.TotalMilliseconds:F0} ms");
    }
}
