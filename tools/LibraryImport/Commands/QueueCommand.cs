using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Library;
using RookHub.Tools.LibraryImport.Infrastructure;

namespace RookHub.Tools.LibraryImport.Commands;

/// <summary><c>queue</c> — die besten Partien zum Rechnen einreihen.</summary>
internal static class QueueCommand
{
    // ===== Einreihen ============================================================

    // Die besten Partien des Bestands zum Rechnen einreihen — der Massen-Weg zu dem, was auf der
    // Punktepartie-Seite der Knopf „Partie anfordern" je Partie tut.
    //
    //   queue [anzahl] --user <id> [--per-annotator n] [--depth d] [--dry-run]
    //   queue --game <bibliotheks-id> --user <id>      (genau diese eine Partie)
    //
    // Bewusst OHNE den Deckel von fuenf offenen Partien (GameAnalysisDefaults.MaxOpenGuessGamesPerUser):
    // der ist eine Fairness-Regel zwischen Nutzern an der Oberflaeche, hier fuellt der Betreiber seinen
    // eigenen Bestand vor. Die Reihenfolge bleibt trotzdem gewahrt — die Pumpe fuettert je Nutzer immer
    // nur EINE Partie weiter (GameAnalysisTurns.IsOwnersTurnAsync), zwanzig eingereihte Partien
    // laufen also nacheinander und nicht zwanzig gleichzeitig.
    //
    // Angelegt werden nur die Zeilen; die AUFTRAEGE macht die laufende API beim naechsten Pump-Durchgang
    // (GameAnalysisPumpService, Vorgabe alle 20 s). Genau deshalb startet dieses Werkzeug keine zweite
    // API-Instanz: die stritte sich mit dem Auftrags-Worker um die Engines.
    public static async Task<int> RunAsync(ToolContext ctx)
    {
        var count = int.TryParse(ctx.Options.Positional(1), out var n) ? n : ctx.Options.Int("--count") ?? 20;
        var userId = ctx.Options.Int("--user");
        var perAnnotator = ctx.Options.Int("--per-annotator") ?? LibraryPicks.DefaultPerAnnotator;
        var depth = ctx.Options.Int("--depth") ?? GameAnalysisDefaults.GuessTargetDepth;
        // Ein Durchgang bindet die Engine fuer Stunden — die Auswahl laesst sich vorher ansehen.
        var dryRun = ctx.Options.Has("--dry-run");
        if (userId is null || count <= 0)
        {
            Console.Error.WriteLine("Aufruf: queue [anzahl] --user <id> [--per-annotator n] [--depth d] [--dry-run]");
            return 1;
        }

        await using var db = ctx.Db.New();
        var comments = new CommentSetService(db, NullLogger<CommentSetService>.Instance);

        var engineOwner = await ResolveEngineOwnerAsync(db, userId.Value);
        if (engineOwner is null)
        {
            Console.Error.WriteLine($"Nutzer {userId} hat keine Hintergrund-Engine, und es gibt keine Haus-Engine.");
            return 1;
        }

        var picks = await PickAsync(db, ctx.Options.Int("--game"), count, perAnnotator);
        if (picks.Count == 0)
        {
            Console.WriteLine("Nichts einzureihen — der Bestand ist leer oder alles ist schon angefordert.");
            return 0;
        }

        int queued = 0, unplayable = 0;
        foreach (var game in picks)
        {
            var analysis = NewAnalysis(game, userId.Value, engineOwner.Value, depth);
            if (analysis is null) { unplayable++; continue; }

            if (!dryRun)
            {
                db.GameAnalyses.Add(analysis);
                await db.SaveChangesAsync();
                // Die Anmerkungen gleich in ihre Sprachen zerlegen — dasselbe tut die API beim
                // Einreihen ueber die Oberflaeche (GameAnalysisService.CreateAsync).
                await comments.EnsureSourceAsync(analysis.Id);
                queued++;
            }
            Console.WriteLine($"  #{(dryRun ? game.Id : analysis.Id),-6} {analysis.PlyCount,3} Halbzuege · {game.CommentedPlies,3} kommentiert · {game.Annotator} · {analysis.Title}");
        }

        Console.WriteLine((dryRun ? $"Probelauf: {picks.Count:N0} Partien waeren dran" : $"Eingereiht: {queued:N0} Partien")
            + $" (Tiefe {depth}, {GameAnalysisDefaults.MultiPv} Linien)"
            + (unplayable > 0 ? $" · {unplayable} ohne spielbare Zuege uebersprungen" : ""));
        return 0;
    }

