using RookHub.Api.Controllers;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Services.Og;

namespace RookHub.Api.Tests;

/// <summary>Tests für die Link-Vorschau (Open Graph): Pfad-Parsing + Brett-Bild-Rendering.</summary>
public class OgTests
{
    private const string StartFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

    [Theory]
    [InlineData("/g/abc123", "game", "abc123")]
    [InlineData("/t/12345", "tournament", "12345")]
    [InlineData("/puzzles/987", "puzzle", "987")]
    [InlineData("/puzzles/book/42", "book", "42")]
    [InlineData("/puzzles/daily/20260707", "daily", "20260707")]
    [InlineData("/puzzles/daily/today", "daily", "today")]
    [InlineData("/g/tok?utm=x", "game", "tok")]
    [InlineData("/tournaments/calendar/1447402", "calendar", "1447402")]
    [InlineData("/tournaments/calendar?t=f12345", "calendar", "f12345")]
    [InlineData("/tournaments/calendar/1447402?utm=x", "calendar", "1447402")]
    public void ParsePath_KnownRoutes_ReturnsKindAndId(string path, string kind, string id)
    {
        var result = OgMetaService.ParsePath(path);
        Assert.NotNull(result);
        Assert.Equal(kind, result!.Value.Kind);
        Assert.Equal(id, result.Value.Id);
    }

    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/puzzles")]          // Modus-Auswahl, keine konkrete Stellung
    [InlineData("/puzzles/endless")]  // keine feste Stellung
    [InlineData("/friends/5/stats")]
    [InlineData("/tournaments/calendar")]        // die Liste, kein einzelner Eintrag
    [InlineData("/tournaments/26")]
    [InlineData("")]
    [InlineData(null)]
    public void ParsePath_NonPreviewRoutes_ReturnsNull(string? path)
    {
        Assert.Null(OgMetaService.ParsePath(path));
    }

    [Fact]
    public void FenCharToFile_MapsColorAndRole()
    {
        Assert.Equal("wN", OgImageService.FenCharToFile('N'));
        Assert.Equal("bQ", OgImageService.FenCharToFile('q'));
        Assert.Equal("wP", OgImageService.FenCharToFile('P'));
        Assert.Null(OgImageService.FenCharToFile('1'));
    }

    [Fact]
    public void RenderBoard_ProducesValidPng()
    {
        var svc = new OgImageService(new TestLogger<OgImageService>());
        var png = svc.RenderBoard(StartFen);

        Assert.NotNull(png);
        Assert.True(png.Length > PngSignature.Length);
        Assert.Equal(PngSignature, png[..PngSignature.Length]);
    }

    // 0.745.0, Wunsch 2026-10-10: Karte des Trainingslinks mit Überschrift (eingebettete Schrift) und Fehler-Punkten in der Kurve
    [Fact]
    public void RenderBoard_TrainCard_DrawsTextAndMarks()
    {
        var svc = new OgImageService(new TestLogger<OgImageService>());
        var curve = new double?[] { 50, 55, 48, 70, 30, 35, 60, 20 };
        var plain = svc.RenderBoard(StartFen, curve: curve);
        var train = svc.RenderBoard(StartFen, curve: curve,
            train: new OgImageService.TrainCard("Verbessere dich", "Spiele deine Fehler neu", new[] { 4, 7 }, "Die Fehler von Hess, Max"));
        if (Environment.GetEnvironmentVariable("OG_DUMP") is { Length: > 0 } dump) File.WriteAllBytes(dump, train);
        Assert.Equal(PngSignature, train[..PngSignature.Length]);
        Assert.NotEqual(plain, train);
        using var bmp = SkiaSharp.SKBitmap.Decode(train);
        // In der Zeile der Überschrift steht helle Schrift (die Schrift ist eingebettet — ohne sie bliebe die Zeile leer)
        var bright = Enumerable.Range(640, 500).Count(x => bmp.GetPixel(x, 35 + 40).Red > 200);
        Assert.True(bright > 20, $"helle Pixel in der Überschrift: {bright}");
    }

    [Fact]
    public void TrainMarks_FehlerDerSeite_AbZehnProzentpunkten()
    {
        // Stellungen (Weiß-Sicht): 20, 30, 40, −300, Ende +400
        var evals = new GameEvalsDto { Status = "done", Total = 4, Plies = new()
        {
            new() { Ply = 0, Cp = 20 }, new() { Ply = 1, Cp = 30 }, new() { Ply = 2, Cp = 40 }, new() { Ply = 3, Cp = -300 },
        }, Final = new() { Cp = 400 } };
        Assert.Equal(new[] { 3 }, OgMetaService.TrainMarks(evals, white: true));    // 3. Halbzug (Weiß): +40 → −300
        Assert.Equal(new[] { 4 }, OgMetaService.TrainMarks(evals, white: false));   // 4. Halbzug (Schwarz): −300 → +400
    }

