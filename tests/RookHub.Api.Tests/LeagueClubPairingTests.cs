using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>Vereinspartie fest einer Brettpaarung zuordnen (0.678.0, Wunsch 2026-10-05: „wenn ich eine Partie von meinen
/// Spielen in die Vereins-DB kopiere, kann ich sie keiner Ligarunde zuweisen").</summary>
public class LeagueClubPairingTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private int _user;
    public void Dispose() => _db.Dispose();

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private const string Moves = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 1-0";

    private static string Pgn(string white, string black, string date) =>
        $"[Event \"Liga\"]\n[Date \"{date}\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n\n{Moves}\n";

    /// <summary>Landesliga 2026/27 Runde 2 (04.10.): Absam (Heim, Weiß an Brett 4) gegen Schwaz — Hengl gegen Oberschmid.</summary>
    private async Task<int> SeedAsync()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2026/27", League = "Landesliga", Stage = "Liga" });
        _db.LeagueRounds.Add(new LeagueRound { Tnr = 7, Round = 2, Date = new DateOnly(2026, 10, 4) });
        foreach (var (team, name, fide) in new[] { ("Schwaz", "Oberschmid, Patrik", "900"), ("Absam", "Hengl, Philip", "222") })
            _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = team, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });
        var lg = new LeagueGame { Tnr = 7, Round = 2, Board = 4, HomeTeam = "Absam", AwayTeam = "Schwaz", HomePlayer = "Hengl, Philip",
            AwayPlayer = "Oberschmid, Patrik", HomeColor = "w", Result = "1 - 0", HomeFide = "222", AwayFide = "900" };
        _db.LeagueGames.Add(lg);
        var u = new AppUser { Username = "patrik", Email = "p@test", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        _db.UserProfiles.Add(new UserProfile { UserId = u.Id, LastName = "Oberschmid", FirstName = "Patrik" });
        await _db.SaveChangesAsync();
        _user = u.Id;
        return lg.Id;
    }

    [Fact]
    public async Task Preview_genauerTreffer_istVorgewaehlt_samtLabel()
    {
        var id = await SeedAsync();
        var g = Assert.Single((await Club().PreviewAsync(_user, Pgn("Hengl, Philip", "Oberschmid, Patrik", "2026.10.04"))).Games);

        Assert.Equal(id, g.PairingId);
        var p = Assert.Single(g.Pairings);
        Assert.True(p.Exact);
        Assert.Equal("2026/27 · Landesliga · Runde 2 · Brett 4 (04.10.2026)", p.Label);
        Assert.True(p.BlackOwnClub);
    }

    [Fact]
    public async Task Preview_falscherTag_wirdVorgeschlagen_aberNichtVorgewaehlt()
    {
        await SeedAsync();
        var g = Assert.Single((await Club().PreviewAsync(_user, Pgn("Hengl, Philip", "Oberschmid, Patrik", "2026.12.01"))).Games);

        Assert.Null(g.PairingId);
        Assert.False(Assert.Single(g.Pairings).Exact);
    }

    [Fact]
    public async Task Import_ohneAngabe_nimmtDenGenauenTreffer_JahrUndKlassifiziererFolgen()
    {
        var id = await SeedAsync();
        var r = await Club().ImportPgnAsync(_user, Pgn("Hengl, Philip", "Oberschmid, Patrik", "2026.10.04"), null);

        var game = await _db.LeagueClubGames.SingleAsync(x => x.Id == r.Ids[0]);
        Assert.Equal((id, 2026, "Landesliga", "2026/27"), (game.LeagueGameId!.Value, game.Year!.Value, game.Classifier1, game.Classifier2));
    }

    [Fact]
    public async Task Import_gewaehlt_schlaegtDasDatum_null_heisstKeine()
    {
        var id = await SeedAsync();
        var pgn = Pgn("Hengl, Philip", "Oberschmid, Patrik", "2025.03.01");
        var chosen = await Club().ImportPgnAsync(_user, pgn,
            new[] { new LeagueClubImportGameDecision { Index = 1, LeagueGameId = id } });
        var game = await _db.LeagueClubGames.SingleAsync(x => x.Id == chosen.Ids[0]);
        Assert.Equal((id, 2026), (game.LeagueGameId!.Value, game.Year!.Value));   // Jahr aus dem Rundentermin
        Assert.Contains("[Date \"2026.??.??\"]", game.Pgn);

        _db.LeagueClubGames.RemoveRange(_db.LeagueClubGames);
        await _db.SaveChangesAsync();
        var none = await Club().ImportPgnAsync(_user, Pgn("Hengl, Philip", "Oberschmid, Patrik", "2026.10.04"),
            new[] { new LeagueClubImportGameDecision { Index = 1, LeagueGameId = 0 } });
        Assert.Null((await _db.LeagueClubGames.SingleAsync(x => x.Id == none.Ids[0])).LeagueGameId);
    }

    [Fact]
    public async Task Bearbeiten_setztUndLoeschtDieZuordnung_nurWerBearbeitenDarfSiehtSie()
    {
        // Oberschmid spielt für Schwaz → die Partie ist anonymisiert, ohne Hochladenden: bearbeiten darf nur die Verwaltung
        var id = await SeedAsync();
        var r = await Club().ImportPgnAsync(_user, Pgn("Hengl, Philip", "Oberschmid, Patrik", "2026.12.01"), null);
        var gameId = r.Ids[0];
        Assert.Null((await _db.LeagueClubGames.SingleAsync(x => x.Id == gameId)).LeagueGameId);

        var (game, reason) = await Club().UpdateAsync(_user, true, gameId, new LeagueClubGameUpdateRequest { LeagueGameId = id });
        Assert.Null(reason);
        Assert.Equal(id, game!.LeagueGameId);

        var manager = await Club().GetAsync(_user, true, gameId);
        Assert.Equal((id, "2026/27 · Landesliga · Runde 2 · Brett 4 (04.10.2026)"), (manager!.LeagueGameId!.Value, manager.LeagueGameLabel));
        var reader = await Club().GetAsync(_user, false, gameId);
        Assert.Null(reader!.LeagueGameId);
        Assert.Null(reader.LeagueGameLabel);
        Assert.Contains(await Club().PairingsForGameAsync(_user, true, gameId, default) ?? new(), p => p.Id == id);
        Assert.Null(await Club().PairingsForGameAsync(_user, false, gameId, default));

        await Club().UpdateAsync(_user, true, gameId, new LeagueClubGameUpdateRequest { LeagueGameId = 0 });
        Assert.Null((await _db.LeagueClubGames.SingleAsync(x => x.Id == gameId)).LeagueGameId);
    }

    [Fact]
    public async Task Formular_VorschlaegeUeberDieNamen()
    {
        var id = await SeedAsync();
        var list = await Club().SuggestPairingsAsync(new LeagueClubPairingQuery
            { White = "Philip Hengl", Black = "Schwaz", Date = "2026-10-04" }, default);

        var p = Assert.Single(list);
        Assert.Equal(id, p.Id);
        Assert.True(p.Exact);
    }

    [Fact]
    public async Task Paarungen_derRunde_nehmenDieFesteZuordnung()
    {
        var id = await SeedAsync();
        // anderes Jahr im Kopf — geraten fände sie nicht; fest zugeordnet schon
        _db.LeagueClubGames.Add(new LeagueClubGame { Year = 2019, White = "Hengl, Philip", WhiteFide = "222", Black = "Schwaz",
            Pgn = "fest", MovesHash = "x", LeagueGameId = id });
        await _db.SaveChangesAsync();

        var pairing = Assert.Single(await new LeagueFixtureGames(_db).ForFixtureAsync(7, 2, "Schwaz", default));
        Assert.Equal(("fest", "club"), (pairing.Pgn, pairing.Source));
    }
}
