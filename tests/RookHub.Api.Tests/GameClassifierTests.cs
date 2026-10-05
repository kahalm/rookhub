using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die zwei Klassifizierer der Partienliste (0.661.0): Online-Partien Seite + Modus (abgeleitet), Ligapartien Liga +
/// Jahrgang (vom Nutzer gesetzt). Ein gesetzter Wert schlägt den abgeleiteten.
/// </summary>
public class GameClassifierTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SavedGameService _svc;

    public GameClassifierTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _svc = TestServices.SavedGames(_db, TestServices.GameAnalyses(_db));
    }

    public void Dispose() => _db.Dispose();

    [Theory]
    [InlineData("60", "Bullet")]        // 1 min
    [InlineData("180", "Blitz")]        // 3 min
    [InlineData("180+2", "Blitz")]      // 180 + 80 = 260
    [InlineData("300+5", "Rapid")]      // 300 + 40 × 5 = 500, über der Blitz-Grenze von 480
    [InlineData("600", "Rapid")]        // 10 min
    [InlineData("900+10", "Rapid")]
    [InlineData("1800", "Classical")]   // 30 min
    [InlineData("1/86400", "Daily")]    // Fernschach
    public void Mode_nachDerSchaetzungVonLichess(string timeControl, string expected)
    {
        Assert.Equal(expected, GameClassifier.Mode(timeControl));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("-")]
    [InlineData("abc")]
    [InlineData("0")]
    public void Mode_ohneVerstaendlicheBedenkzeit_istLeer(string? timeControl)
        => Assert.Null(GameClassifier.Mode(timeControl));

    [Fact]
    public void Effective_leitetNurBeiOnlineQuellenAb_undGesetztSchlaegtAbgeleitet()
    {
        Assert.Equal(("chess.com", "Blitz"), GameClassifier.Effective("chess.com", "180+2", null, null));
        Assert.Equal(("Lichess", "Rapid"), GameClassifier.Effective("lichess", "600", null, null));
        Assert.Equal((null, null), GameClassifier.Effective("pgn", "600", null, null));
        Assert.Equal((null, null), GameClassifier.Effective("scoresheet", null, "  ", ""));
        Assert.Equal(("Landesliga", "2026/27"), GameClassifier.Effective("pgn", null, " Landesliga ", "2026/27"));
        Assert.Equal(("chess.com", "Bullet"), GameClassifier.Effective("chess.com", "180+2", null, "Bullet"));
    }

    private async Task<AppUser> UserAsync()
    {
        var user = new AppUser { Username = "u", Email = "u@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Liste_zeigtAbgeleiteteWerte_beiOnlinePartien()
    {
        var u = await UserAsync();
        await _svc.SaveAsync(u.Id, new SaveGameInputDto
        {
            Source = "chess.com", Moves = new() { "e4", "e5" }, ExternalId = "x1", TimeControl = "180+2",
        });

        var game = Assert.Single(await _svc.ListAsync(u.Id));

        Assert.Equal("chess.com", game.Classifier1);
        Assert.Equal("Blitz", game.Classifier2);
        Assert.Null(game.Classifier1Set);   // nichts gesetzt: der Editor zeigt nur den Platzhalter
    }

    [Fact]
    public async Task Update_setztLigaUndJahrgang_nullLaesstUnveraendert_leerNimmtZurueck()
    {
        var u = await UserAsync();
        var saved = await _svc.ImportPgnAsync(u.Id, "[White \"A\"]\n[Black \"B\"]\n[Result \"1-0\"]\n\n1. e4 e5 1-0\n", null);
        var id = saved.Ids.Single();
        GameUpdateDto Dto(string? c1, string? c2) => new()
        {
            Moves = new() { new() { San = "e4" }, new() { San = "e5" } }, White = "A", Black = "B", Result = "1-0",
            Classifier1 = c1, Classifier2 = c2,
        };

        var set = await _svc.UpdateAsync(u.Id, id, Dto("  Landesliga ", "2026/27"));
        Assert.Equal("Landesliga", set!.Classifier1);
        Assert.Equal("2026/27", set.Classifier2);
        Assert.Equal("Landesliga", Assert.Single(await _svc.ListAsync(u.Id)).Classifier1);

        var kept = await _svc.UpdateAsync(u.Id, id, Dto(null, null));
        Assert.Equal(("Landesliga", "2026/27"), (kept!.Classifier1Set, kept.Classifier2Set));

        var cleared = await _svc.UpdateAsync(u.Id, id, Dto("", "   "));
        Assert.Null(cleared!.Classifier1);
        Assert.Null(cleared.Classifier2);
    }

    [Fact]
    public async Task Update_beiOnlinePartie_leerNimmtDenGesetztenWertZurueck_dannGiltAbgeleitet()
    {
        var u = await UserAsync();
        var saved = await _svc.SaveAsync(u.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "e5" }, ExternalId = "x2", TimeControl = "600",
        });
        GameUpdateDto Dto(string? c2) => new()
        {
            Moves = new() { new() { San = "e4" }, new() { San = "e5" } }, Result = "*", Classifier2 = c2,
        };

        Assert.Equal("Eigener Modus", (await _svc.UpdateAsync(u.Id, saved.Id, Dto("Eigener Modus")))!.Classifier2);
        Assert.Equal("Rapid", (await _svc.UpdateAsync(u.Id, saved.Id, Dto("")))!.Classifier2);
    }

    [Fact]
    public async Task Update_kuerztZuLangeWerte()
    {
        var u = await UserAsync();
        var saved = await _svc.SaveAsync(u.Id, new SaveGameInputDto { Source = "lichess", Moves = new() { "e4" }, ExternalId = "x3" });

        var dto = await _svc.UpdateAsync(u.Id, saved.Id, new GameUpdateDto
        {
            Moves = new() { new() { San = "e4" } }, Result = "*", Classifier1 = new string('x', 200),
        });

        Assert.Equal(GameClassifier.MaxLength, dto!.Classifier1Set!.Length);
    }
}