    [Fact]
    public void RenderBoard_DifferentPositionsDiffer_AndFlipChangesOutput()
    {
        var svc = new OgImageService(new TestLogger<OgImageService>());
        var empty = svc.RenderBoard("8/8/8/8/8/8/8/8 w - - 0 1");
        var start = svc.RenderBoard(StartFen);
        var startFlipped = svc.RenderBoard(StartFen, flip: true);

        Assert.NotEqual(empty, start);          // Figuren werden tatsächlich gezeichnet
        Assert.NotEqual(start, startFlipped);   // Perspektive wirkt sich aus
    }

    [Fact]
    public void RenderBoard_IsCached_ReturnsSameInstance()
    {
        var svc = new OgImageService(new TestLogger<OgImageService>());
        var a = svc.RenderBoard(StartFen);
        var b = svc.RenderBoard(StartFen);
        Assert.Same(a, b);
    }

    // ── Bewertungskurve im Vorschaubild (0.541.0) ────────────────────────────────────────────────

    private static GameEvalsDto Evals(string status = "done") => new()
    {
        Status = status, Total = 4, AnalysisId = 17, Refined = 3,
        Plies =
        {
            new GameEvalPlyDto { Ply = 0, Cp = 30 },
            new GameEvalPlyDto { Ply = 1, Cp = -250 },
            // Halbzug 2 nicht gerechnet → Lücke
            new GameEvalPlyDto { Ply = 3, Cp = 1500 },
        },
        Final = new GameEvalScoreDto { Mate = -2 },
    };

    [Fact]
    public void CurveOf_DoneAnalysis_LinearToTenPawns_MateAtTheEdge_GapsStayGaps()
    {
        Assert.Equal(new double?[] { 51.5, 37.5, null, 100, 0 }, OgMetaService.CurveOf(Evals()));
        Assert.Equal("17-3", OgMetaService.CurveVersion(Evals()));
    }

    [Theory]
    [InlineData("running")]
    [InlineData("pending")]
    [InlineData("none")]
    public void CurveOf_NoFinishedAnalysis_NoCurve_NoNewImageAddress(string status)
    {
        // Eine halbe Kurve sähe im geteilten Bild wie das Ende der Partie aus.
        Assert.Null(OgMetaService.CurveOf(Evals(status)));
        Assert.Null(OgMetaService.CurveVersion(Evals(status)));
    }

    [Fact]
    public void RenderBoard_WithCurve_IsAValidPng_DiffersFromTheBoardAlone_AndIsCachedPerCurve()
    {
        var svc = new OgImageService(new TestLogger<OgImageService>());
        var curve = OgMetaService.CurveOf(Evals());
        var plain = svc.RenderBoard(StartFen);
        var withCurve = svc.RenderBoard(StartFen, curve: curve);
        var otherCurve = svc.RenderBoard(StartFen, curve: new double?[] { 50, 90, 10 });

        Assert.Equal(PngSignature, withCurve[..PngSignature.Length]);
        Assert.NotEqual(plain, withCurve);
        Assert.NotEqual(withCurve, otherCurve);
        Assert.Same(withCurve, svc.RenderBoard(StartFen, curve: OgMetaService.CurveOf(Evals())));
    }

    [Fact]
    public void SmoothPath_GoesThroughEveryPoint_AndNeverOvershootsAnExtreme()
    {
        var points = new[] { new SkiaSharp.SKPoint(0, 50), new SkiaSharp.SKPoint(10, 10), new SkiaSharp.SKPoint(20, 10), new SkiaSharp.SKPoint(30, 90) };
        using var path = OgImageService.SmoothPath(points);

        Assert.Equal(points[^1], path.LastPoint);
        // Monoton: die Kurve bleibt im Band der Punkte (10..90), rundet aber zwischen ihnen.
        Assert.InRange(path.TightBounds.Top, 9.99f, 10.01f);
        Assert.InRange(path.TightBounds.Bottom, 89.99f, 90.01f);
    }

    // ── Zwei Seiten, zwei Shells (RookHub + turnier.oberschmid.homes) ──────────────────────────

