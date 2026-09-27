using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Wie zwei Stände des KidHub-Fortschritts zusammengehen — der im Konto und der aus einem Browser.
/// Das Kind kann auf zwei Geräten spielen, und ohne Netz spielt es weiter; beim nächsten Abgleich darf
/// nichts verloren gehen, was es geschafft hat:
/// <list type="bullet">
/// <item><b>Sterne</b>: der höhere Wert. „Nochmal" nimmt keine Sterne weg, also gibt es kein Zurück.</item>
/// <item><b>Laufender Durchgang</b> (Aufgabe + Fehler): der JÜNGERE (<c>RunAt</c>) — er ist ein Zustand,
///   keine Leistung; bei Gleichstand bleibt der erste (<paramref name="a"/>, am Server der gespeicherte).</item>
/// <item><b>Kurs-Linien</b>: vereinigt, je Linie der jüngste Zeitpunkt — aber nur, was NACH dem jüngsten
///   „Von vorn" (<c>ResetAt</c>) gelöst wurde. Ohne diese Marke brächte das andere Gerät die gerade
///   verworfenen Linien zurück.</item>
/// </list>
/// <para>SPIEGEL: <c>mergeProgress</c> in <c>src-kidhub/app/core/kids-progress.store.ts</c> — beide Seiten
/// haben Tests mit denselben LITERALEN Fällen (<c>KidsProgressMergeTests</c> ↔ <c>kids-progress.store.spec.ts</c>).</para>
/// </summary>
public static class KidsProgressMerge
{
    public static KidsProgressDto Merge(KidsProgressDto a, KidsProgressDto b)
    {
        var levels = new SortedDictionary<int, KidsLevelProgressDto>();
        foreach (var l in a.Levels.Concat(b.Levels))
        {
            if (!levels.TryGetValue(l.Level, out var cur))
            {
                levels[l.Level] = Copy(l);
                continue;
            }
            var run = l.RunAt > cur.RunAt ? l : cur;
            levels[l.Level] = new KidsLevelProgressDto
            {
                Level = l.Level,
                Stars = Math.Max(cur.Stars, l.Stars),
                RunIndex = run.RunIndex,
                RunMistakes = run.RunMistakes,
                RunAt = run.RunAt,
            };
        }

        var courses = new SortedDictionary<int, (long ResetAt, Dictionary<int, long> Lines)>();
        foreach (var c in a.Courses.Concat(b.Courses))
        {
            if (!courses.TryGetValue(c.BookId, out var cur)) cur = (0, new Dictionary<int, long>());
            var lines = cur.Lines;
            foreach (var s in c.Solved)
                lines[s.Id] = lines.TryGetValue(s.Id, out var at) ? Math.Max(at, s.At) : s.At;
            courses[c.BookId] = (Math.Max(cur.ResetAt, c.ResetAt), lines);
        }

        return new KidsProgressDto
        {
            Levels = levels.Values.ToList(),
            Courses = courses
                .Select(kv => new KidsCourseProgressDto
                {
                    BookId = kv.Key,
                    ResetAt = kv.Value.ResetAt,
                    Solved = kv.Value.Lines
                        .Where(line => line.Value > kv.Value.ResetAt)
                        .OrderBy(line => line.Key)
                        .Select(line => new KidsSolvedLineDto { Id = line.Key, At = line.Value })
                        .ToList(),
                })
                // Ein Kurs ohne Linien und ohne „Von vorn" sagt nichts.
                .Where(c => c.ResetAt > 0 || c.Solved.Count > 0)
                .ToList(),
        };
    }

    private static KidsLevelProgressDto Copy(KidsLevelProgressDto l) => new()
    {
        Level = l.Level, Stars = l.Stars, RunIndex = l.RunIndex, RunMistakes = l.RunMistakes, RunAt = l.RunAt,
    };
}
