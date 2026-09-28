using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.League;

/// <summary>
/// Wer steht in einer Meldeliste der Liga (irgendeine Saison, irgendein Team)? Grundlage der Vereins-Datenbank: eine
/// Partie ohne einen einzigen Ligaspieler nützt der Vorbereitung nichts und wird nicht angenommen.
///
/// <para><b>Zuerst die FIDE-ID</b> (Kopfzeile <c>WhiteFideId</c> u. ä.), dann der Name in drei Stufen, jede nur, wenn die
/// vorige nichts fand: (1) alle Namensteile gleich, in beliebiger Reihenfolge („Schnabl, Andreas" = „Andreas Schnabl");
/// (2) Nachname + erster Vorname („Schnabl, Andreas Johann"); (3) Nachname + Anfangsbuchstabe („Schnabl, A.").
/// Verglichen wird ohne Groß/klein, Akzente und akademische Titel, Umlaute in beiden Schreibweisen („Müller" =
/// „Mueller" = „Muller").</para>
///
/// <para><b>Mehrdeutig heißt: Ligaspieler, aber OHNE FIDE-ID</b> — zwei gleichnamige Ligaspieler zu verwechseln wäre
/// schlimmer, als die Partie keinem Profil zuzuordnen. Nennt die Partie eine FIDE-ID, die zu keinem Ligaspieler gehört,
/// zählt ein Namenstreffer nur, wenn dieser Ligaspieler keine eigene FIDE-ID hat (sonst ist es ein Namensvetter).</para>
/// </summary>
public sealed class LeagueRosterIndex
{
    /// <summary>Eine Zeile der Meldelisten; <see cref="Season"/> = „2026/27" (leer, wenn das Turnier fehlt).</summary>
    public sealed record Row(int Tnr, string Team, string Name, string NameKey, string? Fide, string Season = "");

    /// <summary>Ein Ligaspieler; <see cref="Name"/> in der jüngsten Schreibweise der Meldelisten. <see cref="OwnClub"/>:
    /// in seiner jüngsten Saison für den eigenen Verein gemeldet (<see cref="LeagueRefresh.OwnTeam"/>) — wer von Schwaz
    /// weggegangen ist, ist jetzt ein Gegner, wer dazugekommen ist, einer von uns.</summary>
    public sealed record Person(string Key, string? Fide, string Name, IReadOnlyList<string> Teams, bool OwnClub = false);

    /// <summary>Ergebnis des Abgleichs. <see cref="Candidates"/> = alle gleich gut passenden Ligaspieler (bei einem
    /// eindeutigen Treffer genau einer).</summary>
    public sealed record Hit(bool League, Person? Person, IReadOnlyList<Person> Candidates)
    {
        public bool Ambiguous => League && Person is null;
        /// <summary>Ein Spieler des eigenen Vereins — bei Mehrdeutigkeit nur, wenn ALLE Kandidaten es sind.</summary>
        public bool OwnClub => Candidates.Count > 0 && Candidates.All(c => c.OwnClub);
    }

    public static readonly Hit None = new(false, null, Array.Empty<Person>());

    private readonly Dictionary<string, Person> _byFide = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<Person>> _byName = new(StringComparer.Ordinal);

    public IReadOnlyList<Person> People { get; }

    public LeagueRosterIndex(IEnumerable<Row> rows)
    {
        var people = new List<Person>();
        foreach (var g in rows.GroupBy(r => LeagueNames.Pid(string.IsNullOrWhiteSpace(r.Fide) ? null : r.Fide, r.NameKey)))
        {
            var ordered = g.OrderByDescending(r => r.Season, StringComparer.Ordinal).ThenByDescending(r => r.Tnr).ToList();
            var fide = string.IsNullOrWhiteSpace(ordered[0].Fide) ? null : ordered[0].Fide!.Trim();
            var latest = ordered[0].Season;
            var own = ordered.Where(r => r.Season == latest)
                .Any(r => string.Equals(LeagueNames.Clean(r.Team).TrimEnd('/', '-', ' '), LeagueRefresh.OwnTeam, StringComparison.OrdinalIgnoreCase));
            var p = new Person(g.Key, fide, LeagueNames.Clean(ordered[0].Name),
                ordered.Select(r => r.Team).Distinct(StringComparer.Ordinal).ToList(), own);
            people.Add(p);
            if (fide != null) _byFide[fide] = p;
            foreach (var name in ordered.Select(r => r.Name).Distinct(StringComparer.Ordinal))
                foreach (var k in PersonKeys(name))
                {
                    if (!_byName.TryGetValue(k, out var set)) _byName[k] = set = new HashSet<Person>();
                    set.Add(p);
                }
        }
        People = people;
    }

    /// <summary>Der Ligaspieler mit dieser FIDE-ID, sonst <c>null</c>.</summary>
    public Person? ByFide(string? fide) =>
        !string.IsNullOrWhiteSpace(fide) && _byFide.TryGetValue(fide.Trim(), out var p) ? p : null;