    [Fact]
    public void ConfiguredBaseUrl_TurnierSite_PrefersTurnierBaseUrl()
    {
        // Ein /t/{id}-Link lebt auf der Turnierseite — og:url darf nicht auf RookHub zeigen,
        // wo es die Route seit der Trennung nicht mehr gibt.
        var url = OgController.ConfiguredBaseUrl("turnier", "https://rookhub.example", "https://turnier.example");
        Assert.Equal("https://turnier.example", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("rookhub")]
    public void ConfiguredBaseUrl_OtherSites_UseAppBaseUrl(string? site)
    {
        var url = OgController.ConfiguredBaseUrl(site, "https://rookhub.example", "https://turnier.example");
        Assert.Equal("https://rookhub.example", url);
    }

    [Fact]
    public void ConfiguredBaseUrl_TurnierWithoutOwnConfig_FallsBackToAppBaseUrl()
    {
        var url = OgController.ConfiguredBaseUrl("turnier", "https://rookhub.example", null);
        Assert.Equal("https://rookhub.example", url);
    }

    [Fact]
    public void BuildCandidates_ConfiguredUrlComesFirst_AndIsDeduped()
    {
        var candidates = OgIndexHtmlProvider.BuildCandidates("http://turnier:8080/", "http://turnier:8080", "http://rookhub-turnier:8080");
        Assert.Equal(new[] { "http://turnier:8080/index.html", "http://rookhub-turnier:8080/index.html" }, candidates);
    }

    [Fact]
    public void BuildCandidates_WithoutConfiguration_KeepsDefaults()
    {
        var candidates = OgIndexHtmlProvider.BuildCandidates(null, "http://frontend:8080", "http://rookhub-frontend:8080");
        Assert.Equal(new[] { "http://frontend:8080/index.html", "http://rookhub-frontend:8080/index.html" }, candidates);
    }

    // ----- Kalendereintrag der Turnierseite -----------------------------------

    private static TournamentDirectoryEntry CalendarEntry() => new()
    {
        PublicId = "1447402",
        ChessResultsId = "1447402",
        Name = "Innsbrucker Herbstopen 2026",
        StartDate = new DateOnly(2026, 10, 23),
        EndDate = new DateOnly(2026, 10, 25),
        LocationText = "Gasthof Sonne, Hauptstraße 1, 6020 Innsbruck",
        GeoPlaceName = "Innsbruck",
        Speed = TournamentSpeed.Standard,
        TimeControlText = "90 min + 30 s",
        Rounds = 7,
        PlayerCount = 84,
        Organizer = "SK Innsbruck",
    };

    /// <summary>Die Eckdaten in fester Reihenfolge, nur was bekannt ist.</summary>
    [Fact]
    public void CalendarDescription_ListsTheKeyFacts()
    {
        Assert.Equal(
            "23.–25.10.2026 · Innsbruck · Turnierschach (90 min + 30 s) · 7 Runden · 84 Teilnehmer · Veranstalter: SK Innsbruck",
            OgMetaService.CalendarDescription(CalendarEntry()));

        var sparse = new TournamentDirectoryEntry { PublicId = "1", Name = "X", Kind = TournamentKind.Team,
            LocationText = "Wien", Speed = TournamentSpeed.Rapid };
        Assert.Equal("Wien · Schnellschach · Mannschaftsturnier", OgMetaService.CalendarDescription(sparse));

        var removed = CalendarEntry();
        removed.RemovedAt = DateTime.UtcNow;
        Assert.StartsWith("Nicht mehr ausgeschrieben · 23.–25.10.2026", OgMetaService.CalendarDescription(removed));
    }

    [Theory]
    [InlineData("2026-09-27", "2026-09-27", "27.09.2026")]
    [InlineData("2026-09-27", "2026-09-29", "27.–29.09.2026")]
    [InlineData("2026-09-30", "2026-10-02", "30.09.–02.10.2026")]
    [InlineData("2026-12-30", "2027-01-02", "30.12.2026–02.01.2027")]
    [InlineData("2026-09-27", null, "27.09.2026")]
    public void DateRange_ShortGermanForms(string start, string? end, string expected)
    {
        Assert.Equal(expected, OgMetaService.DateRange(DateOnly.Parse(start), end is null ? null : DateOnly.Parse(end)));
    }

    /// <summary>
    /// Ein geteilter Kalender-Link (gemeldet 03.10.2026: …/tournaments/calendar/1447402) bekommt Titel, Eckdaten,
    /// Bild und die eigene Adresse — vorher zeigte das Vorschaufenster nur die allgemeine Seitenvorschau.
    /// </summary>
    [Fact]
    public async Task ResolvePage_CalendarEntry_HasNameAndKeyFacts()
    {
        using var db = new RookHub.Api.Data.AppDbContext(
            new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<RookHub.Api.Data.AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.TournamentDirectoryEntries.Add(CalendarEntry());
        await db.SaveChangesAsync();
        var meta = new OgMetaService(null!, null!, null!, null!, db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OgMetaService>.Instance);

        var page = await meta.ResolvePageAsync("/tournaments/calendar/1447402", "https://tournament.oberschmid.homes");

        Assert.NotNull(page);
        Assert.Equal("Innsbrucker Herbstopen 2026", page!.Title);
        Assert.Contains("23.–25.10.2026 · Innsbruck", page.Description);
        Assert.Equal("https://tournament.oberschmid.homes/tournaments/calendar/1447402", page.CanonicalUrl);
        Assert.Equal("https://tournament.oberschmid.homes/api/og/img/calendar/1447402.png", page.ImageUrl);
        Assert.NotNull(await meta.ResolveBoardAsync("calendar", "1447402"));

        Assert.Null(await meta.ResolvePageAsync("/tournaments/calendar/999", "https://x"));        // gibt es nicht
        Assert.Null(await meta.ResolvePageAsync("/tournaments/calendar/a%27b", "https://x"));       // keine Kennung
    }
}
