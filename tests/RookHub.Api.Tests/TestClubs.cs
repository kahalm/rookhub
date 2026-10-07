using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Vereine der LeagueHub-Tests (Mandanten-Schritt 2026-10-07): ein Tiroler Testverein „Testdorf" (chess-results) und ein
/// bayerischer „SK Weiler" (Ligamanager, Mannschaften „SK Weiler 1"). Bewusst NICHT Schwaz/Weilheim — die Tests sollen zeigen,
/// dass nichts mehr an einem festen Vereinsnamen hängt.
/// </summary>
internal static class TestClubs
{
    public const int HomeId = 1;
    public const int OtherId = 2;

    /// <summary>Der Verein, aus dessen Sicht die meisten Tests laufen: Mannschaft „Testdorf", anonymisiert „Testdorf".</summary>
    public static LeagueClub Home => new() { Id = HomeId, Name = "SK Testdorf", TeamPrefix = "Testdorf", AnonName = "Testdorf" };

    /// <summary>Ein zweiter Verein (Bayern): Mannschaften „SK Weiler 1", anonymisiert „Weiler".</summary>
    public static LeagueClub Other => new()
    {
        Id = OtherId, Name = "SK Weiler", TeamPrefix = "SK Weiler", AnonName = "Weiler", Source = LigamanagerSource.Source,
    };

    /// <summary>Beide Vereine in die Datenbank (für Wege, die den Verein selbst nachschlagen: Teilen-Link, Taktik-Kurs, Resolver).</summary>
    public static async Task SeedAsync(AppDbContext db)
    {
        if (!db.LeagueClubs.Any(c => c.Id == HomeId)) db.LeagueClubs.Add(Home);
        if (!db.LeagueClubs.Any(c => c.Id == OtherId)) db.LeagueClubs.Add(Other);
        await db.SaveChangesAsync();
    }

    /// <summary>Den Controller als dieses Konto aufrufen (Admin = Rolle „Admin"), optional mit <c>?club=</c>.</summary>
    public static T As<T>(this T controller, int userId, bool admin = false, int? club = null) where T : ControllerBase
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) };
        if (club is int c) http.Request.QueryString = new QueryString($"?club={c}");
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }
}
