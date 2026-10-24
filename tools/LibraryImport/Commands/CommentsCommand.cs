using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>comments</c> — die Kommentare eingereihter Partien nach Sprachen trennen (auch <c>--probe</c>, <c>--library</c>, <c>--figurines</c>).</summary>
internal static class CommentsCommand
{
    // ===== Kommentare nach Sprachen trennen =====================================

    // Die Anmerkungen einer eingereihten Partie in ihre Sprachen zerlegen und getrennt vom PGN ablegen
    // (Tabellen CommentSets/CommentTexts).
    //
    //   comments [--limit n] [--probe n] [--library n] [--figurines]
    //
    // Fuer NEUE Partien passiert das von selbst beim Einreihen (GameAnalysisService.CreateAsync); dieser
    // Befehl holt den Altbestand nach. Wiederholbar: was schon Saetze hat, wird uebersprungen.
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var limit = ctx.Options.Int("--limit") ?? int.MaxValue;
        var probe = ctx.Options.Int("--probe");
        var library = ctx.Options.Int("--library");
        await using var db = ctx.Db.New();
        if (probe is int n) return await ProbeAsync(db, n);
        if (ctx.Options.Has("--figurines")) return await FigurinesAsync(db);
        if (library is int top) return await LibraryCommentsAsync(db, top);
        var service = new CommentSetService(db, NullLogger<CommentSetService>.Instance);

        // Nur Partien, die ueberhaupt jemand spielen kann — der Rohbestand hat 94 898 kommentierte
        // Partien, und die alle zu zerlegen waeren Millionen Zeilen fuer Partien ohne Analyse.
        var ids = await db.GameAnalyses.AsNoTracking()
            .OrderBy(g => g.Id)
            .Select(g => g.Id)
            .ToListAsync();

        int done = 0, sets = 0, skipped = 0;
        foreach (var id in ids)
        {
            if (done >= limit) break;
            var created = await service.EnsureSourceAsync(id);
            if (created == 0) { skipped++; continue; }
            done++;
            sets += created;
        }

        Console.WriteLine($"Partien zerlegt: {done:N0} ({sets:N0} Saetze) · uebersprungen {skipped:N0}");

