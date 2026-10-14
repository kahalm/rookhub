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

    /// <summary>Ausweis für einen Nutzer oder (0.778.0) einen Besucher ohne Anmeldung — dann trägt er „a:“ + Kennung.</summary>
    public (string Token, DateTime ExpiresAt) Issue(EbookCaller caller)
    {
        var expires = DateTime.UtcNow.Add(Lifetime);
        var payload = caller.UserId is int uid ? uid.ToString() : "a:" + (caller.AnonId ?? string.Empty);
        return (_protector.Protect(payload, expires), expires);
    }

    /// <summary><c>true</c> mit dem Aufrufer, wenn der Ausweis echt und noch gültig ist.</summary>
    public bool TryRead(string? token, out EbookCaller caller)
    {
        caller = default;
        if (string.IsNullOrWhiteSpace(token)) return false;
        try
        {
            var payload = _protector.Unprotect(token);
            if (payload.StartsWith("a:", StringComparison.Ordinal))
            {
                caller = EbookCaller.Anonymous(payload.Length > 2 ? payload[2..] : null);
                return true;
            }
            if (!int.TryParse(payload, out var userId)) return false;
            caller = userId;
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;   // manipuliert, fremder Schlüsselbund oder abgelaufen — alles dieselbe Antwort
        }
    }
}
