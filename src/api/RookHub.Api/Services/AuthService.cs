using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class AuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthService> _logger;
    private readonly NotificationService? _notifications;
    private readonly IMemoryCache? _loginFailures;

    // Konstanter Dummy-Hash fuer timing-sichere Logins nicht existierender User
    // (gleicher BCrypt-Workfactor wie echte Hashes -> gleiche Verify-Dauer).
    private const int BcryptWorkFactor = 12;  // explizit & versionierbar statt Library-Default (10)
    private static readonly string DummyHash =
        BCrypt.Net.BCrypt.HashPassword("rookhub-constant-time-dummy", BcryptWorkFactor);

    public AuthService(AppDbContext db, IConfiguration config, ILogger<AuthService> logger,
        NotificationService? notifications = null, IMemoryCache? loginFailures = null)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _notifications = notifications;
        _loginFailures = loginFailures;
    }

    /// <summary>Zeitfenster der Fehlversuchs-Zählung je Konto (sliding: anhaltendes Raten hält die Bremse aktiv).</summary>
    private static readonly TimeSpan LoginFailureWindow = TimeSpan.FromMinutes(15);

    /// <summary>So viele Fehlversuche bleiben unverzögert (Vertipper).</summary>
    private const int FreeLoginAttempts = 5;

    /// <summary>Wartezeit VOR der Passwortprüfung, abhängig von den jüngsten Fehlversuchen DIESES Kontos.
    /// FALLE: der Auth-Rate-Limiter partitioniert nur nach IP — über IP-Rotation/Botnetz ist die
    /// Rate pro KONTO sonst unbegrenzt, und Online-Raten wird allein durch BCrypt nicht teuer genug.
    /// Bewusst Verzögerung statt Kontosperre: eine Sperre wäre ein Fremd-DoS („ich sperre dich aus"),
    /// die Verzögerung trifft praktisch nur den, der massenhaft rät.</summary>
    internal static TimeSpan LoginThrottleDelay(int recentFailures)
    {
        if (recentFailures <= FreeLoginAttempts) return TimeSpan.Zero;
        var steps = Math.Min(recentFailures - FreeLoginAttempts, 5);   // 250 ms … 4 s
        return TimeSpan.FromMilliseconds(250 * Math.Pow(2, steps - 1));
    }

    private static string LoginFailureKey(string loginName) => "login-fail:" + loginName.Trim().ToLowerInvariant();

    private void RegisterLoginFailure(string key) =>
        _loginFailures?.Set(key, (_loginFailures.Get<int?>(key) ?? 0) + 1,
            new MemoryCacheEntryOptions { SlidingExpiration = LoginFailureWindow });

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
    {
        var username = dto.Username ?? string.Empty;
        // Case-insensitiv pruefen (passend zur case-insensitiven DB-Collation):
        // sonst koennte z.B. "admin" trotz vorhandenem "Admin" die Vorabpruefung
        // passieren und erst am Unique-Index als 500 statt 409 scheitern.
        if (await _db.AppUsers.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
            throw new InvalidOperationException("Username or email already in use.");

        // Email ist optional: leer/null -> kein Email hinterlegt, keine Dublettenpruefung.
        var normalizedEmail = string.IsNullOrWhiteSpace(dto.Email)
            ? null
            : dto.Email.Trim().ToLowerInvariant();

        if (normalizedEmail != null && await _db.AppUsers.AnyAsync(u => u.Email == normalizedEmail))
            throw new InvalidOperationException("Username or email already in use.");

        var user = new AppUser
        {
            Username = dto.Username,
            Email = normalizedEmail,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password, BcryptWorkFactor),
            SecurityStamp = NewSecurityStamp(),
            Profile = new UserProfile()
        };

        _db.AppUsers.Add(user);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Race/Kollision am Unique-Index (gleichzeitige Registrierung oder
            // Casing-Kollision) -> sauberer Conflict (409) statt unbehandeltem 500.
            // NUR echte Duplikat-Fehler: ein transienter DB-Fehler (Deadlock/Timeout/
            // Verbindungsabriss) hiess sonst faelschlich "Username already exists" -
            // der User haelt den Namen fuer vergeben, obwohl ein Retry genuegt haette.
            throw new InvalidOperationException("Username or email already exists.");
        }

        // Admins über die Neu-Registrierung informieren (best-effort: ein Fehler beim
        // Benachrichtigen darf die erfolgreiche Registrierung nicht kippen).
        if (_notifications != null)
        {
            try
            {
                var adminIds = await _db.AppUsers.Where(u => u.IsAdmin).Select(u => u.Id).ToListAsync();
                if (adminIds.Count > 0)
                    await _notifications.CreateManyAsync(adminIds, NotificationType.NewUserRegistered,
                        new Dictionary<string, string> { ["username"] = user.Username }, "/admin");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Admin-Benachrichtigung über Neu-Registrierung fehlgeschlagen (userId={UserId})", user.Id);
            }
        }

        return new AuthResponseDto
        {
            Token = GenerateJwt(user),
            Username = user.Username,
            UserId = user.Id,
            IsAdmin = user.IsAdmin
        };
    }

    public async Task<AuthResponseDto> LoginAsync(LoginDto dto)
    {
        var loginName = dto.Username ?? string.Empty;

        // Konto-bezogene Bremse VOR jeder Prüfung anwenden (nicht erst im Fehlerfall) — sonst
        // verrät die Antwortzeit, ob das Passwort stimmte bzw. ob es das Konto überhaupt gibt.
        var failureKey = LoginFailureKey(loginName);
        var delay = LoginThrottleDelay(_loginFailures?.Get<int?>(failureKey) ?? 0);
        if (delay > TimeSpan.Zero) await Task.Delay(delay);

        var user = await _db.AppUsers
            .FirstOrDefaultAsync(u => u.Username.ToLower() == loginName.ToLower());

        // Anmeldung auch per E-Mail-Adresse (das Feld heisst "Benutzername", eingegeben wird
        // trotzdem oft die Mail — Resets laufen ueber die Mail, der Login schlug dann endlos fehl).
        // Der Username gewinnt bei Kollision (Usernames duerfen '@' enthalten, Lookup bleibt
        // deterministisch); E-Mails liegen normalisiert (trim+lower) in der DB.
        if (user == null && loginName.Contains('@'))
        {
            var normalizedEmail = loginName.Trim().ToLowerInvariant();
            user = await _db.AppUsers.FirstOrDefaultAsync(u => u.Email == normalizedEmail);
        }

        // Konstante Antwortzeit unabhaengig von der Existenz des Users: immer
        // einen BCrypt-Verify gegen einen Dummy-Hash ausfuehren, statt ihn per ||
        // zu ueberspringen (verhindert Username-Enumeration ueber Timing).
        var hash = user?.PasswordHash ?? DummyHash;
        var passwordOk = BCrypt.Net.BCrypt.Verify(dto.Password, hash);
        // Gelöschte/anonymisierte Accounts können sich nicht mehr einloggen (gleiche Antwort wie
        // ein falsches Passwort, damit der Zustand nicht ableitbar ist).
        if (user == null || !passwordOk || user.DeletedAt != null)
        {
            RegisterLoginFailure(failureKey);
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        _loginFailures?.Remove(failureKey);   // erfolgreicher Login setzt die Bremse zurück

        // Lazy-Backfill: Alt-User ohne Security-Stamp bekommen beim ersten Login einen — damit ihre
        // ab jetzt ausgegebenen Tokens den Stempel tragen und eine spätere Passwortänderung sie
        // wirklich invalidiert (statt für immer grandfathered zu bleiben).
        if (user.SecurityStamp == null)
        {
            user.SecurityStamp = NewSecurityStamp();
            await _db.SaveChangesAsync();
        }

        // Strukturierter Login-Event fuer Kibana: Logins/Tag (Count) + Unique Logins
        // (Cardinality auf fields.UserId). Nur bei erfolgreichem Login, analog zum
        // PuzzleAttempt-Log in PuzzleService. messageTemplate enthaelt "UserLogin".
        _logger.LogInformation(
            "UserLogin: User {UserId} {UserName} logged in",
            user.Id, user.Username);

        return new AuthResponseDto
        {
            Token = GenerateJwt(user, dto.RememberMe, await ResolvePermissionClaimsAsync(user.Id)),
            Username = user.Username,
            UserId = user.Id,
            IsAdmin = user.IsAdmin
        };
    }

    /// <summary>Löst die effektiven Permissions eines Users (über seine Rollen) als <c>perm</c>-Claims
    /// auf — landen im JWT und werden vom <see cref="Authorization.PermissionAuthorizationHandler"/>
    /// geprüft. Trade-off: das Token ist bis zum nächsten Login stale; eine Rollenänderung wirkt erst
    /// dann (bzw. sofort für Admins über die separate Admin-Rolle + SecurityStamp bei PW-Änderung).</summary>
    private async Task<List<Claim>> ResolvePermissionClaimsAsync(int userId)
    {
        var perms = await _db.RolePermissions
            .Where(rp => _db.UserRoles.Any(ur => ur.UserId == userId && ur.RoleId == rp.RoleId))
            .Select(rp => rp.Permission)
            .Distinct()
            .ToListAsync();
        return perms
            .Select(p => new Claim(Authorization.PermissionAuthorizationHandler.PermissionClaimType, p))
            .ToList();
    }

    /// <summary>Passwort ändern. Liefert ein FRISCHES Token zurück: der rotierte Security-Stamp
    /// entwertet auch das Token DIESER Sitzung, und ohne Ersatz flog der Nutzer eine Minute später
    /// (Cache-TTL von <see cref="AuthUserValidation"/>) kommentarlos aus der App — mitten in der
    /// Arbeit und ohne erkennbaren Zusammenhang zur Passwortänderung.</summary>
    public async Task<AuthResponseDto> ChangePasswordAsync(int userId, ChangePasswordDto dto)
    {
        var user = await _db.AppUsers.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, BcryptWorkFactor);
        // Security-Stamp rotieren → alle bisherigen JWTs (mit altem sstamp-Claim) werden ungültig.
        user.SecurityStamp = NewSecurityStamp();
        // API-Tokens (`rkh_…`) tragen KEINEN Stempel-Bezug und laufen ohne Angabe nie ab — der
        // Stempel widerruft sie also nicht. Nach einer Kompromittierung wäre ein vom Angreifer
        // angelegtes Extension-Token die verbleibende Hintertür (Repertoire-PGNs lesen, Share-Links
        // anlegen). Ein Passwortwechsel ist der dokumentierte Wiederherstellungspfad und muss ihn
        // schließen; die Extension-Tokens des Nutzers müssen danach neu erstellt werden.
        _db.UserApiTokens.RemoveRange(await _db.UserApiTokens.Where(t => t.UserId == userId).ToListAsync());
        await _db.SaveChangesAsync();
        // Gecachten Auth-Zustand verwerfen, sonst gelten fremde Sitzungen bis zu 60 s weiter.
        if (_loginFailures is not null) AuthUserValidation.Invalidate(_loginFailures, userId);

        return new AuthResponseDto
        {
            Token = GenerateJwt(user, extraClaims: await ResolvePermissionClaimsAsync(user.Id)),
            Username = user.Username,
            UserId = user.Id,
            IsAdmin = user.IsAdmin,
        };
    }

    /// <summary>Erzeugt einen frischen, kompakten Security-Stamp (Basis für die Token-Invalidierung).</summary>
    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

    /// <summary>Ist die <see cref="DbUpdateException"/> eine Unique-Index-Verletzung (Duplikat)?
    /// Primär strukturiert über den MariaDB-/MySQL-Fehlercode 1062; Nachrichts-Fallback deckt
    /// andere Provider (z. B. die InMemory-Test-DB) ab. Alles andere (Deadlock, Timeout,
    /// Verbindungsabriss) ist KEIN Duplikat und darf nicht als „already exists" maskiert werden.</summary>
    internal static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is MySqlConnector.MySqlException { ErrorCode: MySqlConnector.MySqlErrorCode.DuplicateKeyEntry }
        || (ex.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ?? false)
        || (ex.InnerException?.Message.Contains("unique", StringComparison.OrdinalIgnoreCase) ?? false)
        || ex.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase);

    private string GenerateJwt(AppUser user, bool rememberMe = false, IEnumerable<Claim>? extraClaims = null, TimeSpan? lifetime = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(
            _config["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured")));

        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Username)
        };

        if (user.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        // Security-Stamp als Claim mitgeben (sofern gesetzt) → wird bei jedem Request gegen die DB
        // geprüft; nach Passwort-Reset/-Änderung passt er nicht mehr → Token ungültig.
        if (user.SecurityStamp != null)
            claims.Add(new Claim("sstamp", user.SecurityStamp));

        if (extraClaims != null)
            claims.AddRange(extraClaims);

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"],
            audience: _config["Jwt:Audience"],
            claims: claims,
            // „Eingeloggt bleiben": 90 Tage, sonst 30. JWTs sind stateless und werden nur über DeletedAt
            // + SecurityStamp (Passwort-Reset/-Änderung) invalidiert — ein abgegriffenes Token bliebe sonst
            // unnötig lange gültig, daher kein Jahr mehr.
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(rememberMe ? 90 : 30)),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Erzeugt für einen Admin ein Token, mit dem er als Zielnutzer agiert („Als Nutzer einsteigen").
    /// Das Token trägt die echte Identität/Rollen des Zielnutzers + einen <c>imp</c>-Claim
    /// (ID des Admins, zur Nachvollziehbarkeit) und läuft bewusst kurz ab.
    /// </summary>
    public async Task<AuthResponseDto> ImpersonateAsync(int adminId, string adminUsername, int targetUserId,
        bool actorIsAdmin = true)
    {
        if (adminId == targetUserId)
            throw new InvalidOperationException("Cannot impersonate yourself.");

        var target = await _db.AppUsers.FindAsync(targetUserId)
            ?? throw new KeyNotFoundException("User not found.");

        // Rechteausweitung verhindern: das Impersonations-Token trägt die Rollen des ZIELS — wer nur die
        // Permission `users.manage` hat (delegierte Rolle, selbst KEIN Admin), könnte sich sonst über den
        // Einstieg in ein Admin-Konto volle Admin-Rechte verschaffen. Ein echter Admin darf weiterhin in
        // jedes Konto (Support-Fall).
        if (target.IsAdmin && !actorIsAdmin)
            throw new UnauthorizedAccessException("Only an admin may impersonate an admin account.");

        // Impersonation trägt die Rollen/Permissions des ZIEL-Users (der Admin agiert als dieser)
        // plus den imp-Claim zur Nachvollziehbarkeit.
        var impClaims = new List<Claim> { new Claim("imp", adminId.ToString()) };
        impClaims.AddRange(await ResolvePermissionClaimsAsync(target.Id));
        var token = GenerateJwt(target, extraClaims: impClaims, lifetime: TimeSpan.FromHours(2));

        // Audit-relevant -> landet strukturiert in ES/Kibana (auditierbar bleibt es auf Information).
        // Bewusst NICHT Warning: Impersonation ist ein legitimer Admin-Vorgang und verfälschte sonst
        // die Warn-Rate (log-watcher warn_spike). Severity hier = Information.
        _logger.LogInformation(
            "Impersonation: admin {AdminId} ({AdminName}) steigt als User {UserId} ({UserName}) ein",
            adminId, adminUsername, target.Id, target.Username);

        return new AuthResponseDto
        {
            Token = token,
            Username = target.Username,
            UserId = target.Id,
            IsAdmin = target.IsAdmin,
            Impersonating = true,
            ImpersonatorUsername = adminUsername,
        };
    }
}
