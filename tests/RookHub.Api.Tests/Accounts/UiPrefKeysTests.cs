using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Accounts;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Tests.Accounts;

/// <summary>Anzeige-Vorlieben im Profil (1.018.0): Whitelist (handgespiegelt, LITERALE Werte wie in
/// <c>ui-prefs-sync.spec.ts</c>), Zusammenführen je Schlüssel, Validierung.</summary>
public class UiPrefKeysTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ProfileService _service;
    private static readonly long Now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    public UiPrefKeysTests()
    {
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _service = TestServices.Profile(_db, new NoOpTaskQueue(), NullLogger<ProfileService>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public void Allowed_MatchesFrontendList()
    {
        string[] expected =
        [
            "rookhub_puzzle_config", "rookhub_visualization", "rookhub_viz_arrow", "rookhub_offpath_warn_moves",
            "rookhub_en_passant_forced", "rookhub_dashboard_layout_v2", "rookhub_notifications_hidden_categories",
            "rookhub_guess_accept", "rookhub_guess_comment_lang", "rookhub_explorer_settings",
            "rookhub_rep_train_show_comments", "rookhub_rep_train_review_engine", "rookhub_deviation_threshold",
            "rookhub_game_arrow", "rookhub_game_badges", "rookhub_game_engine_view", "rookhub_game_lines",
            "rookhub_analysis_compare", "rookhub_analysis_depth", "rookhub_analysis_lines", "rookhub_analysis_maia_elo",
            "rookhub_analysis_maia_evalbar", "rookhub_analysis_maia_warn", "rh.ebooks.solveStyle", "rh.ebooks.autoNext",
            "rh.repertoire.treeArrows", "rookhub_live_eval", "rh.turnier.historyView", "lh-club", "lh-training-lines",
            "lh-tree-filter", "lh-auto-my-games", "lh-all-leagues", "lh-overview-newest-first",
        ];
        Assert.Equal(expected.OrderBy(k => k, StringComparer.Ordinal), UiPrefKeys.Allowed.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(UiPrefKeys.Allowed, k => Assert.True(k.Length <= ProfileChoiceMaps.MaxKeyLength, k));
    }

    private async Task<int> CreateUserAsync()
    {
        var user = new Models.AppUser { Username = "ui", Email = "ui@example.com", PasswordHash = "hash", Profile = new Models.UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user.Id;
    }

    [Fact]
    public async Task UpdateProfile_UiPrefs_SavedMergedAndReadBack()
    {
        var userId = await CreateUserAsync();
        await _service.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            UiPrefs = new() { ["lh-club"] = new() { V = "4", At = Now }, ["rookhub_visualization"] = new() { V = "2", At = Now - 100 } },
        });
        var result = await _service.UpdateProfileAsync(userId, new UpdateProfileDto
        {
            UiPrefs = new() { ["lh-club"] = new() { V = "9", At = Now - 50 }, ["rookhub_visualization"] = new() { V = "3", At = Now } },
        });
        Assert.Equal("4", result.UiPrefs!["lh-club"].V);               // gespeicherte jünger
        Assert.Equal("3", result.UiPrefs["rookhub_visualization"].V);  // neue jünger
        Assert.Equal(2, (await _service.GetProfileAsync(userId)).UiPrefs!.Count);
    }

    [Fact]
    public async Task UpdateProfile_UnknownKeyOrTooLongValue_Throws()
    {
        var userId = await CreateUserAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateProfileAsync(userId,
            new UpdateProfileDto { UiPrefs = new() { ["rookhub_user"] = new() { V = "{}", At = Now } } }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateProfileAsync(userId,
            new UpdateProfileDto { UiPrefs = new() { ["lh-club"] = new() { V = new string('x', UiPrefKeys.MaxValueLength + 1), At = Now } } }));
    }
}
