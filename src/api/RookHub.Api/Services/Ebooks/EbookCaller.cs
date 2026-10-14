using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Wer die Bibliothek benutzt (0.778.0): ein angemeldeter Nutzer (<see cref="UserId"/>) oder ein Besucher ohne Anmeldung
/// mit der anonymen Kennung der App (<see cref="AnonId"/> — dieselbe wie bei Puzzles/Endlos, Header <c>X-Visitor-Id</c>).
/// Ohne Anmeldung sieht man die für „Alle“ freigegebenen Bücher und die eigenen anonymen Uploads; beim Anmelden wandern
/// diese ins Konto (<see cref="EbookService.ClaimAnonymousAsync"/>). Ein <c>int</c> wird stillschweigend zum angemeldeten
/// Aufrufer — die bestehenden Aufrufe mit Nutzer-Id bleiben unverändert.
/// </summary>
public readonly record struct EbookCaller(int? UserId, string? AnonId)
{
    private static readonly Regex AnonPattern = new(ValidationConstants.SessionIdPattern, RegexOptions.Compiled);

    public static implicit operator EbookCaller(int userId) => new(userId, null);

    /// <summary>Besucher ohne Anmeldung; eine Kennung, die nicht der strengen Form genügt, zählt als keine.</summary>
    public static EbookCaller Anonymous(string? anonId) => new(null, IsValidAnonId(anonId) ? anonId : null);

    public static bool IsValidAnonId(string? id) => !string.IsNullOrEmpty(id) && id.Length <= 36 && AnonPattern.IsMatch(id);

    public bool IsAnonymous => UserId == null;
}
