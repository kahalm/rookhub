using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Courses;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Tests.Courses;

/// <summary>R3-003: der Kurszugang wurde nur beim Anlegen des Woodpecker-Plans geprüft; danach lieferte <c>/next</c> jede Linie
/// samt Lösung weiter, auch nachdem das Konto aus der Kursgruppe entfernt war. Jetzt ruht ein Kurs ohne Zugang (keine
/// Aufgabe, Versuch 404), die übrigen Kurse des Plans laufen weiter.</summary>
public class WoodpeckerAccessTests : IDisposable
{
    private const int UserId = 1, TrainerId = 2, GroupId = 30;
    private readonly AppDbContext _db = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    public void Dispose() => _db.Dispose();

    private async Task<(int BookId, List<int> Puzzles)> BookAsync(string name, int? owner, int count = 2)
    {
        var book = new Book { FileName = name + ".pgn", DisplayName = name, OwnerUserId = owner, CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var ids = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var p = new BookPuzzle { LineId = $"{name}-{i}", BookFileName = book.FileName, BookId = book.Id, Round = (i + 1).ToString(),
                Fen = "8/8/8/8/8/8/8/K6k w - - 0 1", Moves = "a1a2" };
            _db.BookPuzzles.Add(p);
            await _db.SaveChangesAsync();
            ids.Add(p.Id);
        }
        return (book.Id, ids);
    }

    [Fact]
    public async Task ZugangEntzogen_KursRuht_eigenerKursLaeuftWeiter()
    {
        _db.AppUsers.AddRange(new AppUser { Id = UserId, Username = "u", PasswordHash = "h" }, new AppUser { Id = TrainerId, Username = "t", PasswordHash = "h" });
        _db.Groups.Add(new Group { Id = GroupId, Name = "Kursgruppe" });
        _db.UserGroups.Add(new UserGroup { UserId = UserId, GroupId = GroupId });
        await _db.SaveChangesAsync();
        var (trainerBook, trainerPuzzles) = await BookAsync("Trainer", TrainerId);
        _db.BookGroupAccesses.Add(new BookGroupAccess { BookId = trainerBook, GroupId = GroupId });
        await _db.SaveChangesAsync();
        var (ownBook, ownPuzzles) = await BookAsync("Eigen", UserId);
        var svc = new WoodpeckerService(_db);
        var plan = await svc.CreateAsync(UserId, false, new CreateWoodpeckerPlanDto { BookIds = [trainerBook, ownBook] });
        Assert.Equal(trainerPuzzles[0], (await svc.GetNextAsync(UserId, plan.Id)).Puzzle!.Id);   // mit Zugang: Kurs 1 zuerst

        _db.UserGroups.RemoveRange(_db.UserGroups);   // der Trainer nimmt das Konto aus der Gruppe
        await _db.SaveChangesAsync();

        var next = await svc.GetNextAsync(UserId, plan.Id);
        Assert.Equal(ownPuzzles[0], next.Puzzle!.Id);                                            // Trainer-Kurs ruht
        await Assert.ThrowsAsync<NotFoundException>(() => svc.RecordAttemptAsync(UserId, plan.Id,
            new RecordWoodpeckerAttemptDto { BookPuzzleId = trainerPuzzles[0], Solved = true, TimeSeconds = 5 }));
        foreach (var id in ownPuzzles)
            await svc.RecordAttemptAsync(UserId, plan.Id, new RecordWoodpeckerAttemptDto { BookPuzzleId = id, Solved = true, TimeSeconds = 5 });
        Assert.Null((await svc.GetNextAsync(UserId, plan.Id)).Puzzle);                           // keine Linie des Trainer-Kurses
        // Admin behält den Zugriff (CourseAccess für Admins).
        Assert.NotNull((await svc.GetNextAsync(UserId, plan.Id, isAdmin: true)).Puzzle);
    }
}
