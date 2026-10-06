using System.Text.RegularExpressions;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Die Zeitplan-Regeln der Engine-Clients in C# (0.679.0) — dieselbe Sprache und dieselbe Rechnung wie
/// <c>engine-provider/entrypoint.sh</c> und <c>engine-provider/windows/run_provider.ps1</c>. RookHub rechnet damit
/// nach, welche Engine eines Clients gerade läuft, statt sie anzuschreiben und nach 15 Sekunden einen 503 zu kassieren.
///
/// <para><b>Regeln</b>, mit <c>;</c> getrennt, je Regel <c>&lt;Tage&gt; &lt;von&gt;-&lt;bis&gt; &lt;Prozent&gt;</c> —
/// „Mo-Do 08:00-17:00 0%; Fr 08:00-14:00 25%; Sa,So 100%". Tage deutsch oder englisch, Bereiche (auch über das
/// Wochenende: fr-mo), Listen, <c>*</c>. Das Ende einer Spanne zählt nicht mehr dazu, eine Spanne darf über
/// Mitternacht gehen, ohne Uhrzeit gilt die Regel den ganzen Tag. Erste passende Regel gewinnt, keine = 100 %.</para>
///
/// <para><b>Was läuft:</b> 0 % = nichts, 100 % = alles. Dazwischen entscheidet der Scope: <c>background</c> (Vorgabe)
/// lässt die Live-Engine (Platz 1) an und teilt die Hintergrund-Engines, <c>all</c> teilt alle zusammen. Gerundet
/// wird wie im Client mit Ganzzahlen (<c>(n · p + 50) / 100</c>) — eine Abweichung um eine Engine hiesse, RookHub
/// schickt Arbeit an eine Engine, die der Client gerade abgeschaltet hat.</para>
/// </summary>
public static class EngineScheduleRules
{
    public const string ScopeBackground = "background";
    public const string ScopeAll = "all";
    public const int MaxRuleLength = 500;

    /// <summary>Eine Regel: Wochentage nach ISO (1 = Montag … 7 = Sonntag), Minuten seit Mitternacht, Prozent.</summary>
    public sealed record Rule(IReadOnlySet<int> Days, int From, int To, int Percent);

