using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace RookHub.Api.Services;

/// <summary>
/// EINE Stelle fuer Schluessel, Signatur und Pruefregeln der JWTs dieser API (Codereview 2026-09-29, A1-015):
/// das Zugriffstoken (<see cref="AuthService"/>, geprueft vom JWT-Handler in Program.cs) und das Cookie-Token der
/// geteilten Anmeldung (<see cref="SharedSessionService"/>, eigener Adressat). Vorher las jede der drei Stellen
/// <c>Jwt:Key</c> selbst, und die Pruefparameter standen zweimal — eine Haertung nur in Program.cs liesse den
/// Cookie-Tausch, der ein volles Zugriffstoken ausstellt, mit den alten Regeln weiterlaufen.
/// </summary>
public static class JwtTokens
{
    /// <summary>Standard waere 5 min — auf 1 min gestrafft, damit abgelaufene Tokens (insb. nach
    /// Logout/Passwortwechsel) nicht unnoetig lange akzeptiert werden.</summary>
    public static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

    /// <summary>Signierschluessel aus <c>Jwt:Key</c> (HMAC-SHA256).</summary>
    public static SymmetricSecurityKey SigningKey(IConfiguration config) => new(Encoding.UTF8.GetBytes(
        config["Jwt:Key"] ?? throw new InvalidOperationException("JWT key not configured")));

    /// <summary>Signiertes Token an <paramref name="audience"/>, Aussteller aus <c>Jwt:Issuer</c>.</summary>
    public static string Issue(IConfiguration config, string? audience, IEnumerable<Claim> claims, DateTime expiresUtc)
    {
        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: audience,
            claims: claims,
            expires: expiresUtc,
            signingCredentials: new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>Pruefregeln fuer ein Token an <paramref name="audience"/> — fuer den JWT-Handler UND den Cookie-Tausch.</summary>
    public static TokenValidationParameters ValidationParameters(IConfiguration config, string? audience) => new()
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = config["Jwt:Issuer"],
        ValidAudience = audience,
        IssuerSigningKey = SigningKey(config),
        ClockSkew = ClockSkew,
    };
}
