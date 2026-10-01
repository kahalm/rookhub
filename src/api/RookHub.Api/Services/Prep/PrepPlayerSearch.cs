using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Prep;

/// <summary>
/// Spieler im Partiebestand finden (Spielervorbereitung, Phase 2): eine Zahl sucht die FIDE-ID, sonst der Name als
/// PRÄFIX über den Index auf <c>NameKey</c> — „Carlsen", „Carlsen, M", „Magnus Carlsen", „Höcher" wie „Hoecher".
///
/// <para>Bewusst nicht wie das Megabase-Verzeichnis von LeagueHub (<see cref="LeagueMegaPlayers.SearchAsync"/>) mit
/// <c>LIKE '%wort%'</c>: das liest jede Zeile, bei 432 000 Zeilen Zehntelsekunden, beim vollen Bestand (weit über eine
/// Million Spieler) auf kaltem Puffer viele Sekunden. Ein Präfix ist ein Bereich im Index — im Index (NameKey, Games),
/// damit „die meistgespielten zuerst" keine Zeile lesen muss: gemessen 2026-10-02 an 1,09 Mio. Spielern (Lumbra doppelt, ≈ mit
/// Megabase), 128 MB Puffer, kalt: `ka%` (17 000 Kandidaten) 0,19 s statt 2,4 s mit dem Index auf NameKey allein.</para>
///
/// <para>Umlaute beidseitig: der Bestand schreibt „Höcher" (Lumbra) als „hocher", die Megabase „Hoecher" als „hoecher".
/// Getippt „Höcher" fragt beides (<see cref="LeagueRosterIndex.Spellings"/>), getippt „Hoecher" zusätzlich „hocher"
/// (<see cref="Reverse"/>) — Treffer nur über diese Rückschreibweise stehen hinten, sonst stünde bei „Michael" jeder
/// „Michal" gleichauf.</para>
/// </summary>
public sealed partial class PrepPlayerSearch(AppDbContext db)
{
    [GeneratedRegex(@"[^\p{L}\p{Nd}'-]+")]
    private static partial Regex Compound();
    [GeneratedRegex(@"[^\p{L}\p{Nd}]+")]
    private static partial Regex Atomic();

    public const int DefaultTake = 25, MaxTake = 50;
    /// <summary>So viele Kandidaten je Präfix (die meistgespielten zuerst), bevor die übrigen Wörter im Speicher prüfen.</summary>
    public const int Candidates = 300;
    /// <summary>Kürzester Präfix für den Index — ein Buchstabe wäre ein Zwanzigstel des Bestands.</summary>
    public const int MinPrefix = 2;
    /// <summary>Höchstens so viele Wörter zählen, höchstens so viele Muster gehen an die Datenbank.</summary>
    public const int MaxWords = 4, MaxPatterns = 12;

    public sealed record Hit(int Id, string Name, string? FideId, int Games, short? FirstYear, short? LastYear, short? MaxElo);

    /// <summary>Ein getipptes Wort: seine üblichen Schreibweisen und (hinten gereiht) die Rückschreibweise ohne ae/oe/ue.</summary>
    public sealed record Word(IReadOnlyList<string> Spellings, string? Reverse)
    {
        public IEnumerable<string> All => Reverse is null ? Spellings : Spellings.Append(Reverse);
    }

    /// <summary>Die Suche zerlegt: mit Komma „Nachname, Vorname", sonst Wörter in offener Reihenfolge.</summary>
    public sealed record Parsed(List<Word> Last, List<Word> First, bool Comma, string? Fide);

    /// <summary>„Hoecher" → „hocher" (der Bestand schreibt ein Lumbra-„Höcher" so); <c>null</c>, wenn es nichts umzuschreiben gibt.</summary>
    public static string? Reverse(string folded)
    {
        var r = folded.Replace("ae", "a").Replace("oe", "o").Replace("ue", "u");
        return r == folded ? null : r;
    }

    public static Parsed? Parse(string? query)
    {
        var raw = (query ?? "").Trim();
        if (LeagueRosterIndex.IsFideQuery(raw)) return new([], [], false, raw.TrimStart('0') is { Length: > 0 } f ? f : raw);
        var t = LeagueNames.StripTitles(raw).Replace('.', ' ');
        var comma = t.IndexOf(',');
        var last = Words(comma >= 0 ? t[..comma] : t);
        var first = comma >= 0 ? Words(t[(comma + 1)..]) : [];
        if (last.Count == 0) return null;
        return new(last, first.Take(Math.Max(0, MaxWords - last.Count)).ToList(), comma >= 0, null);
    }

    private static List<Word> Words(string s) => LeagueRosterIndex.QueryWords(s)
        .Select(sp => new Word(sp, sp.Select(Reverse).FirstOrDefault(r => r is not null && !sp.Contains(r))))
        .Take(MaxWords).ToList();

