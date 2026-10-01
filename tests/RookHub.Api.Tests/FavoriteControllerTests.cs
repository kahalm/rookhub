using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class FavoriteControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FavoriteService _service;
    private readonly FavoriteController _controller;

    public FavoriteControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new FavoriteService(_db);
        _controller = new FavoriteController(_service);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")) }
        };
    }

    private async Task<AppUser> CreateUserAsync(string username)
    {
        var user = new AppUser { Username = username, Email = $"{username}@test.com", PasswordHash = "hash", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Puzzle> CreatePuzzleAsync(string lichessId = "p1", int rating = 1600)
    {
        var p = new Puzzle { LichessId = lichessId, Fen = "fen-std", Moves = "e2e4 e7e5", Rating = rating, Themes = "fork" };
        _db.Puzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private async Task<BookPuzzle> CreateBookPuzzleAsync(string lineId = "b1", int rating = 1800)
    {
        var p = new BookPuzzle { LineId = lineId, BookFileName = "book.pgn", Fen = "fen-book", Moves = "d2d4", BookRating = rating, Title = "Kapitel 1", Tags = "pin" };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    private static bool Favorited(IActionResult r)
    {
        var value = Assert.IsType<OkObjectResult>(r).Value!;
        return (bool)value.GetType().GetProperty("favorited")!.GetValue(value)!;
    }

    [Fact]
    public async Task Add_StandardPuzzle_PersistsFavorite()
    {
        var user = await CreateUserAsync("alice");
        var puzzle = await CreatePuzzleAsync();
        SetUser(user.Id);

        var res = await _controller.Add(new ToggleFavoriteDto { PuzzleId = puzzle.Id, Source = PuzzleSource.Standard });

        Assert.True(Favorited(res));
        Assert.Equal(1, await _db.FavoritePuzzles.CountAsync(f => f.UserId == user.Id));
    }

    [Fact]
    public async Task Add_Twice_IsIdempotent()
    {
        var user = await CreateUserAsync("alice");
        var puzzle = await CreatePuzzleAsync();
        SetUser(user.Id);

        await _controller.Add(new ToggleFavoriteDto { PuzzleId = puzzle.Id, Source = PuzzleSource.Standard });
        await _controller.Add(new ToggleFavoriteDto { PuzzleId = puzzle.Id, Source = PuzzleSource.Standard });

        Assert.Equal(1, await _db.FavoritePuzzles.CountAsync(f => f.UserId == user.Id));
    }

    [Fact]
    public async Task Add_MissingPuzzle_ReturnsNotFound()
    {
        var user = await CreateUserAsync("alice");
        SetUser(user.Id);

        var res = await _controller.Add(new ToggleFavoriteDto { PuzzleId = 9999, Source = PuzzleSource.Standard });

        Assert.IsType<NotFoundObjectResult>(res);
    }

    [Fact]
    public async Task Remove_DeletesFavorite_Idempotent()
    {
        var user = await CreateUserAsync("alice");
        var puzzle = await CreatePuzzleAsync();
        SetUser(user.Id);
        await _controller.Add(new ToggleFavoriteDto { PuzzleId = puzzle.Id, Source = PuzzleSource.Standard });

        await _controller.Remove(PuzzleSource.Standard, puzzle.Id);
        await _controller.Remove(PuzzleSource.Standard, puzzle.Id); // nochmal → kein Fehler

        Assert.Equal(0, await _db.FavoritePuzzles.CountAsync(f => f.UserId == user.Id));
    }

    [Fact]
    public async Task Contains_ReflectsState()
    {
        var user = await CreateUserAsync("alice");
        var puzzle = await CreatePuzzleAsync();
        SetUser(user.Id);

        var before = await _service.ContainsAsync(user.Id, PuzzleSource.Standard, puzzle.Id);
        await _controller.Add(new ToggleFavoriteDto { PuzzleId = puzzle.Id, Source = PuzzleSource.Standard });
        var after = await _service.ContainsAsync(user.Id, PuzzleSource.Standard, puzzle.Id);

        Assert.False(before);
        Assert.True(after);
    }

    [Fact]
    public async Task List_EnrichesBothSources_NewestFirst()
    {
        var user = await CreateUserAsync("alice");
        var std = await CreatePuzzleAsync("p-std", 1500);
        var book = await CreateBookPuzzleAsync("b-1", 2000);
        SetUser(user.Id);

        await _service.AddAsync(user.Id, PuzzleSource.Standard, std.Id);
        await _service.AddAsync(user.Id, PuzzleSource.Book, book.Id);

        var list = await _service.ListAsync(user.Id);

        Assert.Equal(2, list.Count);
        var bookDto = list.Single(f => f.Source == nameof(PuzzleSource.Book));
        Assert.Equal(2000, bookDto.Rating);
        Assert.Equal("Kapitel 1", bookDto.Title);
        Assert.Equal("fen-book", bookDto.Fen);
        Assert.Equal("d2d4", bookDto.Moves);
        var stdDto = list.Single(f => f.Source == nameof(PuzzleSource.Standard));
        Assert.Equal(1500, stdDto.Rating);
        Assert.Equal("fork", stdDto.Themes);
        Assert.Equal("e2e4 e7e5", stdDto.Moves);
    }

    [Fact]
    public async Task List_SkipsFavoritesWhosePuzzleVanished()
    {
        var user = await CreateUserAsync("alice");
        var book = await CreateBookPuzzleAsync("b-1");
        SetUser(user.Id);
        await _service.AddAsync(user.Id, PuzzleSource.Book, book.Id);
        // Buch-Puzzle entfernen (z. B. Re-Import) — der Favoriten-Eintrag bleibt verwaist.
        _db.BookPuzzles.Remove(book);
        await _db.SaveChangesAsync();

        var list = await _service.ListAsync(user.Id);

        Assert.Empty(list);
    }

    [Fact]
    public async Task SameId_DifferentSource_AreDistinctFavorites()
    {
        var user = await CreateUserAsync("alice");
        // Künstlich gleiche numerische Id in beiden Tabellen erzeugen ist im InMemory nicht garantiert;
        // hier prüfen wir, dass Standard- und Buch-Favorit unabhängig nebeneinander existieren.
        var std = await CreatePuzzleAsync("p-std");
        var book = await CreateBookPuzzleAsync("b-1");
        SetUser(user.Id);

        await _service.AddAsync(user.Id, PuzzleSource.Standard, std.Id);
        await _service.AddAsync(user.Id, PuzzleSource.Book, book.Id);

        Assert.True(await _service.ContainsAsync(user.Id, PuzzleSource.Standard, std.Id));
        Assert.True(await _service.ContainsAsync(user.Id, PuzzleSource.Book, book.Id));
        Assert.Equal(2, await _service.CountAsync(user.Id));
    }

    // ===== Kalkulationsbücher: die Zugfolge ist die Lösung (Codereview 2026-09-29, A9-001) =====

    /// <summary>Buch + Linie; <paramref name="linkById"/> = false legt Altbestand OHNE BookId an
    /// (Zuordnung nur über den Dateinamen).</summary>
    private async Task<BookPuzzle> CreateBookLineAsync(bool isCalculation, bool linkById = true, string file = "calc.pgn")
    {
        var book = new Book { FileName = file, DisplayName = file, IsCalculation = isCalculation, IsPublic = true, Source = new BookSource() };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        var p = new BookPuzzle
        {
            LineId = $"{file}:1", BookFileName = file, BookId = linkById ? book.Id : null,
            Fen = "fen-calc", Moves = "e2e4 e7e5 g1f3", Title = "Woche 1",
        };
        _db.BookPuzzles.Add(p);
        await _db.SaveChangesAsync();
        return p;
    }

    [Fact]
    public async Task Add_CalculationBookLine_ReturnsNotFound()
    {
        var user = await CreateUserAsync("alice");
        var line = await CreateBookLineAsync(isCalculation: true);
        SetUser(user.Id);

        var res = await _controller.Add(new ToggleFavoriteDto { PuzzleId = line.Id, Source = PuzzleSource.Book });

        Assert.IsType<NotFoundObjectResult>(res);
        Assert.False(await _db.FavoritePuzzles.AnyAsync());
    }

    [Fact]
    public async Task Add_LegacyCalculationLineWithoutBookId_ReturnsNotFound()
    {
        var user = await CreateUserAsync("alice");
        var line = await CreateBookLineAsync(isCalculation: true, linkById: false);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.AddAsync(user.Id, PuzzleSource.Book, line.Id));
    }

    [Fact]
    public async Task List_CalculationBookLine_WithholdsMoves_KeepsFen()
    {
        // Favorit von FRÜHER (vor dem Umschalten bzw. vor diesem Fix direkt angelegt): die Liste darf
        // die Lösung trotzdem nicht ausliefern.
        var user = await CreateUserAsync("alice");
        var line = await CreateBookLineAsync(isCalculation: true);
        _db.FavoritePuzzles.Add(new FavoritePuzzle { UserId = user.Id, Source = PuzzleSource.Book, PuzzleId = line.Id });
        await _db.SaveChangesAsync();

        var fav = Assert.Single(await _service.ListAsync(user.Id));

        Assert.Equal(string.Empty, fav.Moves);
        Assert.Equal("fen-calc", fav.Fen);
        Assert.Equal("Woche 1", fav.Title);
    }

    [Fact]
    public async Task List_BookSwitchedToCalculationLater_WithholdsMoves()
    {
        var user = await CreateUserAsync("alice");
        var line = await CreateBookLineAsync(isCalculation: false);
        await _service.AddAsync(user.Id, PuzzleSource.Book, line.Id);
        Assert.Equal("e2e4 e7e5 g1f3", Assert.Single(await _service.ListAsync(user.Id)).Moves);   // normaler Kurs: Züge da

        var book = await _db.Books.SingleAsync();
        book.IsCalculation = true;
        await _db.SaveChangesAsync();

        Assert.Equal(string.Empty, Assert.Single(await _service.ListAsync(user.Id)).Moves);
    }

    [Fact]
    public async Task Add_NormalBookLine_StillWorks_WithMoves()
    {
        var user = await CreateUserAsync("alice");
        var line = await CreateBookLineAsync(isCalculation: false, file: "normal.pgn");
        SetUser(user.Id);

        Assert.True(Favorited(await _controller.Add(new ToggleFavoriteDto { PuzzleId = line.Id, Source = PuzzleSource.Book })));
        Assert.Equal("e2e4 e7e5 g1f3", Assert.Single(await _service.ListAsync(user.Id)).Moves);
    }

    [Fact]
    public void CalculationLines_TranslatesToSqlOnMySql()
    {
        // Korrelierte Unterabfrage (BookId ODER Altbestand über den Dateinamen) muss als SQL laufen,
        // nicht clientseitig — sonst wirft EF auf MariaDB, was InMemory nie zeigt.
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql("server=localhost;database=x;user=x;password=x", DbServerVersion.Current)
            .Options);

        var sql = FavoriteService.CalculationLines(db).Where(p => p.Id == 1).Select(p => p.Id).ToQueryString();

        Assert.Contains("EXISTS", sql);
        Assert.Contains("`IsCalculation`", sql);
    }
}
