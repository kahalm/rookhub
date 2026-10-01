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
                ["Kibana:Url"] = "https://kibana-test.example.com/",
                ["Jwt:Key"] = "TestSecretKeyThatIsAtLeast32Characters!",
                ["Jwt:Issuer"] = "TestIssuer",
                ["Jwt:Audience"] = "TestAudience"
            })
            .Build();
        _controller = new AdminController(
            TestServices.Admin(_db),
            new BookAdminService(_db),
            new PuzzleService(_db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<PuzzleService>.Instance, new PuzzleTaggingService(_db, NullLogger<PuzzleTaggingService>.Instance)),
            new PgnImportService(_db),
            new AuthService(_db, _config, NullLogger<AuthService>.Instance),
            _config,
            new FakeWebHostEnvironment(),
            new NoOpTaskQueue());
        SetUser(99);
    }

    public void Dispose() => _db.Dispose();

    /// <summary>Setzt den aufrufenden Nutzer. <paramref name="isAdmin"/> steuert den Role-Claim: ein echter
    /// Admin trägt ihn immer (AuthService setzt ihn bei <c>IsAdmin</c>), eine delegierte Rolle mit bloßer
    /// <c>users.manage</c>-Permission NICHT — davon hängen Impersonation/Admin-Toggle ab.</summary>
    private void SetUser(int userId, bool isAdmin = true)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId.ToString()) };
        if (isAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
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
    public async Task Impersonate_ReturnsTargetToken()
    {
        var target = await CreateUserAsync("victim");
        SetUser(99); // Admin

        var result = await _controller.Impersonate(target.Id) as OkObjectResult;

        Assert.NotNull(result);
        var dto = Assert.IsType<RookHub.Api.DTOs.AuthResponseDto>(result!.Value);
        Assert.True(dto.Impersonating);
        Assert.Equal(target.Id, dto.UserId);
        Assert.Equal("victim", dto.Username);
        Assert.NotEmpty(dto.Token);
    }

    [Fact]
    public async Task Impersonate_Self_ReturnsBadRequest()
    {
        SetUser(99);
        var result = await _controller.Impersonate(99);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Impersonate_UnknownUser_ReturnsNotFound()
    {
        SetUser(99);
        var result = await _controller.Impersonate(4242);
        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public void GetConfig_ReturnsKibanaDashboardDeepLink_TrimmedSlash()
    {
        var result = _controller.GetConfig() as OkObjectResult;

        Assert.NotNull(result);
        var data = result.Value!;
        var kibanaUrl = (string)data.GetType().GetProperty("kibanaUrl")!.GetValue(data)!;
        // Trailing slash am Root wird gestrippt; Deep-Link zeigt direkt aufs Dev-Dashboard.
        Assert.Equal("https://kibana-test.example.com/app/dashboards#/view/rookhub-dev-dashboard", kibanaUrl);
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
            TestServices.Admin(db),
            new BookAdminService(db),
            new PuzzleService(db, new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()), NullLogger<PuzzleService>.Instance, new PuzzleTaggingService(db, NullLogger<PuzzleTaggingService>.Instance)),
            new PgnImportService(db),
            new AuthService(db, emptyConfig, NullLogger<AuthService>.Instance),
            emptyConfig,
            new FakeWebHostEnvironment(),
            new NoOpTaskQueue());

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
    public async Task DeleteUser_AnonymizesUser_LikeTheSelfDeletion()
    {
        var user = await CreateUserAsync("target");

        var result = await _controller.DeleteUser(user.Id);

        Assert.IsType<NoContentResult>(result);
        // Seit dem Codereview 2026-09-29 (A9-004) derselbe Kern wie die Selbstlöschung: die Zeile bleibt anonymisiert.
        var row = await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.NotNull(row.DeletedAt);
        Assert.Equal($"deleted_{user.Id}", row.Username);
    }

    [Fact]
    public async Task DeleteUser_Self_ReturnsBadRequest()
    {
        var self = await CreateUserAsync("self");
        SetUser(self.Id);

        var result = await _controller.DeleteUser(self.Id);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal("admin_self_delete", ApiErrorCodesTests.CodeOf(result));   // F5-019
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
        Assert.Equal("admin_self_change", ApiErrorCodesTests.CodeOf(result));
    }

    [Fact]
    public async Task ToggleAdmin_NotFound()
    {
        var result = await _controller.ToggleAdmin(9999);

        Assert.IsType<NotFoundResult>(result);
    }

    // ---- Admin-Recht als Soll-Wert (F5-013) ----

    [Fact]
    public async Task SetAdmin_IsIdempotent_ADoubleClickDoesNotTakeTheRightAway()
    {
        var user = await CreateUserAsync("target", isAdmin: false);

        // Zweimal „zum Admin machen" (Doppelklick bzw. zweiter Admin mit veralteter Liste): bleibt Admin.
        Assert.IsType<OkObjectResult>(await _controller.SetAdmin(user.Id, new SetAdminDto { IsAdmin = true }));
        var second = Assert.IsType<OkObjectResult>(await _controller.SetAdmin(user.Id, new SetAdminDto { IsAdmin = true }));
        Assert.True(Assert.IsType<AdminUserDto>(second.Value).IsAdmin);
        Assert.True((await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id)).IsAdmin);

        // Entzug ebenso idempotent; der zweite Aufruf rotiert den Stamp nicht noch einmal.
        Assert.IsType<OkObjectResult>(await _controller.SetAdmin(user.Id, new SetAdminDto { IsAdmin = false }));
        var stamp = (await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id)).SecurityStamp;
        Assert.IsType<OkObjectResult>(await _controller.SetAdmin(user.Id, new SetAdminDto { IsAdmin = false }));
        var after = await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id);
        Assert.False(after.IsAdmin);
        Assert.Equal(stamp, after.SecurityStamp);
    }

    [Fact]
    public async Task SetAdmin_MissingValue_Self_Unknown_AndNonAdminActor_AreRejected()
    {
        var target = await CreateUserAsync("target");
        Assert.IsType<BadRequestObjectResult>(await _controller.SetAdmin(target.Id, new SetAdminDto()));   // {} ist kein Entzug
        Assert.IsType<BadRequestObjectResult>(await _controller.SetAdmin(target.Id, null));
        Assert.IsType<NotFoundResult>(await _controller.SetAdmin(9999, new SetAdminDto { IsAdmin = true }));

        var self = await CreateUserAsync("self");
        SetUser(self.Id);
        Assert.IsType<BadRequestObjectResult>(await _controller.SetAdmin(self.Id, new SetAdminDto { IsAdmin = false }));

        SetUser(99, isAdmin: false);   // users.manage ohne Admin-Rolle
        var status = Assert.IsType<ObjectResult>(await _controller.SetAdmin(target.Id, new SetAdminDto { IsAdmin = true }));
        Assert.Equal(403, status.StatusCode);
        Assert.False((await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == target.Id)).IsAdmin);
    }

    // ---- Sperren (F5-011) ----

    [Fact]
    public async Task LockUser_LocksAndReturnsTheUser_UnlockLiftsIt()
    {
        var user = await CreateUserAsync("target");
        var until = DateTime.UtcNow.AddDays(1);

        var locked = Assert.IsType<OkObjectResult>(await _controller.LockUser(user.Id, new LockUserDto { Until = until }));
        Assert.Equal(until, Assert.IsType<AdminUserDto>(locked.Value).LockedUntil);
        Assert.Equal(until, (await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == user.Id)).LockedUntil);

        var unlocked = Assert.IsType<OkObjectResult>(await _controller.UnlockUser(user.Id));
        Assert.Null(Assert.IsType<AdminUserDto>(unlocked.Value).LockedUntil);
    }

    [Fact]
    public async Task LockUser_Self_ReturnsBadRequest_Unknown_ReturnsNotFound_AdminByNonAdmin_ReturnsForbidden()
    {
        var self = await CreateUserAsync("self");
        SetUser(self.Id);
        Assert.IsType<BadRequestObjectResult>(await _controller.LockUser(self.Id, null));
        Assert.IsType<NotFoundResult>(await _controller.LockUser(9999, null));
        Assert.IsType<NotFoundResult>(await _controller.UnlockUser(9999));

        var admin = await CreateUserAsync("otheradmin", isAdmin: true);
        SetUser(99, isAdmin: false);
        var status = Assert.IsType<ObjectResult>(await _controller.LockUser(admin.Id, null));
        Assert.Equal(403, status.StatusCode);
    }

    // ---- Rechteausweitung über `users.manage` (ohne Admin-Rolle) --------------------------------

    [Fact]
    public async Task ToggleAdmin_ByNonAdminPermissionHolder_ReturnsForbidden()
    {
        // Delegierte Rolle mit users.manage, selbst KEIN Admin: dürfte sich sonst über ein zweites
        // Konto Admin-Rechte verschaffen.
        var target = await CreateUserAsync("target");
        SetUser(99, isAdmin: false);

        var result = await _controller.ToggleAdmin(target.Id);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
        var unchanged = await _db.AppUsers.FindAsync(target.Id);
        Assert.False(unchanged!.IsAdmin);
    }

    [Fact]
    public async Task DeleteUser_AdminTarget_ByNonAdminPermissionHolder_ReturnsForbidden_OthersStayDeletable()
    {
        // Gleiche Grenze wie Toggle/Sperre/Impersonation (N9-002): sonst löschte eine delegierte Rolle die Admins weg.
        var admin = await CreateUserAsync("realadmin", isAdmin: true);
        var normal = await CreateUserAsync("normaluser");
        SetUser(99, isAdmin: false);

        var status = Assert.IsType<ObjectResult>(await _controller.DeleteUser(admin.Id));
        Assert.Equal(403, status.StatusCode);
        Assert.Null((await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == admin.Id)).DeletedAt);
        Assert.IsType<NoContentResult>(await _controller.DeleteUser(normal.Id));   // Support-Fall bleibt

        SetUser(99, isAdmin: true);
        Assert.IsType<NoContentResult>(await _controller.DeleteUser(admin.Id));    // ein echter Admin darf
        Assert.NotNull((await _db.AppUsers.AsNoTracking().SingleAsync(u => u.Id == admin.Id)).DeletedAt);
    }

    [Fact]
    public async Task Impersonate_AdminTarget_ByNonAdminPermissionHolder_ReturnsForbidden()
    {
        // Das Impersonations-Token trägt die Rollen des ZIELS → Einstieg in ein Admin-Konto wäre
        // eine vollständige Rechteausweitung.
        var admin = await CreateUserAsync("realadmin", isAdmin: true);
        SetUser(99, isAdmin: false);

        var result = await _controller.Impersonate(admin.Id);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public async Task Impersonate_NonAdminTarget_ByNonAdminPermissionHolder_IsAllowed()
    {
        // Support-Fall bleibt erlaubt: nur Admin-ZIELE sind geschützt.
        var target = await CreateUserAsync("normaluser");
        SetUser(99, isAdmin: false);

        var result = await _controller.Impersonate(target.Id) as OkObjectResult;

        Assert.NotNull(result);
        var dto = Assert.IsType<AuthResponseDto>(result!.Value);
        Assert.Equal(target.Id, dto.UserId);
    }

    [Fact]
    public async Task Impersonate_AdminTarget_ByRealAdmin_IsAllowed()
    {
        var admin = await CreateUserAsync("otheradmin", isAdmin: true);
        SetUser(99, isAdmin: true);

        var result = await _controller.Impersonate(admin.Id) as OkObjectResult;

        Assert.NotNull(result);
        var dto = Assert.IsType<AuthResponseDto>(result!.Value);
        Assert.Equal(admin.Id, dto.UserId);
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
        var book = new Book { FileName = "b.pgn", DisplayName = "b", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
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
    public async Task GetBooks_NamesTheOwnerOfPrivateCopies_GlobalBooksHaveNone()
    {
        // N9-010: zwei Nutzer haben denselben Chessable-Kurs importiert — die Liste muss die Kopien unterscheiden.
        var u1 = await CreateUserAsync("alice");
        var u2 = await CreateUserAsync("bob");
        Book B(string file, int? owner) => new() { FileName = file, DisplayName = "Lifetime Repertoires", OwnerUserId = owner,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
        _db.Books.AddRange(B("global.pgn", null), B($"chessable-u{u1.Id}-1.pgn", u1.Id), B($"chessable-u{u2.Id}-1.pgn", u2.Id));
        await _db.SaveChangesAsync();

        var books = Assert.IsType<List<RookHub.Api.DTOs.BookDto>>(Assert.IsType<OkObjectResult>(await _controller.GetBooks()).Value);

        var global = books.Single(b => b.FileName == "global.pgn");
        Assert.Null(global.OwnerUserId);
        Assert.Null(global.OwnerName);
        Assert.Equal("alice", books.Single(b => b.OwnerUserId == u1.Id).OwnerName);
        Assert.Equal("bob", books.Single(b => b.OwnerUserId == u2.Id).OwnerName);
    }

    [Fact]
    public async Task UpdateBook_TogglesFlags()
    {
        var book = new Book { FileName = "b.pgn", DisplayName = "b", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
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
    public async Task UpdateBook_SetsKind()
    {
        var book = new Book { FileName = "b.pgn", DisplayName = "b", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();
        Assert.Equal(BookKind.Puzzle, book.Kind); // Default

        var result = await _controller.UpdateBook(book.Id, new RookHub.Api.DTOs.UpdateBookDto { Kind = BookKind.Study }) as OkObjectResult;

        Assert.NotNull(result);
        var dto = Assert.IsType<RookHub.Api.DTOs.BookDto>(result!.Value);
        Assert.Equal(BookKind.Study, dto.Kind);
        var updated = await _db.Books.FindAsync(book.Id);
        Assert.Equal(BookKind.Study, updated!.Kind);
    }

    [Fact]
    public async Task UpdateBook_WithoutElo_KeepsTheRange_ZeroClearsIt()
    {
        // Codereview N9-009: MinElo/MaxElo wurden als einzige Felder immer gesetzt — ein Teil-Update
        // ohne sie ({isPublic:true}, Umbenennen) leerte die Elo-Spanne still.
        var book = new Book { FileName = "b.pgn", DisplayName = "b", MinElo = 1200, MaxElo = 1600, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
        _db.Books.Add(book);
        await _db.SaveChangesAsync();

        await _controller.UpdateBook(book.Id, new RookHub.Api.DTOs.UpdateBookDto { IsPublic = true, DisplayName = "Neu" });
        var updated = await _db.Books.FindAsync(book.Id);
        Assert.Equal(1200, updated!.MinElo);
        Assert.Equal(1600, updated.MaxElo);

        // Eine Grenze einzeln ändern lässt die andere stehen, 0 entfernt sie.
        await _controller.UpdateBook(book.Id, new RookHub.Api.DTOs.UpdateBookDto { MaxElo = 1800 });
        Assert.Equal(1200, updated.MinElo);
        Assert.Equal(1800, updated.MaxElo);
        await _controller.UpdateBook(book.Id, new RookHub.Api.DTOs.UpdateBookDto { MinElo = 0 });
        Assert.Null(updated.MinElo);
        Assert.Equal(1800, updated.MaxElo);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(3500, true)]
    [InlineData(3501, false)]
    public void UpdateBookDto_EloRange_IsValidated(int elo, bool valid)
    {
        foreach (var dto in new[] { new RookHub.Api.DTOs.UpdateBookDto { MinElo = elo }, new RookHub.Api.DTOs.UpdateBookDto { MaxElo = elo } })
        {
            var ok = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                dto, new System.ComponentModel.DataAnnotations.ValidationContext(dto), null, validateAllProperties: true);
            Assert.Equal(valid, ok);
        }
    }

    [Fact]
    public async Task ImportBooks_DefaultsKindToPuzzle()
    {
        var file = MakePgnFile("sample.pgn", SamplePgn);

        await _controller.ImportBooks(new List<Microsoft.AspNetCore.Http.IFormFile> { file }, default);

        var book = await _db.Books.FirstAsync();
        Assert.Equal(BookKind.Puzzle, book.Kind);
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
        var book = new Book { FileName = "b.pgn", DisplayName = "b", CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, Source = new BookSource() };
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
