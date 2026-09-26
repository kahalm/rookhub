using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Meisterkommentare zur Stellung eines Fehlers (0.542.0): nur über dieselbe Zugfolge, die treffendste Regel
/// gewinnt, der Text wird zu Prosa aufgeräumt.</summary>
public class MasterCommentsTests : IDisposable
{
    private readonly AppDbContext _db;

    public MasterCommentsTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    // Schäfermatt: 3…Sf6?? (Halbzug 5) ist der Fehler.
    private static readonly (string Fen, string Uci, string San, string Cands)[] Plies =
    {
        ("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1", "e2e4", "e4", """[{"uci":"e2e4","cp":30}]"""),
        ("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1", "e7e5", "e5", """[{"uci":"e7e5","cp":-30}]"""),
        ("rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2", "d1h5", "Qh5", """[{"uci":"g1f3","cp":40},{"uci":"d1h5","cp":20}]"""),
        ("rnbqkbnr/pppp1ppp/8/4p2Q/4P3/8/PPPP1PPP/RNB1KBNR b KQkq - 1 2", "b8c6", "Nc6", """[{"uci":"b8c6","cp":-20}]"""),
        ("r1bqkbnr/pppp1ppp/2n5/4p2Q/4P3/8/PPPP1PPP/RNB1KBNR w KQkq - 2 3", "f1c4", "Bc4", """[{"uci":"f1c4","cp":20}]"""),
        ("r1bqkbnr/pppp1ppp/2n5/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR b KQkq - 3 3", "g8f6", "Nf6", """[{"uci":"g7g6","cp":-20,"pv":["g7g6","h5f3","g8f6"]},{"uci":"g8f6","mate":-1}]"""),
        ("r1bqkb1r/pppp1ppp/2n2n2/4p2Q/2B1P3/8/PPPP1PPP/RNB1K1NR w KQkq - 4 4", "h5f7", "Qxf7#", """[{"uci":"h5f7","mate":1,"pv":["h5f7"]}]"""),
    };

    private static List<GameAnalysisPosition> Positions(string? firstFen = null) => Plies.Select((p, i) => new GameAnalysisPosition
    {
        Ply = i, Fen = i == 0 && firstFen != null ? firstFen : p.Fen, GameMoveUci = p.Uci, GameMoveSan = p.San,
        CandidatesJson = p.Cands, Depth = 20,
    }).ToList();

    private static GameMistakes.Flaw Flaw() => GameMistakes.Find(Positions(), Plies.Length).Single();

    private async Task<LibraryGame> LibraryAsync(string opening, string moveText, int score, string lang = "en",
        LibraryGameStatus status = LibraryGameStatus.New, int commentedPlies = 1)
    {
        var g = new LibraryGame
        {
            OpeningLine = opening, Pgn = "[White \"Meister\"]\n[Black \"Gegner\"]\n[Result \"*\"]\n\n" + moveText + " *",
            White = "Meister " + score, Black = "Gegner", Event = "Turnier", PlayedOn = new DateOnly(1985, 3, 1),
            Annotator = "Kommentator", Score = score, CommentedPlies = commentedPlies, Languages = lang, Status = status,
        };
        _db.LibraryGames.Add(g);
        await _db.SaveChangesAsync();
        return g;
    }

    [Fact]
    public async Task SameMoveInAMasterGame_WithTheAnnotatorsVerdict_Wins()
    {
        await LibraryAsync("e4 e5 Qh5 Nc6 Bc4 g6", "1. e4 e5 2. Qh5 Nc6 3. Bc4 g6 {The only move: g6 covers f7 and h5 at once.}", 90);
        var same = await LibraryAsync("e4 e5 Qh5 Nc6 Bc4 Nf6 Qxf7",
            "1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 {A beginner's blunder: queen and bishop both hit f7, and nothing covers it.} 4. Qxf7#", 40);

        var found = await MasterComments.ForFlawsAsync(_db, Positions(), new[] { Flaw() });

        var hit = found[5];
        Assert.Equal((same.Id, 1), (hit.LibraryGameId, hit.Rank));
        Assert.Equal("3...Nf6: A beginner's blunder: queen and bishop both hit f7, and nothing covers it.", hit.Text);
        Assert.Equal("Meister 40 – Gegner, Turnier 1985 (annotated by Kommentator)", hit.Source);
    }

