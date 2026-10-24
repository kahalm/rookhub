using Chess;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die gemeinsamen Schach-Kleinteile der Dienste (Codereview 2026-09-29, N11-006): „wer ist am Zug"
/// (<see cref="FenFields.WhiteToMove"/>) und „Zug als UCI" (<see cref="PgnParser.ToUci"/>). Beide
/// lagen als private Kopien in mehreren Diensten — die Literal-Tests halten die Regel fest, die
/// Quelltext-Wache verhindert, dass wieder eine Kopie entsteht.
/// </summary>
public class SharedChessHelpersTests
{
    [Theory]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", true)]
    [InlineData("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", false)]
    [InlineData("8/8/8/8/8/8/8/K6k", true)]   // Feld fehlt → Weiß (anders als GameEvals, siehe FenFields)
    [InlineData("", true)]
    [InlineData("8/8/8/8/8/8/8/K6k  b - - 0 1", false)]   // doppeltes Leerzeichen (Chessable-Köpfe)
    [InlineData(" 8/8/8/8/8/8/8/K6k b", false)]
    [InlineData("8/8/8/8/8/8/8/K6k B - - 0 1", false)]   // Großschreibung wie bisher ChessableTrainingStart
    [InlineData("8/8/8/8/8/8/8/K6k - - - 0 1", true)]    // kein w/b → Weiß
    public void WhiteToMove_OnlyBMeansBlack(string fen, bool expected)
        => Assert.Equal(expected, FenFields.WhiteToMove(fen));

    [Theory]
    [InlineData("8/P7/8/8/8/8/8/K6k w - - 0 1", "a8=Q", "a7a8q")]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1", "O-O", "e1g1")]
    [InlineData("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "Nf3", "g1f3")]
    public void ToUci_PromotionAndCastling(string fen, string san, string uci)
    {
        var board = ChessBoard.LoadFromFen(fen);
        Assert.True(board.Move(san));
        Assert.Equal(uci, PgnParser.ToUci(board.ExecutedMoves[^1]));
    }

    /// <summary>
    /// Kopien, die es nur an GENAU diesen Stellen geben darf (Datei relativ zur Repo-Wurzel). Die
    /// Wache sucht im ganzen API-Projekt (Controller, Modelle, Dienste …) und in <c>tools/</c> —
    /// nicht nur unter Services/, sonst wäre eine neue Kopie in einem Controller unbemerkt.
    /// </summary>
    [Theory]
    // GameEvals behält seine Fassung bewusst (fehlendes Feld = Schwarz, gekoppelt mit BrokerCandidates).
    [InlineData("static bool WhiteToMove(",
        "src/api/RookHub.Api/Services/FenFields.cs,src/api/RookHub.Api/Services/Games/GameEvals.cs")]
    // Die eine UCI-Schreibweise — Partie-Analyse und Linien-Abgleich MÜSSEN zeichengleich sein.
    [InlineData("OriginalPosition.ToString() +", "src/api/RookHub.Api/Services/PgnParser.cs")]
    public void NoPrivateCopiesLeft(string needle, string allowedCsv)
    {
        var allowed = allowedCsv.Split(',').ToHashSet(StringComparer.Ordinal);
        var repo = RepoRoot();
        var roots = new[] { Path.Combine(repo, "src", "api", "RookHub.Api"), Path.Combine(repo, "tools") };
        var hits = roots
            .Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories))
            .Select(f => (Full: f, Rel: Path.GetRelativePath(repo, f).Replace('\\', '/')))
            .Where(f => !f.Rel.Contains("/bin/", StringComparison.Ordinal) && !f.Rel.Contains("/obj/", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f.Full).Contains(needle, StringComparison.Ordinal))
            .Select(f => f.Rel)
            .Where(f => !allowed.Contains(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.True(hits.Count == 0, $"Kopie von „{needle}“ in: {string.Join(", ", hits)} — die gemeinsame Funktion benutzen.");
    }

    /// <summary>
    /// Das Zugfeld (FEN-Feld 2) wird nirgends mehr von Hand verglichen (N11-006): weder
    /// <c>parts[1] == "w"</c>/<c>!= "b"</c> noch <c>ElementAtOrDefault(1) == "b"</c> noch
    /// <c>parts[1].Equals("b", …)</c>. Die Kopien unterschieden sich ausgerechnet am Rand (fehlendes Feld =
    /// Weiß oder Schwarz, Groß-/Kleinschreibung, doppelte Leerzeichen). Ein Seitenwechsel
    /// (<c>parts[1] == "w" ? "b" : "w"</c>) liest die Seite nicht und bleibt erlaubt.
    /// </summary>
    [Fact]
    public void NoInlineSideToMove()
    {
        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "src/api/RookHub.Api/Services/FenFields.cs",
            // fehlendes Feld = Schwarz, nur gemeinsam umzustellen (siehe FenFields)
            "src/api/RookHub.Api/Services/Games/GameEvals.cs",
            "src/api/RookHub.Api/Services/Engines/BrokerCandidates.cs",
            // Zerleger, der das Feld vorher auf genau w/b prüft und sonst null liefert
            "src/api/RookHub.Api/Services/Engines/ExplorerPathFinder.cs",
        };
        var pattern = new System.Text.RegularExpressions.Regex(
            @"(\[1\]|ElementAtOrDefault\(1\))\s*(==|!=)\s*""[wbWB]""(?!\s*\?\s*""[wb]""\s*:)|\[1\]\.Equals\(""[wbWB]""");
        var repo = RepoRoot();
        var roots = new[] { Path.Combine(repo, "src", "api", "RookHub.Api"), Path.Combine(repo, "tools") };
        var hits = roots
            .Where(Directory.Exists)
            .SelectMany(r => Directory.EnumerateFiles(r, "*.cs", SearchOption.AllDirectories))
            .Select(f => (Full: f, Rel: Path.GetRelativePath(repo, f).Replace('\\', '/')))
            .Where(f => !f.Rel.Contains("/bin/", StringComparison.Ordinal) && !f.Rel.Contains("/obj/", StringComparison.Ordinal)
                        && !f.Rel.Contains("/Migrations/", StringComparison.Ordinal) && !allowed.Contains(f.Rel))
            .SelectMany(f => File.ReadLines(f.Full).Select((line, i) => (f.Rel, Line: i + 1, Text: line)))
            .Where(l => pattern.IsMatch(l.Text))
            .Select(l => $"{l.Rel}:{l.Line}")
            .ToList();
        Assert.True(hits.Count == 0, $"Zugfeld von Hand gelesen in: {string.Join(", ", hits)} — FenFields.WhiteToMove benutzen.");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
