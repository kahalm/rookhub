using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
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
    public static readonly string[] AllowedScopes = { "extension" };
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

        var count = await _db.UserApiTokens.CountAsync(t => t.UserId == userId);
        if (count >= MaxTokensPerUser)
            throw new InvalidOperationException($"Maximum of {MaxTokensPerUser} tokens per user reached.");

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

    /// <summary>Prueft einen Raw-Token. Setzt <c>LastUsedAt</c> fire-and-forget. <c>null</c> = invalide/abgelaufen.</summary>
    public async Task<UserApiToken?> ValidateAsync(string rawToken)
    {
        if (string.IsNullOrEmpty(rawToken) || !rawToken.StartsWith(Prefix))
            return null;

        var hash = ComputeHash(rawToken);
        var token = await _db.UserApiTokens.FirstOrDefaultAsync(t => t.TokenHash == hash);
        if (token == null)
            return null;
        if (token.ExpiresAt.HasValue && token.ExpiresAt.Value < DateTime.UtcNow)
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
    public static string GenerateRawToken()
    {
        var buf = new byte[RandomBytes];
        RandomNumberGenerator.Fill(buf);
        // URL-safe Base64 ohne Padding (Base64URL).
        var b64 = Convert.ToBase64String(buf)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return Prefix + b64;
    }

    /// <summary>SHA-256-Hex (lowercase) eines Raw-Tokens.</summary>
    public static string ComputeHash(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
