using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Tests;

/// <summary>
/// Spielervorbereitung, Phase 2 (2026-10-01): Spieler suchen und seine Karte lesen — Profil, Baum, letzte Partien, PGN in
/// der Form der Liga-Karte; mit FIDE-ID die Quellen von LeagueHub ohne Dubletten, die Online-Konten wie dort (Minderjährige
/// verborgen), die Grenze auf die jüngsten Partien und der Namens-Zwilling (nur auf Wunsch). FIDE-IDs im 99xxxx-Bereich.
/// </summary>
public class PrepCardTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }

    private const string Najdorf = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7";
    private const string Nimzo = "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4";
    private const string Spanish = "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6";
    private const string English = "1. c4 e5 2. Nc3 Nf6";
    private const string Caro = "1. e4 c6 2. d4 d5 3. Nc3 dxe4 4. Nxe4 Bf5 5. Ng3 Bg6 6. h4 h6 7. Nf3 Nd7 8. h5 Bh7 9. Bd3 Bxd3 "
        + "10. Qxd3 e6 11. Bd2 Ngf6 12. O-O-O Be7";

    private static string Game(string white, string black, string moves, string result, string date, string? whiteFide = null,
        string? blackFide = null, string evt = "Open Schwaz") =>
        $"[Event \"{evt}\"]\n[Site \"Schwaz\"]\n[Date \"{date}\"]\n[Round \"1\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n"
        + $"[Result \"{result}\"]\n[WhiteElo \"2210\"]\n[BlackElo \"2105\"]\n"
        + (whiteFide is null ? "" : $"[WhiteFideId \"{whiteFide}\"]\n") + (blackFide is null ? "" : $"[BlackFideId \"{blackFide}\"]\n")
        + "\n" + moves + " " + result + "\n\n";

    /// <summary>Huber (990001) mit drei Brettpartien, dazu sein Namens-Zwilling ohne FIDE-ID mit einer.</summary>
    private async Task SeedHuberAsync()
    {
        var pgn = Game("Huber, Franz", "Mair, Josef", Najdorf, "1-0", "2024.05.17", "990001", "990002")
            + Game("Gruber, Karl", "Huber, Franz", Nimzo, "0-1", "2023.03.01", blackFide: "990001")
            + Game("Huber, Franz", "Gruber, Karl", Spanish, "1/2-1/2", "2022.??.??", "990001")
            + Game("Huber, Franz", "Pichler, Anna", Caro, "1-0", "2021.06.01", "990001")
            + Game("Huber, Franz", "Mair, Josef", English, "1-0", "2010.01.01", blackFide: "990002");     // Zwilling ohne FIDE-ID
        await new PrepImportService(_db).ImportChunkAsync(PrepSources.Mega, 0, 0, pgn, default);
    }

    private PrepCardService Cards(int limit = PrepCardService.DefaultLimit, int max = PrepCardService.DefaultMax) =>
        new(_db, _cache, new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance), limit, max);

    private Task<PrepPlayer> Player(string fide) => _db.PrepPlayers.SingleAsync(p => p.FideId == fide);

    private static readonly LeagueProfileStore.TreeFilter Board = LeagueProfileStore.TreeFilter.Default;

    // ── Suche ──────────────────────────────────────────────────────────────────────────────────

    private async Task<List<PrepPlayerSearch.Hit>> Search(string q, int take = 25) => await new PrepPlayerSearch(_db).SearchAsync(q, take, default);

    [Fact]
    public async Task Search_SurnamePrefix_MostGamesFirst()
    {
        await SeedHuberAsync();
        var hits = await Search("hub");
        Assert.Equal(new[] { "990001", null }, hits.Select(h => h.FideId));   // FIDE-Huber 4 Partien, Zwilling 1
        Assert.Equal(4, hits[0].Games);
        Assert.Equal((short)2021, hits[0].FirstYear);
        Assert.Equal((short)2024, hits[0].LastYear);
    }

    [Theory]
    [InlineData("Huber, Franz")]
    [InlineData("Huber, F")]
    [InlineData("Franz Huber")]
    [InlineData("Huber Franz")]
    [InlineData("HUBER")]
    [InlineData("FM Huber, Franz")]
    public async Task Search_NameForms_FindHuber(string q)
    {
        await SeedHuberAsync();
        Assert.Contains(await Search(q), h => h.FideId == "990001");
    }

    [Fact]
    public async Task Search_WrongFirstName_NoHit()
    {
        await SeedHuberAsync();
        Assert.Empty(await Search("Huber, Josef"));
        Assert.Empty(await Search("Josef Huber"));
    }

    [Fact]
    public async Task Search_Umlauts_BothWays()
    {
        // Lumbra schreibt „Höcher" (Schlüssel „hocher"), die Megabase „Hoecher".
        var pgn = Game("Höcher, Michael", "Mair, Josef", Najdorf, "1-0", "2020.01.01")
            + Game("Hoecher, Martin", "Mair, Josef", Nimzo, "1-0", "2020.01.02");
        await new PrepImportService(_db).ImportChunkAsync(PrepSources.Lumbra, 0, 0, pgn, default);

        Assert.Equal(new[] { "Höcher, Michael", "Hoecher, Martin" }.OrderBy(x => x), (await Search("Höcher")).Select(h => h.Name).OrderBy(x => x));
        Assert.Equal(new[] { "Höcher, Michael", "Hoecher, Martin" }.OrderBy(x => x), (await Search("Hoecher")).Select(h => h.Name).OrderBy(x => x));
        Assert.Equal("Höcher, Michael", Assert.Single(await Search("Hoecher, Mi")).Name);
    }

    [Fact]
    public async Task Search_ReverseSpellingOnly_RanksLast()
    {
        // „Michael" fragt auch „michal" (Rückschreibweise) — der Treffer nur darüber steht hinten, auch mit mehr Partien.
        var pgn = Game("Michal, Jan", "Mair, Josef", Najdorf, "1-0", "2020.01.01") + Game("Michal, Jan", "Mair, Josef", Nimzo, "1-0", "2020.01.02")
            + Game("Michael, Peter", "Mair, Josef", Spanish, "1-0", "2020.01.03");
        await new PrepImportService(_db).ImportChunkAsync(PrepSources.Lumbra, 0, 0, pgn, default);
        Assert.Equal(new[] { "Michael, Peter", "Michal, Jan" }, (await Search("Michael")).Select(h => h.Name));
    }

    [Fact]
    public async Task Search_FideId_AndTooShort()
    {
        await SeedHuberAsync();
        Assert.Equal("Huber, Franz", Assert.Single(await Search("990001")).Name);
        Assert.Empty(await Search("h"));
        Assert.Empty(await Search("  "));
        Assert.Empty(await Search("%"));
    }

    [Fact]
    public void Search_Prefixes_PreciseBeforeLoose()
    {
        var p = PrepPlayerSearch.Parse("Magnus Carlsen")!;
        var (precise, loose) = PrepPlayerSearch.Prefixes(p);
        Assert.Contains("carlsen, magnus", precise);
        Assert.Contains("magnus, carlsen", precise);
        Assert.Contains("magnus carlsen", precise);
        Assert.Equal(new[] { "magnus", "carlsen" }, loose);
        Assert.Equal("hocher", PrepPlayerSearch.Reverse("hoecher"));
        Assert.Null(PrepPlayerSearch.Reverse("huber"));
        Assert.Equal("a\\%b\\_c\\\\", PrepPlayerSearch.Escape("a%b_c\\"));
    }

    // ── Karte ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Card_WithoutFide_ProfileRecentNoAccounts()
    {
        await SeedHuberAsync();
        var gruber = await _db.PrepPlayers.SingleAsync(p => p.NameKey == "gruber, karl");
        var cards = Cards();
        var l = (await cards.LoadAsync(gruber.Id, false, false, default))!;
        var c = await cards.CardAsync(l, false, default);

        Assert.Equal(gruber.Id, c["id"]!.GetValue<int>());
        Assert.Null(c["fide"]);
        Assert.Equal(2, c["n"]!.GetValue<int>());
        Assert.Equal(1, c["white"]!["n"]!.GetValue<int>());            // Nimzo mit Weiß
        Assert.Equal("d4", c["white"]!["first"]![0]![0]!.GetValue<string>());
        Assert.Equal(1, c["black_e4"]!["n"]!.GetValue<int>());         // Spanisch mit Schwarz
        Assert.Equal(new[] { "2023.03.01", "2022.??.??" }, c["recent"]!.AsArray().Select(r => r!["date"]!.GetValue<string>()));
        Assert.Equal("Huber, Franz", c["recent"]![0]!["vs"]!.GetValue<string>());
        Assert.Equal(0.0, c["recent"]![0]!["score"]!.GetValue<double>());
        Assert.Empty(c["accounts"]!.AsArray());
        Assert.Null(c["twin"]);
        Assert.False(c["limited"]!.GetValue<bool>());
        Assert.Equal(2, c["src"]!["Mega"]!.GetValue<int>());            // beide Partien aus der Megabase
    }

    [Fact]
    public async Task Card_UnknownId_Null()
    {
        Assert.Null(await Cards().LoadAsync(999_999, false, false, default));
    }

    [Fact]
    public async Task Tree_CountsScoresAndPrefix()
    {
        await SeedHuberAsync();
        var huber = await Player("990001");
        var cards = Cards();
        var l = (await cards.LoadAsync(huber.Id, false, false, default))!;

        var root = await cards.TreeAsync(l, "w", null, Board, default);
        Assert.Equal(3, root["total"]!.GetValue<int>());
        var moves = root["moves"]!.AsArray();
        Assert.Equal("e4", moves[0]!["san"]!.GetValue<string>());
        Assert.Equal(3, moves[0]!["n"]!.GetValue<int>());
        Assert.Equal(83, moves[0]!["score"]!.GetValue<int>());            // 1 + 0,5 + 1 aus 3
        Assert.Equal("2024", moves[0]!["last"]!.GetValue<string>());

        var e4 = await cards.TreeAsync(l, "w", "e4", Board, default);
        Assert.Equal(new[] { "c5", "e5", "c6" }.OrderBy(x => x), e4["moves"]!.AsArray().Select(m => m!["san"]!.GetValue<string>()).OrderBy(x => x));

        var black = await cards.TreeAsync(l, "s", "d4 Nf6", Board, default);
        Assert.Equal("c4", Assert.Single(black["moves"]!.AsArray())!["san"]!.GetValue<string>());
        Assert.Equal(100, black["moves"]![0]!["score"]!.GetValue<int>());

        var full = await cards.TreeAsync(l, "s", "d4 Nf6 c4 e6 Nc3 Bb4", Board, default);
        Assert.Equal(1, full["ended"]!.GetValue<int>());
    }

    [Fact]
    public async Task Tree_YearsFilter_DropsOlderGames()
    {
        await SeedHuberAsync();
        var cards = Cards();
        var l = (await cards.LoadAsync((await Player("990001")).Id, false, false, default))!;
        var since = DateTime.UtcNow.Year - 2024 + 1;    // nur ab 2024
        var t = await cards.TreeAsync(l, "w", null, LeagueProfileStore.TreeFilter.Parse("board", null, since, false), default);
        Assert.Equal(1, t["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task Limit_YoungestOnly_AllOnRequest()
    {
        await SeedHuberAsync();
        var huber = await Player("990001");
        var cards = Cards(limit: 2);
        var l = (await cards.LoadAsync(huber.Id, false, false, default))!;
        Assert.True(l.Limited);
        Assert.Equal(2, l.PrepLoaded);
        Assert.Equal(4, l.PrepTotal);
        Assert.Equal(new[] { 20240517, 20230301 }, l.Games.Select(g => g.PlayedOn!.Value));
        var c = await cards.CardAsync(l, false, default);
        Assert.Equal("2023.03.01", c["since"]!.GetValue<string>());
        Assert.Equal(2, c["limit"]!.GetValue<int>());
        Assert.Equal(PrepCardService.DefaultMax, c["max"]!.GetValue<int>());

        var all = (await cards.LoadAsync(huber.Id, true, false, default))!;
        Assert.False(all.Limited);
        Assert.Equal(4, all.PrepLoaded);
        Assert.Equal(PrepCardService.DefaultMax, (await cards.CardAsync(all, false, default))["limit"]!.GetValue<int>());
    }

    [Fact]
    public async Task Limit_AllCappedAtMax()
    {
        // „alle“ hat eine Obergrenze — die größten Spieler lägen kalt sonst über den 60 s von nginx.
        await SeedHuberAsync();
        var cards = Cards(limit: 1, max: 3);
        var all = (await cards.LoadAsync((await Player("990001")).Id, true, false, default))!;
        Assert.True(all.Limited);
        Assert.Equal(3, all.PrepLoaded);
        Assert.Equal(20220000, all.Since);
        Assert.Equal(1, (await cards.LoadAsync((await Player("990001")).Id, false, false, default))!.PrepLoaded);
    }

    [Theory]
    [InlineData(null, PrepCardService.DefaultLimit)]
    [InlineData("4000", 4000)]
    [InlineData("5", 100)]
    [InlineData("viele", PrepCardService.DefaultLimit)]
    public void Limit_FromConfig_Clamped(string? value, int expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [PrepCardService.LimitKey] = value }).Build();
        Assert.Equal(expected, PrepCardService.LimitFrom(config));
        Assert.Equal(PrepCardService.DefaultMax, PrepCardService.MaxFrom(config));
        var max = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [PrepCardService.MaxKey] = "8000" }).Build();
        Assert.Equal(8000, PrepCardService.MaxFrom(max));
    }

    [Fact]
    public async Task Twin_OfferedNotIncluded_IncludedOnRequest()
    {
        await SeedHuberAsync();
        var huber = await Player("990001");
        var cards = Cards();
        var plain = await cards.CardAsync((await cards.LoadAsync(huber.Id, false, false, default))!, false, default);
        Assert.Equal(4, plain["n"]!.GetValue<int>());
        Assert.Equal(1, plain["twin"]!["games"]!.GetValue<int>());
        Assert.False(plain["twinIncluded"]!.GetValue<bool>());

        var with = await cards.CardAsync((await cards.LoadAsync(huber.Id, false, true, default))!, false, default);
        Assert.Equal(5, with["n"]!.GetValue<int>());
        Assert.True(with["twinIncluded"]!.GetValue<bool>());
        Assert.Contains("c4", with["white"]!["first"]!.AsArray().Select(f => f![0]!.GetValue<string>()));
    }

    [Fact]
    public async Task Twin_TwoFidePlayersSameName_NotOffered()
    {
        await SeedHuberAsync();
        await new PrepImportService(_db).ImportChunkAsync(PrepSources.Lumbra, 0, 0,
            Game("Huber, Franz", "Mair, Josef", Caro, "0-1", "1999.01.01", "990009"), default);
        var cards = Cards();
        Assert.Null((await cards.LoadAsync((await Player("990001")).Id, false, true, default))!.Twin);
        Assert.Null((await cards.LoadAsync((await Player("990009")).Id, false, true, default))!.Twin);
    }

    [Fact]
    public async Task Twin_NotForPlayerWithoutFide()
    {
        await SeedHuberAsync();
        var twin = await _db.PrepPlayers.SingleAsync(p => p.NameKey == "huber, franz" && p.FideId == null);
        Assert.Null((await Cards().LoadAsync(twin.Id, false, true, default))!.Twin);
    }

    // ── Quellen von LeagueHub ──────────────────────────────────────────────────────────────────

    /// <summary>Liga-Karte von Huber: eine Dublette (gleiches Jahr + Züge), eine Abschrift mit falschem Datum und fehlenden
    /// letzten Zügen, eine eigene chess-results-Partie, eine Vereinspartie.</summary>
    private async Task SeedLeagueAsync()
    {
        // die Abschrift: falsches Jahr, die letzten zwei Halbzüge fehlen — die ersten 20 stimmen
        var caroShort = "1. e4 c6 2. d4 d5 3. Nc3 dxe4 4. Nxe4 Bf5 5. Ng3 Bg6 6. h4 h6 7. Nf3 Nd7 8. h5 Bh7 9. Bd3 Bxd3 10. Qxd3 e6 11. Bd2 Ngf6";
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile
        {
            FideId = "990001", Name = "Huber, Franz", ProfileJson = "{\"fide\":\"990001\",\"n\":3}",
            Pgn = Game("Huber, Franz", "Mair, Josef", Najdorf, "1-0", "2024.05.17")
                  + Game("Huber, Franz", "Pichler, A.", caroShort, "1-0", "2020.06.01")
                  + Game("Huber, Franz", "Moser, Eva", "1. e4 c6 2. d4 d5", "1-0", "2025.02.02"),
        });
        _db.LeagueClubGames.Add(new LeagueClubGame
        {
            Year = 2025, White = "Wolf, Max", Black = "Huber, Franz", BlackFide = "990001", Result = "0-1", Plies = 4,
            Pgn = Game("Wolf, Max", "Huber, Franz", "1. b3 e5 2. Bb2 Nc6", "0-1", "2025.03.03", blackFide: "990001"), MovesHash = "x",
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task League_Merged_WithoutDuplicates()
    {
        await SeedHuberAsync();
        await SeedLeagueAsync();
        var cards = Cards();
        var l = (await cards.LoadAsync((await Player("990001")).Id, false, false, default))!;
        Assert.Equal(6, l.Games.Count);                       // 4 Bestand + chess-results Moser + Verein Wolf
        var c = await cards.CardAsync(l, false, default);
        Assert.Equal(4, c["src"]!["Mega"]!.GetValue<int>());
        Assert.Equal(1, c["src"]!["chess-results"]!.GetValue<int>());
        Assert.Equal(1, c["src"]![LeagueProfileStore.ClubSource]!.GetValue<int>());
        // jüngste zuerst: Verein (2025-03), chess-results (2025-02), dann der Bestand
        Assert.Equal(new[] { "Wolf, Max", "Moser, Eva", "Mair, Josef" }, c["recent"]!.AsArray().Take(3).Select(r => r!["vs"]!.GetValue<string>()));
        var pgn = await cards.PgnAsync(l, default);
        Assert.Equal(6, PgnParser.SplitGames(pgn).Count());
    }

    [Fact]
    public async Task League_Limited_OnlySinceOldestLoaded()
    {
        await SeedHuberAsync();
        await SeedLeagueAsync();
        _db.LeaguePlayerProfiles.Single().Pgn += Game("Huber, Franz", "Alt, Otto", "1. f4 d5", "1-0", "2001.01.01");
        await _db.SaveChangesAsync();
        var l = (await Cards(limit: 2).LoadAsync((await Player("990001")).Id, false, false, default))!;
        Assert.DoesNotContain(l.Games, g => g.League?.Headers["Black"] == "Alt, Otto");
        Assert.Contains(l.Games, g => g.League?.Headers["Black"] == "Moser, Eva");
    }

    // ── Online-Konten (über LeagueHub) ─────────────────────────────────────────────────────────

    private async Task SeedOnlineAsync()
    {
        var sure = new LeagueOnlineAccount { FideId = "990001", Site = "lichess", UserName = "huberfranz", Url = "https://lichess.org/@/huberfranz",
            Confidence = LeagueOnlineAccountService.Sure, GameCount = 2, Evidence = "geheimer Kommentar" };
        var unsure = new LeagueOnlineAccount { FideId = "990001", Site = "chesscom", UserName = "hubi99", Url = "https://www.chess.com/member/hubi99",
            Confidence = LeagueOnlineAccountService.Unsure, GameCount = 1 };
        _db.LeagueOnlineAccounts.AddRange(sure, unsure);
        await _db.SaveChangesAsync();
        var at = new DateTime(DateTime.UtcNow.Year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        _db.LeagueOnlineGames.AddRange(
            new LeagueOnlineGame { AccountId = sure.Id, FideId = "990001", ExternalId = "a", PlayedAt = at, Speed = "blitz", White = true, Result = "1-0", Line = "d4 d5 c4", Moves = "d4 d5 c4", Plies = 3 },
            new LeagueOnlineGame { AccountId = sure.Id, FideId = "990001", ExternalId = "b", PlayedAt = at, Speed = "bullet", White = true, Result = "0-1", Line = "d4 Nf6", Moves = "d4 Nf6", Plies = 2 },
            new LeagueOnlineGame { AccountId = unsure.Id, FideId = "990001", ExternalId = "c", PlayedAt = at, Speed = "blitz", White = true, Result = "1-0", Line = "g3", Moves = "g3", Plies = 1 });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Online_FiltersLikeLeague()
    {
        await SeedHuberAsync();
        await SeedOnlineAsync();
        var cards = Cards();
        var l = (await cards.LoadAsync((await Player("990001")).Id, false, false, default))!;

        var both = await cards.TreeAsync(l, "w", null, LeagueProfileStore.TreeFilter.Parse("both", null, null, onlySure: true), default);
        Assert.Equal(2, both["online"]!.GetValue<int>());              // nur das gesicherte Konto
        Assert.Equal(3, both["board"]!.GetValue<int>());
        Assert.Equal(2, both["moves"]!.AsArray().Single(m => m!["san"]!.GetValue<string>() == "d4")!["n"]!.GetValue<int>());

        var d4 = await cards.TreeAsync(l, "w", "d4", LeagueProfileStore.TreeFilter.Parse("both", null, null, onlySure: true), default);
        Assert.Equal(2, d4["online"]!.GetValue<int>());
        Assert.Equal(new[] { "Nf6", "d5" }, d4["moves"]!.AsArray().Select(m => m!["san"]!.GetValue<string>()).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Equal(0, (await cards.TreeAsync(l, "w", "g3", LeagueProfileStore.TreeFilter.Parse("online", null, null, onlySure: true), default))["total"]!.GetValue<int>());

        var unsure = await cards.TreeAsync(l, "w", null, LeagueProfileStore.TreeFilter.Parse("online", null, null, onlySure: false), default);
        Assert.Equal(3, unsure["online"]!.GetValue<int>());
        Assert.Equal(0, unsure["board"]!.GetValue<int>());

        var blitz = await cards.ProfileAsync(l, LeagueProfileStore.TreeFilter.Parse("online", "blitz", null, onlySure: true), default);
        Assert.Equal(1, blitz["n"]!.GetValue<int>());

        // Verwalter (prep.manage): wie für LeagueHub-Leser — auch das unsichere Konto, mit Kommentar.
        var manager = await cards.CardAsync(l, true, default);
        Assert.Equal(2, manager["accounts"]!.AsArray().Count);
        Assert.Equal("geheimer Kommentar", manager["accounts"]![0]!["comment"]!.GetValue<string>());
        Assert.Equal(3, manager["online"]!.GetValue<int>());
        Assert.Equal(1, manager["onlineUnsure"]!.GetValue<int>());

        // Leser (nur prep.view): wie über einen Teilen-Link — nur das gesicherte Konto, kein Kommentar, keine Notizen.
        var viewer = await cards.CardAsync(l, false, default);
        var acc = Assert.Single(viewer["accounts"]!.AsArray())!;
        Assert.Equal("huberfranz", acc["user"]!.GetValue<string>());
        Assert.Null(acc["comment"]);
        Assert.DoesNotContain("hubi99", viewer.ToJsonString());
        Assert.DoesNotContain("geheimer", viewer.ToJsonString());
        Assert.Equal(2, viewer["online"]!.GetValue<int>());
        Assert.Equal(0, viewer["onlineUnsure"]!.GetValue<int>());
    }

    [Fact]
    public async Task Online_MinorsAccount_NeverLeaves()
    {
        await SeedHuberAsync();
        await SeedOnlineAsync();
        _db.LeagueAccountScans.Add(new LeagueAccountScan { FideId = "990001", BirthYear = DateTime.UtcNow.Year - 12, ScannedAt = DateTime.UtcNow, Version = 1 });
        await _db.SaveChangesAsync();
        var cards = Cards();
        var l = (await cards.LoadAsync((await Player("990001")).Id, false, false, default))!;
        var json = (await cards.CardAsync(l, false, default)).ToJsonString()
                   + (await cards.CardAsync(l, true, default)).ToJsonString()
                   + (await cards.TreeAsync(l, "w", null, LeagueProfileStore.TreeFilter.Parse("both", null, null, false), default)).ToJsonString()
                   + (await cards.ProfileAsync(l, LeagueProfileStore.TreeFilter.Parse("both", null, null, false), default)).ToJsonString()
                   + (await cards.RecentAsync(l, null, default)).ToJsonString()
                   + await cards.PgnAsync(l, default);
        foreach (var secret in new[] { "huberfranz", "hubi99", "lichess", "chesscom", "geheimer" })
            Assert.DoesNotContain(secret, json, StringComparison.OrdinalIgnoreCase);
        // Verwalter: nur, DASS es Konten gibt; Leser: gar keins (wie über einen Teilen-Link).
        var accounts = (await cards.CardAsync(l, true, default))["accounts"]!.AsArray();
        Assert.Equal(2, accounts.Count);
        Assert.All(accounts, a => Assert.True(a!["hidden"]!.GetValue<bool>()));
        Assert.Empty((await cards.CardAsync(l, false, default))["accounts"]!.AsArray());
    }

    // ── PGN und letzte Partien ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Pgn_HeadersMovesSource_Readable()
    {
        await SeedHuberAsync();
        await new PrepImportService(_db).ImportChunkAsync(PrepSources.Lumbra, 0, 0,
            Game("Huber, Franz", "Mair, Josef", Najdorf, "1-0", "2024.05.17", "990001", "990002"), default);
        var cards = Cards();
        var l = (await cards.LoadAsync((await Player("990001")).Id, false, false, default))!;
        var pgn = await cards.PgnAsync(l, default);
        var games = PgnParser.SplitGames(pgn).ToList();
        Assert.Equal(4, games.Count);
        var (h, moves) = games[0];
        Assert.Equal("Open Schwaz", h["Event"]);
        Assert.Equal("Schwaz", h["Site"]);
        Assert.Equal("2024.05.17", h["Date"]);
        Assert.Equal("Huber, Franz", h["White"]);
        Assert.Equal("990001", h["WhiteFideId"]);
        Assert.Equal("990002", h["BlackFideId"]);
        Assert.Equal("1-0", h["Result"]);
        Assert.Equal("Megabase, LumbrasGigaBase", h["Source"]);
        Assert.Equal("e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 a6 Be3 e5 Nb3 Be6 f3 Be7", string.Join(' ', PgnParser.ExtractMainlineSans(moves)));
        Assert.Equal("2022.??.??", games[2].Headers["Date"]);
        Assert.All(pgn.Split('\n'), line => Assert.True(line.Length <= 79 || line.StartsWith('['), line));

        var recent = await cards.RecentAsync(l, "s", default);
        var r = Assert.Single(recent["games"]!.AsArray())!;
        Assert.Equal("Gruber, Karl", r["vs"]!.GetValue<string>());
        Assert.Equal("1.d4 Nf6 2.c4 e6", r["opening"]!.GetValue<string>());
        Assert.Contains("[Black \"Huber, Franz\"]", r["pgn"]!.GetValue<string>());
    }

    [Fact]
    public async Task Cache_SecondLoadSameInstance()
    {
        await SeedHuberAsync();
        var cards = Cards();
        var id = (await Player("990001")).Id;
        var a = await cards.LoadAsync(id, false, false, default);
        Assert.Same(a, await cards.LoadAsync(id, false, false, default));
        Assert.NotSame(a, await cards.LoadAsync(id, true, false, default));
    }

    [Fact]
    public void PrepController_ReadBehindView_ImportBehindManage()
    {
        var actions = typeof(PrepController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        string? Policy(string name) => actions.Single(a => a.Name == name).GetCustomAttribute<HasPermissionAttribute>()?.Policy;
        foreach (var read in new[] { "Players", "Player", "Profile", "Tree", "Recent", "Pgn" })
            Assert.Equal(PermissionPolicyProvider.Prefix + Permissions.PrepView, Policy(read));
        foreach (var admin in new[] { "ImportGames", "Imports" })
            Assert.Equal(PermissionPolicyProvider.Prefix + Permissions.PrepManage, Policy(admin));
        Assert.All(actions, a => Assert.NotNull(a.GetCustomAttribute<HasPermissionAttribute>()));
    }
}
