using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace RookHub.Api.Services;

/// <summary>Optionen fuer <see cref="ApiTokenAuthenticationHandler"/>. Aktuell leer (Marker).</summary>
public class ApiTokenAuthenticationOptions : AuthenticationSchemeOptions { }

/// <summary>
/// ASP.NET-Core-Authentication-Handler fuer <c>Authorization: Bearer rkh_…</c>.
/// Wird neben dem JWT-Handler registriert; ein Policy-Scheme-Selector im Program.cs
/// waehlt anhand des Prefixes.
///
/// Setzt bei Erfolg einen ClaimsPrincipal mit den gleichen Claims wie der JWT-Handler:
/// <c>NameIdentifier</c> = UserId, <c>Name</c> = Username, plus einen optionalen
/// <c>scope</c>-Claim — damit kann Authorization-Policy (z. B. nur Scope <c>extension</c>)
/// pruefen, ob der Token-Inhaber den Endpoint nutzen darf.
/// </summary>
public class ApiTokenAuthenticationHandler : AuthenticationHandler<ApiTokenAuthenticationOptions>
{
    public const string SchemeName = "ApiToken";
    private readonly ApiTokenService _tokens;
    private readonly Data.AppDbContext _db;

    public ApiTokenAuthenticationHandler(
        IOptionsMonitor<ApiTokenAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ApiTokenService tokens,
        Data.AppDbContext db)
        : base(options, logger, encoder)
    {
        _tokens = tokens;
        _db = db;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(header) || !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var raw = header["Bearer ".Length..].Trim();
        if (!raw.StartsWith(ApiTokenService.Prefix, StringComparison.Ordinal))
            return AuthenticateResult.NoResult(); // anderes Bearer-Schema (JWT) — uns nicht zustaendig

        var token = await _tokens.ValidateAsync(raw);
        if (token == null)
            return AuthenticateResult.Fail("Invalid or expired API token.");

        // Username fuer den Name-Claim nachladen (Cache wuerde sich lohnen, ist aber pro-Request
        // ein einfacher PK-Lookup).
        var username = await _db.AppUsers
            .Where(u => u.Id == token.UserId)
            .Select(u => u.Username)
            .FirstOrDefaultAsync() ?? string.Empty;

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, token.UserId.ToString()),
            new(ClaimTypes.Name, username),
            new("scope", token.Scope),
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
