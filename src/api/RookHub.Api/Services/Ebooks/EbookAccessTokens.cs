using Microsoft.AspNetCore.DataProtection;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Kurzlebiger Zugriffsausweis für die DATEI-Endpunkte der Bibliothek (<c>…/file</c>, <c>…/cover</c>): ein
/// <c>&lt;img src&gt;</c>, ein Download-Link und der PDF-Leser (Bereichsanfragen) können keinen Authorization-Header
/// setzen — sie hängen stattdessen <c>?t=…</c> an. Der Ausweis trägt nur die Nutzer-Id, zeitlich begrenzt und mit
/// dem DataProtection-Schlüsselbund der API versiegelt (derselbe Ring wie Passwort-Reset-Links); die Sichtbarkeit
/// des Buchs wird bei JEDER Anfrage neu gegen die Datenbank geprüft. Wer den Ausweis später findet, findet nach
/// <see cref="Lifetime"/> nichts Verwertbares mehr.
/// </summary>
public class EbookAccessTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly ITimeLimitedDataProtector _protector;

    public EbookAccessTokens(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("RookHub.Ebooks.Access.v1").ToTimeLimitedDataProtector();
    }

    public (string Token, DateTime ExpiresAt) Issue(int userId)
    {
        var expires = DateTime.UtcNow.Add(Lifetime);
        return (_protector.Protect(userId.ToString(), expires), expires);
    }

    /// <summary><c>true</c> mit der Nutzer-Id, wenn der Ausweis echt und noch gültig ist.</summary>
    public bool TryRead(string? token, out int userId)
    {
        userId = 0;
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            return int.TryParse(_protector.Unprotect(token), out userId);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;   // manipuliert, fremder Schlüsselbund oder abgelaufen — alles dieselbe Antwort
        }
    }
}
