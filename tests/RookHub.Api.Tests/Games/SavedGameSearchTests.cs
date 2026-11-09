using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Tests.Games;

/// <summary>Freitextsuche der Partienliste: Felder, UND-Verknüpfung, Sonderzeichen, nur eigene Partien. Die SQL-Übersetzung
/// prüft <c>SavedGameSearchSqlTests</c> gegen echtes MariaDB.</summary>
public class SavedGameSearchTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly SavedGameService _service;
    private int _next = 1;

    public SavedGameSearchTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = TestServices.SavedGames(_db);
    }

    public void Dispose() => _db.Dispose();

    private static string Pgn(string headers = "", string moves = "1. e4 e5 *") => $"{headers}\n{moves}\n";

    private async Task<int> AddAsync(int userId = 1, string? white = "Anna", string? black = "Bert", string? pgn = null,
        string source = "pgn", string? c1 = null, string? c2 = null, string? tags = null)
    {
        var id = _next++;
        _db.SavedGames.Add(new SavedGame
        {
            Id = id, UserId = userId, White = white, Black = black, Source = source, Pgn = pgn ?? Pgn(), ShareToken = $"t{id}",
            MoveCount = 2, HeadersScanned = true, Classifier1 = c1, Classifier2 = c2, Tags = tags,
            CreatedAt = new DateTime(2026, 1, 1).AddDays(id),
        });
        await _db.SaveChangesAsync();
        return id;
    }

    private async Task<List<int>> SearchAsync(string? q, int take = 200, int userId = 1)
        => (await _service.ListAsync(userId, take, q)).Select(g => g.Id).ToList();

    [Fact]
    public async Task Search_NoOrBlankQuery_ReturnsEverything()
    {
        var a = await AddAsync();
        var b = await AddAsync();
        Assert.Equal(new[] { b, a }, await SearchAsync(null));
        Assert.Equal(new[] { b, a }, await SearchAsync("   "));
    }

    [Theory]
    [InlineData("carlsen")]
    [InlineData("CARLSEN")]
    [InlineData("arls")]
    public async Task Search_MatchesPlayerColumns_CaseInsensitive(string q)
    {
        var hit = await AddAsync(white: "Magnus Carlsen");
        var asBlack = await AddAsync(black: "Carlsen, M");
        await AddAsync();
        Assert.Equal(new[] { asBlack, hit }, await SearchAsync(q));
    }

    [Theory]
    [InlineData("[Event \"Landesliga Tirol\"]\n", "landesliga")]
    [InlineData("[Site \"Schwaz AUT\"]\n", "schwaz")]
    [InlineData("[ECO \"B90\"]\n", "b90")]
    [InlineData("[Opening \"Sicilian Najdorf\"]\n", "najdorf")]
    [InlineData("[Variation \"English Attack\"]\n", "english")]
    public async Task Search_MatchesPgnHeaders(string header, string q)
    {
        var hit = await AddAsync(pgn: Pgn("[White \"x\"]\n" + header + "[Result \"*\"]"));
        await AddAsync();
        Assert.Equal(new[] { hit }, await SearchAsync(q));
    }

    [Fact]
    public async Task Search_MatchesClassifiersSourceAndTags()
    {
        var c1 = await AddAsync(c1: "Landesliga");
        var c2 = await AddAsync(c2: "2026/27");
        var tag = await AddAsync(tags: "gambit\nendspiel");
        var src = await AddAsync(source: "lichess");
        Assert.Equal(new[] { c1 }, await SearchAsync("landesliga"));
        Assert.Equal(new[] { c2 }, await SearchAsync("2026/27"));
        Assert.Equal(new[] { tag }, await SearchAsync("endspiel"));
        Assert.Equal(new[] { src }, await SearchAsync("lichess"));
    }

    [Fact]
    public async Task Search_SeveralWords_AllMustMatchAcrossDifferentFields()
    {
        var hit = await AddAsync(white: "Carlsen", pgn: Pgn("[Event \"Wijk\"]\n[ECO \"C65\"]"));
        await AddAsync(white: "Carlsen", pgn: Pgn("[Event \"Dortmund\"]\n[ECO \"C65\"]"));
        await AddAsync(white: "Anand", pgn: Pgn("[Event \"Wijk\"]\n[ECO \"C65\"]"));
        Assert.Equal(new[] { hit }, await SearchAsync("  carlsen   wijk "));
    }

    [Fact]
    public async Task Search_LooksOnlyAtHeaders_NotAtMovesCommentsOrOtherHeaders()
    {
        await AddAsync(pgn: Pgn("[Event \"X\"]\n[Annotator \"Kasparov\"]", "1. e4 {Event \"Kasparov\" Nf3} e5 *"));
        Assert.Empty(await SearchAsync("kasparov"));
        Assert.Empty(await SearchAsync("Nf3"));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    [InlineData("!")]
    [InlineData(".*")]
    [InlineData("(")]
    [InlineData("[")]
    public async Task Search_SpecialCharacters_AreLiterals(string q)
    {
        await AddAsync(white: "Anna", pgn: Pgn("[Event \"Open\"]"));
        Assert.Empty(await SearchAsync(q));
    }

    [Fact]
    public async Task Search_Underscore_MatchesTheCharacterOnly()
    {
        var hit = await AddAsync(white: "dr_who");
        await AddAsync(white: "drXwho");
        Assert.Equal(new[] { hit }, await SearchAsync("dr_who"));
    }

    [Fact]
    public async Task Search_RegexCharactersInHeader_MatchLiterally()
    {
        var hit = await AddAsync(pgn: Pgn("[Event \"Open (A+B)\"]"));
        await AddAsync(pgn: Pgn("[Event \"Open A\"]"));
        Assert.Equal(new[] { hit }, await SearchAsync("(a+b)"));
    }

    [Fact]
    public async Task Search_FindsGamesBeyondTheNewestPage()
    {
        var old = await AddAsync(white: "Uralt");
        for (var i = 0; i < 3; i++) await AddAsync();
        Assert.DoesNotContain(old, await SearchAsync(null, take: 2));
        Assert.Equal(new[] { old }, await SearchAsync("uralt", take: 2));
    }

    [Fact]
    public async Task Search_NeverReturnsOtherUsersGames()
    {
        await AddAsync(userId: 2, white: "Carlsen");
        Assert.Empty(await SearchAsync("carlsen"));
    }

    [Fact]
    public void Terms_DeduplicatesCapsCountAndLength()
    {
        Assert.Equal(new[] { "a", "b" }, SavedGameSearch.Terms(" a  A\tb "));
        Assert.Equal(SavedGameSearch.MaxTerms, SavedGameSearch.Terms(string.Join(' ', Enumerable.Range(0, 30))).Count);
        Assert.Equal(SavedGameSearch.MaxQueryLength, SavedGameSearch.Terms(new string('x', 500)).Single().Length);
        Assert.Empty(SavedGameSearch.Terms(null));
    }
}