/// <summary>Eigene Tags einer Partie (0.662.0).</summary>
public class GameTagsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SavedGameService _svc;

    public GameTagsTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _svc = TestServices.SavedGames(_db, TestServices.GameAnalyses(_db));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Clean_trimmtKuerztEntdoppeltUndDeckelt()
    {
        Assert.Equal(new[] { "Endspiel", "neu angelegt" }, GameTags.Clean(new[] { "  Endspiel ", "endspiel", "", "neu,\nangelegt" }));
        Assert.Equal(GameTags.MaxTagLength, GameTags.Clean(new[] { new string('x', 99) })[0].Length);
        Assert.Equal(GameTags.MaxTags, GameTags.Clean(Enumerable.Range(0, 50).Select(i => "t" + i)).Count);
        Assert.Null(GameTags.Join(new[] { " ", "," }));
    }

    [Fact]
    public async Task Update_setztTags_nullLaesstUnveraendert_leereListeLoescht()
    {
        var u = new AppUser { Username = "u", Email = "u@t.com", PasswordHash = "h" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        var saved = await _svc.SaveAsync(u.Id, new SaveGameInputDto { Source = "lichess", Moves = new() { "e4" }, ExternalId = "t1" });
        GameUpdateDto Dto(List<string>? tags) => new() { Moves = new() { new() { San = "e4" } }, Result = "*", Tags = tags };

        var set = await _svc.UpdateAsync(u.Id, saved.Id, Dto(new() { "Endspiel", "lehrreich", "endspiel" }));
        Assert.Equal(new[] { "Endspiel", "lehrreich" }, set!.Tags);
        Assert.Equal(new[] { "Endspiel", "lehrreich" }, Assert.Single(await _svc.ListAsync(u.Id)).Tags);

        Assert.Equal(2, (await _svc.UpdateAsync(u.Id, saved.Id, Dto(null)))!.Tags.Count);
        Assert.Empty((await _svc.UpdateAsync(u.Id, saved.Id, Dto(new())))!.Tags);
    }
}
