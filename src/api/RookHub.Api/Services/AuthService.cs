using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
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
    // (gleicher BCrypt-Workfactor wie echte Hashes -> gleiche Verify-Dauer; Faktor in PasswordHashing).
    private static readonly string DummyHash =
        PasswordHashing.Hash("rookhub-constant-time-dummy");

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

    /// <summary>Fehlversuchs-Stand EINES Kontos im Cache. Ein veränderliches Objekt statt eines int:
    /// nur so lässt sich atomar zählen (Get+Set verlor unter Parallelität Zählungen) und je Konto ein
    /// Tor halten.</summary>
    private sealed class LoginFailureState
    {
        public int Count;   // Versuche seit dem letzten Erfolg (gleitendes Fenster), nur per Interlocked
        public int Busy;    // 1 = für dieses Konto läuft gerade eine GEBREMSTE Prüfung
    }

    private static readonly object LoginFailureCreateLock = new();

    private static LoginFailureState GetLoginFailureState(IMemoryCache cache, string key)
    {
        if (cache.TryGetValue(key, out LoginFailureState? state) && state is not null) return state;
        // Anlegen unter Sperre: zwei gleichzeitige Erstversuche legten sonst je ein eigenes Objekt an,
        // und das spätere überschriebe das frühere — samt Zählung und Tor.
        lock (LoginFailureCreateLock)
        {
            if (cache.TryGetValue(key, out state) && state is not null) return state;
            state = new LoginFailureState();
            cache.Set(key, state, new MemoryCacheEntryOptions { SlidingExpiration = LoginFailureWindow });
            return state;
        }
    }

    /// <summary>Gezählte Versuche seit dem letzten Erfolg für diesen Anmeldenamen (0 = keine).</summary>
    internal static int RecentLoginFailures(IMemoryCache cache, string loginName) =>
        cache.TryGetValue(LoginFailureKey(loginName), out LoginFailureState? state) && state is not null
            ? Volatile.Read(ref state.Count)
            : 0;

    /// <summary>Ein laufender Anmeldeversuch: seine Wartezeit und — nur bei gebremstem Konto — das
    /// gehaltene Tor, das <see cref="Dispose"/> wieder freigibt.</summary>
    private readonly struct LoginAttempt(LoginFailureState? gate, TimeSpan delay) : IDisposable
    {
        public TimeSpan Delay { get; } = delay;
        public void Dispose()
        {
            if (gate is not null) Volatile.Write(ref gate.Busy, 0);
        }
    }

    /// <summary>
    /// Zählt DIESEN Versuch VORAB (atomar) als Fehlversuch; erst ein Erfolg setzt die Zählung zurück.
    /// So sehen gleichzeitige Versuche nicht alle denselben alten Stand, sondern steigende Zahlen.
    /// Ist das Konto gebremst, läuft je Konto höchstens EINE Prüfung gleichzeitig: weitere werden sofort
    /// abgewiesen (<see cref="LoginThrottledException"/> → 429) statt mitzuwarten — eine Wartezeit je
    /// Anfrage begrenzt bei parallelen Anfragen keine Rate. Bewusster Zielkonflikt: während eines
    /// laufenden Angriffs kann auch der echte Besitzer ein 429 sehen (kein Sperren über das Fenster hinaus).
    /// </summary>
    private LoginAttempt BeginLoginAttempt(string key)
    {
        if (_loginFailures is null) return new LoginAttempt(null, TimeSpan.Zero);
        var state = GetLoginFailureState(_loginFailures, key);
        var priorAttempts = Interlocked.Increment(ref state.Count) - 1;
        var delay = LoginThrottleDelay(priorAttempts);
        if (delay == TimeSpan.Zero) return new LoginAttempt(null, delay);
        if (Interlocked.CompareExchange(ref state.Busy, 1, 0) != 0)
            throw new LoginThrottledException();
        return new LoginAttempt(state, delay);
    }

    public async Task<AuthResponseDto> RegisterAsync(RegisterDto dto)
    {
        var username = dto.Username ?? string.Empty;
        // Case-insensitiv pruefen (passend zur case-insensitiven DB-Collation):
        // sonst koennte z.B. "admin" trotz vorhandenem "Admin" die Vorabpruefung
        // passieren und erst am Unique-Index als 500 statt 409 scheitern.
        if (await _db.AppUsers.AnyAsync(u => u.Username.ToLower() == username.ToLower()))
            throw new InvalidOperationException("Username or email already in use.");

        // Benutzername und E-Mail ueber Kreuz pruefen: der Login sucht zuerst den Benutzernamen und faellt
        // erst ohne Treffer auf die E-Mail zurueck. Ein Konto namens „opfer@example.org" fing sonst jeden
        // E-Mail-Login des echten Inhabers ab (Codereview A1-010). Bestandskollisionen bleiben unberuehrt.
        if (username.Contains('@'))
        {
            var usernameAsEmail = username.Trim().ToLowerInvariant();
            if (await _db.AppUsers.AnyAsync(u => u.Email == usernameAsEmail))
                throw new InvalidOperationException("Username or email already in use.");
        }

        // Email ist optional: leer/null -> kein Email hinterlegt, keine Dublettenpruefung.
        var normalizedEmail = string.IsNullOrWhiteSpace(dto.Email)
            ? null
            : dto.Email.Trim().ToLowerInvariant();

        if (normalizedEmail != null && await _db.AppUsers.AnyAsync(u => u.Email == normalizedEmail
                || u.Username.ToLower() == normalizedEmail))
            throw new InvalidOperationException("Username or email already in use.");

        var user = new AppUser
        {
            Username = dto.Username,
            Email = normalizedEmail,
            PasswordHash = PasswordHashing.Hash(dto.Password),
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
                // An alle mit der Nutzerverwaltung (users.manage — Admins immer), nicht nur ans Admin-Flag (F5-005).
                var adminIds = await PermissionResolver.UserIdsWithPermissionAsync(_db, Permissions.UsersManage);
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
        using var attempt = BeginLoginAttempt(failureKey);
        if (attempt.Delay > TimeSpan.Zero) await Task.Delay(attempt.Delay);

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
            // Gezählt ist der Versuch schon (BeginLoginAttempt) — ein Fehlschlag lässt ihn stehen.
            throw new UnauthorizedAccessException("Invalid username or password.");
        }

        // Vom Admin gesperrt (F5-011): erst NACH der Passwortprüfung verraten — wer das Passwort nicht kennt,
        // bekommt dieselbe Antwort wie oben und erfährt vom Sperrzustand nichts.
        if (user.IsLockedAt(DateTime.UtcNow))
            throw new AccountLockedException(user.LockedUntil!.Value);

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

    /// <summary>
    /// Baut eine Anmeldung fuer einen bereits FESTSTEHENDEN Nutzer — ohne Passwortpruefung, weil die
    /// anderswo passiert ist (heute: die Anmelde-Uebergabe zwischen den Oberflaechen, siehe
    /// <see cref="AuthHandoffService"/>). Dieselben Claims wie beim Login, damit ein uebergebenes
    /// Token nicht heimlich weniger kann als ein erlogenes.
    /// </summary>
    public async Task<AuthResponseDto> IssueTokenAsync(AppUser user, bool rememberMe = false) =>
        new AuthResponseDto
        {
            Token = GenerateJwt(user, rememberMe, await ResolvePermissionClaimsAsync(user.Id)),
            Username = user.Username,
            UserId = user.Id,
            IsAdmin = user.IsAdmin,
        };

    /// <summary>Die effektiven Permissions eines Users (eigene Rollen + Rollen seiner Gruppen) als <c>perm</c>-Claims —
    /// nur noch ein Startwert für ältere Oberflächen: geprüft wird live (<see cref="PermissionResolver"/>, 0.589.0), und
    /// die Oberflächen holen den Stand über <c>GET /api/auth/permissions</c>.</summary>
    private async Task<List<Claim>> ResolvePermissionClaimsAsync(int userId)
    {
        var perms = (await PermissionResolver.LoadAsync(_db, userId)).Permissions.OrderBy(p => p);
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

        user.PasswordHash = PasswordHashing.Hash(dto.NewPassword);
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

        return JwtTokens.Issue(_config, _config["Jwt:Audience"], claims,
            // „Eingeloggt bleiben": 90 Tage, sonst 30. JWTs sind stateless und werden nur über DeletedAt
            // + SecurityStamp (Passwort-Reset/-Änderung) invalidiert — ein abgegriffenes Token bliebe sonst
            // unnötig lange gültig, daher kein Jahr mehr.
            DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromDays(rememberMe ? 90 : 30)));
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
        // Ein gesperrtes Konto weist jedes Token ab (AuthUserValidation) — das Einstiegs-Token wäre sofort tot, und die
        // Oberfläche flöge mit dem 401 ganz hinaus. Lieber gleich sagen, warum es nicht geht (F5-011).
        if (target.IsLockedAt(DateTime.UtcNow))
            throw new InvalidOperationException("Cannot impersonate a locked account.");

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

/// <summary>Das Konto ist vom Admin gesperrt (<see cref="AppUser.LockedUntil"/>, F5-011); das Passwort stimmte.
/// Controller → 403 mit dem Sperrende (<c>null</c> = unbefristet).</summary>
public sealed class AccountLockedException(DateTime lockedUntil) : Exception("This account has been locked by an administrator.")
{
    public DateTime LockedUntil { get; } = lockedUntil;

    /// <summary>Sperrende für die Antwort: UTC, <c>null</c> bei einer unbefristeten Sperre.</summary>
    public DateTime? LockedUntilForClient =>
        LockedUntil >= AppUser.LockedIndefinitely ? null : DateTime.SpecifyKind(LockedUntil, DateTimeKind.Utc);
}

/// <summary>Die Konto-Bremse weist einen Anmeldeversuch ab, weil für dieses Konto schon eine gebremste
/// Prüfung läuft (siehe <see cref="AuthService"/>). Controller → 429.</summary>
public sealed class LoginThrottledException()
    : Exception("Too many login attempts for this account. Please wait a few seconds and try again.");
