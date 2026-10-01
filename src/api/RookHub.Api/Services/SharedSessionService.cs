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
/// <c>/api/auth/rh-session</c> beschraenkt.</para>
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

    /// <summary>
    /// Nur der Tausch, das Abmelden und das Einloesen eines Uebergabe-Codes (<c>rh-session/handoff</c>, F1-008)
    /// brauchen das Cookie — ein EIGENER Pfad, den sonst keine Anwendung
    /// unter der Elterndomaene bedient. Mit <c>/api/auth</c> schickte der Browser das 30-Tage-Cookie an
    /// jeden Host der Domaene, der dort eine Anmeldung hat (Dev-Stacks, RCT, Lernkompass, Cal.com;
    /// Codereview N6-001). Gegen Hosts unter derselben Elterndomaene hilft der Pfad allein nicht ganz
    /// (die Dev-Stacks bedienen denselben Pfad) — das loest erst eine eigene Elterndomaene.
    /// </summary>
    public const string CookiePath = "/api/auth/rh-session";

    /// <summary>
    /// Pfad der Cookies, die vor N6-001 ausgegeben wurden. Wird nur noch GELOESCHT, bei jedem Schreiben
    /// und Loeschen des Cookies — Uebergang fuer eine Version, danach samt den Alt-Routen entfernen.
    /// </summary>
    public const string LegacyCookiePath = "/api/auth";

    /// <summary>
    /// Pfad des Merk-Cookies (<see cref="MovedMarkerName"/>): genau die Alt-Route <c>POST /api/auth/session</c>
    /// samt <c>session/end</c>. Uebergang wie <see cref="LegacyCookiePath"/>, mit den Alt-Routen entfernen.
    /// </summary>
    public const string MovedMarkerPath = "/api/auth/session";

    /// <summary>
    /// Name des Merk-Cookies, das neben dem Cookie liegt (Codereview N6-001, Nacharbeit): eine aeltere
    /// Oberflaeche aus dem Browser-Cache gleicht ueber <c>/api/auth/session</c> ab, und dorthin schickt der
    /// Browser das Cookie mit seinem neuen Pfad nicht mehr. Ohne Merker hiesse „kein Cookie" dort sowohl
    /// „abgemeldet" als auch „umgezogen" — und jede uebernommene Sitzung meldete sich beim zweiten Abgleich
    /// ab. Der Wert ist KEIN Nachweis (siehe <see cref="MovedMarkerFor"/>), er darf an andere Anwendungen
    /// unter <c>/api/auth/session</c> gehen.
    /// </summary>
    public string MovedMarkerName => CookieName + "_moved";

    /// <summary>
    /// Wert des Merk-Cookies fuer ein Konto: ein HMAC der Nutzer-Id, gekuerzt auf 128 Bit. Daraus laesst
    /// sich keine Anmeldung machen und die Id nicht ablesen; die Alt-Route erkennt damit nur, ob die
    /// geteilte Anmeldung noch dem Konto der fragenden Oberflaeche gehoert.
    /// </summary>
    public string MovedMarkerFor(int userId)
    {
        using var hmac = new System.Security.Cryptography.HMACSHA256(JwtTokens.SigningKey(_config).Key);
        var mac = hmac.ComputeHash(Encoding.UTF8.GetBytes(Audience + "|moved|" + userId));
        return Base64UrlEncoder.Encode(mac.AsSpan(0, 16).ToArray());
    }

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

        return JwtTokens.Issue(_config, Audience, claims, DateTime.UtcNow.Add(Lifetime));
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
            // Dieselben Pruefregeln wie der JWT-Handler der API, nur mit dem eigenen Adressaten — der ist der
            // Kern: ein normales Zugriffstoken darf hier NICHT durchgehen und dieses hier nirgendwo sonst.
            principal = new JwtSecurityTokenHandler().ValidateToken(
                cookieValue, JwtTokens.ValidationParameters(_config, Audience), out _);
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
}
