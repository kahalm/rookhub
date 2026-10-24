using Microsoft.EntityFrameworkCore;
using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>openings</c> — Eroeffnungszeile + ersten kommentierten Halbzug nachtragen.</summary>
internal static class OpeningsCommand
{
    // ===== 2b. Eroeffnungszeile + erster Kommentar ==============================

    // Das BRETTFELD der Grundstellung — verglichen wird ohne die Zaehler dahinter, die je nach
    // Exporteur abweichen. Als Praefix-Vergleich, damit EF ihn in SQL uebersetzt.
    internal const string GrundstellungBrett = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";

    public static async Task<int> RunAsync(ToolContext ctx)
    {
        // Nachtrag fuer den Altbestand: beide Spalten fallen beim Einlesen ohnehin an, es gab sie nur
        // noch nicht. Gerechnet wird aus dem gespeicherten PGN — die Quelldatei wird nicht gebraucht.
        // ZUERST raeumen: eine Eroeffnungszeile beschreibt einen Weg AUS DER GRUNDSTELLUNG. Partien mit
        // eigener Ausgangsstellung (Vorgabepartie, Chess960, Studie) trugen trotzdem eine, und ihr
        // erster Zug stand damit als Fortsetzung an der WURZEL des Baums — auf Prod ein „Kc6", das es
        // dort nicht gibt. Der Nachtrag unten fuellt nur noch echte Grundstellungs-Partien.
        await using (var raeumen = ctx.Db.New())
        {
            var weg = await raeumen.LibraryGames
                .Where(g => g.OpeningLine != null && g.StartFen != null
                         && !g.StartFen.StartsWith(GrundstellungBrett))
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.OpeningLine, (string?)null));
            if (weg > 0) Console.WriteLine($"{weg:N0} Eroeffnungszeilen von Partien mit eigener Ausgangsstellung entfernt.");
        }

        var done = 0;
        var lastId = 0;
        while (true)
        {
            await using var db = ctx.Db.New();
            var rows = await db.LibraryGames
                .Where(g => g.Id > lastId && g.OpeningLine == null
                         && (g.StartFen == null || g.StartFen.StartsWith(GrundstellungBrett)))
                .OrderBy(g => g.Id)
                .Take(2000)
                .Select(g => new { g.Id, g.Pgn })
                .ToListAsync();
            if (rows.Count == 0) break;
            lastId = rows[^1].Id;

            foreach (var row in rows)
            {
                var stats = LibraryGameReader.Analyse(GuessStartPly.MoveTextOf(row.Pgn));
                if (stats.OpeningLine.Length == 0) continue;
                var opening = stats.OpeningLine.Length > 200 ? stats.OpeningLine[..200] : stats.OpeningLine;
                var firstComment = stats.FirstCommentedPly == 0 ? (int?)null : stats.FirstCommentedPly;
                await db.LibraryGames.Where(g => g.Id == row.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(g => g.OpeningLine, opening)
                        .SetProperty(g => g.FirstCommentedPly, firstComment));
            }

            done += rows.Count;
            if (done % 20000 == 0) Console.WriteLine($"  {done:N0} nachgetragen");
        }
        Console.WriteLine($"Eroeffnungszeile nachgetragen fuer {done:N0} Partien.");
        return 0;
    }
}
