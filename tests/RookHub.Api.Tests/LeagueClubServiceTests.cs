using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>LeagueHub-Vereins-Datenbank: Namensabgleich, Schwaz ersetzen, Übersicht + Entscheidungen, Jahr, Dubletten,
/// Spielerkarten, Löschen, Megabase-Import, Eröffnungsbaum.</summary>
public class LeagueClubServiceTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private void Player(int tnr, string team, string name, string? fide) =>
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = tnr, Team = team, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });

    /// <summary>Der Hochladende und ein zweiter Spieler von Schwaz, zwei Gegner aus der Liga.</summary>
    private async Task<int> SeedAsync()
    {
        Player(7, "Schwaz", "Oberschmid, Patrik", "900");
        Player(7, "Schwaz", "Binder, Moriz", "111");
        Player(7, "Absam", "Hengl, Philip", "222");
        Player(7, "Absam", "Schnabl, Andreas Dr.", "333");
        var u = new AppUser { Username = "patrik", Email = "p@test", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        _db.UserProfiles.Add(new UserProfile { UserId = u.Id, LastName = "Oberschmid", FirstName = "Patrik" });
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private const string LongGame = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 1-0";

    private static string Pgn(string white, string black, string moves = LongGame, string date = "2024.05.12", string extra = "") =>
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
    public void Match_TwoNamesakes_IsLeagueButWithoutFide_AndNamesBothCandidates()
    {
        var hit = Roster(("Huber, Franz", "1"), ("Huber, Florian", "2")).Match("Huber, F.", null);
        Assert.True(hit.Ambiguous);
        Assert.Null(hit.Person);
        Assert.Equal(new[] { "1", "2" }, hit.Candidates.Select(c => c.Fide).OrderBy(f => f));
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
    public void OwnClub_FollowsTheLatestSeason()
    {
        var roster = new LeagueRosterIndex(new[]
        {
            new LeagueRosterIndex.Row(1, "Schwaz", "Weg, Gegangen", "weg, gegangen", "10", "2025/26"),
            new LeagueRosterIndex.Row(2, "Absam", "Weg, Gegangen", "weg, gegangen", "10", "2026/27"),
            new LeagueRosterIndex.Row(3, "Absam", "Neu, Dazu", "neu, dazu", "20", "2025/26"),
            new LeagueRosterIndex.Row(4, "Schwaz", "Neu, Dazu", "neu, dazu", "20", "2026/27"),
        });
        Assert.False(roster.Match("Weg, Gegangen", null).OwnClub);           // jetzt ein Gegner
        Assert.True(roster.Match("Neu, Dazu", null).OwnClub);                // jetzt einer von uns
    }

    [Fact]
    public void RowWithoutFide_BelongsToTheSameNameWithFide_UnlessTwoCarryIt()
    {
        var roster = new LeagueRosterIndex(new[]
        {
            new LeagueRosterIndex.Row(1, "Absam", "Hengl, Philip", "hengl, philip", "222", "2026/27"),
            new LeagueRosterIndex.Row(2, "Absam", "Hengl Philip", "hengl philip", null, "2022/23"),       // ohne Komma, ohne ID
            new LeagueRosterIndex.Row(3, "Hall", "Huber, Franz", "huber, franz", "1", "2026/27"),
            new LeagueRosterIndex.Row(4, "Rum", "Huber, Franz", "huber, franz", "2", "2026/27"),          // Namensvettern
            new LeagueRosterIndex.Row(5, "Rum", "Huber Franz", "huber franz", null, "2022/23"),
        });
        var hit = roster.Match("Hengl Philip", null);
        Assert.False(hit.Ambiguous);
        Assert.Equal(("222", "Hengl, Philip"), (hit.Person!.Fide, hit.Person.Name));
        Assert.Single(roster.People, p => p.Name.StartsWith("Hengl"));
        Assert.True(roster.Match("Huber, Franz", null).Ambiguous);                                     // wer, bleibt offen
        Assert.Equal(3, roster.People.Count(p => p.Name.StartsWith("Huber")));
    }

    [Fact]
    public void Suggest_MatchesWordPrefixes() =>
        Assert.Equal(new[] { "Schnabl, Andreas Dr." },
            Roster(("Schnabl, Andreas Dr.", "333"), ("Hengl, Philip", "222")).Suggest("schn and", 5).Select(p => p.Name));

    // ── Übersicht ───────────────────────────────────────────────────

    [Fact]
    public async Task Preview_WhoAgainstWhom_DefaultsAndWhatStopsAGame()
    {
        var me = await SeedAsync();
        var pgn = string.Join("\n",
            Pgn("Oberschmid, Patrik", "Hengl, Philip"),                          // 1: ich → ersetzt, Gegner Ligaspieler
            Pgn("Hengl, Philip", "Binder, Moriz", "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 5. e3 O-O 6. Nf3 h6 7. Bh4 b6 8. cxd5 Nxd5 9. Bxe7 Qxe7 10. Nxd5 exd5 1-0"),
            Pgn("Nobody, Else", "Someone, Other", "1. c4 e5 1-0"),               // 3: kein Ligaspieler
            Pgn("Hengl, Philip", "Schnabl, Andreas", "1. e4 e5 2. Ke3 1-0"),     // 4: illegal
            Pgn("Oberschmid, Patrik", "Hengl, Philip"));                         // 5: wie 1 → doppelt
        var p = await Club().PreviewAsync(me, pgn);

        Assert.Equal(5, p.Games.Count);
        Assert.Empty(_db.LeagueClubGames);                                        // die Übersicht speichert nichts
        var g1 = p.Games[0];
        Assert.True(g1.White.Owner && g1.White.Replace && g1.White.Match.Club);
        Assert.Equal(("Hengl, Philip", "222", false), (g1.Black.Match.Name, g1.Black.Match.Fide, g1.Black.Replace));
        Assert.Equal(("1.e4 c5 2.Nf3 d6 3.d4 cxd4", 2024, 22), (g1.Opening, g1.Year, g1.Plies));
        var g2 = p.Games[1];
        Assert.False(g2.Black.Owner);
        Assert.True(g2.Black.Replace);                                            // Schwaz-Spieler, auch wenn nicht ich
        Assert.False(p.Games[2].White.Match.League);
        Assert.Null(p.Games[2].Error);                                            // korrigierbar, kein harter Fehler
        Assert.Equal("illegal", p.Games[3].Error);
        Assert.True(p.Games[4].Duplicate);
    }

    // ── Import ──────────────────────────────────────────────────────

    [Fact]
    public async Task Import_Defaults_ReplacesSchwazPlayers_AndStoresNeitherThemNorTheUploader()
    {
        var me = await SeedAsync();
        var r = await Club().ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip", extra: "[WhiteElo \"1850\"]\n[BlackElo \"2172\"]"), null);

        Assert.Equal((1, 1, 0), (r.Added, r.Anonymized, r.Failed.Count));
        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.Equal(("Schwaz", "Hengl, Philip"), (g.White, g.Black));
        Assert.Equal((null, "222"), (g.WhiteFide, g.BlackFide));
        Assert.Null(g.WhiteElo);                                                   // die Wertung verriete den Spieler
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
    public async Task Import_WithoutSchwazPlayers_KeepsNamesAndTheUploader()
    {
        var me = await SeedAsync();
        await Club().ImportPgnAsync(me, Pgn("Hengl Philip", "Schnabl, Andreas"), null);

        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.Equal(("Hengl, Philip", "Schnabl, Andreas Dr."), (g.White, g.Black));   // Schreibweise der Meldeliste
        Assert.Equal(("222", "333"), (g.WhiteFide, g.BlackFide));
        Assert.Equal(me, g.UploadedByUserId);
        Assert.Equal(Now, g.CreatedAt);
        Assert.Equal("Vereinsmeisterschaft", g.Event);
    }

    [Fact]
    public async Task Import_Decisions_CorrectAPlayer_OverrideReplace_AndTheRulesStillHold()
    {
        var me = await SeedAsync();
        var pgn = string.Join("\n",
            Pgn("Nobody, Else", "Oberschmid, Patrik"),                           // 1: „Nobody" ist in Wahrheit Schnabl
            Pgn("Hengl, Philip", "Binder, Moriz", "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 5. e3 O-O 6. Nf3 h6 7. Bh4 b6 8. cxd5 Nxd5 9. Bxe7 Qxe7 10. Nxd5 exd5 1-0"),
            Pgn("Binder, Moriz", "Oberschmid, Patrik", "1. c4 e5 2. Nc3 Nf6 3. g3 d5 4. cxd5 Nxd5 5. Bg2 Nb6 6. Nf3 Nc6 7. O-O Be7 8. d3 O-O 9. a3 Be6 10. b4 f6 1-0"),
            Pgn("Nobody, Else", "Someone, Other", "1. b3 e5 1-0"));
        var r = await Club().ImportPgnAsync(me, pgn, new List<LeagueClubImportGameDecision>
        {
            new() { Index = 1, White = new() { Fide = "333" }, Black = new() { Replace = true } },
            new() { Index = 2, White = new(), Black = new() { Replace = false } },                    // Binder bleibt stehen
            new() { Index = 3, White = new() { Replace = true }, Black = new() { Replace = true } },  // nur Schwaz übrig
            new() { Index = 4, White = new() { Name = "Hengl, Philip" }, Black = new() },             // getippt → erkannt
            new() { Index = 99, White = new(), Black = new() },
        });

        Assert.Equal(3, r.Added);
        Assert.Equal(new[] { (3, "onlyOwnClub"), (99, "notFound") }, r.Failed.Select(f => (f.Index, f.Reason)));
        var games = await _db.LeagueClubGames.OrderBy(g => g.Id).ToListAsync();
        Assert.Equal(("Schnabl, Andreas Dr.", "Schwaz", "333"), (games[0].White, games[0].Black, games[0].WhiteFide));
        Assert.Equal(("Hengl, Philip", "Binder, Moriz", false), (games[1].White, games[1].Black, games[1].Anonymized));
        Assert.Equal(("Hengl, Philip", "Someone, Other"), (games[2].White, games[2].Black));
    }

    [Fact]
    public async Task Import_ViaShareLink_StoresNoUploader_EvenWithNames()
    {
        await SeedAsync();
        var r = await Club().ImportPgnAsync(null, Pgn("Hengl, Philip", "Schnabl, Andreas") + Pgn("Oberschmid, Patrik", "Hengl, Philip",
            "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 4. Qc2 O-O 5. a3 Bxc3+ 6. Qxc3 b6 7. Bg5 Bb7 8. f3 h6 9. Bh4 d5 10. e3 Nbd7 1-0"), null);
        Assert.Equal((2, 1), (r.Added, r.Anonymized));
        Assert.All(_db.LeagueClubGames, g => Assert.Null(g.UploadedByUserId));
        Assert.Equal("Schwaz", _db.LeagueClubGames.Single(g => g.Anonymized).White);   // ohne Konto: Schwaz-Spieler, nicht „ich"
    }

    [Fact]
    public async Task Import_Rejections_NameTheReason()
    {
        var me = await SeedAsync();
        var pgn = string.Join("\n",
            Pgn("Nobody, Else", "Someone, Other"),
            Pgn("Oberschmid, Patrik", "Nobody, Else", "1. d4 d5 2. c4 e6 1-0"),
            Pgn("Hengl, Philip", "Oberschmid, Patrik", "1. e4 e5 2. Ke3 1-0"),
            Pgn("Hengl, Philip", "Oberschmid, Patrik", "1. e4 1-0", extra: "[FEN \"8/8/8/4k3/8/8/4P3/4K3 w - - 0 1\"]\n[SetUp \"1\"]"));
        var r = await Club().ImportPgnAsync(me, pgn, null);

        Assert.Equal(0, r.Added);
        Assert.Equal(new[] { (1, "noLeaguePlayer"), (2, "onlyOwnClub"), (3, "illegal"), (4, "fromPosition") },
            r.Failed.Select(f => (f.Index, f.Reason)));
        Assert.Empty(_db.LeagueClubGames);
    }

    [Fact]
    public async Task Preview_ResultOnlyGame_IsNoMoves_AndABomDoesNotShiftTheNumbers()
    {
        var me = await SeedAsync();
        var pgn = "\uFEFF" + Pgn("Hengl, Philip", "Schnabl, Andreas") + Pgn("Oberschmid, Patrik", "Hengl, Philip", " *");
        var p = await Club().PreviewAsync(me, pgn);
        Assert.Equal(2, p.Games.Count);
        Assert.Equal((null, "Hengl, Philip"), (p.Games[0].Error, p.Games[0].White.Match.Name));
        Assert.Equal("noMoves", p.Games[1].Error);
    }

    [Fact]
    public async Task Import_ProfileFideId_FindsMySide()
    {
        var me = await SeedAsync();
        (await _db.UserProfiles.SingleAsync()).FideId = "999";
        (await _db.UserProfiles.SingleAsync()).LastName = null;
        (await _db.UserProfiles.SingleAsync()).FirstName = null;
        await _db.SaveChangesAsync();
        var p = await Club().PreviewAsync(me, Pgn("Hengl, Philip", "Fremder, Name", extra: "[BlackFideId \"999\"]"));
        Assert.True(p.Games[0].Black.Owner);
        Assert.True(p.Games[0].Black.Replace);                                   // ich — auch ohne Meldeliste
    }

    [Fact]
    public async Task Import_SameGameTwice_IsOneGame_ButShortGamesNeedTheSameNames()
    {
        var me = await SeedAsync();
        var club = Club();
        var r1 = await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip") + Pgn("Oberschmid, Patrik", "Hengl, Philip"), null);
        var r2 = await club.ImportPgnAsync(me, Pgn("Hengl, Philip", "Schnabl, Andreas", date: "2024.10.01"), null);  // gleiche Züge
        Assert.Equal((1, 1), (r1.Added, r1.Duplicates));
        Assert.Equal((0, 1), (r2.Added, r2.Duplicates));

        const string shortGame = "1. e4 e5 2. Nf3 Nc6 1-0";
        var r3 = await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip", shortGame), null);
        var r4 = await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Schnabl, Andreas", shortGame), null);
        var r5 = await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Schnabl, Andreas", shortGame, date: "2025.01.01"), null);
        Assert.Equal((1, 1, 1), (r3.Added, r4.Added, r5.Added));
        Assert.Equal(4, _db.LeagueClubGames.Count());
    }

    // ── Spielerkarten ───────────────────────────────────────────────

    private const string ExternalSameGame =
        "[Event \"TMM Landesliga\"]\n[Date \"2024.05.12\"]\n[White \"Oberschmid, Patrik\"]\n[Black \"Hengl, Philip\"]\n[Result \"1-0\"]\n\n" +
        LongGame + "\n";

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
        await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip"), null);            // = die chess-results-Partie
        await club.ImportPgnAsync(me, Pgn("Schnabl, Andreas", "Oberschmid, Patrik", "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 1-0"), null);

        var hengl = await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == "222");
        Assert.Equal(1, hengl.GameCount);                                  // dieselbe Partie nur einmal
        Assert.Equal(ExternalSameGame, hengl.Pgn);                         // gespeichert bleiben nur die fremden
        var schnabl = await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == "333");
        Assert.Equal(1, schnabl.GameCount);                                // Karte neu angelegt
        Assert.Equal(1, JsonNode.Parse(schnabl.ProfileJson)!["src"]!["Verein"]!.GetValue<int>());
        Assert.Equal("Schnabl, Andreas Dr.", schnabl.Name);

        var roster = JsonNode.Parse(_db.LeagueViews.AsNoTracking().Single().Json)!["fixtures"]!["Absam"]!["1"]!["roster"]!.AsArray();
        Assert.Equal(1, roster[1]!["g"]!.GetValue<int>());                 // Partienzahl in der Ansicht nachgezogen

        var pgn = await new LeagueProfileStore(_db).PgnAsync("333", default);
        Assert.Contains("Schwaz", pgn!.Value.Pgn);                         // Download = fremde + Vereinspartien
    }

    [Fact]
    public async Task Refresh_MergingChessResults_KeepsTheClubGamesInTheCard()
    {
        var me = await SeedAsync();
        await Club().ImportPgnAsync(me, Pgn("Schnabl, Andreas", "Oberschmid, Patrik", "1. d4 Nf6 2. c4 e6 1-0"), null);
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

    // ── Megabase + Eröffnungsbaum ───────────────────────────────────

    private static string MegaGame(string white, string? wf, string black, string? bf, string moves, string date, string result = "1-0") =>
        $"[LeagueSource \"Mega\"]\n[Event \"Open\"]\n[Date \"{date}\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"{result}\"]\n"
        + (wf is null ? "" : $"[WhiteFideId \"{wf}\"]\n") + (bf is null ? "" : $"[BlackFideId \"{bf}\"]\n") + $"\n{moves} {result}\n";

    [Fact]
    public async Task MegaImport_OnlyByFideId_SourceKept_AndSecondImportAddsNothing()
    {
        await SeedAsync();
        var store = new LeagueProfileStore(_db);
        var pgn = string.Join("\n",
            MegaGame("Hengl, Philip", "222", "Fremd, A", "5", "1. e4 c5 2. Nf3 d6", "2019.03.01"),
            MegaGame("Hengl, Philip", "222", "Fremd, B", "6", "1. e4 e5 2. Nf3 Nc6", "2020.03.01", "0-1"),
            MegaGame("Hengl, Philip", null, "Fremd, C", "7", "1. d4 d5", "2021.03.01"),          // ohne FIDE-ID: nicht zugeordnet
            MegaGame("Unbekannt, X", "12345", "Fremd, D", "8", "1. c4 c5", "2021.03.01"));
        var (games, players) = await store.ImportGamesAsync(pgn, "Mega", default);
        Assert.Equal((2, 1), (games, players));

        var row = await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == "222");
        Assert.Equal(2, row.GameCount);
        Assert.Equal(2, JsonNode.Parse(row.ProfileJson)!["src"]!["Mega"]!.GetValue<int>());
        Assert.Contains("[LeagueSource \"Mega\"]", row.Pgn);
        Assert.Equal("Mega", LeagueProfileBuilder.StoredSource(new Dictionary<string, string> { ["LeagueSource"] = "Mega", ["WhiteFideId"] = "1" }));

        await store.ImportGamesAsync(pgn, "Mega", default);
        Assert.Equal(2, (await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == "222")).GameCount);
    }

    [Fact]
    public async Task Tree_CountsNextMoves_WithScoreFromThePlayersSide()
    {
        var me = await SeedAsync();
        var store = new LeagueProfileStore(_db);
        await store.ImportGamesAsync(string.Join("\n",
            MegaGame("Hengl, Philip", "222", "A, A", null, "1. e4 c5 2. Nf3 d6", "2019.03.01"),
            MegaGame("Hengl, Philip", "222", "B, B", null, "1. e4 e5 2. Nf3 Nc6", "2023.03.01", "0-1"),
            MegaGame("Hengl, Philip", "222", "C, C", null, "1. d4 d5", "2021.03.01", "1/2-1/2"),
            MegaGame("D, D", null, "Hengl, Philip", "222", "1. e4 c6", "2022.03.01", "0-1")), "Mega", default);
        await Club().ImportPgnAsync(me, Pgn("Hengl, Philip", "Oberschmid, Patrik"), null);        // Vereinspartie zählt mit

        var root = (await store.TreeAsync("222", "w", null, default))!;
        Assert.Equal(4, root["total"]!.GetValue<int>());
        var moves = root["moves"]!.AsArray();
        Assert.Equal(("e4", 3), (moves[0]!["san"]!.GetValue<string>(), moves[0]!["n"]!.GetValue<int>()));
        Assert.Equal(67, moves[0]!["score"]!.GetValue<int>());              // 1 + 0 + 1 von 3
        Assert.Equal("2024", moves[0]!["last"]!.GetValue<string>());
        Assert.Equal(("d4", 50), (moves[1]!["san"]!.GetValue<string>(), moves[1]!["score"]!.GetValue<int>()));

        var e4 = (await store.TreeAsync("222", "w", "e4", default))!;
        Assert.Equal(new[] { "c5", "e5" }, e4["moves"]!.AsArray().Select(m => m!["san"]!.GetValue<string>()).OrderBy(x => x));

        var black = (await store.TreeAsync("222", "s", "e4", default))!;
        Assert.Equal(1, black["total"]!.GetValue<int>());
        Assert.Equal(100, black["moves"]!.AsArray()[0]!["score"]!.GetValue<int>());   // 0-1 = Sieg für Schwarz
    }

    // ── Einzelne Partie, Liste, Löschen ─────────────────────────────

    [Fact]
    public async Task AddGame_ReplaceFlags_Fide_Year_AndIllegalMovesNamed()
    {
        var me = await SeedAsync();
        var club = Club();
        var (bad, reason, message) = await club.AddGameAsync(me, new LeagueClubGameRequest { Moves = new() { "e4", "e5", "Ke3" }, White = "x", Black = "y" });
        Assert.Null(bad);
        Assert.Equal("illegal", reason);
        Assert.Contains("ply 3", message);

        var (game, r, _) = await club.AddGameAsync(me, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "c5", "Nf3" }, White = "Didi", Black = "irgendwie falsch geschrieben", BlackFide = "222",
            WhiteReplace = true, Year = 2026, Result = "0-1", Event = "Simultan",
        });
        Assert.Null(r);
        Assert.Equal(("Schwaz", "Hengl, Philip", "222", 2026, "0-1"), (game!.White, game.Black, game.BlackFide, game.Year, game.Result));
        Assert.Null(game.Event);
        Assert.Contains("1. e4 c5 2. Nf3", game.Pgn);
        Assert.Equal("duplicate", (await club.AddGameAsync(me, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "c5", "Nf3" }, White = "Didi", Black = "Hengl, Philip", Year = 2026, WhiteReplace = true,
        })).Reason);
        Assert.Equal("onlyOwnClub", (await club.AddGameAsync(me, new LeagueClubGameRequest
        {
            Moves = new() { "d4" }, White = "Binder, Moriz", Black = "Hengl, Philip", WhiteReplace = true, BlackReplace = true,
        })).Reason);
    }

    [Fact]
    public async Task ListAndDelete_OwnNamedGames_AnonymousOnlyByManagers()
    {
        var me = await SeedAsync();
        var club = Club();
        await club.ImportPgnAsync(me, Pgn("Hengl, Philip", "Schnabl, Andreas"), null);
        await club.ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Schnabl, Andreas", "1. d4 d5 1-0"), null);

        var list = await club.ListAsync(me, false, null, null, 1, default);
        Assert.Equal(2, list.Total);
        var named = list.Items.Single(i => !i.Anonymized);
        var anon = list.Items.Single(i => i.Anonymized);
        Assert.Equal("1.e4 c5 2.Nf3 d6 3.d4 cxd4", named.Opening);
        Assert.True(named.CanDelete);
        Assert.False(anon.CanDelete);
        Assert.Equal(2, (await club.ListAsync(me, false, "333", null, 1, default)).Items.Count);
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
        await Club().ImportPgnAsync(me, Pgn("Oberschmid, Patrik", "Hengl, Philip") + Pgn("Oberschmid, Patrik", "Schnabl, Andreas", "1. d4 d5 1-0"), null);
        var pgn = await Club().ExportAsync(null, null, default);
        Assert.Equal(2, RookHub.Api.Services.PgnParser.SplitGames(pgn).Count());
    }
}

