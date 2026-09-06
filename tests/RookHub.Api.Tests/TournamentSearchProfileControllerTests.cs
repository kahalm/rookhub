using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Controllers;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Tests;

/// <summary>
/// Gespeicherte Umkreise. Sie tragen den Wohnort des Nutzers - jede Abfrage muss deshalb auf den
/// eigenen Datensatz eingezaeunt sein, und ein fremdes Profil darf nicht einmal als "existiert"
/// erkennbar sein.
/// </summary>
public class TournamentSearchProfileControllerTests : IDisposable
{
    private readonly AppDbContext _db;

    public TournamentSearchProfileControllerTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    }

    public void Dispose() => _db.Dispose();

    private TournamentSearchProfileController CreateController(int userId)
    {
        var controller = new TournamentSearchProfileController(
            _db, new TestLogger<TournamentSearchProfileController>());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test"))
            }
        };
        return controller;
    }

    private async Task<int> CreateUserAsync(string username)
    {
        var user = new AppUser { Username = username, PasswordHash = "x", Email = $"{username}@example.com" };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private static SearchProfileInputDto Input(string name = "Zuhause") => new()
    {
        Name = name, PlaceQuery = "5020 Salzburg", Lat = 47.80, Lon = 13.04, RadiusKm = 100,
        Speeds = ["Standard", "Rapid"], Federations = ["aut", "ger"], NotifyNew = true,
    };

    [Fact]
    public async Task Create_StoresNormalizedFiltersAndReturnsThem()
    {
        var userId = await CreateUserAsync("a");

        var result = await CreateController(userId).Create(Input(), default);
        var dto = Assert.IsType<DirectorySearchProfileDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("Zuhause", dto.Name);
        Assert.Equal(100, dto.RadiusKm);
        Assert.Equal(["AUT", "GER"], dto.Federations);
        Assert.Equal(["Standard", "Rapid"], dto.Speeds);

        var stored = await _db.TournamentSearchProfiles.SingleAsync();
        Assert.Equal(userId, stored.UserId);
        Assert.Equal("AUT,GER", stored.Federations);
    }

    [Fact]
    public async Task GetAll_ShowsOnlyOwnProfiles()
    {
        var mine = await CreateUserAsync("mine");
        var other = await CreateUserAsync("other");
        await CreateController(mine).Create(Input("Meins"), default);
        await CreateController(other).Create(Input("Fremdes"), default);

        var result = await CreateController(mine).GetAll(default);
        var list = Assert.IsType<List<DirectorySearchProfileDto>>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal("Meins", Assert.Single(list).Name);
    }

    [Fact]
    public async Task Update_ForeignProfile_Is404_NotForbidden()
    {
        var mine = await CreateUserAsync("mine");
        var other = await CreateUserAsync("other");
        await CreateController(other).Create(Input("Fremdes"), default);
        var foreignId = (await _db.TournamentSearchProfiles.SingleAsync()).Id;

        var result = await CreateController(mine).Update(foreignId, Input("Gekapert"), default);

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal("Fremdes", (await _db.TournamentSearchProfiles.SingleAsync()).Name);
    }

    [Fact]
    public async Task Delete_ForeignProfile_Is404_AndLeavesItAlone()
    {
        var mine = await CreateUserAsync("mine");
        var other = await CreateUserAsync("other");
        await CreateController(other).Create(Input("Fremdes"), default);
        var foreignId = (await _db.TournamentSearchProfiles.SingleAsync()).Id;

        Assert.IsType<NotFoundResult>(await CreateController(mine).Delete(foreignId, default));
        Assert.Equal(1, await _db.TournamentSearchProfiles.CountAsync());
    }

    [Fact]
    public async Task Delete_OwnProfile_Removes()
    {
        var userId = await CreateUserAsync("a");
        await CreateController(userId).Create(Input(), default);
        var id = (await _db.TournamentSearchProfiles.SingleAsync()).Id;

        Assert.IsType<NoContentResult>(await CreateController(userId).Delete(id, default));
        Assert.Equal(0, await _db.TournamentSearchProfiles.CountAsync());
    }

    [Fact]
    public async Task Update_ChangesRadiusAndNotifyFlag()
    {
        var userId = await CreateUserAsync("a");
        await CreateController(userId).Create(Input(), default);
        var id = (await _db.TournamentSearchProfiles.SingleAsync()).Id;

        var changed = Input();
        changed.RadiusKm = 25;
        changed.NotifyNew = false;

        var result = await CreateController(userId).Update(id, changed, default);
        var dto = Assert.IsType<DirectorySearchProfileDto>(Assert.IsType<OkObjectResult>(result.Result).Value);

        Assert.Equal(25, dto.RadiusKm);
        Assert.False(dto.NotifyNew);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutName_IsRejected(string name)
    {
        var userId = await CreateUserAsync("a");
        var input = Input();
        input.Name = name;

        Assert.IsType<BadRequestObjectResult>((await CreateController(userId).Create(input, default)).Result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(5000)]
    public async Task Create_AbsurdRadius_IsRejected(int radius)
    {
        var userId = await CreateUserAsync("a");
        var input = Input();
        input.RadiusKm = radius;

        Assert.IsType<BadRequestObjectResult>((await CreateController(userId).Create(input, default)).Result);
    }

    [Fact]
    public async Task Create_CoordinatesOutOfRange_AreRejected()
    {
        var userId = await CreateUserAsync("a");
        var input = Input();
        input.Lat = 120;

        Assert.IsType<BadRequestObjectResult>((await CreateController(userId).Create(input, default)).Result);
    }

    [Fact]
    public async Task Create_UnknownSpeed_IsRejected()
    {
        var userId = await CreateUserAsync("a");
        var input = Input();
        input.Speeds = ["Bullet"];

        Assert.IsType<BadRequestObjectResult>((await CreateController(userId).Create(input, default)).Result);
    }

    [Fact]
    public async Task Create_MalformedFederation_IsRejected()
    {
        var userId = await CreateUserAsync("a");
        var input = Input();
        input.Federations = ["Austria"];

        Assert.IsType<BadRequestObjectResult>((await CreateController(userId).Create(input, default)).Result);
    }

    [Fact]
    public async Task Create_BeyondTheProfileLimit_IsRejected()
    {
        var userId = await CreateUserAsync("a");
        for (var i = 0; i < 20; i++)
            await CreateController(userId).Create(Input($"Profil {i}"), default);

        var result = await CreateController(userId).Create(Input("Eins zu viel"), default);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal(20, await _db.TournamentSearchProfiles.CountAsync());
    }

    [Fact]
    public async Task Create_EmptyFilterLists_AreStoredAsNull_NotAsAnEmptyString()
    {
        // Ein leerer String wuerde in SplitCsv zwar auch leer herauskommen, aber die Absicht
        // "keine Einschraenkung" liest sich nur an NULL sauber ab.
        var userId = await CreateUserAsync("a");
        var input = Input();
        input.Speeds = [];
        input.Federations = null;

        await CreateController(userId).Create(input, default);

        var stored = await _db.TournamentSearchProfiles.SingleAsync();
        Assert.Null(stored.Speeds);
        Assert.Null(stored.Federations);
    }

    [Fact]
    public async Task Create_WithAbsurdlyManyFederations_IsRejectedInsteadOfCrashing()
    {
        // Die Liste landet als CSV in einer varchar(200)-Spalte. Ohne Anzahl-Pruefung waere das
        // eine DbUpdateException — die der Unique-Filter nicht faengt, also ein 500er, den jeder
        // Angemeldete ausloesen kann.
        var userId = await CreateUserAsync("viele");
        var input = Input("Zu viele");
        input.Federations = Enumerable.Range(0, 80).Select(i => $"A{(char)('A' + i % 26)}{(char)('A' + i / 26)}").ToList();

        var result = await CreateController(userId).Create(input, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Empty(_db.TournamentSearchProfiles);
    }
}