        // Was dabei herausgekommen ist — die Zahl der ZWEISPRACHIGEN Partien ist die eigentliche Probe.
        var perGame = await db.CommentSets.AsNoTracking()
            .GroupBy(s => new { s.LibraryGameId, s.GameAnalysisId })
            .Select(g => g.Count())
            .ToListAsync();
        var languages = await db.CommentSets.AsNoTracking()
            .GroupBy(s => s.Language)
            .Select(g => new { Language = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .ToListAsync();
        Console.WriteLine($"Partien mit Saetzen: {perGame.Count:N0} · davon mehrsprachig {perGame.Count(c => c > 1):N0}");
        foreach (var l in languages) Console.WriteLine($"  {l.Language,-6} {l.Count,6:N0}");
        return 0;
    }

    // Probelauf am ROHBESTAND: wie oft trennt die Zerlegung einen zweisprachigen Block wirklich?
    // Schreibt NICHTS. Gebraucht, weil die Trennung eine Heuristik ist (das PGN traegt keine
    // Sprachauszeichnung) und ihr Ertrag nur am echten Text zu messen ist.
    private static async Task<int> ProbeAsync(AppDbContext db, int count)
    {
        var games = await db.LibraryGames.AsNoTracking()
            .Where(g => g.Languages != null && g.Languages.Contains(",") && g.CommentedPlies > 5)
            .OrderByDescending(g => g.Score)
            .Take(Math.Max(1, count))
            .Select(g => new { g.Id, g.Languages, g.Pgn })
            .ToListAsync();

        int blocks = 0, split = 0, shown = 0, missed = 0, longBlocks = 0, longSplit = 0;
        var perPair = new Dictionary<string, (int Blocks, int Split)>(StringComparer.Ordinal);

        foreach (var g in games)
        {
            var languages = (g.Languages ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var moveText = PgnParser.SplitGames(g.Pgn).FirstOrDefault().MoveText;
            if (moveText is null) continue;
            var comments = PgnParser.ExtractMoveComments(moveText) ?? new Dictionary<int, string>();

            foreach (var (_, text) in comments)
            {
                var parts = CommentSplit.Split(text, languages);
                blocks++;
                var two = parts.Count > 1;
                if (two) split++;
                var key = g.Languages ?? "?";
                var cur = perPair.GetValueOrDefault(key);
                perPair[key] = (cur.Blocks + 1, cur.Split + (two ? 1 : 0));

                // Lange Bloecke sind die interessanten: ein kurzes „!" ist zu Recht einsprachig.
                if (text.Length > 300)
                {
                    longBlocks++;
                    if (two) longSplit++;
                    else if (missed < 2)
                    {
                        missed++;
                        Console.WriteLine($"--- NICHT getrennt, Partie {g.Id} ({g.Languages}) ---");
                        Console.WriteLine($"  {text[..Math.Min(400, text.Length)]}");
                    }
                }

                if (two && shown < 2 && text.Length is > 200 and < 700)
                {
                    shown++;
                    Console.WriteLine($"--- Partie {g.Id} ({g.Languages}) ---");
                    foreach (var (lang, part) in parts)
                        Console.WriteLine($"  [{lang}] {part}");
                }
            }
        }

        Console.WriteLine($"Bloecke {blocks:N0} · getrennt {split:N0} ({(blocks == 0 ? 0 : 100.0 * split / blocks):N1} %)");
        Console.WriteLine($"davon lang (>300 Zeichen) {longBlocks:N0} · getrennt {longSplit:N0} "
            + $"({(longBlocks == 0 ? 0 : 100.0 * longSplit / longBlocks):N1} %)");
        foreach (var (pair, v) in perPair.OrderByDescending(x => x.Value.Blocks))
            Console.WriteLine($"  {pair,-8} {v.Split,6:N0} von {v.Blocks,6:N0}");
        return 0;
    }

    // Die besten Partien des ROHBESTANDS zerlegen, lange bevor jemand sie anfordert: der Text haengt
    // nicht an der Engine, und eine angeforderte Partie ist damit sofort in beiden Sprachen da statt
    // erst nach einer halben Stunde Rechnen.
    private static async Task<int> LibraryCommentsAsync(AppDbContext db, int top)
    {
        var service = new CommentSetService(db, NullLogger<CommentSetService>.Instance);

        // Was schon Saetze hat, wird nicht noch einmal gelesen.
        var known = (await db.CommentSets.AsNoTracking()
                .Where(s => s.LibraryGameId != null)
                .Select(s => s.LibraryGameId!.Value)
                .Distinct()
                .ToListAsync())
            .ToHashSet();

        var ids = await db.LibraryGames.AsNoTracking()
            .Where(g => g.Score != null && g.CommentedPlies > 0 && g.Status == LibraryGameStatus.New)
            .OrderByDescending(g => g.Score)
            .ThenByDescending(g => g.CommentedPlies)
            .ThenByDescending(g => g.CommentChars)
            .ThenBy(g => g.Id)
            .Select(g => g.Id)
            .Take(top + known.Count)
            .ToListAsync();

        int done = 0, sets = 0;
        var started = DateTime.UtcNow;
        foreach (var id in ids)
        {
            if (done >= top) break;
            if (known.Contains(id)) continue;
            var created = await service.EnsureSourceForLibraryAsync(id);
            if (created == 0) continue;
            done++;
            sets += created;
            if (done % 100 == 0) Console.WriteLine($"  {done:N0} zerlegt ({Throughput.Rate(started, done)})");
        }
        Console.WriteLine($"Rohbestand zerlegt: {done:N0} Partien · {sets:N0} Saetze · Dauer {DateTime.UtcNow - started:hh\\:mm\\:ss}");
        return 0;
    }

    // Die ChessBase-Figurenzeichen im VORHANDENEN Bestand in Buchstaben aufloesen.
    //
    //   comments --figurines
    //
    // Neu angelegte Saetze machen das von selbst (CommentSetService). Dieser Befehl holt nach, was
    // vorher schon drinsteht — und das ist viel: die Zeichen stehen im privaten Unicode-Bereich und
    // sind ohne die ChessBase-Schrift unsichtbar, „I can't win the pawn due to the h7+ trick" ist in
    // Wahrheit „…due to the Bh7+ trick".
    private static async Task<int> FigurinesAsync(AppDbContext db)
    {
        // Gesucht wird in C# und nicht in SQL: „enthaelt eines von sechs Zeichen" laesst sich mit
        // MariaDBs Standard-Sortierung nicht verlaesslich fragen (Privatzeichen vergleichen sich dort
        // nicht so, wie man denkt), und die Tabelle ist klein genug, sie einmal durchzusehen.
        var alle = await db.CommentTexts
            .Select(t => new { t.Id, t.Text, Sprache = t.CommentSet!.Language })
            .ToListAsync();

        // Der zusammengeschriebene Bauer ist ein Nachlass des ersten Laufs: dort wurde „…e5" zu
        // „Bauere5" statt „Bauer e5". Wiederholbar heisst hier auch: den eigenen Fehler aufraeumen.
        var geklebt = new System.Text.RegularExpressions.Regex(
            @"\b(pawn|Bauer|pion|peón|pedone|peão|pionek|pěšec|gyalog|bonde|pješak)([a-h][1-8])\b");

        var geaendert = 0;
        foreach (var zeile in alle)
        {
            var neu = Figurines.Apply(zeile.Text, zeile.Sprache);
            neu = geklebt.Replace(neu, "$1 $2");
            if (neu == zeile.Text) continue;
            var entity = new CommentText { Id = zeile.Id, Text = neu };
            db.CommentTexts.Attach(entity);
            db.Entry(entity).Property(t => t.Text).IsModified = true;
            geaendert++;
            if (geaendert % 500 == 0) { await db.SaveChangesAsync(); db.ChangeTracker.Clear(); }
        }
        await db.SaveChangesAsync();
        Console.WriteLine($"Figurenzeichen aufgeloest in {geaendert:N0} von {alle.Count:N0} Zeilen.");
        return 0;
    }
}
