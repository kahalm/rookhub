using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Authorization;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.League;
using RookHub.Api.Services.Tactics;
using Uploader = RookHub.Api.Services.League.LeagueBatchUploadService.Uploader;

namespace RookHub.Api.Tests;

/// <summary>
/// LeagueHub für mehrere Vereine (Mandanten-Schritt 2026-10-07): welcher Verein eine Anfrage hat (<see cref="LeagueClubResolver"/>),
/// und dass ein Verein NIE Vereinspartien, Entwürfe, Formulare oder Uploads eines anderen sieht. Die öffentlichen Liga-Daten bleiben
/// geteilt. Testvereine: <see cref="TestClubs.Home"/> („Testdorf", Tirol) und <see cref="TestClubs.Other"/> („SK Weiler", Bayern).
/// </summary>
public class LeagueClubTenancyTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);
    public void Dispose() => _db.Dispose();

    private const int Admin = 1, HomeMember = 2, OtherMember = 3, BothMember = 4, Nobody = 5, AdminInHome = 6;
    private const int HomeGroup = 10, OtherGroup = 11;

    /// <summary>Beide Vereine, je eine Gruppe, Konten: Admin (ohne Gruppe), Mitglied je Verein, eins in beiden, eins ohne, ein Admin
    /// in der Gruppe des ersten Vereins.</summary>
    private async Task SeedAsync()
    {
        await TestClubs.SeedAsync(_db);
        _db.Groups.AddRange(new Group { Id = HomeGroup, Name = "Testdorf" }, new Group { Id = OtherGroup, Name = "Weiler" });
        _db.LeagueClubMembers.AddRange(new LeagueClubMember { ClubId = TestClubs.HomeId, GroupId = HomeGroup },
            new LeagueClubMember { ClubId = TestClubs.OtherId, GroupId = OtherGroup });
        foreach (var (id, admin) in new[] { (Admin, true), (HomeMember, false), (OtherMember, false), (BothMember, false), (Nobody, false), (AdminInHome, true) })
            _db.AppUsers.Add(new AppUser { Id = id, Username = $"u{id}", Email = $"u{id}@t", PasswordHash = "x", IsAdmin = admin });
        _db.UserGroups.AddRange(new UserGroup { UserId = HomeMember, GroupId = HomeGroup }, new UserGroup { UserId = OtherMember, GroupId = OtherGroup },
            new UserGroup { UserId = BothMember, GroupId = HomeGroup }, new UserGroup { UserId = BothMember, GroupId = OtherGroup },
            new UserGroup { UserId = AdminInHome, GroupId = HomeGroup });
        // die Meldelisten: „Testdorf" in Tirol, „SK Weiler 1" in Bayern, dazu je ein Gegner
        void Player(int tnr, string team, string name, string fide) =>
            _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = tnr, Team = team, Name = name, NameKey = LeagueNames.NameKey(name), FideId = fide });
        Player(1, "Testdorf", "Heimer, Hans", "100");
        Player(1, "Absam", "Gast, Gustav", "200");
        Player(900_000_001, "SK Weiler 1", "Bayer, Benno", "300");
        Player(900_000_001, "SC Bad Reichenhall 1", "Reich, Rudi", "400");
        _db.LeagueTournaments.AddRange(
            new LeagueTournament { Tnr = 1, Season = "2026/27", Level = 1, League = "Landesliga", Stage = "Liga" },
            new LeagueTournament { Tnr = 900_000_001, Season = "2026/27", Level = 3, League = "Landesliga Süd", Stage = "Liga",
                Source = LigamanagerSource.Source });
        await _db.SaveChangesAsync();
    }

    private LeagueClubResolver Resolver() => new(_db);
    private LeagueClubService Club() => new(_db, NullLogger<LeagueClubService>.Instance, () => Now);

    private const string Moves = "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Be3 e5 7. Nb3 Be6 8. f3 Be7 9. Qd2 O-O 10. O-O-O Nbd7 11. g4 b5 1-0";
    private static string Pgn(string white, string black, int year = 2025) =>
        $"[Event \"Liga\"]\n[Date \"{year}.10.04\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n[Result \"1-0\"]\n\n{Moves}\n";

    // ── Auflösung des Vereins ───────────────────────────────────────

    [Fact]
    public async Task Resolve_OneClub_NoParameterNeeded()
    {
        await SeedAsync();
        var r = await Resolver().ResolveAsync(HomeMember, false, null);
        Assert.Equal(TestClubs.HomeId, r.Club?.Id);
        Assert.Equal(TestClubs.HomeId, (await Resolver().ResolveAsync(HomeMember, false, TestClubs.HomeId)).Club?.Id);
    }

    [Fact]
    public async Task Resolve_SeveralClubs_NeedsTheParameter_OrThePreferredOne()
    {
        await SeedAsync();
        var r = await Resolver().ResolveAsync(BothMember, false, null);
        Assert.Equal((400, "clubRequired"), (r.Status, r.Reason));
        Assert.Equal(TestClubs.OtherId, (await Resolver().ResolveAsync(BothMember, false, TestClubs.OtherId)).Club?.Id);
        Assert.Equal(TestClubs.OtherId, (await Resolver().ResolveAsync(BothMember, false, null, default, preferred: TestClubs.OtherId)).Club?.Id);
    }

    [Fact]
    public async Task Resolve_ForeignClub_Is403_UnknownToo_ButAdminsSeeEveryClub()
    {
        await SeedAsync();
        Assert.Equal((403, "forbidden"), Pair(await Resolver().ResolveAsync(HomeMember, false, TestClubs.OtherId)));
        Assert.Equal((403, "forbidden"), Pair(await Resolver().ResolveAsync(HomeMember, false, 99)));
        // ein fremder Verein als „preferred" hilft nicht: es bleibt der eigene
        Assert.Equal(TestClubs.HomeId, (await Resolver().ResolveAsync(HomeMember, false, null, default, preferred: TestClubs.OtherId)).Club?.Id);
        Assert.Equal((403, "noClub"), Pair(await Resolver().ResolveAsync(Nobody, false, null)));
        Assert.Equal(TestClubs.OtherId, (await Resolver().ResolveAsync(Admin, true, TestClubs.OtherId)).Club?.Id);
        Assert.Equal((404, "unknownClub"), Pair(await Resolver().ResolveAsync(Admin, true, 99)));
        // Admin ohne Parameter: der Verein SEINER Gruppe, wenn es genau einer ist — sonst muss er wählen
        Assert.Equal(TestClubs.HomeId, (await Resolver().ResolveAsync(AdminInHome, true, null)).Club?.Id);
        Assert.Equal((400, "clubRequired"), Pair(await Resolver().ResolveAsync(Admin, true, null)));

        static (int, string?) Pair(LeagueClubResolver.Resolution r) => (r.Status, r.Reason);
    }

    [Fact]
    public async Task Me_ListsTheClubsOfTheAccount()
    {
        await SeedAsync();
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        async Task<JsonObject> MeAsync(int user, bool admin = false) =>
            (JsonObject)((OkObjectResult)await new LeagueController(league, null!, null!, Resolver()).As(user, admin).Me(default)).Value!;

        var member = await MeAsync(HomeMember);
        Assert.Equal(new[] { "SK Testdorf" }, member["clubs"]!.AsArray().Select(c => c!["name"]!.GetValue<string>()));
        Assert.Equal(TestClubs.HomeId, member["current"]!.GetValue<int>());
        Assert.Equal("Testdorf", member["clubs"]![0]!["anonName"]!.GetValue<string>());
        var both = await MeAsync(BothMember);
        Assert.Equal(2, both["clubs"]!.AsArray().Count);
        Assert.Null(both["current"]);
        Assert.Empty((await MeAsync(Nobody))["clubs"]!.AsArray());
        Assert.Equal(2, (await MeAsync(Admin, admin: true))["clubs"]!.AsArray().Count);
    }

    [Fact]
    public async Task Controller_ForeignClubParameter_Is403_NoParameterWithTwoClubs_Is400()
    {
        await SeedAsync();
        LeagueClubController Ctl(int user, int? club) =>
            new LeagueClubController(Club(), null!, new ScoresheetScanSignal(), Resolver()).As(user, club: club);

        Assert.Equal(403, Assert.IsType<ObjectResult>(await Ctl(HomeMember, TestClubs.OtherId).List(null, null)).StatusCode);
        Assert.Equal(400, Assert.IsType<ObjectResult>(await Ctl(BothMember, null).List(null, null)).StatusCode);
        Assert.IsType<OkObjectResult>(await Ctl(BothMember, TestClubs.OtherId).List(null, null));
        Assert.IsType<OkObjectResult>(await Ctl(HomeMember, null).List(null, null));
    }

    // ── Trennung der Vereins-Datenbank ──────────────────────────────

    [Fact]
    public async Task ClubDatabase_EachClubSeesOnlyItsOwnGames()
    {
        await SeedAsync();
        var club = Club();
        var home = TestClubs.Home;
        var other = TestClubs.Other;
        var h = await club.ImportPgnAsync(home, HomeMember, Pgn("Gast, Gustav", "Reich, Rudi"), null);
        // dieselbe Partie im anderen Verein ist KEINE Dublette — jeder Verein hat seine eigene Datenbank
        var o = await club.ImportPgnAsync(other, OtherMember, Pgn("Gast, Gustav", "Reich, Rudi"), null);
        Assert.Equal((1, 1, 0), (h.Added, o.Added, o.Duplicates));
        var homeId = h.Ids.Single();
        var otherId = o.Ids.Single();

        Assert.Equal(new[] { homeId }, (await club.ListAsync(home, HomeMember, false, null, null, 1, default)).Items.Select(i => i.Id));
        Assert.Equal(new[] { otherId }, (await club.ListAsync(other, OtherMember, false, null, null, 1, default)).Items.Select(i => i.Id));
        // über die Id eines fremden Vereins: nicht gefunden — lesen, Bewertungen, Export, ändern, Züge, löschen, Paarungen
        Assert.Null(await club.GetAsync(home, Admin, true, otherId));
        Assert.Null(await club.EvalsAsync(home, otherId));
        Assert.DoesNotContain("Weiler", await club.ExportAsync(home, null, null, default));
        Assert.Single(PgnParser.SplitGames(await club.ExportAsync(home, null, null, default)));
        Assert.Equal("notFound", (await club.UpdateAsync(home, Admin, true, otherId, new LeagueClubGameUpdateRequest { Result = "0-1" })).Reason);
        Assert.Equal("notFound", (await club.CorrectMovesAsync(home, Admin, true, otherId, new[] { "e4" })).Reason);
        Assert.False(await club.CanCorrectAsync(home, Admin, true, otherId));
        Assert.Null(await club.PairingsForGameAsync(home, Admin, true, otherId, default));
        Assert.Equal(LeagueClubService.DeleteResult.NotFound, await club.DeleteAsync(home, Admin, true, otherId));
        Assert.Equal(2, await _db.LeagueClubGames.CountAsync());
        Assert.Equal(TestClubs.OtherId, await club.ClubOfGameAsync(otherId));
    }

    [Fact]
    public async Task Anonymization_UsesTheNameOfTheClub_AndOnlyItsOwnPlayers()
    {
        await SeedAsync();
        var club = Club();
        // Bayer spielt für SK Weiler 1 → im Verein „SK Weiler" wird er zu „Weiler"; in „Testdorf" ist er ein Gegner
        var o = await club.ImportPgnAsync(TestClubs.Other, OtherMember, Pgn("Bayer, Benno", "Reich, Rudi"), null);
        var t = await club.ImportPgnAsync(TestClubs.Home, HomeMember, Pgn("Bayer, Benno", "Heimer, Hans", 2024), null);
        var og = await _db.LeagueClubGames.AsNoTracking().SingleAsync(g => g.Id == o.Ids[0]);
        var tg = await _db.LeagueClubGames.AsNoTracking().SingleAsync(g => g.Id == t.Ids[0]);
        Assert.Equal(("Weiler", "Reich, Rudi", true), (og.White, og.Black, og.Anonymized));
        Assert.Equal("300", og.WhiteRealFide);   // intern, nie ausgegeben
        Assert.Equal(("Bayer, Benno", "Testdorf"), (tg.White, tg.Black));
        Assert.DoesNotContain("Bayer", (await club.GetAsync(TestClubs.Other, OtherMember, false, og.Id))!.Pgn);
        // die Vorschläge wissen, wer „einer von uns" ist — je Verein
        Assert.True((await club.MatchAsync(TestClubs.Other, "Bayer, Benno", null, default)).White.Club);
        Assert.False((await club.MatchAsync(TestClubs.Home, "Bayer, Benno", null, default)).White.Club);
    }

    [Fact]
    public async Task Drafts_Scans_Batches_AreSeparatedByClub()
    {
        await SeedAsync();
        var drafts = new LeagueClubDraftService(_db, () => Now);
        var (d, _) = await drafts.CreateAsync(TestClubs.OtherId, OtherMember, null, Pgn("A", "B"), "datei", "x.pgn");
        Assert.Empty(await drafts.ListAllAsync(TestClubs.HomeId, Admin));
        Assert.Single(await drafts.ListAllAsync(TestClubs.OtherId, Admin));
        Assert.Null(await drafts.GetAsync(DraftActor.User(Admin, true, TestClubs.HomeId), d!.Id));
        Assert.False(await drafts.DeleteAsync(DraftActor.User(Admin, true, TestClubs.HomeId), d.Id));
        Assert.NotNull(await drafts.GetAsync(DraftActor.User(OtherMember, false, TestClubs.OtherId), d.Id));

        _db.ScoresheetScans.AddRange(
            new ScoresheetScan { Id = 31, UserId = HomeMember, Purpose = ScoresheetScan.PurposeLeague, ClubId = TestClubs.HomeId, ContentType = "image/jpeg",
                FileName = "a.jpg", CreatedAt = Now },
            new ScoresheetScan { Id = 32, UserId = OtherMember, Purpose = ScoresheetScan.PurposeLeague, ClubId = TestClubs.OtherId, ContentType = "image/jpeg",
                FileName = "b.jpg", CreatedAt = Now });
        await _db.SaveChangesAsync();
        var scans = new ScoresheetScanService(_db, null!, TestServices.SavedGames(_db), new NotificationService(_db),
            NullLogger<ScoresheetScanService>.Instance);
        Assert.Equal(new[] { 31 }, (await scans.LeagueOpenScansAsync(Admin, TestClubs.HomeId)).Select(s => s.Scan.Id));
        Assert.Equal(new[] { 32 }, (await scans.LeagueOpenScansAsync(Admin, TestClubs.OtherId)).Select(s => s.Scan.Id));
        var managerOfHome = ScoresheetScanService.ScanActor.ManagerOf(Admin) with { ClubId = TestClubs.HomeId };
        Assert.Null(await scans.LeagueScanStateAsync(managerOfHome, 32));
        Assert.False(await scans.CloseLeagueScanAsync(managerOfHome, 32));

        var notifications = new NotificationService(_db);
        var batches = new LeagueBatchUploadService(_db, new AdminMessageService(_db, notifications), notifications);
        var b = await batches.StartAsync(Uploader.User(OtherMember, TestClubs.OtherId), null, default);
        // derselbe Schlüssel, aber im anderen Verein: „nicht gefunden"
        Assert.Equal("notFound", (await batches.AddFileAsync(Uploader.User(OtherMember, TestClubs.HomeId), b.Key, new byte[] { 1 }, "image/jpeg", "x.jpg", default)).Reason);
        Assert.NotNull((await batches.AddFileAsync(Uploader.User(OtherMember, TestClubs.OtherId), b.Key, new byte[] { 1 }, "image/jpeg", "x.jpg", default)).State);
        Assert.Equal("SK Weiler", (await batches.ListAsync(default)).Single().Club);
    }

    [Fact]
    public async Task ShareLink_BelongsToItsClub_UploadsLandThere_AndTheViewNamesTheClub()
    {
        await SeedAsync();
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        _db.LeagueShares.Add(new LeagueShare { Token = "weilerlinkweilerlink1234", ClubId = TestClubs.OtherId, Tnr = 900_000_001, Round = 1,
            Team = "SK Weiler 1", Expires = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), CreatedAt = Now });
        await _db.SaveChangesAsync();
        var link = await league.ShareContextAsync("weilerlinkweilerlink1234", default);
        Assert.Equal(TestClubs.OtherId, link?.Club.Id);
        var r = await Club().ImportViaShareAsync(link!.Value.Club, link.Value.Token, Pgn("Bayer, Benno", "Reich, Rudi"), null);
        var g = await _db.LeagueClubGames.AsNoTracking().SingleAsync(x => x.Id == r.Ids[0]);
        Assert.Equal((TestClubs.OtherId, "Weiler"), (g.ClubId, g.White));
        // ein Verwalter des ANDEREN Vereins entfernt mit diesem Link nichts
        Assert.Equal(0, await Club().DeleteByShareAsync(TestClubs.Home, "weilerlinkweilerlink1234", dryRun: true));
        Assert.Equal(1, await Club().DeleteByShareAsync(TestClubs.Other, "weilerlinkweilerlink1234", dryRun: true));
        Assert.False(await league.DeleteShareAsync(TestClubs.Home, "weilerlinkweilerlink1234", default));
    }

    // ── Startseite, Paarungen, Quellen ──────────────────────────────

    [Fact]
    public async Task Index_ShowsOnlyLeaguesOfTheClubsSource_AndTheClub()
    {
        await SeedAsync();
        _db.LeagueViews.AddRange(new LeagueView { Tnr = 1, Json = "{}", GeneratedAt = Now }, new LeagueView { Tnr = 900_000_001, Json = "{}", GeneratedAt = Now });
        await _db.SaveChangesAsync();
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var tirol = await league.IndexAsync(TestClubs.Home, default);
        var bayern = await league.IndexAsync(TestClubs.Other, default);
        Assert.Equal(new[] { 1 }, tirol["leagues"]!.AsArray().Select(l => l!["tnr"]!.GetValue<int>()));
        Assert.Equal(new[] { 900_000_001 }, bayern["leagues"]!.AsArray().Select(l => l!["tnr"]!.GetValue<int>()));
        Assert.Equal(("SK Weiler", "SK Weiler", "Weiler"), (bayern["club"]!["name"]!.GetValue<string>(),
            bayern["club"]!["teamPrefix"]!.GetValue<string>(), bayern["club"]!["anonName"]!.GetValue<string>()));
    }

    [Fact]
    public async Task Index_BavarianClubSeesLigamanagerAndZugspitze_TyroleanClubNeither()
    {
        // Region statt Quelle (Schachkreis Zugspitze, 2026-10-07): SK Weiler spielt mit der Ersten im Ligamanager, mit der Zweiten
        // im Schachkreis — beide Ligen auf SEINER Startseite, keine auf der von Testdorf; die Vorsaison des Kreises zählt nicht.
        await SeedAsync();
        var zg = ZugspitzeSource.TnrOf(2026, 1);
        var zgOld = ZugspitzeSource.TnrOf(2025, 1);
        _db.LeagueTournaments.AddRange(
            new LeagueTournament { Tnr = zg, Season = "2026/27", Level = 5, League = "Zugspitzliga", Stage = "Liga", Source = ZugspitzeSource.Source },
            new LeagueTournament { Tnr = zgOld, Season = "2025/26", Level = 5, League = "Zugspitzliga", Stage = "Liga", Source = ZugspitzeSource.Source });
        _db.LeagueViews.AddRange(new LeagueView { Tnr = 1, Json = "{}", GeneratedAt = Now }, new LeagueView { Tnr = 900_000_001, Json = "{}", GeneratedAt = Now },
            new LeagueView { Tnr = zg, Json = "{}", GeneratedAt = Now }, new LeagueView { Tnr = zgOld, Json = "{}", GeneratedAt = Now });
        // die Zweite im Spielplan des Kreises (seit 0.710.0 zeigt die Startseite nur Ligen mit eigener Mannschaft)
        _db.LeagueMatches.Add(new LeagueMatch { Tnr = zg, Round = 1, Home = "SK Weiler 2", Away = "SC Garmisch 1" });
        await _db.SaveChangesAsync();
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var tirol = await league.IndexAsync(TestClubs.Home, default);
        var bayern = await league.IndexAsync(TestClubs.Other, default);
        Assert.Equal(new[] { 1 }, tirol["leagues"]!.AsArray().Select(l => l!["tnr"]!.GetValue<int>()));
        Assert.Equal(new[] { 900_000_001, zg }, bayern["leagues"]!.AsArray().Select(l => l!["tnr"]!.GetValue<int>()));   // nach Stufe
        Assert.Equal(("2026/27", "bayern"), (bayern["season"]!.GetValue<string>(), bayern["club"]!["region"]!.GetValue<string>()));
        Assert.Equal("tirol", tirol["club"]!["region"]!.GetValue<string>());
        Assert.Equal("2026/27", (await league.ForecastStatsAsync(LeagueRegions.Bayern, default))["season"]!.GetValue<string>());
    }

    /// <summary>Fünf Tiroler Ligen mit Ansicht; Testdorf spielt in Liga 1 (Meldeliste aus <see cref="SeedAsync"/>) und Liga 3 (nur im
    /// Spielplan, als Gast), in Liga 4 nur ein „Testdorfer SC" (anderer Verein), Liga 5 ohne Ansicht zählt nicht.</summary>
    private async Task SeedFiveTyroleanLeaguesAsync()
    {
        await SeedAsync();
        foreach (var (tnr, level) in new[] { (2, 2), (3, 3), (4, 4), (5, 5), (6, 6) })
            _db.LeagueTournaments.Add(new LeagueTournament { Tnr = tnr, Season = "2026/27", Level = level, League = $"Liga {tnr}", Stage = "Liga" });
        foreach (var tnr in new[] { 1, 2, 3, 4, 5 })
            _db.LeagueViews.Add(new LeagueView { Tnr = tnr, Json = "{}", GeneratedAt = Now });
        _db.LeagueMatches.AddRange(
            new LeagueMatch { Tnr = 2, Round = 1, Home = "Absam", Away = "Hall" },
            new LeagueMatch { Tnr = 3, Round = 1, Home = "Wörgl", Away = "Testdorf 2" },
            new LeagueMatch { Tnr = 4, Round = 1, Home = "Testdorfer SC", Away = "Kufstein" },
            new LeagueMatch { Tnr = 6, Round = 1, Home = "Testdorf 3", Away = "Kufstein" });   // ohne Ansicht
        _db.LeaguePlayers.Add(new LeaguePlayer { Tnr = 5, Team = "Rum", Name = "Rum, Rudi", NameKey = "rum, rudi" });
        await _db.SaveChangesAsync();
    }

    private static int[] Tnrs(JsonObject ix) => ix["leagues"]!.AsArray().Select(l => l!["tnr"]!.GetValue<int>()).ToArray();

    [Fact]
    public async Task Index_OnlyLeaguesWithAnOwnTeam_AllOnRequest()
    {
        // Wunsch 2026-10-07 (0.710.0): „Zeig bei der Ligaauswahl nur die Ligen, in denen der Verein vertreten ist."
        await SeedFiveTyroleanLeaguesAsync();
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var own = await league.IndexAsync(TestClubs.Home, default);
        Assert.Equal(new[] { 1, 3 }, Tnrs(own));
        Assert.Equal((true, 5), (own["filtered"]!.GetValue<bool>(), own["total"]!.GetValue<int>()));
        var all = await league.IndexAsync(TestClubs.Home, default, all: true);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Tnrs(all));
        Assert.Equal((false, 5), (all["filtered"]!.GetValue<bool>(), all["total"]!.GetValue<int>()));
        // ein Verein ohne Mannschaft in der Region: leer, aber `total` sagt, dass es Ligen gäbe
        var none = await league.IndexAsync(new LeagueClub { Id = 9, Name = "SK Nirgends", TeamPrefix = "Nirgends", AnonName = "Nirgends" }, default);
        Assert.Empty(Tnrs(none));
        Assert.Equal(5, none["total"]!.GetValue<int>());
    }

    [Fact]
    public async Task IndexEndpoint_AllOnlyForManagers_OthersGetTheFilterSilently()
    {
        await SeedFiveTyroleanLeaguesAsync();
        var role = await new RoleAdminService(_db).CreateAsync(new CreateRoleDto
        {
            Key = "liga-verwalter", Name = "Liga-Verwalter", Permissions = [Permissions.LeagueView, Permissions.LeagueManage],
        });
        await new RoleAdminService(_db).SetUserRolesAsync(BothMember, new SetUserRolesDto { RoleIds = [role.Id] });
        var league = new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance);
        var permissions = new PermissionResolver(_db, TestServices.Cache());
        async Task<JsonObject> Index(int user, bool admin, bool? all) =>
            (JsonObject)((OkObjectResult)await new LeagueController(league, null!, null!, Resolver()).As(user, admin, TestClubs.HomeId)
                .Index(all, permissions, default)).Value!;

        var member = await Index(HomeMember, false, true);          // ohne league.manage: Schalter übergangen, kein 400
        Assert.Equal(new[] { 1, 3 }, Tnrs(member));
        Assert.True(member["filtered"]!.GetValue<bool>());
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Tnrs(await Index(BothMember, false, true)));   // Verwalter über die Rolle
        Assert.Equal(new[] { 1, 3 }, Tnrs(await Index(BothMember, false, null)));            // ohne Schalter auch er gefiltert
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, Tnrs(await Index(Admin, true, true)));
    }

    [Fact]
    public async Task FixtureGames_TakeClubGamesOnlyFromTheClubOfTheRequest()
    {
        await SeedAsync();
        _db.LeagueRounds.Add(new LeagueRound { Tnr = 1, Round = 1, Date = new DateOnly(2025, 10, 4) });
        _db.LeagueGames.Add(new LeagueGame { Id = 50, Tnr = 1, Round = 1, Board = 1, HomeTeam = "Testdorf", AwayTeam = "Absam",
            HomePlayer = "Heimer, Hans", HomeFide = "100", AwayPlayer = "Gast, Gustav", AwayFide = "200", HomeColor = "w", Result = "1 - 0" });
        await _db.SaveChangesAsync();
        // die Partie liegt NUR in der Datenbank von „SK Weiler"
        await Club().ImportPgnAsync(TestClubs.Other, OtherMember, Pgn("Heimer, Hans", "Gast, Gustav"), null);
        var games = new LeagueFixtureGames(_db);
        Assert.Null((await games.ForFixtureAsync(TestClubs.Home, 1, 1, "Testdorf", default)).Single().ClubGameId);
        Assert.NotNull((await games.ForFixtureAsync(TestClubs.Other, 1, 1, "Testdorf", default)).Single().ClubGameId);
    }

    [Fact]
    public async Task GameSources_CountTheClubDatabaseOfTheClubOnly()
    {
        await SeedAsync();
        await Club().ImportPgnAsync(TestClubs.Other, OtherMember, Pgn("Gast, Gustav", "Reich, Rudi"), null);
        var sources = new LeagueGameSources(_db);
        bool HasClubRow(JsonObject r) => r["board"]!.AsArray().Any(x => x!["key"]!.GetValue<string>() == LeagueProfileStore.ClubSource);
        Assert.False(HasClubRow(await sources.GetAsync(TestClubs.HomeId, default)));
        Assert.True(HasClubRow(await sources.GetAsync(TestClubs.OtherId, default)));
    }

    // ── Taktik-Kurs je Verein ───────────────────────────────────────

    [Fact]
    public async Task Tactics_EachClubGetsItsOwnCourse_VisibleOnlyToItsGroups()
    {
        await SeedAsync();
        foreach (var (gameId, clubId, analysisId) in new[] { (70, TestClubs.HomeId, 80), (71, TestClubs.OtherId, 81) })
        {
            _db.LeagueClubGames.Add(new LeagueClubGame { Id = gameId, ClubId = clubId, White = "A", Black = "B", Pgn = "", MovesHash = $"h{gameId}" });
            _db.GameAnalyses.Add(new GameAnalysis { Id = analysisId, UserId = Admin, Origin = GameAnalysisOrigin.Club, LeagueClubGameId = gameId,
                Pgn = "", StartFen = "x", Status = GameAnalysisStatus.Done });
            _db.TacticCandidates.Add(new TacticCandidate
            {
                GameAnalysisId = analysisId, Ply = 41, Origin = GameAnalysisOrigin.Club, PrevFen = "6k1/8/8/8/8/8/1R6/R5K1 b - - 0 1",
                BlunderUci = "g8h8", Fen = "7k/8/8/8/8/8/1R6/R5K1 w - - 0 1", GameMoveUci = "g1g2", Found = false, Kind = "mate",
                Moves = "b2b7 h8g8 a1a8", Status = TacticCandidateStatus.Done, Themes = "mateIn2", EvalText = "#2",
            });
        }
        await _db.SaveChangesAsync();
        var config = new ConfigurationBuilder().Build();
        var svc = new TacticHarvestService(_db, new AnalysisJobService(_db, config: config), new QuietHours("", "UTC"), config,
            NullLogger<TacticHarvestService>.Instance);

        Assert.Equal(2, await svc.PublishAsync(default));

        var books = await _db.Books.OrderBy(b => b.FileName).ToListAsync();
        Assert.Equal(new[] { "tactics-club-1.pgn", "tactics-club-2.pgn" }, books.Select(b => b.FileName));
        Assert.Equal("Taktiken aus Vereinspartien – SK Weiler", books[1].DisplayName);
        Assert.Equal(new[] { HomeGroup }, _db.BookGroupAccesses.Where(a => a.BookId == books[0].Id).Select(a => a.GroupId));
        Assert.Equal(new[] { OtherGroup }, _db.BookGroupAccesses.Where(a => a.BookId == books[1].Id).Select(a => a.GroupId));
        Assert.Equal(1, await _db.BookPuzzles.CountAsync(p => p.BookFileName == "tactics-club-2.pgn"));
    }

    // ── Verwaltung der Vereine ──────────────────────────────────────

    [Fact]
    public async Task Admin_CreatesClub_AssignsAndMovesGroups_BookAccessFollows()
    {
        await SeedAsync();
        var admin = new LeagueClubAdminService(_db, NullLogger<LeagueClubAdminService>.Instance);
        Assert.Equal("invalidRegion", (await admin.CreateAsync("SK Neu", "Neu", "Neu", "irgendwas", default)).Reason);
        Assert.Equal("invalidRegion", (await admin.CreateAsync("SK Neu", "Neu", "Neu", LigamanagerSource.Source, default)).Reason);   // Quelle ≠ Region
        Assert.Equal("duplicate", (await admin.CreateAsync("SK Weiler", "X", "X", null, default)).Reason);
        var (club, _) = await admin.CreateAsync("  SK Neu ", "Neu", "Neu", "", default);
        Assert.Equal(("SK Neu", "tirol"), (club!.Name, club.Region));                        // leer = Tirol
        _db.Books.Add(new Book { FileName = TacticHarvestService.ClubBookOf(club.Id), DisplayName = "x", Kind = BookKind.Puzzle, Source = new BookSource() });
        _db.Groups.Add(new Group { Id = 12, Name = "Neue Gruppe" });
        await _db.SaveChangesAsync();

        Assert.Null(await admin.AddGroupAsync(club.Id, 12, default));
        Assert.Single(_db.BookGroupAccesses.Where(a => a.GroupId == 12));
        // die Gruppe von Testdorf wandert zum neuen Verein: ihre Mitglieder gehören jetzt dorthin
        Assert.Null(await admin.AddGroupAsync(club.Id, HomeGroup, default));
        Assert.Equal(club.Id, (await Resolver().ResolveAsync(HomeMember, false, null)).Club?.Id);
        Assert.True(await admin.RemoveGroupAsync(club.Id, 12, default));
        Assert.Empty(_db.BookGroupAccesses.Where(a => a.GroupId == 12));
        Assert.Equal("groupNotFound", await admin.AddGroupAsync(club.Id, 999, default));
        _db.Groups.Add(new Group { Id = 13, Name = "Everyone", IsEveryone = true });
        await _db.SaveChangesAsync();
        Assert.Equal("everyone", await admin.AddGroupAsync(club.Id, 13, default));

        var (renamed, _) = await admin.UpdateAsync(club.Id, "SK Neustadt", null, "Neustadt", null, default);
        Assert.Equal(("SK Neustadt", "Neu", "Neustadt", "tirol"), (renamed!.Name, renamed.TeamPrefix, renamed.AnonName, renamed.Region));
        Assert.Equal("bayern", (await admin.UpdateAsync(club.Id, null, null, null, " Bayern ", default)).Club!.Region);
        Assert.Equal("invalidRegion", (await admin.UpdateAsync(club.Id, null, null, null, "mars", default)).Reason);
        Assert.Equal("Taktiken aus Vereinspartien – SK Neustadt", (await _db.Books.SingleAsync(b => b.FileName == TacticHarvestService.ClubBookOf(club.Id))).DisplayName);
    }

    [Fact]
    public async Task Admin_List_CarriesGroupsWithMembers_GameCount_AndCreatedAt()
    {
        await SeedAsync();
        _db.LeagueClubGames.AddRange(
            new LeagueClubGame { ClubId = TestClubs.HomeId, White = "A", Black = "B", Pgn = "x", MovesHash = "h1" },
            new LeagueClubGame { ClubId = TestClubs.HomeId, White = "C", Black = "D", Pgn = "x", MovesHash = "h2" },
            new LeagueClubGame { ClubId = TestClubs.HomeId, White = "E", Black = "F", Pgn = "x", MovesHash = "h3", ArchivedAt = Now },
            new LeagueClubGame { ClubId = TestClubs.OtherId, White = "G", Black = "H", Pgn = "x", MovesHash = "h4" });
        await _db.SaveChangesAsync();
        var list = await new LeagueClubAdminService(_db, NullLogger<LeagueClubAdminService>.Instance).ListAsync(default);

        var home = list.Single(c => (int)c!["id"]! == TestClubs.HomeId)!;
        Assert.Equal(2, (int)home["clubGames"]!);   // die archivierte zählt nicht
        var group = home["groups"]!.AsArray().Single()!;
        Assert.Equal((HomeGroup, "Testdorf", 3), ((int)group["id"]!, (string)group["name"]!, (int)group["members"]!));
        Assert.NotNull(home["createdAt"]);
        var other = list.Single(c => (int)c!["id"]! == TestClubs.OtherId)!;
        Assert.Equal(1, (int)other["clubGames"]!);
        Assert.Equal(2, (int)other["groups"]![0]!["members"]!);
    }

    [Theory]
    [InlineData("Schwaz", "Schwaz", true)]
    [InlineData("Schwaz", "Schwaz 2", true)]
    [InlineData("Schwaz", "Schwazer SK", false)]
    [InlineData("SK Weilheim", "SK Weilheim 1", true)]
    [InlineData("SK Weilheim", "sk weilheim 2", true)]
    [InlineData("SK Weilheim", "SK Weilheimer 1", false)]
    [InlineData("SK Weilheim", "Weilheim", false)]
    public void OwnsTeam_PrefixAtAWordBoundary(string prefix, string team, bool own) =>
        Assert.Equal(own, new LeagueClub { TeamPrefix = prefix }.OwnsTeam(team));
}
