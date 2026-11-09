using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers.League;
using RookHub.Api.Data;
using RookHub.Api.Services.League;

namespace RookHub.Api.Tests.League;

/// <summary>
/// R2-002 (Entscheidung E2): <c>league.manage</c> ist seit dem Mandanten-Schritt eine Vereinsrolle. Die GLOBALEN Schreibwege
/// (Aktualisieren, Rebuild, Selbstmeldungen, fremde Partien, Megabase-Verzeichnis, Übertragungen, Liga-Importe) wirken auf alle
/// Vereine und verlangen zusätzlich Admin — der Verwalter eines Vereins bekommt 403, bevor ein Dienst angefasst wird
/// (die Dienste sind hier <c>null</c>). Die positiven Fälle mit Admin stehen in den Import-Tests der einzelnen Quellen.
/// </summary>
public class LeagueGlobalAdminRoutesTests : IDisposable
{
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private LeagueController Ctl(bool admin) =>
        new LeagueController(new LeagueService(_db, LeagueModel.FromEmbedded(), NullLogger<LeagueService>.Instance), null!, null!)
            .As(7, admin);

    [Fact]
    public async Task Vereinsverwalter_ohneAdmin_bekommtAufAllenGlobalenWegen403()
    {
        var c = Ctl(admin: false);
        Assert.IsType<ForbidResult>(await c.ImportSelfReports(new(null, null), dryRun: true, _db, default));
        Assert.IsType<ForbidResult>(c.StartUpdate());
        Assert.IsType<ForbidResult>(await c.ImportGames("Mega", default));
        Assert.IsType<ForbidResult>(await c.Broadcasts(null!, default));
        Assert.IsType<ForbidResult>(await c.AddBroadcast(new("https://lichess.org/broadcast/x"), null!, default));
        Assert.IsType<ForbidResult>(await c.ImportMegaPlayers(null!, default));
        Assert.IsType<ForbidResult>(await c.Rebuild(default));
        Assert.IsType<ForbidResult>(await c.LigamanagerImport(new(null, "bsb", "2026/27", "x-1", null), true, null!, default));
        Assert.IsType<ForbidResult>(await c.ChessResultsImport(new(1, "2026/27", 2, "Liga", null, null, null), true, null!, default));
        Assert.IsType<ForbidResult>(await c.ImportZugspitzeOnlineReports("20211", true, null!, default));
        Assert.IsType<ForbidResult>(await c.ZugspitzeImport(new(null, 7, "2026/27", null), true, null!, default));
    }

    [Fact]
    public async Task Admin_kommtDurch_SelbstmeldungenOhneQuelle400()
    {
        // Admin erreicht den Dienst (hier: Prüfung der Eingabe), statt 403.
        Assert.IsType<BadRequestObjectResult>(await Ctl(admin: true).ImportSelfReports(new(null, null), dryRun: true, _db, default));
    }
}
