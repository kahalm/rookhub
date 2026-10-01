using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>Partien im Bestand je Quelle (0.626.0) — Startseite von LeagueHub.</summary>
public class LeagueGameSourcesTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private static string Game(string white, string black, string date, string round, params string[] extra) =>
        $"[Event \"Liga\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Date \"{date}\"]\n[Round \"{round}\"]\n"
        + string.Concat(extra.Select(e => e + "\n")) + "\n1. e4 e5 2. Nf3 1-0\n\n";

    // Muster gegen Huber steht in BEIDEN Profilen (je Spieler gespeichert) — einmal mit Komma, einmal ohne.
    private static readonly string MusterPgn =
        Game("Muster, Max", "Huber, Franz", "2024.03.01", "1", "[WhiteFideId \"222\"]")                      // Lumbra
        + Game("Muster, Max", "Gast, Gerd", "2023.01.01", "2", "[LeagueSource \"Mega\"]", "[WhiteFideId \"222\"]")
        + Game("Muster, Max", "Mair, Moritz", "2022.05.05", "3")                                               // chess-results
        + Game("Muster, Max", "Mair, Moritz", "2022.05.05", "4")                                               // andere Runde
        + Game("Muster, Max", "Senn, Sepp", "2025.09.01", "1", "[LeagueSource \"Lichess-Übertragung\"]");
    private static readonly string HuberPgn =
        Game("Muster Max", "Huber Franz", "2024.03.01", "1")                                                   // dieselbe Partie
        + Game("Huber, Franz", "Neu, Nina", "2024.04.01", "1", "[LeagueSource \"Mega\"]");

    [Fact]
    public void CountBoard_EachGameOnce_SourceByTheCardsRule()
    {
        var seen = new HashSet<string>();
        var counts = new Dictionary<string, int>();
        LeagueGameSources.CountBoard(MusterPgn, seen, counts);
        LeagueGameSources.CountBoard(HuberPgn, seen, counts);
        Assert.Equal(new Dictionary<string, int> { ["Lumbra"] = 1, ["Mega"] = 2, ["chess-results"] = 2, ["Lichess-Übertragung"] = 1 }, counts);
        LeagueGameSources.CountBoard("", seen, counts);                                                        // leeres Profil
        Assert.Equal(6, counts.Values.Sum());
    }

    [Fact]
    public void Labels()
    {
        Assert.Equal("ChessBase-Megabase", LeagueGameSources.Label("Mega"));
        Assert.Equal("Lichess-Übertragungen", LeagueGameSources.Label("Lichess-Übertragung"));
        Assert.Equal("Vereins-Datenbank", LeagueGameSources.Label("Verein"));
        Assert.Equal("chess.com", LeagueGameSources.Label("chess.com"));
        Assert.Equal("Eigene Sammlung", LeagueGameSources.Label("Eigene Sammlung"));
    }

    [Fact]
    public async Task Get_BoardClubAndOnline_SortedWithTotals_AndCached()
    {
        _db.LeaguePlayerProfiles.AddRange(
            new LeaguePlayerProfile { FideId = "222", Name = "Muster, Max", Pgn = MusterPgn },
            new LeaguePlayerProfile { FideId = "333", Name = "Huber, Franz", Pgn = HuberPgn });
        _db.LeagueClubGames.Add(new LeagueClubGame { White = "Schwaz", Black = "Gast", Pgn = "1. d4 *", MovesHash = "h" });
        var li1 = new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "a", Url = "u", Confidence = "sicher" };
        var li2 = new LeagueOnlineAccount { FideId = "333", Site = "lichess", UserName = "b", Url = "u", Confidence = "sicher" };
        var cc = new LeagueOnlineAccount { FideId = "222", Site = "chess.com", UserName = "c", Url = "u", Confidence = "sicher" };
        _db.LeagueOnlineAccounts.AddRange(li1, li2, cc);
        await _db.SaveChangesAsync();
        LeagueOnlineGame G(LeagueOnlineAccount a, string id) => new()
        {
            AccountId = a.Id, FideId = a.FideId, ExternalId = id, PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4",
        };
        // „x1" spielten die beiden Lichess-Konten GEGENEINANDER — eine Partie, zwei Zeilen.
        _db.LeagueOnlineGames.AddRange(G(li1, "x1"), G(li2, "x1"), G(li1, "x2"), G(li2, "x3"), G(cc, "c1"));
        await _db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var r = await new LeagueGameSources(_db, cache).GetAsync(default);
        Assert.Equal(7, r["boardTotal"]!.GetValue<int>());
        var board = r["board"]!.AsArray().Select(x => ((string)x!["label"]!, x["games"]!.GetValue<int>())).ToList();
        Assert.Equal(new[] { ("ChessBase-Megabase", 2), ("chess-results", 2), ("Lichess-Übertragungen", 1), ("Lumbra", 1), ("Vereins-Datenbank", 1) }, board);
        Assert.Equal(4, r["onlineTotal"]!.GetValue<int>());
        var online = r["online"]!.AsArray().Select(x => ((string)x!["key"]!, (string)x["label"]!, x["games"]!.GetValue<int>())).ToList();
        Assert.Equal(new[] { ("lichess", "Lichess", 3), ("chess.com", "chess.com", 1) }, online);

        // 30 min im Speicher: eine neue Partie zählt erst danach.
        _db.LeagueOnlineGames.Add(G(cc, "c2"));
        await _db.SaveChangesAsync();
        Assert.Equal(4, (await new LeagueGameSources(_db, cache).GetAsync(default))["onlineTotal"]!.GetValue<int>());
        Assert.Equal(5, (await new LeagueGameSources(_db).GetAsync(default))["onlineTotal"]!.GetValue<int>());
    }

    [Fact]
    public async Task Opponent_CountsOnlyThesePlayers_SharedGameOnce_OnlineWithAccounts()
    {
        _db.LeaguePlayerProfiles.AddRange(
            new LeaguePlayerProfile { FideId = "222", Name = "Muster, Max", Pgn = MusterPgn },
            new LeaguePlayerProfile { FideId = "333", Name = "Huber, Franz", Pgn = HuberPgn },
            new LeaguePlayerProfile { FideId = "999", Name = "Fremd, Fritz", Pgn = Game("Fremd, Fritz", "X, Y", "2020.01.01", "1") });
        _db.LeagueClubGames.AddRange(
            new LeagueClubGame { White = "Schwaz", Black = "Huber, Franz", BlackFide = "333", Pgn = "1. d4 *", MovesHash = "a" },
            new LeagueClubGame { White = "Schwaz", Black = "Fremd, Fritz", BlackFide = "999", Pgn = "1. e4 *", MovesHash = "b" });
        var sure = new LeagueOnlineAccount { FideId = "222", Site = "lichess", UserName = "a", Url = "u", Confidence = "sicher" };
        var unsure = new LeagueOnlineAccount { FideId = "333", Site = "chess.com", UserName = "b", Url = "u", Confidence = "wahrscheinlich" };
        _db.LeagueOnlineAccounts.AddRange(sure, unsure);
        await _db.SaveChangesAsync();
        _db.LeagueOnlineGames.AddRange(
            new LeagueOnlineGame { AccountId = sure.Id, FideId = "222", ExternalId = "l1", PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4" },
            new LeagueOnlineGame { AccountId = sure.Id, FideId = "222", ExternalId = "l2", PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4" },
            new LeagueOnlineGame { AccountId = unsure.Id, FideId = "333", ExternalId = "c1", PlayedAt = DateTime.UtcNow, Speed = "blitz", Result = "1-0", Line = "e4", Moves = "e4" });
        await _db.SaveChangesAsync();

        // Gegner = Muster + Huber (+ leere/doppelte Einträge der Meldeliste): ihre gemeinsame Partie einmal, Fremd nicht.
        var r = await new LeagueGameSources(_db).GetAsync(default, new[] { "222", "333", null, "", "222" });
        var o = r["opponent"]!.AsObject();
        Assert.Equal(2, o["players"]!.GetValue<int>());
        Assert.Equal(7, o["boardTotal"]!.GetValue<int>());                          // 6 aus den PGN + 1 Vereinspartie von Huber
        Assert.Equal(1, o["board"]!["Verein"]!.GetValue<int>());
        Assert.Equal(2, o["board"]!["Mega"]!.GetValue<int>());
        Assert.Equal((2, 1), (o["online"]!["lichess"]!["games"]!.GetValue<int>(), o["online"]!["lichess"]!["accounts"]!.GetValue<int>()));
        Assert.Equal((1, 1), (o["online"]!["chess.com"]!["games"]!.GetValue<int>(), o["online"]!["chess.com"]!["accounts"]!.GetValue<int>()));
        Assert.Equal((3, 2), (o["onlineTotal"]!.GetValue<int>(), o["onlineAccounts"]!.GetValue<int>()));
        Assert.Equal(9, r["boardTotal"]!.GetValue<int>());                          // Gesamt bleibt Gesamt

        // Teilen-Link: online nur gesicherte Konten — das unsichere chess.com-Konto zählt nicht, auch nicht als Konto.
        var shared = (await new LeagueGameSources(_db).GetAsync(default, new[] { "222", "333" }, onlySure: true))["opponent"]!;
        Assert.Null(shared["online"]!["chess.com"]);
        Assert.Equal((2, 1), (shared["onlineTotal"]!.GetValue<int>(), shared["onlineAccounts"]!.GetValue<int>()));

        // Ohne Gegner kein Block; ein Gegner ohne Daten zählt 0 und hat kein Konto.
        Assert.Null((await new LeagueGameSources(_db).GetAsync(default))["opponent"]);
        var none = (await new LeagueGameSources(_db).GetAsync(default, new[] { "4711" }))["opponent"]!;
        Assert.Equal((1, 0, 0), (none["players"]!.GetValue<int>(), none["boardTotal"]!.GetValue<int>(), none["onlineAccounts"]!.GetValue<int>()));
        // Höchstens 40 Spieler.
        var many = (await new LeagueGameSources(_db).GetAsync(default, Enumerable.Range(1, 100).Select(i => i.ToString())))["opponent"]!;
        Assert.Equal(LeagueGameSources.MaxOpponentPlayers, many["players"]!.GetValue<int>());
    }

    [Fact]
    public async Task League_CountsAllRostersOfThatLeague_Cached_UnknownLeagueIsEmpty()
    {
        _db.LeaguePlayers.AddRange(
            new LeaguePlayer { Tnr = 1, Team = "Kufstein", Name = "Muster, Max", NameKey = "muster, max", FideId = "222" },
            new LeaguePlayer { Tnr = 1, Team = "Kufstein", Name = "Muster, Max", NameKey = "muster, max", FideId = "222" },  // zweimal gemeldet
            new LeaguePlayer { Tnr = 1, Team = "Schwaz", Name = "Huber, Franz", NameKey = "huber, franz", FideId = "333" },
            new LeaguePlayer { Tnr = 1, Team = "Schwaz", Name = "Ohne, Fide", NameKey = "ohne, fide", FideId = null },
            new LeaguePlayer { Tnr = 2, Team = "Absam", Name = "Fremd, Fritz", NameKey = "fremd, fritz", FideId = "999" });
        _db.LeaguePlayerProfiles.AddRange(
            new LeaguePlayerProfile { FideId = "222", Name = "Muster, Max", Pgn = MusterPgn },
            new LeaguePlayerProfile { FideId = "333", Name = "Huber, Franz", Pgn = HuberPgn },
            new LeaguePlayerProfile { FideId = "999", Name = "Fremd, Fritz", Pgn = Game("Fremd, Fritz", "X, Y", "2020.01.01", "1") });
        await _db.SaveChangesAsync();

        var cache = new MemoryCache(new MemoryCacheOptions());
        var r = await new LeagueGameSources(_db, cache).GetAsync(default, new[] { "333" }, leagueTnr: 1);
        var l = r["league"]!;
        Assert.Equal((2, 6), (l["players"]!.GetValue<int>(), l["boardTotal"]!.GetValue<int>()));   // Fremd (Liga 2) zählt nicht
        Assert.Equal((1, 2), (r["opponent"]!["players"]!.GetValue<int>(), r["opponent"]!["boardTotal"]!.GetValue<int>()));
        Assert.Equal(7, r["boardTotal"]!.GetValue<int>());

        // 30 min gemerkt — ein neuer Spieler der Liga zählt erst danach; die Begegnung ist immer frisch.
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Schwaz", Name = "Fremd, Fritz", NameKey = "fremd, fritz", FideId = "999" });
        await _db.SaveChangesAsync();
        Assert.Equal(2, (await new LeagueGameSources(_db, cache).GetAsync(default, leagueTnr: 1))["league"]!["players"]!.GetValue<int>());
        Assert.Equal(3, (await new LeagueGameSources(_db).GetAsync(default, leagueTnr: 1))["league"]!["players"]!.GetValue<int>());
        Assert.Equal(0, (await new LeagueGameSources(_db).GetAsync(default, leagueTnr: 4711))["league"]!["players"]!.GetValue<int>());
        Assert.Null((await new LeagueGameSources(_db).GetAsync(default))["league"]);
    }
}
