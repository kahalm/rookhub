using System.Text.RegularExpressions;

namespace RookHub.Api.Services.TrainingVideos;

/// <summary>
/// „Sagt er immer wieder“: fasst Notizen mit sehr ähnlichem Inhalt über mehrere Stunden zusammen — einfaches Verfahren
/// ohne LLM, abgestimmt an den 394 Notizen der ersten 41 Stunden (Nov. 2026):
/// <list type="number">
/// <item>Stichwörter je Notiz: nur Buchstabenfolgen ab vier Zeichen (Züge wie „Nd4“, „14...Bxg5“ fallen so heraus), klein,
/// ohne deutsche Füllwörter, grob gestutzt (<see cref="Stem"/>: „Läuferpaar/Läuferpaares“, „Bauern/Bauer“).</item>
/// <item>Gewicht je Stichwort = IDF über alle Notizen (ln N/df) — „Figur“, „Bauer“, „Stellung“ zählen wenig,
/// „halbieren“ oder „Checkliste“ viel.</item>
/// <item>Ähnlichkeit zweier Notizen = Gewicht der gemeinsamen Stichwörter / Gewicht der kleineren Menge
/// (Überlappungskoeffizient), mindestens zwei gemeinsame Stichwörter.</item>
/// <item>Gruppen per Average-Linkage: zwei Gruppen verschmelzen, solange ihre mittlere paarweise Ähnlichkeit ≥
/// <see cref="Threshold"/> ist (Single-Linkage verkettete Themen über Brückennotizen).</item>
/// <item>Ein Thema braucht Notizen aus mindestens zwei Stunden; stellvertretend steht die Notiz mit der größten
/// Ähnlichkeit zu den übrigen (bei Gleichstand die neueste).</item>
/// </list>
/// </summary>
public static class TrainingNoteThemes
{
    public const double Threshold = 0.35;
    public const int MaxThemes = 20;
    private const int MinShared = 2;

    public sealed record Input(int Id, int LessonId, DateOnly Date, int Position, string Category, string Text);

    /// <summary>Ein Thema: Notizen neueste zuerst, <c>Lessons</c> = verschiedene Stunden.</summary>
    public sealed record Theme(int RepresentativeId, string Category, int Lessons, List<int> NoteIds);

