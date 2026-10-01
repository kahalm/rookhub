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
}
