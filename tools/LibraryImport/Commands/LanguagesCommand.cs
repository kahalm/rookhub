using Microsoft.EntityFrameworkCore;
using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>languages</c> — Sprache der Kommentare bestimmen.</summary>
internal static class LanguagesCommand
{
    // ===== 3. Sprache ===========================================================

    public static async Task<int> RunAsync(ToolContext ctx)
    {
        // Der Text einer Partie steht im PGN, und das ist die grosse Spalte — geholt wird deshalb
        // seitenweise und nur einmal. Geschrieben wird GRUPPIERT: 130 000 einzelne UPDATEs waeren
        // 130 000 Umlaeufe, waehrend es je Seite nur eine Handvoll verschiedener Sprachen gibt.
        var done = 0;
        while (true)
        {
            await using var db = ctx.Db.New();
            var rows = await db.LibraryGames
                .Where(g => g.Languages == null && g.CommentChars > 0)
                .OrderBy(g => g.Id)
                .Take(2000)
                .Select(g => new { g.Id, g.Pgn })
                .ToListAsync();
            if (rows.Count == 0) break;

            var byLanguage = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            foreach (var row in rows)
            {
                // „und" ist das ISO-Kuerzel fuer „unbestimmt". Es MUSS geschrieben werden, auch wenn
                // nichts erkannt wurde: bliebe die Spalte null, holte die naechste Seite dieselben
                // Zeilen wieder, und der Durchgang liefe im Kreis.
                var lang = CommentLanguage.Detect(CommentLanguage.CommentText(row.Pgn, 4000)) ?? "und";
                if (!byLanguage.TryGetValue(lang, out var ids)) byLanguage[lang] = ids = new List<int>();
                ids.Add(row.Id);
            }

            foreach (var (lang, ids) in byLanguage)
                await db.LibraryGames.Where(g => ids.Contains(g.Id))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(g => g.Languages, lang)
                        .SetProperty(g => g.UpdatedAt, DateTime.UtcNow));

            done += rows.Count;
            if (done % 20000 == 0) Console.WriteLine($"  {done:N0} Partien eingeordnet");
        }
        Console.WriteLine($"Sprache bestimmt fuer {done:N0} Partien.");
        return 0;
    }
}
