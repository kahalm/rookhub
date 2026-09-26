using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace RookHub.Api.Tests;

/// <summary>
/// Auftraege der Kurs-Uebersetzung (Plan „Kurs-Kommentare mehrsprachig", Abschnitt 5, Stufe B): anfordern mit
/// Nutzer-Limit und Admin-Ausnahme, vorhandener Auftrag statt eines neuen, die Absagen, zurueckziehen, Quellsprache
/// korrigieren, die Ansichten — und das Nachziehen nach einer Aenderung des Kurses. Nie ein echtes Modell.
/// </summary>
public class CourseTranslationJobServiceTests : IDisposable
{
    private readonly CourseTranslationTestKit _kit = new();

    public void Dispose() => _kit.Dispose();

    private const int Alice = 7, Bob = 8, Admin = 1;

    /// <summary>Ein oeffentlicher Kurs (Zugang fuer jeden) mit zwei kommentierten Linien, Quellsprache Englisch.</summary>
    private async Task<Book> CourseAsync(string name = "Kurs", string? lang = "en", bool isPublic = true, int? owner = null)
    {
        var book = await _kit.SeedBookAsync(lang, name, isPublic, owner);
        // Bewusst ohne Funktionswoerter einer Sprache: die Sprachpruefung des Kerns liesse sonst nur eine Zielsprache zu.
        await _kit.SeedLineAsync(book, "001.001", comment: "Plan A: Nf3 Bb5");
        await _kit.SeedLineAsync(book, "001.002", comment: "Plan B: Qd4 Rc1");
        return book;
    }

    private async Task<CourseTranslationJob> JobAsync(int id) =>
        await _kit.Db().CourseTranslationJobs.AsNoTracking().SingleAsync(j => j.Id == id);

