using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Persoenliche API-Tokens (GitHub-PAT-Stil) fuer Maschinen-Clients.
/// Raw-Tokens werden NIE gespeichert — nur ihr SHA-256-Hex-Hash.
/// </summary>
public class ApiTokenService
{
    public const string Prefix = "rkh_";
    public const int RandomBytes = 32;          // → ~43 Char Base64URL ohne Padding
    public const int PrefixLength = 12;         // "rkh_" + 8 zufaellige Zeichen → ApiTokenDto.Prefix
    public const string DefaultScope = "extension";
    /// <summary>Scope fuer den Engine-Provider auf dem Rechner des Nutzers: er registriert seine Engine
    /// (<c>/api/external-engine</c>) und holt dort Arbeit. Bewusst ein EIGENER Scope — ein Token, der auf
    /// einem fremden Rechner in einer <c>.env</c> liegt, soll nicht auch die Repertoires lesen koennen
    /// (und umgekehrt ein Extension-Token keine Engines anlegen).</summary>
    public const string EngineScope = "engine";
    public static readonly string[] AllowedScopes = { DefaultScope, EngineScope };
    public const int MaxTokensPerUser = 20;
    /// <summary>LastUsedAt wird höchstens einmal pro diesem Fenster persistiert (Auth-Hot-Path-Drossel).</summary>
    public static readonly TimeSpan LastUsedThrottle = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;
    private readonly ILogger<ApiTokenService> _logger;

    public ApiTokenService(AppDbContext db, ILogger<ApiTokenService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Legt einen neuen Token an. <paramref name="expiresInDays"/> <c>null</c> = nie ablaufen.
    /// Wirft <see cref="InvalidOperationException"/> bei ungueltigem Scope oder ueber Limit.
    /// </summary>
    public async Task<ApiTokenCreatedDto> CreateAsync(int userId, string name, string? scope, int? expiresInDays)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("name is required.");
        if (name.Length > 100) name = name[..100];

        var effectiveScope = string.IsNullOrEmpty(scope) ? DefaultScope : scope;
        if (!AllowedScopes.Contains(effectiveScope))
            throw new InvalidOperationException($"Unsupported scope: {effectiveScope}.");

        // Abgelaufene Tokens zählen nicht mit: sie authentifizieren nichts mehr, und wer nach Ablauf
        // neu verbindet (RepCheck), soll nicht erst im Profil aufräumen müssen.
        var now = DateTime.UtcNow;
        var count = await _db.UserApiTokens.CountAsync(t => t.UserId == userId && (t.ExpiresAt == null || t.ExpiresAt >= now));
        if (count >= MaxTokensPerUser)
            throw new DomainValidationException($"Maximum of {MaxTokensPerUser} tokens per user reached.")
                { Code = ApiErrorCodes.TokenLimitReached };

        var rawToken = GenerateRawToken();
        var hash = ComputeHash(rawToken);
        var prefix = rawToken[..PrefixLength];

        var entity = new UserApiToken
        {
            UserId = userId,
            Name = name,
            TokenHash = hash,
            Prefix = prefix,
            Scope = effectiveScope,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresInDays.HasValue ? DateTime.UtcNow.AddDays(expiresInDays.Value) : null,
        };
        _db.UserApiTokens.Add(entity);
        await _db.SaveChangesAsync();

        _logger.LogInformation("ApiToken: created user={UserId} id={Id} scope={Scope} expires={Expires}",
            userId, entity.Id, entity.Scope, entity.ExpiresAt);

        return new ApiTokenCreatedDto
        {
            Id = entity.Id,
            Name = entity.Name,
            Prefix = entity.Prefix,
            Scope = entity.Scope,
            CreatedAt = entity.CreatedAt,
            LastUsedAt = entity.LastUsedAt,
            ExpiresAt = entity.ExpiresAt,
            RawToken = rawToken,
        };
    }

    public async Task<List<ApiTokenDto>> ListAsync(int userId)
    {
        return await _db.UserApiTokens
            .Where(t => t.UserId == userId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new ApiTokenDto
            {
                Id = t.Id,
                Name = t.Name,
                Prefix = t.Prefix,
                Scope = t.Scope,
                CreatedAt = t.CreatedAt,
                LastUsedAt = t.LastUsedAt,
                ExpiresAt = t.ExpiresAt,
            })
            .ToListAsync();
    }

    /// <summary>Loescht einen Token. Wirft <see cref="KeyNotFoundException"/> wenn der Token nicht zum User gehoert.</summary>
    public async Task RevokeAsync(int userId, int id)
    {
        var token = await _db.UserApiTokens.FirstOrDefaultAsync(t => t.Id == id && t.UserId == userId)
            ?? throw new KeyNotFoundException("Token not found.");
        _db.UserApiTokens.Remove(token);
        await _db.SaveChangesAsync();
        _logger.LogInformation("ApiToken: revoked user={UserId} id={Id}", userId, id);
    }

    /// <summary>Prueft einen Raw-Token OHNE ihn als benutzt zu vermerken (fuer <c>POST /api/token/test</c>:
    /// eine Vorabpruefung ist keine Benutzung). <c>null</c> = unbekannt/abgelaufen.</summary>
    public async Task<UserApiToken?> FindValidAsync(string rawToken, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(rawToken) || !rawToken.StartsWith(Prefix, StringComparison.Ordinal))
            return null;

        var hash = ComputeHash(rawToken);
        var token = await _db.UserApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token == null)
            return null;
        if (token.ExpiresAt.HasValue && token.ExpiresAt.Value < DateTime.UtcNow)
            return null;
        return token;
    }

    /// <summary>Prueft einen Raw-Token. Setzt <c>LastUsedAt</c> fire-and-forget. <c>null</c> = invalide/abgelaufen.</summary>
    public async Task<UserApiToken?> ValidateAsync(string rawToken)
    {
        var token = await FindValidAsync(rawToken);
        if (token == null)
            return null;

        // LastUsedAt aktualisieren — aber gedrosselt: jeder authentifizierte Request liefe sonst
        // in ein SaveChanges (Auth-Hot-Path). Nur schreiben, wenn der letzte Zeitstempel fehlt
        // oder älter als das Drossel-Fenster ist. Bei DB-Fehler nicht die Auth verhindern.
        var now = DateTime.UtcNow;
        if (token.LastUsedAt == null || now - token.LastUsedAt.Value >= LastUsedThrottle)
        {
            try
            {
                token.LastUsedAt = now;
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "ApiToken: LastUsedAt update fehlgeschlagen (id={Id})", token.Id);
            }
        }

        return token;
    }

    /// <summary>Generiert einen neuen Raw-Token im Format <c>rkh_&lt;43-char-base64url&gt;</c>.</summary>
    public static string GenerateRawToken() => Prefix + SecretTokens.NewRaw(RandomBytes);

    /// <summary>SHA-256-Hex (lowercase) eines Raw-Tokens.</summary>
    public static string ComputeHash(string rawToken) => SecretTokens.Sha256Hex(rawToken);
}
