using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.Games;

/// <summary>Partienliste 0.954.0: Zusammenfassungs-Stand je Partie (eigene vor Vereinspartie, Vereinspartie nur mit Rechten),
/// dieselbe Regel auf der Partieseite, „Nur meine Partien“ (<see cref="SavedGameOwnership"/>) und das Turnier je Zeile
/// (<see cref="SavedGameEventLabel"/>).</summary>
public class SavedGameListExtrasTests : IDisposable
{
    private const int Me = 7, Member = 7, Stranger = 8, Admin = 9;
    private const int HomeGroup = 70;
    private const string Pgn = "[Event \"Tirol Open 2025\"]\n[Round \"5\"]\n[White \"A\"]\n[Black \"B\"]\n[Result \"1-0\"]\n\n1. e4 e5 2. Nf3 Nc6 1-0";
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public void Dispose() => _db.Dispose();

    private OwnGameSummaries Svc() => new(_db, null!, new ClubGameSummaries(_db), null!, null, null,
        new PermissionResolver(_db, TestServices.Cache()));

    /// <summary>Zwei Vereine; <see cref="Member"/> gehört zu „Testdorf“ (mit league.view), <see cref="Stranger"/> zu keinem.</summary>
    private async Task SeedClubsAsync(bool memberCanView = true)
    {
        await TestClubs.SeedAsync(_db);
        foreach (var (id, admin) in new[] { (Member, false), (Stranger, false), (Admin, true) })
            _db.AppUsers.Add(new AppUser { Id = id, Username = $"u{id}", Email = $"u{id}@t", PasswordHash = "x", IsAdmin = admin });
        _db.Groups.Add(new Group { Id = HomeGroup, Name = "Testdorf" });
        _db.LeagueClubMembers.Add(new LeagueClubMember { ClubId = TestClubs.HomeId, GroupId = HomeGroup });
        _db.UserGroups.Add(new UserGroup { UserId = Member, GroupId = HomeGroup });
        await _db.SaveChangesAsync();
        if (memberCanView)
        {
            var role = await new RoleAdminService(_db).CreateAsync(new CreateRoleDto { Key = "liga", Name = "Liga", Permissions = [Permissions.LeagueView] });
            await new RoleAdminService(_db).SetUserRolesAsync(Member, new SetUserRolesDto { RoleIds = [role.Id] });
        }
        PermissionResolver.InvalidateAll();
    }

    private LeagueClubGame ClubGame(int id, int club = TestClubs.HomeId, int? tnr = null, int? round = null, string? ev = null)
    {
        var c = new LeagueClubGame { Id = id, ClubId = club, White = "A", Black = "B", Pgn = Pgn, MovesHash = "h", LeagueTnr = tnr, LeagueRound = round, Event = ev };
        _db.LeagueClubGames.Add(c);
        _db.SaveChanges();
        return c;
    }

    private SavedGame Game(int user = Me, int? club = null, string source = "pgn", string? white = "A", string? black = "B",
        string? ownerSide = null, string pgn = Pgn)
    {
        var g = new SavedGame { UserId = user, Source = source, Pgn = pgn, White = white, Black = black, Result = "1-0", LeagueClubGameId = club,
            OwnerSide = ownerSide, MoveCount = 4, HeadersScanned = true, ShareToken = Guid.NewGuid().ToString("N") };
        _db.SavedGames.Add(g);
        _db.SaveChanges();
        return g;
    }

    private void OwnSummary(int gameId, ClubGameSummaryStatus status, string? text = null)
    {
        _db.OwnGameSummaries.Add(new OwnGameSummary { SavedGameId = gameId, UserId = Me, Status = status, Markdown = text, RequestedAt = DateTime.UtcNow });
        _db.SaveChanges();
    }

    private void ClubSummary(int clubGameId, ClubGameSummaryStatus status, string? text = null)
    {
        _db.ClubGameSummaries.Add(new ClubGameSummary { ClubGameId = clubGameId, Status = status, Markdown = text, RequestedAt = DateTime.UtcNow });
        _db.SaveChanges();
    }

    private Task<Dictionary<int, RookHub.Api.DTOs.Games.SavedGameSummaryStateDto>> States(int user, bool admin, params SavedGame[] games) =>
        Svc().ListStatesAsync(user, admin, games.Select(g => (g.Id, g.LeagueClubGameId)).ToList());