    private async Task<CourseTranslationJob> AddJobAsync(int bookId, string lang, int? requestedBy,
        CourseTranslationJobStatus status = CourseTranslationJobStatus.Queued, DateTime? createdAt = null)
    {
        var db = _kit.Db();
        var job = new CourseTranslationJob
        {
            BookId = bookId, Language = lang, RequestedByUserId = requestedBy, Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
        db.CourseTranslationJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    // ── Anfordern ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Request_CreatesQueuedJob_WithTheOpenLines_AndWakesTheWorker()
    {
        var book = await CourseAsync();

        var result = await _kit.Jobs().RequestAsync(Alice, false, book.Id, "DE");

        Assert.Equal(CourseTranslationRequestStatus.Created, result.Status);
        Assert.Null(result.Reason);
        Assert.Equal("de", result.Job!.Language);
        Assert.Equal("queued", result.Job.Status);
        Assert.Equal(2, result.Job.LinesTotal);
        Assert.Equal(1, result.Job.QueuePosition);
        Assert.True(result.Job.RequestedByMe);
        Assert.False(result.Job.Automatic);
        var stored = await JobAsync(result.Job.Id);
        Assert.Equal(Alice, stored.RequestedByUserId);
        Assert.True(await _kit.Signal.WaitAsync(TimeSpan.Zero, default));   // geweckt
        Assert.Empty(_kit.Llm.Calls);                                        // angenommen, nicht gerechnet
    }

    [Fact]
    public async Task Request_OpenJobForCourseAndLanguage_IsReturned_AndDoesNotCountAgainstTheLimit()
    {
        var book = await CourseAsync();
        var other = await CourseAsync("Anderer Kurs");
        var first = await _kit.Jobs().RequestAsync(Alice, false, book.Id, "de");
        // Bob hat schon einen eigenen offenen Auftrag — der vorhandene fuer (Kurs, Sprache) geht trotzdem.
        Assert.Equal(CourseTranslationRequestStatus.Created, (await _kit.Jobs().RequestAsync(Bob, false, other.Id, "fr")).Status);

        var second = await _kit.Jobs().RequestAsync(Bob, false, book.Id, "de");

        Assert.Equal(CourseTranslationRequestStatus.Existing, second.Status);
        Assert.Equal(first.Job!.Id, second.Job!.Id);
        Assert.False(second.Job.RequestedByMe);
        Assert.Equal(2, await _kit.Db().CourseTranslationJobs.CountAsync());
    }

    [Fact]
    public async Task Request_UserLimit_OneOpenJobPerUser_ButAdminIsUnlimited()
    {
        var a = await CourseAsync("A");
        var b = await CourseAsync("B");
        var first = await _kit.Jobs().RequestAsync(Alice, false, a.Id, "de");

        var limited = await _kit.Jobs().RequestAsync(Alice, false, b.Id, "de");
        Assert.Equal(CourseTranslationRequestStatus.UserLimit, limited.Status);
        Assert.Equal("user-limit", limited.Reason);
        Assert.Equal(a.Id, limited.OpenJob!.BookId);
        Assert.Equal("A", limited.OpenJob.BookName);
        Assert.Equal(first.Job!.Id, limited.OpenJob.JobId);

        // Ein LAUFENDER zaehlt genauso; ein erledigter nicht.
        var db = _kit.Db();
        var job = await db.CourseTranslationJobs.SingleAsync(j => j.Id == first.Job.Id);
        job.Status = CourseTranslationJobStatus.Running;
        await db.SaveChangesAsync();
        Assert.Equal(CourseTranslationRequestStatus.UserLimit, (await _kit.Jobs().RequestAsync(Alice, false, b.Id, "de")).Status);
        job.Status = CourseTranslationJobStatus.Done;
        await db.SaveChangesAsync();
        Assert.Equal(CourseTranslationRequestStatus.Created, (await _kit.Jobs().RequestAsync(Alice, false, b.Id, "de")).Status);

        Assert.Equal(CourseTranslationRequestStatus.Created, (await _kit.Jobs().RequestAsync(Admin, true, a.Id, "fr")).Status);
        Assert.Equal(CourseTranslationRequestStatus.Created, (await _kit.Jobs().RequestAsync(Admin, true, b.Id, "fr")).Status);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("pt-br")]
    [InlineData("")]
    [InlineData("deutsch")]
    public async Task Request_OnlyTheTwentyFiveUiLanguages(string lang)
    {
        var book = await CourseAsync();
        var result = await _kit.Jobs().RequestAsync(Alice, false, book.Id, lang);
        Assert.Equal(CourseTranslationRequestStatus.UnsupportedLanguage, result.Status);
        Assert.Equal("unsupported-language", result.Reason);
    }

    [Fact]
    public async Task Request_SameLanguageAsTheCourse_IsRefused()
    {
        var book = await CourseAsync(lang: "de");
        var result = await _kit.Jobs().RequestAsync(Alice, false, book.Id, "de");
        Assert.Equal(CourseTranslationRequestStatus.SameLanguage, result.Status);
        Assert.Equal("same-language", result.Reason);
        Assert.False(await _kit.Db().CourseTranslationJobs.AnyAsync());
    }

    [Fact]
    public async Task Request_NothingToTranslate_WhenEverythingIsAlreadyThere_OrThereIsNoText()
    {
        var book = await CourseAsync();
        await _kit.Service().TranslateCourseAsync(book.Id, "de");

        var done = await _kit.Jobs().RequestAsync(Alice, false, book.Id, "de");
        Assert.Equal(CourseTranslationRequestStatus.NothingToTranslate, done.Status);
        Assert.Equal("nothing-to-translate", done.Reason);

        var empty = await _kit.SeedBookAsync("en", "Leer", isPublic: true);
        await _kit.SeedLineAsync(empty, "001.001");
        Assert.Equal(CourseTranslationRequestStatus.NothingToTranslate,
            (await _kit.Jobs().RequestAsync(Alice, false, empty.Id, "de")).Status);
    }

    [Fact]
    public async Task Request_WithoutTextModel_NotConfigured()
    {
        var book = await CourseAsync();
        _kit.Llm.IsConfigured = false;
        var result = await _kit.Jobs().RequestAsync(Alice, false, book.Id, "de");
        Assert.Equal(CourseTranslationRequestStatus.NotConfigured, result.Status);
        Assert.Equal("not-configured", result.Reason);
    }

    [Fact]
    public async Task Request_WithoutCourseAccess_NotFound()
    {
        var book = await CourseAsync(isPublic: false);
        Assert.Equal(CourseTranslationRequestStatus.NotFound,
            (await _kit.Jobs().RequestAsync(Alice, false, book.Id, "de")).Status);
        Assert.Equal(CourseTranslationRequestStatus.NotFound,
            (await _kit.Jobs().RequestAsync(Admin, true, 99_999, "de")).Status);
        // Der Besitzer darf.
        var own = await CourseAsync("Eigener", isPublic: false, owner: Alice);
        Assert.Equal(CourseTranslationRequestStatus.Created,
            (await _kit.Jobs().RequestAsync(Alice, false, own.Id, "de")).Status);
    }

    [Fact]
    public async Task Request_DuringQuietHours_IsAcceptedAndWaits()
    {
        var time = new QuietHoursTests.ManualTime { Now = new DateTimeOffset(2026, 9, 24, 8, 0, 0, TimeSpan.Zero) }; // Do 10:00 Wien
        using var kit = new CourseTranslationTestKit(quiet: new QuietHours(QuietHours.DefaultSpec, "Europe/Vienna", time));
        var book = await kit.SeedBookAsync("en", "Kurs", isPublic: true);
        await kit.SeedLineAsync(book, "001.001", comment: "Plan A: Nf3 Bb5");

        var result = await kit.Jobs().RequestAsync(Alice, false, book.Id, "de");
        Assert.Equal(CourseTranslationRequestStatus.Created, result.Status);

        var overview = await kit.Jobs().GetOverviewAsync(book.Id, Alice, false);
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 15, 0, 0, TimeSpan.Zero), overview!.QuietUntil);  // 17:00 Wien
        Assert.Empty(kit.Llm.Calls);
    }

