using RookHub.Api.Services;
using RookHub.Api.Services.ChessBase;
using Xunit.Abstractions;

namespace RookHub.Api.Tests;

/// <summary>
/// Echte ChessBase-Datenbanken gegen ihren PGN-Export aus ChessBase (0.598.0). Die Dateien liegen NICHT im Repo (eigene
/// Partien mit Namen), deshalb nur mit <c>CHESSBASE_FIXTURES</c> = Ordner: darin je Datenbank die Dateien und ein PGN
/// gleichen Inhalts (<c>&lt;name&gt;.pgn</c> oder ein einzelnes PGN für alle). Ohne die Variable wird übersprungen.
/// Verglichen wird je Partie die HAUPTVARIANTE als UCI (nach demselben Parser wie der Import) und die Kopfdaten.
/// </summary>
public class ChessBaseFixtureTests(ITestOutputHelper output)
{
    private static string? Dir => Environment.GetEnvironmentVariable("CHESSBASE_FIXTURES");

    [Fact]
    public void Fixtures_MatchTheirPgnExport()
    {
        if (string.IsNullOrEmpty(Dir) || !Directory.Exists(Dir)) return;
        var files = Directory.GetFiles(Dir).Select(f => (Path.GetFileName(f), File.ReadAllBytes(f))).ToList();
        var pgns = Directory.GetFiles(Dir, "*.pgn");
        var bases = files.Select(f => f.Item1).Where(n => n.EndsWith(".cbh", StringComparison.OrdinalIgnoreCase)
                                                           || n.EndsWith(".2cbh", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.NotEmpty(bases);
        foreach (var head in bases)
        {
            var name = head[..head.LastIndexOf('.')];
            var db = ChessBaseFiles.FromFiles(files.Where(f => f.Item1.StartsWith(name + ".", StringComparison.OrdinalIgnoreCase)));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var result = ChessBaseConverter.Convert(db, 100_000);
            watch.Stop();
            var ours = PgnParser.SplitGames(result.Pgn).ToList();
            output.WriteLine($"{head}: {result.Games.Count} Partien, {result.Converted} gelesen, {result.Deleted} gelöscht, {result.Texts} Texte, {watch.ElapsedMilliseconds} ms");
            foreach (var bad in result.Games.Where(g => g.Error != null).Take(20))
                output.WriteLine($"  #{bad.Id} {bad.White} – {bad.Black}: {bad.Error}");

            var reference = pgns.FirstOrDefault(p => Path.GetFileNameWithoutExtension(p).Equals(name, StringComparison.OrdinalIgnoreCase))
                            ?? (pgns.Length == 1 ? pgns[0] : null);
            if (reference == null) continue;
            var theirs = PgnParser.SplitGames(File.ReadAllText(reference)).ToList();
            output.WriteLine($"  Vergleich mit {Path.GetFileName(reference)}: {theirs.Count} Partien");
            output.WriteLine($"  {Compare(ours, theirs)} Abweichungen");
        }
        // Dieselbe Datenbank in beiden Formaten (Morphys World-ch + wch2): die Partien müssen gleich herauskommen.
        var converted = bases.Select(h => (h, PgnParser.SplitGames(ChessBaseConverter.Convert(
            ChessBaseFiles.FromFiles(files.Where(f => f.Item1.StartsWith(h[..h.LastIndexOf('.')] + ".", StringComparison.OrdinalIgnoreCase))), 100_000).Pgn).ToList())).ToList();
        var v1 = converted.Where(c => c.h.EndsWith(".cbh", StringComparison.OrdinalIgnoreCase)).ToList();
        var v2 = converted.Where(c => c.h.EndsWith(".2cbh", StringComparison.OrdinalIgnoreCase)).ToList();
        if (v1.Count == 1 && v2.Count == 1 && pgns.Length == 0)
            output.WriteLine($"{v1[0].h} gegen {v2[0].h}: {Compare(v1[0].Item2, v2[0].Item2)} Abweichungen");
    }

    private static string Tag(Dictionary<string, string> h, string k) => h.TryGetValue(k, out var v) ? v.Trim() : "";

    /// <summary>Ordnet über Weiß + Schwarz + Datum zu (gelöschte Partien fehlen in einer der Listen) und zählt Abweichungen.</summary>
    private int Compare(List<(Dictionary<string, string> Headers, string MoveText)> ours,
        List<(Dictionary<string, string> Headers, string MoveText)> theirs)
    {
        string Key((Dictionary<string, string> Headers, string MoveText) g) =>
            string.Join('|', Tag(g.Headers, "White").Replace("\t", ""), Tag(g.Headers, "Black").Replace("\t", ""), Tag(g.Headers, "Date"));
        var mismatches = 0;
        var j = 0;
        foreach (var a in ours)
        {
            var k = theirs.FindIndex(j, t => Key(t) == Key(a));
            if (k < 0) { mismatches++; output.WriteLine($"  ohne Gegenstück: {Key(a)}"); continue; }
            for (var skipped = j; skipped < k; skipped++) output.WriteLine($"  nur in der Vorlage: {Key(theirs[skipped])}");
            j = k + 1;
            var b = theirs[k];
            {
                var fenA = Tag(a.Headers, "FEN") is { Length: > 0 } fa ? fa : MainlineBoard.StandardFen;
                var fenB = Tag(b.Headers, "FEN") is { Length: > 0 } fb ? fb : MainlineBoard.StandardFen;
                var ma = PgnParser.TryExtractUciMainline(fenA, a.MoveText) ?? new();
                var mb = PgnParser.TryExtractUciMainline(fenB, b.MoveText) ?? new();
                var diffs = new List<string>();
                foreach (var t in new[] { "White", "Black", "Event", "Site", "Date", "Round", "Result", "ECO", "WhiteElo", "BlackElo" })
                    if (Tag(a.Headers, t).Replace("\t", "") != Tag(b.Headers, t).Replace("\t", ""))
                        diffs.Add($"{t}: „{Tag(a.Headers, t)}“ ≠ „{Tag(b.Headers, t)}“");
                if (!ma.SequenceEqual(mb))
                {
                    var at = ma.Zip(mb).TakeWhile(p => p.First == p.Second).Count();
                    diffs.Add($"Züge ab Halbzug {at + 1}: {string.Join(' ', ma.Skip(at).Take(3))} ≠ {string.Join(' ', mb.Skip(at).Take(3))} ({ma.Count}/{mb.Count})");
                }
                if (diffs.Count == 0) continue;
                mismatches++;
                if (mismatches <= 15 || diffs.Any(d => d.StartsWith("Züge"))) output.WriteLine($"  {Key(b)}: {string.Join("; ", diffs)}");
            }
        }
        return mismatches;
    }
}
