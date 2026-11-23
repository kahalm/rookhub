using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>FIDE-Titel an gespeicherten Partien (<see cref="PlayerTitles"/>, 1.009.0): PGN-Kopf → FIDE-ID → eindeutiger Name,
/// nie geraten; Detail, Teilen-Link und Liste tragen <c>whiteTitle</c>/<c>blackTitle</c>.</summary>
public class PlayerTitlesTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SavedGameService _service;
    private int _next = 1;

    public PlayerTitlesTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = TestServices.SavedGames(_db);
    }

    public void Dispose() => _db.Dispose();

    private void Player(string name, string? fide, string? title, int? elo = null, int tnr = 100, string team = "Schmelz")
        => _db.LeaguePlayers.Add(new LeaguePlayer
        {
            Tnr = tnr, Team = team, Name = name, NameKey = Services.League.LeagueNames.NameKey(name), FideId = fide, Title = title, EloI = elo,
        });

    private async Task<SavedGame> GameAsync(string white, string black, string headers = "", string source = "pgn", int? clubGameId = null)
    {
        var id = _next++;
        var g = new SavedGame
        {
            Id = id, UserId = 1, White = white, Black = black, Source = source, ShareToken = $"t{id}", MoveCount = 2,
            HeadersScanned = true, LeagueClubGameId = clubGameId, CreatedAt = new DateTime(2026, 1, 1).AddDays(id),
            Pgn = $"[White \"{white}\"]\n[Black \"{black}\"]\n{headers}\n1. e4 e5 *\n",
        };
        _db.SavedGames.Add(g);
        await _db.SaveChangesAsync();
        return g;
    }

    private Task<IReadOnlyList<string?>> ResolveAsync(params PlayerTitles.Side[] sides) => new PlayerTitles(_db).ResolveAsync(sides);

    [Fact]
    public async Task Detail_NameWithAcademicSuffixAndOtherComma_GetsTitle()
    {
        // Partie 186 auf Prod: „Grötz, Harald“ gegen die Meldelisten „Grötz, Harald Mag.“ / „Grötz Harald Mag.“
        Player("Grötz, Harald Mag.", "1605607", "IM", 2253, tnr: 1);
        Player("Grötz Harald Mag.", "1605607", "IM", 2240, tnr: 2);
        Player("Oberschmid, Patrik", "1650033", null, 1867);
        await _db.SaveChangesAsync();
        var g = await GameAsync("Oberschmid, Patrik", "Grötz, Harald", "[WhiteElo \"1867\"]\n[BlackElo \"2249\"]");

        var dto = await _service.GetAsync(1, g.Id);
        Assert.Null(dto!.WhiteTitle);
        Assert.Equal("IM", dto.BlackTitle);
        var shared = await _service.GetSharedAsync(g.ShareToken);
        Assert.Equal("IM", shared!.BlackTitle);
        var list = await _service.ListAsync(1);
        Assert.Equal("IM", list.Single().BlackTitle);
    }

    [Fact]
    public async Task Resolve_PgnTitleTag_WinsAndOnlyFideTitles()
    {
        var titles = await ResolveAsync(new("A, B", null, "gm", null), new("C, D", null, "MK", null), new("E, F", null, "LM", null));
        Assert.Equal(new string?[] { "GM", null, null }, titles);
    }

    [Fact]
    public async Task Resolve_KnownFide_IsAuthoritative()
    {
        Player("Muster, Max", "111", "FM");
        Player("Muster, Max", "222", null, team: "Andere");
        await _db.SaveChangesAsync();
        // Namensgleich, aber die Partie nennt die ID ohne Titel → kein Titel; die ID mit Titel → FM.
        Assert.Equal(new string?[] { null, "FM" }, await ResolveAsync(new("Muster, Max", "222", null, null), new("Muster, Max", "111", null, null)));
    }

    [Fact]
    public async Task Resolve_TwoPlayersSameName_NoTitle()
    {
        Player("Huber, Franz", "111", "IM");
        Player("Huber, Franz", "222", null, team: "Andere");
        await _db.SaveChangesAsync();
        Assert.Null((await ResolveAsync(new PlayerTitles.Side("Huber, Franz", null, null, null)))[0]);
    }

    [Fact]
    public async Task Resolve_OnlyInitialOrLastName_NoTitle()
    {
        Player("Grötz, Harald", "1605607", "IM");
        await _db.SaveChangesAsync();
        var titles = await ResolveAsync(new("Grötz, H.", null, null, null), new("Grötz", null, null, null),
            new("Harald Groetz", null, null, null));
        Assert.Equal(new string?[] { null, null, "IM" }, titles);
    }

    [Fact]
    public async Task Resolve_MegabaseNamesakeWithOtherFide_NoTitle()
    {
        Player("Maier, Josef", "111", "FM");
        _db.LeagueMegaPlayers.Add(new LeagueMegaPlayer { Name = "Maier, Josef", NameKey = "maier, josef", FideId = "999" });
        await _db.SaveChangesAsync();
        Assert.Null((await ResolveAsync(new PlayerTitles.Side("Maier, Josef", null, null, null)))[0]);
    }

    [Fact]
    public async Task Resolve_EloFarOff_NoTitle()
    {
        Player("Kern, Lukas", "111", "FM", 2300);
        await _db.SaveChangesAsync();
        var titles = await ResolveAsync(new("Kern, Lukas", null, null, 1500), new("Kern, Lukas", null, null, 2250));
        Assert.Equal(new string?[] { null, "FM" }, titles);
    }

    [Fact]
    public async Task Resolve_HighestTitleAcrossSeasons_AndNotTwiceInName()
    {
        Player("Lang, Eva", "111", "FM", tnr: 1);
        Player("Lang, Eva", "111", "IM", tnr: 2);
        await _db.SaveChangesAsync();
        var titles = await ResolveAsync(new("Lang, Eva", null, null, null), new("IM Lang, Eva", null, null, null));
        Assert.Equal(new string?[] { "IM", null }, titles);
    }

    [Fact]
    public async Task Detail_FideFromClubGame_AndOnlineGamesNever()
    {
        Player("Groetz, Harald", "1605607", "IM");
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = 48, White = "Schwaz", Black = "X, Y", BlackFide = "1605607" });
        await _db.SaveChangesAsync();
        var club = await GameAsync("Oberschmid, Patrik", "Unbekannt, Name", clubGameId: 48);
        var online = await GameAsync("Grötz, Harald", "someone", source: "lichess");

        Assert.Equal("IM", (await _service.GetAsync(1, club.Id))!.BlackTitle);
        Assert.Null((await _service.GetAsync(1, online.Id))!.WhiteTitle);
    }

    [Fact]
    public async Task SharePreview_TitleBeforeName_InTitleAndCard()
    {
        Player("Grötz, Harald Mag.", "1605607", "IM", 2253);
        await _db.SaveChangesAsync();
        var g = await GameAsync("Oberschmid, Patrik", "Grötz, Harald", "[BlackElo \"2249\"]");
        var meta = new Services.Og.OgMetaService(_service, null!, null!, null!, _db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Services.Og.OgMetaService>.Instance);

        var page = await meta.ResolvePageAsync($"/g/{g.ShareToken}", "https://rookhub.example");
        Assert.Equal("Oberschmid, Patrik – IM Grötz, Harald (Elo 2249)", page!.Title);
        var board = await meta.ResolveBoardAsync("game", g.ShareToken);
        Assert.Equal("IM Grötz, Harald", board!.Card!.Players[1].Name);
    }

    [Fact]
    public void HeadOf_IgnoresTagsInComments()
    {
        var head = PlayerTitles.HeadOf("[White \"A\"]\n[Black \"B\"]\n\n1. e4 {[WhiteTitle \"GM\"]} e5 *");
        Assert.DoesNotContain("WhiteTitle", head);
        Assert.Contains("[Black \"B\"]", head);
    }

    [Fact]
    public void WithTitle_PrefixesName() =>
        Assert.Equal("IM Grötz, Harald", PlayerTitles.WithTitle("IM", "Grötz, Harald"));
}
