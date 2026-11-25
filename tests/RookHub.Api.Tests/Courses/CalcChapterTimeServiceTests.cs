using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Tests.Courses;

/// <summary>Kapitel-Uhr des Kalkulations-Modus im Konto (1.020.1) — vorher nur im localStorage, beim Abmelden weg.</summary>
public class CalcChapterTimeServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly CalcChapterTimeService _svc;

    public CalcChapterTimeServiceTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _svc = new CalcChapterTimeService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<(int user, int other, int book)> SeedAsync()
    {
        var u = new AppUser { Username = "u", PasswordHash = "h" };
        var o = new AppUser { Username = "o", PasswordHash = "h" };
        _db.AppUsers.AddRange(u, o);
        await _db.SaveChangesAsync();
        var b = new Book { FileName = "calc.pgn", DisplayName = "Calc", OwnerUserId = u.Id, IsCalculation = true,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
        _db.Books.Add(b);
        await _db.SaveChangesAsync();
        return (u.Id, o.Id, b.Id);
    }

    [Fact]
    public async Task Set_KeepsHigherPerChapter_AndGetReturnsIt()
    {
        var s = await SeedAsync();
        await _svc.SetAsync(s.user, s.book, false, new Dictionary<string, int> { ["A"] = 120, [""] = 5 });
        var r = await _svc.SetAsync(s.user, s.book, false, new Dictionary<string, int> { ["A"] = 60, ["B"] = 30 });
        Assert.Equal(120, r["A"]);   // ein älteres Gerät setzt nicht herab
        Assert.Equal(30, r["B"]);
        var got = await _svc.GetAsync(s.user, s.book, false);
        Assert.Equal(3, got.Count);
        Assert.Equal(5, got[""]);
    }

    [Fact]
    public async Task ForeignBook_404_Invalid_400()
    {
        var s = await SeedAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => _svc.GetAsync(s.other, s.book, false));
        await Assert.ThrowsAsync<DomainValidationException>(() => _svc.SetAsync(s.user, s.book, false,
            new Dictionary<string, int> { [new string('k', CalcChapterTimeService.MaxChapterLength + 1)] = 1 }));
        await Assert.ThrowsAsync<DomainValidationException>(() => _svc.SetAsync(s.user, s.book, false,
            new Dictionary<string, int> { ["A"] = -1 }));
    }
}
