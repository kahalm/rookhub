using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>Wunsch 2026-11-14: „neue aktuelle Ligapartien automatisch Summary erzeugen“ — eine neu angelegte (oder nachträglich
/// zugeordnete) Ligapartie der laufenden Saison bekommt die Zusammenfassung von selbst angefordert
/// (<see cref="ClubGameAutoSummary"/>), mit Altersgrenze und Tagesdeckel; nichts davon lässt das Anlegen scheitern.</summary>
public class ClubGameAutoSummaryTests : IDisposable
{
    /// <summary>Lässt jedes Speichern einer neuen Zusammenfassungs-Zeile scheitern (für den Fehlerfall).</summary>
    private sealed class FailSummaryInserts : SaveChangesInterceptor
    {
        public bool Active;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData e, InterceptionResult<int> result,
            CancellationToken ct = default)
        {
            if (Active && e.Context!.ChangeTracker.Entries<ClubGameSummary>().Any(x => x.State == EntityState.Added))
                throw new DbUpdateException("Zusammenfassung lässt sich nicht speichern (Test)");
            return base.SavingChangesAsync(e, result, ct);
        }
    }

    private readonly FailSummaryInserts _fail = new();
    private readonly AppDbContext _db;
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);
    private int _user;

    public ClubGameAutoSummaryTests() =>
        _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).AddInterceptors(_fail).Options);

    public void Dispose() => _db.Dispose();

    private static IConfiguration Config(bool? enabled = null, int? maxAgeDays = null, int? dailyLimit = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubGameSummaries:Auto:Enabled"] = enabled?.ToString(),
            ["ClubGameSummaries:Auto:MaxAgeDays"] = maxAgeDays?.ToString(),
            ["ClubGameSummaries:Auto:DailyLimit"] = dailyLimit?.ToString(),
        }.Where(kv => kv.Value != null)).Build();

    private LeagueClubService Club(IConfiguration? config = null) => new(_db, NullLogger<LeagueClubService>.Instance, () => Now, config: config);

    private const string Moves = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 1-0";

    private static string Pgn(string date = "2026.10.04") =>
        $"[Event \"Liga\"]\n[Date \"{date}\"]\n[White \"Hengl, Philip\"]\n[Black \"Oberschmid, Patrik\"]\n[Result \"1-0\"]\n\n{Moves}\n";

    /// <summary>Landesliga 2026/27 (die laufende Saison) Runde 2 am 04.10.2026: Absam – Testdorf, Brett 4 Hengl gegen Oberschmid.
    /// „Heute“ ist der 06.10.2026.</summary>
    private async Task<int> SeedAsync(bool roundDate = true)
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 7, Season = "2026/27", League = "Landesliga", Stage = "Liga" });
        _db.LeagueRounds.Add(new LeagueRound { Tnr = 7, Round = 2, Date = roundDate ? new DateOnly(2026, 10, 4) : null });
        foreach (var (team, name, fide) in new[] { ("Testdorf", "Oberschmid, Patrik", "900"), ("Absam", "Hengl, Philip", "222") })
            _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = team, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });
        var lg = new LeagueGame { Tnr = 7, Round = 2, Board = 4, HomeTeam = "Absam", AwayTeam = "Testdorf", HomePlayer = "Hengl, Philip",
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

    private async Task<int> ImportAsync(IConfiguration? config = null, string date = "2026.10.04")
    {
        var r = await Club(config).ImportPgnAsync(TestClubs.Home, _user, Pgn(date), null);
        return Assert.Single(r.Ids);
    }

    [Fact]
    public async Task Import_LigapartieDerLaufendenSaison_fordertAn_wieEinAdminKlick()
    {
        var pairing = await SeedAsync();
        var gameId = await ImportAsync();

        Assert.Equal(pairing, (await _db.LeagueClubGames.SingleAsync()).LeagueGameId);
        var s = await _db.ClubGameSummaries.SingleAsync();
        Assert.Equal((gameId, ClubGameSummaryStatus.Requested, (int?)_user, Now, (DateTime?)Now),
            (s.ClubGameId, s.Status, s.RequestedByUserId, s.RequestedAt, s.AutoRequestedAt));
        // … und steht damit so in der Partieseite wie eine von Hand angeforderte
        Assert.Equal("requested", (await new ClubGameSummaries(_db).GetAsync(TestClubs.Home, gameId))!.Status);
    }

    [Fact]
    public async Task Formular_LigapartieDerLaufendenSaison_fordertAn_ohneKontoOhneAnforderer()
    {
        await SeedAsync();
        var moves = Moves.Replace(" 1-0", "").Split(' ').Where(t => !t.EndsWith('.')).ToList();
        var (game, reason, _) = await Club().AddGameViaShareAsync(TestClubs.Home, "token-abc", new LeagueClubGameRequest
        {
            Moves = moves, White = "Hengl, Philip", WhiteFide = "222", Black = "Oberschmid, Patrik", BlackFide = "900", BlackReplace = true,
            Result = "1-0", Year = 2026, Date = "2026.10.04",
        });

        Assert.Null(reason);
        var s = await _db.ClubGameSummaries.SingleAsync();
        Assert.Equal((game!.Id, (int?)null), (s.ClubGameId, s.RequestedByUserId));
    }

    [Fact]
    public async Task Import_alteSaison_fordertNichtAn()
    {
        await SeedAsync();
        // dieselbe Region hat schon eine jüngere Saison: 2026/27 ist nicht mehr die laufende
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 8, Season = "2027/28", League = "Landesliga", Stage = "Liga" });
        await _db.SaveChangesAsync();

        await ImportAsync();

        Assert.NotNull((await _db.LeagueClubGames.SingleAsync()).LeagueGameId);
        Assert.Empty(_db.ClubGameSummaries);
    }

    [Fact]
    public async Task Import_keineLigapartie_fordertNichtAn()
    {
        await SeedAsync();
        await ImportAsync(date: "2026.12.01");   // kein Rundentermin → keine Zuordnung

        Assert.Null((await _db.LeagueClubGames.SingleAsync()).LeagueGameId);
        Assert.Empty(_db.ClubGameSummaries);
    }

    [Fact]
    public async Task Import_PartiedatumZuAlt_fordertNichtAn_anDerGrenzeSchon()
    {
        await SeedAsync();
        await ImportAsync(Config(maxAgeDays: 1));   // gespielt am 04.10., heute ist der 06.10. → 2 Tage
        Assert.Single(_db.LeagueClubGames);
        Assert.Empty(_db.ClubGameSummaries);

        _db.LeagueClubGames.RemoveRange(_db.LeagueClubGames);
        await _db.SaveChangesAsync();
        await ImportAsync(Config(maxAgeDays: 2));
        Assert.Single(_db.ClubGameSummaries);
    }

    [Fact]
    public async Task Alter_ohneTagImUpload_zaehltDerRundentermin_ohneRundenterminHeute()
    {
        var pairing = await SeedAsync();
        var auto = new ClubGameAutoSummary(_db, NullLogger.Instance, () => Now, Config(maxAgeDays: 0));
        var g = new LeagueClubGame { ClubId = TestClubs.HomeId, White = "Hengl, Philip", Black = "Testdorf", Pgn = "x", MovesHash = "a",
            LeagueGameId = pairing, LeagueTnr = 7, LeagueRound = 2, LeagueMatchNo = 0, LeagueBoard = 4 };
        _db.LeagueClubGames.Add(g);
        await _db.SaveChangesAsync();

        Assert.Equal(0, await auto.RequestAsync(new[] { g }, _user, default));   // Rundentermin 04.10. liegt vor heute

        (await _db.LeagueRounds.SingleAsync()).Date = null;
        await _db.SaveChangesAsync();
        Assert.Equal(1, await auto.RequestAsync(new[] { g }, _user, default));   // kein Datum bekannt → zählt als heute
    }

    [Fact]
    public async Task Tagesdeckel_erreicht_fordertNichtAn_gesternZaehltNicht()
    {
        await SeedAsync();
        _db.ClubGameSummaries.Add(new ClubGameSummary { ClubGameId = 9001, Status = ClubGameSummaryStatus.Done, RequestedAt = Now,
            AutoRequestedAt = Now.AddHours(-3) });
        await _db.SaveChangesAsync();

        await ImportAsync(Config(dailyLimit: 1));
        Assert.Single(_db.LeagueClubGames);                                   // die Partie selbst ist angelegt
        Assert.Equal(9001, (await _db.ClubGameSummaries.SingleAsync()).ClubGameId);

        // die automatische Anforderung von gestern (UTC) und die von Hand zählen nicht
        (await _db.ClubGameSummaries.SingleAsync()).AutoRequestedAt = Now.Date.AddSeconds(-1);
        _db.ClubGameSummaries.Add(new ClubGameSummary { ClubGameId = 9002, Status = ClubGameSummaryStatus.Requested, RequestedAt = Now });
        _db.LeagueClubGames.RemoveRange(_db.LeagueClubGames);
        await _db.SaveChangesAsync();
        var gameId = await ImportAsync(Config(dailyLimit: 1));
        Assert.Contains(_db.ClubGameSummaries, s => s.ClubGameId == gameId && s.AutoRequestedAt == Now);
    }

    [Fact]
    public async Task Ausgeschaltet_fordertNichtAn()
    {
        await SeedAsync();
        await ImportAsync(Config(enabled: false));

        Assert.Single(_db.LeagueClubGames);
        Assert.Empty(_db.ClubGameSummaries);
    }

    [Fact]
    public async Task NachtraeglichZugeordnet_fordertAn_vorhandeneZeileBleibtUnberuehrt()
    {
        var pairing = await SeedAsync();
        var gameId = await ImportAsync(date: "2026.12.01");
        Assert.Empty(_db.ClubGameSummaries);

        // schon eine Zeile (hier: gescheitert) → keine automatische Anforderung, die Zeile bleibt, wie sie ist
        _db.ClubGameSummaries.Add(new ClubGameSummary { ClubGameId = gameId, Status = ClubGameSummaryStatus.Failed, RequestedAt = Now.AddDays(-1), Error = "x" });
        await _db.SaveChangesAsync();
        var (_, reason) = await Club().UpdateAsync(TestClubs.Home, _user, true, gameId, new LeagueClubGameUpdateRequest { LeagueGameId = pairing });
        Assert.Null(reason);
        var kept = await _db.ClubGameSummaries.SingleAsync();
        Assert.Equal((ClubGameSummaryStatus.Failed, (DateTime?)null), (kept.Status, kept.AutoRequestedAt));

        // ohne Zeile: gelöst und neu zugeordnet → angefordert, Anforderer = wer zuordnet
        _db.ClubGameSummaries.Remove(kept);
        await _db.SaveChangesAsync();
        await Club().UpdateAsync(TestClubs.Home, _user, true, gameId, new LeagueClubGameUpdateRequest { LeagueGameId = 0 });
        Assert.Empty(_db.ClubGameSummaries);
        await Club().UpdateAsync(TestClubs.Home, _user, true, gameId, new LeagueClubGameUpdateRequest { LeagueGameId = pairing });
        var s = await _db.ClubGameSummaries.SingleAsync();
        Assert.Equal((gameId, ClubGameSummaryStatus.Requested, (int?)_user), (s.ClubGameId, s.Status, s.RequestedByUserId));

        // eine Korrektur OHNE neue Zuordnung fordert nichts an
        _db.ClubGameSummaries.Remove(s);
        await _db.SaveChangesAsync();
        await Club().UpdateAsync(TestClubs.Home, _user, true, gameId, new LeagueClubGameUpdateRequest { Result = "1/2-1/2" });
        Assert.Empty(_db.ClubGameSummaries);
    }

    [Fact]
    public async Task NachtraeglichAutomatischZugeordnet_fordertAn()
    {
        await SeedAsync();
        await TestClubs.SeedAsync(_db);
        var gameId = await ImportAsync(Config(enabled: false));
        var row = await _db.LeagueClubGames.SingleAsync();
        LeagueGameLinks.Set(row, null);
        await _db.SaveChangesAsync();

        var (found, id) = await Club().AssignPairingIfUnlinkedAsync(gameId, default, TestClubs.HomeId, _user);

        Assert.True(found);
        Assert.NotNull(id);
        Assert.Equal((gameId, (int?)_user), await _db.ClubGameSummaries.Select(s => new ValueTuple<int, int?>(s.ClubGameId, s.RequestedByUserId)).SingleAsync());
    }

    [Fact]
    public async Task Archiviert_fordertNichtAn()
    {
        var pairing = await SeedAsync();
        var g = new LeagueClubGame { ClubId = TestClubs.HomeId, White = "Hengl, Philip", Black = "Testdorf", Pgn = "x", MovesHash = "a",
            LeagueGameId = pairing, LeagueTnr = 7, LeagueRound = 2, LeagueMatchNo = 0, LeagueBoard = 4, ArchivedAt = Now };
        _db.LeagueClubGames.Add(g);
        await _db.SaveChangesAsync();
        var auto = new ClubGameAutoSummary(_db, NullLogger.Instance, () => Now);

        Assert.Equal(0, await auto.RequestAsync(new[] { g }, _user, default));
        Assert.Empty(_db.ClubGameSummaries);

        g.ArchivedAt = null;   // Gegenprobe: dieselbe Partie, nicht archiviert
        Assert.Equal(1, await auto.RequestAsync(new[] { g }, _user, default));
    }

    [Fact]
    public async Task FehlerBeimAnfordern_laesstDasAnlegenNichtScheitern()
    {
        await SeedAsync();
        _fail.Active = true;

        var gameId = await ImportAsync();

        Assert.Equal(gameId, (await _db.LeagueClubGames.SingleAsync()).Id);
        Assert.Empty(_db.ClubGameSummaries);
        // der Kontext ist danach sauber: ein weiteres Speichern scheitert nicht an der halben Zeile
        Assert.DoesNotContain(_db.ChangeTracker.Entries<ClubGameSummary>(), e => e.State == EntityState.Added);
        (await _db.LeagueClubGames.SingleAsync()).Event = "später geändert";
        await _db.SaveChangesAsync();
    }
}
