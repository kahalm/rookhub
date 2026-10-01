namespace RookHub.Api.Tests;

/// <summary>
/// Aufruferlose Endpunkte, die der Codereview 2026-09-29 (Karte §4 „Tote Pfade") entfernt hat. Keine der Oberflächen
/// (App, Turnier, KidHub, LeagueHub), weder RepCheck noch Bot, Engine-Provider oder Skripte rufen sie auf; sie
/// vergrößerten nur Angriffs- und Pflegefläche. Kommt eine Route zurück, soll das eine bewusste Entscheidung sein
/// (Aufrufer gleich mitliefern und den Eintrag hier streichen), kein Rückfall durch Kopieren alten Codes.
/// </summary>
public class RetiredEndpointsTests
{
    private static readonly string[] Retired =
    [
        "GET /api/my-groups",                       // MeController.MyGroups (A1-019)
        // N11-010: Doppel bzw. Karteileichen
        "GET /api/explorer/sources",                // Doppel zu GET /api/repertoires/explorer/sources (das nutzt das Frontend)
        "POST /api/courses/upload",                 // alte Zweitroute zu POST /api/courses
        "GET /api/revenge/notifications/count",     // Badge-Zähler, den keine Navbar abfragt
        "GET /api/kids/progress",                   // KidHub gleicht nur per PUT ab (Antwort = gemeinsamer Stand)
        // N4-009: lieferte auch abgelaufene Links; LeagueHub holt einen vorhandenen Link über POST (gleicher Link, solange er gilt)
        "GET /api/league/share",
        // A9-008: Komplettlöschung aller Puzzles ohne Aufrufer — scheiterte relational am Restrict-FK der
        // RevengeNotifications und hätte FavoritePuzzles/PuzzleChallenges/WorksheetItems mit toten Ids zurückgelassen
        "DELETE /api/admin/puzzles",
    ];

    [Fact]
    public void RetiredEndpoints_StayRemoved()
    {
        var live = EndpointAuthInventoryTests.Inventory()
            .Select(e => $"{e.Method} {e.Template}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Nicht dadurch grün werden, dass das Inventar leer läuft.
        Assert.True(live.Count > 100, $"Nur {live.Count} Routen gefunden — Inventar kaputt?");

        var back = Retired.Where(live.Contains).ToList();
        Assert.True(back.Count == 0,
            "Entfernte Route wieder da (Aufrufer mitliefern und Retired-Liste nachziehen):\n  " + string.Join("\n  ", back));
    }
}
