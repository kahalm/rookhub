using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Tests.Accounts;

/// <summary>
/// Spielweise je Puzzle-Bereich und Sprache je Kurs im Profil (statt nur im localStorage, der beim Abmelden geräumt
/// wurde — die Frage nach der Spielweise kam deshalb immer wieder). Gespeichert über <c>PUT /api/profile</c>,
/// je Schlüssel zusammengeführt (der jüngere gewinnt), gelesen über <c>GET /api/profile</c>.
/// </summary>
public class ProfileChoiceSyncTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ProfileService _service;
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public ProfileChoiceSyncTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = TestServices.Profile(_db, new NoOpTaskQueue(), NullLogger<ProfileService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    private async Task<int> CreateUserAsync()
    {
        var user = new Models.AppUser { Username = "choice", Email = "choice@example.com", PasswordHash = "hash", Profile = new Models.UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    private static SolveModeEntryDto M(string mode, long at) => new() { Mode = mode, At = at };
    private static CourseLanguageEntryDto L(string lang, long at) => new() { Lang = lang, At = at };

    [Fact]
    public async Task UpdateProfile_SolveModes_SavedAndReadBack()
    {
        var userId = await CreateUserAsync();
        await _service.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            SolveModes = new() { ["*"] = M("easy", Now - 10), ["course:*"] = M("training", Now - 5), ["course:12"] = M("easy", Now) },
        });

        var profile = await _service.GetProfileAsync(userId);
        Assert.NotNull(profile.SolveModes);
        Assert.Equal("easy", profile.SolveModes!["*"].Mode);
        Assert.Equal("training", profile.SolveModes["course:*"].Mode);
        Assert.Equal(Now, profile.SolveModes["course:12"].At);
    }

    [Fact]
    public async Task UpdateProfile_SolveModes_MergedPerKey_NewerWins()
    {
        var userId = await CreateUserAsync();
        await _service.UpdateProfileAsync(userId, new UpdateProfileDto { SolveModes = new() { ["puzzles"] = M("easy", Now), ["daily"] = M("easy", Now - 100) } });
        // Ein anderes Gerät lädt seinen ÄLTEREN Stand hoch und eine neue Wahl für einen anderen Bereich.
        var result = await _service.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            SolveModes = new() { ["puzzles"] = M("training", Now - 1000), ["daily"] = M("training", Now), ["endless"] = M("easy", Now) },
        });

        Assert.Equal("easy", result.SolveModes!["puzzles"].Mode);    // gespeicherte war jünger
        Assert.Equal("training", result.SolveModes["daily"].Mode);   // neue ist jünger
        Assert.Equal("easy", result.SolveModes["endless"].Mode);     // dazugekommen
    }

    [Fact]
    public async Task UpdateProfile_WithoutSolveModes_KeepsStored()
    {
        var userId = await CreateUserAsync();
        await _service.UpdateProfileAsync(userId, new UpdateProfileDto { SolveModes = new() { ["*"] = M("easy", Now) } });
        var result = await _service.UpdateProfileAsync(userId, new UpdateProfileDto { BoardTheme = "blue" });
        Assert.Equal("easy", result.SolveModes!["*"].Mode);
    }

    [Theory]
    [InlineData("puzzles", "hard")]
    [InlineData("puzzles", "")]
    [InlineData("Course:12", "easy")]
    [InlineData("course:12:3", "easy")]
    [InlineData("course:abc", "easy")]
    [InlineData("<script>", "easy")]
    [InlineData("", "easy")]
    public async Task UpdateProfile_InvalidSolveMode_Throws(string key, string mode)
    {
        var userId = await CreateUserAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateProfileAsync(userId, new UpdateProfileDto { SolveModes = new() { [key] = M(mode, Now) } }));
    }

    [Fact]
    public void MergeSolveModes_KeyTooLongOrBadTimestamp_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProfileChoiceMaps.MergeSolveModes(null,
            new Dictionary<string, SolveModeEntryDto> { [new string('a', 41)] = M("easy", Now) }, Now));
        Assert.Throws<ArgumentException>(() => ProfileChoiceMaps.MergeSolveModes(null,
            new Dictionary<string, SolveModeEntryDto> { ["puzzles"] = M("easy", 0) }, Now));
    }

    [Fact]
    public void MergeSolveModes_TooManyInOneRequest_Throws()
    {
        var many = Enumerable.Range(1, ProfileChoiceMaps.MaxRequestEntries + 1).ToDictionary(i => $"course:{i}", i => M("easy", Now));
        Assert.Throws<ArgumentException>(() => ProfileChoiceMaps.MergeSolveModes(null, many, Now));
    }

    [Fact]
    public void MergeSolveModes_CappedAt200_OldestDropped_BaseChoicesKept()
    {
        var json = ProfileChoiceMaps.MergeSolveModes(null,
            new Dictionary<string, SolveModeEntryDto> { ["*"] = M("easy", 1), ["course:*"] = M("easy", 1) }, Now);
        var batch1 = Enumerable.Range(1, 150).ToDictionary(i => $"course:{i}", i => M("training", 1000 + i));
        var batch2 = Enumerable.Range(151, 100).ToDictionary(i => $"course:{i}", i => M("training", 1000 + i));
        json = ProfileChoiceMaps.MergeSolveModes(json, batch1, Now);
        json = ProfileChoiceMaps.MergeSolveModes(json, batch2, Now);

        var map = ProfileChoiceMaps.Read<SolveModeEntryDto>(json);
        Assert.Equal(ProfileChoiceMaps.MaxEntries + 2, map.Count);
        Assert.Contains("*", map.Keys);
        Assert.Contains("course:*", map.Keys);
        Assert.DoesNotContain("course:1", map.Keys);    // die ältesten flogen
        Assert.DoesNotContain("course:50", map.Keys);
        Assert.Contains("course:51", map.Keys);
        Assert.Contains("course:250", map.Keys);
    }

    [Fact]
    public void MergeSolveModes_FutureTimestamp_ClampedToNow()
    {
        var json = ProfileChoiceMaps.MergeSolveModes(null,
            new Dictionary<string, SolveModeEntryDto> { ["puzzles"] = M("easy", Now + 10L * 86_400_000) }, Now);
        Assert.Equal(Now, ProfileChoiceMaps.Read<SolveModeEntryDto>(json)["puzzles"].At);
    }

    [Fact]
    public void Read_CorruptJson_IsEmpty()
    {
        Assert.Empty(ProfileChoiceMaps.Read<SolveModeEntryDto>("{kaputt"));
        Assert.Empty(ProfileChoiceMaps.Read<SolveModeEntryDto>(null));
    }

    [Fact]
    public async Task UpdateProfile_CourseLanguages_SavedNormalizedAndMerged()
    {
        var userId = await CreateUserAsync();
        await _service.UpdateProfileAsync(userId, new UpdateProfileDto { CourseLanguages = new() { ["b12"] = L(" DE ", Now), ["b7"] = L("da", Now - 10) } });
        var result = await _service.UpdateProfileAsync(userId, new UpdateProfileDto { CourseLanguages = new() { ["b12"] = L("en", Now - 50) } });

        Assert.Equal("de", result.CourseLanguages!["b12"].Lang);   // gespeicherte jünger, normalisiert
        Assert.Equal("da", result.CourseLanguages["b7"].Lang);     // Quellsprache („Original“) zulässig
        var again = await _service.GetProfileAsync(userId);
        Assert.Equal(2, again.CourseLanguages!.Count);
    }

    [Theory]
    [InlineData("b12", "deutsch!")]
    [InlineData("b12", "")]
    [InlineData("fKurs.pgn", "de")]
    [InlineData("12", "de")]
    public async Task UpdateProfile_InvalidCourseLanguage_Throws(string key, string lang)
    {
        var userId = await CreateUserAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateProfileAsync(userId, new UpdateProfileDto { CourseLanguages = new() { [key] = L(lang, Now) } }));
    }

    [Fact]
    public async Task EraseUser_ClearsChoices()
    {
        var userId = await CreateUserAsync();
        await _service.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            SolveModes = new() { ["*"] = M("easy", Now) }, CourseLanguages = new() { ["b3"] = L("de", Now) },
        });
        await _service.EraseUserAsync(userId);
        var p = await _db.UserProfiles.SingleAsync(x => x.UserId == userId);
        Assert.Null(p.SolveModesJson);
        Assert.Null(p.CourseLanguagesJson);
    }
}