    // ── Zurueckziehen ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Withdraw_OwnWaitingJob_OthersForbidden_RunningNotWaiting_AdminAny()
    {
        var book = await CourseAsync();
        var mine = await AddJobAsync(book.Id, "de", Alice);
        var bobs = await AddJobAsync(book.Id, "fr", Bob);
        var running = await AddJobAsync(book.Id, "it", Alice, CourseTranslationJobStatus.Running);
        var auto = await AddJobAsync(book.Id, "es", null);

        Assert.Equal(CourseTranslationWithdrawStatus.Forbidden, await _kit.Jobs().WithdrawAsync(Alice, false, book.Id, bobs.Id));
        Assert.Equal(CourseTranslationWithdrawStatus.Forbidden, await _kit.Jobs().WithdrawAsync(Alice, false, book.Id, auto.Id));
        Assert.Equal(CourseTranslationWithdrawStatus.NotWaiting, await _kit.Jobs().WithdrawAsync(Alice, false, book.Id, running.Id));
        Assert.Equal(CourseTranslationWithdrawStatus.NotFound, await _kit.Jobs().WithdrawAsync(Alice, false, book.Id + 1, mine.Id));
        Assert.Equal(CourseTranslationWithdrawStatus.NotFound, await _kit.Jobs().WithdrawAsync(Alice, false, book.Id, 99_999));

        Assert.Equal(CourseTranslationWithdrawStatus.Withdrawn, await _kit.Jobs().WithdrawAsync(Alice, false, book.Id, mine.Id));
        var withdrawn = await JobAsync(mine.Id);
        Assert.Equal(CourseTranslationJobStatus.Cancelled, withdrawn.Status);
        Assert.NotNull(withdrawn.FinishedAt);
        Assert.Equal(CourseTranslationWithdrawStatus.NotWaiting, await _kit.Jobs().WithdrawAsync(Alice, false, book.Id, mine.Id));

        Assert.Equal(CourseTranslationWithdrawStatus.Withdrawn, await _kit.Jobs().WithdrawAsync(Admin, true, book.Id, running.Id));
        Assert.Equal(CourseTranslationWithdrawStatus.Withdrawn, await _kit.Jobs().WithdrawAsync(Admin, true, book.Id, bobs.Id));
        Assert.Equal(CourseTranslationJobStatus.Cancelled, (await JobAsync(running.Id)).Status);
        Assert.Equal(CourseTranslationWithdrawStatus.NotWaiting, await _kit.Jobs().WithdrawAsync(Admin, true, book.Id, running.Id));
    }

