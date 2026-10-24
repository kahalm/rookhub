using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>
/// Charakterisierung des Seiten-Abgleichs der Vereins-Datenbank (Zerlegung Z14): wer ist welche Seite — Meldeliste,
/// gemerkte Zuordnung, Megabase, der Hochladende — und was daraus als Zeile gespeichert würde. Läuft über die
/// öffentliche Oberfläche von <see cref="LeagueClubService"/>, damit dieselben Tests vor und nach dem Herausziehen
/// von <c>ClubSideResolver</c> gelten.
/// </summary>
public class ClubSideResolverTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private const string Moves = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 1-0";

    private static string Pgn(string white, string black, string extra = "", string date = "2024.05.12") =>
        $"[Event \"Open\"]\n[Date \"{date}\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n{extra}\n{Moves}\n";

    private void Player(string team, string name, string? fide) =>
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 7, Team = team, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });

    private void Alias(string raw, string? fide, string name) =>
        _db.LeagueNameAliases.Add(new LeagueNameAlias { NameKey = LeagueNameAliases.KeyOf(raw), Fide = fide, Name = name, UpdatedAt = Now });

    private async Task SeedAsync(string mega = "")
    {
        Player("Testdorf", "Oberschmid, Patrik", "900");
        Player("Absam", "Hengl, Philip", "222");
        Player("Absam", "Schnabl, Andreas Dr.", "333");
        await _db.SaveChangesAsync();
        if (mega.Length > 0) await new LeagueMegaPlayers(_db).ReplaceAsync(new StringReader(mega), default);
    }

    private async Task<int> UserAsync(string? fide, string? lastName, string? firstName = null)
    {
        var u = new AppUser { Username = "u" + Guid.NewGuid().ToString("N")[..6], Email = "u@test", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        _db.UserProfiles.Add(new UserProfile { UserId = u.Id, FideId = fide, LastName = lastName, FirstName = firstName });
        await _db.SaveChangesAsync();
        return u.Id;
    }

    // ── Wer ist das? (gemerkte Zuordnung, Megabase) ─────────────────

    [Fact]
    public async Task Alias_WithAFideOnTheRoster_IsThatLeaguePlayer()
    {
        await SeedAsync();
        Alias("Phil H.", "222", "Hengl, Philip");
        await _db.SaveChangesAsync();
        var m = (await Club().MatchAsync(TestClubs.Home, "Phil H.", null, default)).White;
        Assert.Equal((true, false, true, "Hengl, Philip", "222", false), (m.League, m.Mega, m.Alias, m.Name, m.Fide, m.LastNameOnly));
    }

    [Fact]
    public async Task Alias_WithAFideOnlyTheMegabaseKnows_IsTheMegabaseEntry()
    {
        await SeedAsync("Bodrov, Timofey\t14131781\t27\t2025\t2128\n");
        Alias("T. B.", "14131781", "Bodrov, T.");
        await _db.SaveChangesAsync();
        var m = (await Club().MatchAsync(TestClubs.Home, "T. B.", null, default)).White;
        Assert.Equal((false, true, true, "Bodrov, Timofey", "14131781"), (m.League, m.Mega, m.Alias, m.Name, m.Fide));
    }

    [Fact]
    public async Task Alias_WithAFideNobodyKnows_KeepsItsRememberedNameAndFide()
    {
        await SeedAsync();
        Alias("Gast", "4711", "Gast, Ein");
        await _db.SaveChangesAsync();
        var m = (await Club().MatchAsync(TestClubs.Home, "Gast", null, default)).White;
        Assert.Equal((false, true, true, "Gast, Ein", "4711"), (m.League, m.Mega, m.Alias, m.Name, m.Fide));
        Assert.Empty(m.Similar);                                                    // gemerkt = erkannt, keine Schnellauswahl
    }

    [Fact]
    public async Task Alias_WithoutFide_ByTheRememberedName_OnTheRosterOrNot()
    {
        await SeedAsync();
        Alias("Andi", null, "Schnabl, Andreas");          // eindeutig in der Meldeliste
        Alias("Niemand", null, "Keiner, Da");              // weder Liste noch Megabase
        await _db.SaveChangesAsync();
        var r = await Club().MatchAsync(TestClubs.Home, "Andi", "Niemand", default);
        Assert.Equal((true, true, "333", false), (r.White.League, r.White.Alias, r.White.Fide, r.White.LastNameOnly));
        Assert.Equal((false, true, true, "Keiner, Da", (string?)null), (r.Black.League, r.Black.Mega, r.Black.Alias, r.Black.Name, r.Black.Fide));
    }

    [Fact]
    public async Task GameFideOnTheRoster_BeatsTheAlias()
    {
        await SeedAsync();
        Alias("Andi", null, "Schnabl, Andreas");
        await _db.SaveChangesAsync();
        var p = await Club().PreviewAsync(TestClubs.Home, null, Pgn("Hengl, Philip", "Andi", "[BlackFideId \"900\"]"));
        var b = p.Games[0].Black;
        Assert.Equal((true, false, "900", true), (b.Match.League, b.Match.Alias, b.Match.Fide, b.Match.Club));
        Assert.True(b.Replace);                                                     // einer von uns → ersetzen
    }

    [Fact]
    public async Task Megabase_AFideItDoesNotKnow_KeepsTheNameOnlyWithoutContradiction()
    {
        await SeedAsync("Bodrov, Timofey\t14131781\t27\t2025\t2128\nCampbell\t\t3\t1995\t1605\n");
        var p = await Club().PreviewAsync(TestClubs.Home, null, string.Join("\n",
            Pgn("Hengl, Philip", "Bodrov, Timofey", "[BlackFideId \"999\"]"),        // Name sagt 14131781 → Widerspruch
            Pgn("Hengl, Philip", "Campbell", "[BlackFideId \"555\"]", "2023.01.01"))); // Name ohne ID → die ID der Partie gilt
        Assert.Equal((false, false), (p.Games[0].Black.Match.Mega, p.Games[0].Black.Match.League));
        Assert.Equal((true, "Campbell", "555"), (p.Games[1].Black.Match.Mega, p.Games[1].Black.Match.Name, p.Games[1].Black.Match.Fide));
    }

    // ── Wer lädt hoch? ──────────────────────────────────────────────

    [Fact]
    public async Task Owner_FideOfTheProfile_PicksTheSide_BeforeTheName()
    {
        await SeedAsync("Bodrov, Timofey\t14131781\t27\t2025\t2128\n");
        var me = await UserAsync("14131781", "Hengl");                              // Name passt zu Weiß, die ID zu Schwarz
        var p = await Club().PreviewAsync(TestClubs.Home, me, Pgn("Hengl, Philip", "Bodrov, Timofey"));
        Assert.Equal((false, true), (p.Games[0].White.Owner, p.Games[0].Black.Owner));
        Assert.Equal((false, true), (p.Games[0].White.Replace, p.Games[0].Black.Replace));
    }

    [Fact]
    public async Task Owner_ByName_WithoutFide_AndNobodyWithoutAnAccount()
    {
        await SeedAsync();
        var me = await UserAsync(null, "Schnabl", "Andreas");
        var pgn = Pgn("Hengl, Philip", "Schnabl, Andreas");
        var mine = await Club().PreviewAsync(TestClubs.Home, me, pgn);
        Assert.Equal((false, true, true), (mine.Games[0].White.Owner, mine.Games[0].Black.Owner, mine.Games[0].Black.Replace));
        var anon = await Club().PreviewAsync(TestClubs.Home, null, pgn);
        Assert.Equal((false, false, false), (anon.Games[0].White.Owner, anon.Games[0].Black.Owner, anon.Games[0].Black.Replace));
    }

    // ── Was gespeichert würde ───────────────────────────────────────

    [Fact]
    public async Task Build_EloOutOfRangeDropped_UnknownResultIsStar_QuestionMarkEventIsNone()
    {
        await SeedAsync();
        var (g, reason, _) = await Club().AddGameAsync(TestClubs.Home, null, new LeagueClubGameRequest
        {
            Moves = new() { "e4", "e5", "Nf3" }, White = "Hengl, Philip", Black = "Schnabl, Andreas",
            WhiteElo = 3500, BlackElo = 1999, Result = "2-0", Event = "?", Year = 2025,
        });
        Assert.Null(reason);
        Assert.Equal(((int?)null, (int?)1999, "*", (string?)null, false), (g!.WhiteElo, g.BlackElo, g.Result, g.Event, g.Anonymized));
        Assert.Equal(("Hengl, Philip", "Schnabl, Andreas Dr.", "222", "333"), (g.White, g.Black, g.WhiteFide, g.BlackFide));
        Assert.Equal((null, null, null, null), (g.WhiteRealName, g.WhiteRealFide, g.BlackRealName, g.BlackRealFide));
        Assert.Contains("[Date \"2025.??.??\"]", g.Pgn);
        Assert.Contains("[BlackElo \"1999\"]", g.Pgn);
        Assert.DoesNotContain("WhiteElo", g.Pgn);
    }

    [Fact]
    public async Task Build_ReplacedUnknownName_KeepsNoHiddenName_ReplacedLeaguePlayerDoes()
    {
        await SeedAsync();
        var (g, reason, _) = await Club().AddGameAsync(TestClubs.Home, null, new LeagueClubGameRequest
        {
            Moves = new() { "d4", "d5" }, White = "Testdorf", Black = "Hengl, Philip", WhiteReplace = true, WhiteElo = 2000, Event = "Liga",
        });
        Assert.Null(reason);
        Assert.Equal(("Testdorf", (string?)null, (int?)null, true, (string?)null), (g!.White, g.WhiteFide, g.WhiteElo, g.Anonymized, g.Event));
        Assert.Equal(((string?)null, (string?)null), (g.WhiteRealName, g.WhiteRealFide));      // „Testdorf" = niemand dahinter

        var (g2, r2, _) = await Club().AddGameAsync(TestClubs.Home, null, new LeagueClubGameRequest
        {
            Moves = new() { "c4", "e5" }, White = "irgendwas", WhiteFide = "900", Black = "Hengl, Philip", WhiteReplace = true,
        });
        Assert.Null(r2);
        Assert.Equal(("Testdorf", "Oberschmid, Patrik", "900"), (g2!.White, g2.WhiteRealName, g2.WhiteRealFide));
    }

    [Fact]
    public async Task Decided_AChosenRosterFide_BeatsTheTypedName_AndAMegaFideStaysOnTheGame()
    {
        await SeedAsync("Bodrov, Timofey\t14131781\t27\t2025\t2128\n");
        var pgn = Pgn("Hengl, Philip", "Wer auch immer");
        var r = await Club().ImportPgnAsync(TestClubs.Home, null, pgn, new[]
        {
            new LeagueClubImportGameDecision { Index = 1, White = new() { Name = "Falsch, Getippt", Fide = "333" }, Black = new() { Fide = "14131781" } },
        });
        Assert.Equal(1, r.Added);
        var g = await _db.LeagueClubGames.SingleAsync();
        Assert.Equal(("Schnabl, Andreas Dr.", "333", "Bodrov, Timofey", "14131781"), (g.White, g.WhiteFide, g.Black, g.BlackFide));
    }
}
