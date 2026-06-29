using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace RookHub.Api.Controllers;

public abstract class BaseApiController : ControllerBase
{
    protected int GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (claim is null || !int.TryParse(claim, out var userId))
            throw new UnauthorizedAccessException("User ID claim is missing or invalid.");
        return userId;
    }

    /// <summary>UserId des aktuellen Tokens, oder <c>null</c> wenn nicht (gültig) eingeloggt — für
    /// <c>[AllowAnonymous]</c>-Endpoints, die optional einen eingeloggten Nutzer berücksichtigen.</summary>
    protected int? GetUserIdOrNull()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var userId) ? userId : null;
    }

    /// <summary>
    /// True, wenn das aktuelle Token aus einer Admin-Impersonation stammt (trägt den <c>imp</c>-Claim).
    /// Destruktive/irreversible Aktionen (Konto löschen, Passwort ändern, API-Token erstellen) dürfen
    /// im Impersonations-Kontext NICHT ausgeführt werden — ein Admin soll fremde Konten nicht
    /// dauerhaft verändern oder dauerhafte Zugangstoken in fremdem Namen erzeugen können.
    /// </summary>
    protected bool IsImpersonating() => User.FindFirst("imp") is not null;
}
