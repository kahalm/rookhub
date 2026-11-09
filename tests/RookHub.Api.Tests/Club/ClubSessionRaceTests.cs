using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Club;
using RookHub.Api.Models;
using RookHub.Api.Exceptions;
using RookHub.Api.Services.Club;
using Xunit;

namespace RookHub.Api.Tests.Club;

/// <summary>G1-007: zwei Trainer speichern gleichzeitig die erste Einheit eines Tages. Der zweite lief in den eindeutigen
/// Index (GroupId, Date) und bekam 500; jetzt wird seine Eingabe auf die inzwischen vorhandene Einheit angewendet.</summary>
public class ClubSessionRaceTests : IDisposable
{
    private readonly string _name = Guid.NewGuid().ToString();
    private readonly AppDbContext _db;
    private readonly SessionRaceInterceptor _race;

    public ClubSessionRaceTests()
    {
        _race = new SessionRaceInterceptor(_name);
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(_name).AddInterceptors(_race).Options);
    }

    public void Dispose() => _db.Dispose();

    private ClubService Svc() => new(_db, () => new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc));
    private static ClubActor Manager() => new(1, true, false);

    [Fact]
    public async Task SaveSession_UniqueRaceOnFirstSessionOfTheDay_ReturnsConflictAndKeepsTheOtherSession()
    {
        var g = (await Svc().CreateGroupAsync(Manager(), new ClubGroupInputDto { Name = "Anfänger" })).Id;
        var a = await Svc().CreateMemberAsync(Manager(), new ClubMemberInputDto { FirstName = "Anna", LastName = "Auer", GroupIds = [g] });
        var b = await Svc().CreateMemberAsync(Manager(), new ClubMemberInputDto { FirstName = "Ben", LastName = "Berger", GroupIds = [g] });
        _race.Arm(g, a.Id);

        // Seit F4-013 überschreibt ein Speichern ohne Kennung keine Einheit: der Verlierer des Wettlaufs bekommt 409 wie
        // jedes Speichern auf einen schon belegten Tag (statt 500 vor G1-007), die Einheit des anderen Trainers bleibt
        // unverändert — er lädt den Tag neu und trägt seine Kinder dort ein.
        await Assert.ThrowsAsync<ConflictException>(() => Svc().SaveSessionAsync(Manager(), g, new ClubSessionInputDto
        {
            Date = "2026-09-25", Topic = "Gabel",
            Attendance = [new() { MemberId = b.Id, Status = "present" }],
        }));

        var session = Assert.Single(await _db.ClubSessions.AsNoTracking().Include(s => s.Attendance).ToListAsync());
        Assert.Null(session.Topic);
        Assert.Equal([a.Id], session.Attendance.Select(x => x.MemberId));
    }

    /// <summary>Beim ersten Speichern einer NEUEN Einheit legt „der andere Trainer“ sie an (mit seinem Eintrag), und das eigene
    /// Speichern scheitert wie unter MariaDB mit „Duplicate entry“.</summary>
    private sealed class SessionRaceInterceptor(string dbName) : SaveChangesInterceptor
    {
        private int? _group, _member;
        public void Arm(int group, int member) => (_group, _member) = (group, member);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (_group is { } g && _member is { } m
                && eventData.Context!.ChangeTracker.Entries<ClubSession>().Any(e => e.State == EntityState.Added && e.Entity.GroupId == g))
            {
                _group = null;
                await using var other = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(dbName).Options);
                var s = new ClubSession { GroupId = g, Date = new DateOnly(2026, 9, 25), CreatedAt = DateTime.UtcNow };
                s.Attendance.Add(new ClubAttendance { MemberId = m, Status = ClubAttendanceStatus.Present });
                other.ClubSessions.Add(s);
                await other.SaveChangesAsync(cancellationToken);
                throw new DbUpdateException("An error occurred while saving the entity changes.",
                    new InvalidOperationException("Duplicate entry for key 'IX_ClubSessions_GroupId_Date'"));
            }
            return await base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}
