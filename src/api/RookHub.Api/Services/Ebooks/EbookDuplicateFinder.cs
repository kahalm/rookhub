using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>Ein Treffer der Dublettensuche: Buch, Grund (<c>isbn</c>/<c>text</c>/<c>title</c>), Ähnlichkeit, sicher oder nur wahrscheinlich.</summary>
public sealed record EbookDuplicateMatch(int EbookId, string Reason, double Similarity, bool Certain);

/// <summary>
/// Sucht zu einem neuen Upload vorhandene Bücher, die dasselbe Buch sind (0.863.0). Gesucht wird NUR unter den Büchern, die
/// der Hochladende sehen darf (<see cref="EbookAccess.Visible(AppDbContext, EbookCaller, bool)"/>) — ein verstecktes Buch
/// verrät sich so nie.
///
/// <para><b>Schwellen</b> (an den Tests <c>EbookFingerprintTests</c> abgestimmt):</para>
/// <list type="bullet">
/// <item>gleiche ISBN-13 → <b>sicher</b></item>
/// <item>Text-Ähnlichkeit (MinHash-Jaccard) ≥ <see cref="CertainSimilarity"/> → <b>sicher</b>; derselbe Text in anderem
///   Umbruch/anderer Datei liegt bei 0,9–1, ein PDF gegen das EPUB desselben Buchs (Titelei verschoben, Kopfzeilen) meist
///   bei 0,7–0,95</item>
/// <item>≥ <see cref="ProbableSimilarity"/> → <b>wahrscheinlich</b> (andere Auflage, stark abweichende Titelei); fremde
///   Bücher liegen praktisch bei 0 (gemeinsame 5-Wort-Folgen sind selten, selbst bei gleichem Thema)</item>
/// <item>gleicher Haupttitel (<see cref="EbookFingerprint.NormalizeTitle"/>) und gemeinsamer Nachname — oder einer der beiden
///   ohne Autor — → <b>wahrscheinlich</b> (Scans ohne Text haben nur das)</item>
/// </list>
/// </summary>
public static class EbookDuplicateFinder
{
    public const double CertainSimilarity = 0.8;
    public const double ProbableSimilarity = 0.4;
    /// <summary>Höchstens so viele Bücher zeigen.</summary>
    public const int MaxResults = 5;

    public static async Task<List<EbookDuplicateMatch>> FindAsync(AppDbContext db, IQueryable<Ebook> visible,
        EbookFileFingerprint fingerprint, string? title, string? author, CancellationToken ct)
    {
        var visibleIds = visible.Select(b => b.Id);
        var matches = new List<EbookDuplicateMatch>();

        foreach (var isbn in fingerprint.Isbns)
        {
            // CSV aus 13-stelligen Zahlen: ein Teilstring-Treffer ist immer eine ganze ISBN.
            var ids = await db.EbookFiles.Where(f => f.Isbns != null && f.Isbns.Contains(isbn) && visibleIds.Contains(f.EbookId))
                .Select(f => f.EbookId).Distinct().ToListAsync(ct);
            matches.AddRange(ids.Select(id => new EbookDuplicateMatch(id, "isbn", 1, true)));
        }

        if (fingerprint.MinHash is { } mine)
        {
            var prints = await db.EbookFiles.Where(f => f.TextFingerprint != null && visibleIds.Contains(f.EbookId))
                .Select(f => new { f.EbookId, f.TextFingerprint }).ToListAsync(ct);
            foreach (var p in prints)
            {
                if (EbookFingerprint.Decode(p.TextFingerprint) is not { } other) continue;
                var sim = EbookFingerprint.Similarity(mine, other);
                if (sim >= ProbableSimilarity) matches.Add(new EbookDuplicateMatch(p.EbookId, "text", sim, sim >= CertainSimilarity));
            }
        }

        if (EbookFingerprint.NormalizeTitle(title) is { } wanted)
        {
            var books = await visible.Select(b => new { b.Id, b.Title, b.Author }).ToListAsync(ct);
            foreach (var b in books)
            {
                if (EbookFingerprint.NormalizeTitle(b.Title) != wanted) continue;
                var sameAuthor = string.IsNullOrWhiteSpace(author) || string.IsNullOrWhiteSpace(b.Author)
                                 || EbookFingerprint.AuthorsOverlap(author, b.Author);
                if (sameAuthor) matches.Add(new EbookDuplicateMatch(b.Id, "title", 0, false));
            }
        }

        // Je Buch der stärkste Treffer: sicher vor wahrscheinlich, dann isbn > text > title, dann Ähnlichkeit.
        return matches
            .GroupBy(m => m.EbookId)
            .Select(g => g.OrderByDescending(m => m.Certain).ThenBy(m => ReasonRank(m.Reason)).ThenByDescending(m => m.Similarity).First()
                with { Similarity = g.Max(m => m.Similarity) })
            .OrderByDescending(m => m.Certain).ThenBy(m => ReasonRank(m.Reason)).ThenByDescending(m => m.Similarity).ThenBy(m => m.EbookId)
            .Take(MaxResults)
            .ToList();
    }

    private static int ReasonRank(string reason) => reason switch { "file" => 0, "isbn" => 1, "text" => 2, _ => 3 };

    /// <summary>Treffer → DTOs (Titel, Autor, Jahr, Formate), in der Reihenfolge der Treffer.</summary>
    public static async Task<List<EbookDuplicateDto>> DescribeAsync(AppDbContext db, IReadOnlyList<EbookDuplicateMatch> matches, CancellationToken ct)
    {
        if (matches.Count == 0) return [];
        var ids = matches.Select(m => m.EbookId).ToList();
        var books = await db.Ebooks.Include(b => b.Files).Where(b => ids.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        return matches.Where(m => books.ContainsKey(m.EbookId)).Select(m =>
        {
            var b = books[m.EbookId];
            return new EbookDuplicateDto
            {
                Id = b.Id,
                Title = b.Title,
                Author = b.Author,
                Year = b.Year,
                Formats = b.Files.Where(f => !f.Derived).Select(f => f.Format).Distinct()
                    .OrderBy(EbookFormats.Rank).ToList(),
                Reason = m.Reason,
                Similarity = Math.Round(m.Similarity, 2),
            };
        }).ToList();
    }
}
