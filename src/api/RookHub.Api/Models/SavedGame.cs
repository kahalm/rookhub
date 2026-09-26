namespace RookHub.Api.Models;

/// <summary>
/// Eine vom User auf chess.com oder lichess.org gespeicherte Partie (Button „Partie speichern"
/// in der RepCheck-Extension). Die Extension schickt die SAN-Zugliste der aktuellen Review-/
/// Analyse-Seite plus Best-Effort-Metadaten; der Server baut daraus ein PGN. Pro Spiel wird ein
/// eindeutiges <see cref="ShareToken"/> erzeugt, über das die Partie öffentlich geteilt werden kann.
/// Gespeichert über <c>POST /api/extension/games</c>.
/// </summary>
public class SavedGame
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>Herkunfts-Plattform: <c>chess.com</c> oder <c>lichess</c>.</summary>
    public string Source { get; set; } = string.Empty;

    /// <summary>Partie-ID auf der Plattform (aus der URL), falls erkannt — für Dedup.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Vollständiges PGN (serverseitig aus Zugliste + Metadaten gebaut).</summary>
    public string Pgn { get; set; } = string.Empty;

    public string? White { get; set; }
    public string? Black { get; set; }

    /// <summary>Ergebnis (<c>1-0</c>/<c>0-1</c>/<c>1/2-1/2</c>/<c>*</c>).</summary>
    public string? Result { get; set; }

    /// <summary>Wann die Partie gespielt wurde, falls bekannt.</summary>
    public DateTime? PlayedAt { get; set; }

    /// <summary>Original-URL der Partie (Link zurück zur Plattform).</summary>
    public string? SourceUrl { get; set; }

    /// <summary>Eindeutiges URL-sicheres Token für den öffentlichen Teilen-Link (<c>/g/{token}</c>).</summary>
    public string ShareToken { get; set; } = string.Empty;

    /// <summary>
    /// Zahl der Halbzüge, aus dem PGN vorberechnet. <c>null</c> = noch nie gezählt (Zeilen von vor
    /// der Einführung des Feldes) — die werden beim nächsten Auflisten nachgetragen.
    /// Existiert, weil die Listenansicht sonst das komplette PGN (LONGTEXT) jeder Partie laden
    /// müsste, nur um Züge zu zählen: <c>CountPlies()</c> ist eine C#-Methode und damit in einer
    /// EF-Projektion nicht übersetzbar, EF holt die Spalte also mit und rechnet im Speicher.
    /// Bei bis zu 500 Partien je Anfrage ist das die mit Abstand teuerste Spalte — und das DTO
    /// der Liste trägt das PGN nicht einmal.
    /// </summary>
    public int? MoveCount { get; set; }

    /// <summary>
    /// Wertung der beiden Seiten, <c>null</c> = keine bekannt. Steht seit 0.526.0 als SPALTE da und
    /// nicht mehr nur im PGN-Header: die Partienliste zeigt sie (wie die Uebersicht auf chess.com),
    /// und das PGN dafuer zu laden ist genau der Fehler, den <see cref="MoveCount"/> schon einmal
    /// behoben hat. Der Altbestand wird portionsweise aus dem PGN nachgetragen
    /// (<see cref="Services.SavedGameService.HeaderBackfillPerCall"/>).
    /// </summary>
    public int? WhiteElo { get; set; }

    /// <inheritdoc cref="WhiteElo"/>
    public int? BlackElo { get; set; }

    /// <summary>
    /// Bedenkzeit in PGN-Schreibweise (<c>180+2</c>, <c>600</c>, <c>1/86400</c> fuer Fernschach);
    /// <c>null</c> = unbekannt. Kommt von der Erweiterung; ALT gespeicherte Partien haben sie nicht
    /// und bekommen sie auch nicht mehr — sie steht in keinem gespeicherten PGN.
    /// </summary>
    public string? TimeControl { get; set; }

    /// <summary>
    /// Wurden die Elo-Header dieser Zeile schon einmal aus dem PGN in die Spalten gehoben? Ohne die
    /// Marke waere <c>WhiteElo == null</c> zweideutig: „noch nicht nachgesehen" und „die Partie nennt
    /// keine Wertung" sehen gleich aus, und der Nachtrag liefe fuer immer im Kreis.
    /// </summary>
    public bool HeadersScanned { get; set; }

    /// <summary>
    /// Die Partie-Analyse, aus der die Bewertungskurve dieser Partie kommt — gesetzt, sobald der
    /// BESITZER „Partie analysieren" drueckt (auch wenn dabei eine vorhandene Analyse wiederverwendet
    /// wird). Ein Gast, der die geteilte Partie rechnen laesst, bekommt seine eigene Analyse, aendert
    /// hier aber nichts: die oeffentliche Kurve ist die des Teilenden.
    ///
    /// <para>Kein Fremdschluessel: die Analyse laesst sich auf „Partie-Analysen" loeschen, und das soll
    /// weder scheitern noch die Partie mitnehmen. Der Verweis faellt dann ins Leere — jeder Leser prueft,
    /// ob es die Analyse noch gibt (dasselbe Muster wie <see cref="LibraryGame.GameAnalysisId"/>).</para>
    /// </summary>
    public int? GameAnalysisId { get; set; }

    /// <summary>
    /// Die Seite des Besitzers, von ihm selbst festgelegt (<c>white</c>/<c>black</c>, 0.531.0) — <c>null</c> = nicht
    /// festgelegt, dann gilt die Zuordnung über den Plattform-Namen. Dreht die eigene Partieseite, den Teilen-Link und
    /// das Link-Vorschaubild. Gebraucht bei eingelesenen Formularen (dort gibt es keinen Plattform-Namen), setzbar für
    /// jede Partie.
    /// </summary>
    public string? OwnerSide { get; set; }

    /// <summary>
    /// In welcher Sprache der Besitzer die Partie ansieht (0.540.0) — mitgeschickt mit „Partie analysieren" aus der
    /// Seite. Darin entstehen nach der Analyse von selbst die Fehler-Erklärungen und die drei Roasts
    /// (<c>GameReviewTexts</c>). <c>null</c> (über die Erweiterung angestoßen) = die zuletzt so gemerkte Sprache des
    /// Nutzers, sonst Englisch.
    /// </summary>
    public string? ReviewLanguage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
