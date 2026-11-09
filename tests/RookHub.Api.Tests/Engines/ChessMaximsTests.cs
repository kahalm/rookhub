using Microsoft.EntityFrameworkCore;
using RookHub.Api.Services.Engines;
using Xunit;

namespace RookHub.Api.Tests.Engines;

/// <summary>Die kuratierte Merksatz-Liste (0.917.0): lädt, ist eindeutig, belegt; Modell-Antworten werden auf den Wortlaut gebracht.</summary>
public class ChessMaximsTests
{
    [Fact]
    public void Liste_laedt_eindeutig_und_jeder_Satz_ist_belegt()
    {
        var all = ChessMaxims.All;
        Assert.InRange(all.Count, 30, 80);
        Assert.Equal(all.Count, all.Select(m => m.Id).Distinct().Count());
        Assert.Equal(all.Count, all.Select(m => m.Text).Distinct().Count());
        Assert.All(all, m =>
        {
            Assert.Matches("^[a-z0-9-]+$", m.Id);
            Assert.False(string.IsNullOrWhiteSpace(m.Text));
            Assert.False(string.IsNullOrWhiteSpace(m.When));
            Assert.NotEmpty(m.Sources);
            Assert.All(m.Sources, s => Assert.True(s.EbookId > 0));
        });
        // Die beiden Beispiele des Users (09.11.) sind drin.
        Assert.NotNull(ChessMaxims.ById("raum-und-tausch"));
        Assert.NotNull(ChessMaxims.ById("laeufer-bauern-andere-farbe"));
    }

    [Fact]
    public void Jeder_Satz_hat_eine_vollstaendige_englische_Fassung()
    {
        var all = ChessMaxims.All;
        Assert.All(all, m =>
        {
            Assert.False(string.IsNullOrWhiteSpace(m.TextEn), m.Id);
            Assert.False(string.IsNullOrWhiteSpace(m.CategoryEn), m.Id);
            Assert.False(string.IsNullOrWhiteSpace(m.WhenEn), m.Id);
            // Kein deutscher Rest und keine versehentlich übernommene Quelle.
            Assert.DoesNotMatch("[äöüÄÖÜß„“]", m.TextEn + m.CategoryEn + m.WhenEn);
            Assert.NotEqual(m.Text, m.TextEn);
            Assert.NotEqual(m.When, m.WhenEn);
        });
        Assert.Equal(all.Count, all.Select(m => m.TextEn).Distinct().Count());
        // Dieselbe deutsche Kategorie hat genau eine englische.
        Assert.All(all.GroupBy(m => m.Category), g => Assert.Single(g.Select(m => m.CategoryEn).Distinct()));
    }

    [Fact]
    public void Canonicalize_Kennung_wird_Wortlaut_Erfundenes_faellt_weg()
    {
        var m = ChessMaxims.ById("raum-und-tausch")!;
        Assert.Equal("Absatz eins.\n\nAbsatz zwei.\n\nMerksatz: " + m.Text,
            ChessMaxims.Canonicalize("Absatz eins.\n\nAbsatz zwei.\n\nMerksatz: raum-und-tausch"));
        Assert.Equal("Absatz eins.\n\nMerksatz: " + m.Text,
            ChessMaxims.Canonicalize("Absatz eins.\n\n**Merksatz:** „" + m.Text + "“"));
        Assert.Equal("Absatz eins.", ChessMaxims.Canonicalize("Absatz eins.\n\nMerksatz: Erst denken, dann ziehen."));
        Assert.Equal("Absatz eins.", ChessMaxims.Canonicalize("Absatz eins."));
    }

    [Fact]
    public void PDF_Fundstellen_tragen_eine_Seite()
    {
        var pdf = ChessMaxims.All.SelectMany(m => m.Sources).Where(s => s.Location?.StartsWith("PDF-S.") == true).ToList();
        Assert.NotEmpty(pdf);
        Assert.All(pdf, s => Assert.True(s.Page > 0));
    }
}

/// <summary>GET /api/maxims (0.917.0): je Buchstelle, ob der Aufrufer das Buch öffnen darf.</summary>
public class ChessMaximsControllerTests : IDisposable
{
    private readonly RookHub.Api.Data.AppDbContext _db = new(new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<RookHub.Api.Data.AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Anonym_sieht_nur_fuer_alle_freigegebene_Buecher_als_oeffenbar()
    {
        var src = ChessMaxims.ById("raum-und-tausch")!.Sources;
        var open = src[0].EbookId;
        var closed = src.Select(s => s.EbookId).First(id => id != open);
        var everyone = new RookHub.Api.Models.Group { Name = "Everyone", IsEveryone = true, CreatedAt = DateTime.UtcNow };
        _db.Groups.Add(everyone);
        _db.Ebooks.Add(new RookHub.Api.Models.Ebook { Id = open, Title = "offen", Source = RookHub.Api.Models.EbookSource.Library });
        _db.Ebooks.Add(new RookHub.Api.Models.Ebook { Id = closed, Title = "zu", Source = RookHub.Api.Models.EbookSource.Library });
        await _db.SaveChangesAsync();
        _db.EbookGroupAccesses.Add(new RookHub.Api.Models.EbookGroupAccess { EbookId = open, GroupId = everyone.Id });
        await _db.SaveChangesAsync();

        var ctl = new RookHub.Api.Controllers.Engines.ChessMaximsController(_db)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext() },
        };
        var res = await ctl.List(CancellationToken.None);
        var list = Assert.IsType<List<RookHub.Api.DTOs.Engines.ChessMaximDto>>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(res.Result).Value);
        var m = list.Single(x => x.Id == "raum-und-tausch");
        Assert.All(m.Sources.Where(s => s.EbookId == open), s => Assert.True(s.Accessible));
        Assert.All(m.Sources.Where(s => s.EbookId == closed), s => Assert.False(s.Accessible));
        Assert.Equal(ChessMaxims.All.Count, list.Count);
        Assert.Equal(ChessMaxims.ById("raum-und-tausch")!.TextEn, m.TextEn);
        Assert.False(string.IsNullOrWhiteSpace(m.WhenEn));
        Assert.False(string.IsNullOrWhiteSpace(m.CategoryEn));
    }
}

