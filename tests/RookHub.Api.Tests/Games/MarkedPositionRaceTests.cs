using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>R4-014: zwei gleichzeitige Markierungen derselben Stellung — die zweite trifft den Unique-Index und liefert die
/// Zeile der ersten statt einer Ausnahme (HTTP 500). InMemory kennt keine Unique-Indizes, deshalb legt der Kontext beim
/// ersten Speichern die „parallele" Zeile selbst an und wirft dann wie MariaDB.</summary>
public class MarkedPositionRaceTests
{
    private const string AfterE4 = "rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1";
    private const int User = 997201;

    private sealed class RacingContext(DbContextOptions<AppDbContext> options, Exception? failure = null) : AppDbContext(options)
    {
        private bool _raced;

        public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            var added = ChangeTracker.Entries<MarkedPosition>().FirstOrDefault(e => e.State == EntityState.Added)?.Entity;
            if (added is not null && !_raced)
            {
                _raced = true;
                if (failure is not null) throw failure;
                await using var other = new AppDbContext(options);
                other.MarkedPositions.Add(new MarkedPosition
                {
                    UserId = added.UserId, PositionKey = added.PositionKey, Fen = added.Fen, Context = "board", CreatedAt = DateTime.UtcNow,
                });
                await other.SaveChangesAsync(ct);
                throw new DbUpdateException("save failed",
                    new Exception("Duplicate entry '997201-x' for key 'IX_MarkedPositions_UserId_PositionKey'"));
            }
            return await base.SaveChangesAsync(ct);
        }
    }

    [Fact]
    public async Task ParallelesMarkieren_liefertDieVorhandeneZeile_stattAusnahme()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new RacingContext(options);

        var (mark, err) = await new MarkedPositionService(db).MarkAsync(User, new MarkPositionInputDto { Fen = AfterE4, Context = "mistake" });

        Assert.Null(err);
        await using var check = new AppDbContext(options);
        var row = await check.MarkedPositions.SingleAsync();
        Assert.Equal((row.Id, "board"), (mark!.Id, mark.Context));
    }

    /// <summary>Negativfall: ein anderer Speicherfehler bleibt ein Fehler (nicht als „schon da" geschluckt).</summary>
    [Fact]
    public async Task AndererSpeicherfehler_fliegtWeiter()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new RacingContext(options, new DbUpdateException("save failed", new Exception("Data too long for column 'Fen'")));

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            new MarkedPositionService(db).MarkAsync(User, new MarkPositionInputDto { Fen = AfterE4 }));
    }
}
