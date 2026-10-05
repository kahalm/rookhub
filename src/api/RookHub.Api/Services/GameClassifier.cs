using System.Text.RegularExpressions;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Die zwei Klassifizierer der Partienliste (0.661.0, Wunsch 2026-10-05): bei Online-Partien Seite + Modus
/// („chess.com – Blitz"), bei Ligapartien Liga + Jahrgang („Landesliga – 2026/27"). Ein vom Nutzer gesetzter Wert
/// (<see cref="SavedGame.Classifier1"/>/<see cref="SavedGame.Classifier2"/>) gilt immer; fehlt er, wird bei
/// chess.com/lichess abgeleitet — Seite aus der Quelle, Modus aus der Bedenkzeit. Andere Quellen bleiben leer, bis der
/// Nutzer etwas einträgt (die Liga steht in keinem Kopf verlässlich: Event ist fast immer „?").
/// </summary>
public static class GameClassifier
{
    public const int MaxLength = 80;

    /// <summary>Der Wert, den die Liste zeigt: gesetzt vor abgeleitet, <c>null</c> = keiner.</summary>
    public static (string? First, string? Second) Effective(string source, string? timeControl, string? set1, string? set2)
    {
        var (d1, d2) = Derived(source, timeControl);
        return (Clean(set1) ?? d1, Clean(set2) ?? d2);
    }

    /// <summary>Ableitung für Online-Partien; alles andere (PGN, Formular, RookHub) hat keine.</summary>
    public static (string? Site, string? Mode) Derived(string source, string? timeControl) => source switch
    {
        "chess.com" => ("chess.com", Mode(timeControl)),
        "lichess" => ("Lichess", Mode(timeControl)),
        _ => (null, null),
    };

    /// <summary>
    /// Modus aus <c>[TimeControl]</c> nach der Schätzung von lichess: Grundzeit + 40 × Inkrement, in Sekunden —
    /// unter 180 Bullet, unter 480 Blitz, unter 1500 Rapid, sonst Classical; Fernschach (<c>1/86400</c>) heißt Daily.
    /// Fehlt die Bedenkzeit oder ist sie unverständlich: <c>null</c> (geraten wird nicht).
    /// </summary>
    public static string? Mode(string? timeControl)
    {
        var tc = timeControl?.Trim();
        if (string.IsNullOrEmpty(tc) || tc == "-") return null;
        if (Regex.IsMatch(tc, @"^\d{1,3}/\d{1,7}$")) return "Daily";
        var m = Regex.Match(tc, @"^(\d{1,6})(?:\+(\d{1,4}))?$");
        if (!m.Success) return null;
        var estimate = int.Parse(m.Groups[1].Value) + 40 * (m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0);
        if (estimate == 0) return null;
        return estimate < 180 ? "Bullet" : estimate < 480 ? "Blitz" : estimate < 1500 ? "Rapid" : "Classical";
    }

    /// <summary>Eingabe bereinigen: getrimmt, auf <see cref="MaxLength"/> gekürzt, leer = <c>null</c>.</summary>
    public static string? Clean(string? raw)
    {
        var s = raw?.Trim();
        if (string.IsNullOrEmpty(s)) return null;
        return s.Length > MaxLength ? s[..MaxLength].TrimEnd() : s;
    }
}