    private static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "aber", "alle", "allem", "allen", "aller", "alles", "also", "andere", "anderen", "anderer", "anderes", "auch",
        "bevor", "bist", "dabei", "damit", "dann", "dass", "daß", "dein", "deine", "dem", "denn", "dessen", "dich", "dies",
        "diese", "diesem", "diesen", "dieser", "dieses", "doch", "dort", "durch", "eine", "einem", "einen", "einer",
        "eines", "einfach", "etwas", "euch", "gegen", "gibt", "hatte", "hier", "hinter", "immer", "jede", "jedem",
        "jeden", "jeder", "jedes", "jetzt", "kann", "kein", "keine", "keinen", "mehr", "mein", "mich", "muss", "müssen",
        "nach", "nicht", "nichts", "noch", "ohne", "schon", "sehr", "sein", "seine", "seinem", "seinen", "seiner", "sich",
        "sind", "soll", "sollte", "sondern", "statt", "unter", "viel", "wenn", "werden", "wieder", "will", "wird",
        "zwar", "über", "lieber", "sonst", "erst", "können", "eher", "gleich", "ganz", "oder", "weil", "dann", "denn",
        "auf", "aus", "beim", "einmal", "selbst", "wenig", "weniger", "meist", "meistens", "oft", "manchmal", "also",
        "hier", "dass", "desto", "umso", "zuerst", "dafür", "dazu", "davon", "darauf", "deshalb", "weiter", "wurde",
        "wäre", "würde", "haben", "habe", "hast", "ihre", "ihren", "ihrer", "unsere", "beide", "beiden", "bzw",
    };

    private static readonly string[] Suffixes = { "ungen", "ung", "en", "er", "es", "em", "e", "n", "s" };
    private static readonly Regex Word = new(@"\p{L}+", RegexOptions.Compiled);

    /// <summary>Grobe Stutzung deutscher Endungen; der Rest bleibt mindestens vier Zeichen lang.</summary>
    internal static string Stem(string w)
    {
        foreach (var suf in Suffixes)
            if (w.EndsWith(suf, StringComparison.Ordinal) && w.Length - suf.Length >= 4) return w[..^suf.Length];
        return w;
    }

    internal static HashSet<string> Tokens(string? text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(text)) return set;
        foreach (Match m in Word.Matches(text))
        {
            var w = m.Value.ToLowerInvariant();
            if (w.Length < 4 || Stop.Contains(w)) continue;
            set.Add(Stem(w));
        }
        return set;
    }

    public static List<Theme> Find(IReadOnlyList<Input> notes)
    {
        var n = notes.Count;
        if (n < 2) return new();
        var tokens = notes.Select(x => Tokens(x.Text)).ToArray();
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var set in tokens)
            foreach (var t in set) df[t] = df.TryGetValue(t, out var c) ? c + 1 : 1;
        var idf = df.ToDictionary(kv => kv.Key, kv => Math.Log((double)n / kv.Value), StringComparer.Ordinal);
        var weight = tokens.Select(set => set.Sum(t => idf[t])).ToArray();

        // Paarweise Ähnlichkeit (symmetrisch).
        var sim = new double[n, n];
        for (var i = 0; i < n; i++)
        {
            if (weight[i] <= 0) continue;
            for (var j = i + 1; j < n; j++)
            {
                if (weight[j] <= 0) continue;
                var shared = 0;
                var w = 0.0;
                var (small, big) = tokens[i].Count <= tokens[j].Count ? (tokens[i], tokens[j]) : (tokens[j], tokens[i]);
                foreach (var t in small)
                    if (big.Contains(t)) { shared++; w += idf[t]; }
                if (shared < MinShared) continue;
                var s = w / Math.Min(weight[i], weight[j]);
                sim[i, j] = s;
                sim[j, i] = s;
            }
        }

        // Average-Linkage: sum[a,b] = Summe der Paar-Ähnlichkeiten zwischen den Gruppen a und b.
        var members = Enumerable.Range(0, n).Select(i => new List<int> { i }).ToArray();
        var alive = Enumerable.Repeat(true, n).ToArray();
        var sum = (double[,])sim.Clone();
        while (true)
        {
            int bestA = -1, bestB = -1;
            var best = Threshold;
            for (var a = 0; a < n; a++)
            {
                if (!alive[a]) continue;
                for (var b = a + 1; b < n; b++)
                {
                    if (!alive[b] || sum[a, b] <= 0) continue;
                    var avg = sum[a, b] / (members[a].Count * members[b].Count);
                    if (avg > best || (avg == best && bestA < 0)) { best = avg; bestA = a; bestB = b; }
                }
            }
            if (bestA < 0) break;
            members[bestA].AddRange(members[bestB]);
            alive[bestB] = false;
            for (var c = 0; c < n; c++)
            {
                if (!alive[c] || c == bestA) continue;
                sum[bestA, c] += sum[bestB, c];
                sum[c, bestA] = sum[bestA, c];
            }
        }

        var themes = new List<(Theme Theme, int Size, DateOnly Newest)>();
        for (var a = 0; a < n; a++)
        {
            if (!alive[a]) continue;
            var group = members[a];
            var lessons = group.Select(i => notes[i].LessonId).Distinct().Count();
            if (lessons < 2) continue;
            var rep = group
                .OrderByDescending(i => group.Where(j => j != i).Sum(j => sim[i, j]))
                .ThenByDescending(i => notes[i].Date).ThenBy(i => notes[i].Position)
                .First();
            var category = group.GroupBy(i => notes[i].Category)
                .OrderByDescending(g => g.Count()).ThenBy(g => g.Key == notes[rep].Category ? 0 : 1).ThenBy(g => g.Key, StringComparer.Ordinal)
                .First().Key;
            var ordered = group.OrderByDescending(i => notes[i].Date).ThenBy(i => notes[i].Position).ToList();
            themes.Add((new Theme(notes[rep].Id, category, lessons, ordered.Select(i => notes[i].Id).ToList()),
                group.Count, notes[ordered[0]].Date));
        }
        return themes
            .OrderByDescending(t => t.Theme.Lessons).ThenByDescending(t => t.Size).ThenByDescending(t => t.Newest)
            .ThenBy(t => t.Theme.RepresentativeId)
            .Take(MaxThemes)
            .Select(t => t.Theme)
            .ToList();
    }
}
