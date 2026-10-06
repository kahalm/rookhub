using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Kursliste zählt die Quiz-Linien je Buch (`BookId`, `!IsInfoOnly`). Ohne einen Index über BEIDE Spalten liest
/// MariaDB dafür jede Linie samt Zugtexten — auf Prod 1,8 s je Aufruf von /courses (gemessen 2026-10-06).
/// </summary>
public class CourseListIndexTests
{
    [Fact]
    public void BookPuzzles_HaveCoveringIndexForTheLineCount()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var entity = db.Model.FindEntityType(typeof(BookPuzzle))!;
        Assert.Contains(entity.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(BookPuzzle.BookId), nameof(BookPuzzle.IsInfoOnly) }));
    }
}
