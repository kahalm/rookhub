using System.Diagnostics;
using System.Text;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Laufzeit und Verhaltensgleichheit von <see cref="RepertoirePgnCleanup.Repair"/> nach dem Umbau auf Indizes
/// (Codereview 2026-09-29, N7-004). Regel 1 lief je Träger einer mehrdeutigen oid einmal über ALLE Partien (Ausschluss-
/// regel) und für jeden falschen Träger noch einmal (Zwillingssuche) — O(n²) in der Partienzahl, im Request des
/// Live-Appends und im Start-Backfill. Die Entscheidungen müssen dieselben bleiben: der Pfad ändert Prod-PGNs (nur
/// ausblenden) — daher der Vergleich gegen die wörtliche alte Fassung (<see cref="LegacyRepertoirePgnCleanup"/>).
/// </summary>
public class RepertoirePgnCleanupScaleTests
{
    private static readonly IReadOnlyDictionary<string, string> NoTruth = new Dictionary<string, string>();

    // ── Golden-Test: neue gegen alte Fassung ─────────────────────────────────────────────────────

    /// <summary>Zugtexte mit Absicht: gleiche Hauptvariante bei anderem Zugtext (Kommentar, Variante), ohne Züge
    /// (Schlüssel = Zugtext) und ganz leer.</summary>
    private static readonly string[] MovePool =
    [
        "1. e4 e5 2. Nf3 *",
        "1. e4 e6 2. d3 *",
        "1. e4 e6 2. Qe2 *",
        "1. d4 d5 *",
        "1. d4 {Kommentar} d5 *",
        "1. d4 d5 (1... Nf6) *",
        "*",
        "",
    ];

    /// <summary>Gleicher Zugtext unter anderer Startstellung = andere Linie (Schlüssel enthält die FEN).</summary>
    private static readonly string?[] FenPool = [null, null, null, "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1"];

    private static string RandomGame(Random r, string[] oids)
    {
        var sb = new StringBuilder("[Event \"C\"]\n");
        sb.Append($"[White \"L{r.Next(4)}\"]\n[Black \"T\"]\n[Result \"*\"]\n");
        var fen = FenPool[r.Next(FenPool.Length)];
        if (fen != null) sb.Append($"[SetUp \"1\"]\n[FEN \"{fen}\"]\n");
        if (r.Next(3) > 0) sb.Append($"[ChessableOid \"{oids[r.Next(oids.Length)]}\"]\n");
        if (r.Next(10) == 0) sb.Append("[RookHubHidden \"alt\"]\n");
        sb.Append(r.Next(2) == 0 ? "\n" : "                        \n");   // piratechess-Trennzeile aus Leerzeichen
        sb.Append(MovePool[r.Next(MovePool.Length)]).Append("\n\n");
        return sb.ToString();
    }

    private static (string Pgn, Dictionary<string, string> Truth) RandomCorpus(int seed, int maxGames)
    {
        var r = new Random(seed);
        var oids = Enumerable.Range(1, 1 + r.Next(6)).Select(i => (100 + i).ToString()).ToArray();
        var sb = new StringBuilder(r.Next(4) == 0 ? "; Vorspann\n\n" : "");
        var count = 2 + r.Next(maxGames - 1);
        for (var i = 0; i < count; i++) sb.Append(RandomGame(r, oids));

        var truth = new Dictionary<string, string>();
        foreach (var oid in oids)
            switch (r.Next(4))
            {
                case 0: truth[oid] = RandomGame(r, oids); break;   // Wahrheit mit zufälligem Inhalt (passt oft, oft nicht)
                case 1: truth[oid] = "   "; break;                 // leer = keine Wahrheit
            }
        return (sb.ToString(), truth);
    }

    [Fact]
    public void Repair_RandomCorpora_DecidesExactlyLikeTheOldVersion()
    {
        var changed = 0;
        var seen = new HashSet<string>();
        for (var seed = 0; seed < 4000; seed++)
        {
            var (pgn, truth) = RandomCorpus(seed, seed % 10 == 0 ? 120 : 25);
            changed += AssertSameAsLegacy(pgn, truth, $"seed {seed}", seen);
        }
        // Der Korpus muss die Regeln auch auslösen, sonst beweist der Vergleich nichts: jeder Zweig von Regel 1
        // (Wahrheit, Ausschlussregel, früheste Partie; mit und ohne Zwilling) und Regel 2 kommt vor.
        Assert.True(changed > 1000, $"nur {changed} Korpora mit Aktionen");
        foreach (var branch in new[]
                 {
                     "ausgeblendet|laut Linien-Cache", "oid entfernt|laut Linien-Cache",
                     "ausgeblendet|Inhalt der übrigen", "oid entfernt|Inhalt der übrigen",
                     "ausgeblendet|früheste Partie", "oid entfernt|früheste Partie",
                     "ausgeblendet|Kopie von Partie", "oid übernommen|von ihrer Kopie",
                 })
            Assert.Contains(branch, seen);
    }

    /// <summary>Die Formen des Laufzeit-Tests in kleiner Größe — dort lässt sich auch die alte Fassung noch rechnen.</summary>
    [Theory]
    [InlineData(nameof(ManyCarriersDistinctMoves))]
    [InlineData(nameof(TwinCascade))]
    [InlineData(nameof(ManyAmbiguousOids))]
    public void Repair_AdversarialShapes_DecideExactlyLikeTheOldVersion(string shape)
    {
        var pgn = Shape(shape, 3000);
        Assert.True(AssertSameAsLegacy(pgn, NoTruth, shape) > 0);
    }

