using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Der anonyme Endless-Pfad ist offen und die Session-Id ein frei wählbares Feld: jede neue
/// Kennung legt eine Zeile mit bis zu 1 MB Spielstand an. Ohne Verfallsdatum wuchs das unbegrenzt.</summary>
public class AnonymousDataRetentionServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public AnonymousDataRetentionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Prune_RemovesOldAnonymousRows_KeepsFreshOnesAndAllUserRows()
    {
        var old = DateTime.UtcNow.AddDays(-90);
        var fresh = DateTime.UtcNow.AddDays(-1);
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "u", PasswordHash = "x" });
        _db.EndlessProgresses.AddRange(
            new EndlessProgress { AnonymousSessionId = "alt", UpdatedAt = old, ActiveGameState = "{}" },
            new EndlessProgress { AnonymousSessionId = "neu", UpdatedAt = fresh },
            // Angemeldeter Nutzer: bleibt IMMER, auch wenn die Zeile alt ist (das ist seine Statistik).
            new EndlessProgress { UserId = 7, UpdatedAt = old });
        _db.EndlessSessions.AddRange(
            new EndlessSession { AnonymousSessionId = "alt", CreatedAt = old, Timestamp = 1 },
            new EndlessSession { AnonymousSessionId = "neu", CreatedAt = fresh, Timestamp = 2 },
            new EndlessSession { UserId = 7, CreatedAt = old, Timestamp = 3 });
        await _db.SaveChangesAsync();

        var removed = await AnonymousDataRetentionService.PruneAsync(
            _db, DateTime.UtcNow - AnonymousDataRetentionService.AnonymousEndlessMaxAge);

        Assert.Equal(2, removed);
        Assert.Equal(new[] { "neu" }, await _db.EndlessProgresses
            .Where(p => p.UserId == null).Select(p => p.AnonymousSessionId).ToArrayAsync());
        Assert.Equal(new[] { "neu" }, await _db.EndlessSessions
            .Where(s => s.UserId == null).Select(s => s.AnonymousSessionId).ToArrayAsync());
        Assert.Equal(1, await _db.EndlessProgresses.CountAsync(p => p.UserId == 7));
        Assert.Equal(1, await _db.EndlessSessions.CountAsync(s => s.UserId == 7));
    }

    [Fact]
    public async Task Prune_EmptyDatabase_IsNoOp()
        => Assert.Equal(0, await AnonymousDataRetentionService.PruneAsync(_db, DateTime.UtcNow));

    [Fact]
    public async Task Prune_DeletesOverSeveralPortions_AndLeavesTheRest()
    {
        // A8-002: portionsweise über die Ids statt alles auf einmal zu laden — auch über die Portionsgrenze hinweg
        // (5 bzw. 3 fällige Zeilen bei Portion 2) muss alles Fällige weg und alles andere bleiben.
        var old = DateTime.UtcNow.AddDays(-90);
        _db.AppUsers.Add(new AppUser { Id = 7, Username = "u", PasswordHash = "x" });
        for (var i = 0; i < 5; i++)
            _db.EndlessProgresses.Add(new EndlessProgress { AnonymousSessionId = $"alt{i}", UpdatedAt = old, ActiveGameState = "{}" });
        for (var i = 0; i < 3; i++)
            _db.EndlessSessions.Add(new EndlessSession { AnonymousSessionId = $"alt{i}", CreatedAt = old, Timestamp = i });
        _db.EndlessProgresses.AddRange(
            new EndlessProgress { AnonymousSessionId = "neu", UpdatedAt = DateTime.UtcNow },
            new EndlessProgress { UserId = 7, UpdatedAt = old });
        _db.EndlessSessions.Add(new EndlessSession { UserId = 7, CreatedAt = old, Timestamp = 9 });
        await _db.SaveChangesAsync();

        var removed = await AnonymousDataRetentionService.PruneAsync(
            _db, DateTime.UtcNow - AnonymousDataRetentionService.AnonymousEndlessMaxAge, chunkSize: 2);

        Assert.Equal(8, removed);
        Assert.Equal(new[] { "neu" }, await _db.EndlessProgresses
            .Where(p => p.UserId == null).Select(p => p.AnonymousSessionId).ToArrayAsync());
        Assert.Equal(1, await _db.EndlessProgresses.CountAsync(p => p.UserId == 7));
        Assert.Equal(1, await _db.EndlessSessions.CountAsync());
    }

    // ---- Anonyme getReview-Senke der Extension (A3-003) ----

    private AnonymousDataRetentionService Service(Func<IServiceProvider, ChessableReviewLineService>? reviewLines = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_db);
        services.AddScoped(reviewLines ?? (_ => new ChessableReviewLineService(_db, new PgnImportService(_db))));
        return new AnonymousDataRetentionService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AnonymousDataRetentionService>.Instance);
    }

    private void AddAnonLine(string uid, string oid, int ageDays) =>
        _db.AnonymousChessableReviewLines.Add(new AnonymousChessableReviewLine
        { ChessableUid = uid, Bid = "228856", Oid = oid, Json = "{}", UpdatedAt = DateTime.UtcNow.AddDays(-ageDays) });

    [Fact]
    public async Task RunOnce_PrunesTheAnonymousReviewSink_OldRowsAndUnlinkedAfterTwoWeeks()
    {
        // Die Löschung der Anon-Senke lief bisher NUR im Kurslisten-Refresh, und der ist mit
        // Chessable:Enabled=false (PROD seit 2026-09-09) gar nicht registriert — der offene Extension-Endpunkt
        // nahm weiter an, gelöscht wurde nie. Der immer laufende Retention-Dienst übernimmt sie.
        _db.ChessableCredentials.Add(new ChessableCredential { UserId = 7, EncryptedBearer = "enc", ChessableUid = "3" });
        AddAnonLine("1", "1", 30);    // ohne Konto, älter als 14 Tage → weg
        AddAnonLine("2", "2", 5);     // ohne Konto, jung → bleibt
        AddAnonLine("3", "3", 30);    // verknüpft (claimbar) → volle 90 Tage, bleibt
        AddAnonLine("3", "4", 120);   // verknüpft, älter als 90 Tage → weg
        await _db.SaveChangesAsync();

        var removed = await Service().RunOnceAsync();

        Assert.Equal(2, removed);
        Assert.Equal(new[] { "2", "3" }, await _db.AnonymousChessableReviewLines
            .OrderBy(r => r.Oid).Select(r => r.Oid).ToArrayAsync());
    }

    [Fact]
    public async Task RunOnce_ReviewSinkFailure_StillPrunesEndless()
    {
        // Getrennte Versuche: scheitert die eine Senke, darf die andere nicht ausfallen (vorher übersprang ein
        // Fehler im Kurslisten-Refresh die Retention im selben try).
        _db.EndlessProgresses.Add(new EndlessProgress
        { AnonymousSessionId = "alt", UpdatedAt = DateTime.UtcNow.AddDays(-90), ActiveGameState = "{}" });
        await _db.SaveChangesAsync();

        var removed = await Service(_ => throw new InvalidOperationException("Senke kaputt")).RunOnceAsync();

        Assert.Equal(1, removed);
        Assert.Equal(0, await _db.EndlessProgresses.CountAsync());
    }

    [Fact]
    public void ProgramCs_RegistersTheRetentionIndependentOfTheChessableSwitch()
    {
        // Verdrahtung: registriert, bevor der Schalter überhaupt gelesen wird — also nie hinter if (chessableEnabled).
        var src = File.ReadAllText(ProgramCs());
        var registration = src.IndexOf("AddHostedService<AnonymousDataRetentionService>()", StringComparison.Ordinal);
        var chessableSwitch = src.IndexOf("var chessableEnabled", StringComparison.Ordinal);
        Assert.True(registration > 0, "AnonymousDataRetentionService nicht registriert");
        Assert.True(chessableSwitch > 0, "Chessable-Schalter nicht gefunden");
        Assert.True(registration < chessableSwitch, "Retention hängt am Chessable-Schalter");
    }

    // ---- Guard (A8-001): nichts hinter dem Chessable-Schalter trägt Pflichten des Extension-Wegs ----

    /// <summary>Was der Extension-Weg (<c>/api/extension/*</c>, läuft auch mit <c>Chessable:Enabled=false</c>) an
    /// Hintergrundpflichten hat: die Anon-getReview-Senke samt Retention und die Browser-Import-Sitzungen. Zweimal
    /// hing so eine Pflicht an einem Dienst hinter dem Schalter und fiel auf PROD (Schalter seit 2026-09-09 aus)
    /// still aus — der Watchdog bis v0.484.3, die Anon-Retention bis W2 A3-003.</summary>
    private static readonly string[] ExtensionPathDuties =
    {
        "AnonymousChessableReviewLines",
        "PruneAnonOlderThanAsync", "PruneUnlinkedAnonOlderThanAsync",
        "CloseExpiredBrowserSessionsAsync", "FailBrowserImportAsync",
    };

    [Fact]
    public void ChessableGatedHostedServices_CarryNoDutyOfTheExtensionPath()
    {
        var gated = ChessableGatedHostedServices(File.ReadAllText(ProgramCs()));
        Assert.NotEmpty(gated);   // sonst fände der Parser den Schalter-Block nicht mehr und prüfte nichts

        foreach (var service in gated)
        {
            var code = SourceOf(service);
            foreach (var duty in ExtensionPathDuties)
                Assert.False(code.Contains(duty, StringComparison.Ordinal),
                    $"{service} ist nur mit Chessable:Enabled=true registriert, trägt aber {duty} (Extension-Weg, läuft auch mit Schalter aus)");
        }

        // Die Pflichten haben einen IMMER laufenden Träger.
        foreach (var (owner, duty) in new[]
                 {
                     ("AnonymousDataRetentionService", "PruneAnonOlderThanAsync"),
                     ("AnonymousDataRetentionService", "PruneUnlinkedAnonOlderThanAsync"),
                     ("ChessableImportWatchdogService", "CloseExpiredBrowserSessionsAsync"),
                 })
        {
            Assert.DoesNotContain(owner, gated);
            Assert.Contains(duty, SourceOf(owner), StringComparison.Ordinal);
        }
    }

    /// <summary>Die Hosted Services unter <c>if (chessableEnabled)</c> — als Block oder als einzelne Anweisung.</summary>
    private static List<string> ChessableGatedHostedServices(string programCs)
    {
        var src = StripLineComments(programCs);
        var names = new List<string>();
        const string condition = "if (chessableEnabled)";
        for (var at = src.IndexOf(condition, StringComparison.Ordinal); at >= 0;
             at = src.IndexOf(condition, at + condition.Length, StringComparison.Ordinal))
        {
            var start = at + condition.Length;
            while (start < src.Length && char.IsWhiteSpace(src[start])) start++;
            int end;
            if (src[start] == '{')
            {
                var depth = 0;
                for (end = start; end < src.Length; end++)
                {
                    if (src[end] == '{') depth++;
                    else if (src[end] == '}' && --depth == 0) break;
                }
            }
            else end = src.IndexOf(';', start);
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                         src[start..end], @"AddHostedService<([\w.]+)>"))
                names.Add(m.Groups[1].Value.Split('.')[^1]);
        }
        return names;
    }

    /// <summary>Quelltext der Klasse <paramref name="className"/> (alle Dateien, die sie deklarieren), ohne Kommentare.</summary>
    private static string SourceOf(string className)
    {
        var apiDir = Path.GetDirectoryName(ProgramCs())!;
        var declaration = new System.Text.RegularExpressions.Regex($@"\bclass\s+{className}\b");
        var files = Directory.EnumerateFiles(apiDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(File.ReadAllText)
            .Where(code => declaration.IsMatch(code))
            .ToList();
        Assert.True(files.Count > 0, $"Quelltext von {className} nicht gefunden");
        return StripLineComments(string.Join("\n", files));
    }

    private static string StripLineComments(string code) =>
        string.Join("\n", code.Split('\n').Select(l =>
        {
            var c = l.IndexOf("//", StringComparison.Ordinal);
            return c >= 0 ? l[..c] : l;
        }));

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
