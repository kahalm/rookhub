using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Prep;

namespace RookHub.Api.Tests;

/// <summary>
/// Teilpartien aus von Hand eingegebenen ersten Zügen (0.725.0, „ja mach das so"): sie zählen in Karte, Profil, Baum beider
/// Spieler, in der Quellen-Zählung und in der Spielervorbereitung — nicht unter „letzte Partien"; eine volle Partie zum Brett
/// schlägt sie; Speichern/Löschen rechnet die Karten nach. Spieler erfunden, FIDE-IDs im 9908xx-Bereich.
/// </summary>
public class LeaguePartialGamesTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 10, 12, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private const int Tnr = 4712, Round = 2, User = 2;
    private const string Ackermann = "990801", Brunner = "990802", Clauss = "990803", Dorn = "990804";

    private async Task SeedAsync()
    {
        await TestClubs.SeedAsync(_db);
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = Tnr, Name = "TMM Landesliga", Season = "2026/27", Level = 3, League = "Landesliga" });
        _db.LeagueRounds.Add(new LeagueRound { Tnr = Tnr, Round = Round, Date = new DateOnly(2026, 10, 11) });
        _db.LeagueMatches.Add(new LeagueMatch { Tnr = Tnr, Round = Round, MatchNo = 1, Home = "Testdorf", Away = "Bergheim" });
        _db.LeagueGames.AddRange(
            new LeagueGame { Tnr = Tnr, Round = Round, MatchNo = 1, Board = 1, HomeTeam = "Testdorf", AwayTeam = "Bergheim",
                HomePlayer = "Ackermann, Anna", AwayPlayer = "Brunner, Bert", HomeColor = "w", Result = "1 - 0",
                HomeFide = Ackermann, AwayFide = Brunner, HomeElo = 1901, AwayElo = 1801 },
            // Heimspieler Clauss hat Schwarz; „0 - 1" aus Sicht Heim – Gast = Dorn (Weiß) gewinnt
            new LeagueGame { Tnr = Tnr, Round = Round, MatchNo = 1, Board = 2, HomeTeam = "Testdorf", AwayTeam = "Bergheim",
                HomePlayer = "Clauss, Carl", AwayPlayer = "Dorn, Dora", HomeColor = "s", Result = "0 - 1",
                HomeFide = Clauss, AwayFide = Dorn, HomeElo = 1902, AwayElo = 1802 });
        foreach (var (team, name, fide) in new[] { ("Testdorf", "Ackermann, Anna", Ackermann), ("Bergheim", "Brunner, Bert", Brunner),
                     ("Testdorf", "Clauss, Carl", Clauss), ("Bergheim", "Dorn, Dora", Dorn) })
            _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = Tnr, Team = team, Name = name, NameKey = name.ToLowerInvariant(), FideId = fide });
        _db.LeagueViews.Add(new LeagueView { Tnr = Tnr, GeneratedAt = Now,
            Json = "{\"fixtures\":{\"Testdorf\":{\"2\":{\"roster\":[{\"fide\":\"" + Clauss + "\",\"g\":0},{\"fide\":\"" + Dorn + "\",\"g\":0}]}}}}" });
        await _db.SaveChangesAsync();
    }

    private Task<LeagueGameMoves.SaveResult> SaveAsync(int board, string? moves) =>
        new LeagueGameMoves(_db, () => Now).SaveAsync(TestClubs.Home, User, canManage: true, Tnr, Round, 1, board, moves);

    private LeagueProfileStore Store => new(_db);

    private async Task<JsonObject> CardAsync(string fide)
    {
        _db.ChangeTracker.Clear();
        var row = await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == fide);
        return JsonNode.Parse(row.ProfileJson)!.AsObject();
    }

    private async Task<int> GameCountAsync(string fide) =>
        (await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == fide)).GameCount;

    [Fact]
    public async Task Partial_InGamesOfBothPlayers_WithColorsResultDateEvent()
    {
        await SeedAsync();
        Assert.Null((await SaveAsync(2, "1.d4 d5 2.c4")).Reason);

        foreach (var fide in new[] { Clauss, Dorn })
        {
            var (_, games) = await Store.GamesAsync(fide, default);
            var g = Assert.Single(games);
            Assert.True(LeaguePartialGames.Is(g));
            Assert.Equal(("Dorn, Dora", "Clauss, Carl", "1-0", "2026.10.11", "TMM Landesliga 2026/27, Runde 2", Dorn, Clauss, "Ligarunde"),
                (g.Headers["White"], g.Headers["Black"], g.Headers["Result"], g.Headers["Date"], g.Headers["Event"],
                 g.Headers["WhiteFideId"], g.Headers["BlackFideId"], g.Headers["LeagueSource"]));
            Assert.Equal(new[] { "d4", "d5", "c4" }, PgnParser.ExtractMainlineSans(PgnParser.SplitGames(g.Raw).First().MoveText));
        }
        // Board 1 ohne Züge → keine Teilpartie
        Assert.Empty((await Store.GamesAsync(Ackermann, default)).Games);
    }

    [Fact]
    public async Task Partial_CountsOnCard_ProfileAndTree_ButNotInRecent()
    {
        await SeedAsync();
        // eine fremde Partie auf Dorns Karte, damit „recent" überhaupt etwas zeigt
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = Dorn, Name = "Dorn, Dora", UpdatedAt = Now,
            Pgn = "[Event \"Open\"]\n[Date \"2025.05.01\"]\n[White \"Dorn, Dora\"]\n[Black \"Egger, Eva\"]\n[Result \"1/2-1/2\"]\n"
                + "[LeagueSource \"chess-results\"]\n\n1. e4 e5 2. Nf3 Nc6 1/2-1/2\n" });
        await _db.SaveChangesAsync();
        await SaveAsync(2, "1.d4 d5 2.c4 e6");

        var dorn = await CardAsync(Dorn);
        Assert.Equal(2, dorn["n"]!.GetValue<int>());
        Assert.Equal(2, await GameCountAsync(Dorn));
        Assert.Equal(1, dorn["src"]!["Ligarunde"]!.GetValue<int>());
        var recent = dorn["recent"]!.AsArray();
        Assert.Equal("Egger, Eva", Assert.Single(recent)!["vs"]!.GetValue<string>());
        Assert.Equal(2, dorn["white"]!["n"]!.GetValue<int>());

        var clauss = await CardAsync(Clauss);   // Karte neu angelegt — nur die Teilpartie
        Assert.Equal(1, clauss["n"]!.GetValue<int>());
        Assert.Empty(clauss["recent"]!.AsArray());
        Assert.Equal(1, clauss["black_d4"]!["n"]!.GetValue<int>());

        var tree = (await Store.TreeAsync(Clauss, "s", "d4", default))!;
        var d5 = Assert.Single(tree["moves"]!.AsArray())!;
        Assert.Equal(("d5", 1, 0), (d5["san"]!.GetValue<string>(), d5["n"]!.GetValue<int>(), d5["score"]!.GetValue<int>()));
        var white = (await Store.TreeAsync(Dorn, "w", "", default, LeagueProfileStore.TreeFilter.Parse("both", null, null, false)))!;
        Assert.Contains(white["moves"]!.AsArray(), m => m!["san"]!.GetValue<string>() == "d4");

        var profile = (await Store.ProfileAsync(Clauss, default, LeagueProfileStore.TreeFilter.Parse("board", null, 1, false)))!;
        Assert.Equal(1, profile["n"]!.GetValue<int>());

        // „letzte Partien" mit PGN und der Download bleiben ohne Teilpartie
        var r = (await Store.RecentAsync(Dorn, default))!;
        Assert.Single(r["games"]!.AsArray());
        Assert.DoesNotContain("Ligarunde", (await Store.PgnAsync(Dorn, default))!.Value.Pgn);
    }

    [Fact]
    public async Task Delete_RecomputesCards_AndPatchesViewCount()
    {
        await SeedAsync();
        await SaveAsync(2, "e4 c5");
        Assert.Equal(1, await GameCountAsync(Clauss));
        Assert.Contains("\"g\":1", (await _db.LeagueViews.AsNoTracking().SingleAsync()).Json);

        Assert.Null((await SaveAsync(2, null)).Reason);
        _db.ChangeTracker.Clear();
        Assert.Equal(0, await GameCountAsync(Clauss));
        Assert.Equal(0, await GameCountAsync(Dorn));
        Assert.DoesNotContain("\"g\":1", (await _db.LeagueViews.AsNoTracking().SingleAsync()).Json);
        Assert.Empty((await Store.GamesAsync(Dorn, default)).Games);
    }

    private const string FullPgn = "[White \"Dorn, Dora\"]\n[Black \"Clauss, Carl\"]\n[Result \"1-0\"]\n[Date \"2026.??.??\"]\n\n"
        + "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 5. e3 O-O 6. Nf3 Nbd7 1-0";

    [Fact]
    public async Task LinkedClubGame_OfAnyClub_ReplacesThePartial()
    {
        await SeedAsync();
        await SaveAsync(2, "d4 d5");
        var lg = await _db.LeagueGames.SingleAsync(g => g.Board == 2);
        // anonymisiert im ANDEREN Verein, ohne FIDE-IDs an der Partie — nur die feste Zuordnung verbindet sie mit dem Brett
        var c = new LeagueClubGame { ClubId = TestClubs.OtherId, Year = 2026, White = "Weiler", Black = "Weiler", Result = "1-0",
            Plies = 12, Pgn = FullPgn, MovesHash = "x" };
        LeagueGameLinks.Set(c, lg);
        _db.LeagueClubGames.Add(c);
        await _db.SaveChangesAsync();
        Assert.Empty((await Store.GamesAsync(Clauss, default)).Games);

        // archiviert zählt nicht — die Teilpartie ist wieder da
        c.ArchivedAt = Now;
        await _db.SaveChangesAsync();
        Assert.Single((await Store.GamesAsync(Clauss, default)).Games);
    }

    [Fact]
    public async Task ClubGame_SamePlayersSameYear_ReplacesThePartial_OnBothCards()
    {
        await SeedAsync();
        await SaveAsync(2, "d4 d5");
        // nur Dorns FIDE-ID an der Partie — Clauss' Karte kennt sie nicht, die Teilpartie bleibt trotzdem draußen
        _db.LeagueClubGames.Add(new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = "Dorn, Dora", Black = "Clauss, Carl",
            WhiteFide = Dorn, Result = "1-0", Plies = 12, Pgn = FullPgn, MovesHash = "y" });
        await _db.SaveChangesAsync();
        var dorn = (await Store.GamesAsync(Dorn, default)).Games;
        Assert.Equal(LeagueProfileStore.ClubSource, Assert.Single(dorn).Source);
        Assert.Empty((await Store.GamesAsync(Clauss, default)).Games);
    }

    [Fact]
    public async Task ProfileGame_WithinThreeDays_ReplacesThePartial_FurtherAwayNot()
    {
        await SeedAsync();
        await SaveAsync(2, "d4 d5");
        static string Full(string date) => $"[Event \"TMM\"]\n[Date \"{date}\"]\n[White \"Dorn, Dora\"]\n[Black \"Clauss, Carl\"]\n"
            + "[Result \"1-0\"]\n\n1. d4 d5 2. c4 e6 3. Nc3 Nf6 1-0\n";
        var row = await _db.LeaguePlayerProfiles.SingleAsync(p => p.FideId == Dorn);   // die Karte legte das Speichern an
        row.Pgn = Full("2026.10.20");
        await _db.SaveChangesAsync();
        Assert.Equal(2, (await Store.GamesAsync(Dorn, default)).Games.Count);   // neun Tage daneben: eine andere Partie

        row.Pgn = Full("2026.10.13");
        await _db.SaveChangesAsync();
        var g = Assert.Single((await Store.GamesAsync(Dorn, default)).Games);
        Assert.False(LeaguePartialGames.Is(g));
    }

    [Fact]
    public async Task Sources_CountPartialsAsOwnRow_OncePerBoard_WithoutCoveredOnes()
    {
        await SeedAsync();
        await SaveAsync(1, "e4 e5");
        await SaveAsync(2, "d4 d5");
        var res = await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default, new[] { Clauss }, leagueTnr: Tnr);
        var row = Assert.Single(res["board"]!.AsArray(), r => r!["key"]!.GetValue<string>() == "Ligarunde")!;
        Assert.Equal(("Ligarunde (erste Züge)", 2), (row["label"]!.GetValue<string>(), row["games"]!.GetValue<int>()));
        Assert.Equal(1, res["opponent"]!["board"]!["Ligarunde"]!.GetValue<int>());
        Assert.Equal(2, res["league"]!["board"]!["Ligarunde"]!.GetValue<int>());   // vier Spieler, zwei Bretter: je Brett einmal

        var lg = await _db.LeagueGames.SingleAsync(g => g.Board == 2);
        var c = new LeagueClubGame { ClubId = TestClubs.HomeId, Year = 2026, White = "Dorn, Dora", Black = "Clauss, Carl", Result = "1-0",
            Plies = 12, Pgn = FullPgn, MovesHash = "z" };
        LeagueGameLinks.Set(c, lg);
        _db.LeagueClubGames.Add(c);
        await _db.SaveChangesAsync();
        res = await new LeagueGameSources(_db).GetAsync(TestClubs.HomeId, default, new[] { Clauss });
        Assert.Equal(1, res["board"]!.AsArray().Single(r => r!["key"]!.GetValue<string>() == "Ligarunde")!["games"]!.GetValue<int>());
        Assert.Null(res["opponent"]!["board"]!["Ligarunde"]);
    }

    [Fact]
    public async Task Prep_TrainingLines_CountThePartial()
    {
        await SeedAsync();
        await SaveAsync(2, "d4 d5 c4");
        var notifications = new NotificationService(_db);
        var reps = new RepertoireService(_db, new RepertoireAnalyzeService(_db, new MemoryCache(new MemoryCacheOptions())),
            new FriendService(_db, notifications), notifications);
        var svc = new TrainingLinesService(_db, reps, new ConfigurationBuilder().Build());
        var (name, games) = await svc.LeagueGamesAsync(Clauss, LeagueProfileStore.TreeFilter.Default, default);
        Assert.Equal("Clauss, Carl", name);
        var g = Assert.Single(games);
        Assert.Equal(new[] { "d4", "d5", "c4" }, g.Sans);
        Assert.False(g.OpponentWhite);
        Assert.Equal(2026, g.Year);
    }

    [Fact]
    public async Task PrepCard_TakesThePartial_UnlessTheStockHasTheFullGame_NotInRecent()
    {
        await SeedAsync();
        await SaveAsync(2, "d4 d5 c4");
        using var cache = new MemoryCache(new MemoryCacheOptions());
        PrepCardService Cards() => new(_db, cache, new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance));
        static string G(string white, string black, string date, string moves, string? wf, string? bf) =>
            $"[Event \"X\"]\n[Site \"?\"]\n[Date \"{date}\"]\n[Round \"1\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n"
            + (wf is null ? "" : $"[WhiteFideId \"{wf}\"]\n") + (bf is null ? "" : $"[BlackFideId \"{bf}\"]\n") + $"\n{moves} 1-0\n\n";
        await new PrepImportService(_db).ImportChunkAsync(PrepSources.Mega, 0, 0,
            G("Clauss, Carl", "Egger, Eva", "2025.03.01", "1. e4 e5 2. Nf3 Nc6", Clauss, null), default);
        var id = (await _db.PrepPlayers.SingleAsync(p => p.FideId == Clauss)).Id;

        var l = (await Cards().LoadAsync(id, false, false, default))!;
        Assert.Equal(2, l.Games.Count);
        Assert.Contains(l.Games, g => g.Label == LeaguePartialGames.Source && g.Color == "s");
        var recent = (await Cards().RecentAsync(l, null, default))["games"]!.AsArray();
        Assert.Single(recent);
        var tree = await Cards().TreeAsync(l, "s", "d4", LeagueProfileStore.TreeFilter.Default, default);
        Assert.Equal("d5", tree["moves"]!.AsArray().Single()!["san"]!.GetValue<string>());

        // der Bestand hat die volle Partie (zwei Tage neben dem Rundentermin) → die Teilpartie fällt weg
        await new PrepImportService(_db).ImportChunkAsync(PrepSources.Mega, 1, 0,
            G("Dorn, Dora", "Clauss, Carl", "2026.10.13", "1. d4 d5 2. c4 e6 3. Nc3 Nf6", Dorn, Clauss), default);
        cache.Clear();
        l = (await Cards().LoadAsync(id, false, false, default))!;
        Assert.Equal(2, l.Games.Count);
        Assert.DoesNotContain(l.Games, g => g.Label == LeaguePartialGames.Source);
    }

    [Theory]
    [InlineData("1 - 0", "1-0")]
    [InlineData("0 - 1", "0-1")]
    [InlineData("½ - ½", "1/2-1/2")]
    [InlineData("+ - -", "*")]
    [InlineData("", "*")]
    public void PgnResult_FromBoardResult(string board, string pgn) => Assert.Equal(pgn, LeaguePartialGames.PgnResult(board));

    [Theory]
    [InlineData("TMM Landesliga 2026/2027", "2026/27", "TMM Landesliga 2026/2027")]
    [InlineData("TMM Landesliga", "2026/27", "TMM Landesliga 2026/27")]
    [InlineData("Landesliga Süd", "", "Landesliga Süd")]
    public void EventName_AddsSeasonOnlyWhenMissing(string name, string season, string expected) =>
        Assert.Equal(expected, LeaguePartialGames.EventName(name, season));
}
