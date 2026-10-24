using System.Collections.Concurrent;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Prozessweiter Fortschrittszustand des Chessable-Imports und seine Meldungen: welche Importe DIESER Prozess
/// gerade treibt (für den <see cref="ChessableImportWatchdogService"/>) und wie Dauer/Fertig-Meldung aussehen.
/// Aus <see cref="ChessableImportService"/> herausgelöst (Zerlegung Z3); der Zustand existiert genau einmal
/// (statisch), wie vorher als statisches Feld der scoped Fassade.
/// </summary>
public static class ChessableImportProgress
{
    /// <summary>Import-Ids, die in DIESEM Prozess GERADE von einem Treiber bearbeitet werden (Claim bis
    /// Ende von <see cref="ChessableImportService.RunAsync"/>), mit Zähler wegen der verschachtelten Registrierung
    /// Drain→RunAsync. Die DB-Phase allein reicht als „läuft"-Nachweis NICHT: verliert ein Job seinen
    /// treibenden Task, ohne einen Terminal-Status zu schreiben (z. B. DB-Ausfall → auch das
    /// Fehler-Update in <see cref="ChessableImportService.FailAsync"/> scheitert), bleibt er auf "claimed"/"fetching"/"importing"
    /// stehen und belegt den Lane-Slot dauerhaft — der Drain steht bis zum API-Neustart. Der
    /// <see cref="ChessableImportWatchdogService"/> vergleicht die DB-Inflight-Menge gegen diese Liste
    /// und reiht verwaiste Jobs wieder ein. Prozesslokal (eine API-Instanz; nach einem Neustart räumt
    /// der <see cref="ChessableImportResumeService"/> auf).</summary>
    private static readonly ConcurrentDictionary<int, int> _localInflight = new();

    /// <summary>Wird dieser Import gerade von einem Treiber DIESES Prozesses bearbeitet?</summary>
    internal static bool IsDrivenLocally(int importId) => _localInflight.ContainsKey(importId);

    /// <summary>Markiert den Import für die Dauer des <c>using</c> als lokal getrieben.</summary>
    internal static IDisposable TrackInflight(int importId) => new InflightToken(importId);

    private sealed class InflightToken : IDisposable
    {
        private readonly int _id;
        public InflightToken(int id)
        {
            _id = id;
            _localInflight.AddOrUpdate(id, 1, (_, c) => c + 1);
        }
        public void Dispose()
        {
            var left = _localInflight.AddOrUpdate(_id, 0, (_, c) => c - 1);
            if (left <= 0) _localInflight.TryRemove(new KeyValuePair<int, int>(_id, left));
        }
    }

    /// <summary>Kompakte, sprachneutrale Dauer für Meldungen: "1 h 5 min", "12 min", "45 s"; "—" wenn unbekannt.</summary>
    internal static string FormatDuration(TimeSpan? span)
    {
        if (span is null || span.Value < TimeSpan.Zero) return "—";
        var t = span.Value;
        if (t.TotalHours >= 1) return $"{(int)t.TotalHours} h {t.Minutes} min";
        if (t.TotalMinutes >= 1) return $"{(int)t.TotalMinutes} min";
        return $"{(int)t.TotalSeconds} s";
    }

    /// <summary>i18n-Parameter der Benachrichtigung „Import fertig" (<c>chessable_import_completed</c>).</summary>
    internal static Dictionary<string, string> CompletedData(string courseName, string target, string queueTime, string fetchTime)
        => new()
        {
            ["courseName"] = courseName,
            ["target"] = target,
            ["queueTime"] = queueTime,
            ["fetchTime"] = fetchTime,
        };
}
