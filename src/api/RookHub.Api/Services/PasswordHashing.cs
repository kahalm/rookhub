namespace RookHub.Api.Services;

/// <summary>
/// Der EINE Ort fuer den BCrypt-Arbeitsfaktor. Er stand dreimal im Code (AuthService, PasswordResetService mit
/// Kommentar „identisch zu AuthService", AdminSeeder) — wer ihn an einer Stelle anhob, liess per Reset gesetzte
/// Passwoerter still auf dem alten Faktor (Codereview 2026-09-29, A1-014). Auch der Dummy-Hash fuer timing-gleiche
/// Logins entsteht hier, damit er immer gegen denselben Faktor misst wie neue echte Hashes.
/// </summary>
public static class PasswordHashing
{
    /// <summary>Explizit und versionierbar statt Library-Vorgabe.</summary>
    public const int WorkFactor = 12;

    /// <summary>BCrypt-Hash mit <see cref="WorkFactor"/>.</summary>
    public static string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
}
