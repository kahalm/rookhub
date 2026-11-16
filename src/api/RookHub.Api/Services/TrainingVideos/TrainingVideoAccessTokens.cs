using Microsoft.AspNetCore.DataProtection;

namespace RookHub.Api.Services.TrainingVideos;

/// <summary>
/// Kurzlebiger Ausweis für die Video-Datei: ein <c>&lt;video src&gt;</c> schickt keinen Authorization-Header und holt
/// das Video in Bereichsanfragen. Der Ausweis trägt nur die Nutzer-Id (versiegelt mit dem DataProtection-Schlüsselbund,
/// eigener Zweck); ob die Stunde diesem Konto gehört, prüft JEDE Anfrage neu. Muster: <c>EbookAccessTokens</c>.
/// </summary>
public class TrainingVideoAccessTokens
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    private readonly ITimeLimitedDataProtector _protector;

    public TrainingVideoAccessTokens(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector("RookHub.TrainingVideos.Access.v1").ToTimeLimitedDataProtector();
    }

    public (string Token, DateTime ExpiresAt) Issue(int userId)
    {
        var expires = DateTime.UtcNow.Add(Lifetime);
        return (_protector.Protect(userId.ToString(System.Globalization.CultureInfo.InvariantCulture), expires), expires);
    }

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
            return false;   // manipuliert, fremder Schlüsselbund oder abgelaufen
        }
    }
}
