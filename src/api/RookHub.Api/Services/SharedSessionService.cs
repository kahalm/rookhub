using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RookHub.Api.Data;
using RookHub.Api.DTOs;

namespace RookHub.Api.Services;

/// <summary>
/// Eine Anmeldung, zwei Oberflaechen: RookHub und die Turnierseite liegen auf eigenen Subdomains
/// und teilen den <c>localStorage</c> NICHT. Wer sich hier anmeldet, ist drueben nicht angemeldet,
/// obwohl dasselbe Konto dahintersteht.
///
/// <para>Der <see cref="AuthHandoffService"/> loest das nur fuer den KLICK im Menue. Hier geht es um
/// den Normalfall: die Turnierseite direkt aufrufen, nachdem man sich vorhin in RookHub angemeldet
/// hat. Dafuer legt der Server beim Anmelden ein Cookie auf der GEMEINSAMEN Elterndomaene ab; beide
/// Oberflaechen tauschen es beim Start gegen ihre eigene Anmeldung.</para>
///
/// <para><b>Warum nicht das normale JWT ins Cookie:</b> es ist der Schluessel zu allem. Das Cookie
/// traegt deshalb ein eigenes Token mit EIGENEM Adressaten (<see cref="Audience"/>) — der
/// JWT-Handler der API weist es ab, es oeffnet also ausschliesslich diesen einen Endpunkt. Dazu
/// <c>HttpOnly</c> (kein Zugriff aus JavaScript, anders als beim localStorage), <c>SameSite=Lax</c>
/// (wird bei fremd ausgeloesten Anfragen gar nicht erst mitgeschickt) und ein Pfad, der es auf
/// <c>/api/auth</c> beschraenkt.</para>
///
/// <para><b>Aus, solange keine Elterndomaene konfiguriert ist</b> (<c>Auth:SharedSessionDomain</c>):
/// auf <c>localhost</c> oder einer IP gibt es keine gemeinsame Domaene, ein Cookie dorthin waere ein
/// stiller Fehlschlag. Dann wird keins geschrieben und der Tausch antwortet mit 401.</para>
/// </summary>
public class SharedSessionService
{
    private readonly AppDbContext _db;
    private readonly AuthService _auth;
    private readonly IConfiguration _config;

    /// <summary>Adressat des Cookie-Tokens — bewusst NICHT <c>Jwt:Audience</c>.</summary>
    public const string Audience = "rookhub-shared-session";

    /// <summary>Nur <c>/api/auth/...</c> braucht das Cookie; sonst haengt es an jedem API-Aufruf.</summary>
    public const string CookiePath = "/api/auth";

    /// <summary>So lange wie eine gewoehnliche Anmeldung ohne „eingeloggt bleiben".</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public SharedSessionService(AppDbContext db, AuthService auth, IConfiguration config)
    {
        _db = db;
        _auth = auth;
        _config = config;
    }

    /// <summary>Elterndomaene des Cookies, oder <c>null</c> — dann ist die Funktion abgeschaltet.</summary>
    public string? CookieDomain
    {
        get
        {
            var value = _config["Auth:SharedSessionDomain"];
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    /// <summary>
    /// Name des Cookies. Dev und Prod teilen sich die Elterndomaene — mit demselben Namen
    /// ueberschreiben sie einander, und das Cookie der einen Umgebung ist in der anderen nur ein
    /// ungueltiges Token. Der Name ist deshalb konfigurierbar.
    /// </summary>
    public string CookieName
    {
        get
        {
            var value = _config["Auth:SharedSessionCookie"];
            return string.IsNullOrWhiteSpace(value) ? "rh_session" : value.Trim();
        }
    }

    public bool IsEnabled => CookieDomain != null;

    /// <summary>
    /// Baut den Cookie-Wert fuer einen Nutzer. <c>null</c>, wenn die Funktion aus ist oder das Konto
    /// nicht (mehr) taugt — der Aufrufer schreibt dann schlicht kein Cookie.
    /// </summary>
    public async Task<string?> IssueAsync(int userId, CancellationToken ct = default)
    {
        if (!IsEnabled) return null;

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user is null) return null;

        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, user.Id.ToString()) };
        // Denselben Stempel wie das normale Token mitfuehren: eine Passwortaenderung entwertet damit
        // auch die geteilte Anmeldung, sonst holte man sich dort ein frisches Token zurueck.
        if (user.SecurityStamp != null) claims.Add(new Claim("sstamp", user.SecurityStamp));

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: Audience,
            claims: claims,
            expires: DateTime.UtcNow.Add(Lifetime),
            signingCredentials: new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Tauscht ein Cookie gegen eine richtige Anmeldung. <c>null</c> bei allem, was nicht passt —
    /// von aussen ohne Unterscheidung, der Grund hilft nur beim Raten.
    /// </summary>
    public async Task<AuthResponseDto?> RedeemAsync(string? cookieValue, CancellationToken ct = default)
    {
        if (!IsEnabled || string.IsNullOrWhiteSpace(cookieValue)) return null;

        ClaimsPrincipal principal;
        try
        {
            principal = new JwtSecurityTokenHandler().ValidateToken(cookieValue, new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = _config["Jwt:Issuer"],
                // Der eigene Adressat ist der Kern: ein normales Zugriffstoken darf hier NICHT
                // durchgehen und dieses hier nirgendwo sonst.
                ValidAudience = Audience,
                IssuerSigningKey = SigningKey(),
                ClockSkew = TimeSpan.FromMinutes(1),
            }, out _);
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            return null;
        }

        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)) return null;

        var user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Id == userId && u.DeletedAt == null, ct);
        if (user is null) return null;

        // Wie beim normalen Token: passt der Stempel nicht mehr, ist die Sitzung entwertet.
        var stamp = principal.FindFirstValue("sstamp");
        if (user.SecurityStamp != null && user.SecurityStamp != stamp) return null;

        return await _auth.IssueTokenAsync(user);
    }

    private SymmetricSecurityKey SigningKey() => new(Encoding.UTF8.GetBytes(
        _config["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured")));
}
