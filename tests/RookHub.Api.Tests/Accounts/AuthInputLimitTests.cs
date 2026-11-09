using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using RookHub.Api.Controllers.Accounts;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Tests.Accounts;

/// <summary>R1-001: der Anmeldename hatte keinen Deckel und wurde als Schlüssel der Konto-Bremse 15 Minuten im Speicher gehalten —
/// je Fehlversuch mit einem neuen Riesen-Namen ein Eintrag. Jetzt: <c>[MaxLength]</c> am DTO, Namen über 255 Zeichen zählen in der
/// Bremse nicht, und die anonymen Anmelde-Wege tragen eine kleine Rumpfgrenze.</summary>
public class AuthInputLimitTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    private readonly IConfiguration _config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Jwt:Key"] = "TestSecretKeyThatIsAtLeast32Characters!", ["Jwt:Issuer"] = "TestIssuer", ["Jwt:Audience"] = "TestAudience",
    }).Build();
    public void Dispose() => _db.Dispose();

    private static bool Valid(object dto) => Validator.TryValidateObject(dto, new ValidationContext(dto), [], validateAllProperties: true);

    [Fact]
    public void LoginDto_deckeltNameUndPasswort()
    {
        Assert.True(Valid(new LoginDto { Username = new string('a', 255), Password = "x" }));
        Assert.False(Valid(new LoginDto { Username = new string('a', 256), Password = "x" }));
        Assert.False(Valid(new LoginDto { Username = "bob", Password = new string('x', 1025) }));
    }

    [Fact]
    public async Task Login_mitUeberlangemNamen_legtKeinenBremsEintragAn()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var sut = new AuthService(_db, _config, new CapturingLogger<AuthService>(), null, cache);
        var huge = new string('z', 300);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.LoginAsync(new LoginDto { Username = huge, Password = "x" }));

        Assert.Equal(0, AuthService.RecentLoginFailures(cache, huge));
        Assert.Equal(0, cache.Count);
        // Ein normaler Fehlversuch zählt weiter.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => sut.LoginAsync(new LoginDto { Username = "bob", Password = "x" }));
        Assert.Equal(1, AuthService.RecentLoginFailures(cache, "bob"));
    }

    [Theory]
    [InlineData(nameof(AuthController.Login))]
    [InlineData(nameof(AuthController.Register))]
    [InlineData(nameof(AuthController.ForgotPassword))]
    [InlineData(nameof(AuthController.ResetPassword))]
    public void AnonymeAnmeldeWege_habenEineKleineRumpfgrenze(string action)
    {
        var limit = typeof(AuthController).GetMethod(action)!.GetCustomAttribute<RequestSizeLimitAttribute>();
        Assert.NotNull(limit);
        Assert.Equal(16 * 1024, AuthController.AuthBodyLimit);
    }
}
