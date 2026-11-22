using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>Namen/Ergebnis einer Vereinspartie korrigieren → die verbundenen Kopien in „Meine Partien" ziehen mit (1.002.1,
/// Befund 2026-11-22: Vereinspartie 259 = 0-1, Kopie 243 = <c>*</c>); dazu der einmalige Start-Abgleich.</summary>
public class ClubGameCopyHeadersTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private const string Moves = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 *";

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private void Player(string team, string name, string fide) =>
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = team, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });

    private async Task<(int Me, int Other, LeagueClubGame Game)> SeedAsync()
    {
        Player("Testdorf", "Oberschmid, Patrik", "900");
        Player("Testdorf", "Binder, Moriz", "111");
        Player("Absam", "Hengl, Philip", "222");
        Player("Absam", "Schnabl, Andreas Dr.", "333");
        var me = new AppUser { Username = "patrik", Email = "p@test", PasswordHash = "x" };
        var other = new AppUser { Username = "x", Email = "x@test", PasswordHash = "x" };
        _db.AppUsers.AddRange(me, other);
        await _db.SaveChangesAsync();
        _db.UserProfiles.Add(new UserProfile { UserId = me.Id, LastName = "Oberschmid", FirstName = "Patrik" });
        await _db.SaveChangesAsync();
        await Club().ImportPgnAsync(TestClubs.Home, me.Id,
            $"[Event \"Vereinsmeisterschaft\"]\n[Date \"2024.05.12\"]\n[White \"Hengl, Philip\"]\n[Black \"Kinsiz, Atlas\"]\n[Result \"*\"]\n\n{Moves}\n", null);
        var g = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.Equal(("Kinsiz, Atlas", "*"), (g.Black, g.Result));
        return (me.Id, other.Id, g);
    }

    private SavedGame Copy(int userId, int clubId, string white, string black, string extra = "") => new()
    {
        UserId = userId, Source = "pgn", LeagueClubGameId = clubId, White = white, Black = black, Result = "*", CreatedAt = Now,
        Pgn = $"[Event \"Vereinsmeisterschaft\"]\n[Site \"?\"]\n[Date \"2024.??.??\"]\n[Round \"?\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"*\"]\n{extra}\n"
            + "1. e4 {Mein Kommentar} c5 (1... e5 2. Nf3) 2. Nf3 d6 *\n",
    };

    [Fact]
    public async Task Update_ResultAndName_ReachEveryLinkedCopy_HeaderAndEndOfMoves_CommentsStay()
    {
        var (me, other, g) = await SeedAsync();
        var mirrored = Copy(other, g.Id, "Hengl, Philip", "Kinsiz, Atlas", "[Annotator \"ich\"]\n");
        var own = Copy(me, g.Id, "Philip Hengl", "Atlas K.");
        var unlinked = Copy(other, g.Id, "Hengl, Philip", "Kinsiz, Atlas");
        unlinked.LeagueClubGameId = null;
        _db.SavedGames.AddRange(mirrored, own, unlinked);
        await _db.SaveChangesAsync();

        var (game, reason) = await Club().UpdateAsync(TestClubs.Home, me, false, g.Id,
            new LeagueClubGameUpdateRequest { Black = new() { Fide = "333" }, Result = "0-1" });
        Assert.Null(reason);
        Assert.Equal(("Schnabl, Andreas Dr.", "0-1"), (game!.Black, game.Result));

        var a = await _db.SavedGames.AsNoTracking().SingleAsync(s => s.Id == mirrored.Id);
        Assert.Equal(("Hengl, Philip", "Schnabl, Andreas Dr.", "0-1"), (a.White, a.Black, a.Result));
        Assert.Contains("[Result \"0-1\"]", a.Pgn);
        Assert.Contains("[Black \"Schnabl, Andreas Dr.\"]", a.Pgn);
        Assert.DoesNotContain("Kinsiz", a.Pgn);
        Assert.EndsWith(" 0-1", a.Pgn.TrimEnd());
        Assert.DoesNotContain("*", a.Pgn);
        Assert.Contains("{Mein Kommentar}", a.Pgn);                    // Kommentar und Variante der Kopie bleiben
        Assert.Contains("(1... e5 2. Nf3)", a.Pgn);
        Assert.Contains("[Annotator \"ich\"]", a.Pgn);
        Assert.Equal(new[] { "e4", "c5", "Nf3", "d6" }, PgnParser.ExtractMainlineSans(PgnParser.SplitGames(a.Pgn).First().MoveText));

        // Eigene Schreibweise: die korrigierte Seite bekommt den neuen (öffentlichen) Namen, die andere bleibt.
        var b = await _db.SavedGames.AsNoTracking().SingleAsync(s => s.Id == own.Id);
        Assert.Equal(("Philip Hengl", "Schnabl, Andreas Dr.", "0-1"), (b.White, b.Black, b.Result));
        Assert.Contains("[White \"Philip Hengl\"]", b.Pgn);
        Assert.EndsWith(" 0-1", b.Pgn.TrimEnd());

        var c = await _db.SavedGames.AsNoTracking().SingleAsync(s => s.Id == unlinked.Id);
        Assert.Equal(("Kinsiz, Atlas", "*"), (c.Black, c.Result));    // nicht verbunden → unberührt
    }

    /// <summary>Wird eine Seite zu „Schwaz" (hier „Testdorf"), steht der echte Name in keiner Kopie; ein eigener Name des
    /// Besitzers bleibt, eine gespiegelte Seite wird „Testdorf" — und die gespiegelte Veranstaltung fällt wie in der Vereinspartie.</summary>
    [Fact]
    public async Task Update_SideBecomesAnonymous_NoRealNameInAnyCopy_OwnNameStays()
    {
        var (me, other, g) = await SeedAsync();
        var mirrored = Copy(other, g.Id, "Hengl, Philip", "Kinsiz, Atlas");
        var own = Copy(me, g.Id, "Hengl, Philip", "Moriz Binder");
        _db.SavedGames.AddRange(mirrored, own);
        await _db.SaveChangesAsync();

        var (game, reason) = await Club().UpdateAsync(TestClubs.Home, me, false, g.Id,
            new LeagueClubGameUpdateRequest { Black = new() { Fide = "111" }, Result = "1/2-1/2" });
        Assert.Null(reason);
        Assert.Equal(("Testdorf", true), (game!.Black, game.Anonymized));

        var a = await _db.SavedGames.AsNoTracking().SingleAsync(s => s.Id == mirrored.Id);
        Assert.Equal(("Testdorf", "1/2-1/2"), (a.Black, a.Result));
        Assert.DoesNotContain("Binder", a.Pgn);
        Assert.Contains("[Black \"Testdorf\"]", a.Pgn);
        Assert.Contains("[Event \"?\"]", a.Pgn);
        Assert.EndsWith(" 1/2-1/2", a.Pgn.TrimEnd());

        var b = await _db.SavedGames.AsNoTracking().SingleAsync(s => s.Id == own.Id);
        Assert.Equal(("Moriz Binder", "1/2-1/2"), (b.Black, b.Result));
        Assert.Contains("[Result \"1/2-1/2\"]", b.Pgn);
    }

    [Fact]
    public async Task Update_OnlyResult_LeavesNamesOfCopiesAlone()
    {
        var (me, other, g) = await SeedAsync();
        var own = Copy(other, g.Id, "Philip Hengl", "Atlas K.");
        _db.SavedGames.Add(own);
        await _db.SaveChangesAsync();
        // Die Oberfläche schickt die unveränderte Seite mit — das ist keine Namensänderung.
        Assert.Null((await Club().UpdateAsync(TestClubs.Home, me, false, g.Id,
            new LeagueClubGameUpdateRequest { White = new() { Name = "Hengl, Philip", Fide = "222" }, Result = "1-0" })).Reason);
        var b = await _db.SavedGames.AsNoTracking().SingleAsync();
        Assert.Equal(("Philip Hengl", "Atlas K.", "1-0"), (b.White, b.Black, b.Result));
        Assert.EndsWith(" 1-0", b.Pgn.TrimEnd());
    }

    [Theory]
    [InlineData("[Result \"*\"]\n\n1. e4 e5 *\n", "[Result \"0-1\"]\n\n1. e4 e5 0-1\n")]
    [InlineData("[Result \"1-0\"]\n\n1. e4 e5 1-0", "[Result \"0-1\"]\n\n1. e4 e5 0-1")]
    [InlineData("[Result \"*\"]\n\n1. e4 e5 {fertig*}\n", "[Result \"0-1\"]\n\n1. e4 e5 {fertig*} 0-1\n")]
    [InlineData("[White \"a\"]\n\n1. e4 e5\n", "[White \"a\"]\n[Result \"0-1\"]\n\n1. e4 e5 0-1\n")]
    [InlineData("[Result \"*\"]\r\n\r\n1. e4 e5 *\r\n", "[Result \"0-1\"]\r\n\r\n1. e4 e5 0-1\r\n")]
    public void SetResult_ReplacesHeaderAndTermination(string pgn, string expected)
    {
        var g = new SavedGame { Pgn = pgn, Result = "*" };
        SavedGameClubHeaders.SetResult(g, "0-1");
        Assert.Equal(expected, g.Pgn);
        Assert.Equal("0-1", g.Result);
    }

    [Fact]
    public void SetTag_RemovesAndEscapes()
    {
        var pgn = "[White \"a\"]\n[WhiteElo \"1500\"]\n[Black \"b\"]\n\n1. e4 *\n";
        Assert.Equal("[White \"a\"]\n[Black \"b\"]\n\n1. e4 *\n", SavedGameClubHeaders.SetTag(pgn, "WhiteElo", null, removeIfNull: true));
        Assert.Equal("[White \"O\\\"Neil\"]\n[WhiteElo \"1500\"]\n[Black \"b\"]\n\n1. e4 *\n", SavedGameClubHeaders.SetTag(pgn, "White", "O\"Neil"));
        Assert.Equal("O\"Neil", SavedGameClubHeaders.TagsOf(SavedGameClubHeaders.SetTag(pgn, "White", "O\"Neil"))["White"]);
    }

    /// <summary>Start-Abgleich: nur Kopien ohne Ergebnis bekommen das der Vereinspartie; ein anderes entschiedenes Ergebnis bleibt
    /// (Prod: Kopie 193 mit Matt durch Schwarz und 0-1, die Vereinspartie sagt 1-0). Einmalig.</summary>
    [Fact]
    public async Task Repair_FillsOnlyMissingResults_Once()
    {
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = 990001, ClubId = TestClubs.HomeId, White = "a", Black = "b", Result = "0-1", Pgn = "x", MovesHash = "h" });
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = 990002, ClubId = TestClubs.HomeId, White = "a", Black = "b", Result = "*", Pgn = "x", MovesHash = "i" });
        var missing = Copy(1, 990001, "a", "b");
        var decided = Copy(1, 990001, "a", "b");
        decided.Result = "1-0";
        decided.Pgn = decided.Pgn.Replace("*", "1-0");
        var bothOpen = Copy(1, 990002, "a", "b");
        _db.SavedGames.AddRange(missing, decided, bothOpen);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await ClubCopyResultRepair.RunOnceAsync(_db));
        var m = await _db.SavedGames.AsNoTracking().SingleAsync(s => s.Id == missing.Id);
        Assert.Equal("0-1", m.Result);
        Assert.Contains("[Result \"0-1\"]", m.Pgn);
        Assert.EndsWith(" 0-1", m.Pgn.TrimEnd());
        Assert.Equal("1-0", (await _db.SavedGames.AsNoTracking().SingleAsync(s => s.Id == decided.Id)).Result);
        Assert.Equal("*", (await _db.SavedGames.AsNoTracking().SingleAsync(s => s.Id == bothOpen.Id)).Result);

        _db.SavedGames.Add(Copy(1, 990001, "a", "b"));
        await _db.SaveChangesAsync();
        Assert.Null(await ClubCopyResultRepair.RunOnceAsync(_db));     // Marke gesetzt — läuft nicht noch einmal
        Assert.Equal(1, await ClubCopyResultRepair.RepairAsync(_db));       // die Regel selbst fände genau die neue
    }
}
