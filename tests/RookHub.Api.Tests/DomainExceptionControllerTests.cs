using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Filters;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die ersten umgestellten Controller (Codereview 2026-09-29, A10-005): Rollenverwaltung und Punktepartie.
/// (1) Die bisherigen Fehlerantworten bleiben gleich (Status + <c>{ message }</c>), jetzt über den Filter.
/// (2) Ein echter Fehler — hier ein verworfener DbContext, ObjectDisposedException erbt von
/// InvalidOperationException — kommt NICHT mehr als 400 mit Framework-Text heraus, sondern läuft durch.
/// </summary>
public class DomainExceptionControllerTests : IDisposable
{
    private const string AnonSession = "11111111-2222-3333-4444-555555555555";
    private readonly AppDbContext _db;

    public DomainExceptionControllerTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private GuessSessionService GuessService() =>
        new(_db, new GuessStartPly(_db), new CommentSetService(_db, NullLogger<CommentSetService>.Instance));

    /// <summary>Die Antwort, die der Client sieht: Ergebnis der Aktion oder — bei einer Ausnahme — das,
    /// was der globale Filter daraus macht. Unbehandelte Ausnahmen werden weitergeworfen (→ 500).</summary>
    private static async Task<ObjectResult> Http(Func<Task<IActionResult?>> action)
    {
        try
        {
            return Assert.IsAssignableFrom<ObjectResult>(await action());
        }
        catch (Exception ex)
        {
            var ctx = new ExceptionContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                new List<IFilterMetadata>()) { Exception = ex };
            new DomainExceptionFilter().OnException(ctx);
            if (!ctx.ExceptionHandled) throw;
            return Assert.IsType<ObjectResult>(ctx.Result);
        }
    }

    private static void AssertError(ObjectResult result, int status, string message)
    {
        Assert.Equal(status, result.StatusCode);
        Assert.Equal(JsonSerializer.Serialize(new { message }), JsonSerializer.Serialize(result.Value));
    }

    [Fact]
    public async Task RolesAdmin_Fehlerantworten_bleibenGleich()
    {
        await RoleSeeder.SeedAsync(_db);
        var controller = new RolesAdminController(new RoleAdminService(_db));

        AssertError(await Http(async () => (await controller.Update(999_901,
            new UpdateRoleDto { Name = "x", Permissions = new() })).Result), 404, "Rolle nicht gefunden.");
        AssertError(await Http(async () => await controller.Delete(999_902)), 404, "Rolle nicht gefunden.");
        AssertError(await Http(async () => (await controller.GetUserRoles(999_903)).Result), 404, "User nicht gefunden.");
        AssertError(await Http(async () => (await controller.Create(new CreateRoleDto
        {
            Key = "bad", Name = "Bad", Permissions = new() { "nonsense.permission" },
        })).Result), 400, "Unbekannte Permission: nonsense.permission");

        var member = await _db.Roles.FirstAsync(r => r.Key == "member");
        AssertError(await Http(async () => await controller.Delete(member.Id)), 400,
            "System-Rollen können nicht gelöscht werden.");
    }

    /// <summary>Vorher: catch (InvalidOperationException ex) → 400 „Cannot access a disposed context
    /// instance…", ohne Log. Jetzt geht die Ausnahme zum globalen Handler.</summary>
    [Fact]
    public async Task RolesAdmin_EchterFehler_wirdNichtMehrZu400()
    {
        var controller = new RolesAdminController(new RoleAdminService(_db));
        _db.Dispose();

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => Http(async () => (await controller.Create(
            new CreateRoleDto { Key = "trainer", Name = "Trainer", Permissions = new() })).Result));
    }

    [Fact]
    public async Task GuessAnonym_Fehlerantworten_bleibenGleich()
    {
        var controller = new GuessSessionAnonymousController(GuessService());

        AssertError(await Http(async () => (await controller.Start(
            new CreateAnonymousGuessSessionRequest { SessionId = AnonSession, GameAnalysisId = 999_904 },
            CancellationToken.None)).Result), 404, "Analysis not found.");
        AssertError(await Http(async () => (await controller.Guess(999_905,
            new AnonymousGuessMoveRequest { SessionId = AnonSession, Uci = "e2e4" },
            CancellationToken.None)).Result), 404, "Session not found.");
        // Die Prüfung der Kennung bleibt im Controller.
        AssertError(await Http(async () => (await controller.Start(
            new CreateAnonymousGuessSessionRequest { SessionId = "kurz", GameAnalysisId = 1 },
            CancellationToken.None)).Result), 400, "Invalid session ID.");
    }

    /// <summary>Der anonyme Fall aus dem Fund: jeder echte Fehler kam hier ohne Anmeldung als 4xx mit
    /// internem Text heraus.</summary>
    [Fact]
    public async Task GuessAnonym_EchterFehler_wirdNichtMehrZu400()
    {
        var controller = new GuessSessionAnonymousController(GuessService());
        _db.Dispose();

        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => Http(async () => (await controller.Start(
            new CreateAnonymousGuessSessionRequest { SessionId = AnonSession, GameAnalysisId = 1 },
            CancellationToken.None)).Result));
        await Assert.ThrowsAnyAsync<ObjectDisposedException>(() => Http(async () => (await controller.Guess(1,
            new AnonymousGuessMoveRequest { SessionId = AnonSession, Uci = "e2e4" },
            CancellationToken.None)).Result));
    }
}
