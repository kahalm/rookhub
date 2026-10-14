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
/// datiert den Start zurück und hat sie im Satz. Zyklus 1 endet mit der Frist, auf Knopfdruck oder wenn alle Aufgaben
/// der Kurse im Satz sind.</para>
/// <para><b>Ab Zyklus 2</b> kommt der Satz in zufälliger Reihenfolge, je Aufgabe zählt der erste Versuch; der Zyklus
/// endet, wenn alle Aufgaben bearbeitet sind. Zustandswechsel (Frist abgelaufen) werden beim Lesen nachgezogen.</para>
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

    public async Task<List<WoodpeckerPlanDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        var plans = await _db.WoodpeckerPlans.Where(p => p.UserId == userId)
            .OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Include(p => p.Books).Include(p => p.Cycles)
            .ToListAsync(ct);
        var result = new List<WoodpeckerPlanDto>(plans.Count);
        foreach (var plan in plans)
        {
            await RefreshAsync(plan, userId, ct);
            result.Add(await BuildDtoAsync(plan, ct));
        }
        return result;
    }

    public async Task<WoodpeckerPlanDto> GetAsync(int userId, int planId, CancellationToken ct = default)
    {
        var plan = await LoadAsync(userId, planId, ct);
        await RefreshAsync(plan, userId, ct);
        return await BuildDtoAsync(plan, ct);
    }

    public async Task<WoodpeckerPlanDto> CreateAsync(int userId, bool isAdmin, CreateWoodpeckerPlanDto dto,
        CancellationToken ct = default)
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
        _db.WoodpeckerPlans.Add(plan);
        await _db.SaveChangesAsync(ct);

        await RefreshAsync(plan, userId, ct);
        return await BuildDtoAsync(plan, ct);
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
        CancellationToken ct = default)
    {
        var plan = await LoadAsync(userId, planId, ct);
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
        return await BuildDtoAsync(plan, ct);
    }

    /// <summary>Zyklus 1 vor der Frist beenden — der Satz steht dann fest.</summary>
    public async Task<WoodpeckerPlanDto> FinishSetAsync(int userId, int planId, CancellationToken ct = default)
    {
        var plan = await LoadAsync(userId, planId, ct);
        await RefreshAsync(plan, userId, ct);
        var current = Current(plan);
        if (current.Number != 1 || current.FinishedAt != null)
            throw new ConflictException("Only the first cycle can be ended early.");
        if (await SetSizeAsync(planId, ct) == 0)
            throw new DomainValidationException("Solve at least one puzzle first.");
        current.FinishedAt = Now;
        await _db.SaveChangesAsync(ct);
        return await BuildDtoAsync(plan, ct);
    }

    /// <summary>Nächsten Zyklus beginnen — erst nach der Pause von mindestens einem Tag.</summary>
    public async Task<WoodpeckerPlanDto> StartNextCycleAsync(int userId, int planId, CancellationToken ct = default)
    {
        var plan = await LoadAsync(userId, planId, ct);
        await RefreshAsync(plan, userId, ct);
        if (plan.CompletedAt != null) throw new ConflictException("This Woodpecker training is already finished.");
        var current = Current(plan);
        if (current.FinishedAt is not { } finished) throw new ConflictException("The current cycle is still running.");
        if (Now < finished + WoodpeckerRules.MinBreak)
            throw new ConflictException("Take at least one day off before the next cycle.");
        plan.Cycles.Add(new WoodpeckerCycle { PlanId = plan.Id, Number = current.Number + 1, StartedAt = Now });
        await _db.SaveChangesAsync(ct);
        return await BuildDtoAsync(plan, ct);
    }

    public async Task<WoodpeckerNextDto> GetNextAsync(int userId, int planId, CancellationToken ct = default)
    {
        var plan = await LoadAsync(userId, planId, ct);
        await RefreshAsync(plan, userId, ct);
        var current = Current(plan);
        int? puzzleId = null;
        if (plan.CompletedAt == null && current.FinishedAt == null)
            puzzleId = current.Number == 1
                ? await NextInSetOrderAsync(plan, ct)
                : await RandomRemainingAsync(plan.Id, current.Number, ct);

        BookPuzzleDto? puzzle = null;
        if (puzzleId is { } id)
        {
            var bp = await BookPuzzleService.PuzzleWithBook(_db, id).FirstAsync(ct);
            puzzle = BookPuzzleService.MapToDto(bp);
        }
        return new WoodpeckerNextDto { Puzzle = puzzle, Plan = await BuildDtoAsync(plan, ct) };
    }

    public async Task<WoodpeckerPlanDto> RecordAttemptAsync(int userId, int planId, RecordWoodpeckerAttemptDto dto,
        CancellationToken ct = default)
    {
        var plan = await LoadAsync(userId, planId, ct);
        await RefreshAsync(plan, userId, ct);
        if (plan.CompletedAt != null) throw new ConflictException("This Woodpecker training is already finished.");
        var current = Current(plan);
        if (current.FinishedAt != null) throw new ConflictException("This cycle is already finished.");

        var puzzle = await _db.BookPuzzles.Where(bp => bp.Id == dto.BookPuzzleId)
            .Select(bp => new { bp.Id, bp.BookId, bp.IsInfoOnly })
            .FirstOrDefaultAsync(ct);
        var bookIds = plan.Books.Select(b => b.BookId).ToList();
        if (puzzle?.BookId is not { } bookId || puzzle.IsInfoOnly || !bookIds.Contains(bookId))
            throw new NotFoundException("Puzzle not found.");
        var inSet = await _db.WoodpeckerSetItems.AnyAsync(i => i.PlanId == planId && i.BookPuzzleId == puzzle.Id, ct);
        if (current.Number > 1 && !inSet) throw new NotFoundException("Puzzle not found.");

        var already = await _db.WoodpeckerAttempts.AnyAsync(
            a => a.PlanId == planId && a.Cycle == current.Number && a.BookPuzzleId == puzzle.Id, ct);
        if (!already)
        {
            var now = Now;
            if (!inSet)
                _db.WoodpeckerSetItems.Add(new WoodpeckerSetItem
                {
                    PlanId = planId, BookId = bookId, BookPuzzleId = puzzle.Id,
                    Position = await SetSizeAsync(planId, ct),
                });
            _db.WoodpeckerAttempts.Add(new WoodpeckerAttempt
            {
                PlanId = planId, Cycle = current.Number, BookId = bookId, BookPuzzleId = puzzle.Id,
                Solved = dto.Solved,
                TimeSeconds = Math.Clamp(dto.TimeSeconds, 0, AttemptRecording.MaxSeconds),
                AttemptedAt = now,
            });
            if (await _db.SaveIgnoringUniqueRaceAsync(ct: ct))
                await FinishIfCompleteAsync(plan, current, now, ct);
            else
                plan = await LoadAsync(userId, planId, ct);   // paralleler Doppel-Klick: der andere hat gespeichert
        }
        return await BuildDtoAsync(plan, ct);
    }

    // ===== intern =====

    private async Task<WoodpeckerPlan> LoadAsync(int userId, int planId, CancellationToken ct) =>
        await _db.WoodpeckerPlans.Include(p => p.Books).Include(p => p.Cycles)
            .FirstOrDefaultAsync(p => p.Id == planId && p.UserId == userId, ct)
        ?? throw new NotFoundException("Woodpecker training not found.");

    private static WoodpeckerCycle Current(WoodpeckerPlan plan) => plan.Cycles.OrderBy(c => c.Number).Last();

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

    /// <summary>Frist von Zyklus 1 abgelaufen → beenden (an der Frist); Kursversuche seit dem Start übernehmen.</summary>
    private async Task RefreshAsync(WoodpeckerPlan plan, int userId, CancellationToken ct)
    {
        var current = Current(plan);
        if (plan.CompletedAt != null || current.FinishedAt != null || current.Number != 1) return;
        await ImportCourseAttemptsAsync(plan, userId, current, ct);
        var deadline = WoodpeckerRules.Deadline(1, current.StartedAt);
        if (Now >= deadline && await SetSizeAsync(plan.Id, ct) > 0)
        {
            current.FinishedAt = deadline;
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            await FinishIfCompleteAsync(plan, current, Now, ct);
        }
    }

    /// <summary>Was der Nutzer in Zyklus 1 im normalen Kurs-Löser an den Plan-Kursen gemacht hat, zählt mit: je
    /// Aufgabe der erste Versuch ab dem Start (bis zur Frist), in zeitlicher Reihenfolge.</summary>
    private async Task ImportCourseAttemptsAsync(WoodpeckerPlan plan, int userId, WoodpeckerCycle cycle1,
        CancellationToken ct)
    {
        var bookIds = plan.Books.Select(b => b.BookId).ToList();
        var from = cycle1.StartedAt;
        var to = WoodpeckerRules.Deadline(1, from);
        var attempts = await _db.CourseAttempts
            .Where(a => a.UserId == userId && bookIds.Contains(a.BookId) && a.AttemptedAt >= from && a.AttemptedAt < to)
            .Where(a => !_db.WoodpeckerSetItems.Any(i => i.PlanId == plan.Id && i.BookPuzzleId == a.BookPuzzleId))
            .Where(a => _db.BookPuzzles.Any(bp => bp.Id == a.BookPuzzleId && !bp.IsInfoOnly))
            .Select(a => new { a.BookId, a.BookPuzzleId, a.Solved, a.TimeSeconds, a.AttemptedAt })
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
                Solved = a.Solved, TimeSeconds = a.TimeSeconds, AttemptedAt = a.AttemptedAt,
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

    /// <summary>Zyklus 1: erste noch nicht bearbeitete Aufgabe — Kurse in Plan-Reihenfolge, je Kurs Lesereihenfolge.</summary>
    private async Task<int?> NextInSetOrderAsync(WoodpeckerPlan plan, CancellationToken ct)
    {
        foreach (var book in plan.Books.OrderBy(b => b.Position))
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

    /// <summary>Ab Zyklus 2: zufällige Aufgabe des Satzes, die in diesem Zyklus noch nicht dran war.</summary>
    private async Task<int?> RandomRemainingAsync(int planId, int cycle, CancellationToken ct)
    {
        var remaining = await _db.WoodpeckerSetItems
            .Where(i => i.PlanId == planId)
            .Where(i => !_db.WoodpeckerAttempts.Any(a => a.PlanId == planId && a.Cycle == cycle && a.BookPuzzleId == i.BookPuzzleId))
            .Select(i => i.BookPuzzleId)
            .ToListAsync(ct);
        return remaining.Count == 0 ? null : remaining[Random.Shared.Next(remaining.Count)];
    }

    private async Task<WoodpeckerPlanDto> BuildDtoAsync(WoodpeckerPlan plan, CancellationToken ct)
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
            .Select(g => new { Cycle = g.Key, Attempted = g.Count(), Solved = g.Count(a => a.Solved), Time = g.Sum(a => a.TimeSeconds) })
            .ToListAsync(ct);

        var cycles = plan.Cycles.OrderBy(c => c.Number).ToList();
        var current = cycles.Last();
        string status;
        DateTime? nextAt = null;
        if (plan.CompletedAt != null) status = "completed";
        else if (current.FinishedAt == null) status = "solving";
        else
        {
            nextAt = current.FinishedAt.Value + WoodpeckerRules.MinBreak;
            status = Now < nextAt ? "break" : "ready";
        }

        return new WoodpeckerPlanDto
        {
            Id = plan.Id,
            Name = plan.Name,
            CreatedAt = plan.CreatedAt,
            CompletedAt = plan.CompletedAt,
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
            SetSize = await SetSizeAsync(plan.Id, ct),
            AvailableCount = counts.Sum(c => c.Count),
            CurrentCycle = current.Number,
            MaxCycles = WoodpeckerRules.MaxCycles,
            Status = status,
            NextCycleAt = nextAt,
            Cycles = cycles.Select(c =>
            {
                var s = stats.FirstOrDefault(x => x.Cycle == c.Number);
                return new WoodpeckerCycleDto
                {
                    Number = c.Number,
                    TargetDays = WoodpeckerRules.TargetDays(c.Number),
                    StartedAt = c.StartedAt,
                    Deadline = WoodpeckerRules.Deadline(c.Number, c.StartedAt),
                    FinishedAt = c.FinishedAt,
                    Attempted = s?.Attempted ?? 0,
                    Solved = s?.Solved ?? 0,
                    TimeSeconds = s?.Time ?? 0,
                };
            }).ToList(),
        };
    }
}
