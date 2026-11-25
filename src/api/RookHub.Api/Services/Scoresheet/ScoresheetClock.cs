using System.Globalization;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Scoresheet;

/// <summary>
/// Handschriftlich notierte Restzeiten (1.015.0, Wunsch 2026-11-25): viele Spieler schreiben neben ihren Zug, wie viel Zeit
/// ihnen danach noch bleibt („1:12", „72", „72'"). Das Modell liefert sie roh je Eintrag (<c>clock</c>), hier werden sie
/// gelesen, auf Plausibilität geprüft und an die Halbzüge gehängt — im PGN stehen sie dann als <c>[%clk h:mm:ss]</c>.
/// Rein, ohne Datenbank.
/// </summary>
public static partial class ScoresheetClock
{
    /// <summary>Mehr Restzeit als 6 Stunden ist kein Uhrenstand, sondern ein Lesefehler (Datum, Zugnummer …).</summary>
    public const int MaxSeconds = 6 * 3600;

    /// <summary>
    /// So viel darf die Restzeit EINER Seite von einem notierten Wert zum nächsten steigen: Zuschlag je Zug plus die
    /// Zeitkontrolle nach Zug 40 (typisch 30 min + 30 s). Mehr ist fast immer ein verlesener Wert („12" statt „72").
    /// </summary>
    public const int JumpToleranceSeconds = 35 * 60;

    /// <summary>
    /// Eine notierte Restzeit → Sekunden; <c>null</c>, wenn es keine ist. Gelesen werden:
    /// <c>1:12</c>/<c>1.12</c>/<c>1,12</c>/<c>1h12</c> (Stunden:Minuten, solange vorn höchstens 9 steht — sonst
    /// Minuten:Sekunden wie <c>45:30</c>), <c>0:05:30</c> (h:mm:ss), <c>72</c>/<c>72'</c>/<c>72min</c> (Minuten) und
    /// <c>72'30</c>/<c>72'30''</c> (Minuten und Sekunden).
    /// </summary>
    public static int? Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = Regex.Replace(raw.Trim().ToLowerInvariant(), @"\s+", "")
            .Replace('’', '\'').Replace('′', '\'').Replace('″', '"').Replace("''", "\"");
        int? seconds = null;
        if (Hms().Match(t) is { Success: true } a)
            seconds = Time(N(a, 1), N(a, 2), N(a, 3));
        else if (HourMinute().Match(t) is { Success: true } b)
        {
            int x = N(b, 1), y = N(b, 3);
            var hours = b.Groups[2].Value == "h" || (x <= 9 && !b.Groups[4].Value.Contains('"'));
            seconds = hours ? Time(x, y, 0) : Time(0, x, y);
        }
        else if (MinuteSecond().Match(t) is { Success: true } c)
            seconds = Time(0, N(c, 1), N(c, 2));
        else if (Minutes().Match(t) is { Success: true } d)
            seconds = N(d, 1) * 60;
        return seconds is int s && s <= MaxSeconds ? s : null;

        static int N(Match m, int g) => int.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
        // Minuten dürfen nur ohne Stunden über 59 gehen („72:30" = 72 min 30 s), Sekunden nie.
        static int? Time(int h, int m, int s) => s > 59 || (h > 0 && m > 59) ? null : h * 3600 + m * 60 + s;
    }

    /// <summary>Normalform <c>h:mm:ss</c> einer notierten Zeit, <c>null</c> = keine lesbare.</summary>
    public static string? Normalize(string? raw) => Parse(raw) is int s ? PgnClock.Format(s) : null;

    /// <summary>
    /// Je Halbzug die Restzeit, soweit sie zu den übrigen Werten derselben Seite passt: behalten wird die längste Folge, in
    /// der kein Wert mehr als <see cref="JumpToleranceSeconds"/> über dem vorigen liegt (Restzeit fällt, bis auf Zuschlag und
    /// Zeitkontrolle). Ein einzelner verlesener Ausreißer fällt so weg, ohne die guten Werte danach mitzunehmen.
    /// </summary>
    /// <remarks>Die Seiten sind die geraden und die ungeraden Indizes — welche davon Weiß ist, spielt keine Rolle.</remarks>
    public static List<int?> Plausible(IReadOnlyList<int?> perPly)
    {
        var result = new List<int?>(perPly);
        for (var side = 0; side < 2; side++)
        {
            var idx = Enumerable.Range(0, perPly.Count)
                .Where(i => i % 2 == side && perPly[i] is not null).ToList();
            if (idx.Count < 2) continue;
            // Längste Kette (O(n²), n ≤ 300): len[k] = längste gültige Folge, die bei idx[k] endet.
            var len = new int[idx.Count];
            var prev = new int[idx.Count];
            for (var k = 0; k < idx.Count; k++)
            {
                len[k] = 1; prev[k] = -1;
                for (var j = 0; j < k; j++)
                    if (perPly[idx[k]]!.Value <= perPly[idx[j]]!.Value + JumpToleranceSeconds && len[j] + 1 > len[k])
                    { len[k] = len[j] + 1; prev[k] = j; }
            }
            var best = 0;
            for (var k = 1; k < idx.Count; k++) if (len[k] > len[best]) best = k;
            var keep = new HashSet<int>();
            for (var k = best; k >= 0; k = prev[k]) keep.Add(idx[k]);
            foreach (var i in idx) if (!keep.Contains(i)) result[i] = null;
        }
        return result;
    }

    /// <summary>
    /// Die Restzeiten der Formular-Einträge an die aufgelösten Halbzüge hängen (<see cref="ScoresheetPly.Clock"/>): je Halbzug
    /// die Zeit seines Eintrags (<see cref="ScoresheetPly.W"/>), geprüft mit <see cref="Plausible"/>. Eingefügte Halbzüge
    /// (ohne Eintrag) haben keine.
    /// </summary>
    public static ScoresheetResolution Attach(ScoresheetResolution r, ScoresheetTranscription t)
    {
        var raw = r.Plies.Select(p => p.W is int w && w >= 0 && w < t.Moves.Count ? Parse(t.Moves[w].Clock) : null).ToList();
        var ok = Plausible(raw);
        for (var i = 0; i < r.Plies.Count; i++)
            r.Plies[i].Clock = ok[i] is int s ? PgnClock.Format(s) : null;
        return r;
    }

    [GeneratedRegex(@"^(\d{1,2})[:.,h](\d{2})[:.,'](\d{2})""?$")]
    private static partial Regex Hms();

    [GeneratedRegex(@"^(\d{1,3})([:.,h])(\d{2})(['""]*|min|m)?$")]
    private static partial Regex HourMinute();

    [GeneratedRegex(@"^(\d{1,3})'(\d{1,2})""?$")]
    private static partial Regex MinuteSecond();

    [GeneratedRegex(@"^(\d{1,3})(?:'|min|m)?$")]
    private static partial Regex Minutes();
}
