using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
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

    // Konstanter Dummy-Hash fuer timing-sichere Logins nicht existierender User
    // (gleicher BCrypt-Workfactor wie echte Hashes -> gleiche Verify-Dauer).
    private const int BcryptWorkFactor = 12;  // explizit & versionierbar statt Library-Default (10)
    private static readonly string DummyHash =
        BCrypt.Net.BCrypt.HashPassword("rookhub-constant-time-dummy", BcryptWorkFactor);

    public AuthService(AppDbContext db, IConfiguration config, ILogger<AuthService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
    }

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
        catch (DbUpdateException)
        {
            // Race/Kollision am Unique-Index (gleichzeitige Registrierung oder
            // Casing-Kollision) -> sauberer Conflict (409) statt unbehandeltem 500.
            throw new InvalidOperationException("Username or email already exists.");
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
        var user = await _db.AppUsers
            .FirstOrDefaultAsync(u => u.Username.ToLower() == loginName.ToLower());

        // Konstante Antwortzeit unabhaengig von der Existenz des Users: immer
        // einen BCrypt-Verify gegen einen Dummy-Hash ausfuehren, statt ihn per ||
        // zu ueberspringen (verhindert Username-Enumeration ueber Timing).
        var hash = user?.PasswordHash ?? DummyHash;
        var passwordOk = BCrypt.Net.BCrypt.Verify(dto.Password, hash);
        if (user == null || !passwordOk)
            throw new UnauthorizedAccessException("Invalid username or password.");

        // Gelöschte/anonymisierte Accounts können sich nicht mehr einloggen.
        if (user.DeletedAt != null)
            throw new UnauthorizedAccessException("Invalid username or password.");

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
            Token = GenerateJwt(user, dto.RememberMe),
            Username = user.Username,
            UserId = user.Id,
            IsAdmin = user.IsAdmin
        };
    }

    public async Task ChangePasswordAsync(int userId, ChangePasswordDto dto)
    {
        var user = await _db.AppUsers.FindAsync(userId)
            ?? throw new KeyNotFoundException("User not found.");

        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            throw new UnauthorizedAccessException("Current password is incorrect.");

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword, BcryptWorkFactor);
        // Security-Stamp rotieren → alle bisherigen JWTs (mit altem sstamp-Claim) werden ungültig.
        user.SecurityStamp = NewSecurityStamp();
        await _db.SaveChangesAsync();
    }

    /// <summary>Erzeugt einen frischen, kompakten Security-Stamp (Basis für die Token-Invalidierung).</summary>
    public static string NewSecurityStamp() => Guid.NewGuid().ToString("N");

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
            // „Eingeloggt bleiben": 1 Jahr, sonst 30 Tage.
            expires: DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(rememberMe ? 365 : 30)),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Erzeugt für einen Admin ein Token, mit dem er als Zielnutzer agiert („Als Nutzer einsteigen").
    /// Das Token trägt die echte Identität/Rollen des Zielnutzers + einen <c>imp</c>-Claim
    /// (ID des Admins, zur Nachvollziehbarkeit) und läuft bewusst kurz ab.
    /// </summary>
    public async Task<AuthResponseDto> ImpersonateAsync(int adminId, string adminUsername, int targetUserId)
    {
        if (adminId == targetUserId)
            throw new InvalidOperationException("Cannot impersonate yourself.");

        var target = await _db.AppUsers.FindAsync(targetUserId)
            ?? throw new KeyNotFoundException("User not found.");

        var token = GenerateJwt(
            target,
            extraClaims: new[] { new Claim("imp", adminId.ToString()) },
            lifetime: TimeSpan.FromHours(2));

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
