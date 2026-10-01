using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
using RookHub.Api.Filters;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Fehlercodes neben der Meldung (Codereview 2026-09-29, F5-019): Die Oberfläche zeigte <c>err.error.message</c> roh,
/// in der Sprache des Dienstes (EN oder DE) statt der UI-Sprache. Jetzt trägt die Antwort zusätzlich einen stabilen
/// <c>code</c>, das Frontend übersetzt ihn (<c>apiErrors.&lt;code&gt;</c>); <c>message</c> bleibt wortgleich.
/// </summary>
public class ApiErrorCodesTests
{
    /// <summary>Der <c>code</c> im Rumpf eines Ergebnisses (null, wenn keiner da ist).</summary>
    public static string? CodeOf(IActionResult? result)
    {
        var value = Assert.IsAssignableFrom<ObjectResult>(result).Value;
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    [Fact]
    public void Body_OhneCode_BleibtDieBisherigeForm()
    {
        Assert.Equal(JsonSerializer.Serialize(new { message = "x" }),
            JsonSerializer.Serialize(ApiErrorResponses.Body(new ConflictException("x"))));
        Assert.Equal(JsonSerializer.Serialize(new { message = "y" }),
            JsonSerializer.Serialize(ApiErrorResponses.Body(new InvalidOperationException("y"))));
    }

    [Fact]
    public void Body_MitCode_HaengtCodeAn()
    {
        Assert.Equal(JsonSerializer.Serialize(new { message = "x", code = "friendship_exists" }),
            JsonSerializer.Serialize(ApiErrorResponses.Body(
                new ConflictException("x") { Code = ApiErrorCodes.FriendshipExists })));
    }

    [Fact]
    public void Filter_CodierteDomaenenAusnahme_LiefertMessageUndCode()
    {
        var ctx = new ExceptionContext(
            new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>())
        {
            Exception = new DomainValidationException("Eine Rolle mit diesem Key existiert bereits.")
            {
                Code = ApiErrorCodes.RoleKeyTaken,
            },
        };

        new DomainExceptionFilter().OnException(ctx);

        Assert.True(ctx.ExceptionHandled);
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(400, result.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(new { message = "Eine Rolle mit diesem Key existiert bereits.", code = "role_key_taken" }),
            JsonSerializer.Serialize(result.Value));
    }

    /// <summary>Der Fall aus dem Fund: der kroatische Admin las „Eine Rolle mit diesem Key existiert bereits.".</summary>
    [Fact]
    public async Task RolleMitVergebenemKey_TraegtCode()
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await RoleSeeder.SeedAsync(db);

        var ex = await Assert.ThrowsAsync<DomainValidationException>(() => new RoleAdminService(db).CreateAsync(
            new CreateRoleDto { Key = "member", Name = "Doppelt", Permissions = new() }));

        Assert.Equal(ApiErrorCodes.RoleKeyTaken, ex.Code);
    }

    private static IEnumerable<string> AllCodes() => typeof(ApiErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.IsLiteral)
        .Select(f => (string)f.GetRawConstantValue()!);

    [Fact]
    public void Codes_SindEindeutigUndSnakeCase()
    {
        var codes = AllCodes().ToList();
        Assert.NotEmpty(codes);
        Assert.Equal(codes.Count, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[a-z][a-z0-9_]*$", c));
    }

    /// <summary>
    /// Ohne Übersetzung zeigt das Frontend wieder die rohe Meldung — also muss jeder Code in den gepflegten Sprachen
    /// (FORMAT_LOCALES: en, de, hr, hu; dieselben, die i18n-parity.spec.ts vollständig verlangt) einen Text haben.
    /// </summary>
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("hr")]
    [InlineData("hu")]
    public void JederCode_HatEinenText_InDenGepflegtenSprachen(string lang)
    {
        var path = Path.Combine(RepoRoot(), "src", "frontend", "app", "public", "i18n", $"{lang}.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.True(doc.RootElement.TryGetProperty("apiErrors", out var texts), $"{lang}.json: apiErrors fehlt");

        var missing = AllCodes()
            .Where(c => !texts.TryGetProperty(c, out var t) || string.IsNullOrWhiteSpace(t.GetString()))
            .ToList();
        Assert.Empty(missing);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "compose.dev.yml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
