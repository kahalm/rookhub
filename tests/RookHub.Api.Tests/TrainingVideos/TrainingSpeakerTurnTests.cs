using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.DTOs.TrainingVideos;
using RookHub.Api.Models;
using RookHub.Api.Services.TrainingVideos;

namespace RookHub.Api.Tests.TrainingVideos;

/// <summary>Sprecherwechsel („Nächster Sprecher“ am Player): Import in beide Teile, Ordner-Import aus <c>sprecher/</c>,
/// Aufräumen, Ausgabe an Besitzer und Gast.</summary>
public sealed class TrainingSpeakerTurnTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly string _root;
    private readonly TrainingVideoImporter _importer;
    private readonly TrainingVideoService _service;
    private readonly string _fixture;

    public TrainingSpeakerTurnTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _root = Path.Combine(Path.GetTempPath(), "rh-tvsp-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        var options = Options.Create(new TrainingVideoOptions { VideoRoot = _root, DataRoot = _root });
        _importer = new TrainingVideoImporter(_db, options);
        _service = new TrainingVideoService(_db, options);
        _fixture = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "TrainingVideos", "2026-10-27.json"));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, true); } catch { /* egal */ }
    }

    private async Task<AppUser> UserAsync(string name)
    {
        var u = new AppUser { Username = name, PasswordHash = "x", CreatedAt = DateTime.UtcNow };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    private string WithTurns(string turnsJson)
    {
        var node = JsonNode.Parse(_fixture)!.AsObject();
        node["speakerTurns"] = JsonNode.Parse(turnsJson);
        return node.ToJsonString();
    }

    [Fact]
    public void Normalize_SortsDedupesMergesAndSkipsBad()
    {
        var warnings = new List<string>();
        var turns = TrainingSpeakerTurns.Normalize(new List<TrainingSpeakerTurnFile>
        {
            new() { T = 40, Sp = "Patrik" },
            new() { T = 0, Sp = "sasa" },
            new() { T = 12.6, Sp = "patrik" },
            new() { T = 20, Sp = "patrik" },          // gleicher Sprecher hintereinander → kein Wechsel
            new() { T = -1, Sp = "sasa" },            // ungültig
            new() { T = 50, Sp = "<script>" },        // ungültig
            new() { T = 60, Sp = "sasa" },
        }, warnings);

        Assert.Equal(new[] { new TrainingSpeakerTurnDto(0, "sasa"), new(13, "patrik"), new(60, "sasa") }, turns);
        Assert.Contains(warnings, w => w.Contains("2 Sprecherwechsel unlesbar"));
        Assert.Null(TrainingSpeakerTurns.Normalize(null, warnings));
        Assert.Empty(TrainingSpeakerTurns.Parse("kaputt"));
        Assert.Empty(TrainingSpeakerTurns.Parse(null));
    }

    [Fact]
    public async Task Import_StoresTurnsInEveryPart_AndBothViewsReturnThem()
    {
        var u = await UserAsync("patrik");
        var report = await _importer.ImportAsync(u.Id, WithTurns("""[{"t":0,"sp":"patrik"},{"t":33,"sp":"sasa"},{"t":90,"sp":"patrik"}]"""), false, default);

        Assert.True(report.Ok);
        Assert.Equal(3, report.SpeakerTurns);
        var lessons = await _db.TrainingLessons.ToListAsync();
        Assert.Equal(2, lessons.Count);   // privat + allgemein, dasselbe Video
        foreach (var l in lessons)
        {
            var dto = await _service.GetAsync(u.Id, false, l.Id, default);
            Assert.Equal(new[] { 0, 33, 90 }, dto.SpeakerTurns!.Select(t => t.T));
            Assert.Equal("sasa", dto.SpeakerTurns![1].Sp);
            var guest = await _service.GetForGuestAsync(l.Id, default);
            Assert.Equal(3, guest!.SpeakerTurns!.Count);
        }
    }

    [Fact]
    public async Task Reimport_WithoutTurnsKeeps_EmptyListClears()
    {
        var u = await UserAsync("patrik");
        await _importer.ImportAsync(u.Id, WithTurns("""[{"t":0,"sp":"patrik"},{"t":33,"sp":"sasa"}]"""), false, default);
        _db.ChangeTracker.Clear();

        var keep = await _importer.ImportAsync(u.Id, _fixture, false, default);
        Assert.Null(keep.SpeakerTurns);
        _db.ChangeTracker.Clear();
        Assert.All(await _db.TrainingLessons.ToListAsync(), l => Assert.NotNull(l.SpeakerTurns));

        await _importer.ImportAsync(u.Id, WithTurns("[]"), false, default);
        _db.ChangeTracker.Clear();
        Assert.All(await _db.TrainingLessons.ToListAsync(), l => Assert.Null(l.SpeakerTurns));
        var id = (await _db.TrainingLessons.FirstAsync()).Id;
        Assert.Empty((await _service.GetAsync(u.Id, false, id, default)).SpeakerTurns!);
    }

    [Fact]
    public async Task ImportFolder_ReadsSpeakerSubfolder_AndIgnoresItAsLesson()
    {
        var u = await UserAsync("patrik");
        File.WriteAllText(Path.Combine(_root, "2026-10-27.json"), _fixture);
        Directory.CreateDirectory(Path.Combine(_root, "sprecher"));
        File.WriteAllText(Path.Combine(_root, "sprecher", "2026-10-27.json"),
            """{"date":"2026-10-27","minTurnSec":4,"turns":[{"t":0,"sp":"sasa"},{"t":7,"sp":"patrik"}]}""");

        var reports = await _importer.ImportFolderAsync(u.Id, false, default);

        var r = Assert.Single(reports);   // die Sprecher-Datei ist keine Stunde
        Assert.Equal(2, r.SpeakerTurns);
        Assert.All(await _db.TrainingLessons.ToListAsync(), l => Assert.Contains("\"sp\":\"patrik\"", l.SpeakerTurns));
    }
}
