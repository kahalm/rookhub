namespace RookHub.Api.Models;

/// <summary>
/// Generische In-App-Benachrichtigung für einen User (treibt die Navbar-Glocke + „!"-Indikator).
/// Bewusst typ-agnostisch: <see cref="Type"/> + <see cref="DataJson"/> (i18n-Parameter) werden im
/// Frontend zu lokalisiertem Text gerendert. <see cref="SeenAt"/> = null ⇒ ungelesen.
/// Spätere Kanäle (Mail/Push) hängen an genau diesem Strom.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>Empfänger.</summary>
    public int UserId { get; set; }
    public AppUser User { get; set; } = null!;

    /// <summary>Typ-Schlüssel (siehe <see cref="NotificationType"/>) → bestimmt Icon + i18n-Text im Frontend.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>Optionale i18n-Parameter als JSON (z. B. {"username":"…"} / {"courseName":"…"}).</summary>
    public string? DataJson { get; set; }

    /// <summary>Ziel-Route beim Klick auf die Benachrichtigung (z. B. "/friends", "/courses").</summary>
    public string? Link { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Gesetzt, sobald der User die Glocke geöffnet hat. null ⇒ ungelesen (Badge zählt es).</summary>
    public DateTime? SeenAt { get; set; }
}

/// <summary>Bekannte Notification-Typen. Frontend mappt jeden auf Icon + i18n-Key "notifications.type.&lt;type&gt;".</summary>
public static class NotificationType
{
    public const string ChessableImportCompleted = "chessable_import_completed";
    public const string ChessableImportFailed = "chessable_import_failed";
    public const string FriendRequestReceived = "friend_request_received";
    public const string FriendRequestAccepted = "friend_request_accepted";
    public const string RevengePerformed = "revenge_performed";
    public const string ChallengeReceived = "challenge_received";
    public const string ChallengeResolved = "challenge_resolved";
    /// <summary>Admin hat dem User eine Direktnachricht geschickt (→ User-Glocke, Link „/messages").</summary>
    public const string AdminMessageReceived = "admin_message_received";
    /// <summary>User hat im Thread geantwortet (→ Glocke aller Admins, Link „/admin").</summary>
    public const string UserMessageReceived = "user_message_received";
    /// <summary>Neue Runde/Paarungen in einem abonnierten Turnier (→ Glocke der Abonnenten,
    /// Link auf die Turnier-Detailseite). Daten: tournamentName, round.</summary>
    public const string TournamentNewRound = "tournament_new_round";
    /// <summary>Der naechtliche Verzeichnis-Sweep hat neue Turniere im Umkreis eines gespeicherten
    /// Suchprofils gefunden (→ Glocke des Profil-Besitzers, Link auf den Turnierkalender).
    /// Bewusst AGGREGIERT: eine Meldung je Profil und Lauf, nicht eine je Turnier.
    /// Daten: profileName, count, firstName (Name des ersten Treffers), radiusKm.</summary>
    public const string TournamentNearbyNew = "tournament_nearby_new";
    /// <summary>Termin oder Spielort eines gemerkten Turniers haben sich geaendert (→ Glocke der
    /// Abonnenten). Daten: tournamentName, oldDate, newDate, oldLocation, newLocation.</summary>
    public const string TournamentChanged = "tournament_changed";
    /// <summary>Ein gemerktes Turnier ist aus der chess-results-Suche verschwunden — vermutlich
    /// abgesagt (→ Glocke der Abonnenten). Daten: tournamentName, date.</summary>
    public const string TournamentCancelled = "tournament_cancelled";
    /// <summary>Ein neuer Benutzer hat sich registriert (→ Glocke aller Admins, Link „/admin").
    /// Daten: username.</summary>
    public const string NewUserRegistered = "new_user_registered";
    /// <summary>Ein User hat (erstmals) einen Chessable-Bearer hinterlegt (→ Glocke aller Admins,
    /// Link „/admin"). Daten: username.</summary>
    public const string ChessableTokenAdded = "chessable_token_added";
    /// <summary>Beim täglichen Kurslisten-Refresh wurde bei einem User ein neuer Chessable-Kurs
    /// entdeckt (→ Glocke aller Admins, Link „/admin"). Daten: username, courseName.</summary>
    public const string ChessableNewCourse = "chessable_new_course";
    /// <summary>Ein Nutzer hat einen Kurs mit dem Empfänger geteilt (→ Glocke des Empfängers,
    /// Link „/courses"). Daten: username (Teilender), courseName.</summary>
    public const string CourseShared = "course_shared";
    /// <summary>Ein Nutzer hat ein Repertoire mit dem Empfänger geteilt (→ Glocke des Empfängers,
    /// Link „/repertoires"). Daten: username (Teilender), repertoireName.</summary>
    public const string RepertoireShared = "repertoire_shared";
    /// <summary>Ein berechtigter Viewer fordert ein Katalog-Item an (→ Glocke des Besitzers,
    /// Link „/catalog"). Daten: username (Anfragender), itemName.</summary>
    public const string CatalogRequestReceived = "catalog_request_received";
    /// <summary>Der Besitzer hat eine Katalog-Anforderung genehmigt (→ Glocke des Anfragenden,
    /// Link „/courses" bzw. „/repertoires"). Daten: itemName.</summary>
    public const string CatalogRequestApproved = "catalog_request_approved";
    /// <summary>Der Besitzer hat eine Katalog-Anforderung abgelehnt (→ Glocke des Anfragenden).
    /// Daten: itemName.</summary>
    public const string CatalogRequestDeclined = "catalog_request_declined";

    /// <summary>Kalkulations-Serie: eine terminierte Woche wurde freigegeben (→ Glocke der Verteiler-
    /// Mitglieder bzw. Tester zum früheren Termin, Link auf die Kursseite). Daten: book, chapter.</summary>
    public const string CalcSeriesEditionReleased = "calc_series_edition_released";
}
