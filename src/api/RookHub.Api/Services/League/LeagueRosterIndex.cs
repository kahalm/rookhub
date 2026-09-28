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
/// „Mueller" = „Muller"). Nennt die Partie NUR einen Nachnamen („Kostic"), gilt als vierte Stufe der Nachname allein —
/// eindeutig nur bei genau einem Ligaspieler dieses Namens (<see cref="Hit.LastNameOnly"/>).</para>
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
    /// <para><see cref="LastNameOnly"/>: die Partie nannte NUR den Nachnamen („Kostic") — Treffer über den Nachnamen allein,
    /// die Oberfläche zeigt ihn als „nur Nachname" zum Prüfen.</para>
    public sealed record Hit(bool League, Person? Person, IReadOnlyList<Person> Candidates, bool LastNameOnly = false)
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
        static string? Norm(string? f) => string.IsNullOrWhiteSpace(f) ? null : f.Trim();
        var list = rows.ToList();
        // Eine Zeile OHNE FIDE-ID gehört zu der Person MIT FIDE-ID, deren Name genau so lautet (alle Namensteile gleich) —
        // wenn es genau eine solche gibt. Die Meldelisten 2022/23 schreiben „Hengl Philip" ohne Komma und oft ohne ID; als
        // eigene Person stünde sie neben „Hengl, Philip" (FIDE-ID), und jeder Abgleich des Namens wäre „mehrdeutig".
        var byFull = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var r in list)
            if (Norm(r.Fide) is { } f)
                foreach (var k in PersonKeys(r.Name).Where(k => k.StartsWith("f:", StringComparison.Ordinal)))
                {
                    if (!byFull.TryGetValue(k, out var set)) byFull[k] = set = new HashSet<string>(StringComparer.Ordinal);
                    set.Add(f);
                }
        string KeyOf(Row r)
        {
            if (Norm(r.Fide) is { } f) return f;
            var fides = PersonKeys(r.Name).Where(k => k.StartsWith("f:", StringComparison.Ordinal))
                .SelectMany(k => byFull.TryGetValue(k, out var set) ? set : Enumerable.Empty<string>()).Distinct().ToList();
            return fides.Count == 1 ? fides[0] : LeagueNames.Pid(null, r.NameKey);
        }

        var people = new List<Person>();
        foreach (var g in list.GroupBy(KeyOf))
        {
            var ordered = g.OrderByDescending(r => r.Season, StringComparer.Ordinal).ThenByDescending(r => r.Tnr).ToList();
            var fide = ordered.Select(r => Norm(r.Fide)).FirstOrDefault(f => f != null);
            var latest = ordered[0].Season;
            var own = ordered.Where(r => r.Season == latest)
                .Any(r => string.Equals(LeagueNames.Clean(r.Team).TrimEnd('/', '-', ' '), LeagueRefresh.OwnTeam, StringComparison.OrdinalIgnoreCase));
            // Anzeige in der jüngsten Schreibweise MIT Komma („Nachname, Vorname"), sonst der jüngsten überhaupt.
            var shown = (ordered.FirstOrDefault(r => r.Name.Contains(',')) ?? ordered[0]).Name;
            var p = new Person(g.Key, fide, LeagueNames.Clean(shown),
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
        var stageNo = 0;
        foreach (var stage in QueryKeys(name ?? string.Empty))
        {
            stageNo++;
            var found = stage.SelectMany(k => _byName.TryGetValue(k, out var s) ? s : Enumerable.Empty<Person>())
                .Distinct().ToList();
            // Eine fremde FIDE-ID in der Partie: nur Ligaspieler ohne eigene ID kommen als dieselbe Person in Frage.
            if (fide != null) found = found.Where(p => p.Fide is null).ToList();
            if (found.Count == 0) continue;
            return new Hit(true, found.Count == 1 ? found[0] : null, found, stageNo == 4);
        }
        return None;
    }

    /// <summary>Vorschläge beim Eintippen („like", Wunsch 2026-09-28): jedes getippte Wort muss IRGENDWO im Namen stehen
    /// („bert rud" findet „Bertl, Rudolf", „ertl" auch „Bertl"), in jeder Umlaut-Schreibweise (<see cref="Spellings"/>);
    /// wer alle Wörter als Wortanfang trägt, steht vorn. Eine reine Zahl ist eine FIDE-ID.</summary>
    public IEnumerable<Person> Suggest(string query, int take)
    {
        if (IsFideQuery(query)) return ByFide(query) is { } byId ? new[] { byId } : Enumerable.Empty<Person>();
        var words = QueryWords(LeagueNames.StripTitles(query));
        if (words.Count == 0 || words.All(w => w[0].Length < 2)) return Enumerable.Empty<Person>();
        return People.Select(p =>
            {
                var folded = Fold(p.Name, false) + " " + Fold(p.Name, true);
                if (!words.All(w => w.Any(v => folded.Contains(v, StringComparison.Ordinal)))) return (p, Rank: -1);
                var own = Tokens(folded);
                return (p, Rank: words.All(w => own.Any(o => w.Any(v => o.StartsWith(v, StringComparison.Ordinal)))) ? 0 : 1);
            })
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank).ThenBy(x => x.p.Name, StringComparer.CurrentCultureIgnoreCase).Take(take).Select(x => x.p);
    }

    /// <summary>
    /// Ähnlich geschriebene Ligaspieler für einen Namen, den <see cref="Match"/> nicht fand (0.596.0, Wunsch 2026-09-28:
    /// „wenn du Namen nicht direkt findest, schau, ob ein ähnlicher Name bei den Ligaspielern existiert — zur
    /// Schnellauswahl"). Je Namensteil der Abstand in Tippfehlern (Einfügen, Weglassen, Ersetzen, zwei vertauschte
    /// Buchstaben), in beiden Umlaut-Schreibweisen, Reihenfolge egal; eine Initiale passt zu jedem Vornamen mit diesem
    /// Buchstaben. Ein Kandidat braucht für JEDEN Namensteil der Partie einen passenden Teil mit höchstens
    /// <see cref="Tolerance"/> Fehlern, und der längste Teil (meist der Nachname) muss in der Länge passen — sonst würde
    /// aus „Maier" jeder „Mayr". Die besten zuerst, höchstens <paramref name="take"/>.
    /// </summary>
    public IReadOnlyList<Person> Similar(string? name, int take = 3)
    {
        var words = new[] { false, true }.Select(tr => Tokens(Fold(LeagueNames.NameKey(LeagueNames.StripTitles(name ?? "")), tr)
            .Replace(",", " "))).Where(w => w.Count > 0).Distinct(new SeqEq()).ToList();
        if (words.Count == 0 || words[0].All(t => t.Length < 3)) return [];
        _similarTokens ??= People.Select(p => (p, Tokens: new[] { false, true }
            .SelectMany(tr => Tokens(Fold(LeagueNames.NameKey(p.Name), tr).Replace(",", " "))).Distinct().ToArray())).ToList();
        var scored = new List<(Person P, int Score)>();
        foreach (var (p, own) in _similarTokens)
        {
            var best = int.MaxValue;
            foreach (var q in words)
            {
                var longest = q.OrderByDescending(t => t.Length).First();
                if (!own.Any(o => Math.Abs(o.Length - longest.Length) <= Tolerance(longest.Length))) continue;
                var total = 0;
                foreach (var t in q)
                {
                    var d = t.Length == 1 ? (own.Any(o => o[0] == t[0]) ? 0 : 99)
                        : own.Min(o => Math.Abs(o.Length - t.Length) > Tolerance(t.Length) ? 99 : Distance(t, o));
                    if (d > Tolerance(t.Length)) { total = int.MaxValue; break; }
                    total += d;
                }
                best = Math.Min(best, total);
            }
            if (best is > 0 and < int.MaxValue) scored.Add((p, best));
        }
        return scored.OrderBy(x => x.Score).ThenBy(x => x.P.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(take).Select(x => x.P).ToList();
    }

    /// <summary>Wie viele Tippfehler ein Namensteil haben darf: kurze keinen („Wolf" ≠ „Golf"), bis 7 Buchstaben einen, länger zwei.</summary>
    internal static int Tolerance(int length) => length < 5 ? 0 : length < 8 ? 1 : 2;

    private List<(Person P, string[] Tokens)>? _similarTokens;

    /// <summary>Tippfehler-Abstand (Damerau-Levenshtein, benachbarte Vertauschung = ein Fehler).</summary>
    internal static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1]) d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
            }
        return d[a.Length, b.Length];
    }

    private sealed class SeqEq : IEqualityComparer<List<string>>
    {
        public bool Equals(List<string>? x, List<string>? y) => x != null && y != null && x.SequenceEqual(y);
        public int GetHashCode(List<string> o) => string.Join(' ', o).GetHashCode();
    }

    /// <summary>Eine reine Zahl (4–12 Ziffern) sucht die FIDE-ID statt eines Namens.</summary>
    public static bool IsFideQuery(string? query) =>
        query?.Trim() is { Length: >= 4 and <= 12 } t && t.All(char.IsAsciiDigit);

    /// <summary>
    /// Die Schreibweisen eines getippten Worts, klein und ohne Akzente: „Höcher" = „hocher" und „hoecher". ChessBase
    /// schreibt Umlaute IMMER aus (im Megabase-Verzeichnis steht kein einziges „ä/ö/ü", „Hoecher, Michael"), die
    /// Meldelisten meist nicht — gemeldet 2026-09-28: „Höcher" fand den Spieler in der Megabase nicht. Den umgekehrten
    /// Weg („oe" → „o") braucht es deshalb nicht; er machte auch aus „Michael" ein „Michal".
    /// </summary>
    public static IReadOnlyList<string> Spellings(string word)
    {
        var list = new List<string>(2);
        foreach (var s in new[] { Fold(word, false), Fold(word, true) })
            if (s.Length > 0 && !list.Contains(s)) list.Add(s);
        return list;
    }

    /// <summary>Die Wörter einer Suche, je mit ihren Schreibweisen.</summary>
    public static List<IReadOnlyList<string>> QueryWords(string query) =>
        Tokens(query.ToLowerInvariant()).Select(Spellings).Where(v => v.Count > 0).ToList();

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
        var key = Fold(LeagueNames.NameKey(LeagueNames.StripTitles(name)), transliterate);
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
            if (last.Count > 0) keys.Add("l:" + Sorted(last));
            if (last.Count > 0 && first.Count > 0)
            {
                keys.Add("g:" + Sorted(last) + "|" + first[0]);
                keys.Add("i:" + Sorted(last) + "|" + first[0][0]);
            }
        }
        return keys;
    }

    /// <summary>Die vier Stufen (je eine Schlüsselmenge), in dieser Reihenfolge versucht.</summary>
    private static IEnumerable<List<string>> QueryKeys(string name)
    {
        var full = new List<string>();
        var given = new List<string>();
        var initial = new List<string>();
        var lastOnly = new List<string>();
        foreach (var tr in new[] { false, true })
        {
            var (last, first, comma) = Parts(name, tr);
            var all = last.Concat(first).ToList();
            if (all.Count == 0) continue;
            full.Add("f:" + Sorted(all));
            // Nur ein Nachname („Kostic", „Kostic, ?"): vierte Stufe, eindeutig nur, wenn ihn genau ein Ligaspieler trägt.
            if (comma ? first.Count == 0 && last.Count > 0 : all.Count == 1) lastOnly.Add("l:" + Sorted(comma ? last : all));
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
        yield return lastOnly;
    }
}