    private static readonly Dictionary<string, int> DayNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["mo"] = 1, ["mon"] = 1, ["montag"] = 1, ["monday"] = 1,
        ["di"] = 2, ["die"] = 2, ["tue"] = 2, ["tuesday"] = 2, ["dienstag"] = 2,
        ["mi"] = 3, ["mit"] = 3, ["wed"] = 3, ["wednesday"] = 3, ["mittwoch"] = 3,
        ["do"] = 4, ["don"] = 4, ["thu"] = 4, ["thursday"] = 4, ["donnerstag"] = 4,
        ["fr"] = 5, ["fre"] = 5, ["fri"] = 5, ["friday"] = 5, ["freitag"] = 5,
        ["sa"] = 6, ["sam"] = 6, ["sat"] = 6, ["saturday"] = 6, ["samstag"] = 6,
        ["so"] = 7, ["son"] = 7, ["sun"] = 7, ["sunday"] = 7, ["sonntag"] = 7,
    };

    private static readonly HashSet<string> EveryDay = new(StringComparer.OrdinalIgnoreCase)
        { "*", "daily", "all", "taeglich", "täglich", "immer" };

    private static readonly Regex TimeRe = new(@"^(\d{1,2}):(\d{2})$", RegexOptions.Compiled);
    private static readonly Regex PercentRe = new(@"^(\d+)%?$", RegexOptions.Compiled);

    /// <summary>Die Regeln aus der Angabe; leer = keine. <see cref="FormatException"/> mit einem Satz, der das falsche
    /// Stück nennt — dieselben Fälle, an denen auch der Client beim Start abbricht.</summary>
    public static IReadOnlyList<Rule> Parse(string? spec)
    {
        var rules = new List<Rule>();
        if (string.IsNullOrWhiteSpace(spec)) return rules;
        if (spec.Length > MaxRuleLength) throw new FormatException($"Der Zeitplan ist länger als {MaxRuleLength} Zeichen.");
        foreach (var raw in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var f = raw.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // Kurzform ohne Uhrzeit: „Sa,So 100%" meint den ganzen Tag.
            if (f.Length == 2 && PercentRe.IsMatch(f[1])) f = [f[0], "00:00-24:00", f[1]];
            if (f.Length != 3)
                throw new FormatException($"Regel „{raw}\" muss „<Tage> <von>-<bis> <Prozent>\" sein, z. B. „Mo-Do 08:00-17:00 0%\".");
            var days = Days(f[0]);
            var span = f[1].Split('-');
            if (span.Length != 2 || Minutes(span[0]) is not { } from || Minutes(span[1]) is not { } to)
                throw new FormatException($"„{f[1]}\" ist keine Zeitspanne HH:MM-HH:MM.");
            var pm = PercentRe.Match(f[2]);
            if (!pm.Success || !int.TryParse(pm.Groups[1].Value, out var pct) || pct > 100)
                throw new FormatException($"„{f[2]}\" ist kein Prozentwert von 0 bis 100.");
            rules.Add(new Rule(days, from, to, pct));
        }
        if (rules.Count == 0) throw new FormatException("Der Zeitplan enthält keine Regel.");
        return rules;
    }

    /// <summary>Prozentsatz für ISO-Wochentag <paramref name="isoDay"/> und Minute <paramref name="minute"/>.</summary>
    public static int PercentAt(IReadOnlyList<Rule> rules, int isoDay, int minute)
    {
        foreach (var r in rules)
        {
            if (!r.Days.Contains(isoDay)) continue;
            var hit = r.From == r.To                                  // „00:00-00:00" bzw. Kurzform = ganzer Tag
                || (r.From < r.To ? minute >= r.From && minute < r.To
                                  : minute >= r.From || minute < r.To); // über Mitternacht
            if (hit) return r.Percent;
        }
        return 100;
    }

    /// <summary>Wie viele Engines eines Clients laufen bei <paramref name="percent"/> Prozent.</summary>
    public static int TargetCount(int percent, int total, string? scope)
    {
        if (percent <= 0 || total <= 0) return 0;
        if (percent >= 100) return total;
        if (string.Equals(scope, ScopeAll, StringComparison.OrdinalIgnoreCase))
            return Math.Max(1, (total * percent + 50) / 100);
        var bg = total - 1;
        var n = (bg * percent + 50) / 100;
        if (n < 1 && bg > 0) n = 1;
        return 1 + n;
    }

    /// <summary>Läuft die Engine auf Platz <paramref name="slot"/> zur Ortszeit <paramref name="local"/>?</summary>
    public static bool IsOpen(int slot, int total, string? scope, IReadOnlyList<Rule> rules, DateTime local)
        => slot <= TargetCount(PercentAt(rules, IsoDay(local.DayOfWeek), local.Hour * 60 + local.Minute), total, scope);

    public static int IsoDay(DayOfWeek day) => day == DayOfWeek.Sunday ? 7 : (int)day;

    public static bool IsValidScope(string? scope)
        => string.Equals(scope, ScopeBackground, StringComparison.OrdinalIgnoreCase)
        || string.Equals(scope, ScopeAll, StringComparison.OrdinalIgnoreCase);

    /// <summary>Zeitzone aus einer IANA- („Europe/Vienna") ODER Windows-Kennung („W. Europe Standard Time") —
    /// das Windows-Skript meldet, was <c>[TimeZoneInfo]::Local.Id</c> dort liefert. <c>null</c> = unbekannt.</summary>
    public static TimeZoneInfo? ResolveZone(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        id = id.Trim();
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        if (TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana))
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(iana); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { }
        }
        return null;
    }

    private static HashSet<int> Days(string spec)
    {
        var set = new HashSet<int>();
        if (EveryDay.Contains(spec)) { for (var d = 1; d <= 7; d++) set.Add(d); return set; }
        foreach (var part in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var range = part.Split('-');
            if (range.Length > 2 || !DayNames.TryGetValue(range[0], out var a)
                || (range.Length == 2 && !DayNames.TryGetValue(range[1], out _)))
                throw new FormatException($"„{spec}\" ist keine Tagesangabe (mo di mi do fr sa so, Bereiche mo-do, Listen sa,so, * für jeden Tag).");
            if (range.Length == 1) { set.Add(a); continue; }
            var b = DayNames[range[1]];
            // Bereich über das Wochenende hinweg (fr-mo) läuft über den Sonntag hinaus.
            for (var d = a; ; d = d % 7 + 1) { set.Add(d); if (d == b) break; }
        }
        if (set.Count == 0)
            throw new FormatException($"„{spec}\" ist keine Tagesangabe (mo di mi do fr sa so, Bereiche mo-do, Listen sa,so, * für jeden Tag).");
        return set;
    }

    private static int? Minutes(string text)
    {
        var m = TimeRe.Match(text);
        if (!m.Success) return null;
        var h = int.Parse(m.Groups[1].Value);
        var min = int.Parse(m.Groups[2].Value);
        if (h > 24 || min > 59 || (h == 24 && min != 0)) return null;
        return h * 60 + min;
    }
}