    // ── Laufzeit: linear statt quadratisch ───────────────────────────────────────────────────────

    /// <summary>Gemessen am 30.09. auf dem Entwicklungsrechner: alte Fassung 61–77 s je Form bei 40 000 Partien, neue
    /// rund 0,5 s. Die Grenze liegt weit unter dem alten und weit über dem neuen Wert.</summary>
    [Theory]
    [InlineData(nameof(ManyCarriersDistinctMoves), 20000)]
    [InlineData(nameof(TwinCascade), 40000)]
    [InlineData(nameof(ManyAmbiguousOids), 20000)]
    public void Repair_FortyThousandGames_RunsInLinearTime(string shape, int expectedActions)
    {
        var pgn = Shape(shape, 40000);
        var clock = Stopwatch.StartNew();
        var result = RepertoirePgnCleanup.Repair(pgn, NoTruth);
        clock.Stop();

        Assert.Equal(expectedActions, result.Actions.Count);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(4), $"{shape}: {clock.Elapsed.TotalSeconds:F1} s");
    }

    // ── Formen ───────────────────────────────────────────────────────────────────────────────────

    private static string Shape(string shape, int n) => shape switch
    {
        nameof(ManyCarriersDistinctMoves) => ManyCarriersDistinctMoves(n),
        nameof(TwinCascade) => TwinCascade(n),
        nameof(ManyAmbiguousOids) => ManyAmbiguousOids(n),
        _ => throw new ArgumentOutOfRangeException(nameof(shape)),
    };

    /// <summary>n Partien unter EINER oid, jede mit eigenem Zugtext, zwei Linien, keine Wahrheit: die Ausschlussregel
    /// lief je Träger über alle Partien, die Zwillingssuche für jede zweite noch einmal. Ergebnis: n/2 „oid entfernt".</summary>
    private static string ManyCarriersDistinctMoves(int n)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < n; i++)
            sb.Append($"[Event \"C\"]\n[White \"L{i}\"]\n[ChessableOid \"7\"]\n\n1. {(i % 2 == 0 ? "e4" : "d4")} {{c{i}}} *\n\n");
        return sb.ToString();
    }

    /// <summary>n Kopien desselben Zugtexts unter oid 7, dazu EINE andere Linie unter 7 und ganz hinten derselbe Zugtext
    /// unter der eindeutigen oid 8: jede Kopie verliert die oid und wird ausgeblendet, die Zwillingssuche lief jedes Mal
    /// von vorn über die schon ausgeblendeten. Ergebnis: n „ausgeblendet".</summary>
    private static string TwinCascade(int n)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < n; i++)
            sb.Append($"[Event \"C\"]\n[White \"Kopie {i}\"]\n[ChessableOid \"7\"]\n\n1. e4 e5 *\n\n");
        sb.Append("[Event \"C\"]\n[White \"Echt\"]\n[ChessableOid \"7\"]\n\n1. d4 d5 *\n\n");
        sb.Append("[Event \"C\"]\n[White \"Original\"]\n[ChessableOid \"8\"]\n\n1. e4 e5 *\n\n");
        return sb.ToString();
    }

    /// <summary>n/2 mehrdeutige oids mit je zwei Trägern: vorher ein Lauf über alle Partien JE oid. Ergebnis: n/2
    /// „oid entfernt" (je oid bleibt die frühere Partie).</summary>
    private static string ManyAmbiguousOids(int n)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < n; i++)
            sb.Append($"[Event \"C\"]\n[White \"L{i}\"]\n[ChessableOid \"{i / 2}\"]\n\n1. {(i % 2 == 0 ? "e4" : "d4")} {{c{i}}} *\n\n");
        return sb.ToString();
    }

    // ── Hilfen ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>Vergleicht Aktionen (Reihenfolge, alle Felder) und PGN; Rückgabe: 1, wenn es Aktionen gab.
    /// <paramref name="branches"/> sammelt, welche Zweige vorkamen („Aktion|Begründung").</summary>
    private static int AssertSameAsLegacy(string pgn, IReadOnlyDictionary<string, string> truth, string label,
        HashSet<string>? branches = null)
    {
        var expected = LegacyRepertoirePgnCleanup.Repair(pgn, truth);
        var actual = RepertoirePgnCleanup.Repair(pgn, truth);
        var expectedActions = expected.Actions.Select(a => (a.Game, a.Line, a.Action, a.Oid, a.Detail)).ToList();
        var actualActions = actual.Actions.Select(a => (a.Game, a.Line, a.Action, a.Oid, a.Detail)).ToList();
        Assert.True(expectedActions.SequenceEqual(actualActions),
            $"{label}: Aktionen weichen ab\nalt: {string.Join(" | ", expectedActions)}\nneu: {string.Join(" | ", actualActions)}");
        Assert.True(expected.Pgn == actual.Pgn, $"{label}: PGN weicht ab");
        if (branches != null)
            foreach (var a in expected.Actions)
                foreach (var basis in new[] { "laut Linien-Cache", "Inhalt der übrigen", "früheste Partie", "Kopie von Partie", "von ihrer Kopie" })
                    if (a.Detail.Contains(basis)) branches.Add($"{a.Action}|{basis}");
        return expectedActions.Count > 0 ? 1 : 0;
    }
}
