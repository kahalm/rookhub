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
    public List<(int OwnerUserId, string Bid, string Target, string CourseName, int? TargetRepertoireId)> Calls { get; } = new();
    public int? ReturnId { get; set; }

    public Task<int?> EnqueueReimportAsync(int ownerUserId, string bid, string target, string courseName, int? targetRepertoireId = null, CancellationToken ct = default)
    {
        Calls.Add((ownerUserId, bid, target, courseName, targetRepertoireId));
        return Task.FromResult(ReturnId);
    }
}

/// <summary>Baut einen <see cref="ImportReprocessService"/> für Tests (NullLogger, Stub-Reimporter).</summary>
public static class ReprocessTestHelper
{
    public static ImportReprocessService Build(AppDbContext db, ICourseReimporter? reimporter = null)
        => new(db, new PgnImportService(db), reimporter ?? new StubCourseReimporter(),
               NullLogger<ImportReprocessService>.Instance);
}