    [Fact]
    public async Task Withdraw_WithoutCourseAccess_NotFound()
    {
        var book = await CourseAsync(isPublic: false, owner: Bob);
        var job = await AddJobAsync(book.Id, "de", Alice);   // z. B. Freigabe inzwischen zurueckgenommen
        Assert.Equal(CourseTranslationWithdrawStatus.NotFound, await _kit.Jobs().WithdrawAsync(Alice, false, book.Id, job.Id));
    }

    // ── Quellsprache ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCommentLanguage_OwnerOrAdmin_KeepsTheTranslations()
    {
        var own = await CourseAsync("Eigener", isPublic: true, owner: Alice);
        await _kit.Service().TranslateCourseAsync(own.Id, "de");
        var sets = await _kit.Db().CommentSets.CountAsync();
        Assert.True(sets > 0);

        Assert.Equal(CourseCommentLanguageStatus.Set, await _kit.Jobs().SetCommentLanguageAsync(Alice, false, own.Id, " FR "));
        Assert.Equal("fr", (await _kit.Db().Books.SingleAsync(b => b.Id == own.Id)).CommentLanguage);
        Assert.Equal(sets, await _kit.Db().CommentSets.CountAsync());

        Assert.Equal(CourseCommentLanguageStatus.Forbidden, await _kit.Jobs().SetCommentLanguageAsync(Bob, false, own.Id, "it"));
        Assert.Equal(CourseCommentLanguageStatus.Set, await _kit.Jobs().SetCommentLanguageAsync(Admin, true, own.Id, "und"));
        Assert.Equal(CourseCommentLanguageStatus.Invalid, await _kit.Jobs().SetCommentLanguageAsync(Alice, false, own.Id, "!!"));
        var hidden = await CourseAsync("Privat", isPublic: false);
        Assert.Equal(CourseCommentLanguageStatus.NotFound, await _kit.Jobs().SetCommentLanguageAsync(Alice, false, hidden.Id, "de"));
        Assert.Equal(CourseCommentLanguageStatus.NotFound, await _kit.Jobs().SetCommentLanguageAsync(Admin, true, 99_999, "de"));
    }

    // ── Ansichten ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Overview_LanguagesJobsPositionsAndMyOpenJob()
    {
        var book = await CourseAsync();
        await _kit.Service().TranslateCourseAsync(book.Id, "fr");
        var elsewhere = await CourseAsync("Woanders");
        var old = DateTime.UtcNow.AddHours(-2);
        var auto = await AddJobAsync(book.Id, "de", null, createdAt: old);           // aelter, aber Automatik
        var requested = await AddJobAsync(book.Id, "it", Bob, createdAt: old.AddHours(1));
        var mine = await AddJobAsync(elsewhere.Id, "es", Alice);
        var done = await AddJobAsync(book.Id, "pl", Bob, CourseTranslationJobStatus.Done);

        var dto = (await _kit.Jobs().GetOverviewAsync(book.Id, Alice, false))!;

        Assert.Equal("en", dto.SourceLanguage);
        var fr = Assert.Single(dto.Languages);
        Assert.Equal(("fr", 2, 2), (fr.Language, fr.LinesTranslated, fr.LinesTotal));
        Assert.Equal(new[] { requested.Id, auto.Id, done.Id }, dto.Jobs.Select(j => j.Id));
        Assert.Equal(1, dto.Jobs[0].QueuePosition);                  // angefordert vor Automatik
        Assert.Equal(3, dto.Jobs[1].QueuePosition);                  // Alices Auftrag woanders steht dazwischen
        Assert.True(dto.Jobs[1].Automatic);
        Assert.Null(dto.Jobs[2].QueuePosition);
        Assert.Equal("done", dto.Jobs[2].Status);
        Assert.Equal(mine.Id, dto.MyOpenJob!.JobId);
        Assert.Equal("Woanders", dto.MyOpenJob.BookName);
        Assert.Null(dto.QuietUntil);
        Assert.True(dto.Available);
        Assert.False(dto.CanRequest);                                // eigener Auftrag offen
        Assert.True((await _kit.Jobs().GetOverviewAsync(book.Id, Admin, true))!.CanRequest);
        Assert.True((await _kit.Jobs().GetOverviewAsync(book.Id, Bob, false))!.Jobs[0].RequestedByMe);
    }

