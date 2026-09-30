using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>
/// Byte-Stand der Chessable-Roh-Senken. Deren Deckel zählen BYTES, nicht Zeilen: ein Zeilendeckel allein ließ in
/// die offene Anon-Senke 200 000 × 256 K Zeichen ≈ 51 GB zu. Gezählt wird, was MariaDB ablegt — UTF-8-Bytes
/// (<c>LENGTH</c>, nicht <c>CHAR_LENGTH</c>: sonst zählt ein Drei-Byte-Zeichen als eines).
///
/// <para>Die Summe über LONGTEXT liest jede Zeile. Mit Cache (als Singleton über <see cref="IMemoryCache"/>) wird
/// sie deshalb höchstens alle <see cref="CacheFor"/> neu gezählt und dazwischen um das hochgezählt, was dieser
/// Prozess selbst schreibt. Ohne Cache (Tests) zählt jeder Aufruf neu.</para>
/// </summary>
public sealed class ChessableSinkBytes
{
    /// <summary>Wie lange eine gezählte Summe gilt, bevor sie neu aus der Datenbank kommt.</summary>
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(10);

    private const string AnonKey = "chessable-sink-bytes:anon";
    private readonly IMemoryCache? _cache;

    public ChessableSinkBytes(IMemoryCache? cache = null) => _cache = cache;

    /// <summary>UTF-8-Bytes eines Texts — so viel legt MariaDB (utf8mb4) dafür ab.</summary>
    public static long Utf8(string? s) => string.IsNullOrEmpty(s) ? 0 : Encoding.UTF8.GetByteCount(s);

    /// <summary>Bytes aller JSON-Antworten der Anon-Senke (<see cref="Models.AnonymousChessableReviewLine"/>).</summary>
    public Task<long> AnonTotalAsync(AppDbContext db, CancellationToken ct = default) =>
        GetAsync(AnonKey, () => CountAnonAsync(db, ct));

    /// <summary>Selbst geschriebene Bytes auf die gezählte Summe der Anon-Senke aufschlagen (negativ = Platz frei).</summary>
    public void AddAnon(long delta) => Add(AnonKey, delta);

    private async Task<long> GetAsync(string key, Func<Task<long>> count)
    {
        if (_cache is not null && _cache.TryGetValue(key, out Tally? cached) && cached is not null)
            return Interlocked.Read(ref cached.Bytes);
        var bytes = await count();
        _cache?.Set(key, new Tally { Bytes = bytes }, CacheFor);
        return bytes;
    }

    private void Add(string key, long delta)
    {
        if (delta != 0 && _cache is not null && _cache.TryGetValue(key, out Tally? cached) && cached is not null)
            Interlocked.Add(ref cached.Bytes, delta);
    }

    private static async Task<long> CountAnonAsync(AppDbContext db, CancellationToken ct)
    {
        if (!db.Database.IsRelational())   // InMemory (Tests) kennt kein LENGTH
            return (await db.AnonymousChessableReviewLines.Select(r => r.Json).ToListAsync(ct)).Sum(Utf8);
        return (await db.Database.SqlQueryRaw<long>(
                "SELECT CAST(COALESCE(SUM(LENGTH(`Json`)), 0) AS SIGNED) AS `Value` FROM `AnonymousChessableReviewLines`")
            .ToListAsync(ct)).Single();
    }

    /// <summary>Veränderliche Summe im Cache — <see cref="Interlocked"/> statt Neu-Setzen, damit parallele
    /// Schreiber einander nichts überschreiben.</summary>
    private sealed class Tally { public long Bytes; }
}
