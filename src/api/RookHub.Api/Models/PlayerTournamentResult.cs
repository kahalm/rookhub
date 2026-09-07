using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.Models;

/// <summary>
/// Die Teilnahme EINES Spielers an EINEM Turnier samt Ergebnis — der Zwischenspeicher hinter der
/// Turnierverlauf-Ansicht.
///
/// <para><b>Woher die Daten kommen und warum sie hier liegen.</b> Zwei chess-results-Seiten
/// tragen das: die Spielersuche liefert in EINEM Abruf alle Teilnahmen eines Spielers (vergangene
/// und kuenftige) samt Platz und Startnummer; die Spielerkarte liefert je Turnier Punkte,
/// Performance-Rating und Elo-Aenderung — dafuer aber EINEN Abruf je Turnier. Gemessen an einem
/// echten Konto: 23 Turniere, 12 davon gespielt, also 13 Abrufe fuer die vollstaendige Historie
/// und rund zwanzig Sekunden hinter dem Rate-Limiter des Crawlers. Das kann keine Seite
/// synchron laden, und ein abgeschlossenes Turnier aendert sich nie wieder — also
/// zwischenspeichern.</para>
///
/// <para><b>Der Schluessel ist der SPIELER, nicht das Konto.</b> Zwei Konten koennen dieselbe
/// Person meinen (Testkonto, uebertragenes Konto), und ein Konto kann seine Kennungen wechseln.
/// Vor allem aber: die Historie eines Spielers ist fuer jeden dieselbe — ein Freund, der auf mein
/// Profil schaut, soll denselben Zwischenspeicher benutzen und nicht dieselben Seiten erneut
/// holen.</para>
/// </summary>
public class PlayerTournamentResult
{
    public int Id { get; set; }

    /// <summary>
    /// Wer der Spieler ist, in der Form <c>fide:1693034</c> oder <c>cr:144749</c>. Die FIDE-Kennung
    /// hat Vorrang: bei einem AUSLANDS-Turnier steht in der chess-results-Ident-Spalte „0", und
    /// dann traegt allein die FIDE-ID die Identitaet.
    /// </summary>
    [Required, MaxLength(40)]
    public string PlayerKey { get; set; } = string.Empty;

    [Required, MaxLength(20)]
    public string ChessResultsId { get; set; } = string.Empty;

    /// <summary>Startnummer des Spielers in diesem Turnier — der Schluessel zur Spielerkarte.</summary>
    public int Snr { get; set; }

    [Required, MaxLength(500)]
    public string TournamentName { get; set; } = string.Empty;

    public DateOnly? EndDate { get; set; }

    /// <summary>Platz aus der Trefferliste; <c>null</c> heisst „noch nicht gespielt".</summary>
    public int? Rank { get; set; }
    public int? Rounds { get; set; }
    public int? PlayerCount { get; set; }

    // ----- aus der Spielerkarte (ein Abruf je Turnier) ----------------------

    /// <summary>Erreichte Punkte. Halbe Punkte sind der Normalfall, daher dezimal.</summary>
    public decimal? Points { get; set; }

    /// <summary>Turnier-Leistung („Performance rating") — die Zahl, um die es hier eigentlich geht.</summary>
    public int? PerformanceRating { get; set; }

    /// <summary>Elo-Aenderung aus diesem Turnier.</summary>
    public decimal? RatingChange { get; set; }

    /// <summary>Wertung des Spielers zu Turnierbeginn — der Bezugspunkt der Performance.</summary>
    public int? RatingInternational { get; set; }

    /// <summary>
    /// Wann die Spielerkarte geholt wurde. <c>null</c> = noch nicht versucht; gesetzt bleibt es
    /// auch dann, wenn die Karte keine Werte hatte (kuenftiges Turnier) — sonst wuerde dieselbe
    /// Seite immer wieder geholt.
    /// </summary>
    public DateTime? CardFetchedAt { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Wann die Turnierliste EINES Spielers zuletzt geholt wurde. Ein Abruf je Spieler und
/// Zeitfenster — ohne diesen Vermerk holte jeder Seitenaufruf die Trefferliste erneut.
/// </summary>
public class PlayerHistorySync
{
    /// <summary>Spielerkennung wie in <see cref="PlayerTournamentResult.PlayerKey"/>.</summary>
    [MaxLength(40)]
    public string PlayerKey { get; set; } = string.Empty;

    public DateTime LastFetchedAt { get; set; }

    /// <summary>Letzter Fehler, falls der Abruf scheiterte — fuer die Anzeige „gerade nicht erreichbar".</summary>
    [MaxLength(500)]
    public string? LastError { get; set; }
}