    [Theory]
    // Der Kommentar zum Zug an dieser Stelle nennt den gespielten Zug — hier als Variante, in deutscher Schreibweise.
    [InlineData("1. e4 e5 2. Qh5 Nc6 3. Bc4 Qe7 {Die solide Antwort; 3...Sf6?? scheitert am Matt auf f7.}", "e4 e5 Qh5 Nc6 Bc4 Qe7", "de", 2)]
    // Der Meister spielte den Bestzug der Engine.
    [InlineData("1. e4 e5 2. Qh5 Nc6 3. Bc4 g6 {The only move: g6 covers f7 and h5 at once.}", "e4 e5 Qh5 Nc6 Bc4 g6", "en", 3)]
    // Nur der Kommentar zur Stellung davor.
    [InlineData("1. e4 e5 2. Qh5 Nc6 3. Bc4 {White threatens mate on f7 with queen and bishop together.} Qe7", "e4 e5 Qh5 Nc6 Bc4 Qe7", "en", 4)]
    public async Task TheRules_InTheirOrder(string moveText, string opening, string lang, int rank)
    {
        await LibraryAsync(opening, moveText, 50, lang);

        var hit = (await MasterComments.ForFlawsAsync(_db, Positions(), new[] { Flaw() }))[5];

        Assert.Equal(rank, hit.Rank);
        if (rank == 4) Assert.StartsWith("3.Bc4: White threatens mate", hit.Text);
    }

    [Fact]
    public async Task Nothing_ForAnotherLine_AnotherStart_ARejectedGame_OrAGameWithoutComments()
    {
        await LibraryAsync("e4 c5 Nf3 d6", "1. e4 c5 2. Nf3 d6 {Sicilian.}", 99);
        await LibraryAsync("e4 e5 Qh5 Nc6 Bc4 Nf6", "1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 {Aussortiert.}", 98, status: LibraryGameStatus.Rejected);
        await LibraryAsync("e4 e5 Qh5 Nc6 Bc4 Nf6", "1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 4. Qxf7#", 97, commentedPlies: 0);

        Assert.Empty(await MasterComments.ForFlawsAsync(_db, Positions(), new[] { Flaw() }));

        // Aus einer anderen Ausgangsstellung gibt es keine vergleichbare Zugfolge.
        await LibraryAsync("e4 e5 Qh5 Nc6 Bc4 Nf6", "1. e4 e5 2. Qh5 Nc6 3. Bc4 Nf6 {Blunder: f7 falls at once to queen and bishop.}", 50);
        Assert.Empty(await MasterComments.ForFlawsAsync(_db,
            Positions("rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBN1 w Qkq - 0 1"), new[] { Flaw() }));
        Assert.NotEmpty(await MasterComments.ForFlawsAsync(_db, Positions(), new[] { Flaw() }));
    }

    [Fact]
    public void Clean_KeepsTheProse_ShortensLines_DropsMarkupAndSymbolGlyphs()
    {
        var text = MasterComments.Clean(
            "[%evp 20,30] Risky is the greedy 5... Qc7 6. Nb5 Qb8 7. d4 cxd4 and Black loses [#] more or less by force. עe8", "en");

        Assert.Equal("Risky is the greedy 5... Qc7 6. … and Black loses more or less by force. e8", text);
        Assert.Null(MasterComments.Clean("12. Nf3 Nc6 13. Bb5 a6 14. Ba4 !?", "en"));   // nur Züge: nichts zu erklären
        Assert.Null(MasterComments.Clean("Blunder.", "en"));                           // zu wenig Prosa
    }

    [Theory]
    [InlineData("Nach 3...Sf6?? folgt Matt.", "Nf6", "de", true)]
    [InlineData("After 3...Nf6?? White mates.", "Nf6", "en", true)]
    [InlineData("The knight on f6 is fine.", "Nf6", "en", false)]
    [InlineData("Qxf7# ends it.", "Qxf7#", "en", true)]
    public void Mentions_InEnglishOrInTheCommentsOwnLetters(string text, string san, string lang, bool expected)
        => Assert.Equal(expected, MasterComments.Mentions(text, san, lang));
}