    // Wer rechnet: erst die EIGENE Hintergrund-Engine des Nutzers, sonst die Haus-Engine eines
    // Admins — dieselbe Reihenfolge wie GameAnalysisIntake.ResolveGuessEngineOwnerAsync.
    private static async Task<int?> ResolveEngineOwnerAsync(AppDbContext db, int userId)
    {
        var own = await db.LichessEngineCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId);
        if (own is not null && own.BackgroundEngines.Count > 0) return userId;
        var house = await db.LichessEngineCredentials.AsNoTracking()
            .Where(c => c.ShareAsHouseEngine && c.BackgroundEngineIds != null && c.User!.IsAdmin)
            .OrderBy(c => c.UserId)
            .ToListAsync();
        return house.FirstOrDefault(c => c.BackgroundEngines.Count > 0)?.UserId;
    }

    private static async Task<List<LibraryGame>> PickAsync(AppDbContext db, int? wanted, int count, int perAnnotator)
    {
        // Was schon einmal angefordert wurde, wird nicht ein zweites Mal gerechnet — eine halbe Stunde
        // Engine-Zeit fuer ein vorhandenes Ergebnis ist der teuerste Weg, nichts zu gewinnen.
        var taken = (await db.GameAnalyses.AsNoTracking()
                .Where(a => a.LibraryGameId != null)
                .Select(a => a.LibraryGameId!.Value)
                .ToListAsync())
            .ToHashSet();

        // Ein Vielfaches der gesuchten Zahl holen: der Deckel je Kommentator wirft Zeilen weg, und ohne
        // Vorrat kaeme am Ende weniger heraus als verlangt.
        // Genau EINE benannte Partie — der Weg fuer „rechne mir diese hier", ohne die Rangfolge.
        var pool = wanted is int only
            ? await db.LibraryGames.AsNoTracking().Where(g => g.Id == only).ToListAsync()
            : await db.LibraryGames.AsNoTracking()
            .Where(g => g.Score != null && g.Status == LibraryGameStatus.New && g.GameAnalysisId == null)
            .OrderByDescending(g => g.Score)
            .ThenByDescending(g => g.CommentedPlies)
            .ThenByDescending(g => g.CommentChars)
            .ThenBy(g => g.Id)
            .Take(Math.Max(count * 20, 200))
            .ToListAsync();

        return wanted is not null
            ? pool
            : LibraryPicks.Best(pool.Where(g => !taken.Contains(g.Id)), count, perAnnotator);
    }

    /// <summary>Die Analyse-Zeile samt Stellungen; <c>null</c>, wenn das PGN keine spielbaren Zuege hat.</summary>
    private static GameAnalysis? NewAnalysis(LibraryGame game, int userId, int engineOwner, int depth)
    {
        var parsed = GamePlies.Parse(game.Pgn, GameAnalysisDefaults.MaxPlies);
        if (parsed is null) return null;
        var (header, plies) = parsed.Value;

        var analysis = new GameAnalysis
        {
            UserId = userId,
            Title = LibraryGameService.TitleOf(game),
            Pgn = game.Pgn,
            White = header.White,
            Black = header.Black,
            Result = header.Result,
            Event = header.Event,
            StartFen = header.StartFen,
            TargetDepth = depth,
            MultiPv = GameAnalysisDefaults.MultiPv,
            EngineOwnerUserId = engineOwner == userId ? null : engineOwner,
            Origin = GameAnalysisOrigin.Guess,
            LibraryGameId = game.Id,
            PlyCount = plies.Count,
            Status = GameAnalysisStatus.Pending,
        };
        foreach (var p in plies)
            analysis.Positions.Add(new GameAnalysisPosition
            {
                Ply = p.Index, Fen = p.Fen, GameMoveUci = p.Uci, GameMoveSan = p.San,
            });
        return analysis;
    }
}
