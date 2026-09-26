using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der Hintergrunddienst der Kurs-Uebersetzung (Plan „Kurs-Kommentare mehrsprachig", Abschnitt 6, Stufe B): Reihenfolge
/// (angeforderte vor Automatik, ein Auftrag nach dem anderen), Automatik (aus bei leerer Einstellung, zuletzt benutzte
/// Kurse zuerst, gleiche Quellsprache uebersprungen), Sperrzeiten (kein Start, Abbruch mitten im Lauf → wieder
/// wartend), Abbruch durch Zurueckziehen und durch Vorrang, Fortschritt am Auftrag. Die Uhr ist gestellt
/// (<see cref="QuietHoursTests.ManualTime"/>), das Modell ein Doppelgaenger — nie die echte Spark.
/// </summary>
public class CourseTranslationWorkerTests
{
    private const int Alice = 7, Bob = 8, Admin = 1;

    /// <summary>September 2026: Wien = UTC+2.</summary>
    private static DateTimeOffset Utc(int day, int hour, int minute = 0) => new(2026, 9, day, hour, minute, 0, TimeSpan.Zero);

    /// <summary>Fr 25.09. 14:30 Wien — frei.</summary>
    private static readonly DateTimeOffset FridayAfternoon = Utc(25, 12, 30);

    /// <summary>Ein Container mit den echten Sperrzeiten (Vorgabe, Wien) an einer gestellten Uhr.</summary>
    private static CourseTranslationTestKit Kit(QuietHoursTests.ManualTime time, string? autoLanguages = null)
        => new(parallel: 1, autoLanguages, new QuietHours(QuietHours.DefaultSpec, "Europe/Vienna", time));

    private static QuietHoursTests.ManualTime Clock(DateTimeOffset now) => new() { Now = now };

    private static async Task<Book> CourseAsync(CourseTranslationTestKit kit, string name, int lines = 2,
        string? lang = "en")
    {
        var book = await kit.SeedBookAsync(lang, name, isPublic: true);
        for (var i = 1; i <= lines; i++)
            await kit.SeedLineAsync(book, $"001.{i:000}", comment: $"Plan {name} {i}: Nf3 Bb5");
        return book;
    }

    private static async Task<CourseTranslationJob> AddJobAsync(CourseTranslationTestKit kit, int bookId, string lang,
        int? requestedBy, DateTime? createdAt = null, CourseTranslationJobStatus status = CourseTranslationJobStatus.Queued)
    {
        var db = kit.Db();
        var job = new CourseTranslationJob
        {
            BookId = bookId, Language = lang, RequestedByUserId = requestedBy, Status = status,
            CreatedAt = createdAt ?? DateTime.UtcNow,
        };
        db.CourseTranslationJobs.Add(job);
        await db.SaveChangesAsync();
        return job;
    }

    private static Task<CourseTranslationJob> JobAsync(CourseTranslationTestKit kit, int id) =>
        kit.Db().CourseTranslationJobs.AsNoTracking().SingleAsync(j => j.Id == id);

    private static async Task UsedAsync(CourseTranslationTestKit kit, Book book, DateTime at)
    {
        var db = kit.Db();
        var line = await db.BookPuzzles.FirstAsync(bp => bp.BookId == book.Id);
        db.CourseAttempts.Add(new CourseAttempt { UserId = Alice, BookId = book.Id, BookPuzzleId = line.Id, AttemptedAt = at });
        await db.SaveChangesAsync();
    }

    private static async Task<T> WithTimeout<T>(Task<T> task, int ms = 10_000)
        => await task.WaitAsync(TimeSpan.FromMilliseconds(ms));

    // ── Reihenfolge ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Step_RequestedBeforeAutomatic_OldestFirst_OneJobPerStep()
    {
        using var kit = Kit(Clock(FridayAfternoon));
        var a = await CourseAsync(kit, "A");
        var b = await CourseAsync(kit, "B");
        var c = await CourseAsync(kit, "C");
        var now = DateTime.UtcNow;
        var auto = await AddJobAsync(kit, a.Id, "de", null, now.AddHours(-3));
        var bob = await AddJobAsync(kit, b.Id, "de", Bob, now.AddHours(-1));
        var alice = await AddJobAsync(kit, c.Id, "de", Alice, now.AddHours(-2));
        var worker = kit.Worker();

        Assert.Equal(TimeSpan.Zero, await worker.StepAsync(default));
        Assert.Equal(CourseTranslationJobStatus.Done, (await JobAsync(kit, alice.Id)).Status);
        Assert.Equal(CourseTranslationJobStatus.Queued, (await JobAsync(kit, bob.Id)).Status);
        Assert.Equal(CourseTranslationJobStatus.Queued, (await JobAsync(kit, auto.Id)).Status);

        await worker.StepAsync(default);
        Assert.Equal(CourseTranslationJobStatus.Done, (await JobAsync(kit, bob.Id)).Status);
        Assert.Equal(CourseTranslationJobStatus.Queued, (await JobAsync(kit, auto.Id)).Status);

        await worker.StepAsync(default);
        var done = await JobAsync(kit, auto.Id);
        Assert.Equal(CourseTranslationJobStatus.Done, done.Status);
        Assert.Equal((2, 2, 0), (done.LinesTotal, done.LinesDone, done.LinesFailed));
        Assert.NotNull(done.StartedAt);
        Assert.NotNull(done.FinishedAt);

        Assert.Equal(CourseTranslationWorker.IdlePoll, await worker.StepAsync(default));   // nichts mehr, Automatik aus
        var firstLine = await kit.Db().BookPuzzles.FirstAsync(bp => bp.BookId == a.Id);
        // Figurenbuchstaben der Zielsprache (PieceLetters) — der Doppelgaenger reicht den Text ja nur durch.
        Assert.Equal("DE:Plan A 1: Sf3 Lb5", (await kit.TextsAsync(firstLine.Id, "de"))[CourseTextSlots.Comment].Text);
    }

    [Fact]
    public async Task Step_AutomaticIsOff_WhenTheSettingIsEmpty()
    {
        using var kit = Kit(Clock(FridayAfternoon), autoLanguages: "");
        await CourseAsync(kit, "A");

        Assert.Equal(CourseTranslationWorker.IdlePoll, await kit.Worker().StepAsync(default));
        Assert.False(await kit.Db().CourseTranslationJobs.AnyAsync());
        Assert.Empty(kit.Llm.Calls);
    }

    [Fact]
    public async Task Step_Automatic_RecentlyUsedCoursesFirst_SkipsCoursesInThatLanguage_OneJobAtATime()
    {
        using var kit = Kit(Clock(FridayAfternoon), autoLanguages: "de");
        var old = await CourseAsync(kit, "Alt");
        var fresh = await CourseAsync(kit, "Frisch");
        var german = await CourseAsync(kit, "Deutsch", lang: "de");
        var never = await CourseAsync(kit, "Nie");
        await UsedAsync(kit, old, DateTime.UtcNow.AddDays(-10));
        await UsedAsync(kit, fresh, DateTime.UtcNow.AddHours(-1));
        await UsedAsync(kit, german, DateTime.UtcNow);
        var worker = kit.Worker();

        var order = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(TimeSpan.Zero, await worker.StepAsync(default));
            var jobs = await kit.Db().CourseTranslationJobs.AsNoTracking().OrderBy(j => j.Id).ToListAsync();
            Assert.Equal(i + 1, jobs.Count);                               // EIN Auftrag je Durchgang
            Assert.All(jobs, j => Assert.Equal(("de", CourseTranslationJobStatus.Done, (int?)null),
                (j.Language, j.Status, j.RequestedByUserId)));
            order.Add(jobs[^1].BookId);
        }
        Assert.Equal(new[] { fresh.Id, old.Id, never.Id }, order);
        Assert.Equal(CourseTranslationWorker.IdlePoll, await worker.StepAsync(default));
        Assert.False(await kit.Db().CourseTranslationJobs.AnyAsync(j => j.BookId == german.Id));
    }

    [Fact]
    public async Task Step_Automatic_TwoLanguages_TheMostRecentCourseGetsBothFirst()
    {
        using var kit = Kit(Clock(FridayAfternoon), autoLanguages: "de,en,xx");
        var old = await CourseAsync(kit, "Alt", lang: "fr");
        var fresh = await CourseAsync(kit, "Frisch", lang: "fr");
        await UsedAsync(kit, old, DateTime.UtcNow.AddDays(-3));
        await UsedAsync(kit, fresh, DateTime.UtcNow.AddHours(-1));
        var worker = kit.Worker();

        for (var i = 0; i < 4; i++) await worker.StepAsync(default);

        var jobs = await kit.Db().CourseTranslationJobs.AsNoTracking().OrderBy(j => j.Id).ToListAsync();
        Assert.Equal(new[] { (fresh.Id, "de"), (fresh.Id, "en"), (old.Id, "de"), (old.Id, "en") },
            jobs.Select(j => (j.BookId, j.Language)));
        Assert.Equal(new[] { "de", "en" }, kit.Jobs().AutoLanguages);     // „xx" ist keine der 25
    }

    [Fact]
    public async Task Step_WithoutTextModel_SleepsAndLeavesTheQueueAlone()
    {
        using var kit = Kit(Clock(FridayAfternoon));
        var book = await CourseAsync(kit, "A");
        var job = await AddJobAsync(kit, book.Id, "de", Alice);
        kit.Llm.IsConfigured = false;

        Assert.Equal(CourseTranslationWorker.NotConfiguredPoll, await kit.Worker().StepAsync(default));
        Assert.Equal(CourseTranslationJobStatus.Queued, (await JobAsync(kit, job.Id)).Status);
    }

    // ── Sperrzeiten ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Step_DuringQuietHours_NoJobStarts_SleepsAtMostTenMinutes()
    {
        var time = Clock(Utc(24, 8));                                    // Do 10:00 Wien, gesperrt bis 17:00
        using var kit = Kit(time);
        var book = await CourseAsync(kit, "A");
        var job = await AddJobAsync(kit, book.Id, "de", Alice);
        var worker = kit.Worker();

        Assert.Equal(CourseTranslationWorker.MaxQuietSleep, await worker.StepAsync(default));
        time.Now = Utc(24, 14, 55);                                      // Do 16:55 Wien
        Assert.Equal(TimeSpan.FromMinutes(5), await worker.StepAsync(default));
        Assert.Equal(CourseTranslationJobStatus.Queued, (await JobAsync(kit, job.Id)).Status);
        Assert.Empty(kit.Llm.Calls);

        time.Now = Utc(24, 15, 0);                                       // 17:00 — frei
        Assert.Equal(TimeSpan.Zero, await worker.StepAsync(default));
        Assert.Equal(CourseTranslationJobStatus.Done, (await JobAsync(kit, job.Id)).Status);
    }

    [Fact]
    public async Task Step_QuietHoursBeginMidRun_TheJobGoesBackToWaiting_AndNothingIsWritten()
    {
        var time = Clock(FridayAfternoon);
        using var kit = Kit(time);
        var book = await CourseAsync(kit, "A", lines: 3);
        var job = await AddJobAsync(kit, book.Id, "de", Alice);
        kit.Llm.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = kit.Worker(quietCheck: TimeSpan.FromMilliseconds(20));

        var step = worker.StepAsync(default);
        await kit.Llm.WaitForCallsAsync();
        Assert.Equal(CourseTranslationJobStatus.Running, (await JobAsync(kit, job.Id)).Status);
        Assert.Equal(3, (await JobAsync(kit, job.Id)).LinesTotal);           // Zwischenstand gleich zu Beginn
        time.Now = Utc(28, 7);                                               // Mo 09:00 Wien — Sperrzeit

        Assert.Equal(TimeSpan.Zero, await WithTimeout(step));
        var stored = await JobAsync(kit, job.Id);
        Assert.Equal(CourseTranslationJobStatus.Queued, stored.Status);
        Assert.Null(stored.FinishedAt);
        Assert.False(await kit.Db().CommentSets.AnyAsync());
        Assert.Null(kit.Signal.RunningJobId);

        // Der naechste Durchgang schlaeft, bis die Sperrzeit vorbei ist — dann laeuft der Auftrag zu Ende.
        Assert.Equal(CourseTranslationWorker.MaxQuietSleep, await worker.StepAsync(default));
        kit.Llm.Hold = null;
        time.Now = Utc(28, 15);
        await worker.StepAsync(default);
        Assert.Equal(CourseTranslationJobStatus.Done, (await JobAsync(kit, job.Id)).Status);
    }

    // ── Abbruch ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Step_AdminWithdrawsTheRunningJob_ItStaysCancelled()
    {
        using var kit = Kit(Clock(FridayAfternoon));
        var book = await CourseAsync(kit, "A");
        var job = await AddJobAsync(kit, book.Id, "de", Alice);
        kit.Llm.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var step = kit.Worker().StepAsync(default);
        await kit.Llm.WaitForCallsAsync();
        Assert.Equal(job.Id, kit.Signal.RunningJobId);
        Assert.Equal(CourseTranslationWithdrawStatus.Withdrawn, await kit.Jobs().WithdrawAsync(Admin, true, book.Id, job.Id));

        await WithTimeout(step);
        Assert.Equal(CourseTranslationJobStatus.Cancelled, (await JobAsync(kit, job.Id)).Status);
        Assert.False(await kit.Db().CommentSets.AnyAsync());
    }

    [Fact]
    public async Task Step_ANewRequestPreemptsARunningAutomaticJob_WhichGoesBackToWaiting()
    {
        using var kit = Kit(Clock(FridayAfternoon));
        var a = await CourseAsync(kit, "A");
        var b = await CourseAsync(kit, "B");
        var auto = await AddJobAsync(kit, a.Id, "de", null);
        kit.Llm.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = kit.Worker();

        var step = worker.StepAsync(default);
        await kit.Llm.WaitForCallsAsync();
        var requested = await kit.Jobs().RequestAsync(Alice, false, b.Id, "de");
        Assert.Equal(CourseTranslationRequestStatus.Created, requested.Status);

        await WithTimeout(step);
        Assert.Equal(CourseTranslationJobStatus.Queued, (await JobAsync(kit, auto.Id)).Status);

        kit.Llm.Hold = null;
        await worker.StepAsync(default);
        Assert.Equal(CourseTranslationJobStatus.Done, (await JobAsync(kit, requested.Job!.Id)).Status);
        Assert.Equal(CourseTranslationJobStatus.Queued, (await JobAsync(kit, auto.Id)).Status);
        await worker.StepAsync(default);
        Assert.Equal(CourseTranslationJobStatus.Done, (await JobAsync(kit, auto.Id)).Status);
    }

    [Fact]
    public async Task Step_ARequestDoesNotPreemptAnotherRequest()
    {
        using var kit = Kit(Clock(FridayAfternoon));
        var a = await CourseAsync(kit, "A");
        var b = await CourseAsync(kit, "B");
        var first = await AddJobAsync(kit, a.Id, "de", Bob);
        kit.Llm.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var step = kit.Worker().StepAsync(default);
        await kit.Llm.WaitForCallsAsync();
        await kit.Jobs().RequestAsync(Alice, false, b.Id, "de");
        await Task.Delay(100);
        Assert.False(step.IsCompleted);
        Assert.Equal(CourseTranslationJobStatus.Running, (await JobAsync(kit, first.Id)).Status);

        kit.Llm.Hold.SetResult();
        await WithTimeout(step);
        Assert.Equal(CourseTranslationJobStatus.Done, (await JobAsync(kit, first.Id)).Status);
    }

    [Fact]
    public async Task Run_JobCancelledMeanwhile_AccountDeleted_StopsAtTheNextProgressReport()
    {
        using var kit = Kit(Clock(FridayAfternoon));
        var book = await CourseAsync(kit, "A", lines: 12);
        var job = await AddJobAsync(kit, book.Id, "de", Alice);
        kit.Llm.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var step = kit.Worker().StepAsync(default);
        await kit.Llm.WaitForCallsAsync();
        var db = kit.Db();
        var row = await db.CourseTranslationJobs.SingleAsync(j => j.Id == job.Id);
        row.Status = CourseTranslationJobStatus.Cancelled;                     // wie beim Loeschen des Kontos
        row.LastError = "account deleted";
        await db.SaveChangesAsync();
        kit.Llm.Hold.SetResult();

        await WithTimeout(step);
        var stored = await JobAsync(kit, job.Id);
        Assert.Equal(CourseTranslationJobStatus.Cancelled, stored.Status);
        Assert.Equal("account deleted", stored.LastError);
        // Nach dem ersten Zwischenstand (10 Linien) ist Schluss — die letzten beiden Linien bleiben offen.
        Assert.True(await kit.Db().CommentSets.CountAsync() <= CourseTranslationService.ProgressEvery);
    }

    [Fact]
    public async Task Step_WritesTheProgressIntoTheJob_AndCountsFailedLines()
    {
        using var kit = Kit(Clock(FridayAfternoon));
        var book = await CourseAsync(kit, "A", lines: 12);
        var job = await AddJobAsync(kit, book.Id, "de", Alice);
        kit.Llm.FailIfPromptContains = "Plan A 3:";

        await kit.Worker().StepAsync(default);

        var stored = await JobAsync(kit, job.Id);
        Assert.Equal(CourseTranslationJobStatus.Done, stored.Status);
        Assert.Equal((12, 11, 1), (stored.LinesTotal, stored.LinesDone, stored.LinesFailed));
        Assert.Equal("1 lines failed", stored.LastError);
    }

    [Fact]
    public async Task Step_EveryLineFails_TheJobFails()
    {
        using var kit = Kit(Clock(FridayAfternoon));
        var book = await CourseAsync(kit, "A");
        var job = await AddJobAsync(kit, book.Id, "de", Alice);
        kit.Llm.Fail = true;

        await kit.Worker().StepAsync(default);

        var stored = await JobAsync(kit, job.Id);
        Assert.Equal(CourseTranslationJobStatus.Failed, stored.Status);
        Assert.Equal((2, 0, 2), (stored.LinesTotal, stored.LinesDone, stored.LinesFailed));
    }

    [Fact]
    public async Task Step_TargetTurnsOutToBeTheSourceLanguage_TheJobIsDropped()
    {
        using var kit = Kit(Clock(FridayAfternoon));
        var book = await CourseAsync(kit, "A");
        var job = await AddJobAsync(kit, book.Id, "de", Alice);
        Assert.Equal(CourseCommentLanguageStatus.Set, await kit.Jobs().SetCommentLanguageAsync(Admin, true, book.Id, "de"));

        await kit.Worker().StepAsync(default);

        var stored = await JobAsync(kit, job.Id);
        Assert.Equal((CourseTranslationJobStatus.Cancelled, "same-language"), (stored.Status, stored.LastError));
        Assert.Empty(kit.Llm.Calls);
    }

    [Fact]
    public async Task Startup_RunningJobsGoBackToWaiting()
    {
        using var kit = Kit(Clock(FridayAfternoon));
        var book = await CourseAsync(kit, "A");
        var stuck = await AddJobAsync(kit, book.Id, "de", Alice, status: CourseTranslationJobStatus.Running);
        var done = await AddJobAsync(kit, book.Id, "fr", Alice, status: CourseTranslationJobStatus.Done);

        Assert.Equal(1, await kit.Jobs().RequeueInterruptedAsync());
        Assert.Equal(CourseTranslationJobStatus.Queued, (await JobAsync(kit, stuck.Id)).Status);
        Assert.Equal(CourseTranslationJobStatus.Done, (await JobAsync(kit, done.Id)).Status);
    }
}
