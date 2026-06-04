using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class AdminControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AdminController _controller;
    private readonly IConfigurationRoot _config;

    public AdminControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kibana:Url"] = "https://kibana-test.example.com/"
            })
            .Build();
        _controller = new AdminController(
            new AdminService(_db),
            new BookAdminService(_db),
            new PuzzleService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<PuzzleService>.Instance),
            new PgnImportService(_db),
            _config);
        SetUser(99);
    }

    public void Dispose() => _db.Dispose();

    private void SetUser(int userId)
    {
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
            }
        };
    }

    private async Task<AppUser> CreateUserAsync(string username, bool isAdmin = false)
    {
        var user = new AppUser
        {
            Username = username,
            Email = $"{username}@test.com",
            PasswordHash = "hash",
            IsAdmin = isAdmin
        };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public void GetConfig_ReturnsKibanaDashboardDeepLink_TrimmedSlash()
    {
        var result = _controller.GetConfig() as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var kibanaUrl = (string)data.GetType().GetProperty("kibanaUrl")!.GetValue(data)!;
        // Trailing slash am Root wird gestrippt; Deep-Link zeigt direkt aufs RookHub-Logging-Dashboard.
        Assert.Equal("https://kibana-test.example.com/app/dashboards#/view/rookhub-logging-dashboard", kibanaUrl);
    }

    [Fact]
    public void GetConfig_ReturnsEmptyString_WhenKibanaUrlMissing()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var db = new AppDbContext(options);
        var emptyConfig = new ConfigurationBuilder().Build();
        var ctrl = new AdminController(
            new AdminService(db),
            new BookAdminService(db),
            new PuzzleService(db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<PuzzleService>.Instance),
            new PgnImportService(db),
            emptyConfig);

        var result = ctrl.GetConfig() as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var kibanaUrl = (string)data.GetType().GetProperty("kibanaUrl")!.GetValue(data)!;
        Assert.Equal(string.Empty, kibanaUrl);
    }

    [Fact]
    public async Task GetUsers_ReturnsAllUsers()
    {
        await CreateUserAsync("alice");
        await CreateUserAsync("bob");

        var result = await _controller.GetUsers(null, 1, 20) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var totalCount = (int)data.GetType().GetProperty("totalCount")!.GetValue(data)!;
        Assert.Equal(2, totalCount);
    }

    [Fact]
    public async Task GetUsers_IncludesGroupNames()
    {
        var alice = await CreateUserAsync("alice");
        await CreateUserAsync("bob");
        var g1 = new Group { Name = "Trainees", CreatedAt = DateTime.UtcNow };
        var g2 = new Group { Name = "Coaches", CreatedAt = DateTime.UtcNow };
        _db.Groups.AddRange(g1, g2);
        await _db.SaveChangesAsync();
        _db.UserGroups.AddRange(
            new UserGroup { UserId = alice.Id, GroupId = g1.Id },
            new UserGroup { UserId = alice.Id, GroupId = g2.Id });
        await _db.SaveChangesAsync();

        var result = await _controller.GetUsers(null, 1, 20) as OkObjectResult;
        var data = result!.Value!;
        var items = (System.Collections.IEnumerable)data.GetType().GetProperty("items")!.GetValue(data)!;
        var aliceDto = items.Cast<AdminUserDto>().Single(u => u.Username == "alice");
        var bobDto = items.Cast<AdminUserDto>().Single(u => u.Username == "bob");

        Assert.Equal(new[] { "Coaches", "Trainees" }, aliceDto.Groups);   // alphabetisch
        Assert.Empty(bobDto.Groups);
    }

    [Fact]
    public async Task GetUsers_SearchFilter_ReturnsMatching()
    {
        await CreateUserAsync("alice");
        await CreateUserAsync("bob");

        var result = await _controller.GetUsers("ali", 1, 20) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var totalCount = (int)data.GetType().GetProperty("totalCount")!.GetValue(data)!;
        Assert.Equal(1, totalCount);
    }

    [Fact]
    public async Task GetUsers_Pagination()
    {
        await CreateUserAsync("user1");
        await CreateUserAsync("user2");
        await CreateUserAsync("user3");

        var result = await _controller.GetUsers(null, 2, 2) as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var items = data.GetType().GetProperty("items")!.GetValue(data) as System.Collections.IList;
        Assert.Single(items!);
    }

    [Fact]
    public async Task DeleteUser_RemovesUser()
    {
        var user = await CreateUserAsync("target");

        var result = await _controller.DeleteUser(user.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _db.AppUsers.FindAsync(user.Id));
    }

    [Fact]
    public async Task DeleteUser_Self_ReturnsBadRequest()
    {
        var self = await CreateUserAsync("self");
        SetUser(self.Id);

        var result = await _controller.DeleteUser(self.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task DeleteUser_NotFound()
    {
        var result = await _controller.DeleteUser(9999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task ToggleAdmin_TogglesFlag()
    {
        var user = await CreateUserAsync("target", isAdmin: false);

        var result = await _controller.ToggleAdmin(user.Id) as OkObjectResult;

        Assert.NotNull(result);
        var updated = await _db.AppUsers.FindAsync(user.Id);
        Assert.True(updated!.IsAdmin);
    }

    [Fact]
    public async Task ToggleAdmin_Self_ReturnsBadRequest()
    {
        var self = await CreateUserAsync("self");
        SetUser(self.Id);

        var result = await _controller.ToggleAdmin(self.Id);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ToggleAdmin_NotFound()
    {
        var result = await _controller.ToggleAdmin(9999);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---- Book management -------------------------------------------------

    private static Microsoft.AspNetCore.Http.IFormFile MakePgnFile(string name, string content)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "files", name)
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/x-chess-pgn"
        };
    }

    private const string SamplePgn = @"
[Event ""Sample""]
[Round ""1.1""]
[White ""Mate idea""]
[Black ""Chapter 1""]
[FEN ""rnbqkbnr/pppp1ppp/8/4p3/4P3/8/PPPP1PPP/RNBQKBNR w KQkq - 0 2""]

{ [%tqu ""En"",""hint""] Entwickle. } 2. Nf3 Nc6 3. Bb5 a6 *
";

    [Fact]
    public async Task ImportBooks_CreatesBookAndPuzzles()
    {
        var file = MakePgnFile("sample.pgn", SamplePgn);

        var result = await _controller.ImportBooks(new List<Microsoft.AspNetCore.Http.IFormFile> { file }, default) as OkObjectResult;

        Assert.NotNull(result);
        var dto = Assert.IsType<RookHub.Api.DTOs.BookImportResultDto>(result.Value);
        Assert.Equal(1, dto.TotalImported);
        Assert.Single(dto.Books);
        Assert.Equal(1, await _db.Books.CountAsync());
        Assert.Equal(1, await _db.BookPuzzles.CountAsync());
        var book = await _db.Books.FirstAsync();
        Assert.Equal("sample.pgn", book.FileName);
        Assert.Equal("sample", book.DisplayName);
    }

    [Fact]
    public async Task ImportBooks_NoFiles_ReturnsBadRequest()
    {
        var result = await _controller.ImportBooks(new List<Microsoft.AspNetCore.Http.IFormFile>(), default);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GetBooks_ReturnsBooksWithCounts()
    {
        var book = new Book { FileName = "b.pgn", DisplayName = "b", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        _db.BookPuzzles.AddRange(
            new BookPuzzle { LineId = "b.pgn:1", BookFileName = "b.pgn", BookId = book.Id, Round = "1", Fen = "f", Moves = "e2e4" },
            new BookPuzzle { LineId = "b.pgn:2", BookFileName = "b.pgn", BookId = book.Id, Round = "2", Fen = "f", Moves = "e2e4" });
        await _db.SaveChangesAsync();

        var result = await _controller.GetBooks() as OkObjectResult;

        Assert.NotNull(result);
        var books = Assert.IsType<List<RookHub.Api.DTOs.BookDto>>(result.Value);
        var dto = Assert.Single(books);
        Assert.Equal(2, dto.PuzzleCount);
    }

    [Fact]
    public async Task UpdateBook_TogglesFlags()
    {
        var book = new Book { FileName = "b.pgn", DisplayName = "b", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        var result = await _controller.UpdateBook(book.Id, new RookHub.Api.DTOs.UpdateBookDto
        {
            ForDaily = true,
            ForRandom = true,
            Rating = 5,
            MinElo = 1200,
            MaxElo = 1600
        }) as OkObjectResult;

        Assert.NotNull(result);
        var updated = await _db.Books.FindAsync(book.Id);
        Assert.True(updated!.ForDaily);
        Assert.True(updated.ForRandom);
        Assert.False(updated.ForBlind);
        Assert.Equal(5, updated.Rating);
        Assert.Equal(1200, updated.MinElo);
        Assert.Equal(1600, updated.MaxElo);
    }

    [Fact]
    public async Task UpdateBook_NotFound()
    {
        var result = await _controller.UpdateBook(9999, new RookHub.Api.DTOs.UpdateBookDto { ForDaily = true });

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task DeleteBook_RemovesBookAndPuzzles()
    {
        var book = new Book { FileName = "b.pgn", DisplayName = "b", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        _db.BookPuzzles.Add(new BookPuzzle { LineId = "b.pgn:1", BookFileName = "b.pgn", BookId = book.Id, Round = "1", Fen = "f", Moves = "e2e4" });
        await _db.SaveChangesAsync();

        var result = await _controller.DeleteBook(book.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _db.Books.FindAsync(book.Id));
        Assert.Equal(0, await _db.BookPuzzles.CountAsync());
    }
}
