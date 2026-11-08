using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// „Bücher, die ich lesen kann“ (0.896.0): die Bibliothek zeigt per Vorgabe nur Bücher in der eigenen Sprache und auf
/// Englisch — dazu jedes Buch mit fertiger Übersetzung in eine dieser Sprachen (ein russisches Buch mit englischer
/// Übersetzung gehört dazu). Die Sprachen kommen als Komma-Liste aus der Oberfläche (<c>readable=de,en</c>).
/// </summary>
public static class EbookReadable
{
    /// <summary>Komma-Liste → bereinigte Sprachcodes (klein, je höchstens 8 Zeichen, ohne Doppelte); leer = kein Filter.</summary>
    public static List<string> Parse(string? csv) =>
        string.IsNullOrWhiteSpace(csv)
            ? []
            : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(l => l.ToLowerInvariant())
                .Where(l => l.Length is > 0 and <= 8 && l != "all")
                .Distinct().Take(8).ToList();

    /// <summary>Nur Bücher in einer der Sprachen oder mit fertiger Übersetzung in eine davon.</summary>
    public static IQueryable<Ebook> Filter(IQueryable<Ebook> query, AppDbContext db, IReadOnlyCollection<string> langs) =>
        langs.Count == 0
            ? query
            : query.Where(b => (b.Language != null && langs.Contains(b.Language))
                               || db.EbookTranslations.Any(t => t.EbookId == b.Id && t.State == EbookTranslationState.Done && langs.Contains(t.Language)));

    /// <summary>Bücher, deren übersetzter Titel den Suchbegriff (klein) enthält — die Suche findet „Winning Miniatures“
    /// auch beim russischen Original.</summary>
    public static IQueryable<int> TitleMatches(AppDbContext db, string term) =>
        db.EbookTranslations.Where(t => t.Title != null && t.Title.ToLower().Contains(term)).Select(t => t.EbookId);

    /// <summary>Titel des Buchs geändert → übersetzte Titel verwerfen (der Worker übersetzt neu). Speichert NICHT selbst.</summary>
    public static async Task ResetTitlesAsync(AppDbContext db, int ebookId, CancellationToken ct)
    {
        foreach (var t in await db.EbookTranslations.Where(t => t.EbookId == ebookId && t.Title != null).ToListAsync(ct))
            t.Title = null;
    }
}
