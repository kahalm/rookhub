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

    /// <summary>Kopien, die es nur an GENAU diesen Stellen geben darf (Datei relativ zu Services/).</summary>
    [Theory]
    // GameEvals behält seine Fassung bewusst (fehlendes Feld = Schwarz, gekoppelt mit BrokerCandidates).
    [InlineData("static bool WhiteToMove(", "FenFields.cs,GameEvals.cs")]
    // Die eine UCI-Schreibweise — Partie-Analyse und Linien-Abgleich MÜSSEN zeichengleich sein.
    [InlineData("OriginalPosition.ToString() +", "PgnParser.cs")]
    public void NoPrivateCopiesLeft(string needle, string allowedCsv)
    {
        var allowed = allowedCsv.Split(',').ToHashSet(StringComparer.Ordinal);
        var services = Path.Combine(ApiRoot(), "Services");
        var hits = Directory.EnumerateFiles(services, "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains(needle, StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(services, f).Replace('\\', '/'))
            .Where(f => !allowed.Contains(f))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();
        Assert.True(hits.Count == 0, $"Kopie von „{needle}“ in: {string.Join(", ", hits)} — die gemeinsame Funktion benutzen.");
    }

    private static string ApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "api", "RookHub.Api");
    }
}
