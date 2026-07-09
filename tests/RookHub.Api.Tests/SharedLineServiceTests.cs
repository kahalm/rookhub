using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

public class SharedLineServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SharedLineService _svc;

    public SharedLineServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _svc = new SharedLineService(_db);
    }

    public void Dispose() => _db.Dispose();

    private const string Pgn = "[Event \"Repertoire line\"]\n[White \"?\"]\n[Black \"Najdorf\"]\n\n1. e4 c5 2. Nf3 d6 {solid} *\n";

    private async Task<int> AddUserAsync(string name)
    {
        var u = new AppUser { Username = name, Email = name + "@x.y", PasswordHash = "h" };
        _db.AppUsers.Add(u);
        await _db.SaveChangesAsync();
        return u.Id;
    }

    private async Task<int> AddRepertoireAsync(int ownerId, string name = "My Rep")
    {
        var rep = new Repertoire { UserId = ownerId, Name = name, Kind = RepertoireKind.Opening };
        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();
        return rep.Id;
    }

    [Fact]
    public async Task Owner_Creates_And_GetByToken_ReturnsSnapshot()
    {
        var owner = await AddUserAsync("owner");
        var repId = await AddRepertoireAsync(owner);

        var res = await _svc.CreateAsync(owner, repId, new ShareLineInputDto { Pgn = Pgn, Title = "Sicilian Najdorf" });
        Assert.NotNull(res);
        Assert.False(string.IsNullOrWhiteSpace(res!.ShareToken));

        var dto = await _svc.GetByTokenAsync(res.ShareToken);
        Assert.NotNull(dto);
        Assert.Equal("Sicilian Najdorf", dto!.Title);
        Assert.Equal("My Rep", dto.RepertoireName);
        Assert.Contains("Najdorf", dto.Pgn);
    }

    [Fact]
    public async Task SameLine_SharedTwice_ReturnsSameToken()
    {
        var owner = await AddUserAsync("owner");
        var repId = await AddRepertoireAsync(owner);

        var first = await _svc.CreateAsync(owner, repId, new ShareLineInputDto { Pgn = Pgn });
        var second = await _svc.CreateAsync(owner, repId, new ShareLineInputDto { Pgn = Pgn, Title = "changed title" });

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first!.ShareToken, second!.ShareToken);
        Assert.Single(_db.SharedLines);
    }

    [Fact]
    public async Task NonOwner_WithoutShare_GetsNull()
    {
        var owner = await AddUserAsync("owner");
        var stranger = await AddUserAsync("stranger");
        var repId = await AddRepertoireAsync(owner);

        var res = await _svc.CreateAsync(stranger, repId, new ShareLineInputDto { Pgn = Pgn });
        Assert.Null(res);
    }

    [Fact]
    public async Task ShareRecipient_CanShareLine()
    {
        var owner = await AddUserAsync("owner");
        var friend = await AddUserAsync("friend");
        var repId = await AddRepertoireAsync(owner);
        _db.RepertoireShares.Add(new RepertoireShare { RepertoireId = repId, OwnerId = owner, RecipientId = friend });
        await _db.SaveChangesAsync();

        var res = await _svc.CreateAsync(friend, repId, new ShareLineInputDto { Pgn = Pgn });
        Assert.NotNull(res);
    }

    [Fact]
    public async Task InvalidPgn_And_UnknownToken_ReturnNull()
    {
        var owner = await AddUserAsync("owner");
        var repId = await AddRepertoireAsync(owner);

        Assert.Null(await _svc.CreateAsync(owner, repId, new ShareLineInputDto { Pgn = "   " }));
        Assert.Null(await _svc.CreateAsync(owner, repId, new ShareLineInputDto { Pgn = "not a pgn at all" }));
        Assert.Null(await _svc.GetByTokenAsync("does-not-exist"));
    }

    [Fact]
    public async Task UnknownRepertoire_ReturnsNull()
    {
        var owner = await AddUserAsync("owner");
        Assert.Null(await _svc.CreateAsync(owner, 9999, new ShareLineInputDto { Pgn = Pgn }));
    }

    [Fact]
    public async Task Standalone_BuildsPgnFromMoves_DedupsAndRejectsEmpty()
    {
        var user = await AddUserAsync("ext");

        // Leere Zugliste → null.
        Assert.Null(await _svc.CreateStandaloneAsync(user, new List<string>(), "x"));
        Assert.Null(await _svc.CreateStandaloneAsync(user, new List<string> { "", "  " }, "x"));

        var res = await _svc.CreateStandaloneAsync(user, new List<string> { "e4", "c5", "Nf3" }, "Sicilian");
        Assert.NotNull(res);

        var dto = await _svc.GetByTokenAsync(res!.ShareToken);
        Assert.NotNull(dto);
        Assert.Null(dto!.RepertoireName);                 // freistehend, kein Repertoire
        Assert.Contains("1. e4 c5 2. Nf3", dto.Pgn);       // Zugnummern korrekt
        Assert.Contains("[Event \"Sicilian\"]", dto.Pgn);

        // Dieselbe Zugfolge erneut → derselbe Link.
        var again = await _svc.CreateStandaloneAsync(user, new List<string> { "e4", "c5", "Nf3" }, "other title");
        Assert.Equal(res.ShareToken, again!.ShareToken);
        Assert.Single(_db.SharedLines);
    }
}