/// <summary>Megabase-Spielerverzeichnis, Suche mit Häkchen, Lichess-Studien-Adressen.</summary>
public class LeagueMegaPlayersAndLichessTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private async Task SeedAsync()
    {
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Absam", Name = "Hengl, Philip", NameKey = "hengl, philip", FideId = "222" });
        await _db.SaveChangesAsync();
        const string tsv = "Hengl, Philip\t222\t120\t2025\t2214\nHengl, Peter\t777\t12\t2019\t1850\nMüller, Hans\t\t5\t2001\t\nOhneZeile\n";
        Assert.Equal(3, await new LeagueMegaPlayers(_db).ReplaceAsync(new StringReader(tsv), default));
    }

    [Fact]
    public async Task Search_WordPrefixes_WithoutAccents_MostGamesFirst()
    {
        await SeedAsync();
        var mega = new LeagueMegaPlayers(_db);
        Assert.Equal(new[] { "Hengl, Philip", "Hengl, Peter" }, (await mega.SearchAsync("heng", 10, default)).Select(p => p.Name));
        Assert.Equal(new[] { "Hengl, Peter" }, (await mega.SearchAsync("peter hengl", 10, default)).Select(p => p.Name));
        Assert.Equal("Müller, Hans", (await mega.SearchAsync("muller", 10, default)).Single().Name);
        Assert.Empty(await mega.SearchAsync("x", 10, default));                      // unter zwei Buchstaben keine Suche
        await mega.ReplaceAsync(new StringReader("Neu, Name\t1\t1\t2020\t\n"), default);
        Assert.Single(_db.LeagueMegaPlayers);                                        // ersetzt, nicht ergänzt
    }

    [Fact]
    public async Task Suggest_All_AddsMegabasePlayers_LeaguePlayersStayLeague()
    {
        await SeedAsync();
        var club = new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance);
        Assert.Single(await club.SuggestAsync("hengl", false, default));
        var all = await club.SuggestAsync("hengl", true, default);
        Assert.Equal(new[] { ("Hengl, Philip", "liga", true), ("Hengl, Peter", "mega", false) },
            all.Select(p => (p.Name, p.Source, p.League)));                           // Philip nur einmal: als Ligaspieler
    }

    [Fact]
    public async Task Import_ChosenMegabasePlayer_KeepsItsFideId()
    {
        await SeedAsync();
        var club = new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance);
        var (game, reason, _) = await club.AddGameAsync(null, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "e5" }, White = "Hengl, Philip", Black = "Hengl, Peter", BlackFide = "777",
        });
        Assert.Null(reason);
        Assert.Equal(("222", "777"), (game!.WhiteFide, game.BlackFide));
        Assert.Equal("Hengl, Peter", game.Black);
    }

    [Theory]
    [InlineData("https://lichess.org/study/AbCdEf12", "AbCdEf12", null, "/api/study/AbCdEf12.pgn")]
    [InlineData("lichess.org/study/AbCdEf12/ZyXwVu98#last", "AbCdEf12", "ZyXwVu98", "/api/study/AbCdEf12/ZyXwVu98.pgn")]
    [InlineData("http://www.lichess.org/study/AbCdEf12?x=1", "AbCdEf12", null, "/api/study/AbCdEf12.pgn")]
    public void LichessStudy_AcceptsStudyAndChapterLinks(string url, string study, string? chapter, string path)
    {
        var s = LichessStudySource.Parse(url);
        Assert.Equal((study, chapter), (s!.Value.Study, s.Value.Chapter));
        Assert.Equal(path, LichessStudySource.ApiPath(s.Value));
    }

    [Theory]
    [InlineData("https://evil.example/study/AbCdEf12")]
    [InlineData("https://lichess.org/AbCdEf12")]
    [InlineData("https://lichess.org/study/short")]
    [InlineData("https://lichess.org.evil.example/study/AbCdEf12")]
    [InlineData("")]
    public void LichessStudy_RejectsEverythingElse(string url) => Assert.Null(LichessStudySource.Parse(url));

    private sealed class StubFactory(HttpStatusCode code, string body) : IHttpClientFactory
    {
        public List<string> Paths { get; } = new();
        public HttpClient CreateClient(string name) => new(new Handler(this, code, body)) { BaseAddress = new Uri("https://lichess.org") };
        private sealed class Handler(StubFactory f, HttpStatusCode code, string body) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
            {
                f.Paths.Add(r.RequestUri!.PathAndQuery);
                return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
            }
        }
    }

    [Fact]
    public async Task LichessStudy_FetchesOnlyTheFixedApi_AndNamesFailures()
    {
        var ok = new StubFactory(HttpStatusCode.OK, "[Event \"S: 1\"]\n\n1. e4 *\n");
        var (pgn, reason) = await new LichessStudySource(ok).FetchAsync("https://lichess.org/study/AbCdEf12", default);
        Assert.Null(reason);
        Assert.StartsWith("[Event", pgn);
        Assert.Equal(new[] { "/api/study/AbCdEf12.pgn" }, ok.Paths);
        Assert.Equal("lichessNotFound", (await new LichessStudySource(new StubFactory(HttpStatusCode.NotFound, ""))
            .FetchAsync("https://lichess.org/study/AbCdEf12", default)).Reason);
        var never = new StubFactory(HttpStatusCode.OK, "x");
        Assert.Equal("invalidUrl", (await new LichessStudySource(never).FetchAsync("https://evil.example/x", default)).Reason);
        Assert.Empty(never.Paths);
    }
}
