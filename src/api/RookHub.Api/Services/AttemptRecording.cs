using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Die vier Werte, die JEDER Lösungsversuch gemeinsam hat — einmal normalisiert statt an sieben
/// Stellen von Hand geklemmt. Vorher stand in jedem Recorder (Standard-Puzzle, Buch-/Tagespuzzle,
/// geteiltes Puzzle, Kurs-Linie, Wochenpost) dieselbe Zeilenfolge
/// <c>Math.Clamp(time, 0, 86400)</c> / <c>Math.Clamp(hints, 0, 3)</c> /
/// <c>SolveMode.Normalize(mode)</c> / <c>solvedAt.AddSeconds(-time)</c>; eine geänderte Grenze
/// hätte an allen sieben nachgezogen werden müssen, und genau dort war sie schon auseinandergelaufen
/// (der Standard-Puzzle-Pfad klemmte die Zeit nur fürs LOG und speicherte sie ungeklemmt).
/// </summary>
/// <remarks>
/// Die Grenzen sind bewusst KLEMMEN und keine Validierung: ein unsinniger Wert darf einen sonst
/// gültigen Versuch nie mit 400 abweisen — er ist gelöst worden, egal was die Uhr des Browsers sagt.
/// Wo ein DTO zusätzlich <c>[Range]</c> trägt (Wochenpost), bleibt das der Vertrag zum Client und
/// der Helfer dort ein No-op.
/// </remarks>
public static class AttemptRecording
{
    /// <summary>Obergrenze der gespeicherten Lösezeit: 24 h. Ein Browser-Tab, der über Nacht offen
    /// stand, ist keine Lösezeit — und ohne Deckel verschöbe ein einziger Ausreißer jede Summe.</summary>
    public const int MaxSeconds = 86400;

    /// <summary>Höchste Tipp-Stufe (0 = keine, 1–3) — es gibt drei Stufen je Puzzle.</summary>
    public const int MaxHints = 3;

    /// <summary>
    /// Normalisiert die gemeinsamen Versuchs-Werte. <paramref name="now"/> ist der Zeitpunkt des
    /// Versuchs (Vorgabe: jetzt) — als Parameter, damit ein Aufrufer denselben Stempel für Log,
    /// Entität und Folgesätze benutzen kann statt <c>DateTime.UtcNow</c> mehrfach zu lesen.
    /// </summary>
    public static AttemptCore From(bool solved, int seconds, int hints, string? mode, DateTime? now = null)
    {
        var attemptedAt = now ?? DateTime.UtcNow;
        var timeSeconds = Math.Clamp(seconds, 0, MaxSeconds);
        return new AttemptCore(
            solved,
            timeSeconds,
            Math.Clamp(hints, 0, MaxHints),
            SolveMode.Normalize(mode),
            attemptedAt,
            attemptedAt.AddSeconds(-timeSeconds));
    }
}

/// <summary>
/// Das Ergebnis von <see cref="AttemptRecording.From"/>: die geklemmten/normalisierten Werte eines
/// Lösungsversuchs samt der daraus abgeleiteten Startzeit.
/// </summary>
/// <param name="Solved">Wurde das Puzzle gelöst?</param>
/// <param name="TimeSeconds">Lösezeit, auf 0..<see cref="AttemptRecording.MaxSeconds"/> geklemmt.</param>
/// <param name="HintsUsed">Höchste angesehene Tipp-Stufe, auf 0..<see cref="AttemptRecording.MaxHints"/> geklemmt.</param>
/// <param name="Mode">Spielweise, normalisiert über <see cref="SolveMode.Normalize"/>.</param>
/// <param name="AttemptedAt">Zeitpunkt des Versuchs (= „SolvedAt" in den Log-Zeilen).</param>
/// <param name="StartedAt">Abgeleitet: <paramref name="AttemptedAt"/> minus der geklemmten Lösezeit.</param>
public readonly record struct AttemptCore(
    bool Solved,
    int TimeSeconds,
    int HintsUsed,
    string Mode,
    DateTime AttemptedAt,
    DateTime StartedAt);
