using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RookHub.Api.Controllers.TrainingVideos;
using RookHub.Api.Data;
using RookHub.Api.DTOs.TrainingVideos;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.TrainingVideos;

namespace RookHub.Api.Tests.TrainingVideos;

/// <summary>Teilen-Links der Trainerstunden: nur der Besitzer legt an/listet/widerruft, der Gast sieht genau den Eintrag
/// des Links (ohne Notizen, Übungsstand, andere Teile, RookHub-Partien, Besitzernamen), abgelaufen/widerrufen = 410,
/// unbekannt = 404 — auch am Video.</summary>
public sealed class TrainingLessonShareTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _videoRoot;
    private readonly TrainingVideoOptions _options;
    private readonly TrainingVideoService _service;
    private readonly TrainingLessonShares _shares;
    private readonly string _fixture;

    public TrainingLessonShareTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _videoRoot = Path.Combine(Path.GetTempPath(), "rh-tvs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_videoRoot);
        _options = new TrainingVideoOptions { VideoRoot = _videoRoot };
        _service = new TrainingVideoService(_db, Options.Create(_options));
        _shares = new TrainingLessonShares(_db);
        _fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TrainingVideos", "2026-10-27.json"));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_videoRoot, true); } catch { /* egal */ }
    }

    private async Task<AppUser> UserAsync(string name, bool admin = false)
    {
        var u = new AppUser { Username = name, Email = name + "@test.invalid", PasswordHash = "x", IsAdmin = admin, CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    /// <summary>Konto mit eingelesener Stunde (privater + allgemeiner Teil), RookHub-Partie 4711, Video und Übungsstand.</summary>
    private async Task<(AppUser Owner, TrainingLesson Private, TrainingLesson General)> SetupAsync()
    {
        var u = await UserAsync("patrik");
        _db.SavedGames.Add(new SavedGame
        {
            Id = 4711, UserId = u.Id, Source = "otb", ShareToken = Guid.NewGuid().ToString("N"),
            Pgn = "[White \"Oberschmid, Patrik\"]\n[Black \"Gegner, Max\"]\n[Result \"1-0\"]\n\n1. e4 e6 2. d4 d5 3. Nd2 c5 4. Ngf3 Nc6 5. Bb5 cxd4 6. Nxd4 Bd7 1-0",
        });
        await _db.SaveChangesAsync();
        File.WriteAllText(Path.Combine(_videoRoot, "2026-10-27.mp4"), "0123456789abcdefghij");
        await new TrainingVideoImporter(_db, Options.Create(_options)).ImportAsync(u.Id, _fixture, dryRun: false, default);
        _db.ChangeTracker.Clear();
        var priv = await _db.TrainingLessons.Include(l => l.Segments).SingleAsync(l => l.Part == TrainingLessonParts.Private);
        var general = await _db.TrainingLessons.SingleAsync(l => l.Part == TrainingLessonParts.General);
        var seg = priv.Segments.OrderBy(s => s.Position).First();
        await _service.SetProgressAsync(u.Id, false, priv.Id, seg.Id, new TrainingProgressUpdateDto("wrong", 2), default);
        return (u, priv, general);
    }

    private TrainingVideosController Controller(int? userId, bool admin = false)
    {
        var c = new TrainingVideosController(_service, new TrainingVideoAccessTokens(new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()),
            null!, _db, _shares);
        var identity = userId is int id
            ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id.ToString()) }.Concat(admin ? new[] { new Claim(ClaimTypes.Role, "Admin") } : []), "test")
            : new ClaimsIdentity();
        c.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) } };
        return c;
    }

    [Fact]
    public async Task Create_Owner_LongRandomTokenWithDefaultSevenDays()
    {
        var (u, priv, _) = await SetupAsync();
        var a = await _shares.CreateAsync(u.Id, priv.Id, null);
        var b = await _shares.CreateAsync(u.Id, priv.Id, 30);

        Assert.NotNull(a);
        Assert.NotNull(b);
        Assert.Equal(43, a!.Token.Length);
        Assert.NotEqual(a.Token, b!.Token);
        Assert.InRange((a.ExpiresAt - a.CreatedAt).TotalDays, 6.99, 7.01);
        Assert.InRange((b.ExpiresAt - b.CreatedAt).TotalDays, 29.99, 30.01);
        // Mehrere Links je Eintrag, neueste zuerst.
        Assert.Equal(new[] { b.Id, a.Id }, (await _shares.ListAsync(u.Id, priv.Id))!.Select(s => s.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(365)]
    public async Task Create_OtherDays_Rejected(int days)
    {
        var (u, priv, _) = await SetupAsync();
        await Assert.ThrowsAsync<DomainValidationException>(() => _shares.CreateAsync(u.Id, priv.Id, days));
    }

    [Fact]
    public async Task Create_List_Revoke_ForeignLessonOrAdmin_NotFound()
    {
        var (u, priv, _) = await SetupAsync();
        var other = await UserAsync("fremd");
        var admin = await UserAsync("admin", admin: true);
        var share = (await _shares.CreateAsync(u.Id, priv.Id, 7))!;

        Assert.Null(await _shares.CreateAsync(other.Id, priv.Id, 7));
        Assert.Null(await _shares.CreateAsync(admin.Id, priv.Id, 7));    // auch Admins teilen keine fremden Stunden
        Assert.Null(await _shares.ListAsync(other.Id, priv.Id));
        Assert.False(await _shares.RevokeAsync(other.Id, priv.Id, share.Id));
        Assert.Null(await _shares.CreateAsync(u.Id, 999999, 7));
        // Link des einen Eintrags lässt sich nicht über den anderen widerrufen.
        var general = await _db.TrainingLessons.SingleAsync(l => l.Part == TrainingLessonParts.General);
        Assert.False(await _shares.RevokeAsync(u.Id, general.Id, share.Id));
        Assert.Equal(TrainingShareState.Valid, (await _shares.ResolveAsync(share.Token)).State);
    }

    [Fact]
    public async Task Resolve_Valid_Expired_Revoked_Unknown()
    {
        var (u, priv, _) = await SetupAsync();
        var ok = (await _shares.CreateAsync(u.Id, priv.Id, 1))!;
        var expired = (await _shares.CreateAsync(u.Id, priv.Id, 1))!;
        var revoked = (await _shares.CreateAsync(u.Id, priv.Id, 7))!;
        var row = await _db.TrainingLessonShares.SingleAsync(s => s.Id == expired.Id);
        row.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        await _db.SaveChangesAsync();
        Assert.True(await _shares.RevokeAsync(u.Id, priv.Id, revoked.Id));
        Assert.False(await _shares.RevokeAsync(u.Id, priv.Id, revoked.Id));   // schon widerrufen

        Assert.Equal((TrainingShareState.Valid, priv.Id), await _shares.ResolveAsync(ok.Token));
        Assert.Equal(TrainingShareState.Gone, (await _shares.ResolveAsync(expired.Token)).State);
        Assert.Equal(TrainingShareState.Gone, (await _shares.ResolveAsync(revoked.Token)).State);
        Assert.Equal(TrainingShareState.Unknown, (await _shares.ResolveAsync("A" + ok.Token[1..] + "x")).State);
        Assert.Equal(TrainingShareState.Unknown, (await _shares.ResolveAsync("../../etc/passwd")).State);
        Assert.Equal(TrainingShareState.Unknown, (await _shares.ResolveAsync(null)).State);
        // Liste zeigt nur den aktiven.
        Assert.Equal(new[] { ok.Id }, (await _shares.ListAsync(u.Id, priv.Id))!.Select(s => s.Id));
    }

    [Fact]
    public async Task Create_TooManyActive_Rejected()
    {
        var (u, priv, _) = await SetupAsync();
        for (var i = 0; i < TrainingLessonShares.MaxActivePerLesson; i++) await _shares.CreateAsync(u.Id, priv.Id, 1);
        await Assert.ThrowsAsync<DomainValidationException>(() => _shares.CreateAsync(u.Id, priv.Id, 1));
    }

    [Fact]
    public async Task GuestView_NoNotesProgressOtherPartsGameIdsOrOwner()
    {
        var (u, priv, general) = await SetupAsync();
        var share = (await _shares.CreateAsync(u.Id, priv.Id, 7))!;

        var result = await Controller(null).Shared(share.Token, default);
        var dto = Assert.IsType<TrainingLessonDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(priv.Id, dto.Id);
        Assert.Equal("private", dto.Part);
        Assert.True(dto.HasVideo);
        Assert.Null(dto.OwnerName);
        Assert.Empty(dto.OtherParts!);                                   // kein Weg zum allgemeinen Teil
        Assert.NotEmpty(dto.Segments);
        Assert.All(dto.Segments, s => Assert.Null(s.Progress));         // Übungsstand des Besitzers bleibt privat
        Assert.Contains(dto.Segments, s => s.Comment != null);
        Assert.NotEmpty(dto.Games);
        Assert.All(dto.Games, g => { Assert.Null(g.RookhubGameId); Assert.Null(g.MatchConfidence); });
        // Aufgabe im Nachspielen gibt es trotzdem (aus der RookHub-Partie gelesen, nur die Stellung).
        Assert.Contains(dto.Segments, s => s.TaskMove != null);
        // Notizen und Konto tauchen nirgends auf.
        var json = JsonSerializer.Serialize(dto);
        var notes = await _db.TrainingNotes.Where(n => n.LessonId == priv.Id).Select(n => n.Text).ToListAsync();
        Assert.NotEmpty(notes);
        Assert.All(notes, n => Assert.DoesNotContain(n, json));
        Assert.DoesNotContain("4711", json);
        Assert.DoesNotContain(u.Email!, json);
        Assert.DoesNotContain("\"" + general.Id + "\"", json);
    }

    [Fact]
    public async Task GuestView_OwnerViewUnchanged()
    {
        var (u, priv, general) = await SetupAsync();
        var own = await _service.GetAsync(u.Id, false, priv.Id, default);
        Assert.Equal(general.Id, Assert.Single(own.OtherParts!).Id);
        Assert.Contains(own.Games, g => g.RookhubGameId == 4711);
        Assert.Contains(own.Segments, s => s.Progress?.Status == "wrong");
    }

    [Fact]
    public async Task GuestEndpoints_ExpiredRevoked_Gone_Unknown_NotFound()
    {
        var (u, priv, _) = await SetupAsync();
        var expired = (await _shares.CreateAsync(u.Id, priv.Id, 1))!;
        (await _db.TrainingLessonShares.SingleAsync(s => s.Id == expired.Id)).ExpiresAt = DateTime.UtcNow.AddMinutes(-1);
        await _db.SaveChangesAsync();
        var revoked = (await _shares.CreateAsync(u.Id, priv.Id, 7))!;
        await _shares.RevokeAsync(u.Id, priv.Id, revoked.Id);

        foreach (var token in new[] { expired.Token, revoked.Token })
        {
            Assert.Equal(StatusCodes.Status410Gone, Assert.IsType<ObjectResult>((await Controller(null).Shared(token, default)).Result).StatusCode);
            Assert.Equal(StatusCodes.Status410Gone, Assert.IsType<ObjectResult>(await Controller(null).SharedVideo(token, default)).StatusCode);
        }
        Assert.IsType<NotFoundResult>((await Controller(null).Shared("x" + revoked.Token[1..], default)).Result);
        Assert.IsType<NotFoundResult>(await Controller(null).SharedVideo("nope-nope-nope-nope-nope", default));
    }

    [Fact]
    public async Task GuestVideo_ValidToken_RangeFileOfThatLessonOnly()
    {
        var (u, priv, _) = await SetupAsync();
        var share = (await _shares.CreateAsync(u.Id, priv.Id, 7))!;

        var file = Assert.IsType<PhysicalFileResult>(await Controller(null).SharedVideo(share.Token, default));
        Assert.True(file.EnableRangeProcessing);
        Assert.Equal("video/mp4", file.ContentType);
        Assert.Equal(Path.GetFullPath(Path.Combine(_videoRoot, "2026-10-27.mp4")), file.FileName);
    }

    [Theory]
    [InlineData("../2026-10-27.mp4")]
    [InlineData("..")]
    [InlineData("/etc/passwd")]
    [InlineData("sub/2026-10-27.mp4")]
    public async Task GuestVideo_ManipulatedVideoFile_NotFound(string videoFile)
    {
        var (u, priv, _) = await SetupAsync();
        var share = (await _shares.CreateAsync(u.Id, priv.Id, 7))!;
        (await _db.TrainingLessons.SingleAsync(l => l.Id == priv.Id)).VideoFile = videoFile;
        await _db.SaveChangesAsync();

        Assert.IsType<NotFoundResult>(await Controller(null).SharedVideo(share.Token, default));
    }

    [Fact]
    public async Task Controller_OwnerEndpoints_ForeignNotFound()
    {
        var (u, priv, _) = await SetupAsync();
        var other = await UserAsync("fremd");

        var created = Assert.IsType<TrainingShareDto>(Assert.IsType<OkObjectResult>((await Controller(u.Id).CreateShare(priv.Id, new TrainingShareCreateDto(30), default)).Result).Value);
        Assert.IsType<NotFoundResult>((await Controller(other.Id).CreateShare(priv.Id, new TrainingShareCreateDto(7), default)).Result);
        Assert.IsType<NotFoundResult>((await Controller(other.Id).Shares(priv.Id, default)).Result);
        Assert.IsType<NotFoundResult>(await Controller(other.Id).RevokeShare(priv.Id, created.Id, default));
        Assert.IsType<NoContentResult>(await Controller(u.Id).RevokeShare(priv.Id, created.Id, default));
        Assert.Empty(Assert.IsType<List<TrainingShareDto>>(Assert.IsType<OkObjectResult>((await Controller(u.Id).Shares(priv.Id, default)).Result).Value));
    }

    [Fact]
    public async Task Reimport_KeepsLinksOfKeptLesson()
    {
        var (u, priv, _) = await SetupAsync();
        var share = (await _shares.CreateAsync(u.Id, priv.Id, 7))!;
        _db.ChangeTracker.Clear();
        await new TrainingVideoImporter(_db, Options.Create(_options)).ImportAsync(u.Id, _fixture, dryRun: false, default);

        Assert.Equal((TrainingShareState.Valid, priv.Id), await _shares.ResolveAsync(share.Token));
    }
}
