using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Puzzles;

namespace RookHub.Api.Services.Courses;

/// <summary>
/// Woodpecker-Training über einen oder mehrere Kurse (Regeln fest in <see cref="WoodpeckerRules"/>).
///
/// <para><b>Zyklus 1</b> geht die Kurse in Plan-Reihenfolge und je Kurs in Lesereihenfolge durch; jede bearbeitete
/// Aufgabe kommt in den Satz. Er zählt auch, was der Nutzer seit dem (rückdatierbaren) Start im normalen Kurs-Löser
/// an diesen Kursen gemacht hat (<see cref="ImportCourseAttemptsAsync"/>) — wer schon 250 Aufgaben im Kurs gelöst hat,
/// datiert den Start zurück und hat sie im Satz. Zyklus 1 endet auf Knopfdruck oder wenn alle Aufgaben der Kurse im
/// Satz sind.</para>
/// <para><b>Ab Zyklus 2</b> kommt der Satz in einer je Zyklus gemischten, aber von Beginn an festen Reihenfolge
/// (<see cref="WoodpeckerRules.ShuffleKey"/>: Neu laden bringt dieselbe Aufgabe), je Aufgabe zählt der erste Versuch;
/// der Zyklus endet, wenn alle Aufgaben bearbeitet sind.</para>
/// <para><b>Frist um, Zyklus nicht fertig</b> (1.007.0, Nutzerwunsch 22.11.2026; bis dahin endete Zyklus 1 still an der
/// Frist und ab Zyklus 2 lief er unbegrenzt weiter): der Plan meldet <see cref="WoodpeckerPlanDto.DeadlinePassed"/>, die
/// App fragt — <see cref="ExtendCycleAsync"/> setzt eine neue Frist, <see cref="FinishCycleAsync"/> schließt ab (Offenes
/// bleibt unbearbeitet). Bis dahin läuft der Zyklus weiter und Versuche zählen (Offline-Warteschlange!).</para>
/// </summary>
public class WoodpeckerService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public WoodpeckerService(AppDbContext db, TimeProvider? clock = null)
    {
        _db = db;
        _clock = clock ?? TimeProvider.System;
    }

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    public async Task<List<WoodpeckerPlanDto>> ListAsync(int userId, CancellationToken ct = default, int utcOffsetMinutes = 0)
    {
        var plans = await _db.WoodpeckerPlans.Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Include(p => p.Books).Include(p => p.Cycles)
            .ToListAsync(ct);
        var result = new List<WoodpeckerPlanDto>(plans.Count);
        foreach (var plan in plans)
        {
            await RefreshAsync(plan, userId, ct);
            result.Add(await BuildDtoAsync(plan, utcOffsetMinutes, ct));
        }
        return result;
    }

    public async Task<WoodpeckerPlanDto> GetAsync(int userId, int planId, CancellationToken ct = default, int utcOffsetMinutes = 0)
    {
        var plan = await LoadAsync(userId, planId, ct);
        await RefreshAsync(plan, userId, ct);
        return await BuildDtoAsync(plan, utcOffsetMinutes, ct);
    }

    public async Task<WoodpeckerPlanDto> CreateAsync(int userId, bool isAdmin, CreateWoodpeckerPlanDto dto,
        CancellationToken ct = default, int utcOffsetMinutes = 0)
    {
        var bookIds = dto.BookIds.Distinct().ToList();
        if (bookIds.Count == 0) throw new DomainValidationException("Choose at least one course.");
        var now = Now;
        var startedAt = NormalizeUtc(dto.StartedAt ?? now);
        if (startedAt > now) throw new DomainValidationException("The start cannot be in the future.");

        var books = await _db.Books.Where(b => bookIds.Contains(b.Id))
            .Select(b => new { b.Id, b.DisplayName, b.FileName, b.IsCalculation })
            .ToListAsync(ct);
        foreach (var id in bookIds)
        {
            var book = books.FirstOrDefault(b => b.Id == id);
            if (book == null || !await CourseAccess.CanAccessAsync(_db, userId, id, isAdmin, ct))
                throw new NotFoundException("Course not found.");
            if (book.IsCalculation)
                throw new DomainValidationException("Calculation courses have no solutions and cannot be used.");
        }

        var names = bookIds.Select(id => books.First(b => b.Id == id))
            .Select(b => string.IsNullOrWhiteSpace(b.DisplayName) ? b.FileName : b.DisplayName);
        var name = string.Join(" + ", names);
        if (name.Length > 200) name = name[..197] + "...";

        var plan = new WoodpeckerPlan
        {
            UserId = userId,
            Name = name,
            CreatedAt = now,
            Books = bookIds.Select((id, i) => new WoodpeckerPlanBook { BookId = id, Position = i }).ToList(),
            Cycles = { new WoodpeckerCycle { Number = 1, StartedAt = startedAt } },
        };

        // EIN aktives Training je Nutzer (0.958.0): Prüfen, bisheriges archivieren und neues anlegen in EINER Transaktion
        // (in der Execution-Strategy, docs/konventionen.md). Die Sperre auf die Nutzerzeile reiht zwei gleichzeitige
        // Anlagen hintereinander — sonst sähen beide „keins da“. InMemory kennt weder Transaktion noch Sperre.
        if (_db.Database.IsRelational())
        {
            var strategy = _db.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync(ct);
                await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT Id FROM AppUsers WHERE Id = {userId} FOR UPDATE", ct);
                await ArchiveCurrentAndAddAsync(userId, plan, dto.ArchiveCurrent, now, ct);
                await tx.CommitAsync(ct);
            });
        }
        else
        {
            await ArchiveCurrentAndAddAsync(userId, plan, dto.ArchiveCurrent, now, ct);
        }

        await RefreshAsync(plan, userId, ct);
        return await BuildDtoAsync(plan, utcOffsetMinutes, ct);
    }

    /// <summary>Wiederholbarer Kern von <see cref="CreateAsync"/>: liest die nicht archivierten Trainings neu, bricht
    /// ohne <paramref name="archiveCurrent"/> mit 409 ab, archiviert sonst ALLE (Altbestand kann mehrere haben) und legt
    /// das neue an — ein <c>SaveChanges</c>.</summary>
    private async Task ArchiveCurrentAndAddAsync(int userId, WoodpeckerPlan plan, bool archiveCurrent, DateTime now,
        CancellationToken ct)
    {
        var active = await _db.WoodpeckerPlans.Where(p => p.UserId == userId && p.ArchivedAt == null && p.Id != plan.Id)
            .ToListAsync(ct);
        if (active.Count > 0 && !archiveCurrent)
            throw new ConflictException("You already have a Woodpecker training. Archive it to start a new one.")
            { Code = ApiErrorCodes.WoodpeckerActiveExists };
        foreach (var old in active) old.ArchivedAt = now;
        if (_db.Entry(plan).State == EntityState.Detached) _db.WoodpeckerPlans.Add(plan);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Training ins Archiv legen — jederzeit, auch mitten im Zyklus. Es bleibt lesbar (Zyklen, Statistik),
    /// ändert sich aber nicht mehr (keine Fristen, kein Kurs-Import). Schon archiviert → unverändert.</summary>
    public async Task<WoodpeckerPlanDto> ArchiveAsync(int userId, int planId, CancellationToken ct = default, int utcOffsetMinutes = 0)
    {
        var plan = await LoadAsync(userId, planId, ct);
        if (plan.ArchivedAt == null)
        {
            // Erst den Stand nachziehen (Frist von Zyklus 1 vorbei, Kurs-Versuche) — eingefroren wird, was bis jetzt galt.
            await RefreshAsync(plan, userId, ct);
            plan.ArchivedAt = Now;
            await _db.SaveChangesAsync(ct);
        }
        return await BuildDtoAsync(plan, utcOffsetMinutes, ct);
    }

    /// <summary>Archiviertes Training wieder aktiv machen — nur, wenn es kein anderes nicht archiviertes gibt (409).</summary>
    public async Task<WoodpeckerPlanDto> UnarchiveAsync(int userId, int planId, CancellationToken ct = default, int utcOffsetMinutes = 0)
    {
        var plan = await LoadAsync(userId, planId, ct);
        if (plan.ArchivedAt != null)
        {
            if (await _db.WoodpeckerPlans.AnyAsync(p => p.UserId == userId && p.Id != planId && p.ArchivedAt == null, ct))
                throw new ConflictException("Archive your current Woodpecker training before restoring another one.")
                { Code = ApiErrorCodes.WoodpeckerActiveExists };
            plan.ArchivedAt = null;
            await _db.SaveChangesAsync(ct);
            await RefreshAsync(plan, userId, ct);
        }
        return await BuildDtoAsync(plan, utcOffsetMinutes, ct);
    }

    public async Task DeleteAsync(int userId, int planId, CancellationToken ct = default)
    {
        var plan = await LoadAsync(userId, planId, ct);
        // InMemory kaskadiert nicht — die Kinder ausdrücklich mitnehmen (MariaDB erledigte es selbst).
        _db.WoodpeckerAttempts.RemoveRange(await _db.WoodpeckerAttempts.Where(a => a.PlanId == planId).ToListAsync(ct));
        _db.WoodpeckerSetItems.RemoveRange(await _db.WoodpeckerSetItems.Where(i => i.PlanId == planId).ToListAsync(ct));
        _db.WoodpeckerPlans.Remove(plan);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Beginn des laufenden Zyklus verschieben — vor allem Zyklus 1 rückdatieren. Nicht in die Zukunft,
    /// nicht hinter den ersten Versuch dieses Zyklus und nicht vor das Ende des vorigen.</summary>
    public async Task<WoodpeckerPlanDto> UpdateStartAsync(int userId, int planId, DateTime startedAt,
        CancellationToken ct = default, int utcOffsetMinutes = 0)
    {
        var plan = await LoadAsync(userId, planId, ct);
        EnsureNotArchived(plan);
        if (plan.CompletedAt != null) throw new ConflictException("This Woodpecker training is already finished.");
        var current = Current(plan);
        if (current.FinishedAt != null) throw new ConflictException("This cycle is already finished.");
        startedAt = NormalizeUtc(startedAt);
        if (startedAt > Now) throw new DomainValidationException("The start cannot be in the future.");
        var previous = plan.Cycles.FirstOrDefault(c => c.Number == current.Number - 1);
        if (previous?.FinishedAt is { } prevEnd && startedAt < prevEnd)
            throw new DomainValidationException("The start cannot be before the end of the previous cycle.");
        var firstAttempt = await _db.WoodpeckerAttempts
            .Where(a => a.PlanId == planId && a.Cycle == current.Number)
            .MinAsync(a => (DateTime?)a.AttemptedAt, ct);
        if (firstAttempt is { } first && startedAt > first)
            throw new DomainValidationException("The start cannot be after the first puzzle of this cycle.");

        current.StartedAt = startedAt;
        await _db.SaveChangesAsync(ct);
        await RefreshAsync(plan, userId, ct);
        return await BuildDtoAsync(plan, utcOffsetMinutes, ct);
    }

    /// <summary>Zyklus 1 beenden (vor oder nach der Frist) — der Satz steht dann fest.</summary>
    public async Task<WoodpeckerPlanDto> FinishSetAsync(int userId, int planId, CancellationToken ct = default, int utcOffsetMinutes = 0)
    {
        var plan = await LoadAsync(userId, planId, ct);
        EnsureNotArchived(plan);
        await RefreshAsync(plan, userId, ct);
        var current = Current(plan);
        if (current.Number != 1 || current.FinishedAt != null)
            throw new ConflictException("Only the first cycle can be ended early.");
        await CloseAsync(plan, current, ct);
        return await BuildDtoAsync(plan, utcOffsetMinutes, ct);
    }

    /// <summary>
    /// Laufenden Zyklus abschließen, obwohl nicht alles bearbeitet ist (1.007.0) — die Antwort „abschließen“ auf die
    /// Frage nach Ablauf der Frist. Zyklus 1 wie <see cref="FinishSetAsync"/> (der Satz steht fest); ab Zyklus 2 erst nach
    /// der Frist (vorher 409). Die offenen Aufgaben bekommen keinen Versuch (die Statistik zählt sie nicht), danach geht es
    /// wie gewohnt weiter: Pause, nächster Zyklus in halber Zeit; nach Zyklus 7 ist die Methode abgeschlossen.
    /// </summary>
    public async Task<WoodpeckerPlanDto> FinishCycleAsync(int userId, int planId, CancellationToken ct = default, int utcOffsetMinutes = 0)
    {
        var plan = await LoadAsync(userId, planId, ct);
        EnsureNotArchived(plan);
        await RefreshAsync(plan, userId, ct);
        if (plan.CompletedAt != null) throw new ConflictException("This Woodpecker training is already finished.");
        var current = Current(plan);
        if (current.FinishedAt != null) throw new ConflictException("This cycle is already finished.");
        if (current.Number > 1 && Now < EffectiveDeadline(current))
            throw new ConflictException("The cycle still has time left.");
        await CloseAsync(plan, current, ct);
        return await BuildDtoAsync(plan, utcOffsetMinutes, ct);
    }

    /// <summary>
    /// Laufenden Zyklus verlängern (1.007.0) — die Antwort „verlängern“ auf die Frage nach Ablauf der Frist (geht auch
    /// vorher). Neue Frist = spätere von jetzt und bisheriger Frist + <paramref name="days"/> (1 … 28). Die Zieldauer aus
    /// dem Buch und damit die Halbierung für den nächsten Zyklus ändern sich nicht.
    /// </summary>
    public async Task<WoodpeckerPlanDto> ExtendCycleAsync(int userId, int planId, int days, CancellationToken ct = default,
        int utcOffsetMinutes = 0)
    {
        if (days < 1 || days > WoodpeckerRules.MaxExtensionDays)
            throw new DomainValidationException($"Extend by 1 to {WoodpeckerRules.MaxExtensionDays} days.");
        var plan = await LoadAsync(userId, planId, ct);
        EnsureNotArchived(plan);
        await RefreshAsync(plan, userId, ct);
        if (plan.CompletedAt != null) throw new ConflictException("This Woodpecker training is already finished.");
        var current = Current(plan);
        if (current.FinishedAt != null) throw new ConflictException("This cycle is already finished.");
        var from = EffectiveDeadline(current);
        if (from < Now) from = Now;
        current.ExtendedUntil = from.AddDays(days);
        await _db.SaveChangesAsync(ct);
        return await BuildDtoAsync(plan, utcOffsetMinutes, ct);
    }

    /// <summary>
    /// Gemeinsames Ende von <see cref="FinishSetAsync"/> und <see cref="FinishCycleAsync"/>. Ende = jetzt; nach Ablauf
    /// der Frist aber die Frist bzw. der letzte Versuch danach — sonst zählten die Tage, an denen man nur nicht
    /// entschieden hat, als Zyklusdauer (und die Pause begänne erst mit der Entscheidung). Zyklus 7 schließt die Methode.
    /// </summary>
    private async Task CloseAsync(WoodpeckerPlan plan, WoodpeckerCycle current, CancellationToken ct)
    {
        if (current.Number == 1 && await SetSizeAsync(plan.Id, ct) == 0)
            throw new DomainValidationException("Solve at least one puzzle first.");
        var now = Now;
        var deadline = EffectiveDeadline(current);
        var end = now;
        if (now > deadline)
        {
            var last = await _db.WoodpeckerAttempts.Where(a => a.PlanId == plan.Id && a.Cycle == current.Number)
                .MaxAsync(a => (DateTime?)a.AttemptedAt, ct);
            end = last is { } l && l > deadline ? l : deadline;
            if (end > now) end = now;
        }
        current.FinishedAt = end;
        if (current.Number >= WoodpeckerRules.MaxCycles) plan.CompletedAt = end;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Nächsten Zyklus beginnen — erst nach der Pause von mindestens einem Tag.</summary>
    public async Task<WoodpeckerPlanDto> StartNextCycleAsync(int userId, int planId, CancellationToken ct = default, int utcOffsetMinutes = 0)
    {
        var plan = await LoadAsync(userId, planId, ct);
        EnsureNotArchived(plan);
        await RefreshAsync(plan, userId, ct);
        if (plan.CompletedAt != null) throw new ConflictException("This Woodpecker training is already finished.");
        var current = Current(plan);
        if (current.FinishedAt is not { } finished) throw new ConflictException("The current cycle is still running.");
        if (Now < finished + WoodpeckerRules.MinBreak)
            throw new ConflictException("Take at least one day off before the next cycle.");
        plan.Cycles.Add(new WoodpeckerCycle { PlanId = plan.Id, Number = current.Number + 1, StartedAt = Now });
        await _db.SaveChangesAsync(ct);
        return await BuildDtoAsync(plan, utcOffsetMinutes, ct);
    }

    /// <summary>Nächste Aufgabe. Nur aus Kursen, auf die das Konto JETZT Zugriff hat (R3-003): wer aus der Kursgruppe fällt (oder
    /// dessen Kurs privat wird), bekommt aus diesem Kurs nichts mehr — die übrigen Kurse des Plans laufen weiter, der Kurs ruht,
    /// bis der Zugang wieder da ist. Ein Kalkulationsbuch liefert nie die Lösung.</summary>
    public async Task<WoodpeckerNextDto> GetNextAsync(int userId, int planId, CancellationToken ct = default, bool isAdmin = false,
        int utcOffsetMinutes = 0)
    {
        var plan = await LoadAsync(userId, planId, ct);
        EnsureNotArchived(plan);
        await RefreshAsync(plan, userId, ct);
        var current = Current(plan);
        int? puzzleId = null;
        if (plan.CompletedAt == null && current.FinishedAt == null)
        {
            var open = await AccessibleBookIdsAsync(plan, userId, isAdmin, ct);
            puzzleId = current.Number == 1
                ? await NextInSetOrderAsync(plan, open, ct)
                : await FixedOrderRemainingAsync(plan.Id, current.Number, open, ct);
        }

        BookPuzzleDto? puzzle = null;
        if (puzzleId is { } id)
        {
            var bp = await BookPuzzleService.PuzzleWithBook(_db, id).FirstAsync(ct);
            puzzle = BookPuzzleService.MapToDto(bp);
            if (bp.Book?.IsCalculation == true) BookPuzzleService.WithholdSolution(puzzle);
        }
        return new WoodpeckerNextDto { Puzzle = puzzle, Plan = await BuildDtoAsync(plan, utcOffsetMinutes, ct) };
    }

    public async Task<WoodpeckerPlanDto> RecordAttemptAsync(int userId, int planId, RecordWoodpeckerAttemptDto dto,
        CancellationToken ct = default, bool isAdmin = false, int utcOffsetMinutes = 0)
    {
        var plan = await LoadAsync(userId, planId, ct);
        EnsureNotArchived(plan);
        await RefreshAsync(plan, userId, ct);
        if (plan.CompletedAt != null) throw new ConflictException("This Woodpecker training is already finished.");
        var current = Current(plan);
        if (current.FinishedAt != null) throw new ConflictException("This cycle is already finished.");

        var puzzle = await _db.BookPuzzles.Where(bp => bp.Id == dto.BookPuzzleId)
            .Select(bp => new { bp.Id, bp.BookId, bp.IsInfoOnly })
            .FirstOrDefaultAsync(ct);
        var bookIds = plan.Books.Select(b => b.BookId).ToList();
        if (puzzle?.BookId is not { } bookId || puzzle.IsInfoOnly || !bookIds.Contains(bookId)
            || !await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin, ct))   // Zugang wie beim Anlegen (R3-003)
            throw new NotFoundException("Puzzle not found.");
        var inSet = await _db.WoodpeckerSetItems.AnyAsync(i => i.PlanId == planId && i.BookPuzzleId == puzzle.Id, ct);
        if (current.Number > 1 && !inSet) throw new NotFoundException("Puzzle not found.");

        var already = await _db.WoodpeckerAttempts.AnyAsync(
            a => a.PlanId == planId && a.Cycle == current.Number && a.BookPuzzleId == puzzle.Id, ct);
        if (!already)
        {
            var now = Now;
            var core = AttemptRecording.From(dto.Solved, dto.TimeSeconds, dto.HintsUsed, null, now);
            if (!inSet)
                _db.WoodpeckerSetItems.Add(new WoodpeckerSetItem
                {
                    PlanId = planId, BookId = bookId, BookPuzzleId = puzzle.Id,
                    Position = await SetSizeAsync(planId, ct),
                });
            _db.WoodpeckerAttempts.Add(new WoodpeckerAttempt
            {
                PlanId = planId, Cycle = current.Number, BookId = bookId, BookPuzzleId = puzzle.Id,
                Solved = core.Solved,
                TimeSeconds = core.TimeSeconds,
                HintsUsed = core.HintsUsed,
                AttemptedAt = now,
            });
            if (await _db.SaveIgnoringUniqueRaceAsync(ct: ct))
                await FinishIfCompleteAsync(plan, current, now, ct);
            else
                plan = await LoadAsync(userId, planId, ct);   // paralleler Doppel-Klick: der andere hat gespeichert
        }
        return await BuildDtoAsync(plan, utcOffsetMinutes, ct);
    }

    /// <summary>Ausführliche Statistik (Vergleich auf denselben Aufgaben, Hochrechnung, Verlauf, Sorgenkinder) — eigener
    /// Abruf, damit <see cref="BuildDtoAsync"/> (läuft bei jedem Versuch, in Liste und Dashboard) schlank bleibt. Lädt die
    /// Versuche des Plans in EINER Abfrage und verbindet im Speicher (<see cref="WoodpeckerStatsCalculator"/>).</summary>
    public async Task<WoodpeckerStatsDto> GetStatsAsync(int userId, int planId, int utcOffsetMinutes = 0, CancellationToken ct = default)
    {
        var plan = await LoadAsync(userId, planId, ct);
        await RefreshAsync(plan, userId, ct);
        var attempts = await _db.WoodpeckerAttempts.AsNoTracking().Where(a => a.PlanId == planId)
            .Select(a => new WoodpeckerStatsCalculator.Attempt(a.Cycle, a.BookPuzzleId, a.BookId, a.Solved, a.TimeSeconds, a.HintsUsed, a.AttemptedAt))
            .ToListAsync(ct);
        var setIds = await _db.WoodpeckerSetItems.AsNoTracking().Where(i => i.PlanId == planId)
            .Select(i => i.BookPuzzleId).ToListAsync(ct);
        var cycles = plan.Cycles.Select(c => new WoodpeckerStatsCalculator.Cycle(c.Number, c.StartedAt, c.FinishedAt)).ToList();
        var dto = WoodpeckerStatsCalculator.Compute(plan.Id, cycles, attempts, setIds, Now, utcOffsetMinutes);

        // Bezeichnungen der Sorgenkinder: nur Titel bzw. Kapitel — nie Stellung oder Lösung (DESIGN 2.7).
        var trouble = dto.Slowest.Concat(dto.AlwaysMissed).ToList();
        if (trouble.Count > 0)
        {
            var ids = trouble.Select(t => t.BookPuzzleId).Distinct().ToList();
            var labels = await _db.BookPuzzles.AsNoTracking().Where(bp => ids.Contains(bp.Id))
                .Select(bp => new { bp.Id, bp.Title, bp.Chapter })
                .ToListAsync(ct);
            foreach (var t in trouble)
            {
                var l = labels.FirstOrDefault(x => x.Id == t.BookPuzzleId);
                t.Label = !string.IsNullOrWhiteSpace(l?.Title) ? l.Title.Trim()
                    : !string.IsNullOrWhiteSpace(l?.Chapter) ? l.Chapter.Trim() : "";
            }
        }
        return dto;
    }

    // ===== intern =====

    private async Task<WoodpeckerPlan> LoadAsync(int userId, int planId, CancellationToken ct) =>
        await _db.WoodpeckerPlans.Include(p => p.Books).Include(p => p.Cycles)
            .FirstOrDefaultAsync(p => p.Id == planId && p.UserId == userId, ct)
        ?? throw new NotFoundException("Woodpecker training not found.");

    /// <summary>Archiviert = nur lesbar (0.958.0): jeder schreibende Schritt und <c>next</c> → 409.</summary>
    private static void EnsureNotArchived(WoodpeckerPlan plan)
    {
        if (plan.ArchivedAt != null)
            throw new ConflictException("This Woodpecker training is archived.") { Code = ApiErrorCodes.WoodpeckerArchived };
    }

    private static WoodpeckerCycle Current(WoodpeckerPlan plan) => plan.Cycles.OrderBy(c => c.Number).Last();

    private static DateTime EffectiveDeadline(WoodpeckerCycle c) => WoodpeckerRules.Deadline(c.Number, c.StartedAt, c.ExtendedUntil);

    private static DateTime NormalizeUtc(DateTime t) => t.Kind switch
    {
        DateTimeKind.Local => t.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(t, DateTimeKind.Utc),
        _ => t,
    };

    private Task<int> SetSizeAsync(int planId, CancellationToken ct) =>
        _db.WoodpeckerSetItems.CountAsync(i => i.PlanId == planId, ct);

    private IQueryable<BookPuzzle> Solvable(IEnumerable<int> bookIds) =>
        _db.BookPuzzles.Where(bp => bp.BookId != null && bookIds.Contains(bp.BookId.Value) && !bp.IsInfoOnly);

    /// <summary>Zyklus 1: Kursversuche seit dem Start übernehmen; sind damit alle Aufgaben im Satz, endet er. Die Frist
    /// beendet ihn seit 1.007.0 NICHT mehr still — der Nutzer entscheidet (<see cref="WoodpeckerPlanDto.DeadlinePassed"/>).</summary>
    private async Task RefreshAsync(WoodpeckerPlan plan, int userId, CancellationToken ct)
    {
        var current = Current(plan);
        // Archiviert: eingefroren — kein Kurs-Import (0.958.0).
        if (plan.ArchivedAt != null || plan.CompletedAt != null || current.FinishedAt != null || current.Number != 1) return;
        await ImportCourseAttemptsAsync(plan, userId, current, ct);
        await FinishIfCompleteAsync(plan, current, Now, ct);
    }

    /// <summary>Was der Nutzer in Zyklus 1 im normalen Kurs-Löser an den Plan-Kursen gemacht hat, zählt mit: je
    /// Aufgabe der erste Versuch ab dem Start (bis zur gültigen, ggf. verlängerten Frist), in zeitlicher Reihenfolge.</summary>
    private async Task ImportCourseAttemptsAsync(WoodpeckerPlan plan, int userId, WoodpeckerCycle cycle1,
        CancellationToken ct)
    {
        var bookIds = plan.Books.Select(b => b.BookId).ToList();
        var from = cycle1.StartedAt;
        var to = EffectiveDeadline(cycle1);
        var attempts = await _db.CourseAttempts
            .Where(a => a.UserId == userId && bookIds.Contains(a.BookId) && a.AttemptedAt >= from && a.AttemptedAt < to)
            .Where(a => !_db.WoodpeckerSetItems.Any(i => i.PlanId == plan.Id && i.BookPuzzleId == a.BookPuzzleId))
            .Where(a => _db.BookPuzzles.Any(bp => bp.Id == a.BookPuzzleId && !bp.IsInfoOnly))
            .Select(a => new { a.BookId, a.BookPuzzleId, a.Solved, a.TimeSeconds, a.HintsUsed, a.AttemptedAt })
            .ToListAsync(ct);
        if (attempts.Count == 0) return;

        var position = await SetSizeAsync(plan.Id, ct);
        foreach (var a in attempts.OrderBy(a => a.AttemptedAt).GroupBy(a => a.BookPuzzleId).Select(g => g.First())
                     .OrderBy(a => a.AttemptedAt))
        {
            _db.WoodpeckerSetItems.Add(new WoodpeckerSetItem
            {
                PlanId = plan.Id, BookId = a.BookId, BookPuzzleId = a.BookPuzzleId, Position = position++,
            });
            _db.WoodpeckerAttempts.Add(new WoodpeckerAttempt
            {
                PlanId = plan.Id, Cycle = 1, BookId = a.BookId, BookPuzzleId = a.BookPuzzleId,
                Solved = a.Solved, TimeSeconds = a.TimeSeconds,
                HintsUsed = Math.Clamp(a.HintsUsed, 0, AttemptRecording.MaxHints), AttemptedAt = a.AttemptedAt,
            });
        }
        // Läuft ein zweiter Abruf parallel, gewinnt einer — der andere sieht beim nächsten Lesen alles.
        await _db.SaveIgnoringUniqueRaceAsync(ct: ct);
    }

    /// <summary>Zyklus voll? Zyklus 1: alle Aufgaben der Kurse im Satz; ab 2: jede Aufgabe des Satzes bearbeitet.
    /// Dann beenden und prüfen, ob die Methode damit abgeschlossen ist.</summary>
    private async Task FinishIfCompleteAsync(WoodpeckerPlan plan, WoodpeckerCycle cycle, DateTime now,
        CancellationToken ct)
    {
        if (cycle.FinishedAt != null) return;
        var setSize = await SetSizeAsync(plan.Id, ct);
        if (setSize == 0) return;
        var full = cycle.Number == 1
            ? setSize >= await Solvable(plan.Books.Select(b => b.BookId)).CountAsync(ct)
            : await _db.WoodpeckerAttempts.CountAsync(a => a.PlanId == plan.Id && a.Cycle == cycle.Number, ct) >= setSize;
        if (!full) return;
        cycle.FinishedAt = now;
        if (cycle.Number > 1 && WoodpeckerRules.CompletesMethod(cycle.Number, cycle.StartedAt, now))
            plan.CompletedAt = now;
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Die Kurse des Plans, auf die das Konto jetzt Zugriff hat (<see cref="CourseAccess.CanAccessAsync"/>, R3-003).</summary>
    private async Task<HashSet<int>> AccessibleBookIdsAsync(WoodpeckerPlan plan, int userId, bool isAdmin, CancellationToken ct)
    {
        var open = new HashSet<int>();
        foreach (var b in plan.Books)
            if (await CourseAccess.CanAccessAsync(_db, userId, b.BookId, isAdmin, ct)) open.Add(b.BookId);
        return open;
    }

    /// <summary>Zyklus 1: erste noch nicht bearbeitete Aufgabe — Kurse in Plan-Reihenfolge, je Kurs Lesereihenfolge; nur
    /// zugängliche Kurse (<paramref name="open"/>).</summary>
    private async Task<int?> NextInSetOrderAsync(WoodpeckerPlan plan, HashSet<int> open, CancellationToken ct)
    {
        foreach (var book in plan.Books.Where(b => open.Contains(b.BookId)).OrderBy(b => b.Position))
        {
            var id = await Solvable(new[] { book.BookId })
                .Where(bp => !_db.WoodpeckerSetItems.Any(i => i.PlanId == plan.Id && i.BookPuzzleId == bp.Id))
                .InReadingOrder()
                .Select(bp => (int?)bp.Id)
                .FirstOrDefaultAsync(ct);
            if (id != null) return id;
        }
        return null;
    }

    /// <summary>Ab Zyklus 2: die erste Aufgabe des Satzes in der festen, gemischten Reihenfolge des Zyklus
    /// (<see cref="WoodpeckerRules.ShuffleKey"/>), die in diesem Zyklus noch nicht dran war — nur aus zugänglichen Kursen.
    /// Seit 1.013.0 fest (Wunsch 25.11.2026): Neu laden, später weitermachen oder ein anderes Gerät bringen
    /// dieselbe Aufgabe; vorher zog jeder Abruf neu (<c>Random.Shared</c>).</summary>
    private async Task<int?> FixedOrderRemainingAsync(int planId, int cycle, HashSet<int> open, CancellationToken ct)
    {
        var openIds = open.ToList();
        var remaining = await _db.WoodpeckerSetItems
            .Where(i => i.PlanId == planId && openIds.Contains(i.BookId))
            .Where(i => !_db.WoodpeckerAttempts.Any(a => a.PlanId == planId && a.Cycle == cycle && a.BookPuzzleId == i.BookPuzzleId))
            .Select(i => i.BookPuzzleId)
            .ToListAsync(ct);
        return remaining.Count == 0
            ? null
            : remaining.OrderBy(id => WoodpeckerRules.ShuffleKey(planId, cycle, id)).ThenBy(id => id).First();
    }

    private async Task<WoodpeckerPlanDto> BuildDtoAsync(WoodpeckerPlan plan, int utcOffsetMinutes, CancellationToken ct)
    {
        var bookIds = plan.Books.Select(b => b.BookId).ToList();
        var books = await _db.Books.Where(b => bookIds.Contains(b.Id))
            .Select(b => new { b.Id, b.DisplayName, b.FileName })
            .ToListAsync(ct);
        var counts = await Solvable(bookIds)
            .GroupBy(bp => bp.BookId!.Value)
            .Select(g => new { BookId = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var stats = await _db.WoodpeckerAttempts.Where(a => a.PlanId == plan.Id)
            .GroupBy(a => a.Cycle)
            .Select(g => new
            {
                Cycle = g.Key, Attempted = g.Count(), Solved = g.Count(a => a.Solved), Time = g.Sum(a => a.TimeSeconds),
                Hinted = g.Count(a => a.HintsUsed > 0), SolvedWithHint = g.Count(a => a.Solved && a.HintsUsed > 0),
            })
            .ToListAsync(ct);

        var cycles = plan.Cycles.OrderBy(c => c.Number).ToList();
        var current = cycles.Last();
        // „Heute bearbeitet“ (0.993.0): gewertete Versuche des laufenden Zyklus seit der lokalen Mitternacht des
        // Betrachters — je Aufgabe und Zyklus gibt es nur den ersten, jede Zeile ist also eine Aufgabe. Eine Zählabfrage,
        // damit Kachel und Löser-Kopf das Tagesziel ohne /stats zeigen können.
        var dayStart = WoodpeckerStatsCalculator.LocalDayStartUtc(Now, utcOffsetMinutes);
        var todayAttempted = await _db.WoodpeckerAttempts.CountAsync(
            a => a.PlanId == plan.Id && a.Cycle == current.Number && a.AttemptedAt >= dayStart, ct);
        var setSize = await SetSizeAsync(plan.Id, ct);
        string status;
        DateTime? nextAt = null;
        if (plan.ArchivedAt != null) status = "archived";
        else if (plan.CompletedAt != null) status = "completed";
        else if (current.FinishedAt == null) status = "solving";
        else
        {
            nextAt = current.FinishedAt.Value + WoodpeckerRules.MinBreak;
            status = Now < nextAt ? "break" : "ready";
        }
        // Frist um, Zyklus nicht fertig → die App fragt „verlängern oder abschließen?“ (1.007.0).
        var deadlinePassed = status == "solving" && Now >= EffectiveDeadline(current) && (current.Number > 1 || setSize > 0);
        int? suggestedDays = null;
        if (deadlinePassed)
        {
            var attempted = stats.FirstOrDefault(x => x.Cycle == current.Number)?.Attempted ?? 0;
            var open = current.Number == 1 ? counts.Sum(c => c.Count) - setSize : setSize - attempted;
            suggestedDays = WoodpeckerRules.SuggestExtensionDays(current.Number, open, attempted,
                (Now - current.StartedAt).TotalDays);
        }

        return new WoodpeckerPlanDto
        {
            Id = plan.Id,
            Name = plan.Name,
            CreatedAt = plan.CreatedAt,
            CompletedAt = plan.CompletedAt,
            ArchivedAt = plan.ArchivedAt,
            Books = plan.Books.OrderBy(b => b.Position).Select(pb =>
            {
                var b = books.FirstOrDefault(x => x.Id == pb.BookId);
                return new WoodpeckerBookDto
                {
                    BookId = pb.BookId,
                    DisplayName = b == null ? "" : string.IsNullOrWhiteSpace(b.DisplayName) ? b.FileName : b.DisplayName,
                    PuzzleCount = counts.FirstOrDefault(c => c.BookId == pb.BookId)?.Count ?? 0,
                };
            }).ToList(),
            SetSize = setSize,
            AvailableCount = counts.Sum(c => c.Count),
            CurrentCycle = current.Number,
            MaxCycles = WoodpeckerRules.MaxCycles,
            Status = status,
            NextCycleAt = nextAt,
            TodayAttempted = todayAttempted,
            DeadlinePassed = deadlinePassed,
            SuggestedExtensionDays = suggestedDays,
            Cycles = cycles.Select(c =>
            {
                var s = stats.FirstOrDefault(x => x.Cycle == c.Number);
                return new WoodpeckerCycleDto
                {
                    Number = c.Number,
                    TargetDays = WoodpeckerRules.TargetDays(c.Number),
                    StartedAt = c.StartedAt,
                    Deadline = EffectiveDeadline(c),
                    ExtendedUntil = c.ExtendedUntil,
                    FinishedAt = c.FinishedAt,
                    Attempted = s?.Attempted ?? 0,
                    Solved = s?.Solved ?? 0,
                    TimeSeconds = s?.Time ?? 0,
                    Hinted = s?.Hinted ?? 0,
                    SolvedWithHint = s?.SolvedWithHint ?? 0,
                };
            }).ToList(),
        };
    }
}
