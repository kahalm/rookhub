using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RookHub.Api.Models;
using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>resplit</c> — als einsprachig vermerkte, aber zweisprachige Partien nachtraeglich zerlegen.</summary>
internal static class ResplitCommand
{
    // ===== Zweisprachige Partien nachtraeglich zerlegen (0.560.1) ===============
    //
    //   resplit [--dry-run] [--limit n] [--game <id>]
    //
    // `languages` nennt eine Partie nur zweisprachig, wenn die zweite Sprache 60 % der Funktionswoerter der ersten
    // erreicht — an streng parallelen en/de-Bloecken kommt das Deutsche nur auf 46–67 % (die englische Liste trifft mehr
    // sehr haeufige Woerter). Diese Partien stehen als „en" im Bestand, ihre Kommentare wurden nie zerlegt, und die
    // Uebersetzung nach de verwarf sie als „zu wenig Text" (das Modell uebernahm die schon vorhandene deutsche Haelfte).
    // Geprueft wird jede einsprachig vermerkte Partie mit Kommentaren (CommentSetService.ResplitLibraryAsync), zerlegt
    // nur, wo die Aufteilung fuer mindestens die Haelfte der Zuege beide Sprachen findet. Wiederholbar: was zweisprachig
    // vermerkt ist, faellt beim naechsten Lauf aus der Auswahl.
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var dryRun = ctx.Options.Has("--dry-run");
        var limit = ctx.Options.Int("--limit") ?? int.MaxValue;
        var one = ctx.Options.Int("--game");
        using var loggers = ToolLoggers.WarningsToConsole();

        List<int> ids;
        await using (var db = ctx.Db.New())
            ids = one is int only ? [only] : await db.LibraryGames.AsNoTracking()
                .Where(g => g.CommentedPlies > 0
                            && g.Status != LibraryGameStatus.Rejected && g.Status != LibraryGameStatus.Duplicate
                            && g.Languages != null && g.Languages != "und" && !g.Languages.Contains(","))
                .OrderBy(g => g.Id).Select(g => g.Id).ToListAsync();
        ids = ids.Take(limit).ToList();

        var counts = new SortedDictionary<ResplitStatus, int>();
        int removed = 0, examined = 0;
        var started = DateTime.UtcNow;
        foreach (var chunk in ids.Chunk(200))
        {
            await using var db = ctx.Db.New();
            var svc = new CommentSetService(db, loggers.CreateLogger<CommentSetService>());
            foreach (var id in chunk)
            {
                var r = await svc.ResplitLibraryAsync(id, dryRun);
                counts[r.Status] = counts.GetValueOrDefault(r.Status) + 1;
                removed += r.RemovedMachineSets;
                examined++;
                if (r.Status is ResplitStatus.Applied or ResplitStatus.WouldApply or ResplitStatus.Conflict)
                    Console.WriteLine($"  #{id,-6} {r.Status,-10} {r.Languages} ({r.FirstPlies}/{r.SecondPlies} Zuege)"
                                      + (r.RemovedMachineSets > 0 ? $", {r.RemovedMachineSets} maschinelle(r) Satz/Saetze" : ""));
                db.ChangeTracker.Clear();
            }
            if (examined % 5000 < chunk.Length) Console.WriteLine($"  … {examined:N0} von {ids.Count:N0} geprueft");
        }
        Console.WriteLine((dryRun ? "PROBELAUF, nichts geschrieben. " : "")
            + $"Geprueft {examined:N0}: " + string.Join(" · ", counts.Select(k => $"{k.Key} {k.Value:N0}"))
            + $" · maschinelle Saetze {(dryRun ? "wuerden weichen" : "gewichen")}: {removed:N0}"
            + $" · Dauer {DateTime.UtcNow - started:hh\\:mm\\:ss}");
        return 0;
    }
}
