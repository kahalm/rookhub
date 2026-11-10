using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>Laschen der Partienliste (<c>GET /api/games?kind=</c>): Online nach Quelle, Am Brett nach Verbindung, Formular
/// und PGN-Kopf, Zusammenspiel mit Suche, Deckel und Zählung. Die SQL-Übersetzung prüft <c>SavedGameKindsSqlTests</c>.</summary>
public class SavedGameKindsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SavedGameService _service;
    private int _next = 1;

    public SavedGameKindsTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = TestServices.SavedGames(_db);
    }

    public void Dispose() => _db.Dispose();

    private static string Pgn(string headers) => $"{headers}\n\n1. e4 e5 *\n";

    private async Task<int> AddAsync(string source, string pgn = "1. e4 e5 *\n", int? clubGameId = null, string? timeControl = null,
        int userId = 1, string? white = "Anna")
    {
        var id = _next++;
        _db.SavedGames.Add(new SavedGame
        {
            Id = id, UserId = userId, White = white, Black = "Bert", Source = source, Pgn = pgn, ShareToken = $"t{id}",
            MoveCount = 2, HeadersScanned = true, LeagueClubGameId = clubGameId, TimeControl = timeControl,
            CreatedAt = new DateTime(2026, 1, 1).AddDays(id),
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task<List<int>> ListAsync(string? kind, string? q = null, int take = 200, int userId = 1)
        => (await _service.ListAsync(userId, take, q, kind)).Select(g => g.Id).ToList();

    [Fact]
    public async Task List_Online_OnlyChessComAndLichess()
    {
        var com = await AddAsync("chess.com");
        var li = await AddAsync("lichess");
        await AddAsync("pgn", Pgn("[Site \"https://lichess.org/abc\"]")); // Quelle entscheidet, nicht der Kopf
        await AddAsync("rookhub");
        await AddAsync("scoresheet");
        Assert.Equal(new[] { li, com }, await ListAsync("online"));
    }

    [Fact]
    public async Task List_OnlineWithClubLink_StaysOnline_NotOtb()
    {
        var com = await AddAsync("chess.com", clubGameId: 7);
        Assert.Equal(new[] { com }, await ListAsync("online"));
        Assert.Empty(await ListAsync("otb"));
    }

    [Fact]
    public async Task List_Otb_LinkScoresheetAndBoardPgn()
    {
        var linkedPgn = await AddAsync("pgn", "1. d4 *\n", clubGameId: 3);   // Kopf egal, Verbindung reicht
        var sheet = await AddAsync("scoresheet");                           // ohne Verbindung
        var linkedSheet = await AddAsync("scoresheet", clubGameId: 4);
        var board = await AddAsync("pgn", Pgn("[Event \"TMM Landesliga 2026/27\"]\n[Site \"?\"]"));
        var siteOnly = await AddAsync("pgn", Pgn("[Event \"?\"]\n[Site \"Kufstein\"]"));
        var linkedRookhub = await AddAsync("rookhub", clubGameId: 5);
        await AddAsync("rookhub");
        await AddAsync("chess.com");
        Assert.Equal(new[] { linkedRookhub, siteOnly, board, linkedSheet, sheet, linkedPgn }, await ListAsync("otb"));
    }

    [Theory]
    [InlineData("[Event \"?\"]\n[Site \"?\"]\n[Round \"1\"]")]                                // nichts Benanntes
    [InlineData("[White \"A\"]\n[Black \"B\"]")]                                             // gar kein Event/Site
    [InlineData("[Event \"-\"]\n[Site \"\"]")]
    [InlineData("[Event \"rated bullet game\"]\n[Site \"https://lichess.org/game/AgKIX7a9\"]")]
    [InlineData("[Event \"Live Chess\"]\n[Site \"Chess.com\"]")]
    [InlineData("[Event \"Club match\"]\n[Site \"Wien\"]\n[Link \"https://www.chess.com/game/live/1\"]")]
    [InlineData("[Event \"iChessOne offline game\"]\n[Site \"?\"]")]
    [InlineData("[Event \"Titled Arena\"]\n[Site \"lichess.org\"]")]
    [InlineData("[Event \"Bundesliga Online 2021\"]\n[Site \"?\"]")]
    public async Task List_Otb_PgnWithoutBoardHint_IsNotOtb(string headers)
    {
        await AddAsync("pgn", Pgn(headers));
        Assert.Empty(await ListAsync("otb"));
        Assert.Single(await ListAsync("all"));
    }

    [Theory]
    [InlineData("180+2", false)]     // Blitz
    [InlineData("600+5", false)]     // Schnellschach
    [InlineData("1/86400", false)]   // Fernschach
    [InlineData("5400+30", true)]    // klassisch
    [InlineData("-", true)]          // unbekannt sagt nichts
    [InlineData("40/7200:3600", true)] // unverständlich sagt nichts
    [InlineData(null, true)]
    public async Task List_Otb_PgnTimeControl_MustBeClassicalWhenReadable(string? tc, bool otb)
    {
        var id = await AddAsync("pgn", Pgn("[Event \"Tegernsee Open 2025\"]"), timeControl: tc);
        Assert.Equal(otb ? new[] { id } : Array.Empty<int>(), await ListAsync("otb"));
    }

    [Fact]
    public async Task List_Otb_CrlfAndHeaderAfterOthers()
    {
        var id = await AddAsync("pgn", "[White \"x\"]\r\n[Black \"y\"]\r\n[Event \"Schwazer Stadtmeisterschaft 25\"]\r\n\r\n1. e4 *\r\n");
        Assert.Equal(new[] { id }, await ListAsync("otb"));
    }

    [Fact]
    public async Task List_Otb_EventInCommentAfterMoves_DoesNotCount()
    {
        await AddAsync("pgn", "[Event \"?\"]\n\n1. e4 {[Event \"Landesliga\"]} e5 *\n");
        Assert.Empty(await ListAsync("otb"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ALL")]
    [InlineData("bogus")]
    public async Task List_AllOrUnknownKind_ReturnsEverything(string? kind)
    {
        var a = await AddAsync("rookhub");
        var b = await AddAsync("chess.com");
        var c = await AddAsync("pgn", Pgn("[Event \"?\"]"));
        Assert.Equal(new[] { c, b, a }, await ListAsync(kind));
    }

    [Fact]
    public async Task List_KindIsCaseInsensitive()
    {
        var b = await AddAsync("lichess");
        await AddAsync("rookhub");
        Assert.Equal(new[] { b }, await ListAsync(" Online "));
    }

    [Fact]
    public async Task List_KindAndSearch_AreAnded()
    {
        var hitOnline = await AddAsync("chess.com", white: "Carlsen");
        await AddAsync("chess.com", white: "Anna");
        var hitOtb = await AddAsync("scoresheet", white: "Carlsen");
        Assert.Equal(new[] { hitOnline }, await ListAsync("online", "carlsen"));
        Assert.Equal(new[] { hitOtb }, await ListAsync("otb", "carlsen"));
        Assert.Equal(new[] { hitOtb, hitOnline }, await ListAsync("all", "carlsen"));
    }

    [Fact]
    public async Task List_Kind_TakeCapsWithinTheTab()
    {
        var ids = new List<int>();
        for (var i = 0; i < 3; i++) ids.Add(await AddAsync("lichess"));
        for (var i = 0; i < 3; i++) await AddAsync("rookhub"); // jünger, dürfen den Deckel nicht verbrauchen
        Assert.Equal(new[] { ids[2], ids[1] }, await ListAsync("online", take: 2));
    }

    [Fact]
    public async Task List_Kind_OnlyOwnGames()
    {
        await AddAsync("scoresheet", userId: 2);
        await AddAsync("chess.com", userId: 2);
        Assert.Empty(await ListAsync("otb"));
        Assert.Empty(await ListAsync("online"));
    }

    [Fact]
    public async Task List_Otb_OtherUsersBlitzTimeControl_DoesNotLeak()
    {
        // Die Ausschlussliste der Bedenkzeiten entsteht aus den Partien DIESES Kontos; ein fremdes Blitz stört nicht.
        await AddAsync("pgn", Pgn("[Event \"Blitz\"]"), timeControl: "180+2", userId: 2);
        var mine = await AddAsync("pgn", Pgn("[Event \"Open\"]"), timeControl: "5400+30");
        Assert.Equal(new[] { mine }, await ListAsync("otb"));
    }

    [Fact]
    public async Task KindCounts_CountsEachTab_IgnoresOtherUsers()
    {
        await AddAsync("chess.com");
        await AddAsync("lichess");
        await AddAsync("chess.com", clubGameId: 1);
        await AddAsync("pgn", "1. e4 *\n", clubGameId: 2);
        await AddAsync("pgn", Pgn("[Event \"Simulton 2026\"]"));
        await AddAsync("pgn", Pgn("[Event \"?\"]"));
        await AddAsync("scoresheet");
        await AddAsync("rookhub");
        await AddAsync("scoresheet", userId: 2);

        var counts = await _service.KindCountsAsync(1);
        Assert.Equal(8, counts.All);
        Assert.Equal(3, counts.Online);
        Assert.Equal(3, counts.Otb);
    }
}