    [Fact]
    public async Task Overview_Anonymous_OnlyPublicCourses_AndDeterminesTheSourceLanguageOnce()
    {
        var hidden = await CourseAsync("Privat", isPublic: false);
        Assert.Null(await _kit.Jobs().GetOverviewAsync(hidden.Id, null, false));
        Assert.Null(await _kit.Jobs().GetOverviewAsync(hidden.Id, Alice, false));
        Assert.NotNull(await _kit.Jobs().GetOverviewAsync(hidden.Id, Admin, true));

        var open = await CourseAsync("Offen", lang: null);
        var dto = (await _kit.Jobs().GetOverviewAsync(open.Id, null, false))!;
        Assert.NotNull(dto.SourceLanguage);
        Assert.Equal(dto.SourceLanguage, (await _kit.Db().Books.SingleAsync(b => b.Id == open.Id)).CommentLanguage);
        Assert.Null(dto.MyOpenJob);
        Assert.False(dto.CanRequest);
    }

    [Fact]
    public async Task AdminOverview_RunningFirst_ThenTheQueue_ThenRecent()
    {
        var db = _kit.Db();
        db.AppUsers.Add(new AppUser { Id = Bob, Username = "bob", PasswordHash = "x" });
        await db.SaveChangesAsync();
        var book = await CourseAsync("Buch");
        var old = DateTime.UtcNow.AddHours(-3);
        var auto = await AddJobAsync(book.Id, "de", null, createdAt: old);
        var requested = await AddJobAsync(book.Id, "fr", Bob, createdAt: old.AddHours(1));
        var running = await AddJobAsync(book.Id, "it", null, CourseTranslationJobStatus.Running);
        var failed = await AddJobAsync(book.Id, "es", Bob, CourseTranslationJobStatus.Failed);

        var dto = await _kit.Jobs().GetAdminOverviewAsync();

        Assert.Equal(new[] { running.Id, requested.Id, auto.Id }, dto.Queue.Select(j => j.Id));
        Assert.Equal("bob", dto.Queue[1].RequestedByUsername);
        Assert.Equal("Buch", dto.Queue[1].BookName);
        Assert.Equal(2, dto.Queue[2].QueuePosition);
        Assert.Equal(failed.Id, Assert.Single(dto.Recent).Id);
        Assert.Empty(dto.AutoLanguages);
        Assert.True(dto.Available);
    }

    // ── Nachziehen nach einer Aenderung ──────────────────────────────────────────────────────────

    [Fact]
    public async Task EnqueueRefresh_OneAutomaticJobPerLanguageWithSets_AWaitingOneSuffices()
    {
        var book = await CourseAsync();
        await _kit.Service().TranslateCourseAsync(book.Id, "de");
        await _kit.Service().TranslateCourseAsync(book.Id, "fr");
        var bare = await CourseAsync("Ohne Uebersetzung");

        Assert.Equal(0, await _kit.Jobs().EnqueueRefreshAsync(bare.Id));
        Assert.Equal(2, await _kit.Jobs().EnqueueRefreshAsync(book.Id));
        Assert.Equal(0, await _kit.Jobs().EnqueueRefreshAsync(book.Id));    // warten schon
        var jobs = await _kit.Db().CourseTranslationJobs.AsNoTracking().OrderBy(j => j.Language).ToListAsync();
        Assert.Equal(new[] { "de", "fr" }, jobs.Select(j => j.Language));
        Assert.All(jobs, j => Assert.Null(j.RequestedByUserId));

        // Ein LAUFENDER deckt eine Aenderung nicht ab (er hat seine Arbeit vorher bestimmt).
        var db = _kit.Db();
        var de = await db.CourseTranslationJobs.SingleAsync(j => j.Language == "de");
        de.Status = CourseTranslationJobStatus.Running;
        await db.SaveChangesAsync();
        Assert.Equal(1, await _kit.Jobs().EnqueueRefreshAsync(book.Id));
    }