    // ── Zusammenfassung in der Liste ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ListStates_eigene_fertig_angefordert_laeuft_fehlgeschlagen_keine()
    {
        var done = Game(); var requested = Game(); var running = Game(); var failed = Game(); var none = Game();
        OwnSummary(done.Id, ClubGameSummaryStatus.Done, "# Text");
        OwnSummary(requested.Id, ClubGameSummaryStatus.Requested);
        OwnSummary(running.Id, ClubGameSummaryStatus.Running, "# alter Text");
        OwnSummary(failed.Id, ClubGameSummaryStatus.Failed);

        var s = await States(Me, false, done, requested, running, failed, none);

        Assert.Equal(("done", $"/games/{done.Id}/summary"), (s[done.Id].Status, s[done.Id].Link));
        Assert.Equal("requested", s[requested.Id].Status);
        Assert.Equal("running", s[running.Id].Status);      // offene Neuanforderung schlägt den alten Text
        Assert.False(s.ContainsKey(failed.Id));
        Assert.False(s.ContainsKey(none.Id));
    }

    [Fact]
    public async Task ListStates_verknuepfteVereinspartie_mitRechten_linktDorthin()
    {
        await SeedClubsAsync();
        ClubGame(38); ClubGame(39);
        var requested = Game(club: 38); var done = Game(club: 39);
        ClubSummary(38, ClubGameSummaryStatus.Requested);
        ClubSummary(39, ClubGameSummaryStatus.Done, "# Verein");

        var s = await States(Member, false, requested, done);

        Assert.Equal(("requested", "/club-games/38/summary"), (s[requested.Id].Status, s[requested.Id].Link));
        Assert.Equal(("done", "/club-games/39/summary"), (s[done.Id].Status, s[done.Id].Link));
    }

    [Fact]
    public async Task ListStates_beide_eigeneHatVorrang()
    {
        await SeedClubsAsync();
        ClubGame(38);
        var g = Game(club: 38);
        ClubSummary(38, ClubGameSummaryStatus.Done, "# Verein");
        OwnSummary(g.Id, ClubGameSummaryStatus.Requested);

        var s = await States(Member, false, g);

        Assert.Equal(("requested", $"/games/{g.Id}/summary"), (s[g.Id].Status, s[g.Id].Link));
    }

    [Fact]
    public async Task ListStates_fremderVerein_oderOhneLeagueView_nichts_AdminAlles()
    {
        await SeedClubsAsync(memberCanView: false);
        ClubGame(38); ClubGame(40, club: TestClubs.OtherId);
        var own = Game(club: 38); var foreign = Game(club: 40);
        ClubSummary(38, ClubGameSummaryStatus.Done, "# Verein");
        ClubSummary(40, ClubGameSummaryStatus.Requested);

        Assert.Empty(await States(Member, false, own, foreign));     // Mitglied, aber ohne league.view
        Assert.Empty(await States(Stranger, false, own, foreign));   // gar kein Verein
        var admin = await States(Admin, true, own, foreign);
        Assert.Equal("/club-games/40/summary", admin[foreign.Id].Link);
    }

    [Fact]
    public async Task ListStates_MitgliedSiehtNurSeinenVerein()
    {
        await SeedClubsAsync();
        ClubGame(40, club: TestClubs.OtherId);
        var foreign = Game(club: 40);
        ClubSummary(40, ClubGameSummaryStatus.Done, "# Weiler");

        Assert.Empty(await States(Member, false, foreign));
    }

    // ── Partieseite: dieselbe Vorrangregel ─────────────────────────────────────────────────────

    [Fact]
    public async Task Get_verknuepfteVereinspartieAngefordert_zeigtDerenStand_mitLink()
    {
        await SeedClubsAsync();
        ClubGame(38);
        var g = Game(club: 38);
        ClubSummary(38, ClubGameSummaryStatus.Requested);

        var dto = (await Svc().GetAsync(Member, g.Id, false))!;

        Assert.Equal(("requested", "club", 38), (dto.Status, dto.Source, dto.ClubGameId));
        Assert.NotNull(dto.Readiness);
        Assert.NotNull(dto.RunningSeconds);
    }

    [Fact]
    public async Task Get_eigeneVorVereinspartie_undFehlgeschlageneEigeneFaelltAufVereinZurueck()
    {
        await SeedClubsAsync();
        ClubGame(38);
        var g = Game(club: 38);
        ClubSummary(38, ClubGameSummaryStatus.Done, "# Verein");
        OwnSummary(g.Id, ClubGameSummaryStatus.Requested);
        Assert.Equal(("requested", "own"), ((await Svc().GetAsync(Member, g.Id, false))!.Status, (await Svc().GetAsync(Member, g.Id, false))!.Source));

        _db.OwnGameSummaries.Single().Status = ClubGameSummaryStatus.Failed;
        _db.SaveChanges();
        var dto = (await Svc().GetAsync(Member, g.Id, false))!;
        Assert.Equal(("done", "club", 38, "# Verein"), (dto.Status, dto.Source, dto.ClubGameId, dto.Markdown));
    }

