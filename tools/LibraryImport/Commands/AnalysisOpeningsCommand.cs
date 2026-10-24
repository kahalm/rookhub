using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>analysis-openings</c> — Eroeffnungszeile der eingereihten Partien nachtragen.</summary>
internal static class AnalysisOpeningsCommand
{
    // Die Eroeffnungszeile der eingereihten Partien nachtragen — die Grundlage des Stellungsfilters.
    //
    //   analysis-openings
    //
    // Neue Partien bekommen sie beim Anlegen (GameAnalysisService.CreateAsync). Gerechnet wird sie
    // hier aus den STELLUNGSZEILEN und nicht aus dem PGN: dort steht der Zug schon geprueft und
    // normalisiert da, und ein zweiter Parser waere eine zweite Gelegenheit, anders zu zaehlen.
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        await using var db = ctx.Db.New();
        // Wie bei `openings`: erst die Zeilen der Partien mit eigener Ausgangsstellung raeumen.
        var weg = await db.GameAnalyses
            .Where(g => g.OpeningLine != null && !g.StartFen.StartsWith(OpeningsCommand.GrundstellungBrett))
            .ExecuteUpdateAsync(s => s.SetProperty(g => g.OpeningLine, (string?)null));
        if (weg > 0) Console.WriteLine($"{weg:N0} Eroeffnungszeilen von Partien mit eigener Ausgangsstellung entfernt.");

        var ids = await db.GameAnalyses.AsNoTracking()
            .Where(g => g.OpeningLine == null && g.StartFen.StartsWith(OpeningsCommand.GrundstellungBrett))
            .OrderBy(g => g.Id)
            .Select(g => g.Id)
            .ToListAsync();

        var getan = 0;
        foreach (var id in ids)
        {
            var sans = await db.GameAnalysisPositions.AsNoTracking()
                .Where(p => p.GameAnalysisId == id)
                .OrderBy(p => p.Ply)
                .Take(30)
                .Select(p => p.GameMoveSan)
                .ToListAsync();
            if (sans.Count == 0) continue;

            var zeile = string.Join(' ', sans.Select(s => new string(
                s.Where(c => c is not ('+' or '#' or '!' or '?')).ToArray())));
            if (zeile.Length > 200) zeile = zeile[..200];

            var eintrag = new GameAnalysis { Id = id, OpeningLine = zeile };
            db.GameAnalyses.Attach(eintrag);
            db.Entry(eintrag).Property(g => g.OpeningLine).IsModified = true;
            getan++;
            if (getan % 200 == 0) { await db.SaveChangesAsync(); db.ChangeTracker.Clear(); }
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"Eroeffnungszeile nachgetragen fuer {getan:N0} von {ids.Count:N0} Partien.");
        return 0;
    }
}
