using System.Text.RegularExpressions;
using RookHub.Api.DTOs.EngineBroker;
using RookHub.Api.Models;

namespace RookHub.Api.Services.EngineBroker;

/// <summary>
/// Bausteine des Admin-Reiters „Engines“ (0.953.0), die nur rechnen und nichts abfragen: Maschinen-Stamm aus dem Namen,
/// Zeitplan jetzt / nächstes Umschalten, Einordnung eines Durchsatz-Korbs als „normal“ oder „Schonzeit“, Grund für
/// Leerlauf. Die Zeitplan-Regeln kommen aus <see cref="EngineScheduleRules"/> und <see cref="EngineAvailability.IsOpen"/>
/// (dieselben wie bei der Vergabe), die Sperrzeiten aus <see cref="QuietHours"/>.
/// </summary>
public static class EngineOverviewSchedules
{
    private static readonly Regex TrailingNumber = new(@"(\s+#?|#)\d+$", RegexOptions.Compiled);
    private static readonly Regex TrailingRole = new(@"\s+(hintergrund|background|bg|live)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Namensstamm einer Maschine: „RookHub Gross Hintergrund 12“ → „RookHub Gross“, „RookHub Gross“ bleibt.</summary>
    public static string Stem(string? name)
    {
        var s = (name ?? string.Empty).Trim();
        s = TrailingNumber.Replace(s, string.Empty);
        s = TrailingRole.Replace(s, string.Empty).Trim();
        return s.Length == 0 ? (name ?? string.Empty).Trim() : s;
    }

    /// <summary>Grund-Schlüssel einer Maschine: Konto + Stamm (direkt angemeldet), Konto + „lichess“, sonst Konto + „other“.</summary>
    public static string MachineKey(int? ownerId, string source, string? name) => source switch
    {
        "rookhub" when name is not null => $"{ownerId}|{Stem(name)}",
        "lichess" => $"{ownerId}|lichess",
        _ => $"{ownerId}|other",
    };

    /// <summary>
    /// Maschinen-Schlüssel je Engine (gleiche Reihenfolge wie <paramref name="engines"/>). Engines mit gleichem Besitzer
    /// und Stamm gehören zusammen, wenn ihr Token gleich ODER bei einem Teil noch unbekannt (<c>null</c>) ist — die
    /// Spalte <c>ApiTokenId</c> füllt sich erst nach und nach (Anmeldung bzw. erster Poll nach 0.955.2), eine Maschine
    /// darf daran nicht zerfallen. Nur VERSCHIEDENE bekannte Tokens unter einem Stamm trennen: dann je Token eine
    /// Maschine (<c>…|t{id}</c>) und die noch unbekannten als eigene (<c>…|t-</c>); <c>Split</c> sagt, dass der Kopf
    /// den Token im Namen braucht.
    /// </summary>
    public static IReadOnlyList<(string Key, bool Split)> MachineKeys(
        IReadOnlyList<(int? OwnerId, string Source, int? TokenId, string? Name)> engines)
    {
        var bases = engines.Select(e => MachineKey(e.OwnerId, e.Source, e.Name)).ToList();
        var tokensOf = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
        for (var i = 0; i < engines.Count; i++)
            if (engines[i] is { Source: "rookhub", TokenId: { } t, Name: not null })
            {
                if (!tokensOf.TryGetValue(bases[i], out var set)) tokensOf[bases[i]] = set = [];
                set.Add(t);
            }
        return engines.Select((e, i) =>
            tokensOf.TryGetValue(bases[i], out var set) && set.Count > 1
                ? ($"{bases[i]}|t{e.TokenId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "-"}", true)
                : (bases[i], false)).ToList();
    }

    /// <summary>Ortszeit einer Meldung (unbekannte Zone → Vorgabe wie <see cref="EngineAvailability.IsOpen"/>).</summary>
    private static TimeZoneInfo ZoneOf(EngineClientSchedule s) =>
        EngineScheduleRules.ResolveZone(s.TimeZone) ?? TimeZoneInfo.FindSystemTimeZoneById(QuietHours.DefaultTimeZone);

    /// <summary>Prozentsatz der Regel zum Zeitpunkt <paramref name="atUtc"/>; unlesbare Regel = 100 (wie die Vergabe).</summary>
    public static int PercentAt(EngineClientSchedule s, DateTime atUtc)
    {
        try
        {
            var rules = EngineScheduleRules.Parse(s.Rule);
            var local = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc), ZoneOf(s));
            return EngineScheduleRules.PercentAt(rules, EngineScheduleRules.IsoDay(local.DayOfWeek), local.Hour * 60 + local.Minute);
        }
        catch (FormatException) { return 100; }
    }

    /// <summary>Schonzeit zum Korb-Beginn: mit Meldung „Regel &lt; 100 %“, ohne Meldung „RookHub-Sperrzeit“.</summary>
    public static bool IsReduced(EngineClientSchedule? s, QuietHours? quiet, DateTime bucketStartUtc) =>
        s is not null ? PercentAt(s, bucketStartUtc) < 100
        : quiet?.IsQuiet(new DateTimeOffset(DateTime.SpecifyKind(bucketStartUtc, DateTimeKind.Utc))) == true;

    public static EngineScheduleDto ToDto(EngineClientSchedule s, DateTime nowUtc) => new(s.Slot, s.Total, s.Scope, s.Rule,
        s.TimeZone, s.ReportedAt, EngineAvailability.IsOpen(s, new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc))),
        PercentAt(s, nowUtc));

    /// <summary>Zeitplan einer Maschine aus den Meldungen ihrer Engines (jüngste Meldung gibt Regel/Zone vor). Das nächste
    /// Umschalten = erste Minute binnen 7 Tagen mit anderem Prozentsatz (Minutenschritte, höchstens 10 080).</summary>
    public static EngineMachineScheduleDto? MachineSchedule(IReadOnlyList<EngineClientSchedule> reported, DateTime nowUtc)
    {
        if (reported.Count == 0) return null;
        var s = reported.MaxBy(r => r.ReportedAt)!;
        var now = new DateTimeOffset(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc));
        var percentNow = PercentAt(s, nowUtc);
        DateTime? next = null;
        int? nextPercent = null;
        var start = nowUtc.AddSeconds(-nowUtc.Second).AddMilliseconds(-nowUtc.Millisecond);
        for (var m = 1; m <= 7 * 24 * 60; m++)
        {
            var at = start.AddMinutes(m);
            var p = PercentAt(s, at);
            if (p != percentNow) { next = at; nextPercent = p; break; }
        }
        return new EngineMachineScheduleDto(s.Rule, s.TimeZone, s.Scope, s.Total, s.ReportedAt, reported.Count,
            reported.Count(r => EngineAvailability.IsOpen(r, now)), percentNow, next, nextPercent);
    }

    /// <summary>Gründe für Leerlauf (Frontend <c>admin.engines.idle.*</c>).</summary>
    public const string IdlePaused = "paused", IdleExplicitOnly = "explicitOnly", IdleNotInList = "notInList",
        IdleScheduleOff = "scheduleOff", IdleQuietHours = "quietHours", IdleNothingWaiting = "nothingWaiting",
        IdleWaitingElsewhere = "waitingElsewhere";

    /// <summary>
    /// Warum rechnet eine freie, erreichbare Engine nichts? Die Reihenfolge folgt der Vergabe: Pause hält alles an;
    /// „nur auf Anforderung“ nimmt nichts Automatisches; ohne Platz in der Liste des Besitzers keine Hintergrund-Arbeit;
    /// <paramref name="usableAll"/>/<paramref name="usableBatch"/> sind das Ergebnis von <see cref="EngineAvailability.UsableAsync"/>
    /// für die Liste des Besitzers (normale bzw. Stapel-Aufträge); dann, ob für den Besitzer überhaupt etwas wartet.
    /// </summary>
    public static string IdleReason(string engineId, bool paused, bool explicitOnly, bool inOwnerList, bool hasSchedule,
        IReadOnlySet<string> usableAll, IReadOnlySet<string> usableBatch, int ownerWaiting, int ownerWaitingNonBatch)
    {
        if (paused) return IdlePaused;
        if (explicitOnly) return IdleExplicitOnly;
        if (!inOwnerList) return IdleNotInList;
        if (!usableAll.Contains(engineId) && hasSchedule) return IdleScheduleOff;
        if (ownerWaiting == 0) return IdleNothingWaiting;
        if (!usableBatch.Contains(engineId) && ownerWaitingNonBatch == 0) return hasSchedule ? IdleScheduleOff : IdleQuietHours;
        return IdleWaitingElsewhere;
    }
}
