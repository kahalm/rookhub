using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>
/// Prod 26.11.2026 (1.033.1): Raja, Rithvik R – Binder, Moriz (1. Bundesliga AUT 2026/27, Runde 1, Brett 5, 1-0, 89 Halbzüge)
/// stand als Vereinspartie 268 mit echten Namen und 45.Ke4 (Signal-Königszug der Übertragung) da. Binder lud dieselbe Partie aus
/// „Meine Partien“ mit 45.Kg4 hoch → anderer Hash, neue Partie 269 mit „Schwaz“, vorläufig auf Runde 2 Schwaz – Gamlitz Brett 4,
/// samt automatisch angeforderter Zusammenfassung. Nachgebaut mit „Testdorf“ als Schwaz.
/// </summary>
public class ClubDuplicateDetectionTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 11, 26, 9, 24, 0, DateTimeKind.Utc);
    private const int Tnr = 1404438;
    private const string Raja = "35007394", Binder = "1616951";
    public void Dispose() => _db.Dispose();

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    /// <summary>Die Züge bis 44...Dc3+ (88 Halbzüge), danach der letzte Zug je Fassung.</summary>
    private const string Moves88 = "1. c4 Nf6 2. Nc3 e6 3. e4 d5 4. e5 d4 5. exf6 dxc3 6. bxc3 Qxf6 7. d4 b6 8. Be2 Bb7 9. Bf3 Bxf3 "
        + "10. Nxf3 Bd6 11. Qa4+ c6 12. O-O O-O 13. Bg5 Qf5 14. Rab1 e5 15. dxe5 Bxe5 16. Rxb6 axb6 17. Qxa8 Bxc3 18. Be7 Re8 "
        + "19. Bd6 Qc8 20. Bxb8 c5 21. Qa3 Bb4 22. Qd3 Qxb8 23. a3 Rd8 24. Qf5 Bd2 25. g3 g6 26. Qf6 Qd6 27. Qh4 Bc3 28. Ng5 Bg7 "
        + "29. Qxh7+ Kf8 30. Re1 Qd2 31. Re3 Qc1+ 32. Kg2 Rd1 33. Ne6+ fxe6 34. Rf3+ Ke7 35. Qxg7+ Kd6 36. Qf8+ Kc6 37. Qe8+ Kd6 "
        + "38. Re3 Rg1+ 39. Kf3 Qxc4 40. Qd8+ Kc6 41. Rd3 Kb5 42. Qe8+ Ka5 43. Rd7 Ka6 44. Rd8 Qc3+";

    /// <summary>Die Fassung aus „Meine Partien“ (SavedGame 3098): 45.Kg4, Kopf wie aus der Ligadatenbank.</summary>
    private const string MyGamesPgn = "[Event \"1. BUNDESLIGA AUT 2026/2027\"]\n[Date \"2026.11.25\"]\n[Round \"1\"]\n[White \"Raja, Rithvik R\"]\n"
        + "[Black \"Binder, Moriz\"]\n[Result \"1-0\"]\n[Board \"5\"]\n[WhiteElo \"2519\"]\n[BlackElo \"2212\"]\n[PlyCount \"0\"]\n"
        + "[WhiteTeam \"Pinggau-Friedberg\"]\n[BlackTeam \"Testdorf\"]\n\n" + Moves88 + " 45. Kg4 1-0\n";

    /// <summary>Die Übertragung (mit Uhren und 45.Ke4).</summary>
    private static readonly string BroadcastPgn = "[Event \"1. Bundesliga AUT 2026/2027\"]\n[Date \"2026.11.25\"]\n[Round \"1.5\"]\n[White \"Raja Rithvik R\"]\n"
        + "[Black \"Binder, Moriz\"]\n[Result \"1-0\"]\n[WhiteFideId \"35007394\"]\n[BlackFideId \"1616951\"]\n\n"
        + "1. c4 { [%clk 1:30:53] } " + Moves88[6..] + " 45. Ke4 { [%clk 0:26:46] } 1-0\n";

    private static List<string> Sans(string lastMove) =>
        SavedGameService.LegalSans(PgnParser.ExtractMainlineSans(Moves88 + " 45. " + lastMove).ToList());

    private int _r1Board5;
    private readonly Dictionary<int, int> _r2 = new();
    private int _stored;

    /// <summary>Liga, Meldelisten, Runde 1 (besetzt) und Runde 2 (noch leer), dazu Vereinspartie „268“ wie auf Prod.</summary>
    private async Task SeedAsync()
    {
        await TestClubs.SeedAsync(_db);
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = Tnr, Season = "2026/27", League = "1. Bundesliga", Stage = "Liga" });
        _db.LeagueRounds.AddRange(new LeagueRound { Tnr = Tnr, Round = 1, Date = new DateOnly(2026, 11, 25) },
            new LeagueRound { Tnr = Tnr, Round = 2, Date = new DateOnly(2026, 11, 26) },
            new LeagueRound { Tnr = Tnr, Round = 3, Date = new DateOnly(2026, 11, 27) });
        void Player(string team, int rb, string name, string fide) => _db.LeaguePlayers.Add(new LeaguePlayer
            { Tnr = Tnr, Team = team, RosterBoard = rb, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });
        Player("Pinggau-Friedberg", 8, "Raja, Rithvik R", Raja);
        Player("Testdorf", 7, "Binder, Moriz", Binder);
        for (var i = 1; i <= 6; i++) Player("SV Gamlitz", i, $"Gamlitz {i}", $"66{i}");
        for (var b = 1; b <= 6; b++)
        {
            var g = new LeagueGame { Tnr = Tnr, Round = 1, MatchNo = 1, Board = b, HomeTeam = "Pinggau-Friedberg", AwayTeam = "Testdorf",
                HomePlayer = b == 5 ? "Raja, Rithvik R" : $"Pinggau {b}", HomeFide = b == 5 ? Raja : $"77{b}",
                AwayPlayer = b == 5 ? "Binder, Moriz" : $"Testdorf {b}", AwayFide = b == 5 ? Binder : $"55{b}", HomeColor = b % 2 == 1 ? "w" : "s",
                Result = "" };
            _db.LeagueGames.Add(g);
            await _db.SaveChangesAsync();
            if (b == 5) _r1Board5 = g.Id;
        }
        for (var b = 1; b <= 6; b++)
        {
            var g = new LeagueGame { Tnr = Tnr, Round = 2, MatchNo = 1, Board = b, HomeTeam = "Testdorf", AwayTeam = "SV Gamlitz", HomeColor = "w", Result = "" };
            _db.LeagueGames.Add(g);
            await _db.SaveChangesAsync();
            _r2[b] = g.Id;
        }
        var sans = Sans("Ke4");
        var stored = new LeagueClubGame
        {
            ClubId = TestClubs.HomeId, Year = 2026, White = "Raja Rithvik R", WhiteFide = Raja, Black = "Binder, Moriz", BlackFide = Binder,
            Result = "1-0", Plies = sans.Count, MovesHash = LeagueClubService.HashOf(sans), Pgn = BroadcastPgn, LeagueGameId = _r1Board5,
            LeagueTnr = Tnr, LeagueRound = 1, LeagueMatchNo = 1, LeagueBoard = 5,
        };
        _db.LeagueClubGames.Add(stored);
        await _db.SaveChangesAsync();
        _stored = stored.Id;
    }

    // ── die Regel selbst ─────────────────────────────────────────────

    [Fact]
    public void SameMovesButEnd_SignalKoenigszugStattEchtemKoenigszug_istDieselbePartie()
    {
        var ke4 = Sans("Ke4");
        var kg4 = Sans("Kg4");
        Assert.Equal(89, ke4.Count);
        Assert.NotEqual(LeagueClubService.HashOf(ke4), LeagueClubService.HashOf(kg4));   // darum fand der Hash sie nicht
        Assert.True(ClubGameDuplicates.SameMovesButEnd(ke4, kg4));
        Assert.True(ClubGameDuplicates.SameMovesButEnd(ke4, ke4.Take(88).ToList()));   // Signalzug weggelassen (1.022.0)
        Assert.True(ClubGameDuplicates.SameMovesButEnd(kg4.Take(88).ToList(), kg4));
    }

    [Fact]
    public void SameMovesButEnd_bleibtEng()
    {
        var kg4 = Sans("Kg4");
        Assert.False(ClubGameDuplicates.SameMovesButEnd(kg4, kg4.Take(87).ToList()));       // zwei Halbzüge weniger
        var other = kg4.ToList(); other[40] = other[40] == "Qd2" ? "Qe2" : "Qd2";
        Assert.False(ClubGameDuplicates.SameMovesButEnd(kg4, other));                         // Abweichung mitten drin
        var lastNoKing = kg4.Take(88).Append("Rd7").ToList();
        Assert.False(ClubGameDuplicates.SameMovesButEnd(kg4, lastNoKing));                    // letzter Zug kein Königszug
        var kg2 = kg4.Take(88).Append("Kg2").ToList();
        Assert.False(ClubGameDuplicates.SameMovesButEnd(kg4, kg2));                           // zwei Königszüge, keiner in die Mitte
        var shortA = kg4.Take(20).ToList();
        Assert.False(ClubGameDuplicates.SameMovesButEnd(shortA, kg4.Take(21).ToList()));      // zu kurz für die lockere Regel
    }

    [Fact]
    public void Same_verschiedeneFideIdsOderErgebnisse_sindVerschiedenePartien()
    {
        var a = Sans("Ke4");
        var b = Sans("Kg4");
        var stored = new LeagueClubGame { Year = 2026, White = "A", WhiteFide = Raja, Black = "B", BlackFide = Binder, Result = "1-0", MovesHash = "x", Plies = 89 };
        LeagueClubGame Anon(string result = "1-0", string realFide = Binder, int year = 2026) => new()
            { Year = year, White = "A", WhiteFide = Raja, Black = "Testdorf", BlackRealFide = realFide, Result = result, MovesHash = "y", Plies = 89 };
        Assert.True(ClubGameDuplicates.Same(stored, a, Anon(), b));
        Assert.True(ClubGameDuplicates.Same(stored, a, Anon(result: "*"), b));
        Assert.False(ClubGameDuplicates.Same(stored, a, Anon(result: "0-1"), b));
        Assert.False(ClubGameDuplicates.Same(stored, a, Anon(realFide: "999"), b));
        Assert.False(ClubGameDuplicates.Same(stored, a, Anon(year: 2025), b));
    }

    // ── Upload in die Vereins-Datenbank ──────────────────────────────

    [Fact]
    public async Task Import_AusMeinenPartien_anonymisiert_mitKg4_istSchonVorhanden_keineZweitePartie_keineZusammenfassung()
    {
        await SeedAsync();
        var preview = await Club().PreviewAsync(TestClubs.Home, null, MyGamesPgn);
        var pg = Assert.Single(preview.Games);
        Assert.True(pg.Duplicate);
        Assert.Equal(_stored, pg.DuplicateId);

        var r = await Club().ImportPgnAsync(TestClubs.Home, null, MyGamesPgn, null);

        Assert.Equal((0, 1), (r.Added, r.Duplicates));
        Assert.Equal(_stored, Assert.Single(r.DuplicateOf).Id);
        Assert.Equal(_stored, (await _db.LeagueClubGames.AsNoTracking().SingleAsync()).Id);
        Assert.Empty(await _db.ClubGameSummaries.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Import_derUebertragung_ohneSignalzug_istSchonVorhanden()
    {
        await SeedAsync();
        // die Übertragung als PGN hochgeladen: der Upload lässt 45.Ke4 weg (88 Halbzüge), gespeichert sind 89
        var r = await Club().ImportPgnAsync(TestClubs.Home, null, BroadcastPgn, null);

        Assert.Equal((0, 1), (r.Added, r.Duplicates));
        Assert.Equal(_stored, Assert.Single(r.DuplicateOf).Id);
    }

    [Fact]
    public async Task Formular_mitKg4_istSchonVorhanden_mitNummer()
    {
        await SeedAsync();
        var (game, reason, message) = await Club().AddGameAsync(TestClubs.Home, null, new LeagueClubGameRequest
        {
            Moves = Sans("Kg4"), White = "Raja, Rithvik R", WhiteFide = Raja, Black = "Binder, Moriz", BlackFide = Binder, BlackReplace = true,
            Result = "1-0", Year = 2026, Date = "2026.11.25",
        });

        Assert.Null(game);
        Assert.Equal("duplicate", reason);
        Assert.Equal(_stored.ToString(), message);
    }

    [Fact]
    public async Task Formular_mitMehrHalbzuegen_ersetztDieHochgeladeneFassung()
    {
        await SeedAsync();
        // die gespeicherte Fassung ohne den letzten Zug (88), das Formular hat ihn (89): bessere Daten → ersetzt, alte archiviert
        var row = await _db.LeagueClubGames.SingleAsync();
        var s88 = Sans("Kg4").Take(88).ToList();
        (row.Plies, row.MovesHash, row.Pgn) = (88, LeagueClubService.HashOf(s88), MyGamesPgn.Replace(" 45. Kg4", ""));
        await _db.SaveChangesAsync();

        var (game, reason, _) = await Club().AddGameAsync(TestClubs.Home, null, new LeagueClubGameRequest
        {
            Moves = Sans("Kg4"), White = "Raja, Rithvik R", WhiteFide = Raja, Black = "Binder, Moriz", BlackFide = Binder, BlackReplace = true,
            Result = "1-0", Year = 2026, Date = "2026.11.25",
        });

        Assert.Null(reason);
        Assert.Equal(1, game!.Replaced);
        var old = await _db.LeagueClubGames.IgnoreQueryFilters().AsNoTracking().SingleAsync(g => g.Id == _stored);
        Assert.Equal(game.Id, old.ReplacedById);
    }

    // ── vorläufige Zuordnung ─────────────────────────────────────────

    [Theory]
    [InlineData(25)]   // am Spieltag
    [InlineData(26)]   // einen Tag später eingelesen, mit dem Tag des Uploads
    public async Task Paarung_GegnerAusAndererMannschaft_nieVorlaeufigNachSchwazGamlitz_dieFesteRunde1Gewinnt(int day)
    {
        await SeedAsync();
        var finder = new LeaguePairingFinder(_db) { Today = () => new DateOnly(2026, 11, 26) };
        var options = await finder.ForAsync(new("Raja, Rithvik R", Raja, "Testdorf", Binder, new DateOnly(2026, 11, day), 2026), TestClubs.Home, default);

        Assert.DoesNotContain(options, o => o.Provisional);
        Assert.Equal(_r1Board5, LeaguePairingFinder.AutoPick(options));
    }

    [Fact]
    public async Task Paarung_GegnerDerGegnerMannschaft_bekommtWeiterDieVorlaeufige()
    {
        await SeedAsync();
        var finder = new LeaguePairingFinder(_db) { Today = () => new DateOnly(2026, 11, 26) };
        // Gamlitz 4 (Weiß, Gast am geraden Brett) gegen Binder („Testdorf“) am Tag der Runde 2
        var options = await finder.ForAsync(new("Gamlitz 4", "664", "Testdorf", Binder, new DateOnly(2026, 11, 26), 2026), TestClubs.Home, default);

        var pick = Assert.Single(options, o => o.Provisional);
        Assert.Contains(pick.Id, _r2.Values);
        Assert.Equal(pick.Id, LeaguePairingFinder.AutoPick(options));
    }

    [Fact]
    public async Task Paarung_amTerminEinerAnderenRunde_nichtDieseRunde()
    {
        await SeedAsync();
        var finder = new LeaguePairingFinder(_db) { Today = () => new DateOnly(2026, 11, 26) };
        // Gegner von Gamlitz, aber am 25.11. gespielt — dem Termin der Runde 1, nicht der Runde 2
        var options = await finder.ForAsync(new("Gamlitz 4", "664", "Testdorf", Binder, new DateOnly(2026, 11, 25), 2026), TestClubs.Home, default);

        Assert.DoesNotContain(options, o => o.Provisional);
    }
}
