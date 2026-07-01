using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Stub-<see cref="ICourseReimporter"/> für Tests: zeichnet die angeforderten Re-Fetch-Aufrufe auf
/// und liefert eine konfigurierbare Import-Id (oder null = „kein Bearer / nicht möglich").
/// </summary>
public class StubCourseReimporter : ICourseReimporter
{
    public List<(int OwnerUserId, string Bid, string Target, string CourseName, int? TargetRepertoireId, bool? KnownCached, bool TrustOwnership)> Calls { get; } = new();
    public int? ReturnId { get; set; }
    /// <summary>Batch-Cache-Menge, die <see cref="GetCachedBidsAsync"/> liefert (leer = nichts gecacht).</summary>
    public HashSet<string> CachedBids { get; set; } = new();
    /// <summary>Wie oft der Batch-Cache-Abruf aufgerufen wurde (soll 1× je Reprocess-Lauf sein).</summary>
    public int GetCachedBidsCalls { get; private set; }

    public Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName, int? targetRepertoireId = null, bool? knownCached = null, bool trustOwnership = false, CancellationToken ct = default)
    {
        Calls.Add((ownerUserId, bid, target, courseName, targetRepertoireId, knownCached, trustOwnership));
        return Task.FromResult(ReturnId);
    }

    public Task<HashSet<string>> GetCachedBidsAsync(CancellationToken ct = default)
    {
        GetCachedBidsCalls++;
        return Task.FromResult(CachedBids);
    }
}

/// <summary>Baut einen <see cref="ImportReprocessService"/> für Tests (NullLogger, Stub-Reimporter).</summary>
public static class ReprocessTestHelper
{
    public static ImportReprocessService Build(AppDbContext db, ICourseReimporter? reimporter = null)
        => new(db, new PgnImportService(db), reimporter ?? new StubCourseReimporter(),
               NullLogger<ImportReprocessService>.Instance);
}

/// <summary>Test-Doppel für <see cref="IReprocessLauncher"/>: merkt sich die aufgerufenen Läufe,
/// ohne einen echten Hintergrund-Task/Scope zu starten.</summary>
public sealed class RecordingReprocessLauncher : IReprocessLauncher
{
    public int CoursesCalls { get; private set; }
    public int RepertoiresCalls { get; private set; }
    public bool? LastLocalOnly { get; private set; }

    public void LaunchCourses(int userId, bool isAdmin, bool localOnly) { CoursesCalls++; LastLocalOnly = localOnly; }
    public void LaunchRepertoires(int userId, bool isAdmin, bool localOnly) { RepertoiresCalls++; LastLocalOnly = localOnly; }
}
