using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>Schach- und Mattzeichen einer Vereinspartie bleiben bei der Korrektur von Namen/Ergebnis (1.002.4, Prod-Partie 10:
/// „55. Kh2 Qd2+ 56. Kg1 Qg2# 1-0" wurde nach einer Ergebnisänderung zu „55. Kh2 Qd2 56. Kg1 Qg2 0-1"); dazu der Start-Abgleich.</summary>
public class ClubGameCheckMarksTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    // 3. Bxf7+ (Schach) und 10. Qf7# (Matt)
    private const string Moves = "1. e4 e5 2. Bc4 Nc6 3. Bxf7+ Kxf7 4. Qh5+ g6 5. Qxe5 Nf6 6. Qf4 d6 7. Qxf6+ Ke8 8. Nf3 Qd7 9. Ng5 Qd8 10. Qf7# 1-0";
    private const string Stripped = "1. e4 e5 2. Bc4 Nc6 3. Bxf7 Kxf7 4. Qh5 g6 5. Qxe5 Nf6 6. Qf4 d6 7. Qxf6 Ke8 8. Nf3 Qd7 9. Ng5 Qd8 10. Qf7 1-0";

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private async Task<(int Me, LeagueClubGame Game)> SeedAsync()
    {
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = "Testdorf", Name = "Oberschmid, Patrik", NameKey = LeagueNames.NameKey("Oberschmid, Patrik"), FideId = "900" });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = "Absam", Name = "Hengl, Philip", NameKey = LeagueNames.NameKey("Hengl, Philip"), FideId = "222" });
        var me = new AppUser { Username = "patrik", Email = "p@test", PasswordHash = "x" };
        _db.AppUsers.Add(me);
        await _db.SaveChangesAsync();
        await Club().ImportPgnAsync(TestClubs.Home, me.Id,
            $"[Event \"Vereinsmeisterschaft\"]\n[Date \"2024.05.12\"]\n[White \"Hengl, Philip\"]\n[Black \"Kinsiz, Atlas\"]\n[Result \"1-0\"]\n\n{Moves}\n", null);
        var g = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.Contains("Bxf7+", g.Pgn);
        Assert.Contains("Qf7#", g.Pgn);
        return (me.Id, g);
    }

    [Fact]
    public async Task Update_ResultChange_KeepsCheckAndMateMarks()
    {
        var (me, g) = await SeedAsync();
        var (game, reason) = await Club().UpdateAsync(TestClubs.Home, me, false, g.Id, new LeagueClubGameUpdateRequest { Result = "0-1" });
        Assert.Null(reason);
        Assert.Equal("0-1", game!.Result);
        var pgn = (await _db.LeagueClubGames.AsNoTracking().SingleAsync()).Pgn;
        Assert.Contains("3. Bxf7+ Kxf7 4. Qh5+ g6", pgn);
        Assert.Contains("7. Qxf6+ Ke8", pgn);
        Assert.Contains("10. Qf7# 0-1", pgn);
        Assert.Equal(g.MovesHash, (await _db.LeagueClubGames.AsNoTracking().SingleAsync()).MovesHash);   // Schlüssel unverändert
    }

    [Fact]
    public void PgnOf_StrippedSans_WritesMarks_IllegalStaysAsIs()
    {
        var game = new LeagueClubGame { White = "a", Black = "b", Result = "1-0" };
        var sans = PgnParser.ExtractMainlineSans(Moves);
        Assert.DoesNotContain(sans, s => s.Contains('+') || s.Contains('#'));
        Assert.Contains("10. Qf7# 1-0", ClubPgnText.PgnOf(game, sans));
        Assert.Equal(new[] { "e4", "Ke2", "Qxf7" }, SavedGamePgn.WithCheckMarks(new[] { "e4", "Ke2", "Qxf7" }));   // nicht spielbar
    }

    [Fact]
    public void Repair_OnlyMarksChange_CommentsVariationsHeadersStay()
    {
        var pgn = "[Event \"x\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Bc4 {Läufer + Dame} Nc6 (2... Nf6 3. Qh5) 3. Bxf7! Kxf7 4. Qh5 $1 g6 "
                  + "5. Qxe5 Nf6 6. Qf4 d6 7. Qxf6 Ke8 8. Nf3 Qd7 9. Ng5 Qd8 10. Qf7 1-0\n";
        var (fixedPgn, problem) = PgnCheckMarks.Repair(pgn);
        Assert.Null(problem);
        Assert.Equal("[Event \"x\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Bc4 {Läufer + Dame} Nc6 (2... Nf6 3. Qh5) 3. Bxf7+! Kxf7 4. Qh5+ $1 g6 "
                     + "5. Qxe5 Nf6 6. Qf4 d6 7. Qxf6+ Ke8 8. Nf3 Qd7 9. Ng5 Qd8 10. Qf7# 1-0\n", fixedPgn);
        Assert.Equal((null, null), PgnCheckMarks.Repair(fixedPgn!));          // zweiter Lauf: nichts mehr zu tun
        Assert.Equal((null, "illegal"), PgnCheckMarks.Repair("[Result \"*\"]\n\n1. e4 Ke2 *\n"));
    }

    [Fact]
    public async Task RepairRun_FixesClubGamesAnalysesCopies_Once_LeavesOthers()
    {
        string Pgn(string moves) => $"[Event \"x\"]\n[White \"a\"]\n[Black \"b\"]\n[Result \"1-0\"]\n\n{moves}\n";
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = 990101, ClubId = TestClubs.HomeId, White = "a", Black = "b", Result = "1-0", Pgn = Pgn(Stripped), MovesHash = "h" });
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = 990102, ClubId = TestClubs.HomeId, White = "a", Black = "b", Result = "1-0", Pgn = Pgn(Moves), MovesHash = "i" });
        _db.LeagueClubGames.Add(new LeagueClubGame { Id = 990103, ClubId = TestClubs.HomeId, White = "a", Black = "b", Result = "*", Pgn = Pgn("1. e4 Ke2 *"), MovesHash = "j" });
        _db.GameAnalyses.Add(new GameAnalysis { Id = 990111, UserId = 1, Pgn = Pgn(Stripped), StartFen = "x", LeagueClubGameId = 990101 });
        _db.GameAnalyses.Add(new GameAnalysis { Id = 990112, UserId = 1, Pgn = Pgn(Stripped), StartFen = "x" });   // keine Vereinspartie: bleibt
        _db.SavedGames.Add(new SavedGame { Id = 990121, UserId = 1, Source = "pgn", Pgn = Pgn(Stripped.Replace("2. Bc4", "2. Bc4 {gut}")), LeagueClubGameId = 990101, CreatedAt = Now });
        await _db.SaveChangesAsync();

        var c = await ClubCheckMarkRepair.RunOnceAsync(_db, NullLogger.Instance);
        Assert.Equal(new ClubCheckMarkRepair.Counts(1, 1, 1, 1), c);
        Assert.Equal(Pgn(Moves), (await _db.LeagueClubGames.AsNoTracking().SingleAsync(g => g.Id == 990101)).Pgn);
        Assert.Equal(Pgn(Moves), (await _db.GameAnalyses.AsNoTracking().SingleAsync(a => a.Id == 990111)).Pgn);
        Assert.Equal(Pgn(Stripped), (await _db.GameAnalyses.AsNoTracking().SingleAsync(a => a.Id == 990112)).Pgn);
        Assert.Equal(Pgn(Moves.Replace("2. Bc4", "2. Bc4 {gut}")), (await _db.SavedGames.AsNoTracking().SingleAsync(s => s.Id == 990121)).Pgn);
        Assert.Equal(Pgn("1. e4 Ke2 *"), (await _db.LeagueClubGames.AsNoTracking().SingleAsync(g => g.Id == 990103)).Pgn);

        Assert.Null(await ClubCheckMarkRepair.RunOnceAsync(_db, NullLogger.Instance));   // Marke gesetzt
        Assert.Equal(new ClubCheckMarkRepair.Counts(0, 0, 0, 1), await ClubCheckMarkRepair.RepairAsync(_db, NullLogger.Instance));
    }
}
