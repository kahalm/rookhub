namespace RookHub.Api.Models;

/// <summary>
/// Teilen-Link eines Eintrags der Trainerstunden (Wunsch 2026-11-17: „einzelne Stunden sollen per Link (mit Token) teilbar
/// sein, zeitlich begrenzt"). Der Besitzer des Eintrags (<see cref="TrainingLesson.UserId"/>) legt beliebig viele an, jeder
/// mit eigener Frist; wer den Link hat, sieht GENAU diesen Eintrag (einen Teil einer Stunde) ohne Anmeldung, nur lesend:
/// Video, Segmente, Partien, Nachspielen. Nie: Notizen, Übungsstand, andere Einträge, RookHub-Partien des Besitzers.
/// <para>Das Token liegt im Klartext (wie <see cref="SavedGame.AnonShareToken"/> und <see cref="SharedLine.ShareToken"/>),
/// damit der Besitzer einen bestehenden Link wieder kopieren kann; 256 Bit Zufall, eindeutiger Index.</para>
/// </summary>
public class TrainingLessonShare
{
    public int Id { get; set; }

    public int LessonId { get; set; }
    public TrainingLesson? Lesson { get; set; }

    /// <summary>Base64URL, 43 Zeichen (<c>SecretTokens.NewRaw()</c>).</summary>
    public string Token { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    /// <summary>Ablauf (UTC); danach antwortet der Link mit 410.</summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>Widerrufen (UTC); <c>null</c> = aktiv. Widerrufene Links antworten mit 410 wie abgelaufene.</summary>
    public DateTime? RevokedAt { get; set; }
}
