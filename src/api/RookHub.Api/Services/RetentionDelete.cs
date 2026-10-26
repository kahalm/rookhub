using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Löschen in Portionen, ohne die Zeilen zu laden (Aufbewahrungsfristen, A8-002): erst nur die Ids, dann ein DELETE über
/// dieselbe Bedingung plus die Ids — eine inzwischen wieder berührte Zeile bleibt. InMemory (Tests) kennt kein
/// ExecuteDelete; dort über den Tracker. Jede Entität braucht eine <c>int</c>-Spalte <c>Id</c>.
/// </summary>
public static class RetentionDelete
{
    /// <summary>Höchstens so viele Zeilen je DELETE.</summary>
    public const int ChunkSize = 1000;

    /// <summary>Alles löschen, was <paramref name="query"/> trifft. <paramref name="beforeChunk"/> läuft vor jeder Portion
    /// mit deren Ids (abhängige Zeilen zuerst abräumen).</summary>
    public static async Task<int> AllAsync<T>(AppDbContext db, IQueryable<T> query, int chunkSize, CancellationToken ct,
        Func<IReadOnlyList<int>, Task>? beforeChunk = null) where T : class
    {
        var deleted = 0;
        while (true)
        {
            var ids = await query.OrderBy(e => EF.Property<int>(e, "Id")).Select(e => EF.Property<int>(e, "Id"))
                .Take(chunkSize).ToListAsync(ct);
            if (ids.Count == 0) break;
            deleted += await ChunkAsync(db, query, ids, ct, beforeChunk);
            if (ids.Count < chunkSize) break;
        }
        return deleted;
    }

    /// <summary>Hält <paramref name="query"/> bei höchstens <paramref name="cap"/> Zeilen: der Überhang geht, älteste
    /// (nach <paramref name="age"/>, dann Id) zuerst.</summary>
    public static async Task<int> OverCapAsync<T>(AppDbContext db, IQueryable<T> query, Expression<Func<T, DateTime>> age, int cap,
        int chunkSize, CancellationToken ct, Func<IReadOnlyList<int>, Task>? beforeChunk = null) where T : class
    {
        var deleted = 0;
        while (true)
        {
            var excess = await query.CountAsync(ct) - cap;
            if (excess <= 0) break;
            var ids = await query.OrderBy(age).ThenBy(e => EF.Property<int>(e, "Id")).Select(e => EF.Property<int>(e, "Id"))
                .Take(Math.Min(excess, chunkSize)).ToListAsync(ct);
            if (ids.Count == 0) break;
            var n = await ChunkAsync(db, query, ids, ct, beforeChunk);
            deleted += n;
            if (n == 0) break;   // nichts mehr löschbar (gleichzeitig berührt) — kein Kreisel
        }
        return deleted;
    }

    private static async Task<int> ChunkAsync<T>(AppDbContext db, IQueryable<T> query, List<int> ids, CancellationToken ct,
        Func<IReadOnlyList<int>, Task>? beforeChunk) where T : class
    {
        if (beforeChunk != null) await beforeChunk(ids);
        var chunk = query.Where(e => ids.Contains(EF.Property<int>(e, "Id")));
        if (db.Database.IsRelational()) return await chunk.ExecuteDeleteAsync(ct);
        var rows = await chunk.ToListAsync(ct);
        db.Set<T>().RemoveRange(rows);
        await db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
