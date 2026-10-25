using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Scoresheet;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>
/// Golden-Test des PGN-Baus gespeicherter Partien über alle Wege, die ihn benutzen (vom Server angelegt, PGN-Upload,
/// Korrigieren, Vereinspartie in die Kopien tragen, Import-Kennung) — zeichengenau, wie vor der Zerlegung Z13. Das PGN
/// liegt in der Datenbank und hängt am öffentlichen Teilen-Link.
/// </summary>
public class SavedGamePgnGoldenTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SavedGameService _service;

    public SavedGamePgnGoldenTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _service = TestServices.SavedGames(_db);
    }

    public void Dispose() => _db.Dispose();

    private const string ImportText = """
        [Event "Landesliga \"A\""]
        [Site "Schwaz"]
        [Date "2026.03.14"]
        [Round "5.2"]
        [White "Muster, Max"]
        [Black "Beispiel, Bea"]
        [Result "1/2-1/2"]
        [WhiteElo "1850"]
        [BlackElo "1790"]
        [TimeControl "5400+30"]
        [ECO "C65"]
        [Annotator ""]

        1. e4 {Königsbauer} e5 2. Nf3 Nc6 (2... d6 3. d4) 3. Bb5 Nf6 $1 4. O-O {Berliner} 1/2-1/2

        [Event "?"]
        [Site "?"]
        [Date "????.??.??"]
        [Round "?"]
        [White "Weiß"]
        [Black "Schwarz"]
        [Result "0-1"]
        [SetUp "1"]
        [FEN "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3"]

        3. Bc4 Bc5 4. c3 {Giuoco} Nf6 0-1
        """;

    [Fact]
    public async Task CreateGenerated_FullHeaderAndComments_Golden()
    {
        var g = await _service.CreateGeneratedAsync(1, SavedGameService.ScoresheetSource, new[] { "e4", "e5", "Nf3", "Nc6", "Bb5" },
            new Dictionary<int, string> { [1] = "sheet: e6", [4] = "sheet, not resolved: a6" },
            new GameHeaderInput("Vereinsmeisterschaft", "Schwaz", "5.6.2026", "3", "Max \"Mad\" Muster", " Bea\nBeispiel ", "1-0"));
        Assert.Equal(N(GoldenGenerated), g.Pgn);
    }

    [Fact]
    public async Task CreateGenerated_EmptyHeader_Golden()
    {
        var g = await _service.CreateGeneratedAsync(1, "scoresheet", new[] { "d4", "d5" }, null,
            new GameHeaderInput(null, " ", "irgendwann", null, null, null, "2-0"));
        Assert.Equal(N(GoldenGeneratedEmpty), g.Pgn);
    }

    [Fact]
    public async Task ImportPgn_TwoGames_Golden()
    {
        var r = await _service.ImportPgnAsync(1, ImportText.Replace("\r\n", "\n"));
        Assert.Equal(2, r.Imported);
        var pgns = await _db.SavedGames.OrderBy(g => g.Id).Select(g => g.Pgn).ToListAsync();
        Assert.Equal(N(GoldenImport1), pgns[0]);
        Assert.Equal(N(GoldenImport2), pgns[1]);
        var ids = await _db.SavedGames.OrderBy(g => g.Id).Select(g => g.ExternalId).ToListAsync();
        Assert.Equal(new[] { GoldenImportKey1, GoldenImportKey2 }, ids);
    }

    [Fact]
    public async Task Update_KeepsUneditedHeaders_Golden()
    {
        await _service.ImportPgnAsync(1, ImportText.Replace("\r\n", "\n"));
        var id = await _db.SavedGames.OrderBy(g => g.Id).Select(g => g.Id).FirstAsync();
        await _service.UpdateAsync(1, id, new GameUpdateDto
        {
            Moves = new()
            {
                new GameMoveInputDto { San = "e4" }, new GameMoveInputDto { San = "c5", Comment = " Sizilianisch " },
                new GameMoveInputDto { San = "Nf3" }, new GameMoveInputDto { San = "d6" },
            },
            White = "Muster, Max", Black = "Neu \"Nina\"", Result = "0-1", Event = "Korrigiert", Site = null,
            Round = "6", Date = "2026-06-05",
        });
        Assert.Equal(N(GoldenUpdated), (await _db.SavedGames.AsNoTracking().FirstAsync(g => g.Id == id)).Pgn);
    }

    [Fact]
    public async Task ApplyClubMoves_KeepsPartialDate_Golden()
    {
        _db.SavedGames.Add(new SavedGame
        {
            Id = 70, UserId = 1, Source = "pgn", ShareToken = "t70", LeagueClubGameId = 9, Result = "1-0", White = "A", Black = "B",
            Pgn = "[Event \"Liga\"]\n[Site \"?\"]\n[Date \"2026.??.??\"]\n[Round \"?\"]\n[White \"A\"]\n[Black \"B\"]\n[Result \"1-0\"]\n[WhiteElo \"2000\"]\n\n1. e4 e5 1-0",
        });
        await _db.SaveChangesAsync();
        Assert.Equal(1, await SavedGameService.ApplyClubMovesAsync(_db, 9, new[] { "d4", "Nf6", "c4" }, null));
        Assert.Equal(N(GoldenClubMoves), (await _db.SavedGames.AsNoTracking().FirstAsync(g => g.Id == 70)).Pgn);
    }

    /// <summary>PGN-Upload: maskierte Werte (<c>\"</c>, <c>\\</c>) werden korrekt wieder maskiert geschrieben (wie
    /// <c>PgnWriter.Escape</c>), die Spalten tragen den entmaskierten Namen.</summary>
    [Fact]
    public async Task ImportPgn_EscapedHeaderValues_StayEscaped()
    {
        const string text = "[White \"O\\\"Neil\"]\n[Black \"B\"]\n[Result \"1-0\"]\n[Annotator \"a\\\\b \\\"c\\\"\"]\n\n1. e4 e5 1-0\n";
        var r = await _service.ImportPgnAsync(1, text);
        Assert.Equal(1, r.Imported);
        var g = await _db.SavedGames.SingleAsync();
        Assert.Contains("[White \"O\\\"Neil\"]\n", g.Pgn);
        Assert.Contains("[Annotator \"a\\\\b \\\"c\\\"\"]\n", g.Pgn);
        Assert.Equal("O\"Neil", g.White);
    }

    /// <summary>Zeilenenden der Quelldatei (CRLF auf Windows) gehören nicht zum PGN.</summary>
    private static string N(string s) => s.Replace("\r\n", "\n");

    // ── Erwartete Ausgaben (Stand vor der Zerlegung Z13) ──
    private const string GoldenGenerated = """
        [Event "Vereinsmeisterschaft"]
        [Site "Schwaz"]
        [Date "2026.06.05"]
        [Round "3"]
        [White "Max 'Mad' Muster"]
        [Black "Bea Beispiel"]
        [Result "1-0"]

        1. e4 e5 {sheet: e6} 2. Nf3 Nc6 3. Bb5 {sheet, not resolved: a6} 1-0
        """;

    private const string GoldenGeneratedEmpty = """
        [Event "?"]
        [Site "?"]
        [Date "????.??.??"]
        [Round "?"]
        [White "?"]
        [Black "?"]
        [Result "*"]

        1. d4 d5 *
        """;

    // Seit dem Fix (PGN-Escape beim Upload) bleibt ein maskiertes Anführungszeichen maskiert; vorher stand hier
    // "Landesliga \'A\'". Gespeicherte Partien behalten ihren Stand.
    private const string GoldenImport1 = """
        [Event "Landesliga \"A\""]
        [Site "Schwaz"]
        [Date "2026.03.14"]
        [Round "5.2"]
        [White "Muster, Max"]
        [Black "Beispiel, Bea"]
        [Result "1/2-1/2"]
        [WhiteElo "1850"]
        [BlackElo "1790"]
        [TimeControl "5400+30"]
        [ECO "C65"]

        1. e4 {Königsbauer} e5 2. Nf3 Nc6 3. Bb5 Nf6 4. O-O {Berliner} 1/2-1/2
        """;

    private const string GoldenImport2 = """
        [Event "?"]
        [Site "?"]
        [Date "????.??.??"]
        [Round "?"]
        [White "Weiß"]
        [Black "Schwarz"]
        [Result "0-1"]
        [SetUp "1"]
        [FEN "r1bqkbnr/pppp1ppp/2n5/4p3/4P3/5N2/PPPP1PPP/RNBQKB1R w KQkq - 2 3"]

        3. Bc4 Bc5 4. c3 {Giuoco} Nf6 0-1
        """;

    private const string GoldenImportKey1 = "pgn:0313a76f4aba0449f3e7db518f66d4a8eddcf0f7";

    private const string GoldenImportKey2 = "pgn:3357ffb9066de3885f532a31555a2edf108b0ff4";

    private const string GoldenUpdated = """
        [Event "Korrigiert"]
        [Site "?"]
        [Date "2026.06.05"]
        [Round "6"]
        [White "Muster, Max"]
        [Black "Neu 'Nina'"]
        [Result "0-1"]
        [WhiteElo "1850"]
        [BlackElo "1790"]
        [TimeControl "5400+30"]
        [ECO "C65"]

        1. e4 c5 {Sizilianisch} 2. Nf3 d6 0-1
        """;

    private const string GoldenClubMoves = """
        [Event "Liga"]
        [Site "?"]
        [Date "2026.??.??"]
        [Round "?"]
        [White "A"]
        [Black "B"]
        [Result "1-0"]
        [WhiteElo "2000"]

        1. d4 Nf6 2. c4 1-0
        """;
}
