using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Models;
using RookHub.Api.Services.Ebooks;
using Xunit;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// Bibliotheksliste (0.877.0) gegen ECHTES MariaDB: Sprachfilter, Übersetzungsfilter und die neuen Zähler (Sprachen,
/// Übersetzungen, Bücher einzeln samt Sammlungen, Dateien je Format ohne abgeleitete) übersetzen sich in SQL —
/// InMemory prüft GroupBy über die nullable Sprache und die Unterabfragen nicht.
/// </summary>
public class EbookListSqlTests(EbookListSqlFixture fixture) : IAsyncLifetime, IClassFixture<EbookListSqlFixture>
{
    public Task InitializeAsync() => fixture.ResetAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    private static Ebook Book(string title, string? language, params (string Format, bool Derived)[] files) => new()
    {
        Title = title, Source = EbookSource.Library, Language = language,
        Files = files.Select(f => new EbookFile { Format = f.Format, Derived = f.Derived, RelativePath = $"{title}.{f.Format}", FileName = $"{title}.{f.Format}" }).ToList(),
    };

    [MySqlFact]
    public async Task List_LanguageAndTranslationFilters_AndCounts_TranslateToSql()
    {
        await using var db = fixture.Schema.NewContext();
        var collection = new EbookCollection { Name = "Reihe" };
        var de = Book("Deutsch", "de", ("pdf", false), ("epub", true));
        var es1 = Book("Spanisch 1", "es", ("pdf", false));
        var es2 = Book("Spanisch 2", "es", ("epub", false), ("pdf", false));
        var unknown = Book("Unbekannt", null, ("djvu", false), ("pdf", true));
        es1.Collection = collection; es2.Collection = collection;
        db.Ebooks.AddRange(de, es1, es2, unknown);
        await db.SaveChangesAsync();
        db.EbookTranslations.Add(new EbookTranslation { EbookId = es2.Id, Language = "en", State = EbookTranslationState.Done });
        db.EbookTranslations.Add(new EbookTranslation { EbookId = es1.Id, Language = "en", State = EbookTranslationState.Pending });
        await db.SaveChangesAsync();

        var svc = new EbookService(db, new EbookStorage(Options.Create(new EbookOptions()), NullLogger<EbookStorage>.Instance),
            NullLogger<EbookService>.Instance);

        var all = await svc.ListAsync(1, manageAll: true, new EbookQuery());
        Assert.Equal(4, all.BookCount);   // Sammlung zählt ihre zwei Bücher einzeln
        Assert.Equal(new Dictionary<string, int> { ["pdf"] = 3, ["epub"] = 1, ["djvu"] = 1 },
            all.FileCounts.ToDictionary(f => f.Format.ToLowerInvariant(), f => f.Count));   // abgeleitete Dateien zählen nicht
        Assert.Equal(2, all.Languages.Single(l => l.Language == "es").Count);
        Assert.Equal(1, all.Languages.Single(l => l.Language == null).Count);
        Assert.Equal(1, all.Translations.Single(t => t.Language == "en").Count);   // nur fertige

        var spanish = await svc.ListAsync(1, manageAll: true, new EbookQuery { Language = "es" });
        Assert.Equal(2, spanish.BookCount);
        var none = await svc.ListAsync(1, manageAll: true, new EbookQuery { Language = "unknown" });
        Assert.Equal(1, none.BookCount);
        var translated = await svc.ListAsync(1, manageAll: true, new EbookQuery { Translation = "en" });
        Assert.Equal(1, translated.BookCount);
    }
}

public sealed class EbookListSqlFixture() : MariaDbClassFixture("ebooklist", withApp: false);
