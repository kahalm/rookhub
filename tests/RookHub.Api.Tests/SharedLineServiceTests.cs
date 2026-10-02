using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Exceptions;
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
        Assert.Matches(ShareTokensTests.Format, res.ShareToken);

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
    public async Task ShareRecipient_CanNoLongerCreatePublicLink()
    {
        // VERHALTENSÄNDERUNG (bewusst, siehe SharedLineService.CreateAsync): der Freigabe-Empfänger
        // darf den Repertoire-Inhalt nicht mehr öffentlich machen — das entscheidet nur der Besitzer.
        var owner = await AddUserAsync("owner");
        var friend = await AddUserAsync("friend");
        var repId = await AddRepertoireAsync(owner);
        _db.RepertoireShares.Add(new RepertoireShare { RepertoireId = repId, OwnerId = owner, RecipientId = friend });
        await _db.SaveChangesAsync();

        var res = await _svc.CreateAsync(friend, repId, new ShareLineInputDto { Pgn = Pgn });
        Assert.Null(res);
        Assert.Empty(_db.SharedLines);
    }

    [Fact]
    public async Task Owner_PgnOverCap_Throws400_AndStoresNothing()
    {
        var owner = await AddUserAsync("owner");
        var repId = await AddRepertoireAsync(owner);
        // Übergroßes „PGN": ohne Cap landete jeder beliebig große Text als öffentlicher LONGTEXT-Blob.
        var huge = "[Event \"x\"]\n\n1. e4 e5 " + new string('y', SharedLineService.MaxPgnChars) + " *";

        await Assert.ThrowsAsync<DomainValidationException>(
            () => _svc.CreateAsync(owner, repId, new ShareLineInputDto { Pgn = huge }));
        Assert.Empty(_db.SharedLines);
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

    /// <summary>Golden: die freistehende Linie wird SERVERSEITIG gebaut und steht danach hinter
    /// einem öffentlichen Link — der Test hält sie deshalb zeichengenau fest (die übrigen Tests
    /// prüfen nur Teilzeichenketten). Enthalten: der escapte Titel im Event-Header, die Zugnummern
    /// ab der Grundstellung, das Ergebnis mit genau einem Leerzeichen davor und der abschliessende
    /// Zeilenumbruch.</summary>
    [Fact]
    public async Task Standalone_Golden_PgnIsBuiltCharacterForCharacter()
    {
        var user = await AddUserAsync("golden");

        var res = await _svc.CreateStandaloneAsync(user, new List<string> { "e4", "c5", "Nf3", "d6" }, "Si\"ci\\lian");
        var dto = await _svc.GetByTokenAsync(res!.ShareToken);

        Assert.Equal(
            "[Event \"Si\\\"ci\\\\lian\"]\n[White \"?\"]\n[Black \"?\"]\n[Result \"*\"]\n\n1. e4 c5 2. Nf3 d6 *\n",
            dto!.Pgn);
    }

    // ── N8-003: der Deckel gilt auch für den Erweiterungs-Weg ───────────────────────────────────

    /// <summary>Vorher ging jeder Zugtext roh ins PGN: ~15 MB beliebiger Text je Aufruf wurden ein anonym abrufbarer,
    /// nicht löschbarer /l/-Link. Die Meldung nennt den Riesenzug nicht (sie landet im 400-Rumpf).</summary>
    [Fact]
    public async Task Standalone_HugeMoveText_IsRejected_AndNothingIsStored()
    {
        var user = await AddUserAsync("blob");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.CreateStandaloneAsync(user, new List<string> { "e4", new string('x', 15_000_000) }, "x"));

        Assert.True(ex.Message.Length < 100);
        Assert.Empty(_db.SharedLines);
    }

    [Theory]
    [InlineData("}[Evil \"x\"]{")]   // Kopfzeile ins angezeigte PGN schleusen
    [InlineData("e4\n[Evil")]
    [InlineData("Nf6")]                // legal geschrieben, aber nicht in dieser Stellung (Weiß am Zug)
    [InlineData("hello")]
    public async Task Standalone_IllegalOrForeignMove_IsRejected(string second)
    {
        var user = await AddUserAsync("inject");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _svc.CreateStandaloneAsync(user, new List<string> { "e4", "e5", second }, "x"));

        Assert.Empty(_db.SharedLines);
    }

    /// <summary>Geschrieben wird die Schreibweise des Bretts (Figurinen, Null-Rochade, fehlendes Schach) — und dieselbe
    /// Linie in anderer Schreibweise ist derselbe Link.</summary>
    [Fact]
    public async Task Standalone_WritesBoardSan_AndDedupsAcrossNotations()
    {
        var user = await AddUserAsync("notation");

        var res = await _svc.CreateStandaloneAsync(user,
            new List<string> { "e4", "e5", "♘f3", "Nc6", "Bc4", "Nf6", "Ng5", "d5", "exd5", "Nxd5", "Nxf7", "Kxf7", "Qf3", "Ke6", "Nc3", "Nb4", "0-0" }, "Fegatello");
        var dto = await _svc.GetByTokenAsync(res!.ShareToken);

        Assert.Contains("\n1. e4 e5 2. Nf3 Nc6 3. Bc4 Nf6 4. Ng5 d5 5. exd5 Nxd5 6. Nxf7 Kxf7 7. Qf3+ Ke6 8. Nc3 Nb4 9. O-O *\n", dto!.Pgn);
        var again = await _svc.CreateStandaloneAsync(user,
            new List<string> { "e4", "e5", "Nf3", "Nc6", "Bc4", "Nf6", "Ng5", "d5", "exd5", "Nxd5", "Nxf7", "Kxf7", "Qf3+", "Ke6", "Nc3", "Nb4", "O-O" }, "x");
        Assert.Equal(res.ShareToken, again!.ShareToken);
        Assert.Single(_db.SharedLines);
    }
}
