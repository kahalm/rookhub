using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>tree</c> — den Eroeffnungsbaum einer Stellung gegen die echte Datenbank nachsehen.</summary>
internal static class TreeCommand
{
    // ===== Eroeffnungsbaum nachsehen ============================================

    //   tree [zeile] [--all]
    //
    // Zeigt, was `GET /api/guess-tree` zu dieser Stellung liefert — gegen die ECHTE Datenbank.
    // Gebraucht, weil die Zaehlung auf MariaDBs SUBSTRING_INDEX baut: die InMemory-Datenbank der
    // Tests kennt es nicht und nimmt dort den clientseitigen Weg, eine kaputte SQL-Fassung faellt
    // also in keinem Test auf (siehe CLAUDE.md zur InMemory-Luecke).
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var zeile = string.Join(' ', ctx.Options.All.Skip(1).Where(a => !a.StartsWith("--")));
        var nurGerechnete = !ctx.Options.Has("--all");

        await using var db = ctx.Db.New();
        var uhr = System.Diagnostics.Stopwatch.StartNew();
        var ast = await new GuessOpeningTree(db).BranchAsync(zeile, nurGerechnete);
        uhr.Stop();

        Console.WriteLine($"Stellung [{(ast.Line.Length == 0 ? "Grundstellung" : ast.Line)}] "
                        + $"({(nurGerechnete ? "nur gerechnete" : "alle Partien")}): "
                        + $"{ast.Total:N0} Partien, {uhr.ElapsedMilliseconds} ms");
        foreach (var m in ast.Moves.Take(12))
            Console.WriteLine($"  {m.San,-8}{m.Games,10:N0}");
        if (ast.Moves.Sum(m => (long)m.Games) > ast.Total)
            Console.WriteLine("WARNUNG: die Zuege summieren sich hoeher als die Gesamtzahl.");
        return 0;
    }
}
