using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Aufgabenblätter: Zwischenablage als Sammelkorb, „Als Aufgabenblatt speichern" (Stellungen
/// WANDERN), Umsortieren, Begleittexte — und die Grenzen (fremdes Blatt, kaputte FEN, volles Blatt).
/// </summary>
public class WorksheetServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly WorksheetService _service;

    public WorksheetServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _service = new WorksheetService(_db);
    }

    public void Dispose() => _db.Dispose();

    private const string Fen1 = "r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R b KQkq - 4 4";
    private const string Fen2 = "8/8/8/4k3/8/4K3/4P3/8 w - - 0 1";

    private async Task<AppUser> CreateUserAsync(string username = "kahalm")
    {
        var user = new AppUser { Username = username, Email = $"{username}@test.com", PasswordHash = "hash", Profile = new UserProfile() };
        _db.AppUsers.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private static List<NewWorksheetItemDto> Items(params string[] fens)
        => fens.Select(f => new NewWorksheetItemDto { Fen = f }).ToList();

    [Fact]
    public async Task Clipboard_is_created_on_first_access_and_stays_the_only_one()
    {
        var user = await CreateUserAsync();

        var first = await _service.GetOrCreateClipboardAsync(user.Id);
        var second = await _service.GetOrCreateClipboardAsync(user.Id);

        Assert.Equal(first.Id, second.Id);
        Assert.Single(_db.Worksheets.Where(w => w.UserId == user.Id && w.IsClipboard));
    }

    [Fact]
    public async Task List_shows_the_clipboard_first_even_when_it_was_never_touched()
    {
        var user = await CreateUserAsync();
        await _service.CreateAsync(user.Id, "Mittwochstraining", 4);

        var list = await _service.ListAsync(user.Id);

        Assert.True(list[0].IsClipboard);
        Assert.Equal(2, list.Count);
        Assert.Equal("Mittwochstraining", list[1].Name);
        Assert.Equal(4, list[1].PerPage);
    }

    [Fact]
    public async Task Sending_without_target_lands_in_the_clipboard()
    {
        var user = await CreateUserAsync();

        var result = await _service.AddItemsAsync(user.Id, null, Items(Fen1, Fen2));

        Assert.NotNull(result);
        Assert.True(result!.IsClipboard);
        Assert.Equal(2, result.Added);
        Assert.Equal(2, result.Total);
        var clip = await _service.GetClipboardAsync(user.Id);
        Assert.Equal(new[] { 0, 1 }, clip.Items.Select(i => i.SortOrder));
    }

    [Fact]
    public async Task Sending_the_same_position_twice_does_not_print_it_twice()
    {
        var user = await CreateUserAsync();
        await _service.AddItemsAsync(user.Id, null, Items(Fen1));

        var result = await _service.AddItemsAsync(user.Id, null, Items(Fen1, Fen2));

        Assert.Equal(1, result!.Added);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task Same_position_from_the_other_side_is_a_separate_task()
    {
        var user = await CreateUserAsync();
        await _service.AddItemsAsync(user.Id, null, Items(Fen1));

        var result = await _service.AddItemsAsync(user.Id, null, new List<NewWorksheetItemDto>
        {
            new() { Fen = Fen1, Orientation = "black" },
        });

        Assert.Equal(1, result!.Added);
    }

    [Fact]
    public async Task Broken_fens_are_skipped_instead_of_poisoning_the_sheet()
    {
        var user = await CreateUserAsync();

        var result = await _service.AddItemsAsync(user.Id, null, Items("kaputt", "", Fen1));

        Assert.Equal(1, result!.Added);
        Assert.Equal(2, result.Skipped);
    }

    [Fact]
    public async Task Pattern_diagrams_with_illegal_positions_are_allowed()
    {
        // Kurse enthalten Muster-Diagramme (hier: gar keine Könige) — die gehören aufs Blatt.
        var user = await CreateUserAsync();

        var result = await _service.AddItemsAsync(user.Id, null, Items("8/8/8/3q4/8/8/8/8 w - - 0 1"));

        Assert.Equal(1, result!.Added);
    }

    [Fact]
    public async Task A_full_sheet_says_so_instead_of_silently_swallowing_positions()
    {
        var user = await CreateUserAsync();
        var many = Enumerable.Range(0, WorksheetService.MaxItemsPerSheet + 5)
            .Select(i => new NewWorksheetItemDto { Fen = $"8/8/8/8/8/8/8/{7 - i % 8}K1k4 w - - 0 {i}" })
            .ToList();

        var result = await _service.AddItemsAsync(user.Id, null, many);

        Assert.True(result!.Full);
        Assert.Equal(WorksheetService.MaxItemsPerSheet, result.Total);
    }

    [Fact]
    public async Task Saving_the_clipboard_moves_the_positions_and_leaves_it_empty()
    {
        var user = await CreateUserAsync();
        await _service.AddItemsAsync(user.Id, null, Items(Fen1, Fen2));

        var saved = await _service.SaveClipboardAsAsync(user.Id, "  Mittwochstraining  ", 2);

        Assert.NotNull(saved);
        Assert.Equal("Mittwochstraining", saved!.Name);
        Assert.Equal(2, saved.PerPage);
        Assert.Equal(2, saved.Items.Count);
        var clip = await _service.GetClipboardAsync(user.Id);
        Assert.Empty(clip.Items);
    }

    [Fact]
    public async Task Saving_an_empty_clipboard_creates_nothing()
    {
        var user = await CreateUserAsync();

        Assert.Null(await _service.SaveClipboardAsAsync(user.Id, "Leer", null));
        Assert.Empty(_db.Worksheets.Where(w => !w.IsClipboard));
    }

    [Fact]
    public async Task Reordering_with_a_partial_list_keeps_every_position()
    {
        var user = await CreateUserAsync();
        await _service.AddItemsAsync(user.Id, null, Items(Fen1, Fen2, "8/8/8/8/8/8/4K3/4k3 w - - 0 1"));
        var clip = await _service.GetClipboardAsync(user.Id);
        var ids = clip.Items.Select(i => i.Id).ToList();

        var reordered = await _service.ReorderAsync(user.Id, clip.Id, new List<int> { ids[2], ids[0] });

        Assert.Equal(new[] { ids[2], ids[0], ids[1] }, reordered!.Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Heading_and_text_can_be_set_per_position()
    {
        var user = await CreateUserAsync();
        await _service.AddItemsAsync(user.Id, null, Items(Fen1));
        var clip = await _service.GetClipboardAsync(user.Id);

        var updated = await _service.UpdateItemAsync(user.Id, clip.Id, clip.Items[0].Id, new UpdateWorksheetItemDto
        {
            Heading = "Grundreihenschwäche",
            Text = "Weiß droht Matt — wie verteidigt Schwarz?",
            Orientation = "black",
        });

        Assert.Equal("Grundreihenschwäche", updated!.Heading);
        Assert.Equal("black", updated.Orientation);
    }

    [Fact]
    public async Task Deleting_the_clipboard_only_empties_it()
    {
        var user = await CreateUserAsync();
        await _service.AddItemsAsync(user.Id, null, Items(Fen1));
        var clip = await _service.GetClipboardAsync(user.Id);

        Assert.True(await _service.DeleteAsync(user.Id, clip.Id));

        var after = await _service.GetClipboardAsync(user.Id);
        Assert.Equal(clip.Id, after.Id);
        Assert.Empty(after.Items);
    }

    [Fact]
    public async Task Deleting_a_named_sheet_removes_it_with_its_positions()
    {
        var user = await CreateUserAsync();
        await _service.AddItemsAsync(user.Id, null, Items(Fen1));
        var sheet = await _service.SaveClipboardAsAsync(user.Id, "Weg damit", null);

        Assert.True(await _service.DeleteAsync(user.Id, sheet!.Id));

        Assert.Null(await _service.GetAsync(user.Id, sheet.Id));
        Assert.Empty(_db.WorksheetItems.Where(i => i.WorksheetId == sheet.Id));
    }

    [Fact]
    public async Task A_foreign_sheet_is_neither_readable_nor_writable()
    {
        var owner = await CreateUserAsync("owner");
        var stranger = await CreateUserAsync("stranger");
        await _service.AddItemsAsync(owner.Id, null, Items(Fen1));
        var sheet = await _service.SaveClipboardAsAsync(owner.Id, "Meins", null);

        Assert.Null(await _service.GetAsync(stranger.Id, sheet!.Id));
        Assert.Null(await _service.AddItemsAsync(stranger.Id, sheet.Id, Items(Fen2)));
        Assert.Null(await _service.ReorderAsync(stranger.Id, sheet.Id, new List<int>()));
        Assert.False(await _service.DeleteAsync(stranger.Id, sheet.Id));
        Assert.Null(await _service.UpdateItemAsync(stranger.Id, sheet.Id, sheet.Items[0].Id, new UpdateWorksheetItemDto { Heading = "geklaut" }));
    }

    [Fact]
    public async Task The_clipboard_keeps_its_name_when_someone_tries_to_rename_it()
    {
        var user = await CreateUserAsync();
        var clip = await _service.GetClipboardAsync(user.Id);

        var updated = await _service.UpdateAsync(user.Id, clip.Id, new UpdateWorksheetDto { Name = "Heimlich benannt", PerPage = 2 });

        Assert.Equal(string.Empty, updated!.Name);
        Assert.Equal(2, updated.PerPage);
    }

    // ===== Themen =====

    [Fact]
    public async Task Themes_are_set_on_the_sheet_and_come_back_in_the_list()
    {
        var user = await CreateUserAsync();
        var sheet = await _service.CreateAsync(user.Id, "Mittwoch", null);

        var updated = await _service.UpdateAsync(user.Id, sheet.Id, new UpdateWorksheetDto
        {
            Themes = new List<string> { "rookEndgame", "Turmendspiel" },
        });

        Assert.Equal(new[] { "rookEndgame", "Turmendspiel" }, updated!.Themes);
        var listed = (await _service.ListAsync(user.Id)).First(w => !w.IsClipboard);
        Assert.Equal(new[] { "rookEndgame", "Turmendspiel" }, listed.Themes);
    }

    [Fact]
    public async Task Themes_untouched_when_the_update_does_not_mention_them()
    {
        var user = await CreateUserAsync();
        var sheet = await _service.CreateAsync(user.Id, "Mittwoch", null);
        await _service.UpdateAsync(user.Id, sheet.Id, new UpdateWorksheetDto { Themes = new List<string> { "fork" } });

        var renamed = await _service.UpdateAsync(user.Id, sheet.Id, new UpdateWorksheetDto { Name = "Donnerstag" });

        Assert.Equal(new[] { "fork" }, renamed!.Themes);
        Assert.Equal("Donnerstag", renamed.Name);
    }

    [Fact]
    public async Task An_empty_theme_list_clears_them()
    {
        var user = await CreateUserAsync();
        var sheet = await _service.CreateAsync(user.Id, "Mittwoch", null);
        await _service.UpdateAsync(user.Id, sheet.Id, new UpdateWorksheetDto { Themes = new List<string> { "fork" } });

        var cleared = await _service.UpdateAsync(user.Id, sheet.Id, new UpdateWorksheetDto { Themes = new List<string>() });

        Assert.Empty(cleared!.Themes);
    }

    [Fact]
    public async Task The_source_themes_of_a_sent_position_ride_along_for_the_suggestions()
    {
        var user = await CreateUserAsync();

        await _service.AddItemsAsync(user.Id, null, new List<NewWorksheetItemDto>
        {
            new() { Fen = Fen1, SourceThemes = "backRankMate fork" },
        });

        var clip = await _service.GetClipboardAsync(user.Id);
        Assert.Equal("backRankMate fork", clip.Items[0].SourceThemes);
    }

    [Fact]
    public void Themes_are_trimmed_deduplicated_and_capped()
    {
        var normalized = WorksheetService.NormalizeThemes(new[]
        {
            "  fork  ", "FORK", "", "rook,endgame", new string('x', 60),
        });

        Assert.Equal("fork", normalized[0]);                       // getrimmt
        Assert.Equal(3, normalized.Count);                          // „FORK" ist dasselbe Fach
        Assert.Equal("rook endgame", normalized[1]);                // Komma trennt die Spalte → Leerzeichen
        Assert.Equal(40, normalized[2].Length);                     // auf Spaltenbreite gekappt
    }

    [Fact]
    public void At_most_twelve_themes_fit_on_a_sheet()
        => Assert.Equal(12, WorksheetService.NormalizeThemes(
            Enumerable.Range(0, 20).Select(i => $"thema{i}")).Count);

    // ===== Teilen =====

    [Fact]
    public async Task Sharing_creates_a_link_and_keeps_it_on_a_second_call()
    {
        // Ein zweites „Teilen" darf kein neues Token geben — sonst wären gedruckte QR-Codes tot.
        var user = await CreateUserAsync();
        await _service.AddItemsAsync(user.Id, null, Items(Fen1));
        var sheet = await _service.SaveClipboardAsAsync(user.Id, "Mittwoch", null);

        var first = await _service.ShareAsync(user.Id, sheet!.Id);
        var second = await _service.ShareAsync(user.Id, sheet.Id);

        Assert.False(string.IsNullOrWhiteSpace(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task The_clipboard_cannot_be_shared_it_is_a_workbench()
    {
        var user = await CreateUserAsync();
        var clip = await _service.GetClipboardAsync(user.Id);

        Assert.Null(await _service.ShareAsync(user.Id, clip.Id));
    }

    [Fact]
    public async Task Stopping_the_sharing_kills_the_old_link_for_good()
    {
        var user = await CreateUserAsync();
        await _service.AddItemsAsync(user.Id, null, Items(Fen1));
        var sheet = await _service.SaveClipboardAsAsync(user.Id, "Mittwoch", null);
        var token = await _service.ShareAsync(user.Id, sheet!.Id);

        Assert.True(await _service.UnshareAsync(user.Id, sheet.Id));

        Assert.Null(await _service.GetSharedAsync(token!));
        var again = await _service.ShareAsync(user.Id, sheet.Id);
        Assert.NotEqual(token, again);   // neues Teilen = neuer Link
    }

    [Fact]
    public async Task The_shared_sheet_carries_the_positions_with_their_solutions()
    {
        var user = await CreateUserAsync();
        await _service.AddItemsAsync(user.Id, null, new List<NewWorksheetItemDto>
        {
            new() { Fen = Fen1, Orientation = "black", Heading = "Grundreihe", Text = "Wie geht es weiter?", SolutionMoves = "e1e2 d8d1" },
            new() { Fen = Fen2 },
        });
        var sheet = await _service.SaveClipboardAsAsync(user.Id, "Mittwoch", null);
        var token = await _service.ShareAsync(user.Id, sheet!.Id);

        var shared = await _service.GetSharedAsync(token!);

        Assert.Equal("Mittwoch", shared!.Name);
        Assert.Equal(2, shared.Items.Count);
        Assert.Equal("Grundreihe", shared.Items[0].Heading);
        Assert.Equal("e1e2 d8d1", shared.Items[0].SolutionMoves);
        Assert.Equal(string.Empty, shared.Items[1].SolutionMoves);   // ohne Lösung = nur zum Rechnen
    }

    [Fact]
    public async Task An_unknown_token_is_simply_not_found()
        => Assert.Null(await _service.GetSharedAsync("gibtsnicht"));

    [Fact]
    public async Task A_foreign_sheet_cannot_be_shared_or_unshared()
    {
        var owner = await CreateUserAsync("owner2");
        var stranger = await CreateUserAsync("stranger2");
        await _service.AddItemsAsync(owner.Id, null, Items(Fen1));
        var sheet = await _service.SaveClipboardAsAsync(owner.Id, "Meins", null);

        Assert.Null(await _service.ShareAsync(stranger.Id, sheet!.Id));
        Assert.False(await _service.UnshareAsync(stranger.Id, sheet.Id));
    }

    [Theory]
    [InlineData("e2e4 e7e5", "e2e4 e7e5")]
    [InlineData("e7e8q", "e7e8q")]
    [InlineData("  e2e4   e7e5  ", "e2e4 e7e5")]
    [InlineData("e2e4 <script> e7e5", "e2e4 e7e5")]
    [InlineData("z9z9", "")]
    [InlineData(null, "")]
    public void Only_real_uci_halfmoves_survive_as_a_solution(string? raw, string expected)
        => Assert.Equal(expected, WorksheetService.CleanUciMoves(raw));

    [Fact]
    public void An_absurdly_long_solution_is_cut_to_fit_the_column()
    {
        var long_ = string.Join(' ', Enumerable.Repeat("e2e4", 400));   // 1999 Zeichen

        var cleaned = WorksheetService.CleanUciMoves(long_);

        Assert.True(cleaned.Length <= 1000);
        Assert.StartsWith("e2e4 e2e4", cleaned);
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(4, 4)]
    [InlineData(6, 6)]
    [InlineData(5, 6)]
    [InlineData(null, 6)]
    public void Only_two_four_or_six_diagrams_fit_on_a_page(int? requested, int expected)
        => Assert.Equal(expected, WorksheetService.NormalizePerPage(requested));
}
