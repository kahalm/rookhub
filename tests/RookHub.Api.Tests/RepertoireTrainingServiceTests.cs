using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class RepertoireTrainingServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RepertoireTrainingService _service;

    public RepertoireTrainingServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new RepertoireTrainingService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync(string username = "u")
    {
        var user = new AppUser { Username = username, Email = $"{username}@x.de", PasswordHash = "h", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private async Task<int> CreateRepertoireAsync(int userId)
    {
        var r = new Repertoire { UserId = userId, Name = "Französisch" };
        _db.Repertoires.Add(r);
        await _db.SaveChangesAsync();
        return r.Id;
    }

    private static LineReviewRequest Rev(string key, bool correct) => new() { LineKey = key, Correct = correct };

    // ===== Scheduling (pure) =====

    [Fact]
    public void ScheduleLevel_DefaultLadder_NewCorrect_GoesToLevel1_Due4h()
    {
        var hours = RepertoireTrainingService.DefaultLevels.Select(RepertoireTrainingService.HoursOf).ToArray();
        var card = new RepertoireCardState();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        RepertoireTrainingService.ScheduleLevel(card, correct: true, hours, now);

        Assert.Equal(1, card.Level);
        Assert.Equal(now.AddHours(4), card.DueAt);   // Stufe 1 = 4h
    }

    [Fact]
    public void ScheduleLevel_CorrectAdvancesOneLevel_AndCapsAt9()
    {
        var hours = RepertoireTrainingService.DefaultLevels.Select(RepertoireTrainingService.HoursOf).ToArray();
        var card = new RepertoireCardState();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (var i = 0; i < 12; i++) RepertoireTrainingService.ScheduleLevel(card, true, hours, now);

        Assert.Equal(9, card.Level);
        Assert.Equal(now.AddHours(24 * 30 * 6), card.DueAt);   // Stufe 9 = 6 Monate (30d)
    }

    [Fact]
    public void ScheduleLevel_Wrong_FallsBackToLevel1()
    {
        var hours = RepertoireTrainingService.DefaultLevels.Select(RepertoireTrainingService.HoursOf).ToArray();
        var card = new RepertoireCardState { Level = 5 };
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        RepertoireTrainingService.ScheduleLevel(card, correct: false, hours, now);

        Assert.Equal(1, card.Level);
        Assert.Equal(now.AddHours(4), card.DueAt);
        Assert.Equal(1, card.Lapses);
    }

    [Fact]
    public void HoursOf_ConvertsUnits()
    {
        Assert.Equal(4, RepertoireTrainingService.HoursOf(new(4, "h")));
        Assert.Equal(60, RepertoireTrainingService.HoursOf(new(2.5, "d")));
        Assert.Equal(420, RepertoireTrainingService.HoursOf(new(2.5, "w")));
        Assert.Equal(1080, RepertoireTrainingService.HoursOf(new(1.5, "mo")));
    }

    // ===== ReviewLine =====

    [Fact]
    public async Task ReviewLine_ForeignRepertoire_ReturnsNull()
    {
        var owner = await CreateUserAsync("owner");
        var other = await CreateUserAsync("other");
        var repId = await CreateRepertoireAsync(owner);
        Assert.Null(await _service.ReviewLineAsync(other, repId, Rev("L1", true)));
    }

    [Fact]
    public async Task ReviewLine_CreatesState_Correct_Level1()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);

        var dto = await _service.ReviewLineAsync(user, repId, Rev("lineA", true));

        Assert.NotNull(dto);
        Assert.Equal("lineA", dto!.LineKey);
        Assert.Equal(1, dto.Level);
        Assert.Equal(1, dto.Reps);
    }

    [Fact]
    public async Task ReviewLine_WrongAfterProgress_ResetsToLevel1()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);

        await _service.ReviewLineAsync(user, repId, Rev("lineA", true));   // L1
        await _service.ReviewLineAsync(user, repId, Rev("lineA", true));   // L2
        var dto = await _service.ReviewLineAsync(user, repId, Rev("lineA", false));   // wrong -> L1

        Assert.Equal(1, dto!.Level);
    }

    [Fact]
    public async Task GetLineStates_ReturnsPersistedStates()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);
        await _service.ReviewLineAsync(user, repId, Rev("a", true));
        await _service.ReviewLineAsync(user, repId, Rev("b", false));

        var states = await _service.GetLineStatesAsync(user, repId);
        Assert.NotNull(states);
        Assert.Equal(2, states!.Count);
    }

    // ===== Config =====

    [Fact]
    public async Task GetConfig_Default_WhenNothingSet()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);

        var cfg = await _service.GetConfigAsync(user, repId);
        Assert.NotNull(cfg);
        Assert.Equal("default", cfg!.Source);
        Assert.Equal(9, cfg.Effective.Count);
        Assert.Null(cfg.Repertoire);
    }

    [Fact]
    public async Task SetUserConfig_ThenEffectiveSourceIsUser()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);
        var levels = RepertoireTrainingService.DefaultLevels.Select(l => new SrLevelDto(l.Value, l.Unit)).ToList();
        levels[0] = new SrLevelDto(6, "h");

        Assert.True(await _service.SetUserConfigAsync(user, levels));
        var cfg = await _service.GetConfigAsync(user, repId);
        Assert.Equal("user", cfg!.Source);
        Assert.Equal(6, cfg.Effective[0].Value);
    }

    [Fact]
    public async Task SetRepertoireConfig_Overrides_ThenClearFallsBack()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);
        var levels = RepertoireTrainingService.DefaultLevels.Select(l => new SrLevelDto(l.Value, l.Unit)).ToList();
        levels[0] = new SrLevelDto(2, "h");

        Assert.True((await _service.SetRepertoireConfigAsync(user, repId, levels))!.Value);
        var cfg = await _service.GetConfigAsync(user, repId);
        Assert.Equal("repertoire", cfg!.Source);
        Assert.Equal(2, cfg.Effective[0].Value);

        // Override löschen → zurück auf Default
        Assert.True((await _service.SetRepertoireConfigAsync(user, repId, null))!.Value);
        cfg = await _service.GetConfigAsync(user, repId);
        Assert.Equal("default", cfg!.Source);
    }

    [Fact]
    public async Task SetUserConfig_InvalidLevelCount_Rejected()
    {
        var user = await CreateUserAsync();
        var bad = new List<SrLevelDto> { new(4, "h"), new(10, "h") };   // nur 2 Stufen
        Assert.False(await _service.SetUserConfigAsync(user, bad));
    }

    [Fact]
    public async Task RepertoireConfig_AffectsReviewDue()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);
        var levels = RepertoireTrainingService.DefaultLevels.Select(l => new SrLevelDto(l.Value, l.Unit)).ToList();
        levels[0] = new SrLevelDto(1, "h");   // Stufe 1 = 1h statt 4h
        await _service.SetRepertoireConfigAsync(user, repId, levels);

        var before = DateTime.UtcNow;
        var dto = await _service.ReviewLineAsync(user, repId, Rev("x", true));
        Assert.Equal(1, dto!.Level);
        Assert.InRange(dto.DueAt, before.AddMinutes(59), before.AddMinutes(61));
    }

    // ===== Pool / Pause / Make-due =====

    [Fact]
    public async Task Promote_CreatesInPoolState_DueNow()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);

        var before = DateTime.UtcNow;
        var n = await _service.PromoteAsync(user, repId, new() { "l1", "l2" });
        Assert.Equal(2, n);

        var states = (await _service.GetLineStatesAsync(user, repId))!;
        Assert.Equal(2, states.Count);
        Assert.All(states, s => Assert.True(s.InPool));
        Assert.All(states, s => Assert.False(s.Paused));
        Assert.All(states, s => Assert.InRange(s.DueAt, before.AddSeconds(-2), DateTime.UtcNow.AddSeconds(2)));
    }

    [Fact]
    public async Task Pause_MarksPaused_WithoutPuttingInPool()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);

        await _service.SetPausedAsync(user, repId, new() { "l1" }, paused: true);
        var s = (await _service.GetLineStatesAsync(user, repId))!.Single();
        Assert.True(s.Paused);
        Assert.False(s.InPool);   // Pausieren allein nimmt nicht in den Pool auf

        await _service.SetPausedAsync(user, repId, new() { "l1" }, paused: false);
        s = (await _service.GetLineStatesAsync(user, repId))!.Single();
        Assert.False(s.Paused);
    }

    [Fact]
    public async Task MakeDue_OnlyAffectsInPoolLines_AndUnpauses()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);
        await _service.PromoteAsync(user, repId, new() { "inpool" });
        await _service.SetPausedAsync(user, repId, new() { "inpool" }, paused: true);
        await _service.SetPausedAsync(user, repId, new() { "onlypaused" }, paused: true);   // nie im Pool

        var n = await _service.MakeDueAsync(user, repId, new());   // ganzer Kurs
        Assert.Equal(1, n);   // nur die InPool-Linie

        var states = (await _service.GetLineStatesAsync(user, repId))!;
        Assert.False(states.Single(s => s.LineKey == "inpool").Paused);        // entpausiert
        Assert.True(states.Single(s => s.LineKey == "onlypaused").Paused);     // unberührt
    }

    [Fact]
    public async Task Reset_ClearsStates()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);
        await _service.ReviewLineAsync(user, repId, Rev("a", true));
        await _service.ReviewLineAsync(user, repId, Rev("b", true));

        var deleted = await _service.ResetAsync(user, repId);
        Assert.Equal(2, deleted);
        Assert.Empty((await _service.GetLineStatesAsync(user, repId))!);
    }
}
