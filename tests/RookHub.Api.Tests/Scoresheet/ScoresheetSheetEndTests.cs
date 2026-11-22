using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Social;
using static RookHub.Api.Services.Scoresheet.ScoresheetScanRows;

namespace RookHub.Api.Tests.Scoresheet;

/// <summary>
/// „Partie hier beenden“ beim Speichern (2026-11-22): nach dem Kürzen kamen die nicht zugeordneten Einträge nach dem Neuladen
/// von „Korrigieren“ wieder („n Einträge ließen sich nicht zuordnen“), weil der Server die gespeicherte Liste beim Speichern
/// nie anpasste — und RookHub hängte sie beim nächsten Speichern als „Offen: …“ an den letzten Zug. Mit dem Signal
/// <c>sheetEnd</c> gelten die Einträge ab dem Schnitt als verworfen, und das bleibt auch beim nächsten Laden so.
/// </summary>
public class ScoresheetSheetEndTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ScoresheetScanService _service;

    public ScoresheetSheetEndTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _service = new ScoresheetScanService(_db, new NoVision(), TestServices.SavedGames(_db), new NotificationService(_db),
            NullLogger<ScoresheetScanService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private sealed class NoVision : IScoresheetVisionClient
    {
        public bool IsConfigured => false;
        public string Model => "none";
        public Task<ScoresheetVisionResult> ReadAsync(byte[] jpeg, string instructions, int maxTokens, CancellationToken ct = default,
            ScoresheetReadMode mode = ScoresheetReadMode.Full) => throw new InvalidOperationException("kein Modell im Test");
    }

    // 6 Einträge, die zur Partie gehören, dann 3 Kritzeleien nach dem echten Ende, die sich nicht zuordnen lassen.
    private static readonly string[] Written = { "e4", "e5", "Sf3", "Sc6", "Lb5", "a6", "Lxc6", "?", "Kxh9" };
    private static readonly (string San, string Uci)[] Moves =
        { ("e4", "e2e4"), ("e5", "e7e5"), ("Nf3", "g1f3"), ("Nc6", "b8c6"), ("Bb5", "f1b5"), ("a6", "a7a6") };

    private static string Transcription() => JsonSerializer.Serialize(new
    {
        notationLanguage = "de",
        moves = Written.Select((w, i) => new { moveNumber = i / 2 + 1, color = i % 2 == 0 ? "w" : "b", written = w, san = "" }),
    });

    private static List<ScoresheetPly> Plies(int count) => Moves.Take(count)
        .Select((m, i) => new ScoresheetPly { W = i, Written = Written[i], San = m.San, Uci = m.Uci, Match = "written" }).ToList();

    /// <summary>Wie nach dem Einlesen: 6 Halbzüge, die letzten 3 Einträge offen.</summary>
    private static string Resolution() => JsonSerializer.Serialize(new StoredResolution
    {
        Language = "de", Plies = Plies(6), Unresolved = new() { "Lxc6", "?", "Kxh9" }, UnresolvedFrom = 6,
    }, Json);

    private async Task<(int UserId, int GameId)> OwnScanAsync()
    {
        var u = new AppUser { Username = "reader", Email = "reader@test.com", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        _db.ScoresheetScans.Add(new ScoresheetScan
        {
            UserId = u.Id, SavedGameId = 77, ContentType = "image/jpeg", FileName = "a.jpg", NotationLanguage = "de", Status = ScoresheetScanStatus.Done,
            TranscriptionJson = Transcription(), ResolutionJson = Resolution(),
        });
        await _db.SaveChangesAsync();
        return (u.Id, 77);
    }

    private async Task<int> ClubArchiveAsync()
    {
        _db.ScoresheetScanArchives.Add(new ScoresheetScanArchive
        {
            ScoresheetScanId = 5, Page = 1, View = 0, LeagueClubGameId = 258, ContentType = "image/jpeg", NotationLanguage = "de",
            TranscriptionJson = Transcription(), ResolutionJson = Resolution(), Outcome = "saved",
            ArchivedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30),
        });
        await _db.SaveChangesAsync();
        return 258;
    }

    [Fact]
    public async Task SaveEditState_WithSheetEnd_DiscardsTheEntriesAfterTheCut_AlsoAfterReloading()
    {
        var (user, game) = await OwnScanAsync();
        Assert.Equal(3, (await _service.EditStateAsync(user, game))!.Unresolved.Count);

        // „Partie hier beenden“ vor 3. Bb5: 4 Halbzüge, ab Eintrag 4 gehört nichts mehr zur Partie
        await _service.SaveEditStateAsync(user, game, Plies(4), sheetEnd: 4);

        var state = (await _service.EditStateAsync(user, game))!;
        Assert.Equal(4, state.Plies.Count);
        Assert.Empty(state.Unresolved);
        Assert.Null(state.UnresolvedFrom);
        Assert.Equal(4, state.SheetEnd);
        Assert.Equal(Written, state.Written);                         // die Lesung selbst bleibt

        // ein späteres Speichern ohne Signal (nur ein Zug bestätigt) lässt das Ende stehen
        await _service.SaveEditStateAsync(user, game, Plies(4));
        state = (await _service.EditStateAsync(user, game))!;
        Assert.Equal((4, 0), (state.SheetEnd ?? -1, state.Unresolved.Count));
    }

    [Fact]
    public async Task SaveEditState_WithoutSheetEnd_KeepsTheOpenEntriesAsBefore()
    {
        var (user, game) = await OwnScanAsync();
        await _service.SaveEditStateAsync(user, game, Plies(6));

        var state = (await _service.EditStateAsync(user, game))!;
        Assert.Equal(new[] { "Lxc6", "?", "Kxh9" }, state.Unresolved);
        Assert.Equal(6, state.UnresolvedFrom);
        Assert.Null(state.SheetEnd);
    }

    /// <summary>Beendet MITTEN in den offenen Einträgen (am Ende der Zugliste, ein Eintrag gehört noch dazu): nur die danach
    /// fallen weg.</summary>
    [Fact]
    public async Task SaveEditState_SheetEndInsideTheOpenEntries_KeepsOnlyThoseBeforeIt()
    {
        var (user, game) = await OwnScanAsync();
        await _service.SaveEditStateAsync(user, game, Plies(6), sheetEnd: 7);

        var state = (await _service.EditStateAsync(user, game))!;
        Assert.Equal(new[] { "Lxc6" }, state.Unresolved);
        Assert.Equal(6, state.UnresolvedFrom);
        Assert.Equal(7, state.SheetEnd);
    }

    [Fact]
    public async Task SaveClubEditState_WithSheetEnd_KorrigierenShowsNoOpenEntriesAfterReloading()
    {
        var club = await ClubArchiveAsync();
        Assert.Equal(3, (await _service.ClubEditStateAsync(club))!.Unresolved.Count);

        await _service.SaveClubEditStateAsync(club, Plies(4), "1. e4 e5 2. Nf3 Nc6 *", sheetEnd: 4);

        var state = (await _service.ClubEditStateAsync(club))!;
        Assert.Equal(4, state.Plies.Count);
        Assert.Empty(state.Unresolved);
        Assert.Null(state.UnresolvedFrom);
        Assert.Equal(4, state.SheetEnd);
        var row = await _db.ScoresheetScanArchives.SingleAsync();
        Assert.Equal("1. e4 e5 2. Nf3 Nc6 *", row.FinalPgn);
    }

    /// <summary>„Partie hier beenden“ am Ende der Zugliste (die Züge bleiben, nur die übrigen Einträge fallen weg) — auch ohne
    /// neue Halbzüge im Rumpf wird das Ende abgelegt.</summary>
    [Fact]
    public async Task SaveClubEditState_SheetEndWithoutPlies_StillDiscardsTheRest()
    {
        var club = await ClubArchiveAsync();
        await _service.SaveClubEditStateAsync(club, null, "pgn", sheetEnd: 6);

        var state = (await _service.ClubEditStateAsync(club))!;
        Assert.Equal(6, state.Plies.Count);
        Assert.Empty(state.Unresolved);
        Assert.Equal(6, state.SheetEnd);
    }

    [Fact]
    public async Task SaveClubEditState_WithoutPliesAndSheetEnd_OnlyTheFinalPgnChanges()
    {
        var club = await ClubArchiveAsync();
        await _service.SaveClubEditStateAsync(club, null, "1. e4 e5 *");

        var state = (await _service.ClubEditStateAsync(club))!;
        Assert.Equal((6, 3), (state.Plies.Count, state.Unresolved.Count));
        Assert.Null(state.SheetEnd);
    }

    [Theory]
    [InlineData(null, 3, 6)]
    [InlineData(9, 3, 6)]
    [InlineData(8, 2, 6)]
    [InlineData(6, 0, null)]
    [InlineData(2, 0, null)]
    [InlineData(-1, 3, 6)]
    public void EndSheetAt_TrimsTheOpenEntries(int? end, int left, int? from)
    {
        var s = JsonSerializer.Deserialize<StoredResolution>(Resolution(), Json)!;
        s.EndSheetAt(end);
        Assert.Equal((left, from), (s.Unresolved.Count, s.UnresolvedFrom));
    }
}