    /// <summary>
    /// Die Präfixe für den Index: zuerst die GENAUEN („carlsen, magnus" aus „Magnus Carlsen" oder „Carlsen, Magnus"), dann
    /// — nur wenn die nicht reichen — die LOSEN (der Nachname allein, die übrigen Wörter prüft der Speicher).
    /// </summary>
    public static (List<string> Precise, List<string> Loose) Prefixes(Parsed p)
    {
        var precise = new List<string>();
        var loose = new List<string>();
        static IEnumerable<string> Phrase(IEnumerable<Word> words) =>
            words.Aggregate(new[] { "" }.AsEnumerable(), (acc, w) => acc.SelectMany(a => w.All.Select(s => a.Length == 0 ? s : a + " " + s)));

        if (p.Comma)
        {
            foreach (var last in Phrase(p.Last))
            {
                if (p.First.Count > 0) precise.AddRange(p.First[0].All.Select(f => $"{last}, {f}"));
                else precise.Add(last);
                loose.Add(last);
            }
        }
        else if (p.Last.Count == 1)
        {
            precise.AddRange(p.Last[0].All);
        }
        else
        {
            var w = p.Last;
            // „Nachname Vorname", „Vorname Nachname", und Namen, die der Bestand ohne Komma schreibt.
            foreach (var l in Phrase(w.Take(w.Count - 1))) precise.AddRange(w[^1].All.Select(f => $"{l}, {f}"));
            foreach (var l in Phrase(w.Skip(1))) precise.AddRange(w[0].All.Select(f => $"{l}, {f}"));
            precise.AddRange(Phrase(w));
            loose.AddRange(w[0].All);
            loose.AddRange(w[^1].All);
        }
        static List<string> Clean(IEnumerable<string> l) =>
            l.Where(x => x.Length >= MinPrefix).Distinct(StringComparer.Ordinal).Take(MaxPatterns).ToList();
        var pr = Clean(precise);
        return (pr, Clean(loose).Where(x => !pr.Contains(x)).ToList());
    }

    /// <summary>
    /// Passt ein Name zur Suche? Jedes Wort muss am Anfang eines Namensteils stehen; mit Komma die Nachnamen-Wörter vor dem
    /// Komma des Namens, die Vornamen-Wörter dahinter. → (passt, nur über die Rückschreibweise, Nachname genau getroffen).
    /// </summary>
    public static (bool Ok, bool ViaReverse, bool ExactLast) Match(Parsed p, string nameKey)
    {
        // Namensteile in beiden Formen: ganz („o'kelly", „muller-ludenscheidt" — so zerlegt die Suche, was getippt wurde)
        // und in Stücken („kelly", „ludenscheidt").
        static string[] Tokens(string s) => Compound().Split(s).Concat(Atomic().Split(s)).Where(t => t.Length > 0).Distinct().ToArray();
        var comma = nameKey.IndexOf(',');
        var lastTokens = Tokens(comma >= 0 ? nameKey[..comma] : nameKey);
        var firstTokens = comma >= 0 ? Tokens(nameKey[(comma + 1)..]) : [];
        var allTokens = lastTokens.Concat(firstTokens).ToArray();
        var surname = Atomic().Split(comma >= 0 ? nameKey[..comma] : nameKey).FirstOrDefault(t => t.Length > 0) ?? "";
        var viaReverse = false;
        bool One(Word w, string[] tokens)
        {
            if (tokens.Any(t => w.Spellings.Any(s => t.StartsWith(s, StringComparison.Ordinal)))) return true;
            if (w.Reverse is { } r && tokens.Any(t => t.StartsWith(r, StringComparison.Ordinal))) { viaReverse = true; return true; }
            return false;
        }
        bool ok;
        if (p.Comma && comma >= 0)
            ok = p.Last.All(w => One(w, lastTokens)) && p.First.All(w => One(w, firstTokens));
        else
            ok = p.Last.Concat(p.First).All(w => One(w, allTokens));
        var exact = p.Last.Concat(p.First).Any(w => w.All.Contains(surname));
        return (ok, viaReverse, exact);
    }

    public async Task<List<Hit>> SearchAsync(string? query, int take, CancellationToken ct)
    {
        take = Math.Clamp(take, 1, MaxTake);
        var p = Parse(query);
        if (p is null) return [];
        if (p.Fide is { } fide)
            return await db.PrepPlayers.AsNoTracking().Where(x => x.FideId == fide).OrderByDescending(x => x.Games).Take(take)
                .Select(x => new Hit(x.Id, x.Name, x.FideId, x.Games, x.FirstYear, x.LastYear, x.MaxElo)).ToListAsync(ct);

        var (precise, loose) = Prefixes(p);
        var found = new Dictionary<int, (string Key, int Games, bool Reverse, bool Exact)>();
        async Task RunAsync(IEnumerable<string> patterns)
        {
            foreach (var pre in patterns)
            {
                var like = Escape(pre) + "%";
                // Nur Spalten aus dem Index (NameKey, Games, Id): die Kandidaten kosten keinen Zeilenzugriff.
                var rows = await db.PrepPlayers.AsNoTracking().Where(x => EF.Functions.Like(x.NameKey, like, "\\"))
                    .OrderByDescending(x => x.Games).Take(Candidates).Select(x => new { x.Id, x.NameKey, x.Games }).ToListAsync(ct);
                foreach (var r in rows)
                {
                    if (found.ContainsKey(r.Id)) continue;
                    var (ok, viaReverse, exact) = Match(p, r.NameKey);
                    if (ok) found[r.Id] = (r.NameKey, r.Games, viaReverse, exact);
                }
            }
        }
        await RunAsync(precise);
        if (found.Count < take) await RunAsync(loose);

        var ids = found.OrderBy(x => x.Value.Reverse ? 1 : 0).ThenBy(x => x.Value.Exact ? 0 : 1).ThenByDescending(x => x.Value.Games)
            .ThenBy(x => x.Value.Key, StringComparer.Ordinal).ThenBy(x => x.Key).Take(take).Select(x => x.Key).ToList();
        var full = await db.PrepPlayers.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new Hit(x.Id, x.Name, x.FideId, x.Games, x.FirstYear, x.LastYear, x.MaxElo)).ToListAsync(ct);
        return ids.Select(i => full.First(h => h.Id == i)).ToList();
    }

    /// <summary>Platzhalter von LIKE im getippten Text wörtlich nehmen.</summary>
    public static string Escape(string s) => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
