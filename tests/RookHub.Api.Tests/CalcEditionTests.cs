using RookHub.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class CalcEditionTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CalcEditionService _editions;
    private readonly CalculationService _calc;
    private readonly CourseAuthoringService _authoring;
    private const int OwnerId = 5, ViewerId = 6, TesterId = 7;

    public CalcEditionTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(opts);
        _editions = new CalcEditionService(_db, TestServices.Friends(_db));
        _calc = new CalculationService(_db);
        _authoring = new CourseAuthoringService(_db);
    }
    public void Dispose() => _db.Dispose();

    private async Task<int> SeedBookAsync()
    {
        _db.AppUsers.Add(new AppUser { Id = OwnerId, Username = "owner", PasswordHash = "x" });
        _db.AppUsers.Add(new AppUser { Id = ViewerId, Username = "viewer", PasswordHash = "x" });
        _db.AppUsers.Add(new AppUser { Id = TesterId, Username = "tester", PasswordHash = "x" });
        // Verteiler nur für Freunde des Besitzers (A7-004): Betrachter und Tester sind mit ihm befreundet.
        _db.Friendships.Add(new Friendship { RequesterId = OwnerId, AddresseeId = ViewerId, Status = FriendshipStatus.Accepted });
        _db.Friendships.Add(new Friendship { RequesterId = TesterId, AddresseeId = OwnerId, Status = FriendshipStatus.Accepted });
        var book = new Book { FileName = "noel.pgn", DisplayName = "Noel", IsCalculation = true, IsPublic = true, OwnerUserId = OwnerId, Source = new BookSource() };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        void Pos(string chapter, string round) => _db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = $"noel:{round}", BookFileName = "noel.pgn", BookId = book.Id, Round = round,
            Fen = "8/8/8/8/8/8/8/8 w - - 0 1", Moves = "", Chapter = chapter, IsInfoOnly = true,
        });
        Pos("Woche A", "001"); Pos("Woche A", "002");
        Pos("Woche B", "003"); Pos("Woche B", "004");
        await _db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task Upsert_CreatesThenUpdates_ByChapter()
    {
        var bookId = await SeedBookAsync();
        var future = DateTime.UtcNow.AddDays(2);
        var a = await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", VideoUrl = "https://yt/1", PublishAt = future });
        var b = await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", VideoUrl = "https://yt/2", PublishAt = future });
        Assert.Equal(a.Id, b.Id);                  // Upsert je Kapitel → dieselbe Ausgabe
        Assert.Equal("https://yt/2", b.VideoUrl);
        Assert.Equal(1, await _db.CalcEditions.CountAsync());
    }

    [Fact]
    public async Task ListVisible_OnlyReleased()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche A", PublishAt = DateTime.UtcNow.AddDays(-1) });
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(1) });
        var vis = await _editions.ListVisibleAsync(bookId);
        Assert.Single(vis);
        Assert.Equal("Woche A", vis[0].Chapter);
        Assert.True(vis[0].Released);
    }

    [Fact]
    public async Task GetBook_HidesFutureWeek_ForViewer_ButNotOwner()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(2) });

        var viewer = await _calc.GetBookAsync(ViewerId, bookId, isAdmin: false);
        Assert.DoesNotContain(viewer.Positions, p => p.Chapter == "Woche B");   // Entwurf ausgeblendet
        Assert.Contains(viewer.Positions, p => p.Chapter == "Woche A");         // freie Woche sichtbar

        var owner = await _calc.GetBookAsync(OwnerId, bookId, isAdmin: false);
        Assert.Contains(owner.Positions, p => p.Chapter == "Woche B");          // Besitzer sieht alles
    }

    [Fact]
    public async Task GetPublicBook_HidesFutureWeek()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(2) });
        var pub = await _calc.GetPublicBookAsync(bookId);
        Assert.DoesNotContain(pub.Positions, p => p.Chapter == "Woche B");
        Assert.Contains(pub.Positions, p => p.Chapter == "Woche A");
    }

    [Fact]
    public async Task GetPosition_HiddenWeek_NotFoundForViewer_OkForOwner()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(2) });
        var wb = await _db.BookPuzzles.FirstAsync(p => p.Chapter == "Woche B");
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _calc.GetPositionAsync(ViewerId, wb.Id, isAdmin: false));
        var owner = await _calc.GetPositionAsync(OwnerId, wb.Id, isAdmin: false);
        Assert.Equal(wb.Id, owner.Id);
    }

    [Fact]
    public async Task CanManage_OwnerAndAdminOnly()
    {
        var bookId = await SeedBookAsync();
        Assert.True(await _editions.CanManageAsync(OwnerId, bookId, isAdmin: false));
        Assert.False(await _editions.CanManageAsync(ViewerId, bookId, isAdmin: false));
        Assert.True(await _editions.CanManageAsync(ViewerId, bookId, isAdmin: true));
    }

    [Fact]
    public async Task Members_AddUpdateRemove_ByUsername()
    {
        var bookId = await SeedBookAsync();
        Assert.Null(await _editions.UpsertMemberAsync(bookId, "does-not-exist", isTester: false)); // unbekannt → null

        var added = await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        Assert.NotNull(added);
        Assert.False(added!.IsTester);

        var updated = await _editions.UpsertMemberAsync(bookId, "VIEWER", isTester: true); // case-insensitiv + Upsert
        Assert.Equal(ViewerId, updated!.UserId);
        Assert.True(updated.IsTester);
        Assert.Equal(1, await _db.CalcSeriesMembers.CountAsync(m => m.BookId == bookId)); // kein Duplikat

        var list = await _editions.ListMembersAsync(bookId);
        Assert.Single(list);
        Assert.Equal("viewer", list[0].Username);
        Assert.True(list[0].IsTester);

        Assert.True(await _editions.RemoveMemberAsync(bookId, ViewerId));
        Assert.False(await _editions.RemoveMemberAsync(bookId, ViewerId));   // schon weg
        Assert.Empty(await _editions.ListMembersAsync(bookId));
    }

    [Fact]
    public async Task Tester_SeesTesterPreviewWeek_PlainViewerDoesNot()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto
        {
            Chapter = "Woche B",
            PublishAt = DateTime.UtcNow.AddDays(5),          // öffentliche Freigabe noch fern
            TesterPreviewAt = DateTime.UtcNow.AddDays(-1),   // Tester-Vorschau schon offen
        });
        await _editions.UpsertMemberAsync(bookId, "tester", isTester: true);

        var tester = await _calc.GetBookAsync(TesterId, bookId, isAdmin: false);
        Assert.Contains(tester.Positions, p => p.Chapter == "Woche B");        // Tester sieht die Vorschau

        var viewer = await _calc.GetBookAsync(ViewerId, bookId, isAdmin: false);
        Assert.DoesNotContain(viewer.Positions, p => p.Chapter == "Woche B");  // Nicht-Tester noch nicht
    }

    [Fact]
    public async Task PrivateSeries_MemberHasAccess_NonMemberDoesNot()
    {
        var bookId = await SeedBookAsync();
        var book = await _db.Books.FirstAsync(b => b.Id == bookId);
        book.IsPublic = false;                               // Serie privat schalten
        await _db.SaveChangesAsync();

        // Nicht-Mitglied (kein Owner/Share/Gruppe): kein Zugriff → wie „nicht gefunden".
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _calc.GetBookAsync(ViewerId, bookId, isAdmin: false));

        // Verteiler-Mitglied: Zugriff.
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        var viewer = await _calc.GetBookAsync(ViewerId, bookId, isAdmin: false);
        Assert.NotEmpty(viewer.Positions);

        // Besitzer immer.
        var owner = await _calc.GetBookAsync(OwnerId, bookId, isAdmin: false);
        Assert.NotEmpty(owner.Positions);
    }

    [Fact]
    public async Task View_RecordedForMemberOnly_Idempotent()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche A", PublishAt = DateTime.UtcNow.AddDays(-1) }); // freigegeben
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        var wa = await _db.BookPuzzles.Where(p => p.Chapter == "Woche A").OrderBy(p => p.Id).ToListAsync();

        // Mitglied öffnet zwei Stellungen derselben Woche → genau EIN „gesehen"-Vermerk (je Ausgabe).
        await _calc.GetPositionAsync(ViewerId, wa[0].Id, isAdmin: false);
        await _calc.GetPositionAsync(ViewerId, wa[1].Id, isAdmin: false);
        Assert.Equal(1, await _db.CalcEditionViews.CountAsync());

        // Besitzer (kein Mitglied) zählt nicht.
        await _calc.GetPositionAsync(OwnerId, wa[0].Id, isAdmin: false);
        // Nicht-Mitglied mit Zugriff (Buch ist öffentlich) zählt nicht.
        await _calc.GetPositionAsync(TesterId, wa[0].Id, isAdmin: false);
        Assert.Equal(1, await _db.CalcEditionViews.CountAsync());

        var views = await _editions.ListViewsAsync(bookId);
        Assert.Single(views);
        Assert.Equal("viewer", views[0].Username);
        Assert.Equal("Woche A", views[0].Chapter);
    }

    [Fact]
    public async Task View_NotRecorded_ForHiddenWeek()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(3) }); // Entwurf
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        var wb = await _db.BookPuzzles.FirstAsync(p => p.Chapter == "Woche B");

        // Mitglied kann die noch nicht freigegebene Woche nicht öffnen → kein Vermerk.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _calc.GetPositionAsync(ViewerId, wb.Id, isAdmin: false));
        Assert.Equal(0, await _db.CalcEditionViews.CountAsync());
    }

    private CalcSeriesAnnounceService Announcer() => new(_db, new NotificationService(_db));

    [Fact]
    public async Task Announce_PublicRelease_NotifiesAllMembers_Idempotent()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche A", PublishAt = DateTime.UtcNow.AddMinutes(-1) });
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        await _editions.UpsertMemberAsync(bookId, "tester", isTester: true);   // ohne Tester-Termin ist er normales Mitglied

        var announcer = Announcer();
        Assert.Equal(1, await announcer.RunOnceAsync());                        // eine öffentliche Runde
        var notifs = await _db.Notifications.ToListAsync();
        Assert.Equal(2, notifs.Count);                                         // beide Mitglieder
        Assert.All(notifs, n => Assert.Equal(NotificationType.CalcSeriesEditionReleased, n.Type));
        Assert.Contains(notifs, n => n.UserId == ViewerId);
        Assert.Contains(notifs, n => n.UserId == TesterId);
        Assert.NotNull((await _db.CalcEditions.FirstAsync()).PublishAnnouncedAt);

        Assert.Equal(0, await announcer.RunOnceAsync());                        // idempotent
        Assert.Equal(2, await _db.Notifications.CountAsync());
    }

    [Fact]
    public async Task Announce_TesterPreviewFirst_ThenPublicToNonTestersOnly()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto
        {
            Chapter = "Woche B",
            PublishAt = DateTime.UtcNow.AddDays(1),          // öffentlich noch fern
            TesterPreviewAt = DateTime.UtcNow.AddMinutes(-1) // Tester-Vorschau offen
        });
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        await _editions.UpsertMemberAsync(bookId, "tester", isTester: true);
        var announcer = Announcer();

        // Runde 1: nur Tester.
        Assert.Equal(1, await announcer.RunOnceAsync());
        var afterTester = await _db.Notifications.ToListAsync();
        Assert.Single(afterTester);
        Assert.Equal(TesterId, afterTester[0].UserId);
        var ed = await _db.CalcEditions.FirstAsync();
        Assert.NotNull(ed.TesterAnnouncedAt);
        Assert.Null(ed.PublishAnnouncedAt);

        // Zeit vergeht: öffentliche Freigabe erreicht.
        ed.PublishAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        // Runde 2: nur NICHT-Tester (Tester wurde schon informiert).
        Assert.Equal(1, await announcer.RunOnceAsync());
        var all = await _db.Notifications.ToListAsync();
        Assert.Equal(2, all.Count);
        Assert.Single(all, n => n.UserId == ViewerId);      // Betrachter jetzt informiert
        Assert.Single(all, n => n.UserId == TesterId);      // Tester NICHT erneut
        Assert.NotNull((await _db.CalcEditions.FirstAsync()).PublishAnnouncedAt);
    }

    [Fact]
    public async Task Announce_NoMembers_StampsWithoutCrash()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche A", PublishAt = DateTime.UtcNow.AddMinutes(-1) });
        var announcer = Announcer();
        Assert.Equal(1, await announcer.RunOnceAsync());
        Assert.Equal(0, await _db.Notifications.CountAsync());
        Assert.NotNull((await _db.CalcEditions.FirstAsync()).PublishAnnouncedAt);
        Assert.Equal(0, await announcer.RunOnceAsync());     // nichts mehr offen
    }

    [Fact]
    public async Task Announce_SkipsOrphanedAndDeletedMembers()
    {
        // Codereview 2026-09-29 (A9-004): CalcSeriesMember.UserId hat keinen FK. Eine Waise (Konto hart
        // gelöscht) ließ den Benachrichtigungs-INSERT am FK scheitern — die Runde samt Marker fiel bei jedem Lauf,
        // niemand bekam mehr eine Ankündigung. InMemory kennt keinen FK: hier fällt der Test an der Zeile für die Waise.
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto
        {
            Chapter = "Woche B",
            PublishAt = DateTime.UtcNow.AddDays(1),
            TesterPreviewAt = DateTime.UtcNow.AddMinutes(-1),
        });
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        await _editions.UpsertMemberAsync(bookId, "tester", isTester: true);
        const int OrphanId = 990777, DeletedId = 990778;
        _db.AppUsers.Add(new AppUser { Id = DeletedId, Username = $"deleted_{DeletedId}", PasswordHash = "x", DeletedAt = DateTime.UtcNow });
        _db.CalcSeriesMembers.Add(new CalcSeriesMember { BookId = bookId, UserId = OrphanId, IsTester = true });
        _db.CalcSeriesMembers.Add(new CalcSeriesMember { BookId = bookId, UserId = DeletedId });
        await _db.SaveChangesAsync();
        var announcer = Announcer();

        Assert.Equal(1, await announcer.RunOnceAsync());                  // Tester-Runde: nur der lebende Tester
        var ed = await _db.CalcEditions.FirstAsync();
        Assert.Equal(TesterId.ToString(), ed.TesterAnnouncedUserIds);
        ed.PublishAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();
        Assert.Equal(1, await announcer.RunOnceAsync());                  // öffentliche Runde: nur der Betrachter

        var recipients = await _db.Notifications.Select(n => n.UserId).OrderBy(id => id).ToListAsync();
        Assert.Equal(new[] { ViewerId, TesterId }, recipients);
    }

    [Fact]
    public async Task Announce_TesterAddedAfterTesterRound_StillNotifiedAtPublic()
    {
        // Regression (Review 3b): ein NACH der Tester-Runde hinzugefügter Tester darf nicht verloren gehen.
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto
        {
            Chapter = "Woche B",
            PublishAt = DateTime.UtcNow.AddDays(1),
            TesterPreviewAt = DateTime.UtcNow.AddMinutes(-1),
        });
        var announcer = Announcer();

        // Tester-Runde läuft ohne Mitglieder → Marker gesetzt, Empfängerliste leer.
        Assert.Equal(1, await announcer.RunOnceAsync());
        Assert.Equal(0, await _db.Notifications.CountAsync());

        // Erst danach kommt ein Tester dazu; öffentliche Freigabe wird erreicht.
        await _editions.UpsertMemberAsync(bookId, "tester", isTester: true);
        var ed = await _db.CalcEditions.FirstAsync();
        ed.PublishAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await announcer.RunOnceAsync());
        var notifs = await _db.Notifications.ToListAsync();
        Assert.Single(notifs);                      // genau einmal
        Assert.Equal(TesterId, notifs[0].UserId);   // der spät hinzugefügte Tester
    }

    [Fact]
    public async Task Announce_TesterFlagRemovedBetweenRounds_NotDoubleNotified()
    {
        // Regression (Review 3b): ein zwischen den Runden ent-Tester-tes Mitglied darf nicht doppelt kommen.
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto
        {
            Chapter = "Woche B",
            PublishAt = DateTime.UtcNow.AddDays(1),
            TesterPreviewAt = DateTime.UtcNow.AddMinutes(-1),
        });
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        await _editions.UpsertMemberAsync(bookId, "tester", isTester: true);
        var announcer = Announcer();

        // Tester-Runde: nur der Tester.
        Assert.Equal(1, await announcer.RunOnceAsync());
        Assert.Single(await _db.Notifications.ToListAsync());

        // Tester-Häkchen entfernt + öffentliche Freigabe erreicht.
        await _editions.UpsertMemberAsync(bookId, "tester", isTester: false);
        var ed = await _db.CalcEditions.FirstAsync();
        ed.PublishAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();

        Assert.Equal(1, await announcer.RunOnceAsync());
        var byUser = await _db.Notifications.GroupBy(n => n.UserId).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
        Assert.Equal(1, byUser.Single(x => x.Key == TesterId).Count);   // Tester NICHT doppelt
        Assert.Equal(1, byUser.Single(x => x.Key == ViewerId).Count);   // Betrachter genau einmal
    }

    // ---- Zugriffsschranke der öffentlichen Ausgaben-Liste (Review 2026-09-03) ----

    private CalcSeriesController Controller(int? userId, bool isAdmin = false)
    {
        var claims = new List<Claim>();
        if (userId is not null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId.Value.ToString()));
        if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return new CalcSeriesController(_editions, _db)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(claims, userId is null ? null : "test")),
                },
            },
        };
    }

    private async Task<int> SeedSeriesBookAsync(bool isPublic, int ownerUserId = 999)
    {
        var book = new Book { FileName = $"serie-{Guid.NewGuid():N}.pgn", DisplayName = "Serie", IsPublic = isPublic, OwnerUserId = ownerUserId, Source = new BookSource() };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        _db.CalcEditions.Add(new CalcEdition
        {
            BookId = book.Id, Chapter = "Woche 1", Title = "W1",
            VideoUrl = "https://youtu.be/geheim", PublishAt = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task ListVisible_PrivateBook_Anonymous_IsNotFound()
    {
        // Der Inhalt einer PRIVATEN Serie sind genau diese Video-URLs. Ohne Gate war die Liste per
        // Buch-Id-Iteration ohne Login lesbar — „privat schalten" über IsPublic griff hier nicht.
        var bookId = await SeedSeriesBookAsync(isPublic: false);

        Assert.IsType<NotFoundResult>((await Controller(null).ListVisible(bookId, default)).Result);
    }

    [Fact]
    public async Task ListVisible_PublicBook_Anonymous_ReturnsEditions()
    {
        var bookId = await SeedSeriesBookAsync(isPublic: true);

        var ok = Assert.IsType<OkObjectResult>((await Controller(null).ListVisible(bookId, default)).Result);
        Assert.Single(Assert.IsAssignableFrom<List<CalcEditionDto>>(ok.Value));
    }

    [Fact]
    public async Task ListVisible_PrivateBook_DistributionMember_ReturnsEditions()
    {
        var bookId = await SeedSeriesBookAsync(isPublic: false);
        _db.CalcSeriesMembers.Add(new CalcSeriesMember { BookId = bookId, UserId = 42 });
        await _db.SaveChangesAsync();

        var ok = Assert.IsType<OkObjectResult>((await Controller(42).ListVisible(bookId, default)).Result);
        Assert.Single(Assert.IsAssignableFrom<List<CalcEditionDto>>(ok.Value));
    }

    [Fact]
    public async Task ListVisible_PrivateBook_StrangerLoggedIn_IsNotFound()
    {
        var bookId = await SeedSeriesBookAsync(isPublic: false);

        Assert.IsType<NotFoundResult>((await Controller(77).ListVisible(bookId, default)).Result);
    }

    // ===== Kurs-Detailseite: dieselbe Termin-Sperre wie der Kalkulations-Modus (Codereview 2026-09-29, A7-002) =====

    [Fact]
    public async Task Detail_HidesFutureWeek_ForViewer_ButNotOwnerOrAdmin()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(2) });

        var viewer = await _authoring.GetDetailAsync(ViewerId, bookId, isAdmin: false);
        Assert.Equal(new[] { "Woche A" }, viewer.Chapters.Select(c => c.Name));   // gesperrte Woche fehlt
        Assert.Equal(2, viewer.TotalLines);                                      // … auch in den Zählern
        Assert.Equal(2, viewer.PuzzleCount);

        var owner = await _authoring.GetDetailAsync(OwnerId, bookId, isAdmin: false);
        Assert.Equal(new[] { "Woche A", "Woche B" }, owner.Chapters.Select(c => c.Name));
        Assert.Equal(4, owner.TotalLines);

        var admin = await _authoring.GetDetailAsync(ViewerId, bookId, isAdmin: true);
        Assert.Equal(2, admin.Chapters.Count);
    }

    [Fact]
    public async Task Detail_ReleasedWeek_VisibleForViewer()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddMinutes(-1) });

        var viewer = await _authoring.GetDetailAsync(ViewerId, bookId, isAdmin: false);
        Assert.Equal(new[] { "Woche A", "Woche B" }, viewer.Chapters.Select(c => c.Name));
    }

    [Fact]
    public async Task Detail_Tester_SeesTesterPreviewWeek_PlainViewerDoesNot()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto
        {
            Chapter = "Woche B",
            PublishAt = DateTime.UtcNow.AddDays(5),
            TesterPreviewAt = DateTime.UtcNow.AddDays(-1),
        });
        await _editions.UpsertMemberAsync(bookId, "tester", isTester: true);
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);

        Assert.Contains((await _authoring.GetDetailAsync(TesterId, bookId, isAdmin: false)).Chapters, c => c.Name == "Woche B");
        Assert.DoesNotContain((await _authoring.GetDetailAsync(ViewerId, bookId, isAdmin: false)).Chapters, c => c.Name == "Woche B");
    }

    [Fact]
    public async Task ChapterLines_HiddenWeek_EmptyForViewer_FullForOwner()
    {
        // Die Linien-Tabelle der Detailseite zeigt FEN + Kommentar jeder Stellung — für eine gesperrte
        // Woche darf sie beim Betrachter leer bleiben (wie ein unbekanntes Kapitel).
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(2) });

        Assert.Empty(await _authoring.GetChapterLinesAsync(ViewerId, bookId, "Woche B", isAdmin: false));
        Assert.Equal(2, (await _authoring.GetChapterLinesAsync(ViewerId, bookId, "Woche A", isAdmin: false)).Count);
        Assert.Equal(2, (await _authoring.GetChapterLinesAsync(OwnerId, bookId, "Woche B", isAdmin: false)).Count);
    }

    // ===== Kapitel umbenennen/löschen nimmt die Ausgabe mit (Codereview 2026-09-29, A7-003) =====

    [Fact]
    public async Task RenameChapter_WithEdition_WeekStaysHidden_EditionFollows()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(7) });

        await _authoring.RenameChapterAsync(OwnerId, bookId,
            new RenameCourseChapterDto { Chapter = "Woche B", NewName = "Woche B – Opfer" }, isAdmin: false);

        Assert.Equal("Woche B – Opfer", (await _db.CalcEditions.SingleAsync()).Chapter);
        var viewer = await _calc.GetBookAsync(ViewerId, bookId, isAdmin: false);
        Assert.DoesNotContain(viewer.Positions, p => p.Chapter == "Woche B – Opfer");   // bleibt bis zum Termin gesperrt
        Assert.DoesNotContain((await _calc.GetPublicBookAsync(bookId)).Positions, p => p.Chapter == "Woche B – Opfer");
        var owner = await _calc.GetBookAsync(OwnerId, bookId, isAdmin: false);
        Assert.Equal(2, owner.Positions.Count(p => p.Chapter == "Woche B – Opfer"));
    }

    [Fact]
    public async Task RenameChapter_WithEdition_ToNoChapter_IsRejected()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(7) });

        await Assert.ThrowsAsync<ArgumentException>(() => _authoring.RenameChapterAsync(OwnerId, bookId,
            new RenameCourseChapterDto { Chapter = "Woche B", NewName = "  " }, isAdmin: false));

        Assert.Equal(2, await _db.BookPuzzles.CountAsync(p => p.Chapter == "Woche B"));   // nichts verändert
        Assert.Equal("Woche B", (await _db.CalcEditions.SingleAsync()).Chapter);
    }

    [Fact]
    public async Task RenameChapter_TargetNameAlreadyHasEdition_IsRejected()
    {
        // Ausgabe für ein (noch) leeres Kapitel „Woche C" — Unique (BookId, Chapter) darf nicht platzen.
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(7) });
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche C", PublishAt = DateTime.UtcNow.AddDays(14) });

        await Assert.ThrowsAsync<ArgumentException>(() => _authoring.RenameChapterAsync(OwnerId, bookId,
            new RenameCourseChapterDto { Chapter = "Woche B", NewName = "Woche C" }, isAdmin: false));
        Assert.Equal(2, await _db.BookPuzzles.CountAsync(p => p.Chapter == "Woche B"));
    }

    [Fact]
    public async Task RenameChapter_WithoutEdition_Unchanged()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(7) });

        Assert.Equal(2, await _authoring.RenameChapterAsync(OwnerId, bookId,
            new RenameCourseChapterDto { Chapter = "Woche A", NewName = "Woche A neu" }, isAdmin: false));
        Assert.Equal("Woche B", (await _db.CalcEditions.SingleAsync()).Chapter);
    }

    [Fact]
    public async Task DeleteChapter_RemovesEdition_NoAnnouncement()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddMinutes(-1) });
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche A", PublishAt = DateTime.UtcNow.AddDays(7) });
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);

        await _authoring.DeleteChapterAsync(OwnerId, bookId, "Woche B", isAdmin: false);

        Assert.Equal("Woche A", (await _db.CalcEditions.SingleAsync()).Chapter);   // nur die Ausgabe des Kapitels
        Assert.Equal(0, await Announcer().RunOnceAsync());
        Assert.Equal(0, await _db.Notifications.CountAsync());
    }

    [Fact]
    public async Task Announce_OrphanEdition_SkippedUntilChapterHasPositions()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche C", PublishAt = DateTime.UtcNow.AddMinutes(-1) });
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);

        Assert.Equal(0, await Announcer().RunOnceAsync());                     // Kapitel ohne Stellungen → keine Ankündigung
        Assert.Equal(0, await _db.Notifications.CountAsync());
        Assert.Null((await _db.CalcEditions.SingleAsync()).PublishAnnouncedAt); // … und nicht als erledigt markiert

        _db.BookPuzzles.Add(new BookPuzzle
        {
            LineId = "noel:005", BookFileName = "noel.pgn", BookId = bookId, Round = "005",
            Fen = "8/8/8/8/8/8/8/8 w - - 0 1", Moves = "", Chapter = "Woche C", IsInfoOnly = true,
        });
        await _db.SaveChangesAsync();

        Assert.Equal(1, await Announcer().RunOnceAsync());                     // jetzt gibt es die Woche
        Assert.Equal(ViewerId, (await _db.Notifications.SingleAsync()).UserId);
    }

    // ===== Verteiler nur für Freunde, Deckel, Selbst-Austragen (Codereview 2026-09-29, A7-004) =====

    private const int StrangerId = 8;

    private async Task<int> SeedBookWithStrangerAsync()
    {
        var bookId = await SeedBookAsync();
        _db.AppUsers.Add(new AppUser { Id = StrangerId, Username = "stranger", PasswordHash = "x" });
        // Offene (nicht bestätigte) Anfrage zählt NICHT als Freundschaft.
        _db.Friendships.Add(new Friendship { RequesterId = OwnerId, AddresseeId = StrangerId, Status = FriendshipStatus.Pending });
        await _db.SaveChangesAsync();
        return bookId;
    }

    [Fact]
    public async Task Members_NonFriend_IsRejected_LikeUnknownUser()
    {
        var bookId = await SeedBookWithStrangerAsync();

        Assert.Null(await _editions.UpsertMemberAsync(bookId, "stranger", isTester: false));
        Assert.False(await _db.CalcSeriesMembers.AnyAsync());   // kein Eintrag → keine Ankündigung, kein Kurs in seiner Liste
    }

    [Fact]
    public async Task Members_Controller_NonFriendAndUnknown_SameNotFound()
    {
        var bookId = await SeedBookWithStrangerAsync();
        var owner = Controller(OwnerId);

        var stranger = Assert.IsType<NotFoundObjectResult>((await owner.UpsertMember(bookId,
            new CalcSeriesMemberInputDto { Username = "stranger" }, default)).Result);
        var unknown = Assert.IsType<NotFoundObjectResult>((await owner.UpsertMember(bookId,
            new CalcSeriesMemberInputDto { Username = "gibt-es-nicht" }, default)).Result);
        // Kein Benutzernamen-Orakel: identischer Körper für „kein Freund" und „gibt es nicht".
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(unknown.Value), System.Text.Json.JsonSerializer.Serialize(stranger.Value));
        Assert.False(await _db.CalcSeriesMembers.AnyAsync());

        Assert.IsType<OkObjectResult>((await owner.UpsertMember(bookId,
            new CalcSeriesMemberInputDto { Username = "viewer" }, default)).Result);   // Freund → ok
    }

    [Fact]
    public async Task Members_Admin_MayAddNonFriend()
    {
        var bookId = await SeedBookWithStrangerAsync();

        var ok = Assert.IsType<OkObjectResult>((await Controller(ViewerId, isAdmin: true).UpsertMember(bookId,
            new CalcSeriesMemberInputDto { Username = "stranger" }, default)).Result);
        Assert.Equal(StrangerId, Assert.IsType<CalcSeriesMemberDto>(ok.Value).UserId);
    }

    [Fact]
    public async Task Members_ExistingMember_StaysEditable_AfterFriendshipEnded()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        _db.Friendships.RemoveRange(_db.Friendships.Where(f => f.AddresseeId == ViewerId || f.RequesterId == ViewerId));
        await _db.SaveChangesAsync();

        var updated = await _editions.UpsertMemberAsync(bookId, "viewer", isTester: true);   // Tester-Häkchen bleibt schaltbar
        Assert.True(updated!.IsTester);
    }

    [Fact]
    public async Task Members_NonCalculationBook_IsRejected()
    {
        var bookId = await SeedBookAsync();
        var book = await _db.Books.SingleAsync();
        book.IsCalculation = false;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _editions.UpsertMemberAsync(bookId, "viewer", isTester: false));
        Assert.IsType<BadRequestObjectResult>((await Controller(OwnerId).UpsertMember(bookId,
            new CalcSeriesMemberInputDto { Username = "viewer" }, default)).Result);
        Assert.False(await _db.CalcSeriesMembers.AnyAsync());
    }

    [Fact]
    public async Task Members_CapReached_NewMemberRejected_ExistingStillEditable()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        for (var i = 0; i < CalcEditionService.MaxMembersPerBook - 1; i++)
            _db.CalcSeriesMembers.Add(new CalcSeriesMember { BookId = bookId, UserId = 1000 + i });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _editions.UpsertMemberAsync(bookId, "tester", isTester: false));
        Assert.Equal(CalcEditionService.MaxMembersPerBook, await _db.CalcSeriesMembers.CountAsync(m => m.BookId == bookId));
        Assert.True((await _editions.UpsertMemberAsync(bookId, "viewer", isTester: true))!.IsTester);
    }

    [Fact]
    public async Task Editions_CapReached_NewEditionRejected_ExistingStillEditable()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche A", PublishAt = DateTime.UtcNow.AddDays(1) });
        for (var i = 1; i < CalcEditionService.MaxEditionsPerBook; i++)
            _db.CalcEditions.Add(new CalcEdition { BookId = bookId, Chapter = $"x{i}", PublishAt = DateTime.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _editions.UpsertAsync(bookId,
            new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(-1) }));
        Assert.IsType<BadRequestObjectResult>((await Controller(OwnerId).Upsert(bookId,
            new CalcEditionInputDto { Chapter = "Woche B", PublishAt = DateTime.UtcNow.AddDays(-1) }, default)).Result);
        Assert.Equal(CalcEditionService.MaxEditionsPerBook, await _db.CalcEditions.CountAsync(e => e.BookId == bookId));

        var edited = await _editions.UpsertAsync(bookId, new CalcEditionInputDto { Chapter = "Woche A", Title = "neu", PublishAt = DateTime.UtcNow.AddDays(2) });
        Assert.Equal("neu", edited.Title);
    }

    [Fact]
    public async Task RemoveMember_Self_WithoutManageRights()
    {
        var bookId = await SeedBookAsync();
        await _editions.UpsertMemberAsync(bookId, "viewer", isTester: false);
        await _editions.UpsertMemberAsync(bookId, "tester", isTester: true);

        // Fremdes Mitglied austragen: weiterhin nur Besitzer/Admin.
        Assert.IsType<ForbidResult>(await Controller(ViewerId).RemoveMember(bookId, TesterId, default));
        // Sich selbst austragen: ohne Verwaltungsrecht erlaubt, idempotent (danach 404).
        Assert.IsType<NoContentResult>(await Controller(ViewerId).RemoveMember(bookId, ViewerId, default));
        Assert.IsType<NotFoundResult>(await Controller(ViewerId).RemoveMember(bookId, ViewerId, default));
        Assert.Equal(new[] { TesterId }, await _db.CalcSeriesMembers.Select(m => m.UserId).ToArrayAsync());
    }
}
