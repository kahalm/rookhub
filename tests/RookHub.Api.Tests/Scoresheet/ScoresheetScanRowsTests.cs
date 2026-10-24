using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Scoresheet;
using RookHub.Api.Services.Social;

namespace RookHub.Api.Tests.Scoresheet;

/// <summary>
/// Charakterisierung der reinen Abbildungen der Einlesung (Zerlegung Z6): Kommentare im PGN, Schlüssel je Anschluss und
/// Zeile → DTO. Die erwarteten Werte sind die Ausgabe des Codes VOR dem Umbau — sie dürfen sich nicht ändern.
/// </summary>
public class ScoresheetScanRowsTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ScoresheetScanService _service;

    public ScoresheetScanRowsTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        _db = new AppDbContext(options);
        _service = new ScoresheetScanService(_db, new NoVision(), TestServices.SavedGames(_db), new NotificationService(_db),
            NullLogger<ScoresheetScanService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private sealed class NoVision : IScoresheetVisionClient
    {
        public bool IsConfigured => false;
        public string Model => "none";
        public Task<ScoresheetVisionResult> ReadAsync(byte[] jpeg, string instructions, int maxTokens,
            CancellationToken ct = default, ScoresheetReadMode mode = ScoresheetReadMode.Full) => throw new NotSupportedException();
    }

    private static ScoresheetPly Ply(string san, string match, string written = "", bool uncertain = false, bool confirmed = false)
        => new() { San = san, Match = match, Written = written, Uncertain = uncertain, Confirmed = confirmed };

    [Fact]
    public void CommentsFor_MixedResolution_ExactComments()
    {
        var r = new ScoresheetResolution
        {
            Plies =
            {
                Ply("e4", ScoresheetResolver.Matches.Exact, "e4"),
                Ply("e5", ScoresheetResolver.Matches.Fuzzy, "e6"),
                Ply("Nf3", ScoresheetResolver.Matches.Guess, " "),
                Ply("Nc6", ScoresheetResolver.Matches.Inserted),
                Ply("Bb5", ScoresheetResolver.Matches.Exact, "Bb5"),
            },
            Skipped =
            {
                new ScoresheetSkip { W = 3, Written = "Nc6", AfterPly = 2 },
                new ScoresheetSkip { W = 9, Written = "zz", AfterPly = 0 },
                new ScoresheetSkip { W = 10, Written = "out", AfterPly = 9 },
            },
            Unresolved = { "a6", "Ba4" },
        };

        var c = ScoresheetScanService.CommentsFor(r);

        Assert.Equal(new Dictionary<int, string>
        {
            [0] = "sheet, extra: zz",
            [1] = "sheet: e6 | sheet, extra: Nc6",
            [2] = "sheet: ?",
            [3] = "sheet: —",
            [4] = "sheet, not resolved: a6 Ba4",
        }, c);
    }

    [Fact]
    public void CommentsFor_UnresolvedWithoutPlies_NoComments()
    {
        var r = new ScoresheetResolution { Unresolved = { "e4" } };
        Assert.Empty(ScoresheetScanService.CommentsFor(r));
    }

    [Theory]
    [InlineData("203.0.113.7", "203.0.113.7")]
    [InlineData("::ffff:203.0.113.7", "203.0.113.7")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    public void IpKey_Addresses_StableKeys(string ip, string expected) =>
        Assert.Equal(expected, ScoresheetScanService.IpKey(IPAddress.Parse(ip)));

    [Fact]
    public void IpKey_Null_QuestionMark() => Assert.Equal("?", ScoresheetScanService.IpKey(null));

    [Fact]
    public void AnonIpHash_SameNet_SameHash()
    {
        var a = _service.AnonIpHash(IPAddress.Parse("2001:db8:1:2::1"));
        var b = _service.AnonIpHash(IPAddress.Parse("2001:db8:1:2::ffff"));
        Assert.Equal(a, b);
        Assert.Equal(32, a.Length);
        Assert.NotEqual(a, _service.AnonIpHash(IPAddress.Parse("2001:db8:1:3::1")));
    }

    [Fact]
    public async Task GetAsync_StoredResolution_MapsAllFields()
    {
        var created = new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        _db.ScoresheetScans.Add(new ScoresheetScan
        {
            Id = 41, UserId = 7, SavedGameId = null, Photo = new byte[] { 1 }, ContentType = "image/png", FileName = "a.png",
            NotationLanguage = "de", OwnerSide = "black", Status = ScoresheetScanStatus.Done, Error = "x", Rounds = 2,
            PageCount = 0, CreatedAt = created, StartedAt = created.AddMinutes(1), FinishedAt = created.AddMinutes(2),
            ResolutionJson = """{"language":"de","plies":[{"san":"e4","uncertain":true},{"san":"e5","uncertain":true,"confirmed":true},{"san":"Nf3"}],"unresolved":["a","b"],"unresolvedFrom":3}""",
        });
        await _db.SaveChangesAsync();

        var dto = await _service.GetAsync(7, 41);

        Assert.NotNull(dto);
        Assert.Equal(41, dto!.Id);
        Assert.Equal("done", dto.Status);
        Assert.Equal("x", dto.Error);
        Assert.Null(dto.SavedGameId);
        Assert.Equal("de", dto.NotationLanguage);
        Assert.Equal("black", dto.OwnerSide);
        Assert.Equal("a.png", dto.FileName);
        Assert.Equal(created, dto.CreatedAt);
        Assert.Equal(created.AddMinutes(1), dto.StartedAt);
        Assert.Equal(created.AddMinutes(2), dto.FinishedAt);
        Assert.Equal(2, dto.Rounds);
        Assert.Equal(1, dto.PageCount);
        Assert.Equal(3, dto.MoveCount);
        Assert.Equal(1, dto.UncertainCount);
        Assert.Equal(2, dto.UnresolvedCount);
        Assert.Null(dto.White);
        Assert.Null(dto.Black);
    }

    [Fact]
    public async Task GetAsync_BrokenResolutionJson_ZeroCounts()
    {
        _db.ScoresheetScans.Add(new ScoresheetScan
        {
            Id = 42, UserId = 7, Photo = new byte[] { 1 }, ContentType = "image/jpeg", Status = ScoresheetScanStatus.Pending,
            PageCount = 3, CreatedAt = DateTime.UtcNow, ResolutionJson = "{kaputt",
        });
        await _db.SaveChangesAsync();

        var dto = await _service.GetAsync(7, 42);

        Assert.Equal("pending", dto!.Status);
        Assert.Equal(3, dto.PageCount);
        Assert.Equal(0, dto.MoveCount);
        Assert.Equal(0, dto.UncertainCount);
        Assert.Equal(0, dto.UnresolvedCount);
        Assert.Null(await _service.GetAsync(8, 42));
    }
}
