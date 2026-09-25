using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;
using SkiaSharp;

namespace RookHub.Api.Tests;

public class ScoresheetScanServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeVision _vision = new();
    private readonly SavedGameService _games;
    private ScoresheetScanService _service;

    public ScoresheetScanServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _games = TestServices.SavedGames(_db);
        _service = new ScoresheetScanService(_db, _vision, _games, new NotificationService(_db),
            NullLogger<ScoresheetScanService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Dienst mit eigenen Budgets (Dollar) — die Vorgaben sind für Tests zu großzügig.</summary>
    private void WithBudgets(decimal userDaily, decimal userMonthly = 100m, decimal globalDaily = 1000m)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Scoresheet:UserDailyUsd"] = userDaily.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Scoresheet:UserMonthlyUsd"] = userMonthly.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Scoresheet:GlobalDailyUsd"] = globalDaily.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }).Build();
        _service = new ScoresheetScanService(_db, _vision, _games, new NotificationService(_db),
            NullLogger<ScoresheetScanService>.Instance, config);
    }

    /// <summary>Dienst MIT Nachdenken (<c>Scoresheet:Thinking=true</c>) — die Vorgabe ist seit 0.533.2 „nur abschreiben".</summary>
    private void WithThinking()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Scoresheet:Thinking"] = "true",
        }).Build();
        _service = new ScoresheetScanService(_db, _vision, _games, new NotificationService(_db),
            NullLogger<ScoresheetScanService>.Instance, config);
    }

    /// <summary>Liefert der Reihe nach die hinterlegten Antworten und merkt sich die Aufträge.</summary>
    private sealed class FakeVision : IScoresheetVisionClient
    {
        public bool IsConfigured { get; set; } = true;
        public string Model => "fake-model";
        public Queue<ScoresheetVisionResult> Answers { get; } = new();
        public List<string> Instructions { get; } = new();

        public List<int> MaxTokens { get; } = new();

        public List<ScoresheetReadMode> Modes { get; } = new();

        /// <summary>Hängen, bis abgebrochen wird — ein Aufruf, der den Laufzeit-Deckel reißt.</summary>
        public bool Hang { get; set; }

        public async Task<ScoresheetVisionResult> ReadAsync(byte[] jpeg, string instructions, int maxTokens,
            CancellationToken ct = default, ScoresheetReadMode mode = ScoresheetReadMode.Full)
        {
            Instructions.Add(instructions);
            MaxTokens.Add(maxTokens);
            Modes.Add(mode);
            if (Hang) await Task.Delay(Timeout.Infinite, ct);
            return Answers.Count > 0 ? Answers.Dequeue() : new ScoresheetVisionResult(null, "failed");
        }
    }

    private static byte[] Jpeg()
    {
        using var bmp = new SKBitmap(40, 30);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.White);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Jpeg, 80);
        return data.ToArray();
    }

    private async Task<AppUser> UserAsync(string name = "reader")
    {
        var u = new AppUser { Username = name, Email = $"{name}@test.com", PasswordHash = "x" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u;
    }

    private static readonly string[] Written =
    {
        "Sf3", "d5", "g3", "Sf6", "Lg2", "e6", "0-0", "Le7", "b3", "0-0", "Lb2", "c5", "d4", "Sc6", "Sd2", "b5",
        "dxc5", "Lxc5", "c4", "bxc4", "bxc4", "Le7", "Tc1", "Db6", "Lxf6", "Lxf6", "cxd5", "exd5", "e3", "Lf5",
        "Sb3", "Tad8", "Sd4", "Sxd4", "Sxd4", "Le4", "Dd2", "Tc8", "h4", "h5", "Lh3", "Tc4", "Txc4", "dxc4",
        "Tc1", "Ld3", "Lf1", "Lxd4", "Qxd4", "Dxd4", "Td1", "Td8", "Dg5", "Td5", "De7", "Tf5", "De8+", "Kh7",
        "De1", "Tf3", "Lxd3", "Txd3", "Txd3", "Dxd3", "De7", "Df3",
    };

    /// <summary>Eine Antwort, wie das Modell sie schickt: nur die Einträge, ohne SAN-Lesart (die schwerste Form).</summary>
    private static string Answer(IEnumerable<string> written, string? white = "Didi", string? black = "Patrick")
        => JsonSerializer.Serialize(new
        {
            notationLanguage = "de", @event = "Simultan 2026", site = "Schwaz", date = "5.6.26", dateIso = "2026-06-05",
            round = "1", white, black, result = "0-1",
            moves = written.Select((w, i) => new
            {
                moveNumber = i / 2 + 1, color = i % 2 == 0 ? "w" : "b", written = w, san = "", alternatives = Array.Empty<string>(),
                confidence = "high", note = "",
            }),
            remarks = "",
        });

    private async Task<ScoresheetScanDto> UploadAndProcessAsync(int userId)
    {
        var (scan, reason) = await _service.CreateAsync(userId, Jpeg(), "image/jpeg", "IMG_1.jpg", "de");
        Assert.Null(reason);
        Assert.Equal(scan!.Id, await _service.ClaimNextAsync(default));
        await _service.ProcessAsync(scan.Id, default);
        return (await _service.GetAsync(userId, scan.Id))!;
    }

    [Fact]
    public async Task Create_WithoutApiKey_IsRefused()
    {
        var u = await UserAsync();
        _vision.IsConfigured = false;
        var (scan, reason) = await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "auto");
        Assert.Null(scan);
        Assert.Equal("notConfigured", reason);
    }

    [Fact]
    public async Task Create_NotAnImage_IsRefused()
    {
        var u = await UserAsync();
        var (_, reason) = await _service.CreateAsync(u.Id, "hello"u8.ToArray(), "image/jpeg", "a.jpg", "auto");
        Assert.Equal("unsupportedImage", reason);
        var (_, reason2) = await _service.CreateAsync(u.Id, Jpeg(), "application/pdf", "a.pdf", "auto");
        Assert.Equal("unsupportedImage", reason2);
    }

    [Fact]
    public async Task Create_UnknownLanguage_IsRefused()
    {
        var u = await UserAsync();
        var (_, reason) = await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "klingon");
        Assert.Equal("invalidLanguage", reason);
    }

    [Fact]
    public async Task Create_CapsOpenScansPerUser()
    {
        var u = await UserAsync();
        for (var i = 0; i < ScoresheetScanService.MaxOpenPerUser; i++)
            Assert.Null((await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "auto")).Reason);
        var (_, reason) = await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "auto");
        Assert.Equal("tooManyOpen", reason);
    }

    [Fact]
    public async Task Process_ReadsTheSheet_AndStoresAGameWithThePhoto()
    {
        var u = await UserAsync();
        _vision.Answers.Enqueue(new(Answer(Written), null));

        var scan = await UploadAndProcessAsync(u.Id);

        Assert.Equal("done", scan.Status);
        Assert.Equal(Written.Length, scan.MoveCount);
        Assert.Equal(1, scan.Rounds);
        Assert.Equal(0, scan.UnresolvedCount);
        var game = await _db.SavedGames.SingleAsync(g => g.Id == scan.SavedGameId);
        Assert.Equal(SavedGameService.ScoresheetSource, game.Source);
        Assert.Equal("Didi", game.White);
        Assert.Equal("0-1", game.Result);
        Assert.Equal(new DateTime(2026, 6, 5), game.PlayedAt!.Value.Date);
        Assert.Contains("[Event \"Simultan 2026\"]", game.Pgn);
        Assert.Contains("[Round \"1\"]", game.Pgn);
        Assert.Contains("25. exd4", game.Pgn);
        // Der reparierte Lesefehler steht als Kommentar im PGN — mit dem, was auf dem Formular stand.
        Assert.Contains("{sheet: Qxd4}", game.Pgn);

        var photo = await _service.PhotoForGameAsync(u.Id, game.Id);
        Assert.NotNull(photo);
        Assert.Equal("IMG_1.jpg", photo!.Value.FileName);
        Assert.True(photo.Value.Data.Length > 0);

        // In der Partienliste trägt die Partie ihre Einlesung (⋮-Menü: Foto anzeigen/herunterladen).
        var list = await _games.ListAsync(u.Id);
        Assert.Equal(scan.Id, list.Single().ScanId);

        // Glocke: fertig gelesen, mit Link direkt auf die Korrekturseite.
        var note = await _db.Notifications.SingleAsync(n => n.UserId == u.Id);
        Assert.Equal(NotificationType.ScoresheetRead, note.Type);
        Assert.Equal($"/games/{game.Id}/edit", note.Link);
        Assert.Contains("\"moves\":\"66\"", note.DataJson);
    }

    [Fact]
    public async Task Process_WhenTheReadingGetsStuck_AsksAgainWithThePositionAndLegalMoves()
    {
        var u = await UserAsync();
        var broken = Written.ToArray();
        broken[40] = "Zz9"; // 21. Lh3 unlesbar gelesen …
        broken[41] = "Yy8"; // … und der Folgezug auch: kein Joker kann das überbrücken
        _vision.Answers.Enqueue(new(Answer(broken), null));
        _vision.Answers.Enqueue(new(Answer(Written), null));

        var scan = await UploadAndProcessAsync(u.Id);

        Assert.Equal("done", scan.Status);
        Assert.Equal(2, scan.Rounds);
        Assert.Equal(Written.Length, scan.MoveCount);
        Assert.Equal(2, _vision.Instructions.Count);
        var repair = _vision.Instructions[1];
        Assert.Contains("second look", repair);
        Assert.Contains("Legal moves there are:", repair);
        Assert.Contains("move 21", repair);
    }

    [Fact]
    public async Task Process_KeepsTheBetterReading_WhenTheSecondLookIsWorse()
    {
        var u = await UserAsync();
        var broken = Written.ToArray();
        broken[60] = "Zz9";
        broken[61] = "Yy8";
        _vision.Answers.Enqueue(new(Answer(broken), null));                 // bis Zug 30 gut
        _vision.Answers.Enqueue(new(Answer(Written.Take(10)), null));       // Nachfrage: schlechter
        _vision.Answers.Enqueue(new(null, "failed"));                       // dritte Runde scheitert

        var scan = await UploadAndProcessAsync(u.Id);

        Assert.Equal("done", scan.Status);
        Assert.Equal(60, scan.MoveCount);
        Assert.Equal(6, scan.UnresolvedCount);
        var game = await _db.SavedGames.SingleAsync();
        Assert.Contains("sheet, not resolved: Zz9 Yy8", game.Pgn);
    }

    [Fact]
    public async Task Process_Refusal_FailsWithReason()
    {
        var u = await UserAsync();
        _vision.Answers.Enqueue(new(null, "refused"));
        var scan = await UploadAndProcessAsync(u.Id);
        Assert.Equal("failed", scan.Status);
        Assert.Equal("refused", scan.Error);
        Assert.Empty(_db.SavedGames);
        var note = await _db.Notifications.SingleAsync();
        Assert.Equal(NotificationType.ScoresheetFailed, note.Type);
        Assert.Contains("refused", note.DataJson);
    }

    [Fact]
    public async Task Process_NoMoves_Fails()
    {
        var u = await UserAsync();
        _vision.Answers.Enqueue(new(Answer(Array.Empty<string>()), null));
        var scan = await UploadAndProcessAsync(u.Id);
        Assert.Equal("failed", scan.Status);
        Assert.Equal("noMoves", scan.Error);
    }

    [Fact]
    public async Task ClaimNext_GivesUpAfterMaxAttempts()
    {
        var u = await UserAsync();
        var (scan, _) = await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "auto");
        var row = await _db.ScoresheetScans.SingleAsync();
        row.Attempts = ScoresheetScanService.MaxAttempts;
        await _db.SaveChangesAsync();

        Assert.Null(await _service.ClaimNextAsync(default));
        Assert.Equal("failed", (await _service.GetAsync(u.Id, scan!.Id))!.Status);
    }

    [Fact]
    public async Task RequeueInterrupted_PutsRunningScansBack()
    {
        var u = await UserAsync();
        await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "auto");
        await _service.ClaimNextAsync(default);
        Assert.Equal(1, await _service.RequeueInterruptedAsync(default));
        Assert.Equal(ScoresheetScanStatus.Pending, (await _db.ScoresheetScans.SingleAsync()).Status);
    }

    [Fact]
    public async Task EditState_AndResolveRest_WorkOnTheOwnGameOnly()
    {
        var u = await UserAsync();
        var other = await UserAsync("other");
        _vision.Answers.Enqueue(new(Answer(Written), null));
        var scan = await UploadAndProcessAsync(u.Id);
        var gameId = scan.SavedGameId!.Value;

        var state = await _service.EditStateAsync(u.Id, gameId);
        Assert.NotNull(state);
        Assert.Equal(Written, state!.Written);
        Assert.Equal(Written.Length, state.Plies.Count);
        Assert.Null(await _service.EditStateAsync(other.Id, gameId));
        Assert.Null(await _service.PhotoForGameAsync(other.Id, gameId));

        // Der Nutzer legt 25. exd4 fest — der Rest ab Eintrag 49 wird neu aufbereitet.
        var prefix = state.Plies.Take(49).Select(p => p.San).ToList();
        var rest = await _service.ResolveRestAsync(u.Id, gameId, prefix, 49);
        Assert.NotNull(rest);
        Assert.Equal(Written.Length - 49, rest!.Plies.Count);
        Assert.Equal("Qxd4", rest.Plies[0].San);
    }

    [Fact]
    public async Task DeleteGame_TakesThePhotoAlong()
    {
        var u = await UserAsync();
        _vision.Answers.Enqueue(new(Answer(Written), null));
        var scan = await UploadAndProcessAsync(u.Id);
        Assert.True(await _games.DeleteAsync(u.Id, scan.SavedGameId!.Value));
        Assert.Empty(_db.ScoresheetScans);
    }

    // ── Korrigieren (SavedGameService.UpdateAsync über den Controller) ─────────

    private GameCorrectionController Controller(int userId)
    {
        var c = new GameCorrectionController(_games, _service);
        c.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) }, "Test")),
            },
        };
        return c;
    }

    [Fact]
    public async Task Update_ReplacesMovesAndHeaders_AndDropsTheAnalysisLink()
    {
        var u = await UserAsync();
        var saved = await _games.SaveAsync(u.Id, new SaveGameInputDto
        {
            Source = "lichess", Moves = new() { "e4", "e5", "Nf3" }, White = "a", Black = "b", WhiteElo = 1800, ExternalId = "x1",
        });
        var row = await _db.SavedGames.SingleAsync();
        row.GameAnalysisId = 42;
        await _db.SaveChangesAsync();

        var result = await Controller(u.Id).Update(saved.Id, new GameUpdateDto
        {
            Moves = new() { new() { San = "e4" }, new() { San = "c5", Comment = "Sizilianisch" }, new() { San = "Nf3" } },
            White = "Anna", Black = "Bert", Result = "1-0", Event = "Vereinsmeisterschaft", Date = "2026-06-05", Round = "3",
        });

        var dto = Assert.IsType<OkObjectResult>(result.Result).Value as SavedGameDetailDto;
        Assert.NotNull(dto);
        Assert.Contains("1. e4 c5 {Sizilianisch} 2. Nf3 1-0", dto!.Pgn);
        Assert.Contains("[WhiteElo \"1800\"]", dto.Pgn);           // nicht bearbeitete Header bleiben
        Assert.Contains("[Event \"Vereinsmeisterschaft\"]", dto.Pgn);
        Assert.Equal("Anna", dto.White);
        row = await _db.SavedGames.SingleAsync();
        Assert.Null(row.GameAnalysisId);
        Assert.Equal(3, row.MoveCount);
    }

    [Fact]
    public async Task Update_SameMoves_KeepsTheAnalysisLink()
    {
        var u = await UserAsync();
        var saved = await _games.SaveAsync(u.Id, new SaveGameInputDto { Source = "lichess", Moves = new() { "e4", "e5" } });
        var row = await _db.SavedGames.SingleAsync();
        row.GameAnalysisId = 42;
        await _db.SaveChangesAsync();

        await Controller(u.Id).Update(saved.Id, new GameUpdateDto
        {
            Moves = new() { new() { San = "e4" }, new() { San = "e5" } }, White = "Neu",
        });
        Assert.Equal(42, (await _db.SavedGames.SingleAsync()).GameAnalysisId);
    }

    [Fact]
    public async Task Update_IllegalMove_Is400_AndNothingChanges()
    {
        var u = await UserAsync();
        var saved = await _games.SaveAsync(u.Id, new SaveGameInputDto { Source = "lichess", Moves = new() { "e4", "e5" } });
        var before = (await _db.SavedGames.SingleAsync()).Pgn;

        var result = await Controller(u.Id).Update(saved.Id, new GameUpdateDto
        {
            Moves = new() { new() { San = "e4" }, new() { San = "Ke2" } },
        });
        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(before, (await _db.SavedGames.SingleAsync()).Pgn);
    }

    [Fact]
    public async Task Update_ForeignGame_Is404()
    {
        var u = await UserAsync();
        var other = await UserAsync("other");
        var saved = await _games.SaveAsync(u.Id, new SaveGameInputDto { Source = "lichess", Moves = new() { "e4" } });
        var result = await Controller(other.Id).Update(saved.Id, new GameUpdateDto { Moves = new() { new() { San = "d4" } } });
        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Photo_Download_HasTheFileName()
    {
        var u = await UserAsync();
        _vision.Answers.Enqueue(new(Answer(Written), null));
        var scan = await UploadAndProcessAsync(u.Id);
        var c = Controller(u.Id);
        var file = Assert.IsType<FileContentResult>(await c.Photo(scan.SavedGameId!.Value, download: true));
        Assert.Equal("IMG_1.jpg", file.FileDownloadName);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.IsType<NotFoundResult>(await Controller(u.Id).Photo(999));
    }
    // ── Kostenbremse ────────────────────────────────────────────────────────

    [Fact]
    public void Budget_AnswerCap_ComesFromWhatIsLeft()
    {
        var b = new ScoresheetBudget(null);
        // Opus 5.5: 4 $ bzw. 20 $ je Million Tokens = 4 bzw. 20 Millionstel Dollar je Token.
        Assert.Equal(10_000 * 4 + 2_000 * 20, b.CostMicroUsd(10_000, 2_000));
        Assert.Equal(2_000_000, b.UserDailyMicroUsd);
        // Frisch: der volle Deckel (2 $ − 0,048 $ Eingabe-Reserve reicht für 97 600 Tokens → auf 64 000 gedeckelt).
        Assert.Equal(new CallAllowance(ScoresheetBudget.MaxOutputTokens, null), b.Allowance(0, 0, 0, false));
        // 1,5 $ verbraucht: (0,5 − 0,048) / 20e-6 = 22 600 Tokens.
        Assert.Equal(new CallAllowance(22_600, null), b.Allowance(1_500_000, 1_500_000, 1_500_000, false));
        // Unter 16 000 Tokens gesperrt — mit dem Grund des knappsten Budgets.
        Assert.Equal("userDailyBudget", b.Allowance(1_700_000, 0, 0, false).Blocked);
        Assert.Equal("userMonthlyBudget", b.Allowance(0, 9_700_000, 0, false).Blocked);
        Assert.Equal("globalBudget", b.Allowance(0, 0, 14_700_000, false).Blocked);
        // Admins: keine Nutzerbudgets, das Gesamtbudget gilt trotzdem.
        Assert.Null(b.Allowance(50_000_000, 50_000_000, 0, true).Blocked);
        Assert.Equal("globalBudget", b.Allowance(0, 0, 15_000_000, true).Blocked);
    }

    [Fact]
    public async Task Process_BooksTheTokens_OfEveryCall()
    {
        var u = await UserAsync();
        _vision.Answers.Enqueue(new(Answer(Written), null, 8_000, 12_000));
        var scan = await UploadAndProcessAsync(u.Id);
        var row = await _db.ScoresheetScans.SingleAsync(s => s.Id == scan.Id);
        Assert.Equal(8_000, row.InputTokens);
        Assert.Equal(12_000, row.OutputTokens);
        Assert.Equal(8_000 * 4 + 12_000 * 20, row.CostMicroUsd);
    }

    [Fact]
    public async Task Process_FailedCall_IsBookedToo()
    {
        var u = await UserAsync();
        _vision.Answers.Enqueue(new(null, "truncated", 8_000, 24_000));
        var scan = await UploadAndProcessAsync(u.Id);
        Assert.Equal("failed", scan.Status);
        Assert.Equal("truncated", scan.Error);
        Assert.Equal([ScoresheetReadMode.Transcribe], _vision.Modes); // schon ohne Nachdenken: kein Rückfall mehr
        Assert.Equal(8_000 * 4 + 24_000 * 20, (await _db.ScoresheetScans.SingleAsync()).CostMicroUsd);
    }

    [Fact]
    public async Task Process_WithThinking_CutOffTwice_FailsTruncated_AndBooksBoth()
    {
        WithThinking();
        var u = await UserAsync();
        _vision.Answers.Enqueue(new(null, "truncated", 8_000, 24_000));
        _vision.Answers.Enqueue(new(null, "truncated", 8_000, 10_000)); // auch der Rückfall ohne Nachdenken
        var scan = await UploadAndProcessAsync(u.Id);
        Assert.Equal("failed", scan.Status);
        Assert.Equal("truncated", scan.Error);
        Assert.Equal([ScoresheetReadMode.Full, ScoresheetReadMode.Transcribe], _vision.Modes);
        Assert.Equal(16_000 * 4 + 34_000 * 20, (await _db.ScoresheetScans.SingleAsync()).CostMicroUsd);
    }

    // ── Festgedacht (Prod 25.09.: 64 000 Tokens Nachdenken, keine Antwort) ─────────────────────────

    [Fact]
    public async Task Process_ThinkingCutOff_ReadsAgainWithoutThinking_AndStaysThereForTheSecondLook()
    {
        WithThinking();
        var u = await UserAsync();
        var broken = Written.ToArray();
        broken[40] = "Zz9";
        broken[41] = "Yy8";
        _vision.Answers.Enqueue(new(null, "truncated", 5_500, 40_000));  // mit Nachdenken: nichts
        _vision.Answers.Enqueue(new(Answer(broken), null, 5_500, 9_000)); // ohne: bleibt bei Zug 21 hängen
        _vision.Answers.Enqueue(new(Answer(Written), null, 9_000, 9_000)); // Nachfrage, ebenfalls ohne

        var scan = await UploadAndProcessAsync(u.Id);

        Assert.Equal("done", scan.Status);
        Assert.Equal(Written.Length, scan.MoveCount);
        Assert.Equal(3, scan.Rounds);
        Assert.Equal([ScoresheetReadMode.Full, ScoresheetReadMode.Transcribe, ScoresheetReadMode.Transcribe], _vision.Modes);
        Assert.Equal(ScoresheetReader.FullCallMaxTokens, _vision.MaxTokens[0]);
        Assert.Equal(ScoresheetReader.TranscribeCallMaxTokens, _vision.MaxTokens[1]);
        Assert.DoesNotContain("second look", _vision.Instructions[1]); // derselbe erste Auftrag, nur ohne Nachdenken
        Assert.Contains("second look", _vision.Instructions[2]);
        var row = await _db.ScoresheetScans.SingleAsync();
        Assert.Equal(20_000 * 4 + 58_000 * 20, row.CostMicroUsd); // alle drei Aufrufe verbucht
    }

    [Fact]
    public async Task Process_ByDefault_ReadsWithoutThinking_AlsoTheSecondLook()
    {
        // Vorgabe seit 0.533.2 (Scoresheet:Thinking nicht gesetzt): nur abschreiben, auch die Nachfrage.
        var u = await UserAsync();
        var broken = Written.ToArray();
        broken[40] = "Zz9";
        broken[41] = "Yy8";
        _vision.Answers.Enqueue(new(Answer(broken), null));
        _vision.Answers.Enqueue(new(Answer(Written), null));

        var scan = await UploadAndProcessAsync(u.Id);

        Assert.Equal("done", scan.Status);
        Assert.Equal([ScoresheetReadMode.Transcribe, ScoresheetReadMode.Transcribe], _vision.Modes);
        Assert.Equal(ScoresheetReader.TranscribeCallMaxTokens, _vision.MaxTokens[0]);
    }

    [Fact]
    public async Task Process_RuntimeCapMidCall_FailsWithTimeout_BooksTheWorstCase_AndRingsTheBell()
    {
        var u = await UserAsync();
        _vision.Hang = true;
        var (created, _) = await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "de");
        await _service.ClaimNextAsync(default);
        using var cap = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await _service.ProcessAsync(created!.Id, cap.Token, shutdown: CancellationToken.None);

        var row = await _db.ScoresheetScans.SingleAsync();
        Assert.Equal(ScoresheetScanStatus.Failed, row.Status);
        Assert.Equal("timeout", row.Error);
        // Was der abgebrochene Aufruf gekostet hat, meldet niemand mehr — verbucht wird sein Deckel.
        Assert.Equal(ScoresheetReader.TranscribeCallMaxTokens, row.OutputTokens);
        Assert.Equal(ScoresheetBudget.ReserveInputTokens * 4L + ScoresheetReader.TranscribeCallMaxTokens * 20L, row.CostMicroUsd);
        var note = await _db.Notifications.SingleAsync(n => n.UserId == u.Id);
        Assert.Equal(NotificationType.ScoresheetFailed, note.Type);
        Assert.Contains("\"reason\":\"timeout\"", note.DataJson);
    }

    [Fact]
    public async Task Process_Shutdown_LeavesTheScanRunning_ForTheNextStart()
    {
        var u = await UserAsync();
        _vision.Hang = true;
        var (created, _) = await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "de");
        await _service.ClaimNextAsync(default);
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.ProcessAsync(created!.Id, stop.Token, stop.Token));

        Assert.Equal(ScoresheetScanStatus.Running, (await _db.ScoresheetScans.SingleAsync()).Status);
        Assert.Empty(_db.Notifications);
    }

    [Fact]
    public async Task Create_OverTheDailyBudget_IsRefused_ButNotForAdmins()
    {
        WithBudgets(userDaily: 1m);
        var u = await UserAsync();
        _db.ScoresheetScans.Add(new ScoresheetScan
        {
            UserId = u.Id, Photo = new byte[] { 1 }, Status = ScoresheetScanStatus.Done, CostMicroUsd = 700_000,
        });
        await _db.SaveChangesAsync();

        var (_, reason) = await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "de");
        Assert.Equal("userDailyBudget", reason); // 0,30 $ übrig reichen nicht für 16 000 Antwort-Tokens + Eingabe

        var status = await _service.StatusAsync(u.Id);
        Assert.Equal("userDailyBudget", status.Blocked);
        Assert.Equal(70, status.BudgetUsedPercent);

        u.IsAdmin = true;
        await _db.SaveChangesAsync();
        Assert.Null((await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "de")).Reason);
        Assert.True((await _service.StatusAsync(u.Id)).Unlimited);
    }

    [Fact]
    public async Task Create_TheGlobalBudget_StopsEveryone()
    {
        WithBudgets(userDaily: 100m, globalDaily: 5m);
        var rich = await UserAsync("rich");
        var other = await UserAsync("other");
        _db.ScoresheetScans.Add(new ScoresheetScan
        {
            UserId = rich.Id, Photo = new byte[] { 1 }, Status = ScoresheetScanStatus.Done, CostMicroUsd = 4_700_000,
        });
        await _db.SaveChangesAsync();
        Assert.Equal("globalBudget", (await _service.CreateAsync(other.Id, Jpeg(), "image/jpeg", "a.jpg", "de")).Reason);
    }

    [Fact]
    public async Task Process_WhenTheBudgetRunsOut_SkipsTheSecondLook_AndKeepsTheFirstReading()
    {
        WithBudgets(userDaily: 0.6m);
        var u = await UserAsync();
        var broken = Written.ToArray();
        broken[60] = "Zz9";
        broken[61] = "Yy8";
        // Die erste Lesung kostet 0,28 $ — danach reicht der Rest nicht mehr für eine brauchbare Antwort.
        _vision.Answers.Enqueue(new(Answer(broken), null, 20_000, 10_000));
        _vision.Answers.Enqueue(new(Answer(Written), null, 1, 1));

        var scan = await UploadAndProcessAsync(u.Id);

        Assert.Equal("done", scan.Status);
        Assert.Single(_vision.Instructions);           // keine Nachfrage
        Assert.Equal(27_600, _vision.MaxTokens[0]);    // (0,6 $ − 0,048 $) / 20e-6 — der Deckel kam aus dem Budget
        Assert.Equal(60, scan.MoveCount);              // die erste Lesung bleibt
        Assert.Equal(6, scan.UnresolvedCount);
    }

    [Fact]
    public async Task Process_WhenTheBudgetIsGoneBeforeTheFirstCall_FailsWithoutCalling()
    {
        WithBudgets(userDaily: 1m);
        var u = await UserAsync();
        var (scan, reason) = await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "de");
        Assert.Null(reason);
        // Zwischen Hochladen und Lesen hat eine andere Einlesung das Budget aufgebraucht.
        _db.ScoresheetScans.Add(new ScoresheetScan
        {
            UserId = u.Id, Photo = new byte[] { 1 }, Status = ScoresheetScanStatus.Done, CostMicroUsd = 900_000,
        });
        await _db.SaveChangesAsync();

        await _service.ClaimNextAsync(default);
        await _service.ProcessAsync(scan!.Id, default);
        var dto = await _service.GetAsync(u.Id, scan.Id);
        Assert.Equal("failed", dto!.Status);
        Assert.Equal("userDailyBudget", dto.Error);
        Assert.Empty(_vision.Instructions);
    }

    // ── Meine Seite (0.531.0) ────────────────────────────────────────────

    [Fact]
    public async Task Process_ChosenSide_IsStored_AndTurnsTheSharedView()
    {
        var u = await UserAsync();
        _vision.Answers.Enqueue(new(Answer(Written), null));
        var (scan, _) = await _service.CreateAsync(u.Id, Jpeg(), "image/jpeg", "a.jpg", "de", "black");
        await _service.ClaimNextAsync(default);
        await _service.ProcessAsync(scan!.Id, default);

        var game = await _db.SavedGames.SingleAsync();
        Assert.Equal("black", game.OwnerSide);
        Assert.Equal("black", (await _games.GetAsync(u.Id, game.Id))!.OwnerSide);
        Assert.Equal("black", (await _games.GetSharedAsync(game.ShareToken))!.OwnerSide);
    }

    [Fact]
    public async Task Process_AutoSide_FindsTheProfileNameAmongThePlayers()
    {
        var u = await UserAsync();
        _db.UserProfiles.Add(new UserProfile { UserId = u.Id, FirstName = "Patrick", LastName = "Oberschmid" });
        await _db.SaveChangesAsync();
        _vision.Answers.Enqueue(new(Answer(Written, white: "Didi", black: "P. Oberschmid"), null));
        var scan = await UploadAndProcessAsync(u.Id);
        Assert.Equal("black", (await _db.SavedGames.SingleAsync(g => g.Id == scan.SavedGameId)).OwnerSide);
    }

    [Theory]
    [InlineData("Didi", "Patrick", "white")]           // eigener Name „Didi" steht bei Weiß
    [InlineData("Müller, Jörg", "Huber", "white")]
    [InlineData("Huber", "Jorg Muller", "black")]      // Akzente egal
    [InlineData("Muller", "Muller", null)]             // beide Seiten → keine Drehung
    [InlineData("Maier", "Huber", null)]               // keine Seite
    public void GuessOwnerSide_MatchesWholeWords_OnlyOneSide(string white, string black, string? expected)
    {
        var mine = white == "Didi" ? new[] { "Didi" } : new[] { "Müller", null, "Jörg" };
        Assert.Equal(expected, ScoresheetScanService.GuessOwnerSide(white, black, mine));
    }

    [Fact]
    public async Task Update_SetsAndClearsTheOwnSide()
    {
        var u = await UserAsync();
        var saved = await _games.SaveAsync(u.Id, new SaveGameInputDto { Source = "lichess", Moves = new() { "e4", "e5" } });
        var c = Controller(u.Id);
        var moves = new List<GameMoveInputDto> { new() { San = "e4" }, new() { San = "e5" } };

        var r1 = Assert.IsType<OkObjectResult>((await c.Update(saved.Id, new GameUpdateDto { Moves = moves, OwnerSide = "black" })).Result);
        Assert.Equal("black", ((SavedGameDetailDto)r1.Value!).OwnerSide);
        await c.Update(saved.Id, new GameUpdateDto { Moves = moves });          // null = unverändert
        Assert.Equal("black", (await _db.SavedGames.SingleAsync()).OwnerSide);
        await c.Update(saved.Id, new GameUpdateDto { Moves = moves, OwnerSide = "" });
        Assert.Null((await _db.SavedGames.SingleAsync()).OwnerSide);
    }
}
