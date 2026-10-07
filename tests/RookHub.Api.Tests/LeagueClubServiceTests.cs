using RookHub.Api.Services;
using System.Linq.Expressions;
using System.Net;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
        Player(7, "Testdorf", "Oberschmid, Patrik", "900");
        Player(7, "Testdorf", "Binder, Moriz", "111");
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
            new LeagueRosterIndex.Row(1, "Testdorf", "Weg, Gegangen", "weg, gegangen", "10", "2025/26"),
            new LeagueRosterIndex.Row(2, "Absam", "Weg, Gegangen", "weg, gegangen", "10", "2026/27"),
            new LeagueRosterIndex.Row(3, "Absam", "Neu, Dazu", "neu, dazu", "20", "2025/26"),
            new LeagueRosterIndex.Row(4, "Testdorf", "Neu, Dazu", "neu, dazu", "20", "2026/27"),
        }, TestClubs.Home.OwnsTeam);
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

    [Fact]
    public void Suggest_EveryWordAnywhere_PrefixesFirst()
    {
        // Wunsch 2026-09-28: „bert rud soll Bertl Rudolf finden" — jedes Wort IRGENDWO im Namen, Wortanfänge zuerst.
        var roster = Roster(("Bertl, Rudolf", "1"), ("Albertl, Rudi", "2"), ("Hengl, Philip", "3"));
        Assert.Equal(new[] { "Bertl, Rudolf", "Albertl, Rudi" }, roster.Suggest("bert rud", 5).Select(p => p.Name));
        Assert.Equal(new[] { "Albertl, Rudi", "Bertl, Rudolf" }, roster.Suggest("ertl", 5).Select(p => p.Name));
        Assert.Empty(roster.Suggest("bert xyz", 5));
    }

    [Theory]
    [InlineData("FM Humer, Wolfgang", "Humer, Wolfgang")]
    [InlineData("Humer, Wolfgang FM", "Humer, Wolfgang")]
    [InlineData("WGM Humer Wolfgang", "Humer Wolfgang")]
    [InlineData("Dr. Huber, Franz", "Huber, Franz")]
    [InlineData("Dipl.-Ing. Berger, Hans", "Berger, Hans")]
    [InlineData("DI Mair Josef", "Mair Josef")]
    [InlineData("Im, Seong", "Im, Seong")]            // ein Name, kein Titel
    [InlineData("Di Marco, Luca", "Di Marco, Luca")]
    [InlineData("FM", "FM")]
    public void StripTitles_ChessAndAcademicTitles_ButNotNames(string name, string expected) =>
        Assert.Equal(expected, LeagueNames.StripTitles(name));

    [Fact]
    public void Match_WithATitleInFront_FindsThePlayer() =>
        Assert.Equal("1600370", Roster(("Humer, Wolfgang", "1600370")).Match("FM Humer, Wolfgang", null).Person?.Fide);

    [Fact]
    public void Suggest_UmlautsInEverySpelling_AndAFideIdNumber()
    {
        // Gemeldet 2026-09-28: „Höcher" fand „Hoecher, Michael" nicht (ChessBase schreibt Umlaute aus).
        var roster = Roster(("Hoecher, Michael", "1271145"), ("Müller, Hans", "444"));
        Assert.Equal("Hoecher, Michael", roster.Suggest("Höcher", 5).Single().Name);
        Assert.Equal("Müller, Hans", roster.Suggest("mueller ha", 5).Single().Name);
        Assert.Equal("Hoecher, Michael", roster.Suggest("1271145", 5).Single().Name);
        Assert.Empty(roster.Suggest("9999999", 5));
        Assert.Equal("Hoecher, Michael", roster.Suggest("FM Hoecher", 5).Single().Name);
    }

    [Theory]
    [InlineData("Kostic")]
    [InlineData("Kostic, ?")]
    [InlineData("KOSTIC,")]
    public void Match_OnlyALastName_FindsTheOneLeaguePlayer_AndSaysSo(string name)
    {
        var hit = Roster(("Kostic, Milan", "42"), ("Hengl, Philip", "222")).Match(name, null);
        Assert.Equal("42", hit.Person?.Fide);
        Assert.True(hit.LastNameOnly);
        Assert.True(LeagueClubService.MatchDto(hit).LastNameOnly);
    }

    [Fact]
    public void Match_OnlyALastName_TwoPlayers_IsAmbiguous_AndAFullNameIsNoLastNameMatch()
    {
        var roster = Roster(("Kostic, Milan", "42"), ("Kostic, Vera", "43"));
        var hit = roster.Match("Kostic", null);
        Assert.True(hit.Ambiguous);
        Assert.Equal(2, hit.Candidates.Count);
        Assert.False(roster.Match("Kostic, Zoran", null).League);          // anderer Vorname: kein Nachnamen-Treffer
        Assert.False(roster.Match("Kostic, Milan", null).LastNameOnly);
    }

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
        var p = await Club().PreviewAsync(TestClubs.Home, me, pgn);

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

    /// <summary>0.590.0: die Übersicht gibt je Partie ihren eigenen PGN-Text zurück — die Seite importiert damit
    /// portionsweise, und Portion für Portion ergibt dasselbe wie die ganze Datei auf einmal.</summary>
    [Fact]
    public async Task Preview_GivesEachGameItsOwnPgn_ImportingThemInPortionsEqualsTheWholeFile()
    {
        var me = await SeedAsync();
        var pgn = string.Join("\n",
            Pgn("Oberschmid, Patrik", "Hengl, Philip", extra: "[WhiteElo \"1850\"]"),
            Pgn("Hengl Philip", "Schnabl, Andreas", "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 5. e3 O-O 6. Nf3 h6 7. Bh4 b6 8. cxd5 Nxd5 9. Bxe7 Qxe7 10. Nxd5 exd5 1-0"),
            Pgn("Hengl, Philip", "Schnabl, Andreas", "1. e4 e5 2. Ke3 1-0"),      // illegal → kein eigener Text
            Pgn("Oberschmid, Patrik", "Hengl, Philip"));                         // wie 1 → doppelt
        var p = await Club().PreviewAsync(TestClubs.Home, me, pgn);
        Assert.Null(p.Games[2].Pgn);
        var one = await Club().PreviewAsync(TestClubs.Home, me, p.Games[0].Pgn!);                  // der Text allein liest sich gleich
        Assert.Equal((p.Games[0].Opening, p.Games[0].Year, p.Games[0].Black.Match.Fide),
            (one.Games[0].Opening, one.Games[0].Year, one.Games[0].Black.Match.Fide));

        // Portion 1: Partie 1 + 2, Portion 2: Partie 4 — Nummern je Portion ab 1, die Entscheidungen wie die Seite sie
        // schickt (Vorgaben der Übersicht, „ersetzen" eingeschlossen).
        LeagueClubImportGameDecision As(int i, int n) => new()
        {
            Index = n, White = new() { Replace = p.Games[i].White.Replace }, Black = new() { Replace = p.Games[i].Black.Replace },
        };
        var first = await Club().ImportPgnAsync(TestClubs.Home, me, p.Games[0].Pgn + "\n" + p.Games[1].Pgn, [As(0, 1), As(1, 2)]);
        Assert.Equal((2, 1), (first.Added, first.Anonymized));
        var second = await Club().ImportPgnAsync(TestClubs.Home, me, p.Games[3].Pgn!, [As(3, 1)]);
        Assert.Equal((0, 1), (second.Added, second.Duplicates));                  // in Portion 1 schon gespeichert
        var again = await Club().ImportPgnAsync(TestClubs.Home, me, p.Games[0].Pgn + "\n" + p.Games[1].Pgn, [As(0, 1), As(1, 2)]);  // Antwort verloren → nochmal
        Assert.Equal((0, 2), (again.Added, again.Duplicates));
        Assert.Equal(2, await _db.LeagueClubGames.CountAsync());
    }

    // ── Import ──────────────────────────────────────────────────────

    [Fact]
    public async Task Import_Defaults_ReplacesSchwazPlayers_AndStoresNeitherThemNorTheUploader()
    {
        var me = await SeedAsync();
        var r = await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Hengl, Philip", extra: "[WhiteElo \"1850\"]\n[BlackElo \"2172\"]"), null);

        Assert.Equal((1, 1, 0), (r.Added, r.Anonymized, r.Failed.Count));
        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.Equal(("Testdorf", "Hengl, Philip"), (g.White, g.Black));
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

    /// <summary>Korrigieren (0.660.0): Züge der Vereinspartie neu — nur Hochladender/Verwalter, legal, neuer Dubletten-Schlüssel;
    /// die verbundenen Kopien ziehen mit (Kopfdaten und Datum der Kopie bleiben), Analyse-Verweis und Training der Kopie fallen.</summary>
    [Fact]
    public async Task CorrectMoves_RewritesTheClubGame_AndEveryLinkedCopy()
    {
        var me = await SeedAsync();
        await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Schnabl, Andreas Dr."), null);
        var g = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.Equal(me, g.UploadedByUserId);
        var sans = PgnParser.ExtractMainlineSans(PgnParser.SplitGames(g.Pgn).First().MoveText);
        var shorter = sans.Take(sans.Count - 2).ToList();

        var other = new AppUser { Username = "x", Email = "x@test", PasswordHash = "x" };
        _db.AppUsers.Add(other);
        await _db.SaveChangesAsync();
        Assert.Equal("forbidden", (await Club().CorrectMovesAsync(TestClubs.Home, other.Id, false, g.Id, shorter)).Reason);
        Assert.Equal("illegal", (await Club().CorrectMovesAsync(TestClubs.Home, me, false, g.Id, new[] { "e4", "Ke2", "Ke7", "Kxe7" })).Reason);

        var copy = new SavedGame { UserId = other.Id, Source = "pgn", Pgn = "[Event \"Kopie\"]\n[Date \"2024.??.??\"]\n[White \"Hengl\"]\n[Black \"Schnabl\"]\n[Result \"1-0\"]\n\n1. e4 1-0\n",
            White = "Hengl", Black = "Schnabl", Result = "1-0", LeagueClubGameId = g.Id, GameAnalysisId = 77, CreatedAt = Now };
        _db.SavedGames.Add(copy);
        await _db.SaveChangesAsync();

        var (game, newSans, reason) = await Club().CorrectMovesAsync(TestClubs.Home, me, false, g.Id, shorter);
        Assert.Null(reason);
        Assert.Equal(shorter.Count, game!.Plies);
        Assert.Equal(LeagueClubService.HashOf(shorter), game.MovesHash);
        Assert.Equal(1, await SavedGameService.ApplyClubMovesAsync(_db, g.Id, newSans!, null));
        var c = await _db.SavedGames.AsNoTracking().SingleAsync();
        Assert.Equal(PgnParser.ExtractMainlineSans(PgnParser.SplitGames(c.Pgn).First().MoveText), shorter);
        Assert.Contains("[Event \"Kopie\"]", c.Pgn);                // Kopfdaten der Kopie bleiben
        Assert.Contains("[Date \"2024.??.??\"]", c.Pgn);             // auch das Datum mit nur dem Jahr
        Assert.Null(c.GameAnalysisId);                                  // Analyse der alten Zugfolge fällt
        Assert.Equal(shorter.Count, c.MoveCount);
    }

    /// <summary>Wunsch 2026-10-04: über den Teilen-Link hochgeladen → der Browser merkt sich einen Schlüssel; nach dem Anmelden
    /// und einem JA gehören die Partien dem Konto (auch „Schwaz", nur mit dieser Zustimmung) und sind bearbeitbar.</summary>
    [Fact]
    public async Task Claim_AfterLogin_AssignsAnonymousShareUploads_OnlyWithTheBrowsersKey()
    {
        var me = await SeedAsync();
        var r = await Club().ImportViaShareAsync(TestClubs.Home, "TOKEN-1", Pgn("Oberschmid, Patrik", "Hengl, Philip"), null);
        Assert.Single(r.Ids);
        Assert.Equal(32, r.ClaimKey!.Length);
        var g = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.Null(g.UploadedByUserId);                                                 // erst einmal niemandem
        Assert.Equal(LeagueClubService.ClaimHashOf(r.ClaimKey), g.ClaimKeyHash);

        Assert.Equal((0, 0), await Club().ClaimPreviewAsync(new[] { LeagueClubService.NewClaimKey() }));   // fremder Schlüssel
        var preview = await Club().ClaimPreviewAsync(new[] { r.ClaimKey, "kaputt" });
        Assert.Equal(1, preview.Games);
        Assert.Equal(g.Anonymized ? 1 : 0, preview.Anonymized);

        Assert.Equal(1, await Club().ClaimAsync(me, new[] { r.ClaimKey }));
        var claimed = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.Equal(me, claimed.UploadedByUserId);
        Assert.Null(claimed.ClaimKeyHash);                                                // Schlüssel verfällt
        Assert.Equal(0, await Club().ClaimAsync(me + 1, new[] { r.ClaimKey }));          // kein zweites Mal
        var mine = Assert.Single((await Club().ListAsync(TestClubs.Home, me, false, null, null, 1, default, mine: true)).Items);
        Assert.True(mine.CanDelete);                                                      // bearbeitbar, auch als „Schwaz"
    }

    [Fact]
    public async Task Claim_No_ForgetsTheKey_GameStaysWithoutUploader()
    {
        await SeedAsync();
        var r = await Club().ImportViaShareAsync(TestClubs.Home, "TOKEN-1", Pgn("Oberschmid, Patrik", "Hengl, Philip"), null);
        Assert.Equal(1, await Club().ForgetClaimsAsync(new[] { r.ClaimKey! }));
        var g = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.Null(g.ClaimKeyHash);
        Assert.Null(g.UploadedByUserId);
        Assert.Equal((0, 0), await Club().ClaimPreviewAsync(new[] { r.ClaimKey! }));
    }

    /// <summary>Lasche „Meine Partien" (0.652.0): nur mit dem eigenen Konto hochgeladene — fremde und anonyme („Schwaz",
    /// ohne Hochladenden) nicht; dort darf man bearbeiten und löschen.</summary>
    [Fact]
    public async Task List_Mine_OnlyOwnUploads()
    {
        var me = await SeedAsync();
        _db.LeagueClubGames.AddRange(
            new LeagueClubGame { ClubId = TestClubs.HomeId, White = "A", Black = "B", Pgn = "1. e4 *", MovesHash = "h1", UploadedByUserId = me },
            new LeagueClubGame { ClubId = TestClubs.HomeId, White = "C", Black = "D", Pgn = "1. d4 *", MovesHash = "h2", UploadedByUserId = me + 1 },
            new LeagueClubGame { ClubId = TestClubs.HomeId, White = "Testdorf", Black = "E", Pgn = "1. c4 *", MovesHash = "h3", Anonymized = true });
        await _db.SaveChangesAsync();

        var mine = await Club().ListAsync(TestClubs.Home, me, false, null, null, 1, default, mine: true);
        var g = Assert.Single(mine.Items);
        Assert.Equal("A", g.White);
        Assert.True(g.CanDelete);
        Assert.Equal(3, (await Club().ListAsync(TestClubs.Home, me, false, null, null, 1, default)).Total);
    }

    /// <summary>Wunsch 2026-10-04: „merk dir im Hintergrund den echten Namen der Schwazer Spieler, damit ich später
    /// Auswertungen fahren kann — niemals in der GUI ausgeben". Gespeichert in eigenen Spalten, in KEINER Ausgabe.</summary>
    [Fact]
    public async Task Import_Replaced_KeepsTheRealNameInternally_ButNoOutputCarriesIt()
    {
        var me = await SeedAsync();
        await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Hengl, Philip"), null);

        var g = await _db.LeagueClubGames.AsNoTracking().SingleAsync();
        Assert.Equal(("Oberschmid, Patrik", "900"), (g.WhiteRealName, g.WhiteRealFide));
        Assert.Equal(((string?)null, (string?)null), (g.BlackRealName, g.BlackRealFide));   // nicht ersetzt → nichts doppelt

        var outputs = new[]
        {
            System.Text.Json.JsonSerializer.Serialize((await Club().ListAsync(TestClubs.Home, me, true, null, null, 1, default)).Items),
            System.Text.Json.JsonSerializer.Serialize(await Club().GetAsync(TestClubs.Home, me, true, g.Id)),
            System.Text.Json.JsonSerializer.Serialize(LeagueClubService.ToDto(g, me, true)),
            await Club().ExportAsync(TestClubs.Home, null, null, default),
        };
        foreach (var o in outputs)
        {
            Assert.DoesNotContain("Oberschmid", o);
            Assert.DoesNotContain("\"900\"", o);
        }
    }

    [Fact]
    public async Task Import_WithoutSchwazPlayers_KeepsNamesAndTheUploader()
    {
        var me = await SeedAsync();
        await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Hengl Philip", "Schnabl, Andreas"), null);

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
        var r = await Club().ImportPgnAsync(TestClubs.Home, me, pgn, new List<LeagueClubImportGameDecision>
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
        Assert.Equal(("Schnabl, Andreas Dr.", "Testdorf", "333"), (games[0].White, games[0].Black, games[0].WhiteFide));
        Assert.Equal(("Hengl, Philip", "Binder, Moriz", false), (games[1].White, games[1].Black, games[1].Anonymized));
        Assert.Equal(("Hengl, Philip", "Someone, Other"), (games[2].White, games[2].Black));
    }

    /// <summary>Wunsch 2026-09-28: „wenn ich einen Spieler umbenenne, merk dir das zum Original und matche das zukünftig
    /// bei allen selbst".</summary>
    [Fact]
    public async Task Import_Correction_IsRemembered_AndMatchesNextTimeForEveryone()
    {
        var me = await SeedAsync();
        var club = Club();
        var pgn = Pgn("Oberschmid, Patrik", "Hengl P.");                     // „Hengl P." erkennt der Abgleich (Anfangsbuchstabe)
        var pgn2 = Pgn("Oberschmid, Patrik", "Dr. Andi S.",
            "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 5. e3 O-O 6. Nf3 h6 7. Bh4 b6 8. cxd5 Nxd5 9. Bxe7 Qxe7 10. Nxd5 exd5 1-0");
        Assert.False((await club.PreviewAsync(TestClubs.Home, me, pgn2)).Games[0].Black.Match.League);   // „Andi S." kennt niemand

        var result = await club.ImportPgnAsync(TestClubs.Home, me, pgn2, new[]
        {
            new LeagueClubImportGameDecision { Index = 1, White = new() { Fide = "900", Replace = true }, Black = new() { Fide = "333" } },
        });
        Assert.Equal((1, 1), (result.Added, result.Remembered));
        var alias = Assert.Single(_db.LeagueNameAliases);
        Assert.Equal(("andi s.", "333", "Schnabl, Andreas Dr."), (alias.NameKey, alias.Fide, alias.Name));   // Titel weg, ohne Partie/Person

        // Nächstes Mal (auch über einen Teilen-Link, ohne Konto): von selbst Schnabl, als „gemerkt" markiert.
        var next = await club.PreviewAsync(TestClubs.Home, null, Pgn("Hengl, Philip", "Andi S.", "1. c4 e5 2. Nc3 Nf6 3. g3 d5 4. cxd5 Nxd5 5. Bg2 Nb6 6. Nf3 Nc6 7. O-O Be7 8. d3 O-O 9. a3 Be6 10. b4 f6 1-0"));
        var b = next.Games[0].Black.Match;
        Assert.Equal((true, true, "333"), (b.League, b.Alias, b.Fide));
        Assert.True((await club.MatchAsync(TestClubs.Home, "andi s.", null, default)).White.Alias);

        // Eine FIDE-ID aus der Partie, die ein Ligaspieler trägt, schlägt die Zuordnung.
        var withId = await club.PreviewAsync(TestClubs.Home, null, Pgn("Hengl, Philip", "Andi S.", extra: "[BlackFideId \"111\"]"));
        Assert.Equal(("111", false), (withId.Games[0].Black.Match.Fide, withId.Games[0].Black.Match.Alias));

        // Unveränderte Seiten (der Client schickt die FIDE-ID der Vorgabe mit) und Teilen-Links merken nichts.
        var same = await club.ImportPgnAsync(TestClubs.Home, me, pgn, new[]
        {
            new LeagueClubImportGameDecision { Index = 1, White = new() { Fide = "900", Replace = true }, Black = new() { Fide = "222" } },
        });
        Assert.Equal(0, same.Remembered);
        var anon = await club.ImportPgnAsync(TestClubs.Home, null, Pgn("Oberschmid, Patrik", "Irgendwer", "1. e4 e6 2. d4 d5 3. Nc3 Bb4 4. e5 c5 5. a3 Bxc3+ 6. bxc3 Ne7 7. Qg4 Qc7 8. Qxg7 Rg8 9. Qxh7 cxd4 10. Ne2 Nbc6 1-0"),
            new[] { new LeagueClubImportGameDecision { Index = 1, White = new() { Fide = "900", Replace = true }, Black = new() { Fide = "222" } } });
        Assert.Equal((1, 0), (anon.Added, anon.Remembered));
        Assert.Single(_db.LeagueNameAliases);

        // Eine neue Korrektur desselben Namens überschreibt die alte.
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Andi S.", "1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 5. O-O Be7 6. Re1 b5 7. Bb3 d6 8. c3 O-O 9. h3 Nb8 10. d4 Nbd7 1-0"),
            new[] { new LeagueClubImportGameDecision { Index = 1, White = new() { Fide = "900", Replace = true }, Black = new() { Fide = "222" } } });
        Assert.Equal("222", _db.LeagueNameAliases.AsNoTracking().Single().Fide);
    }

    [Fact]
    public async Task Import_ViaShareLink_StoresNoUploader_EvenWithNames()
    {
        await SeedAsync();
        var r = await Club().ImportPgnAsync(TestClubs.Home, null, Pgn("Hengl, Philip", "Schnabl, Andreas") + Pgn("Oberschmid, Patrik", "Hengl, Philip",
            "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 4. Qc2 O-O 5. a3 Bxc3+ 6. Qxc3 b6 7. Bg5 Bb7 8. f3 h6 9. Bh4 d5 10. e3 Nbd7 1-0"), null);
        Assert.Equal((2, 1), (r.Added, r.Anonymized));
        Assert.All(_db.LeagueClubGames, g => Assert.Null(g.UploadedByUserId));
        Assert.Equal("Testdorf", _db.LeagueClubGames.Single(g => g.Anonymized).White);   // ohne Konto: Schwaz-Spieler, nicht „ich"
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
        var r = await Club().ImportPgnAsync(TestClubs.Home, me, pgn, null);

        Assert.Equal(0, r.Added);
        Assert.Equal(new[] { (1, "noLeaguePlayer"), (2, "onlyOwnClub"), (3, "illegal"), (4, "fromPosition") },
            r.Failed.Select(f => (f.Index, f.Reason)));
        Assert.Empty(_db.LeagueClubGames);
    }

    /// <summary>Springer hin und her (ohne AutoEndgameRules endet das nie), <paramref name="plies"/> Halbzüge, dann
    /// <paramref name="tail"/> — mit Zugnummern.</summary>
    private static string Pendulum(int plies, string? tail = null)
    {
        var cycle = new[] { "Nf3", "Nf6", "Ng1", "Ng8" };
        var sb = new System.Text.StringBuilder();
        var all = Enumerable.Range(0, plies).Select(i => cycle[i % 4]).Concat(tail is null ? Array.Empty<string>() : new[] { tail });
        var i = 0;
        foreach (var san in all)
        {
            if (i % 2 == 0) sb.Append(i / 2 + 1).Append(". ");
            sb.Append(san).Append(' ');
            i++;
        }
        return sb.Append("1-0").ToString();
    }

    /// <summary>N3-002: die Länge zählt VOR dem Nachspielen, wie beim Partieformular. Vorher spielte Gera.Chess eine
    /// überlange Hauptvariante erst ganz nach — bis rund eine Million Halbzüge je Aufruf, auch anonym über den
    /// Teilen-Link —, bevor „tooLong" griff. Die dritte Partie ist am Ende illegal: nachgespielt hieße sie „illegal".</summary>
    [Fact]
    public async Task Preview_OverlongGame_IsTooLong_BeforeItIsReplayed()
    {
        var me = await SeedAsync();
        var pgn = string.Join("\n",
            Pgn("Oberschmid, Patrik", "Hengl, Philip", Pendulum(LeagueClubService.MaxPlies)),        // genau am Deckel
            Pgn("Oberschmid, Patrik", "Hengl, Philip", Pendulum(LeagueClubService.MaxPlies + 1)),    // einer darüber
            Pgn("Oberschmid, Patrik", "Hengl, Philip", Pendulum(20_000, tail: "Ke3")));             // riesig, am Ende illegal

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var p = await Club().PreviewAsync(TestClubs.Home, me, pgn);
        sw.Stop();

        Assert.Equal(new string?[] { null, "tooLong", "tooLong" }, p.Games.Select(g => g.Error));
        Assert.Equal(LeagueClubService.MaxPlies, p.Games[0].Plies);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"Übersicht {sw.Elapsed.TotalMilliseconds:F0} ms");
    }

    [Fact]
    public async Task Preview_ResultOnlyGame_IsNoMoves_AndABomDoesNotShiftTheNumbers()
    {
        var me = await SeedAsync();
        var pgn = "\uFEFF" + Pgn("Hengl, Philip", "Schnabl, Andreas") + Pgn("Oberschmid, Patrik", "Hengl, Philip", " *");
        var p = await Club().PreviewAsync(TestClubs.Home, me, pgn);
        Assert.Equal(2, p.Games.Count);
        Assert.Equal((null, "Hengl, Philip"), (p.Games[0].Error, p.Games[0].White.Match.Name));
        Assert.Equal("noMoves", p.Games[1].Error);
    }

    [Fact]
    public async Task Preview_UnknownName_OffersSimilarLeaguePlayers_KnownNameDoesNot()
    {
        var me = await SeedAsync();
        var p = await Club().PreviewAsync(TestClubs.Home, me, Pgn("Hengl, Phillip", "Schnabl, Andreas") + Pgn("Hengl, Phillip", "Niemand, Kennt"));
        var typo = p.Games[0].White.Match;
        Assert.False(typo.League);                                               // nicht erkannt …
        Assert.Equal("222", Assert.Single(typo.Similar).Fide);                   // … aber „Hengl, Philip" zur Schnellauswahl
        Assert.Empty(p.Games[0].Black.Match.Similar);                            // erkannt: nichts vorzuschlagen
        Assert.Same(typo.Similar, p.Games[1].White.Match.Similar);               // derselbe Name wird einmal gerechnet
        Assert.Empty(p.Games[1].Black.Match.Similar);
    }

    [Fact]
    public async Task Match_UnknownName_OffersSimilarLeaguePlayers()
    {
        await SeedAsync();
        var m = await Club().MatchAsync(TestClubs.Home, "Schnabel, Andreas", "Hengl, Philip", default);
        Assert.Equal("333", Assert.Single(m.White.Similar).Fide);
        Assert.Empty(m.Black.Similar);
    }

    [Fact]
    public async Task Import_ProfileFideId_FindsMySide()
    {
        var me = await SeedAsync();
        (await _db.UserProfiles.SingleAsync()).FideId = "999";
        (await _db.UserProfiles.SingleAsync()).LastName = null;
        (await _db.UserProfiles.SingleAsync()).FirstName = null;
        await _db.SaveChangesAsync();
        var p = await Club().PreviewAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Fremder, Name", extra: "[BlackFideId \"999\"]"));
        Assert.True(p.Games[0].Black.Owner);
        Assert.True(p.Games[0].Black.Replace);                                   // ich — auch ohne Meldeliste
    }

    [Fact]
    public async Task Import_SameGameTwice_IsOneGame_ButShortGamesNeedTheSameNames()
    {
        var me = await SeedAsync();
        var club = Club();
        var r1 = await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Hengl, Philip") + Pgn("Oberschmid, Patrik", "Hengl, Philip"), null);
        var r2 = await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Schnabl, Andreas", date: "2024.10.01"), null);  // gleiche Züge
        Assert.Equal((1, 1), (r1.Added, r1.Duplicates));
        Assert.Equal((0, 1), (r2.Added, r2.Duplicates));

        const string shortGame = "1. e4 e5 2. Nf3 Nc6 1-0";
        var r3 = await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Hengl, Philip", shortGame), null);
        var r4 = await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Schnabl, Andreas", shortGame), null);
        var r5 = await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Schnabl, Andreas", shortGame, date: "2025.01.01"), null);
        Assert.Equal((1, 1, 1), (r3.Added, r4.Added, r5.Added));
        Assert.Equal(4, _db.LeagueClubGames.Count());
    }

    /// <summary>Zählt die Abfragen eines InMemory-Kontexts: ohne Cache übersetzter Abfragen (<see cref="EveryQueryCompiled"/>)
    /// geht jede Ausführung durch die Übersetzung, und die meldet sich hier.</summary>
    private sealed class QueryCounter : IQueryExpressionInterceptor
    {
        public int Count;
        public Expression QueryCompilationStarting(Expression queryExpression, QueryExpressionEventData eventData)
        {
            Interlocked.Increment(ref Count);
            return queryExpression;
        }
    }

