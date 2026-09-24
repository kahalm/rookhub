namespace RookHub.Api.Models;

/// <summary>
/// Stand des Fehler-Trainings („Eigene Fehler nachspielen", 0.516.0) zu EINER gespeicherten Partie.
/// Eine Zeile je Nutzer und Partie; die Uebersicht `/games` zeigt daraus „4 von 7 · 3 offen".
///
/// <para>Gespeichert werden die HALBZUEGE, die der Nutzer selbst gefunden hat (CSV, aufsteigend) — nicht
/// nur ein Zaehler: der Trainer laesst sich beliebig oft aufrufen, und nur so bleibt „schon gefunden"
/// über mehrere Durchlaeufe hinweg dasselbe. <see cref="SolvedCount"/> steht denormalisiert daneben,
/// damit die Liste nicht jede CSV zerlegen muss.</para>
/// </summary>
public class GameMistakeProgress
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    public int SavedGameId { get; set; }
    public SavedGame? Game { get; set; }

    /// <summary>Wie viele Aufgaben die Analyse hergibt (Seite des Nutzers) — zuletzt gemeldeter Stand.</summary>
    public int Total { get; set; }

    /// <summary>Selbst gefundene Halbzuege als CSV („12,34,56"), aufsteigend und ohne Doppelte.</summary>
    public string SolvedPlies { get; set; } = string.Empty;

    /// <summary>Anzahl der Eintraege in <see cref="SolvedPlies"/> (fuer die Liste).</summary>
    public int SolvedCount { get; set; }

    public DateTime FirstTrainedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastTrainedAt { get; set; } = DateTime.UtcNow;
}
