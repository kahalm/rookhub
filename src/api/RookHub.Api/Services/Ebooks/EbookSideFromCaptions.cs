using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Exceptions;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Nachtrag (0.823.0): Diagramme ohne Zugrecht eines Buchs bekommen es aus der Bildunterschrift „White/Black to move“ der
/// PDF-Textebene (<see cref="ScanCaptionSide"/>). Anders als „Stellung korrigieren“ gilt das Buch danach NICHT als kuratiert
/// — eine spätere Neuerkennung darf die Bretter weiter ersetzen (und liest die Unterschrift dann selbst).
/// Wechselt die Seite, wird die Buchlösung mit der neuen Seite neu gelesen.
/// </summary>
public static class EbookSideFromCaptions
{
    public sealed record Outcome(int EbookId, int Checked, int Set, int Changed);

    public static async Task<Outcome> ApplyAsync(AppDbContext db, EbookStorage storage, int ebookId, CancellationToken ct)
    {
        var ebook = await db.Ebooks.Include(b => b.Files).FirstOrDefaultAsync(b => b.Id == ebookId, ct)
            ?? throw new NotFoundException("E-book not found.");
        var pdf = ebook.Files.Where(f => f.Format == "pdf").OrderBy(f => f.Derived).FirstOrDefault();
        if (pdf == null) return new Outcome(ebookId, 0, 0, 0);
        var path = storage.ResolveFile(ebook, pdf);
        if (!File.Exists(path)) throw new NotFoundException("The PDF is missing on the server.") { Code = ApiErrorCodes.EbookFileMissing };

        var diagrams = await db.EbookDiagrams.Where(d => d.EbookId == ebookId && !d.SideKnown && d.W > 0).ToListAsync(ct);
        var sides = await Task.Run(() => EbookDiagramExtractor.SidesFromCaptions(path, diagrams, ct), ct);
        var changed = 0;
        foreach (var d in diagrams)
        {
            if (!sides.TryGetValue(d.Id, out var side)) continue;
            var parts = d.Fen.Split(' ');
            d.SideKnown = true;
            if (parts.Length > 1 && parts[1][0] == side) continue;
            parts[1] = side.ToString();
            d.Fen = string.Join(' ', parts);
            EbookService.ResetSolutionForNewPosition(d);
            changed++;
        }
        await db.SaveChangesAsync(ct);
        return new Outcome(ebookId, diagrams.Count, sides.Count, changed);
    }
}
