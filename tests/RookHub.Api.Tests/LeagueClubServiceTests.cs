using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>LeagueHub-Vereins-Datenbank: Namensabgleich, Anonymisieren, Jahr, Dubletten, Spielerkarten, Löschen.</summary>
public class LeagueClubServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private void Player(int tnr, string team, string name, string? fide) =>
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = tnr, Team = team, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });

    /// <summary>Der Hochladende (Schwaz) und zwei Gegner aus der Liga.</summary>
    private async Task<int> SeedAsync()
    {
        Player(7, "Schwaz", "Oberschmid, Patrik", "900");
        Player(7, "Absam", "Hengl, Philip", "222");
        Player(7, "Absam", "Schnabl, Andreas Dr.", "333");
        var u = new AppUser { Username = "patrik", Email = "p@test", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        _db.UserProfiles.Add(new UserProfile { UserId = u.Id, LastName = "Oberschmid", FirstName = "Patrik" });
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private static string Pgn(string white, string black, string moves = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 1-0",
        string date = "2024.05.12", string extra = "") =>
        $"[Event \"Vereinsmeisterschaft\"]\n[Date \"{date}\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n{extra}\n{moves}\n";

    // ── Namensabgleich ──────────────────────────────────────────────

    private static LeagueRosterIndex Roster(params (string Name, string? Fide)[] people) =>
        new(people.Select((p, i) => new LeagueRosterIndex.Row(i + 1, "T", p.Name, LeagueNames.NameKey(p.Name), p.Fide)));

    [Theory]
    [InlineData("Schnabl, Andreas")]
    [InlineData("Andreas Schnabl")]
    [InlineData("Schnabl Andreas")]
    [InlineData("SCHNABL,Andreas")]
    [InlineData("Schnabl, A.")]
    [InlineData("A. Schnabl")]
    [InlineData("Schnabl, Andreas Dr.")]
    public void Match_FindsTheLeaguePlayerInCommonSpellings(string name)
    {
        var hit = Roster(("Schnabl, Andreas Dr.", "333"), ("Hengl, Philip", "222")).Match(name, null);
        Assert.True(hit.League);
        Assert.Equal("333", hit.Person?.Fide);
    }

    [Theory]
    [InlineData("Müller, Hans")]
    [InlineData("Mueller, Hans")]
    [InlineData("Muller, Hans")]
    public void Match_Umlauts_InBothSpellings(string name) =>
        Assert.Equal("444", Roster(("Müller, Hans", "444")).Match(name, null).Person?.Fide);

    [Fact]
    public void Match_SecondGivenName_ByFirstGivenName() =>
        Assert.Equal("555", Roster(("Kleissl, Helmut Johann", "555")).Match("Helmut Kleissl", null).Person?.Fide);

    [Fact]
    public void Match_TwoNamesakes_IsLeagueButWithoutFide()
    {
        var hit = Roster(("Huber, Franz", "1"), ("Huber, Florian", "2")).Match("Huber, F.", null);
        Assert.True(hit.Ambiguous);
        Assert.Null(hit.Person);
    }

    [Fact]
    public void Match_FideIdWins_AndAForeignIdRejectsTheNamesake()
    {
        var roster = Roster(("Huber, Franz", "1"), ("Gruber, Anna", null));
        Assert.Equal("1", roster.Match("irgendwer", "1").Person?.Fide);
        Assert.False(roster.Match("Huber, Franz", "999").League);            // Namensvetter mit anderer FIDE-ID
        Assert.True(roster.Match("Gruber, Anna", "999").League);             // Ligaspielerin ohne eigene ID: kann sie sein
        Assert.False(roster.Match("Nobody, Else", null).League);
    }

    [Fact]
    public void Suggest_MatchesWordPrefixes() =>
        Assert.Equal(new[] { "Schnabl, Andreas Dr." },
            Roster(("Schnabl, Andreas Dr.", "333"), ("Hengl, Philip", "222")).Suggest("schn and", 5).Select(p => p.Name));

    // ── Import ──────────────────────────────────────────────────────

    [Fact]
    public async Task Import_Anonymized_ReplacesMyNameAndStoresNeitherMeNorTheUploader()
    {
        var me = await SeedAsync();
        var r = await Club().ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip", extra: "[WhiteElo \"1850\"]\n[BlackElo \"2172\"]"), true);

        Assert.Equal((1, 1, 0), (r.Added, r.Anonymized, r.Failed.Count));
        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.Equal(("Schwaz", "Hengl, Philip"), (g.White, g.Black));
        Assert.Equal((null, "222"), (g.WhiteFide, g.BlackFide));
        Assert.Null(g.WhiteElo);                        // die eigene Wertung verriete den Spieler
        Assert.Equal(2172, g.BlackElo);
        Assert.Null(g.UploadedByUserId);
        Assert.Null(g.CreatedAt);
        Assert.Null(g.Event);
        Assert.True(g.Anonymized);
        Assert.Equal(2024, g.Year);
        Assert.Contains("[Date \"2024.??.??\"]", g.Pgn);
        Assert.DoesNotContain("Oberschmid", g.Pgn);
        Assert.DoesNotContain("1850", g.Pgn);
        Assert.DoesNotContain("Vereinsmeisterschaft", g.Pgn);
        Assert.Contains("[BlackFideId \"222\"]", g.Pgn);
    }

    [Fact]
    public async Task Import_NotAnonymized_KeepsNamesAndTheUploader()
    {
        var me = await SeedAsync();
        await Club().ImportPgnAsync(me, Pgn("Hengl Philip", "Oberschmid, Patrik"), false);

        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.Equal(("Hengl, Philip", "Oberschmid, Patrik"), (g.White, g.Black));   // Schreibweise der Meldeliste
        Assert.Equal(("222", "900"), (g.WhiteFide, g.BlackFide));
        Assert.Equal(me, g.UploadedByUserId);
        Assert.Equal(Now, g.CreatedAt);
        Assert.Equal("Vereinsmeisterschaft", g.Event);
    }

    [Fact]
    public async Task Import_Rejections_NameTheReason()
    {
        var me = await SeedAsync();
        var pgn = string.Join("\n",
            Pgn("Nobody, Else", "Someone, Other"),                                  // 1: kein Ligaspieler
            Pgn("Oberschmid, Patrik", "Nobody, Else", "1. d4 d5 2. c4 e6 1-0"),    // 2: nur ich — anonym bleibt keiner
            Pgn("Hengl, Philip", "Schnabl, Andreas", "1. e4 e5 1-0"),              // 3: ich spiele nicht mit
            Pgn("Hengl, Philip", "Oberschmid, Patrik", "1. e4 e5 2. Ke3 1-0"),     // 4: illegal
            Pgn("Hengl, Philip", "Oberschmid, Patrik", "1. e4 1-0",
                extra: "[FEN \"8/8/8/4k3/8/8/4P3/4K3 w - - 0 1\"]\n[SetUp \"1\"]")); // 5: aus einer Stellung
        var r = await Club().ImportPgnAsync(me, pgn, true);

        Assert.Equal(0, r.Added);
        Assert.Equal(new[] { (1, "noLeaguePlayer"), (2, "noLeaguePlayer"), (3, "ownerNotFound"), (4, "illegal"), (5, "fromPosition") },
            r.Failed.Select(f => (f.Index, f.Reason)));
        Assert.Empty(_db.LeagueClubGames);
    }

    [Fact]
    public async Task Import_ProfileFideId_FindsMySide()
    {
        var me = await SeedAsync();
        (await _db.UserProfiles.SingleAsync()).FideId = "900";
        (await _db.UserProfiles.SingleAsync()).LastName = null;
        await _db.SaveChangesAsync();
        await Club().ImportPgnAsync(me, Pgn("Hengl, Philip", "P. Oberschmid"), true);
        Assert.Equal(("Hengl, Philip", "Schwaz"), (_db.LeagueClubGames.Single().White, _db.LeagueClubGames.Single().Black));
    }

    [Fact]
    public async Task Import_SameGameTwice_IsOneGame_ButShortGamesNeedTheSameNames()
    {
        var me = await SeedAsync();
        var club = Club();
        var r1 = await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip") + Pgn("Oberschmid, Patrik", "Hengl, Philip"), true);
        var r2 = await club.ImportPgnAsync(me, Pgn("Hengl, Philip", "Oberschmid, Patrik", date: "2024.10.01"), false);  // gleiche Züge, anderer Name
        Assert.Equal((1, 1), (r1.Added, r1.Duplicates));
        Assert.Equal((0, 1), (r2.Added, r2.Duplicates));

        var shortGame = "1. e4 e5 2. Nf3 Nc6 1-0";
        var r3 = await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip", shortGame), true);
        var r4 = await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Schnabl, Andreas", shortGame), true);
        var r5 = await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Schnabl, Andreas", shortGame, date: "2025.01.01"), true);
        Assert.Equal((1, 1, 1), (r3.Added, r4.Added, r5.Added));    // anderer Gegner, anderes Jahr = andere Partie
        Assert.Equal(4, _db.LeagueClubGames.Count());
    }

    // ── Spielerkarten ───────────────────────────────────────────────

    private const string ExternalSameGame =
        "[Event \"TMM Landesliga\"]\n[Date \"2024.05.12\"]\n[White \"Oberschmid, Patrik\"]\n[Black \"Hengl, Philip\"]\n[Result \"1-0\"]\n\n" +
        "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 1-0\n";

    [Fact]
    public async Task Cards_IncludeClubGames_WithoutCountingTheSameGameTwice()
    {
        var me = await SeedAsync();
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "222", Name = "Hengl, Philip", Pgn = ExternalSameGame, GameCount = 1 });
        _db.LeagueViews.Add(new LeagueView
        {
            Tnr = 7,
            Json = "{\"fixtures\":{\"Absam\":{\"1\":{\"roster\":[{\"fide\":\"222\",\"g\":1},{\"fide\":\"333\",\"g\":0}]}}}}",
        });
        await _db.SaveChangesAsync();
        var club = Club();
        await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip"), true);            // = die chess-results-Partie
        await club.ImportPgnAsync(me, Pgn("Schnabl, Andreas", "Oberschmid, Patrik", "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 1-0"), true);

        var hengl = await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == "222");
        Assert.Equal(1, hengl.GameCount);                                  // dieselbe Partie nur einmal
        Assert.Equal(ExternalSameGame, hengl.Pgn);                         // gespeichert bleiben nur die fremden
        var schnabl = await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == "333");
        Assert.Equal(1, schnabl.GameCount);                                // Karte neu angelegt
        Assert.Equal(1, JsonNode.Parse(schnabl.ProfileJson)!["src"]!["Verein"]!.GetValue<int>());
        Assert.Equal("Schnabl, Andreas Dr.", schnabl.Name);         // Schreibweise der Meldeliste

        var roster = JsonNode.Parse(_db.LeagueViews.AsNoTracking().Single().Json)!["fixtures"]!["Absam"]!["1"]!["roster"]!.AsArray();
        Assert.Equal(1, roster[1]!["g"]!.GetValue<int>());                 // Partienzahl in der Ansicht nachgezogen

        var pgn = await new LeagueProfileStore(_db).PgnAsync("333", default);
        Assert.Contains("Schwaz", pgn!.Value.Pgn);                         // Download = fremde + Vereinspartien
    }

    [Fact]
    public async Task Refresh_MergingChessResults_KeepsTheClubGamesInTheCard()
    {
        var me = await SeedAsync();
        await Club().ImportPgnAsync(me, Pgn("Schnabl, Andreas", "Oberschmid, Patrik", "1. d4 Nf6 2. c4 e6 1-0"), true);
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var refresh = new LeagueRefresh(_db, league, new NoClients(), NullLogger<LeagueRefresh>.Instance, () => Now);

        await refresh.MergeGamesAsync("333", ExternalSameGame.Replace("Oberschmid, Patrik", "Schnabl, Andreas"), default);

        var row = await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == "333");
        Assert.Equal(2, row.GameCount);
        Assert.DoesNotContain("Schwaz", row.Pgn);
        Assert.Equal(Now, row.CrFetchedAt);
    }

    private sealed class NoClients : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new InvalidOperationException();
    }

    // ── Einzelne Partie, Liste, Löschen ─────────────────────────────

    [Fact]
    public async Task AddGame_ExplicitSide_Year_AndIllegalMovesNamed()
    {
        var me = await SeedAsync();
        var club = Club();
        var (bad, reason, message) = await club.AddGameAsync(me, new LeagueClubGameRequest { Moves = new() { "e4", "e5", "Ke3" }, White = "x", Black = "y" });
        Assert.Null(bad);
        Assert.Equal("illegal", reason);
        Assert.Contains("ply 3", message);

        var (game, r, _) = await club.AddGameAsync(me, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "c5", "Nf3" }, White = "Didi", Black = "Hengl, Philip", Year = 2026, Result = "0-1",
            OwnerSide = "white", Anonymize = true, Event = "Simultan",
        });
        Assert.Null(r);
        Assert.Equal(("Schwaz", "Hengl, Philip", 2026, "0-1"), (game!.White, game.Black, game.Year, game.Result));
        Assert.Contains("1. e4 c5 2. Nf3", game.Pgn);
        Assert.Equal("duplicate", (await club.AddGameAsync(me, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "c5", "Nf3" }, White = "Didi", Black = "Hengl, Philip", Year = 2026, OwnerSide = "white",
        })).Reason);
    }

    [Fact]
    public async Task ListAndDelete_OwnNamedGames_AnonymousOnlyByManagers()
    {
        var me = await SeedAsync();
        var club = Club();
        await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip"), false);
        await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Schnabl, Andreas", "1. d4 d5 1-0"), true);

        var list = await club.ListAsync(me, false, null, null, 1, default);
        Assert.Equal(2, list.Total);
        var named = list.Items.Single(i => !i.Anonymized);
        var anon = list.Items.Single(i => i.Anonymized);
        Assert.Equal("1.e4 c5 2.Nf3 d6 3.d4 cxd4", named.Opening);
        Assert.True(named.CanDelete);
        Assert.False(anon.CanDelete);
        Assert.Single((await club.ListAsync(me, false, "333", null, 1, default)).Items);
        Assert.Single((await club.ListAsync(me, false, null, "Hengl", 1, default)).Items);

        Assert.Equal(LeagueClubService.DeleteResult.Forbidden, await club.DeleteAsync(me + 1, false, named.Id));
        Assert.Equal(LeagueClubService.DeleteResult.Forbidden, await club.DeleteAsync(me, false, anon.Id));
        Assert.Equal(LeagueClubService.DeleteResult.Deleted, await club.DeleteAsync(me, false, named.Id));
        Assert.Equal(LeagueClubService.DeleteResult.Deleted, await club.DeleteAsync(me + 1, true, anon.Id));
        Assert.Empty(_db.LeagueClubGames);
        Assert.Equal(0, _db.LeaguePlayerProfiles.Single(p => p.FideId == "333").GameCount);   // Karte ohne die Partie
    }

    [Fact]
    public async Task Export_IsAllGamesAsPgn()
    {
        var me = await SeedAsync();
        await Club().ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip") + Pgn("Oberschmid, Patrik", "Schnabl, Andreas", "1. d4 d5 1-0"), true);
        var pgn = await Club().ExportAsync(null, null, default);
        Assert.Equal(2, RookHub.Api.Services.PgnParser.SplitGames(pgn).Count());
    }
}
