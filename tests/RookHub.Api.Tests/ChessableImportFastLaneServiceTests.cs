using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Slot-Logik der schnellen (gecachten) Import-Lane: sie darf bis zu <c>MaxParallel</c> voll-gecachte
/// Importe (Phase "queued", FullyCached==true) GLEICHZEITIG anstoßen, abzüglich der bereits laufenden.
/// Nicht-gecachte/unklassifizierte (null) Jobs ignoriert sie (die laufen in der Download-Lane).
/// </summary>
public class ChessableImportFastLaneServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public ChessableImportFastLaneServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync(params (ChessableImportStatus status, ChessableImportPhase phase, bool? fullyCached)[] jobs)
    {
        foreach (var (status, phase, fc) in jobs)
            _db.ChessableImports.Add(new ChessableImport
            {
                UserId = 5, Bid = "b", CourseName = "C", Target = "repertoire",
                Status = status, Phase = phase, FullyCached = fc, CreatedAt = DateTime.UtcNow,
            });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task FreeSlots_CachedQueuedNoneInflight_UpToMaxOrQueued()
    {
        await SeedAsync((ChessableImportStatus.Running, ChessableImportPhase.Queued, true), (ChessableImportStatus.Running, ChessableImportPhase.Queued, true),
                        (ChessableImportStatus.Running, ChessableImportPhase.Queued, true), (ChessableImportStatus.Running, ChessableImportPhase.Queued, true));
        Assert.Equal(3, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 3)); // min(3, 4 wartende)
        Assert.Equal(1, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 1)); // seriell (MaxParallel=1)
    }

    [Fact]
    public async Task FreeSlots_SubtractsInflightFromMax()
    {
        await SeedAsync((ChessableImportStatus.Running, ChessableImportPhase.Queued, true), (ChessableImportStatus.Running, ChessableImportPhase.Queued, true),
                        (ChessableImportStatus.Running, ChessableImportPhase.Claimed, true), (ChessableImportStatus.Running, ChessableImportPhase.Importing, true));
        Assert.Equal(1, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 3)); // min(3-2, 2 wartende)
    }

    [Fact]
    public async Task FreeSlots_ZeroWhenMaxReached()
    {
        await SeedAsync((ChessableImportStatus.Running, ChessableImportPhase.Queued, true),
                        (ChessableImportStatus.Running, ChessableImportPhase.Fetching, true), (ChessableImportStatus.Running, ChessableImportPhase.Importing, true), (ChessableImportStatus.Running, ChessableImportPhase.Claimed, true));
        Assert.Equal(0, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 3)); // 3 laufen bereits
    }

    [Fact]
    public async Task FreeSlots_IgnoresDownloadAndUnclassified()
    {
        // Nicht-gecacht (false) und unklassifiziert (null) gehören NICHT in die Fast-Lane.
        await SeedAsync((ChessableImportStatus.Running, ChessableImportPhase.Queued, false), (ChessableImportStatus.Running, ChessableImportPhase.Queued, null));
        Assert.Equal(0, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 3));
    }

    [Fact]
    public async Task FreeSlots_ZeroOnEmptyQueue()
    {
        Assert.Equal(0, await ChessableImportFastLaneService.FreeSlotsAsync(_db, 3));
    }

    /// <summary>Verdrahtung (A3-008): der Admin-Import „Kurse von Usern holen" läuft auch mit
    /// <c>Chessable:Enabled=false</c>, ein voll gecachter Kurs bekommt aber kein Queue-Ticket — stand die netzfreie
    /// Fast-Lane hinter dem Schalter, blieb er auf PROD für immer „wartend". Sie wird also nie bedingt registriert.</summary>
    [Fact]
    public void ProgramCs_RegistersTheFastLane_IndependentOfTheChessableSwitch()
    {
        var src = File.ReadAllText(ProgramCs());
        const string registration = "AddHostedService<ChessableImportFastLaneService>()";
        var at = src.IndexOf(registration, StringComparison.Ordinal);
        Assert.True(at > 0, "Fast-Lane nicht registriert");
        Assert.Equal(at, src.LastIndexOf(registration, StringComparison.Ordinal));

        // Nicht im Block um den Resume-Dienst (der bleibt hinter dem Schalter) …
        var resume = src.IndexOf("AddHostedService<ChessableImportResumeService>()", StringComparison.Ordinal);
        var blockStart = src.LastIndexOf("if (chessableEnabled)", resume, StringComparison.Ordinal);
        var blockEnd = src.IndexOf('}', resume);
        Assert.True(blockStart > 0 && blockEnd > resume, "Resume-Block nicht gefunden");
        Assert.False(at > blockStart && at < blockEnd, "Fast-Lane hängt am Chessable-Schalter");
        // … und auch nicht als einzelne Anweisung direkt dahinter.
        var statementStart = src.LastIndexOfAny(new[] { ';', '{', '}' }, at) + 1;
        var code = string.Concat(src[statementStart..at].Split('\n').Where(l => !l.TrimStart().StartsWith("//")));
        Assert.DoesNotContain("if (", code);
    }

    private static string ProgramCs([CallerFilePath] string thisFile = "")
    {
        var dir = Path.GetDirectoryName(thisFile);
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "src", "api", "RookHub.Api", "Program.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        Assert.Fail("Program.cs nicht gefunden");
        return "";
    }
}
