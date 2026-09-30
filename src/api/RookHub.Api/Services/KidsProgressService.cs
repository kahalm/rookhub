using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Der KidHub-Fortschritt im Konto. KidHub schickt seinen GANZEN Stand (er ist klein: ein Eintrag je
/// gespielter Stufe, die gelösten Kurs-Linien) und bekommt den zusammengeführten zurück
/// (<see cref="KidsProgressMerge"/>) — so übernimmt der erste Abgleich nach dem Anmelden auch, was das
/// Kind vorher ohne Konto gespielt hat, und ein zweites Gerät bekommt denselben Stand.
/// </summary>
public class KidsProgressService
{
    /// <summary>Deckel je Anfrage — ein echter Stand liegt weit darunter (40 Stufen, eine Handvoll Kurse).
    /// <see cref="MaxLevels"/> ist zugleich die höchste erlaubte Stufen-Nummer: so trägt ein Konto höchstens so viele
    /// Stufen-Zeilen. Kurse und Linien deckelt je Konto der Abgleich mit dem Bestand (<see cref="KeepKnownCoursesAsync"/>).</summary>
    public const int MaxLevels = 1000;
    public const int MaxCourses = 500;
    public const int MaxLinesPerCourse = 10_000;
    /// <summary>Rumpf-Deckel für PUT /api/kids/progress (eine gelöste Linie sind rund 35 Byte) — ein echter Stand liegt
    /// weit darunter. Ohne ihn las die API bis zu nginx' 15 MB je Aufruf ein.</summary>
    public const int MaxRequestBytes = 1024 * 1024;
    /// <summary>Zeiten aus dem Browser: höchstens so weit in der Zukunft (falsch gestellte Uhr), sonst
    /// gewönne ein Durchgang mit Jahr 2099 jeden Abgleich für immer.</summary>
    public static readonly TimeSpan MaxClockAhead = TimeSpan.FromDays(1);

    private readonly AppDbContext _db;
    private readonly Func<DateTime> _utcNow;

    public KidsProgressService(AppDbContext db) : this(db, () => DateTime.UtcNow) { }

    internal KidsProgressService(AppDbContext db, Func<DateTime> utcNow)
    {
        _db = db;
        _utcNow = utcNow;
    }

    public async Task<KidsProgressDto> GetAsync(int userId, CancellationToken ct = default) =>
        (await LoadAsync(userId, ct)).Dto;

    /// <summary>Stand des Browsers mit dem im Konto zusammenführen, speichern, Ergebnis zurück.
    /// <see cref="ArgumentException"/> bei einer Anfrage über den Deckeln.</summary>
    public async Task<KidsProgressDto> SyncAsync(int userId, KidsProgressDto incoming, CancellationToken ct = default)
    {
        var clean = await KeepKnownCoursesAsync(Normalize(incoming), ct);
        try
        {
            return await SyncOnceAsync(userId, clean, ct);
        }
        catch (DbUpdateException)
        {
            // Zwei Geräte zugleich: dieselbe Zeile wurde zweimal angelegt (eindeutiger Index). Neu laden —
            // die andere Anfrage steht jetzt drin — und noch einmal zusammenführen.
            _db.ChangeTracker.Clear();
            return await SyncOnceAsync(userId, clean, ct);
        }
    }

    private async Task<KidsProgressDto> SyncOnceAsync(int userId, KidsProgressDto clean, CancellationToken ct)
    {
        var stored = await LoadAsync(userId, ct);
        var merged = KidsProgressMerge.Merge(stored.Dto, clean);
        var now = _utcNow();

        var levels = stored.Levels.ToDictionary(l => l.Level);
        foreach (var l in merged.Levels)
        {
            var runAt = FromMs(l.RunAt);
            if (!levels.TryGetValue(l.Level, out var row))
            {
                _db.KidsLevelProgresses.Add(new KidsLevelProgress
                {
                    UserId = userId, Level = l.Level, Stars = l.Stars, RunIndex = l.RunIndex,
                    RunMistakes = l.RunMistakes, RunAt = runAt, UpdatedAt = now,
                });
            }
            else if (row.Stars != l.Stars || row.RunIndex != l.RunIndex || row.RunMistakes != l.RunMistakes
                     || ToMs(row.RunAt) != l.RunAt)
            {
                row.Stars = l.Stars;
                row.RunIndex = l.RunIndex;
                row.RunMistakes = l.RunMistakes;
                row.RunAt = runAt;
                row.UpdatedAt = now;
            }
        }

        var courses = stored.Courses.ToDictionary(c => c.BookId);
        var linesByBook = stored.Lines.GroupBy(l => l.BookId).ToDictionary(g => g.Key, g => g.ToDictionary(l => l.BookPuzzleId));
        var mergedBooks = merged.Courses.Select(c => c.BookId).ToHashSet();
        foreach (var c in merged.Courses)
        {
            DateTime? resetAt = c.ResetAt > 0 ? FromMs(c.ResetAt) : null;
            if (!courses.TryGetValue(c.BookId, out var progress))
            {
                _db.KidsCourseProgresses.Add(new KidsCourseProgress { UserId = userId, BookId = c.BookId, ResetAt = resetAt, UpdatedAt = now });
            }
            else if (progress.ResetAt != resetAt)
            {
                progress.ResetAt = resetAt;
                progress.UpdatedAt = now;
            }

            var existing = linesByBook.GetValueOrDefault(c.BookId) ?? new Dictionary<int, KidsCourseLine>();
            var wanted = c.Solved.ToDictionary(s => s.Id, s => s.At);
            foreach (var (lineId, row) in existing)
                if (!wanted.ContainsKey(lineId)) _db.KidsCourseLines.Remove(row);     // vor einem „Von vorn" gelöst
            foreach (var (lineId, at) in wanted)
            {
                if (!existing.TryGetValue(lineId, out var row))
                    _db.KidsCourseLines.Add(new KidsCourseLine { UserId = userId, BookId = c.BookId, BookPuzzleId = lineId, SolvedAt = FromMs(at) });
                else if (ToMs(row.SolvedAt) != at)
                    row.SolvedAt = FromMs(at);
            }
        }
        // Ein Kurs, der nach dem Zusammenführen nichts mehr trägt (kein „Von vorn", keine Linie).
        foreach (var c in stored.Courses.Where(c => !mergedBooks.Contains(c.BookId)))
            _db.KidsCourseProgresses.Remove(c);
        foreach (var l in stored.Lines.Where(l => !mergedBooks.Contains(l.BookId)))
            _db.KidsCourseLines.Remove(l);

        await _db.SaveChangesAsync(ct);
        return merged;
    }

    private sealed record Stored(KidsProgressDto Dto, List<KidsLevelProgress> Levels,
        List<KidsCourseProgress> Courses, List<KidsCourseLine> Lines);

    private async Task<Stored> LoadAsync(int userId, CancellationToken ct)
    {
        var levels = await _db.KidsLevelProgresses.Where(l => l.UserId == userId).ToListAsync(ct);
        var courses = await _db.KidsCourseProgresses.Where(c => c.UserId == userId).ToListAsync(ct);
        var lines = await _db.KidsCourseLines.Where(l => l.UserId == userId).ToListAsync(ct);

        var books = courses.Select(c => c.BookId).Concat(lines.Select(l => l.BookId)).Distinct().OrderBy(b => b);
        var dto = new KidsProgressDto
        {
            Levels = levels.OrderBy(l => l.Level).Select(l => new KidsLevelProgressDto
            {
                Level = l.Level, Stars = l.Stars, RunIndex = l.RunIndex, RunMistakes = l.RunMistakes, RunAt = ToMs(l.RunAt),
            }).ToList(),
            Courses = books.Select(bookId => new KidsCourseProgressDto
            {
                BookId = bookId,
                ResetAt = courses.FirstOrDefault(c => c.BookId == bookId)?.ResetAt is DateTime r ? ToMs(r) : 0,
                Solved = lines.Where(l => l.BookId == bookId).OrderBy(l => l.BookPuzzleId)
                    .Select(l => new KidsSolvedLineDto { Id = l.BookPuzzleId, At = ToMs(l.SolvedAt) }).ToList(),
            }).ToList(),
        };
        return new Stored(dto, levels, courses, lines);
    }

    /// <summary>
    /// Nur Kinderkurse (<see cref="Book.ForKids"/>, kein Kalkulationsbuch) und nur Linien, die zu DIESEM Buch gehören.
    /// Die Deckel oben gelten je Anfrage, der Stand wird aber mit dem gespeicherten vereinigt — ohne diesen Abgleich
    /// wuchs ein Konto mit erfundenen Linien-Ids um bis zu 10 000 Zeilen je Aufruf, ohne Ende, und jeder spätere
    /// Abgleich lud alles. So trägt ein Konto höchstens die Linien der Kinderkurse. Verworfen wird nur der
    /// EINGEHENDE Stand: was schon gespeichert ist, bleibt (ein Admin, der die Freigabe kurz zurücknimmt, löscht
    /// keinen Fortschritt); eine Linie eines gelöschten oder neu eingespielten Buchs fällt aus dem Stand — gewollt,
    /// vorher scheiterte sie am Fremdschlüssel, und der Abgleich des Kontos antwortete bei jedem Aufruf 500.
    /// </summary>
    private async Task<KidsProgressDto> KeepKnownCoursesAsync(KidsProgressDto clean, CancellationToken ct)
    {
        if (clean.Courses.Count == 0) return clean;
        var bookIds = clean.Courses.Select(c => c.BookId).ToList();
        var kidsBooks = await _db.Books.AsNoTracking()
            .Where(b => bookIds.Contains(b.Id) && b.ForKids && !b.IsCalculation)
            .Select(b => b.Id).ToListAsync(ct);
        var lines = (await _db.BookPuzzles.AsNoTracking()
                .Where(p => p.BookId != null && kidsBooks.Contains(p.BookId.Value))
                .Select(p => new { p.Id, BookId = p.BookId!.Value }).ToListAsync(ct))
            .ToLookup(p => p.BookId, p => p.Id);

        var result = new KidsProgressDto { Levels = clean.Levels };
        foreach (var c in clean.Courses.Where(c => kidsBooks.Contains(c.BookId)))
        {
            var known = lines[c.BookId].ToHashSet();
            result.Courses.Add(new KidsCourseProgressDto
            {
                BookId = c.BookId,
                ResetAt = c.ResetAt,
                Solved = c.Solved.Where(s => known.Contains(s.Id)).ToList(),
            });
        }
        return result;
    }

    /// <summary>Werte aus dem Browser in erlaubte Grenzen bringen — über den Deckeln: Fehler statt still kürzen.</summary>
    internal KidsProgressDto Normalize(KidsProgressDto incoming)
    {
        if (incoming.Levels.Count > MaxLevels) throw new ArgumentException($"At most {MaxLevels} levels.");
        if (incoming.Courses.Count > MaxCourses) throw new ArgumentException($"At most {MaxCourses} courses.");
        var maxAt = ToMs(_utcNow() + MaxClockAhead);
        long At(long ms) => Math.Clamp(ms, 0, maxAt);

        var result = new KidsProgressDto
        {
            Levels = incoming.Levels.Where(l => l.Level is >= 1 and <= MaxLevels).Select(l => new KidsLevelProgressDto
            {
                Level = l.Level,
                Stars = Math.Clamp(l.Stars, 0, 3),
                RunIndex = Math.Clamp(l.RunIndex, 0, 1000),
                RunMistakes = Math.Clamp(l.RunMistakes, 0, 100_000),
                RunAt = At(l.RunAt),
            }).ToList(),
        };
        foreach (var c in incoming.Courses.Where(c => c.BookId > 0))
        {
            if (c.Solved.Count > MaxLinesPerCourse) throw new ArgumentException($"At most {MaxLinesPerCourse} lines per course.");
            result.Courses.Add(new KidsCourseProgressDto
            {
                BookId = c.BookId,
                ResetAt = At(c.ResetAt),
                // Mindestens 1: eine Linie ohne Zeit (vor dem Konto-Abgleich im Browser geloest) gilt als
                // „irgendwann frueher" — mit 0 fiele sie durch „nach dem letzten Von vorn" (0 > 0).
                Solved = c.Solved.Where(s => s.Id > 0).Select(s => new KidsSolvedLineDto { Id = s.Id, At = Math.Max(1, At(s.At)) }).ToList(),
            });
        }
        // Doppelte Stufen/Kurse/Linien in EINER Anfrage fasst dieselbe Regel zusammen wie zwei Geräte.
        return KidsProgressMerge.Merge(new KidsProgressDto(), result);
    }

    internal static long ToMs(DateTime at) => (long)Math.Round((at - DateTime.UnixEpoch).TotalMilliseconds);
    internal static DateTime FromMs(long ms) => DateTime.SpecifyKind(DateTime.UnixEpoch.AddMilliseconds(ms), DateTimeKind.Utc);
}