#pragma warning disable EF1001 // interne EF-Schnittstelle — nur im Test, um jede Ausführung zu zählen
    private sealed class EveryQueryCompiled : Microsoft.EntityFrameworkCore.Query.Internal.ICompiledQueryCache
    {
        public Func<QueryContext, TResult> GetOrAddQuery<TResult>(object cacheKey, Func<Func<QueryContext, TResult>> compiler) => compiler();
    }
#pragma warning restore EF1001

    /// <summary>Codereview 2026-09-29, N4-004: Übersicht und Import fragten Dubletten je Partie einzeln ab — bis 500 Abfragen
    /// je Aufruf, auch anonym über den Teilen-Link. Jetzt für alle Partien zusammen: gleich viele Abfragen bei 2 wie bei 40
    /// Partien derselben Spieler.</summary>
    [Fact]
    public async Task PreviewAndImport_LookUpDuplicatesForAllGamesTogether_NotOneQueryPerGame()
    {
        var counter = new QueryCounter();
#pragma warning disable EF1001
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(counter).ReplaceService<Microsoft.EntityFrameworkCore.Query.Internal.ICompiledQueryCache, EveryQueryCompiled>().Options);
#pragma warning restore EF1001
        foreach (var (name, fide) in new[] { ("Hengl, Philip", "222"), ("Schnabl, Andreas Dr.", "333") })
            db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = "Absam", Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });
        await db.SaveChangesAsync();
        var club = new LeagueClubService(db, NullLogger<LeagueClubService>.Instance, () => Now);
        static string Games(int count, int firstYear) =>
            string.Concat(Enumerable.Range(0, count).Select(i => Pgn("Hengl, Philip", "Schnabl, Andreas", date: $"{firstYear + i}.05.12")));
        async Task<int> Queries(Func<Task> act)
        {
            db.ChangeTracker.Clear();
            var before = counter.Count;
            await act();
            return counter.Count - before;
        }

        await club.ImportPgnAsync(TestClubs.Home, null, Games(1, 1900), null);                        // Karten anlegen, damit beide Läufe gleich beginnen
        var previewFew = await Queries(() => club.PreviewAsync(TestClubs.Home, null, Games(2, 1950)));
        var previewMany = await Queries(() => club.PreviewAsync(TestClubs.Home, null, Games(40, 1950)));
        var importFew = await Queries(() => club.ImportPgnAsync(TestClubs.Home, null, Games(2, 1910), null));
        var importMany = await Queries(() => club.ImportPgnAsync(TestClubs.Home, null, Games(40, 1950), null));

        Assert.True(previewFew > 0);                                                   // der Zähler sieht die Abfragen
        Assert.Equal(previewFew, previewMany);
        Assert.Equal(importFew, importMany);
        Assert.Equal(43, await db.LeagueClubGames.CountAsync());
        Assert.Equal((0, 40), ((await club.ImportPgnAsync(TestClubs.Home, null, Games(40, 1950), null)).Added,
            (await club.PreviewAsync(TestClubs.Home, null, Games(40, 1950))).Games.Count(g => g.Duplicate)));   // die Dubletten stimmen weiter
    }

    /// <summary>Codereview 2026-09-29, F7-007: Suche und Namensabgleich lasen bei JEDEM Aufruf (jede Tipp-Pause, auch über
    /// den Teilen-Link) alle Meldelisten-Zeilen und bauten den Index neu. Jetzt aus dem Cache, solange sich Anzahl und
    /// höchste Id der Zeilen nicht ändern — Meldelisten werden nur gelöscht und neu angelegt, das trifft den Schlüssel.</summary>
    /// <summary>Wunsch 2026-10-05: beim Auswählen eines Spielers die Elo mit vorbelegen — aus der JÜNGSTEN Meldeliste,
    /// international vor national.</summary>
    [Fact]
    public async Task Suggest_CarriesTheEloOfTheLatestRoster()
    {
        await SeedAsync();
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 3, Team = "Absam", Name = "Hengl, Philip", NameKey = LeagueNames.NameKey("Hengl, Philip"), FideId = "222", EloI = 2000 });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 9, Team = "Absam", Name = "Hengl, Philip", NameKey = LeagueNames.NameKey("Hengl, Philip"), FideId = "222", EloN = 2150 });
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 11, Team = "Absam", Name = "Hengl, Philip", NameKey = LeagueNames.NameKey("Hengl, Philip"), FideId = "222", EloI = 2172, EloN = 2100 });
        await _db.SaveChangesAsync();

        var hit = (await Club().SuggestAsync(TestClubs.Home, "hengl", false, default)).Single(p => p.Fide == "222");
        Assert.Equal(2172, hit.Elo);
    }

    [Fact]
    public async Task Roster_CachedAcrossRequests_RebuiltWhenTheRostersAreReplaced()
    {
        await SeedAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        LeagueClubService Request() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now, cache: cache);   // je Anfrage ein Dienst
        static string Names(List<LeagueRosterPersonDto> l) => string.Join(" | ", l.Select(p => p.Name));

        Assert.Equal("Hengl, Philip", Names(await Request().SuggestAsync(TestClubs.Home, "hengl", false, default)));
        Assert.Equal(1, cache.Count);

        // An Ort und Stelle geändert (das tut kein Schreibweg): nicht neu gelesen — der Index kommt aus dem Cache.
        var hengl = await _db.LeaguePlayers.SingleAsync(p => p.FideId == "222");
        hengl.Name = "Hengl, Philipp";
        hengl.NameKey = LeagueNames.NameKey(hengl.Name);
        await _db.SaveChangesAsync();
        Assert.Equal("Hengl, Philip", Names(await Request().SuggestAsync(TestClubs.Home, "hengl", false, default)));
        Assert.True((await Request().MatchAsync(TestClubs.Home, "Hengl, Philip", null, default)).White.League);

        // Wie LeagueRefresh.ReplaceAsync: Zeilen der Liga gelöscht und neu angelegt — neue Ids, neuer Index.
        var old = await _db.LeaguePlayers.Where(p => p.Tnr == 7 && p.Team == "Absam").ToListAsync();
        _db.LeaguePlayers.RemoveRange(old);
        Player(7, "Absam", "Hengl, Philipp", "222");
        Player(7, "Absam", "Schnabl, Andreas Dr.", "333");
        await _db.SaveChangesAsync();
        Assert.Equal("Hengl, Philipp", Names(await Request().SuggestAsync(TestClubs.Home, "hengl", false, default)));
        Assert.Equal(("Hengl, Philipp", "222"), ((await Request().MatchAsync(TestClubs.Home, "Hengl, Philipp", null, default)).White.Name,
            (await Request().MatchAsync(TestClubs.Home, "Hengl, Philipp", null, default)).White.Fide));
    }

    /// <summary>Die API baut <see cref="LeagueClubService"/> mit dem allgemeinen Cache (DI) — sonst wäre der Cache wirkungslos.</summary>
    [Fact]
    public async Task DependencyInjection_LeagueClubServiceGetsTheMemoryCache()
    {
        var services = new ServiceCollection();
        var dbName = Guid.NewGuid().ToString();
        services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddLogging();
        services.AddMemoryCache();
        services.AddScoped<LeagueClubService>();
        using var provider = services.BuildServiceProvider();
        using (var seed = provider.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = "Absam", Name = "Hengl, Philip", NameKey = LeagueNames.NameKey("Hengl, Philip"), FideId = "222" });
            db.SaveChanges();
        }

        using var scope = provider.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<LeagueClubService>().SuggestAsync(TestClubs.Home, "hengl", false, default));
        Assert.Equal(1, ((MemoryCache)provider.GetRequiredService<IMemoryCache>()).Count);
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
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Hengl, Philip"), null);            // = die chess-results-Partie
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Schnabl, Andreas", "Oberschmid, Patrik", "1. d4 Nf6 2. c4 e6 3. Nc3 Bb4 1-0"), null);

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
        Assert.Contains("Testdorf", pgn!.Value.Pgn);                         // Download = fremde + Vereinspartien
    }

    /// <summary>Wunsch 2026-09-28: „die letzten Partien sollen auch klickbar sein" — dieselbe Auswahl wie auf der Karte,
    /// samt PGN, fremde und Vereinspartien.</summary>
    [Fact]
    public async Task Recent_SameGamesAsTheCard_WithPgn()
    {
        var me = await SeedAsync();
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "222", Name = "Hengl, Philip", Pgn = ExternalSameGame, GameCount = 1 });
        await _db.SaveChangesAsync();
        await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Binder, Moriz",
            "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 5. e3 O-O 6. Nf3 h6 7. Bh4 b6 8. cxd5 Nxd5 9. Bxe7 Qxe7 10. Nxd5 exd5 1-0", "2025.03.01"), null);

        var card = JsonNode.Parse((await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == "222")).ProfileJson)!["recent"]!.AsArray();
        var recent = (await new LeagueProfileStore(_db).RecentAsync("222", default))!["games"]!.AsArray();
        Assert.Equal(card.Select(g => (g!["date"]!.GetValue<string>(), g["vs"]!.GetValue<string>(), g["color"]!.GetValue<string>())),
            recent.Select(g => (g!["date"]!.GetValue<string>(), g["vs"]!.GetValue<string>(), g["color"]!.GetValue<string>())));
        Assert.Equal(("2025.??.??", "Testdorf", "w"), (recent[0]!["date"]!.GetValue<string>(), recent[0]!["vs"]!.GetValue<string>(),
            recent[0]!["color"]!.GetValue<string>()));                                     // die Vereinspartie, neueste zuerst
        Assert.Contains("10. Nxd5 exd5", recent[0]!["pgn"]!.GetValue<string>());
        Assert.Contains("[Event \"TMM Landesliga\"]", recent[1]!["pgn"]!.GetValue<string>());
        Assert.Null(await new LeagueProfileStore(_db).RecentAsync("999999", default));
    }

    /// <summary>0.592.0: oben nach Farbe gefiltert → die letzten Partien NUR mit dieser Farbe (nicht die acht gemischten
    /// gefiltert), mit denselben Angaben wie die Karte.</summary>
    [Fact]
    public async Task Recent_ByColor_OnlyThatColor_WithTheCardsFields()
    {
        var me = await SeedAsync();
        _db.LeaguePlayerProfiles.Add(new LeaguePlayerProfile { FideId = "222", Name = "Hengl, Philip", Pgn = ExternalSameGame, GameCount = 1 });
        await _db.SaveChangesAsync();
        await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Binder, Moriz",
            "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 5. e3 O-O 6. Nf3 h6 7. Bh4 b6 8. cxd5 Nxd5 9. Bxe7 Qxe7 10. Nxd5 exd5 1-0", "2025.03.01"), null);
        var store = new LeagueProfileStore(_db);
        var all = (await store.RecentAsync("222", default))!["games"]!.AsArray();
        var white = (await store.RecentAsync("222", default, "w"))!["games"]!.AsArray();
        var black = (await store.RecentAsync("222", default, "s"))!["games"]!.AsArray();
        Assert.All(white, g => Assert.Equal("w", g!["color"]!.GetValue<string>()));
        Assert.All(black, g => Assert.Equal("s", g!["color"]!.GetValue<string>()));
        Assert.Equal(all.Count, white.Count + black.Count);
        Assert.Equal("1.d4 d5 2.c4 e6", white[0]!["opening"]!.GetValue<string>());          // Angaben der Karte dabei
        Assert.Equal(1.0, white[0]!["score"]!.GetValue<double>());
        Assert.Equal(all.Count, (await store.RecentAsync("222", default, "x"))!["games"]!.AsArray().Count);   // unbekannt = beide
    }

    [Fact]
    public async Task Refresh_MergingChessResults_KeepsTheClubGamesInTheCard()
    {
        var me = await SeedAsync();
        await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Schnabl, Andreas", "Oberschmid, Patrik", "1. d4 Nf6 2. c4 e6 1-0"), null);
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var refresh = new LeagueRefresh(_db, league, new NoClients(), NullLogger<LeagueRefresh>.Instance, () => Now);

        await refresh.MergeGamesAsync("333", ExternalSameGame.Replace("Oberschmid, Patrik", "Schnabl, Andreas"), default);

        var row = await _db.LeaguePlayerProfiles.AsNoTracking().SingleAsync(p => p.FideId == "333");
        Assert.Equal(2, row.GameCount);
        Assert.DoesNotContain("Testdorf", row.Pgn);
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
        await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Oberschmid, Patrik"), null);        // Vereinspartie zählt mit

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

    /// <summary>Codereview 2026-09-29, N4-003: jeder Klick im Eröffnungsbaum (auch anonym über einen Teilen-Link) zerlegte das
    /// ganze gespeicherte PGN der Karte neu, zweimal. Mit Cache wird je Karte und Stand (<c>UpdatedAt</c>) EINMAL gelesen und
    /// zerlegt — Baum, Profil, letzte Partien und Download bedienen sich daraus; ein neuer Stand liest neu, die
    /// Vereinspartien kommen weiter frisch dazu.</summary>
    [Fact]
    public async Task Card_StoredPgnReadOncePerVersion_FromTheCache_AndAgainAfterAnUpdate()
    {
        var me = await SeedAsync();
        await new LeagueProfileStore(_db).ImportGamesAsync(string.Join("\n",
            MegaGame("Hengl, Philip", "222", "A, A", null, "1. e4 c5 2. Nf3 d6", "2019.03.01"),
            MegaGame("Hengl, Philip", "222", "C, C", null, "1. d4 d5", "2021.03.01", "1/2-1/2")), "Mega", default);
        using var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = LeagueProfileStore.CacheSizeLimit });
        LeagueProfileStore Store() => new(_db, cache);                   // je Anfrage ein Store, der Cache bleibt
        static string Moves(JsonObject t) => string.Join(" ", t["moves"]!.AsArray().Select(m => $"{m!["san"]}:{m["n"]}").Order());

        Assert.Equal("d4:1 e4:1", Moves((await Store().TreeAsync("222", "w", null, default))!));
        Assert.Equal(1, cache.Count);

        // Das PGN ändert sich OHNE neuen Stand — kein Schreibweg tut das; hier zeigt es, dass nicht neu gelesen wird.
        var row = await _db.LeaguePlayerProfiles.SingleAsync(p => p.FideId == "222");
        var stored = row.Pgn;
        row.Pgn = MegaGame("Hengl, Philip", "222", "B, B", null, "1. c4 e5", "2024.03.01");
        await _db.SaveChangesAsync();
        Assert.Equal("d4:1 e4:1", Moves((await Store().TreeAsync("222", "w", null, default))!));
        Assert.Equal("c5:1", Moves((await Store().TreeAsync("222", "w", "e4", default))!));   // der nächste Klick: dieselben Partien
        Assert.Equal(2, (await Store().ProfileAsync("222", default))!["board"]!.GetValue<int>());
        Assert.Contains("1. d4 d5", (await Store().PgnAsync("222", default))!.Value.Pgn);
        Assert.Equal(2, (await Store().RecentAsync("222", default))!["games"]!.AsArray().Count);

        // Eine Vereinspartie (RefreshCardsAsync → RebuildAsync setzt UpdatedAt): neuer Stand, neu gelesen — samt Vereinspartie.
        row.Pgn = stored;
        await _db.SaveChangesAsync();
        await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Oberschmid, Patrik"), null);
        Assert.Equal("d4:1 e4:2", Moves((await Store().TreeAsync("222", "w", null, default))!));

        row.Pgn = MegaGame("Hengl, Philip", "222", "B, B", null, "1. c4 e5", "2024.03.01");
        row.UpdatedAt = row.UpdatedAt.AddSeconds(1);
        await _db.SaveChangesAsync();
        Assert.Equal("c4:1 e4:1", Moves((await Store().TreeAsync("222", "w", null, default))!));   // neues PGN + Vereinspartie
        Assert.Equal(2, (await Store().ProfileAsync("222", default))!["board"]!.GetValue<int>());
    }

    // ── Einzelne Partie, Liste, Löschen ─────────────────────────────

    [Fact]
    public async Task AddGame_ReplaceFlags_Fide_Year_AndIllegalMovesNamed()
    {
        var me = await SeedAsync();
        var club = Club();
        var (bad, reason, message) = await club.AddGameAsync(TestClubs.Home, me, new LeagueClubGameRequest { Moves = new() { "e4", "e5", "Ke3" }, White = "x", Black = "y" });
        Assert.Null(bad);
        Assert.Equal("illegal", reason);
        Assert.Contains("ply 3", message);

        var (game, r, _) = await club.AddGameAsync(TestClubs.Home, me, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "c5", "Nf3" }, White = "Didi", Black = "irgendwie falsch geschrieben", BlackFide = "222",
            WhiteReplace = true, Year = 2026, Result = "0-1", Event = "Simultan",
        });
        Assert.Null(r);
        Assert.Equal(("Testdorf", "Hengl, Philip", "222", 2026, "0-1"), (game!.White, game.Black, game.BlackFide, game.Year, game.Result));
        Assert.Null(game.Event);
        Assert.Contains("1. e4 c5 2. Nf3", game.Pgn);
        Assert.Equal("duplicate", (await club.AddGameAsync(TestClubs.Home, me, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "c5", "Nf3" }, White = "Didi", Black = "Hengl, Philip", Year = 2026, WhiteReplace = true,
        })).Reason);
        Assert.Equal("onlyOwnClub", (await club.AddGameAsync(TestClubs.Home, me, new LeagueClubGameRequest
        {
            Moves = new() { "d4" }, White = "Binder, Moriz", Black = "Hengl, Philip", WhiteReplace = true, BlackReplace = true,
        })).Reason);
    }

    /// <summary>Wunsch 2026-09-28: „auf /verein die Namen anpassen, Ergebnis soll auch anpassbar sein".</summary>
    [Fact]
    public async Task Update_NamesAndResult_RulesStay_SchwazStaysAnonymous_UnknownNameIsRemembered()
    {
        var me = await SeedAsync();
        var club = Club();
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Kinsiz, Atlas"), null);                    // Schwarz unbekannt
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Hengl, Philip", "1. d4 d5 2. c4 e6 3. Nc3 Nf6 4. Bg5 Be7 5. e3 O-O 6. Nf3 h6 7. Bh4 b6 8. cxd5 Nxd5 9. Bxe7 Qxe7 10. Nxd5 exd5 1-0"), null);
        var named = _db.LeagueClubGames.AsNoTracking().Single(g => !g.Anonymized);
        var anon = _db.LeagueClubGames.AsNoTracking().Single(g => g.Anonymized);
        Assert.Equal(("Kinsiz, Atlas", (string?)null), (named.Black, named.BlackFide));

        var (game, reason) = await club.UpdateAsync(TestClubs.Home, me, false, named.Id,
            new LeagueClubGameUpdateRequest { Black = new() { Fide = "333" }, Result = "0-1" });
        Assert.Null(reason);
        Assert.Equal(("Schnabl, Andreas Dr.", "333", "0-1"), (game!.Black, game.BlackFide, game.Result));
        Assert.Contains("[Black \"Schnabl, Andreas Dr.\"]", game.Pgn);
        Assert.Contains("[Result \"0-1\"]", game.Pgn);
        Assert.Equal(named.MovesHash, game.MovesHash);
        Assert.Equal(("kinsiz, atlas", "333"), (_db.LeagueNameAliases.Single().NameKey, _db.LeagueNameAliases.Single().Fide));
        Assert.Equal(1, _db.LeaguePlayerProfiles.Single(p => p.FideId == "333").GameCount);          // Karte nachgezogen

        Assert.Equal("anonymous", (await club.UpdateAsync(TestClubs.Home, me, true, anon.Id,
            new LeagueClubGameUpdateRequest { White = new() { Name = "Wer auch immer" } })).Reason);
        Assert.Equal("forbidden", (await club.UpdateAsync(TestClubs.Home, me, false, anon.Id, new LeagueClubGameUpdateRequest { Result = "*" })).Reason);
        Assert.Null((await club.UpdateAsync(TestClubs.Home, me + 1, true, anon.Id, new LeagueClubGameUpdateRequest { Result = "1/2-1/2" })).Reason);
        Assert.Equal("Testdorf", _db.LeagueClubGames.AsNoTracking().Single(g => g.Id == anon.Id).White);   // bleibt anonym
        Assert.Equal("forbidden", (await club.UpdateAsync(TestClubs.Home, me + 1, false, named.Id, new LeagueClubGameUpdateRequest { Result = "*" })).Reason);
        Assert.Equal("invalidResult", (await club.UpdateAsync(TestClubs.Home, me, false, named.Id, new LeagueClubGameUpdateRequest { Result = "2-0" })).Reason);
        Assert.Equal("noLeaguePlayer", (await club.UpdateAsync(TestClubs.Home, me, false, named.Id, new LeagueClubGameUpdateRequest
            { White = new() { Name = "Niemand, Bekannt" }, Black = new() { Name = "Auch, Niemand" } })).Reason);
        Assert.Equal("notFound", (await club.UpdateAsync(TestClubs.Home, me, true, 99999, new LeagueClubGameUpdateRequest())).Reason);
    }

    /// <summary>Gemeldet 2026-09-28: auf „Oberschmid, Patrik" korrigiert — „der soll dann natürlich auch durch Schwaz ersetzt
    /// werden". Und die Liste bringt die Züge als UCI für „Analyse" mit.</summary>
    [Fact]
    public async Task Update_ToASchwazMember_AnonymizesAndDropsTheUploader_ListCarriesUci()
    {
        var me = await SeedAsync();
        var club = Club();
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Unbekannt, Wer", "Hengl, Philip"), null);          // mit Namen: Hochladender gespeichert
        var g0 = _db.LeagueClubGames.AsNoTracking().Single();
        Assert.Equal((false, (int?)me, "Vereinsmeisterschaft"), (g0.Anonymized, g0.UploadedByUserId, g0.Event));

        var (game, reason) = await club.UpdateAsync(TestClubs.Home, me, false, g0.Id, new LeagueClubGameUpdateRequest { White = new() { Fide = "900" } });
        Assert.Null(reason);
        Assert.Equal(("Testdorf", (string?)null, (int?)null), (game!.White, game.WhiteFide, game.WhiteElo));
        Assert.True(game.Anonymized);
        Assert.Null(game.UploadedByUserId);
        Assert.Null(game.CreatedAt);
        Assert.Null(game.Event);
        Assert.DoesNotContain("Oberschmid", game.Pgn);
        Assert.DoesNotContain("Vereinsmeisterschaft", game.Pgn);
        Assert.Equal(("Oberschmid, Patrik", "900"), (game.WhiteRealName, game.WhiteRealFide));   // intern gemerkt
        var (again, _) = await club.UpdateAsync(TestClubs.Home, me, true, g0.Id, new LeagueClubGameUpdateRequest { Result = "0-1" });
        Assert.Equal("Oberschmid, Patrik", again!.WhiteRealName);                                // unveränderte Seite behält ihn

        var item = (await club.ListAsync(TestClubs.Home, me, true, null, null, 1, default)).Items.Single();
        Assert.StartsWith("e2e4 c7c5 g1f3 d7d6 d2d4 c5d4", item.Uci);
    }

    /// <summary>A9-011: <c>page=int.MaxValue</c> ergab Skip(-100) (MariaDB: 500, InMemory: erste Seite).</summary>
    [Fact]
    public async Task List_HugePage_ReturnsAnEmptyPageInsteadOfOverflowing()
    {
        var me = await SeedAsync();
        var club = Club();
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Hengl, Philip"), null);

        var list = await club.ListAsync(TestClubs.Home, me, true, null, null, int.MaxValue, default);

        Assert.Empty(list.Items);
        Assert.Equal(1, list.Total);
    }

    /// <summary>0.594.0: eine Seite ohne FIDE-ID, deren Name in einer Meldeliste steht (Ligaspieler ohne FIDE-ID wie Kinsiz,
    /// Atlas) — die Liste sagt es, damit die Seite dort keinen „bitte zuordnen"-Bleistift zeigt.</summary>
    [Fact]
    public async Task List_FlagsRosterPlayersWithoutFide_NotUnknownNamesNorSchwaz()
    {
        var me = await SeedAsync();
        Player(7, "Absam", "Kinsiz, Atlas", null);
        await _db.SaveChangesAsync();
        var club = Club();
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Kinsiz, Atlas"), null);          // Schwaz – Ligaspieler ohne ID
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Niemand, Kennt", "1. d4 d5 1-0"),
            [new() { Index = 1, Black = new() { Name = "Niemand, Kennt" } }]);                   // Name, den keiner kennt

        var items = (await club.ListAsync(TestClubs.Home, me, true, null, null, 1, default)).Items;
        var kinsiz = items.Single(i => i.Black == "Kinsiz, Atlas");
        Assert.Null(kinsiz.BlackFide);
        Assert.True(kinsiz.BlackInRoster);
        Assert.False(kinsiz.WhiteInRoster);                                                   // „Schwaz" zählt nicht
        var unknown = items.Single(i => i.Black == "Niemand, Kennt");
        Assert.False(unknown.BlackInRoster);
        Assert.False(unknown.WhiteInRoster);                                                  // Hengl hat eine FIDE-ID
    }

    [Fact]
    public async Task ListAndDelete_OwnNamedGames_AnonymousOnlyByManagers()
    {
        var me = await SeedAsync();
        var club = Club();
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Schnabl, Andreas"), null);
        await club.ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Schnabl, Andreas", "1. d4 d5 1-0"), null);

        var list = await club.ListAsync(TestClubs.Home, me, false, null, null, 1, default);
        Assert.Equal(2, list.Total);
        var named = list.Items.Single(i => !i.Anonymized);
        var anon = list.Items.Single(i => i.Anonymized);
        Assert.Equal("1.e4 c5 2.Nf3 d6 3.d4 cxd4", named.Opening);
        Assert.True(named.CanDelete);
        Assert.False(anon.CanDelete);
        Assert.Equal(2, (await club.ListAsync(TestClubs.Home, me, false, "333", null, 1, default)).Items.Count);
        Assert.Single((await club.ListAsync(TestClubs.Home, me, false, null, "Hengl", 1, default)).Items);

        Assert.Equal(LeagueClubService.DeleteResult.Forbidden, await club.DeleteAsync(TestClubs.Home, me + 1, false, named.Id));
        Assert.Equal(LeagueClubService.DeleteResult.Forbidden, await club.DeleteAsync(TestClubs.Home, me, false, anon.Id));
        Assert.Equal(LeagueClubService.DeleteResult.Deleted, await club.DeleteAsync(TestClubs.Home, me, false, named.Id));
        Assert.Equal(LeagueClubService.DeleteResult.Deleted, await club.DeleteAsync(TestClubs.Home, me + 1, true, anon.Id));
        Assert.Empty(_db.LeagueClubGames);
        Assert.Equal(0, _db.LeaguePlayerProfiles.Single(p => p.FideId == "333").GameCount);   // Karte ohne die Partie
    }

    [Fact]
    public async Task Export_IsAllGamesAsPgn()
    {
        var me = await SeedAsync();
        await Club().ImportPgnAsync(TestClubs.Home, me, Pgn("Oberschmid, Patrik", "Hengl, Philip") + Pgn("Oberschmid, Patrik", "Schnabl, Andreas", "1. d4 d5 1-0"), null);
        var pgn = await Club().ExportAsync(TestClubs.Home, null, null, default);
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
        Assert.Single(await club.SuggestAsync(TestClubs.Home, "hengl", false, default));
        var all = await club.SuggestAsync(TestClubs.Home, "hengl", true, default);
        Assert.Equal(new[] { ("Hengl, Philip", "liga", true), ("Hengl, Peter", "mega", false) },
            all.Select(p => (p.Name, p.Source, p.League)));                           // Philip nur einmal: als Ligaspieler
    }

    [Fact]
    public async Task Search_WordsAnywhereInTheName()
    {
        await SeedAsync();
        var mega = new LeagueMegaPlayers(_db);
        Assert.Equal(new[] { "Hengl, Philip", "Hengl, Peter" }, (await mega.SearchAsync("ngl", 10, default)).Select(p => p.Name));
        Assert.Equal(new[] { "Hengl, Peter" }, (await mega.SearchAsync("eng ete", 10, default)).Select(p => p.Name));
    }

    [Fact]
    public async Task Search_UmlautSpellings_FideIdNumber_AndTitles()
    {
        var mega = new LeagueMegaPlayers(_db);
        await mega.ReplaceAsync(new StringReader("Hoecher, Michael\t1271145\t83\t2025\t2157\nMueller, Hans\t\t5\t2001\t\n" +
            "Humer, Wolfgang\t1600370\t189\t2025\t2315\n"), default);
        Assert.Equal("Hoecher, Michael", (await mega.SearchAsync("Höcher", 10, default)).Single().Name);
        Assert.Equal("Mueller, Hans", (await mega.SearchAsync("Müller", 10, default)).Single().Name);
        Assert.Equal("Hoecher, Michael", (await mega.SearchAsync("michael", 10, default)).Single().Name);
        Assert.Equal("Hoecher, Michael", (await mega.SearchAsync("1271145", 10, default)).Single().Name);
        Assert.Equal("Humer, Wolfgang", (await mega.SearchAsync("FM Humer", 10, default)).Single().Name);
        var l = await mega.LookupAsync(new[] { "Höcher, Michael", "FM Humer, Wolfgang" }, Array.Empty<string?>(), default);
        Assert.Equal("1271145", l.ByName("Höcher, Michael")?.Fide);
        Assert.Equal("1600370", l.ByName("FM Humer, Wolfgang")?.Fide);
    }

    [Theory]
    [InlineData("Angerer, Helmut", new[] { "angerer, helmut" })]
    [InlineData("Angerer,Helmut", new[] { "angerer,helmut", "angerer, helmut" })]
    [InlineData("Helmut Angerer", new[] { "helmut angerer", "angerer, helmut", "helmut, angerer" })]
    [InlineData("Kostic", new[] { "kostic" })]
    [InlineData("Höcher, Michael", new[] { "hocher, michael", "hoecher, michael" })]
    [InlineData("FM Humer, Wolfgang", new[] { "humer, wolfgang" })]
    [InlineData("", new string[0])]
    public void LookupKeys_CommaFormAndBothOrdersWithoutComma(string name, string[] keys) =>
        Assert.Equal(keys, LeagueMegaPlayers.LookupKeys(name).Distinct().ToArray());

    private async Task<LeagueMegaPlayers.Lookup> LookupAsync(string tsv, params string[] names)
    {
        await new LeagueMegaPlayers(_db).ReplaceAsync(new StringReader(tsv), default);
        return await new LeagueMegaPlayers(_db).LookupAsync(names, Array.Empty<string?>(), default);
    }

    [Fact]
    public async Task Lookup_OnlyUnique_NamesakesWithTwoFideIdsAreNobody()
    {
        var l = await LookupAsync(
            "Angerer, Helmut\t1607162\t98\t2023\t2195\nAngerer, Helmut\t\t3\t1980\t\n" +   // Dublette ohne ID
            "Huber, Franz\t1\t10\t2020\t\nHuber, Franz\t2\t20\t2021\t\n" +                 // zwei mit ID
            "Campbell\t\t3\t1995\t1605\n",
            "Helmut Angerer", "Huber, Franz", "Campbell", "Niemand, Hier");
        Assert.Equal(new LeagueMegaPlayers.Hit("Angerer, Helmut", "1607162"), l.ByName("Helmut Angerer"));
        Assert.Null(l.ByName("Huber, Franz"));
        Assert.Equal(new LeagueMegaPlayers.Hit("Campbell", null), l.ByName("Campbell"));
        Assert.Null(l.ByName("Niemand, Hier"));
        Assert.Equal("Angerer, Helmut", l.ByFide("1607162")?.Name);         // mitgeladen über den Namen
        Assert.Null(l.ByFide("999"));
    }

    private const string Moves = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 1-0";

    private static string Game(string white, string black, string extra = "", string date = "2025.04.15") =>
        $"[Event \"Open\"]\n[Date \"{date}\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n{extra}\n{Moves}\n";

    /// <summary>Wunsch 2026-09-28: „standardmäßig auf Megabase matchen (wenn in Tirol kein Treffer) … nicht in Liga, und
    /// man kann die Partie trotzdem hinzufügen".</summary>
    [Fact]
    public async Task NotInTheLeague_ButInTheMegabase_IsKnown_AndCanBeImported()
    {
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 1, Team = "Testdorf", Name = "Oberschmid, Patrik", NameKey = "oberschmid, patrik", FideId = "900" });
        await _db.SaveChangesAsync();
        await new LeagueMegaPlayers(_db).ReplaceAsync(new StringReader(
            "Bodrov, Timofey\t14131781\t27\t2025\t2128\nSchett, Franz\t1611135\t189\t2019\t2029\n"), default);
        var club = new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance);
        var pgn = Game("Oberschmid, Patrik", "Bodrov, Timofey") + "\n" + Game("Oberschmid, Patrik", "Unbekannt, Wer") + "\n"
            + Game("Oberschmid, Patrik", "Irgendwer", "[BlackFideId \"1611135\"]", "2019.03.01");   // anderes Jahr: keine Dublette

        var preview = await club.PreviewAsync(TestClubs.Home, null, pgn);
        var b1 = preview.Games[0].Black.Match;
        Assert.Equal((false, true, "Bodrov, Timofey", "14131781"), (b1.League, b1.Mega, b1.Name, b1.Fide));
        Assert.False(preview.Games[1].Black.Match.Mega);
        Assert.Equal(("Schett, Franz", "1611135"), (preview.Games[2].Black.Match.Name, preview.Games[2].Black.Match.Fide));

        var decisions = new[] { 1, 2, 3 }.Select(i => new LeagueClubImportGameDecision
        {
            Index = i, White = new() { Fide = "900", Replace = true },
            Black = new() { Fide = preview.Games[i - 1].Black.Match.Fide },
        }).ToList();
        var result = await club.ImportPgnAsync(TestClubs.Home, null, pgn, decisions);
        Assert.Equal(2, result.Added);
        Assert.Equal(("onlyOwnClub", 2), (result.Failed.Single().Reason, result.Failed.Single().Index));   // nur Schwaz bleibt
        var stored = _db.LeagueClubGames.OrderBy(g => g.Id).ToList();
        Assert.Equal(new (string, string, string?)[] { ("Testdorf", "Bodrov, Timofey", "14131781"), ("Testdorf", "Schett, Franz", "1611135") },
            stored.Select(g => (g.White, g.Black, g.BlackFide)));

        // Ein getippter Name, den nur die Megabase kennt, geht auch — und der Abgleich sagt „nicht in Liga".
        var typed = await club.ImportPgnAsync(TestClubs.Home, null, Game("Oberschmid, Patrik", "X", date: "2021.01.01"),
            new[] { new LeagueClubImportGameDecision { Index = 1, White = new() { Replace = true }, Black = new() { Name = "Timofey Bodrov" } } });
        Assert.Equal(1, typed.Added);
        Assert.True((await club.MatchAsync(TestClubs.Home, "Timofey Bodrov", "Unbekannt", default)).White.Mega);
    }

    [Fact]
    public async Task NeitherLeagueNorMegabase_StaysUnknown()
    {
        var club = new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance);
        var (_, reason, _) = await club.AddGameAsync(TestClubs.Home, null, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "e5" }, White = "Nobody, Else", Black = "Someone, Other",
        });
        Assert.Equal("noLeaguePlayer", reason);
    }

    [Fact]
    public async Task Import_ChosenMegabasePlayer_KeepsItsFideId()
    {
        await SeedAsync();
        var club = new LeagueClubService(_db, NullLogger<LeagueClubService>.Instance);
        var (game, reason, _) = await club.AddGameAsync(TestClubs.Home, null, new LeagueClubGameRequest
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