    [Fact]
    public async Task Get_fremderVerein_angefordert_none_fertigerText_ohneLink()
    {
        await SeedClubsAsync();
        ClubGame(40, club: TestClubs.OtherId);
        var g = Game(club: 40);
        ClubSummary(40, ClubGameSummaryStatus.Requested);
        Assert.Equal("none", (await Svc().GetAsync(Member, g.Id, false))!.Status);

        var row = _db.ClubGameSummaries.Single();
        row.Status = ClubGameSummaryStatus.Done;
        row.Markdown = "# Weiler";
        _db.SaveChanges();
        var dto = (await Svc().GetAsync(Member, g.Id, false))!;
        // E7: der fertige Text bleibt sichtbar (die Verbindung entstand als Mitglied), aber ohne Link zur Vereinspartie.
        Assert.Equal(("done", "club", (int?)null), (dto.Status, dto.Source, dto.ClubGameId));
    }

    // ── „Nur meine Partien“ ────────────────────────────────────────────────────────────────────

    private static readonly SavedGameOwnership.Me Patrik = new("1693034", ["kahalm"], "Patrik", "Oberschmid");

    [Theory]
    [InlineData("Oberschmid, Patrik", true)]
    [InlineData("Patrik Oberschmid", true)]
    [InlineData("OBERSCHMID PATRIK", true)]
    [InlineData("Obercshmid, Patrik", true)]     // Vertauschung, Vorname stimmt
    [InlineData("Oberschmidt, Patrik", true)]    // ein Zeichen mehr
    [InlineData("Oberschmid", false)]            // nur Nachname
    [InlineData("Patrik", false)]                // nur Vorname
    [InlineData("Obrschmd, Patrik", false)]      // zwei Fehler
    [InlineData("Oberschmid, Peter", false)]
    [InlineData("kahalm", true)]
    [InlineData("KAHALM", true)]
    [InlineData("Rapid Chess • kahalm", true)]
    [InlineData("kahalm2", false)]
    [InlineData("taulajoe", false)]
    [InlineData(null, false)]
    public void IsMyName(string? name, bool expected) => Assert.Equal(expected, SavedGameOwnership.IsMyName(name, Patrik));

    [Fact]
    public void IsMyName_Umlaute_gleichgesetzt()
    {
        var me = new SavedGameOwnership.Me(null, [], "Jürgen", "Mühlbacher");
        Assert.True(SavedGameOwnership.IsMyName("Muehlbacher, Juergen", me));
        Assert.True(SavedGameOwnership.IsMyName("Muhlbacher Jurgen", me));
    }

    [Fact]
    public void IsOwn_EigeneSeite_FideDerVereinspartie_sonstNamen()
    {
        Assert.True(SavedGameOwnership.IsOwn(new("black", "X", "Y"), Patrik));
        Assert.True(SavedGameOwnership.IsOwn(new(null, "Dessl, Marco", "Schwaz", BlackRealFide: "1693034"), Patrik));
        Assert.True(SavedGameOwnership.IsOwn(new(null, "Treuer, Jan", "Oberschmid, Patrik"), Patrik));
        Assert.False(SavedGameOwnership.IsOwn(new(null, "Neuschmied, Siegfried", "Streiter, Gerhard", "1602446", null, null, "1621530"), Patrik));
        Assert.False(SavedGameOwnership.IsOwn(new(null, "Oberschmid, Patrik", "B"), SavedGameOwnership.Me.Nobody));
    }

