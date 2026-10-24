using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>
/// Vorläufige Zuordnung zur laufenden Runde (0.832.0, Wunsch 2026-10-24: „er sollte direkt nach dem Einlesen, wenn er es
/// zuordnen kann, die Matchings für die aktuelle Runde schreiben — und später nur bestätigen"). Nachgebaut ist die Gebietsklasse
/// vom 24.10.2026: Runde 3 Testdorf (Heim, Weiß an ungeraden Brettern) – Sparkasse Jenbach 2, vier Bretter noch ohne Spieler;
/// Fercher saß in Runde 1 an Brett 2 von Jenbach 1 – Jenbach 2 (der Fehlgriff von Vereinspartie 225).
/// </summary>
public class LeagueProvisionalPairingTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 10, 24, 18, 0, 0, DateTimeKind.Utc);
    private const int Tnr = 1479342;
    public void Dispose() => _db.Dispose();

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private int _r1Fercher;
    private readonly Dictionary<int, int> _r3 = new();   // Brett → LeagueGames.Id

    private const string Jenbach2 = "Sparkasse Jenbach 2";

    private async Task SeedAsync()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = Tnr, Season = "2026/27", League = "Gebietsklasse", Stage = "Liga" });
        _db.LeagueRounds.AddRange(new LeagueRound { Tnr = Tnr, Round = 1, Date = new DateOnly(2026, 9, 26) },
            new LeagueRound { Tnr = Tnr, Round = 3, Date = new DateOnly(2026, 10, 24) });
        foreach (var (rb, name, fide) in new[] { (4, "Erlacher, Herbert", "1614533"), (11, "Katkic, Samuel", "530005108"),
                     (12, "Ober, David", "530014190"), (16, "Moser, Niklas William", "530021065"), (1, "Oberschmid, Patrik", "1693034") })
            _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = Tnr, Team = "Testdorf", RosterBoard = rb, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });
        foreach (var (rb, name, fide) in new[] { (2, "Spierings, Benjamin", "1671219"), (9, "Duftner, Johannes", "1658093"),
                     (10, "Fercher, Alois", "1621947"), (11, "Rupprechter, Alfred", "1682679"), (12, "Guseck, Markus", "1642510") })
            _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = Tnr, Team = Jenbach2, RosterBoard = rb, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });
        // Runde 1: Jenbach 1 – Jenbach 2, Jenbach 2 mit Meldenummern 9–12 (Fercher an Brett 2, Weiß als Gast an geradem Brett)
        var r1 = new[] { (9, "Duftner, Johannes", "1658093"), (10, "Fercher, Alois", "1621947"), (11, "Rupprechter, Alfred", "1682679"),
            (12, "Guseck, Markus", "1642510") };
        for (var i = 0; i < r1.Length; i++)
        {
            var b = i + 1;
            var g = new LeagueGame
            {
                Tnr = Tnr, Round = 1, MatchNo = 1, Board = b, HomeTeam = "Sparkasse Jenbach 1", AwayTeam = Jenbach2,
                HomePlayer = $"Heim {b}", HomeFide = $"77{b}", AwayPlayer = r1[i].Item2, AwayFide = r1[i].Item3, AwayRb = r1[i].Item1,
                HomeRb = b, HomeColor = b % 2 == 1 ? "w" : "s", Result = "½ - ½",
            };
            _db.LeagueGames.Add(g);
            if (b == 2) await _db.SaveChangesAsync();
            if (b == 2) _r1Fercher = g.Id;
        }
        // ein Gegner, der in keiner Meldeliste steht (nur im Megabase-Verzeichnis) — wie „Markus, Robert" am 24.10.
        _db.LeagueMegaPlayers.Add(new LeagueMegaPlayer { Name = "Markus, Robert", NameKey = LeagueMegaPlayers.KeyOf("Markus, Robert"), FideId = "921637", Games = 3 });
        var open = new List<LeagueGame>();
        for (var b = 1; b <= 4; b++)
        {
            var g = new LeagueGame { Tnr = Tnr, Round = 3, MatchNo = 3, Board = b, HomeTeam = "Testdorf", AwayTeam = Jenbach2, HomeColor = "w", Result = "" };
            _db.LeagueGames.Add(g);
            open.Add(g);
        }
        await _db.SaveChangesAsync();
        foreach (var g in open) _r3[g.Board] = g.Id;
    }

    private static readonly string[][] Openings =
    {
        new[] { "e4", "e5", "Nf3", "Nc6" }, new[] { "d4", "d5", "c4", "e6" }, new[] { "c4", "e5", "Nc3", "Nf6" },
        new[] { "Nf3", "d5", "g3", "Nf6" }, new[] { "e4", "c5", "Nf3", "d6" },
    };

    private static LeagueClubGameRequest Sheet(int opening, string white, string? whiteFide, string black, string? blackFide,
        bool whiteOwn, string date = "2026.10.24") => new()
    {
        Moves = Openings[opening].ToList(), White = white, WhiteFide = whiteFide, Black = black, BlackFide = blackFide,
        WhiteReplace = whiteOwn, BlackReplace = !whiteOwn, Result = "1-0", Year = 2026, Date = date,
    };

    private async Task<LeagueClubGame> AddAsync(LeagueClubGameRequest req)
    {
        var (g, reason, msg) = await Club().AddGameAsync(TestClubs.Home, null, req);
        Assert.True(reason == null, $"{reason}: {msg}");
        return await _db.LeagueClubGames.AsNoTracking().SingleAsync(x => x.Id == g!.Id);
    }

    private int BoardOf(LeagueClubGame g) => _db.LeagueGames.AsNoTracking().Single(x => x.Id == g.LeagueGameId).Board;

    [Fact]
    public async Task Formular_LeereBretter_ordnetVorlaeufigZurLaufendenRundeZu_nichtDerAltenRundeDesGegners()
    {
        await SeedAsync();
        // Vereinspartie 225: Fercher (Weiß) gegen Katkic („Testdorf", Schwarz)
        var g = await AddAsync(Sheet(0, "Fercher, Alois", "1621947", "Katkic, Samuel", "530005108", whiteOwn: false));

        Assert.NotEqual(_r1Fercher, g.LeagueGameId);
        Assert.True(g.LeaguePairingProvisional);
        Assert.Equal((Tnr, 3, 3), (g.LeagueTnr!.Value, g.LeagueRound!.Value, g.LeagueMatchNo!.Value));
        Assert.Contains(BoardOf(g), new[] { 2, 4 });   // Heim hat Weiß an ungeraden — der Gast (Weiß) sitzt an einem geraden
        Assert.Equal("Gebietsklasse", g.Classifier1);
    }

    [Fact]
    public async Task Vorschlaege_altePaarungOhneEigeneMannschaft_fallenWeg_vorlaeufigeIstVorgewaehlt()
    {
        await SeedAsync();
        var list = await Club().SuggestPairingsAsync(TestClubs.Home, new LeagueClubPairingQuery
            { White = "Fercher, Alois", WhiteFide = "1621947", Black = "Katkic, Samuel", BlackFide = "530005108", Date = "2026.10.24" }, default);

        Assert.DoesNotContain(list, p => p.Id == _r1Fercher);
        var pick = Assert.Single(list, p => p.Provisional);
        Assert.Equal(list[0].Id, pick.Id);              // die vorläufige steht vorn
        Assert.True(pick.Open);
        Assert.True(pick.Uncertain);                    // zwei Bretter in der Farbe — geschätzt
        Assert.Contains(pick.Id, new[] { _r3[2], _r3[4] });
        Assert.Equal(pick.Id, LeaguePairingFinder.AutoPick(await new LeaguePairingFinder(_db) { Today = () => new DateOnly(2026, 10, 24) }
            .ForAsync(new("Fercher, Alois", "1621947", "Testdorf", "530005108", new DateOnly(2026, 10, 24), 2026), TestClubs.Home, default)));
    }

    [Fact]
    public async Task AutoPick_nimmtDiePaarungOhneEigeneMannschaft_nie_auchOhneLaufendeRunde()
    {
        await SeedAsync();
        // Runde 3 ist vorbei und noch immer leer (> 21 Tage) — die alte Runde 1 des Gegners darf trotzdem nicht kommen
        var finder = new LeaguePairingFinder(_db) { Today = () => new DateOnly(2026, 12, 24) };
        var options = await finder.ForAsync(new("Fercher, Alois", "1621947", "Testdorf", "530005108", null, 2026), TestClubs.Home, default);

        Assert.DoesNotContain(options, o => o.Id == _r1Fercher);
        Assert.Null(LeaguePairingFinder.AutoPick(options));
        // ohne eigene Seite (zwei Fremde) bleibt die alte Paarung ein Vorschlag
        Assert.Contains(await finder.ForAsync(new("Fercher, Alois", "1621947", "Heim 1", "771", null, 2026), TestClubs.Home, default),
            o => o.Id == _r1Fercher);
    }

    [Fact]
    public async Task VierFormulare_derselbenBegegnung_verteilenSichNachMeldelisteUndFarbe()
    {
        await SeedAsync();
        var fercher = await AddAsync(Sheet(0, "Fercher, Alois", "1621947", "Katkic, Samuel", "530005108", whiteOwn: false));
        var markus = await AddAsync(Sheet(1, "Markus, Robert", "921637", "Moser, Niklas William", "530021065", whiteOwn: false));   // Gegner unbekannt
        var spierings = await AddAsync(Sheet(2, "Erlacher, Herbert", "1614533", "Spierings, Benjamin", "1671219", whiteOwn: true));
        var rupp = await AddAsync(Sheet(3, "Ober, David", "530014190", "Rupprechter, Alfred", "1682679", whiteOwn: true));

        var all = await _db.LeagueClubGames.AsNoTracking().ToDictionaryAsync(g => g.Id);
        Assert.All(all.Values, g => Assert.True(g.LeaguePairingProvisional));
        Assert.Equal(new[] { 1, 2, 3, 4 }, new[] { spierings, fercher, rupp, markus }.Select(g => BoardOf(all[g.Id])));
    }

    [Fact]
    public async Task NachDemAktualisieren_passendesBrett_wirdBestaetigt()
    {
        await SeedAsync();
        var g = await AddAsync(Sheet(0, "Fercher, Alois", "1621947", "Katkic, Samuel", "530005108", whiteOwn: false));
        Fill(BoardOf(g), home: ("Katkic, Samuel", "530005108"), away: ("Fercher, Alois", "1621947"));
        var log = new CapturingLogger<LeagueProvisionalPairingTests>();

        await LeagueGameLinks.RelinkAsync(_db, Tnr, default, log);

        var after = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.False(after.LeaguePairingProvisional);
        Assert.Equal(g.LeagueGameId, after.LeagueGameId);
        Assert.Contains(log.Events, e => e.Level == LogLevel.Information && e.Message.Contains("bestätigt"));
    }

    [Fact]
    public async Task NachDemAktualisieren_GegnerAnAnderemBrett_wirdUmgehaengtUndBestaetigt()
    {
        await SeedAsync();
        var g = await AddAsync(Sheet(0, "Fercher, Alois", "1621947", "Katkic, Samuel", "530005108", whiteOwn: false));
        var other = BoardOf(g) == 2 ? 4 : 2;
        Fill(BoardOf(g), home: ("Ober, David", "530014190"), away: ("Guseck, Markus", "1642510"));
        Fill(other, home: ("Katkic, Samuel", "530005108"), away: ("Fercher, Alois", "1621947"));

        await LeagueRefreshRelink();

        var after = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.False(after.LeaguePairingProvisional);
        Assert.Equal((_r3[other], other), (after.LeagueGameId!.Value, after.LeagueBoard!.Value));
    }

    [Fact]
    public async Task NachDemAktualisieren_SpielerNirgendsInDerBegegnung_ZuordnungGeloest_mitWarnung()
    {
        await SeedAsync();
        var g = await AddAsync(Sheet(0, "Fercher, Alois", "1621947", "Katkic, Samuel", "530005108", whiteOwn: false));
        foreach (var b in new[] { 1, 2, 3, 4 }) Fill(b, home: ($"Testdorf {b}", $"55{b}"), away: ($"Jenbach {b}", $"66{b}"));
        var log = new CapturingLogger<LeagueProvisionalPairingTests>();

        await LeagueGameLinks.RelinkAsync(_db, Tnr, default, log);

        var after = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.Null(after.LeagueGameId);
        Assert.Null(LeagueGameLinks.KeyOf(after));
        Assert.False(after.LeaguePairingProvisional);
        Assert.Contains(log.Events, e => e.Level == LogLevel.Warning && e.Message.Contains("gelöst"));
    }

    [Fact]
    public async Task NochLeer_bleibtVorlaeufig_undVonHandBestaetigen()
    {
        await SeedAsync();
        var g = await AddAsync(Sheet(0, "Fercher, Alois", "1621947", "Katkic, Samuel", "530005108", whiteOwn: false));
        await LeagueGameLinks.RelinkAsync(_db, Tnr, default);
        Assert.True((await _db.LeagueClubGames.AsNoTracking().SingleAsync()).LeaguePairingProvisional);

        var list = await Club().ListAsync(TestClubs.Home, 0, true, null, null, 1, default);
        Assert.True(Assert.Single(list.Items).LeaguePairingProvisional);

        Assert.Equal((true, (string?)null), await Club().ConfirmPairingAsync(TestClubs.Home, 0, true, g.Id));
        Assert.False((await _db.LeagueClubGames.AsNoTracking().SingleAsync()).LeaguePairingProvisional);
        Assert.Equal((false, (string?)null), await Club().ConfirmPairingAsync(TestClubs.Home, 0, true, g.Id));
        Assert.Equal("notFound", (await Club().ConfirmPairingAsync(TestClubs.Other, 0, true, g.Id)).Reason);
    }

    [Fact]
    public async Task AndererVerein_bekommtKeineVorlaeufigeZuordnung()
    {
        await SeedAsync();
        await TestClubs.SeedAsync(_db);
        var list = await Club().SuggestPairingsAsync(TestClubs.Other, new LeagueClubPairingQuery
            { White = "Fercher, Alois", WhiteFide = "1621947", Black = "Weiler", Date = "2026.10.24" }, default);

        Assert.DoesNotContain(list, p => p.Provisional || p.Open);
    }

    [Fact]
    public async Task Nachtraeglich_PartieOhneZuordnung_bekommtDieVorlaeufige()
    {
        await SeedAsync();
        await TestClubs.SeedAsync(_db);
        var g = await AddAsync(Sheet(0, "Fercher, Alois", "1621947", "Katkic, Samuel", "530005108", whiteOwn: false));
        var row = await _db.LeagueClubGames.SingleAsync();
        LeagueGameLinks.Set(row, null);
        await _db.SaveChangesAsync();

        var (found, id) = await Club().AssignPairingIfUnlinkedAsync(g.Id, default, TestClubs.HomeId);

        Assert.True(found);
        Assert.Equal(g.LeagueGameId, id);
        Assert.True((await _db.LeagueClubGames.AsNoTracking().SingleAsync()).LeaguePairingProvisional);
        Assert.False((await Club().AssignPairingIfUnlinkedAsync(g.Id, default, TestClubs.OtherId)).Found);   // anderer Verein
    }

    [Fact]
    public void Fits_FarbenUndFideIds()
    {
        var g = new LeagueGame { HomeColor = "s", HomeFide = "1", AwayFide = "2" };   // Gast Weiß
        Assert.True(LeagueProvisionalPairing.Fits(g, "2", "1"));
        Assert.True(LeagueProvisionalPairing.Fits(g, "2", null));
        Assert.False(LeagueProvisionalPairing.Fits(g, "1", "2"));   // Farben vertauscht
        Assert.False(LeagueProvisionalPairing.Fits(g, "2", "9"));   // anderer Gegner
        Assert.False(LeagueProvisionalPairing.Fits(g, null, null));
    }

    private Task LeagueRefreshRelink() => LeagueGameLinks.RelinkAsync(_db, Tnr, default);

    /// <summary>chess-results hat die Aufstellung: Spieler an ein Brett der Runde 3 (Heim Weiß an ungeraden).</summary>
    private void Fill(int board, (string Name, string Fide) home, (string Name, string Fide) away)
    {
        var g = _db.LeagueGames.Single(x => x.Id == _r3[board]);
        (g.HomePlayer, g.HomeFide, g.AwayPlayer, g.AwayFide) = (home.Name, home.Fide, away.Name, away.Fide);
        g.HomeColor = board % 2 == 1 ? "w" : "s";
        _db.SaveChanges();
    }
}
