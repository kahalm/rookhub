using System.IdentityModel.Tokens.Jwt;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Schluessel, Signatur und Pruefregeln der JWTs an EINER Stelle (Codereview 2026-09-29, A1-015). Vorher lasen
/// Program.cs, AuthService und SharedSessionService <c>Jwt:Key</c> je selbst, und die Pruefparameter standen in
/// Program.cs und im Cookie-Tausch doppelt — eine Haertung nur im JWT-Handler liesse den Tausch, der ein volles
/// Zugriffstoken ausstellt, mit den alten Regeln weiterlaufen.
/// </summary>
public class JwtTokensTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "TestSecretKeyThatIsAtLeast32Characters!",
            ["Jwt:Issuer"] = "TestIssuer",
            ["Jwt:Audience"] = "TestAudience",
            ["Auth:SharedSessionDomain"] = ".example.test",
        }).Build();

    public JwtTokensTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AppUser> UserAsync()
    {
        var u = new AppUser { Username = "jwt", Email = "jwt@t.com", PasswordHash = "h", SecurityStamp = "s1" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    [Fact]
    public void Pruefregeln_SindDieGewohnten()
    {
        var p = JwtTokens.ValidationParameters(_config, "TestAudience");
        Assert.True(p.ValidateIssuer && p.ValidateAudience && p.ValidateLifetime && p.ValidateIssuerSigningKey);
        Assert.Equal("TestIssuer", p.ValidIssuer);
        Assert.Equal("TestAudience", p.ValidAudience);
        Assert.Equal(TimeSpan.FromMinutes(1), p.ClockSkew);
        Assert.Equal(JwtTokens.SigningKey(_config).Key, ((SymmetricSecurityKey)p.IssuerSigningKey).Key);
    }

    [Fact]
    public async Task Zugriffstoken_HatHs256AusstellerUndAdressat_UndBestehtDiePruefungDesHandlers()
    {
        var user = await UserAsync();
        var auth = new AuthService(_db, _config, new CapturingLogger<AuthService>());
        var raw = (await auth.IssueTokenAsync(user)).Token;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(raw);
        Assert.Equal("HS256", jwt.Header.Alg);
        Assert.Equal("TestIssuer", jwt.Issuer);
        Assert.Equal(["TestAudience"], jwt.Audiences);

        // Genau die Parameter, die Program.cs dem JWT-Handler gibt.
        new JwtSecurityTokenHandler().ValidateToken(raw,
            JwtTokens.ValidationParameters(_config, _config["Jwt:Audience"]), out _);
        // Und der Cookie-Tausch nimmt es NICHT (anderer Adressat).
        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(raw,
            JwtTokens.ValidationParameters(_config, SharedSessionService.Audience), out _));
    }

    [Fact]
    public async Task CookieToken_GehtNurDurchDenTausch_NichtDurchDenHandler()
    {
        var user = await UserAsync();
        var svc = new SharedSessionService(_db, new AuthService(_db, _config, new CapturingLogger<AuthService>()), _config);
        var cookie = await svc.IssueAsync(user.Id);
        Assert.NotNull(cookie);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(cookie);
        Assert.Equal("HS256", jwt.Header.Alg);
        Assert.Equal("TestIssuer", jwt.Issuer);
        Assert.Equal([SharedSessionService.Audience], jwt.Audiences);

        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(cookie,
            JwtTokens.ValidationParameters(_config, _config["Jwt:Audience"]), out _));
        Assert.NotNull(await svc.RedeemAsync(cookie));
    }

    private static string ApiRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "src", "api", "RookHub.Api");
    }

    private static List<string> DateienMit(Regex muster)
    {
        var root = ApiRoot();
        return Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .Where(p => !p.StartsWith("bin/") && !p.StartsWith("obj/"))
            .Where(p => muster.IsMatch(File.ReadAllText(Path.Combine(root, p))))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Wachhund: Pruefregeln entstehen nur in JwtTokens — ein zweiter Satz <c>TokenValidationParameters</c>
    /// ist genau die Kopie, die bei einer Haertung zurueckbleibt. Program.cs nennt den Typ nur als Ziel der Zuweisung
    /// an den JWT-Handler und holt die Werte aus JwtTokens.</summary>
    [Fact]
    public void Pruefparameter_NurInJwtTokens()
    {
        Assert.Equal(["Program.cs", "Services/JwtTokens.cs"], DateienMit(new Regex(@"\bTokenValidationParameters\b")));
        var program = File.ReadAllText(Path.Combine(ApiRoot(), "Program.cs"));
        Assert.Contains("JwtTokens.ValidationParameters(", program);
        Assert.DoesNotMatch(new Regex(@"new\s+TokenValidationParameters\b"), program);
    }

    /// <summary>Wachhund: der Signierschluessel wird nur in JwtTokens gelesen. Die beiden anderen Treffer haben einen
    /// anderen Zweck: ScoresheetScanService nimmt den Wert als Geheimnis fuer IP-Hashes, SecretConfigCheck nennt nur
    /// den Namen der Umgebungsvariablen.</summary>
    [Fact]
    public void JwtKey_NurInJwtTokensGelesen()
    {
        Assert.Equal(
            ["Services/JwtTokens.cs", "Services/ScoresheetScanService.cs", "Services/SecretConfigCheck.cs"],
            DateienMit(new Regex(@"\[""Jwt:Key""\]")));
    }
}
