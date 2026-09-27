using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// KidHub-Fortschritt im Konto: die Zusammenfuehr-Regel (<see cref="KidsProgressMerge"/>) und der
/// Abgleich (<see cref="KidsProgressService"/>).
///
/// <para>Die Faelle der Regel sind LITERAL dieselben wie in <c>src-kidhub/app/core/kids-progress.store.spec.ts</c>
/// (<c>mergeProgress</c>) — wer eine Seite aendert, aendert beide.</para>
/// </summary>
public class KidsProgressTests : IDisposable
{
    private readonly AppDbContext _db;
    private static readonly DateTime Now = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
    private readonly KidsProgressService _service;

    public KidsProgressTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = new KidsProgressService(_db, () => Now);
    }

    public void Dispose() => _db.Dispose();

    private static KidsLevelProgressDto L(int level, int stars, int runIndex, int runMistakes, long runAt) =>
        new() { Level = level, Stars = stars, RunIndex = runIndex, RunMistakes = runMistakes, RunAt = runAt };

    private static KidsCourseProgressDto C(int bookId, long resetAt, params (int Id, long At)[] solved) =>
        new() { BookId = bookId, ResetAt = resetAt, Solved = solved.Select(s => new KidsSolvedLineDto { Id = s.Id, At = s.At }).ToList() };

    private static string Show(KidsProgressDto p) =>
        string.Join(" ", p.Levels.Select(l => $"L{l.Level}:{l.Stars}/{l.RunIndex}/{l.RunMistakes}@{l.RunAt}"))
        + " | " + string.Join(" ", p.Courses.Select(c => $"C{c.BookId}@{c.ResetAt}[{string.Join(",", c.Solved.Select(s => $"{s.Id}@{s.At}"))}]"));

    // ---- Regel (Faelle A–E, gespiegelt im Frontend) ----

    [Fact]
    public void A_SterneNachHoehe_DurchgangDerJuengere()
    {
        var a = new KidsProgressDto { Levels = { L(1, 2, 3, 1, 1000) } };
        var b = new KidsProgressDto { Levels = { L(1, 3, 0, 0, 2000), L(2, 0, 4, 2, 1500) } };
        Assert.Equal("L1:3/0/0@2000 L2:0/4/2@1500 | ", Show(KidsProgressMerge.Merge(a, b)));
    }

    [Fact]
    public void A2_AelterDurchgang_HoehereSterne_BeidesBleibt()
    {
        var a = new KidsProgressDto { Levels = { L(1, 3, 2, 0, 5000) } };
        var b = new KidsProgressDto { Levels = { L(1, 1, 6, 4, 4000) } };
        Assert.Equal("L1:3/2/0@5000 | ", Show(KidsProgressMerge.Merge(a, b)));
    }

    [Fact]
    public void B_GleicheZeit_ErsterGewinnt()
    {
        var a = new KidsProgressDto { Levels = { L(1, 1, 5, 2, 3000) } };
        var b = new KidsProgressDto { Levels = { L(1, 0, 7, 0, 3000) } };
        Assert.Equal("L1:1/5/2@3000 | ", Show(KidsProgressMerge.Merge(a, b)));
    }

    [Fact]
    public void C_LinienVereinigt_VonVornWirftAeltereWeg()
    {
        var a = new KidsProgressDto { Courses = { C(5, 0, (10, 100), (11, 200)) } };
        var b = new KidsProgressDto { Courses = { C(5, 150, (12, 300)), C(6, 0, (20, 50)) } };
        Assert.Equal(" | C5@150[11@200,12@300] C6@0[20@50]", Show(KidsProgressMerge.Merge(a, b)));
    }

    [Fact]
    public void D_NachDemVonVornWiederGeloest_ZaehltMitJuengsterZeit()
    {
        var a = new KidsProgressDto { Courses = { C(5, 0, (10, 100)) } };
        var b = new KidsProgressDto { Courses = { C(5, 150, (10, 400)) } };
        Assert.Equal(" | C5@150[10@400]", Show(KidsProgressMerge.Merge(a, b)));
    }

    [Fact]
    public void E_LeererKursOhneVonVorn_FaelltWeg()
    {
        var a = new KidsProgressDto { Courses = { C(7, 0) } };
        Assert.Equal(" | ", Show(KidsProgressMerge.Merge(a, new KidsProgressDto())));
    }

    // ---- Abgleich ----

    [Fact]
    public async Task ErsterAbgleich_UebernimmtDenBrowserStand()
    {
        var local = new KidsProgressDto { Levels = { L(1, 3, 0, 0, 1000), L(2, 0, 2, 1, 2000) }, Courses = { C(5, 0, (10, 100)) } };

        var merged = await _service.SyncAsync(1, local);

        Assert.Equal("L1:3/0/0@1000 L2:0/2/1@2000 | C5@0[10@100]", Show(merged));
        Assert.Equal(Show(merged), Show(await _service.GetAsync(1)));
    }

    [Fact]
    public async Task ZweitesGeraet_BekommtDenGemeinsamenStand()
    {
        await _service.SyncAsync(1, new KidsProgressDto { Levels = { L(1, 3, 0, 0, 1000) }, Courses = { C(5, 0, (10, 100)) } });

        var tablet = await _service.SyncAsync(1, new KidsProgressDto { Levels = { L(2, 1, 0, 0, 3000) }, Courses = { C(5, 0, (11, 200)) } });

        Assert.Equal("L1:3/0/0@1000 L2:1/0/0@3000 | C5@0[10@100,11@200]", Show(tablet));
        Assert.Equal(2, await _db.KidsCourseLines.CountAsync());
    }

    [Fact]
    public async Task VonVorn_RaeumtDieLinienImKontoAb()
    {
        await _service.SyncAsync(1, new KidsProgressDto { Courses = { C(5, 0, (10, 100), (11, 200)) } });

        var after = await _service.SyncAsync(1, new KidsProgressDto { Courses = { C(5, 300) } });

        Assert.Equal(" | C5@300[]", Show(after));
        Assert.Equal(0, await _db.KidsCourseLines.CountAsync());
        var progress = await _db.KidsCourseProgresses.SingleAsync();
        Assert.Equal(KidsProgressService.FromMs(300), progress.ResetAt);
    }

    [Fact]
    public async Task JedesKontoHatSeinenEigenenStand()
    {
        await _service.SyncAsync(1, new KidsProgressDto { Levels = { L(1, 3, 0, 0, 1000) } });
        await _service.SyncAsync(2, new KidsProgressDto { Levels = { L(1, 1, 0, 0, 1000) } });

        Assert.Equal("L1:3/0/0@1000 | ", Show(await _service.GetAsync(1)));
        Assert.Equal("L1:1/0/0@1000 | ", Show(await _service.GetAsync(2)));
    }

    [Fact]
    public async Task UnsinnigeWerte_WerdenBegrenzt()
    {
        var farFuture = KidsProgressService.ToMs(Now.AddYears(70));
        var merged = await _service.SyncAsync(1, new KidsProgressDto
        {
            Levels = { L(0, 3, 0, 0, 1), L(1, 9, -4, -1, farFuture), L(1, 2, 0, 0, 5) },
            Courses = { C(-3, 0, (1, 1)), C(5, 0, (0, 1), (12, -9)) },
        });

        var maxAt = KidsProgressService.ToMs(Now + KidsProgressService.MaxClockAhead);
        Assert.Equal($"L1:3/0/0@{maxAt} | C5@0[12@1]", Show(merged));     // Linie ohne Zeit: 1, nicht verworfen
    }

    [Fact]
    public async Task ZuVieleStufen_SindEinFehler()
    {
        var huge = new KidsProgressDto { Levels = Enumerable.Range(1, KidsProgressService.MaxLevels + 1).Select(i => L(i, 1, 0, 0, 1)).ToList() };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SyncAsync(1, huge));
    }

    [Fact]
    public void Zeiten_ueberlebenDenWegZurDatenbank()
    {
        const long ms = 1790000000123;
        Assert.Equal(ms, KidsProgressService.ToMs(KidsProgressService.FromMs(ms)));
    }

    [Fact]
    public async Task Endpunkt_ZuGross_Ist400()
    {
        var controller = new KidsController(new KidsPuzzleService(_db), progress: _service)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "7") }, "test")),
                },
            },
        };
        var huge = new KidsProgressDto { Courses = Enumerable.Range(1, KidsProgressService.MaxCourses + 1).Select(i => C(i, 1)).ToList() };

        Assert.IsType<BadRequestObjectResult>((await controller.PutProgress(huge, CancellationToken.None)).Result);
        var ok = Assert.IsType<OkObjectResult>((await controller.PutProgress(new KidsProgressDto { Levels = { L(1, 2, 0, 0, 1) } }, CancellationToken.None)).Result);
        Assert.Equal("L1:2/0/0@1 | ", Show((KidsProgressDto)ok.Value!));
        Assert.Equal(7, (await _db.KidsLevelProgresses.SingleAsync()).UserId);
    }

    [Fact]
    public async Task BuchLoeschen_RaeumtDenKinderFortschrittMitAb()
    {
        _db.Books.Add(new Book { Id = 5, FileName = "kids.pgn", Source = new BookSource() });
        await _db.SaveChangesAsync();
        await _service.SyncAsync(1, new KidsProgressDto { Courses = { C(5, 50, (10, 100)) } });

        await new BookAdminService(_db).DeleteBookAsync(5);

        Assert.Empty(_db.KidsCourseLines);
        Assert.Empty(_db.KidsCourseProgresses);
    }
}
