using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Repertoires;
using RookHub.Api.DTOs.Repertoires;

namespace RookHub.Api.Tests.Repertoires;

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
    public void ScheduleLevel_CorrectWithHint_StaysOnItsLevel_NoRepNoLapse_DueByItsInterval()
    {
        // 0.886.0: ein Tipp ist kein Fehler, aber die Karte rückt nicht vor
        var hours = RepertoireTrainingService.DefaultLevels.Select(RepertoireTrainingService.HoursOf).ToArray();
        var card = new RepertoireCardState { Level = 5, Reps = 4 };
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        RepertoireTrainingService.ScheduleLevel(card, correct: true, hours, now, hinted: true);

        Assert.Equal((5, 4, 0), (card.Level, card.Reps, card.Lapses));
        Assert.Equal(now.AddHours(hours[4]), card.DueAt);

        var fresh = new RepertoireCardState();                        // nie bewertet → Stufe 1
        RepertoireTrainingService.ScheduleLevel(fresh, correct: true, hours, now, hinted: true);
        Assert.Equal((1, 0), (fresh.Level, fresh.Reps));

        RepertoireTrainingService.ScheduleLevel(card, correct: false, hours, now, hinted: true);   // Fehler schlägt Tipp
        Assert.Equal((1, 1), (card.Level, card.Lapses));
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

    // ===== Reihenfolge der Linien im Trainer (je Nutzer und Repertoire, 0.996.0) =====

    [Fact]
    public async Task GetConfig_LineOrder_IsNull_WhenNothingChosen()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);

        // null = „nichts gewählt“: die Vorgabe wird NICHT gespeichert und nicht geliefert — das Frontend entscheidet
        // (Vorgabe bzw. alter Geräte-Schalter „Häufigste zuerst“).
        Assert.Null((await _service.GetConfigAsync(user, repId))!.LineOrder);
        Assert.Empty(_db.RepertoireTrainingPrefs);
    }

    [Fact]
    public async Task SetLineOrder_OwnRepertoire_IsStoredAndReturned_AndOverwritesInPlace()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);

        Assert.True(await _service.SetLineOrderAsync(user, repId, "mostTrained"));
        Assert.Equal("mostTrained", (await _service.GetConfigAsync(user, repId))!.LineOrder);

        Assert.True(await _service.SetLineOrderAsync(user, repId, "repertoire"));
        Assert.Equal("repertoire", (await _service.GetConfigAsync(user, repId))!.LineOrder);
        Assert.Single(_db.RepertoireTrainingPrefs);   // eine Zeile je (Nutzer, Repertoire), kein Verlauf
    }

    [Fact]
    public async Task SetLineOrder_IsPerRepertoire()
    {
        var user = await CreateUserAsync();
        var a = await CreateRepertoireAsync(user);
        var b = await CreateRepertoireAsync(user);

        Assert.True(await _service.SetLineOrderAsync(user, a, "random"));

        Assert.Equal("random", (await _service.GetConfigAsync(user, a))!.LineOrder);
        Assert.Null((await _service.GetConfigAsync(user, b))!.LineOrder);
    }

    [Fact]
    public async Task SetLineOrder_ForeignRepertoire_ReturnsNull_AndStoresNothing()
    {
        var owner = await CreateUserAsync("owner");
        var stranger = await CreateUserAsync("stranger");
        var repId = await CreateRepertoireAsync(owner);

        Assert.Null(await _service.SetLineOrderAsync(stranger, repId, "random"));
        Assert.Null(await _service.SetLineOrderAsync(stranger, 99_999, "random"));   // gibt es nicht
        Assert.Empty(_db.RepertoireTrainingPrefs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("byDate")]
    [InlineData("LeastTrained")]      // Groß-/Kleinschreibung zählt
    [InlineData(" leastTrained")]     // kein Trimmen
    [InlineData("leastTrained,random")]
    public async Task SetLineOrder_ValueOutsideTheList_ReturnsFalse_AndKeepsTheStoredOne(string? order)
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);
        Assert.True(await _service.SetLineOrderAsync(user, repId, "frequency"));

        Assert.False(await _service.SetLineOrderAsync(user, repId, order));

        Assert.Equal("frequency", (await _service.GetConfigAsync(user, repId))!.LineOrder);
    }

    [Fact]
    public async Task SetLineOrder_SharedRepertoire_RecipientHasOwnValue_OwnerKeepsHis()
    {
        var owner = await CreateUserAsync("owner");
        var friend = await CreateUserAsync("friend");
        var repId = await CreateRepertoireAsync(owner);
        _db.RepertoireShares.Add(new RepertoireShare { RepertoireId = repId, OwnerId = owner, RecipientId = friend });
        await _db.SaveChangesAsync();
        Assert.True(await _service.SetLineOrderAsync(owner, repId, "repertoire"));

        // Der Empfänger einer Freigabe trainiert mit eigenem Fortschritt — und mit eigener Reihenfolge.
        Assert.Null((await _service.GetConfigAsync(friend, repId))!.LineOrder);
        Assert.True(await _service.SetLineOrderAsync(friend, repId, "mostTrained"));

        Assert.Equal("mostTrained", (await _service.GetConfigAsync(friend, repId))!.LineOrder);
        Assert.Equal("repertoire", (await _service.GetConfigAsync(owner, repId))!.LineOrder);
        Assert.Equal(2, _db.RepertoireTrainingPrefs.Count());
    }

    [Fact]
    public async Task GetConfig_StoredValueOutsideTheList_IsReportedAsNothingChosen()
    {
        var user = await CreateUserAsync();
        var repId = await CreateRepertoireAsync(user);
        // z. B. ein Wert, den eine spätere Fassung gestrichen hat
        _db.RepertoireTrainingPrefs.Add(new RepertoireTrainingPref { UserId = user, RepertoireId = repId, LineOrder = "alphabetical" });
        await _db.SaveChangesAsync();

        Assert.Null((await _service.GetConfigAsync(user, repId))!.LineOrder);
    }

    // ===== „Nach dem Gegner“ (0.999.3): nur für ein Repertoire mit gespeichertem Gegner =====

    private const string OpponentJson = "{\"opponent\":\"prep:154602\",\"name\":\"Schett, Franz\",\"color\":\"w\",\"params\":{}}";

    private async Task<int> CreatePrepRepertoireAsync(int userId, string? opponentJson = OpponentJson)
    {
        var r = new Repertoire { UserId = userId, Name = "Prep: Schett, Franz 2026", OpponentJson = opponentJson };
        _db.Repertoires.Add(r);
        await _db.SaveChangesAsync();
        return r.Id;
    }

    [Fact]
    public async Task GetConfig_HasOpponent_OnlyForARepertoireWithAStoredOpponent()
    {
        var user = await CreateUserAsync();
        var plain = await CreateRepertoireAsync(user);
        var prep = await CreatePrepRepertoireAsync(user);
        var broken = await CreatePrepRepertoireAsync(user, "{\"opponent\":\"nix\"}");   // kein gültiger Schlüssel = kein Gegner

        Assert.False((await _service.GetConfigAsync(user, plain))!.HasOpponent);
        Assert.True((await _service.GetConfigAsync(user, prep))!.HasOpponent);
        Assert.False((await _service.GetConfigAsync(user, broken))!.HasOpponent);
    }

    [Fact]
    public async Task SetLineOrder_Opponent_WithStoredOpponent_IsStoredAndReturned()
    {
        var user = await CreateUserAsync();
        var prep = await CreatePrepRepertoireAsync(user);

        Assert.True(await _service.SetLineOrderAsync(user, prep, "opponent"));

        var cfg = (await _service.GetConfigAsync(user, prep))!;
        Assert.Equal("opponent", cfg.LineOrder);
        Assert.True(cfg.HasOpponent);
    }

    [Fact]
    public async Task SetLineOrder_Opponent_WithoutStoredOpponent_ReturnsFalse_AndKeepsTheStoredOne()
    {
        var user = await CreateUserAsync();
        var plain = await CreateRepertoireAsync(user);
        var broken = await CreatePrepRepertoireAsync(user, "kaputt");
        Assert.True(await _service.SetLineOrderAsync(user, plain, "random"));

        Assert.False(await _service.SetLineOrderAsync(user, plain, "opponent"));
        Assert.False(await _service.SetLineOrderAsync(user, broken, "opponent"));

        Assert.Equal("random", (await _service.GetConfigAsync(user, plain))!.LineOrder);
        Assert.Null((await _service.GetConfigAsync(user, broken))!.LineOrder);
    }

    [Fact]
    public async Task SetLineOrder_Opponent_SharedPrepRepertoire_RecipientMayChooseIt()
    {
        var owner = await CreateUserAsync("owner");
        var friend = await CreateUserAsync("friend");
        var prep = await CreatePrepRepertoireAsync(owner);
        _db.RepertoireShares.Add(new RepertoireShare { RepertoireId = prep, OwnerId = owner, RecipientId = friend });
        await _db.SaveChangesAsync();

        Assert.True(await _service.SetLineOrderAsync(friend, prep, "opponent"));

        Assert.Equal("opponent", (await _service.GetConfigAsync(friend, prep))!.LineOrder);
        Assert.Null((await _service.GetConfigAsync(owner, prep))!.LineOrder);
    }

    [Fact]
    public async Task GetConfig_StoredOpponentOrder_StaysEvenWhenTheOpponentIsGone_HasOpponentTellsTheClient()
    {
        var user = await CreateUserAsync();
        var prep = await CreatePrepRepertoireAsync(user);
        Assert.True(await _service.SetLineOrderAsync(user, prep, "opponent"));
        (await _db.Repertoires.SingleAsync(r => r.Id == prep)).OpponentJson = null;
        await _db.SaveChangesAsync();

        var cfg = (await _service.GetConfigAsync(user, prep))!;

        Assert.Equal("opponent", cfg.LineOrder);   // das Frontend fällt mit HasOpponent = false auf die Vorgabe
        Assert.False(cfg.HasOpponent);
    }

    [Theory]
    [InlineData("opponent", true, true)]
    [InlineData("opponent", false, false)]
    [InlineData("leastTrained", false, true)]
    [InlineData("frequency", true, true)]
    [InlineData("alphabetical", true, false)]
    [InlineData(null, true, false)]
    public void IsValidFor_OpponentNeedsAStoredOpponent(string? order, bool hasOpponent, bool expected)
        => Assert.Equal(expected, RepertoireLineOrders.IsValidFor(order, hasOpponent));

    // ===== Spiegel-Pin: erlaubte Reihenfolgen ====================================================
    // Dieselbe Liste steht ein zweites Mal im Frontend (features/repertoire/repertoire-line-order.ts, LINE_ORDERS;
    // festgenagelt in repertoire-line-order.spec.ts). BEIDE Seiten prüfen literale Werte, nicht die jeweils andere
    // Implementierung: kennt nur das Frontend einen Wert, scheitert das Speichern mit 400; kennt ihn nur der Server,
    // fällt das Frontend beim Lesen still auf die Vorgabe. Wer hier etwas ändert, ändert die Spec dort mit.
    [Fact]
    public void LineOrders_MatchTheFrontendMirror()
    {
        Assert.Equal(new[] { "leastTrained", "mostTrained", "random", "repertoire", "frequency", "opponent" }, RepertoireLineOrders.All);
        Assert.Equal("leastTrained", RepertoireLineOrders.Default);
        Assert.Equal("opponent", RepertoireLineOrders.Opponent);   // der eine Wert, der einen gespeicherten Gegner braucht
        Assert.All(RepertoireLineOrders.All, o => Assert.True(o.Length <= 20, $"„{o}“ passt nicht in RepertoireTrainingPref.LineOrder (20)"));
    }

    // ===== Spiegel-Pin: SR-Intervall-Vorgaben =====================================================
    // Dieselben neun Stufen stehen ein zweites Mal im Frontend (features/repertoire/repertoire-sr.util.ts,
    // DEFAULT_SR_LEVELS) — der Offline-Trainer bewertet damit ohne Server. Laufen die beiden auseinander,
    // plant eine offline bewertete Linie auf einen anderen Termin als dieselbe Linie online, und es faellt
    // niemandem auf. Der LineKey-Spiegel ist genauso festgenagelt (ChessableTrainedLineServiceTests):
    // BEIDE Seiten pruefen literale Werte, nicht die jeweils andere Implementierung.
    // Wer hier etwas aendert, aendert repertoire-sr.util.spec.ts mit.
    [Fact]
    public void DefaultLevels_MatchTheFrontendMirror()
    {
        var erwartet = new (double Value, string Unit)[]
        {
            (4, "h"), (10, "h"), (24, "h"),
            (2.5, "d"), (1, "w"), (2.5, "w"),
            (1.5, "mo"), (3, "mo"), (6, "mo"),
        };

        Assert.Equal(erwartet.Length, RepertoireTrainingService.DefaultLevels.Count);
        Assert.Equal(erwartet, RepertoireTrainingService.DefaultLevels.Select(l => (l.Value, l.Unit)).ToArray());
    }

    // Die Stufe bestimmt den Termin ueber den INDEX (Stufe 1 = Index 0) — zusammen mit dem Pin oben
    // haelt das die konkreten Abstaende fest, nicht nur die Liste.
    [Fact]
    public void DefaultLevels_FirstLevelIsFourHours_LastIsSixMonths()
    {
        var erste = RepertoireTrainingService.DefaultLevels[0];
        var letzte = RepertoireTrainingService.DefaultLevels[^1];
        Assert.Equal((4, "h"), (erste.Value, erste.Unit));
        Assert.Equal((6, "mo"), (letzte.Value, letzte.Unit));
    }
}