/// <summary>„Empfohlene Literatur“ (Wunsch 2026-11-09): nur Bücher der Merksatz-Belege, Sperre je Konto.</summary>
public class EbookSuggestionsTests : IDisposable
{
    private readonly RookHub.Api.Data.AppDbContext _db = new(new DbContextOptionsBuilder<RookHub.Api.Data.AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private const string RealName = "Geheim, Klarname";
    private const string RealFide = "76543210";

    private RookHub.Api.Services.Ebooks.EbookSuggestions Strength(params RookHub.Api.Models.LeagueClubGame[] games)
    {
        _db.LeagueClubs.Add(new RookHub.Api.Models.LeagueClub { Id = 1, Name = "Schwaz", AnonName = "Schwaz", TeamPrefix = "Schwaz" });
        _db.LeagueClubGames.AddRange(games);
        _db.SaveChanges();
        return new(_db);
    }

    private static RookHub.Api.Models.LeagueClubGame Game(int id, string white, string black) => new()
    {
        Id = id, ClubId = 1, Year = 2026, White = white, Black = black, Result = "1-0", Pgn = "1. e4 *", MovesHash = "h" + id,
    };

    private void Roster(string fide, int? eloI, int? eloN = null, string season = "2025/26", int tnr = 1)
    {
        if (!_db.LeagueTournaments.Any(t => t.Tnr == tnr))
            _db.LeagueTournaments.Add(new RookHub.Api.Models.LeagueTournament { Tnr = tnr, Name = "Liga", Season = season, League = "L", Grp = "G", Stage = "S" });
        _db.LeaguePlayers.Add(new RookHub.Api.Models.LeaguePlayer { Tnr = tnr, Team = "T", Name = "X", NameKey = "x", FideId = fide, EloI = eloI, EloN = eloN });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Anonymisierte_Seite_liefert_nur_das_Band_ohne_Name_und_Fide()
    {
        var g = Game(1, "Schwaz", "Gegner, Otto");
        g.WhiteRealName = RealName; g.WhiteRealFide = RealFide; g.BlackFide = "111"; g.Anonymized = true;
        var svc = Strength(g);
        Roster(RealFide, 1754);
        Roster("111", null, 1612);

        var s = await svc.StrengthAsync("1");
        Assert.Equal(new RookHub.Api.Services.Ebooks.PlayerStrength(null, "1700–1799"), s!.White);
        Assert.Equal(new RookHub.Api.Services.Ebooks.PlayerStrength(1612, null), s.Black);
        var json = System.Text.Json.JsonSerializer.Serialize(s);
        Assert.DoesNotContain(RealFide, json);
        Assert.DoesNotContain("Geheim", json);
        Assert.DoesNotContain("1754", json);
    }

    [Fact]
    public async Task Gespeicherte_Elo_der_anonymen_Seite_wird_ebenfalls_zum_Band()
    {
        var g = Game(1, "Schwaz", "Gegner"); g.WhiteElo = 1899; g.BlackElo = 2001; g.Anonymized = true;
        var s = await Strength(g).StrengthAsync("1");
        Assert.Equal("1800–1899", s!.White!.Band);
        Assert.Null(s.White.Elo);
        Assert.Equal(2001, s.Black!.Elo);
    }

    [Fact]
    public async Task Ligapaarung_liefert_Elo_je_Farbe_und_Unbekanntes_bleibt_null()
    {
        var linked = Game(1, "Schwaz", "Gegner"); linked.Anonymized = true; linked.LeagueGameId = 5;
        var none = Game(2, "Schwaz", "Niemand"); none.Anonymized = true;
        var svc = Strength(linked, none);
        _db.LeagueTournaments.Add(new RookHub.Api.Models.LeagueTournament { Tnr = 9, Name = "Liga", Season = "2025/26", League = "L", Grp = "G", Stage = "S" });
        _db.LeagueGames.Add(new RookHub.Api.Models.LeagueGame { Id = 5, Tnr = 9, Round = 1, MatchNo = 1, Board = 1, HomeTeam = "Wörgl", AwayTeam = "Schwaz", HomeColor = "s", HomeElo = 1500, AwayElo = 2105 });
        _db.SaveChanges();

        var s = await svc.StrengthAsync("1");
        Assert.Equal("2100–2199", s!.White!.Band);
        Assert.Equal(1500, s.Black!.Elo);
        var n = await svc.StrengthAsync("2");
        Assert.Null(n!.White);
        Assert.Null(n.Black);
        Assert.Null(await svc.StrengthAsync("99"));
        Assert.Null(await svc.StrengthAsync("abc"));
    }

    [Fact]
    public async Task Einzige_genaue_Paarung_liefert_das_Band_ohne_interne_Fide()
    {
        var g = Game(1, "Schwaz", "Gegner, Otto"); g.BlackFide = "111"; g.Anonymized = true;
        var svc = Strength(g);
        _db.LeagueTournaments.Add(new RookHub.Api.Models.LeagueTournament { Tnr = 9, Name = "Liga", Season = "2026/27", League = "L", Grp = "G", Stage = "S" });
        _db.LeagueGames.Add(new RookHub.Api.Models.LeagueGame
        {
            Id = 5, Tnr = 9, Round = 1, MatchNo = 1, Board = 1, HomeTeam = "Schwaz 1", AwayTeam = "Wörgl", HomeColor = "w", HomePlayer = "Klar, Name", AwayPlayer = "Gegner, Otto",
            HomeFide = "222", AwayFide = "111", HomeElo = 1650, AwayElo = 1700, Result = "1 - 0",
        });
        _db.SaveChanges();

        var s = await svc.StrengthAsync("1");
        Assert.Equal("1600–1699", s!.White!.Band);
        Assert.Null(s.Black);   // geratene Paarung nur für die anonyme Seite
    }

    [Fact]
    public async Task Meldeliste_nimmt_die_Saison_am_naechsten_zur_Partie()
    {
        var g = Game(1, "Gegner A", "Gegner B"); g.WhiteFide = "111";
        var svc = Strength(g);
        Roster("111", 1400, null, "2019/20", 1);
        Roster("111", 1800, null, "2025/26", 2);
        Assert.Equal(1800, (await svc.StrengthAsync("1"))!.White!.Elo);
    }

    [Fact]
    public async Task Eigene_Partie_nur_gespeicherte_Elo()
    {
        var svc = new RookHub.Api.Services.Ebooks.EbookSuggestions(_db);
        _db.SavedGames.Add(new RookHub.Api.Models.SavedGame { Id = 7, UserId = 1, Pgn = "*", WhiteElo = 1500, BlackElo = 0 });
        _db.SaveChanges();
        var s = await svc.StrengthAsync("o7");
        Assert.Equal(1500, s!.White!.Elo);
        Assert.Null(s.Black);
        Assert.Null(await svc.StrengthAsync("o8"));
    }

    [Fact]
    public void Strength_nur_fuer_Admins()
    {
        // Seit R1-004 live über das Admin-Flag statt über die Rolle im Token.
        var a = typeof(RookHub.Api.Controllers.Ebooks.EbookSuggestionsController).GetMethod("Strength")!
            .GetCustomAttributes(typeof(RookHub.Api.Authorization.HasPermissionAttribute), false)
            .Cast<RookHub.Api.Authorization.HasPermissionAttribute>().Single();
        Assert.Equal("perm:" + RookHub.Api.Models.Permissions.AdminOnly, a.Policy);
    }

    [Fact]
    public async Task Sperre_gilt_je_Konto_fremde_Buecher_bleiben_unsichtbar()
    {
        var known = ChessMaxims.ById("raum-und-tausch")!.Sources[0].EbookId;
        const int foreign = 987654;
        _db.Ebooks.Add(new RookHub.Api.Models.Ebook { Id = known, Title = "Belegt", Source = RookHub.Api.Models.EbookSource.Library });
        _db.Ebooks.Add(new RookHub.Api.Models.Ebook { Id = foreign, Title = "Privat", Source = RookHub.Api.Models.EbookSource.Library });
        await _db.SaveChangesAsync();
        var svc = new RookHub.Api.Services.Ebooks.EbookSuggestions(_db);

        Assert.False(await svc.SetBlockedAsync(1, foreign, true, DateTime.UtcNow));
        var info = await svc.InfoAsync([known, foreign], 1, false);
        Assert.Equal((known, false, false), (Assert.Single(info).Id, info[0].Blocked, info[0].Accessible));

        Assert.True(await svc.SetBlockedAsync(1, known, true, DateTime.UtcNow));
        Assert.True((await svc.InfoAsync([known], 1, false))[0].Blocked);
        Assert.False((await svc.InfoAsync([known], 2, false))[0].Blocked);
        Assert.Empty(await svc.CandidatesAsync(1));
        var c = Assert.Single(await svc.CandidatesAsync(2));
        Assert.Contains("raum-und-tausch", c.Maxims);

        Assert.True(await svc.SetBlockedAsync(1, known, false, DateTime.UtcNow));
        Assert.Single(await svc.CandidatesAsync(1));
    }
}