    [Fact]
    public async Task ListUndZaehlung_mitMine_sehenDieselbeMenge()
    {
        _db.UserProfiles.Add(new UserProfile { UserId = Me, FideId = "1693034", ChessComUsername = "kahalm", FirstName = "Patrik", LastName = "Oberschmid" });
        _db.SaveChanges();
        ClubGame(50); ClubGame(51);
        var c = _db.LeagueClubGames.Find(50)!; c.BlackRealFide = "1693034"; _db.SaveChanges();
        var mine1 = Game(club: 50, white: "Dessl, Marco", black: "Schwaz");                // FIDE der Vereinspartie
        var other = Game(club: 51, white: "Gruber, Michael", black: "Pils, Martina");      // nur hochgeladen
        var mine2 = Game(source: "chess.com", white: "kahalm", black: "x");               // Online-Konto
        var watched = Game(source: "lichess", white: "taulajoe", black: "arapars");         // zugeschaut
        var mine3 = Game(white: "Q", black: "R", ownerSide: "white",                       // selbst festgelegt, ohne Turnier = nur „Alle“
            pgn: "[White \"Q\"]\n[Black \"R\"]\n\n1. e4 e5 1-0");
        _ = (other, watched);
        var svc = TestServices.SavedGames(_db);

        var all = await svc.ListAsync(Me, mine: true);
        Assert.Equal(new[] { mine1.Id, mine2.Id, mine3.Id }.OrderBy(i => i), all.Select(g => g.Id).OrderBy(i => i));
        Assert.Equal(new[] { mine2.Id }, (await svc.ListAsync(Me, kind: "online", mine: true)).Select(g => g.Id));
        Assert.Equal(new[] { mine1.Id }, (await svc.ListAsync(Me, kind: "otb", mine: true)).Select(g => g.Id));
        Assert.Equal(5, (await svc.ListAsync(Me)).Count);

        var counts = await svc.KindCountsAsync(Me, default, mine: true);
        Assert.Equal((3, 1, 1), (counts.All, counts.Online, counts.Otb));
        var unfiltered = await svc.KindCountsAsync(Me);
        Assert.Equal((5, 2, 2), (unfiltered.All, unfiltered.Online, unfiltered.Otb));
    }

    // ── Turnier je Zeile ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("TMM Landesliga 2025/2026", 5, "LL", null, "TMM Landesliga 2025/2026 · R5")]
    [InlineData(null, 5, "LL Tirol 26/27 Söll - Rum", null, "LL Tirol 26/27 Söll - Rum")]
    [InlineData(null, null, " ? ", "[Event \"Open\"]\n[Round \"3.1\"]\n", "Open · R3.1")]
    [InlineData(null, null, null, "[Event \"Open\"]\n[Round \"?\"]\n", "Open")]
    [InlineData(null, null, null, "[Event \"?\"]\n[Round \"3\"]\n", null)]
    [InlineData(null, null, null, "[Event \"-\"]\n", null)]
    [InlineData(null, null, null, "[White \"A\"]\n", null)]
    public void EventLabel(string? league, int? round, string? clubEvent, string? head, string? expected) =>
        Assert.Equal(expected, SavedGameEventLabel.Of(league, round, clubEvent, head));

    [Fact]
    public async Task List_TurnierAusLigaVereinspartieOderPgnKopf_nichtBeiOnline()
    {
        _db.LeagueTournaments.Add(new LeagueTournament { Tnr = 1206271, Name = "TMM Landesliga 2025/2026", Season = "2025/26", UpdatedAt = DateTime.UtcNow });
        _db.SaveChanges();
        ClubGame(60, tnr: 1206271, round: 5); ClubGame(61, ev: "LL Tirol 26/27");
        var league = Game(club: 60); var clubEvent = Game(club: 61); var pgn = Game();
        var online = Game(source: "lichess", pgn: Pgn);

        var list = (await TestServices.SavedGames(_db).ListAsync(Me)).ToDictionary(g => g.Id);

        Assert.Equal("TMM Landesliga 2025/2026 · R5", list[league.Id].Event);
        Assert.Equal("LL Tirol 26/27", list[clubEvent.Id].Event);
        Assert.Equal("Tirol Open 2025 · R5", list[pgn.Id].Event);
        Assert.Null(list[online.Id].Event);
        Assert.Equal(60, list[league.Id].ClubGameId);
    }

    /// <summary>InMemory rechnet alles in C#: die beiden neuen Abfrageformen müssen der ECHTE Provider übersetzen.</summary>
    [Fact]
    public void Abfragen_uebersetztDerEchteProvider()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql("server=localhost;database=x;user=x;password=x", DbServerVersion.Current).Options);
        var rows = SavedGameOwnership.RowsQuery(db, db.SavedGames.Where(g => g.UserId == 7)).ToQueryString();
        Assert.Contains("LEFT JOIN", rows);
        Assert.DoesNotContain("`Pgn`", rows);
        var head = db.SavedGames.Select(g => SavedGameKinds.OnlineSources.Contains(g.Source) ? null
            : g.Pgn.Length > SavedGameEventLabel.HeadChars ? g.Pgn.Substring(0, SavedGameEventLabel.HeadChars) : g.Pgn).ToQueryString();
        Assert.Contains("SUBSTRING", head);
    }
}