    [Fact]
    public async Task RenameChapter_And_AddLines_EnqueueTheTranslations()
    {
        var book = await _kit.SeedBookAsync("en", "Kurs", isPublic: false, ownerUserId: Alice);
        await _kit.SeedLineAsync(book, "001.001", chapter: "Kapitel Eins", comment: "Plan A: Nf3 Bb5");
        await _kit.Service().TranslateCourseAsync(book.Id, "de");

        var authoring = new CourseAuthoringService(_kit.Db(), _kit.Jobs());
        await authoring.RenameChapterAsync(Alice, book.Id,
            new RenameCourseChapterDto { Chapter = "Kapitel Eins", NewName = "Kapitel Zwei" }, isAdmin: false);
        var job = Assert.Single(await _kit.Db().CourseTranslationJobs.AsNoTracking().ToListAsync());
        Assert.Equal(("de", CourseTranslationJobStatus.Queued), (job.Language, job.Status));

        var db = _kit.Db();
        (await db.CourseTranslationJobs.SingleAsync()).Status = CourseTranslationJobStatus.Done;
        await db.SaveChangesAsync();
        await new CourseAuthoringService(_kit.Db(), _kit.Jobs()).AddLinesAsync(Alice, book.Id,
            new AddCourseLinesDto { Chapter = "Kapitel Zwei", Text = "8/8/8/4k3/8/8/8/4K3 w - - 0 1 | Neue Stellung" },
            isAdmin: false);
        Assert.Equal(2, await _kit.Db().CourseTranslationJobs.CountAsync());
    }

    [Fact]
    public async Task Import_ChangedCourseWithTranslations_EnqueuesThem_UnchangedDoesNot()
    {
        // Kurs-Linie ab einer Stellung (in der Grundstellung ohne Trainingsmarker legte der Import keine Linie an).
        const string fen = "rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2";
        var one = "[Event \"X\"]\n[Round \"1\"]\n[FEN \"" + fen + "\"]\n\n{Einleitung eins} 2. Nf3 Nc6 3. Bb5 a6 *\n";
        await new PgnImportService(_kit.Db(), null, _kit.Jobs()).ImportFileAsync("kurs.pgn", one, CancellationToken.None);
        var bookId = (await _kit.Db().Books.SingleAsync(b => b.FileName == "kurs.pgn")).Id;
        Assert.Equal(1, await _kit.Db().BookPuzzles.CountAsync(bp => bp.BookId == bookId));
        Assert.False(await _kit.Db().CourseTranslationJobs.AnyAsync());       // noch keine Uebersetzung → nichts

        var db = _kit.Db();
        (await db.Books.SingleAsync(b => b.Id == bookId)).CommentLanguage = "en";
        await db.SaveChangesAsync();
        await _kit.Service().TranslateCourseAsync(bookId, "de");

        // Derselbe Stand noch einmal: nichts geaendert, nichts eingereiht.
        await new PgnImportService(_kit.Db(), null, _kit.Jobs()).ImportFileAsync("kurs.pgn", one, CancellationToken.None);
        Assert.False(await _kit.Db().CourseTranslationJobs.AnyAsync());

        var two = "[Event \"X\"]\n[Round \"2\"]\n[FEN \"" + fen + "\"]\n\n{Einleitung zwei} 2. Bc4 Bc5 3. Qh5 Nf6 *\n";
        await new PgnImportService(_kit.Db(), null, _kit.Jobs()).ImportFileAsync("kurs.pgn", two, CancellationToken.None, partial: true);
        var job = Assert.Single(await _kit.Db().CourseTranslationJobs.AsNoTracking().ToListAsync());
        Assert.Equal((bookId, "de"), (job.BookId, job.Language));
        Assert.Null(job.RequestedByUserId);
    }