    /// <summary>Wer ist das? <paramref name="fide"/> = FIDE-ID aus der Partie (darf fehlen).</summary>
    public Hit Match(string? name, string? fide)
    {
        fide = string.IsNullOrWhiteSpace(fide) ? null : fide.Trim();
        if (fide != null && _byFide.TryGetValue(fide, out var byId)) return new Hit(true, byId, new[] { byId });
        foreach (var stage in QueryKeys(name ?? string.Empty))
        {
            var found = stage.SelectMany(k => _byName.TryGetValue(k, out var s) ? s : Enumerable.Empty<Person>())
                .Distinct().ToList();
            // Eine fremde FIDE-ID in der Partie: nur Ligaspieler ohne eigene ID kommen als dieselbe Person in Frage.
            if (fide != null) found = found.Where(p => p.Fide is null).ToList();
            if (found.Count == 0) continue;
            return new Hit(true, found.Count == 1 ? found[0] : null, found);
        }
        return None;
    }

    /// <summary>Vorschläge beim Eintippen: Namensteile, die mit dem Getippten BEGINNEN (alle Wörter).</summary>
    public IEnumerable<Person> Suggest(string query, int take)
    {
        var words = Tokens(Fold(query, false));
        if (words.Count == 0 || words.All(w => w.Length < 2)) return Enumerable.Empty<Person>();
        return People.Where(p =>
            {
                var own = Tokens(Fold(p.Name, false)).Concat(Tokens(Fold(p.Name, true))).ToList();
                return words.All(w => own.Any(o => o.StartsWith(w, StringComparison.Ordinal)));
            })
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Take(take);
    }

    // ── Namensschlüssel ─────────────────────────────────────────────

    internal static string Fold(string s, bool transliterate)
    {
        s = s.ToLowerInvariant();
        if (transliterate) s = s.Replace("ä", "ae").Replace("ö", "oe").Replace("ü", "ue");
        s = s.Replace("ß", "ss");
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    private static List<string> Tokens(string s) =>
        Regex.Split(s, @"[^\p{L}\p{Nd}'-]+").Where(t => t.Length > 0).ToList();

    /// <summary>Nachname(n) und Vorname(n). Mit Komma eindeutig; ohne Komma schreiben die Meldelisten „Nachname
    /// Vorname" (2022/23) — bei einer Partie ist die Reihenfolge offen, deshalb liefert <see cref="QueryKeys"/> beide.</summary>
    private static (List<string> Last, List<string> First, bool Comma) Parts(string name, bool transliterate)
    {
        var key = Fold(LeagueNames.NameKey(name), transliterate);
        var comma = key.IndexOf(',');
        if (comma >= 0) return (Tokens(key[..comma]), Tokens(key[(comma + 1)..]), true);
        var all = Tokens(key);
        return (all.Take(1).ToList(), all.Skip(1).ToList(), false);
    }

    private static string Sorted(IEnumerable<string> tokens) => string.Join(' ', tokens.OrderBy(t => t, StringComparer.Ordinal));

    private static IEnumerable<string> PersonKeys(string name)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var tr in new[] { false, true })
        {
            var (last, first, _) = Parts(name, tr);
            if (last.Count + first.Count == 0) continue;
            keys.Add("f:" + Sorted(last.Concat(first)));
            if (last.Count > 0 && first.Count > 0)
            {
                keys.Add("g:" + Sorted(last) + "|" + first[0]);
                keys.Add("i:" + Sorted(last) + "|" + first[0][0]);
            }
        }
        return keys;
    }

    /// <summary>Die drei Stufen (je eine Schlüsselmenge), in dieser Reihenfolge versucht.</summary>
    private static IEnumerable<List<string>> QueryKeys(string name)
    {
        var full = new List<string>();
        var given = new List<string>();
        var initial = new List<string>();
        foreach (var tr in new[] { false, true })
        {
            var (last, first, comma) = Parts(name, tr);
            var all = last.Concat(first).ToList();
            if (all.Count == 0) continue;
            full.Add("f:" + Sorted(all));
            // Ohne Komma: der Vorname steht vorn („Andreas Schnabl", „A. Schnabl") ODER hinten („Schnabl Andreas").
            var splits = comma ? new[] { (last, first) }
                : all.Count >= 2 ? new[] { (all.Skip(1).ToList(), all.Take(1).ToList()), (all.Take(all.Count - 1).ToList(), all.Skip(all.Count - 1).ToList()) }
                : Array.Empty<(List<string>, List<string>)>();
            foreach (var (l, f) in splits)
            {
                if (l.Count == 0 || f.Count == 0) continue;
                if (f[0].Length > 1) given.Add("g:" + Sorted(l) + "|" + f[0]);
                else initial.Add("i:" + Sorted(l) + "|" + f[0]);
            }
        }
        yield return full;
        yield return given;
        yield return initial;
    }
}
