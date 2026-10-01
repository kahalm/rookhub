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