    [Fact]
    public async Task DeleteAccount_CancelsTheOpenJobsOfTheUser()
    {
        var db = _kit.Db();
        var user = new AppUser { Username = "weg", PasswordHash = BCrypt.Net.BCrypt.HashPassword("pw"), Profile = new UserProfile() };
        db.AppUsers.Add(user);
        await db.SaveChangesAsync();
        var book = await CourseAsync();
        var waiting = await AddJobAsync(book.Id, "de", user.Id);
        var running = await AddJobAsync(book.Id, "fr", user.Id, CourseTranslationJobStatus.Running);
        var done = await AddJobAsync(book.Id, "it", user.Id, CourseTranslationJobStatus.Done);
        var foreign = await AddJobAsync(book.Id, "es", Bob);

        await TestServices.Profile(_kit.Db(), new NoOpTaskQueue()).DeleteAccountAsync(user.Id, "pw");

        Assert.Equal(CourseTranslationJobStatus.Cancelled, (await JobAsync(waiting.Id)).Status);
        Assert.Equal("account deleted", (await JobAsync(running.Id)).LastError);
        Assert.Equal(CourseTranslationJobStatus.Cancelled, (await JobAsync(running.Id)).Status);
        Assert.Equal(CourseTranslationJobStatus.Done, (await JobAsync(done.Id)).Status);
        Assert.Equal(CourseTranslationJobStatus.Queued, (await JobAsync(foreign.Id)).Status);
    }

    // ── Controller ───────────────────────────────────────────────────────────────────────────────

    private CourseTranslationController Controller(int? userId, bool admin = false)
    {
        var claims = new List<Claim>();
        if (userId is int id) claims.Add(new Claim(ClaimTypes.NameIdentifier, id.ToString()));
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        return new CourseTranslationController(_kit.Jobs())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(userId is null ? new ClaimsIdentity() : new ClaimsIdentity(claims, "t")),
                },
            },
        };
    }

    private static int? StatusOf(IActionResult r) => (r as IStatusCodeActionResult)?.StatusCode;

    [Fact]
    public async Task Controller_MapsTheOutcomesToHttp()
    {
        var book = await CourseAsync();
        var other = await CourseAsync("Anderer");

        Assert.Equal(202, StatusOf(await Controller(Alice).RequestTranslation(book.Id, new() { Language = "de" }, default)));
        Assert.Equal(200, StatusOf(await Controller(Bob).RequestTranslation(book.Id, new() { Language = "de" }, default)));
        Assert.Equal(409, StatusOf(await Controller(Alice).RequestTranslation(other.Id, new() { Language = "de" }, default)));
        Assert.Equal(400, StatusOf(await Controller(Alice).RequestTranslation(book.Id, new() { Language = "en" }, default)));
        Assert.Equal(400, StatusOf(await Controller(Alice).RequestTranslation(book.Id, new() { Language = "xx" }, default)));
        Assert.Equal(404, StatusOf(await Controller(Alice).RequestTranslation(99_999, new() { Language = "de" }, default)));

        var jobId = (await _kit.Db().CourseTranslationJobs.SingleAsync()).Id;
        Assert.Equal(403, StatusOf(await Controller(Bob).WithdrawTranslation(book.Id, jobId, default)));
        Assert.Equal(204, StatusOf(await Controller(Alice).WithdrawTranslation(book.Id, jobId, default)));
        Assert.Equal(409, StatusOf(await Controller(Alice).WithdrawTranslation(book.Id, jobId, default)));

        var anon = await Controller(null).GetTranslations(book.Id, default);
        Assert.IsType<OkObjectResult>(anon.Result);
        var hidden = await CourseAsync("Privat", isPublic: false);
        Assert.IsType<NotFoundObjectResult>((await Controller(null).GetTranslations(hidden.Id, default)).Result);

        Assert.Equal(403, StatusOf(await Controller(Alice).SetCommentLanguage(book.Id, new() { Language = "de" }, default)));
        Assert.Equal(200, StatusOf(await Controller(Admin, admin: true).SetCommentLanguage(book.Id, new() { Language = "de" }, default)));

        _kit.Llm.IsConfigured = false;
        Assert.Equal(503, StatusOf(await Controller(Admin, admin: true).RequestTranslation(other.Id, new() { Language = "fr" }, default)));
    }
}
