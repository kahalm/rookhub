using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RookHub.Api.Services;
using RookHub.Tools.LibraryImport;

// ---------------------------------------------------------------------------
// Rohbestand befuellen. Vier Schritte, jeder fuer sich wiederholbar:
//
//   import <datei>   Partien einlesen (Kopfdaten, Kommentar-Merkmale, Zug-Hash)
//   dedupe           gleiche Zugfolgen zusammenfassen (DuplicateOfId, Status)
//   openings         Eroeffnungszeile + ersten kommentierten Halbzug nachtragen
//   languages        Sprache der Kommentare bestimmen
//   score            Eignungsnote fuer die Punktepartie berechnen
//   queue            die besten Partien zum Rechnen einreihen
//   comments         die Kommentare eingereihter Partien nach Sprachen trennen
//   translate        fehlende Sprachen uebersetzen lassen (Anthropic:ApiKey noetig)
//   stats            zeigen, was drinsteht
//
// Verbindung ueber ConnectionStrings__DefaultConnection. Laeuft NICHT als API-Instanz —
// ein zweiter RookHub.Api gegen dieselbe Datenbank streitet sich mit dem Auftrags-Worker
// um die Engines. Hier oeffnet nur ein DbContext.
// ---------------------------------------------------------------------------

var command = args.FirstOrDefault() ?? "stats";
var connection = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("ConnectionStrings__DefaultConnection fehlt.");
    return 1;
}

AppDbContext NewDb()
{
    var options = new DbContextOptionsBuilder<AppDbContext>()
        .UseMySql(connection, new MariaDbServerVersion(new Version(11, 0, 0)))
        .EnableSensitiveDataLogging(false)
        .Options;
    var db = new AppDbContext(options);
    db.ChangeTracker.AutoDetectChangesEnabled = false;
    return db;
}

switch (command)
{
    case "import": return await ImportAsync();
    case "dedupe": return await DedupeAsync();
    case "openings": return await OpeningsAsync();
    case "languages": return await LanguagesAsync();
    case "score": return await ScoreAsync();
    case "queue": return await QueueAsync();
    case "comments": return await CommentsAsync();
    case "translate": return await TranslateAsync();
    case "stats": return await StatsAsync();
    default:
        Console.Error.WriteLine($"Unbekannter Befehl: {command}");
        return 1;
}

// ===== 1. Einlesen ==========================================================

async Task<int> ImportAsync()
{
    var path = args.ElementAtOrDefault(1);
    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
    {
        Console.Error.WriteLine("Aufruf: import <pfad-zur-pgn> [--limit n]");
        return 1;
    }
    var limit = IntArg("--limit") ?? int.MaxValue;
    var sourceFile = Path.GetFileName(path);
    const int batchSize = 500;

    // Wiederholbar: was aus DIESER Datei schon drinsteht, kommt nicht ein zweites Mal hinein.
    // Der Griff ist der Zug-Hash zusammen mit dem Kommentator — dieselbe Partie von zwei Leuten
    // kommentiert sind zwei Zeilen, dieselbe Partie zweimal eingelesen ist eine.
    HashSet<string> known;
    await using (var db = NewDb())
    {
        known = (await db.LibraryGames
                .Where(g => g.SourceFile == sourceFile)
                .Select(g => g.MovesHash + "" + (g.Annotator ?? ""))
                .ToListAsync())
            .ToHashSet(StringComparer.Ordinal);
    }
    if (known.Count > 0)
        Console.WriteLine($"{known.Count:N0} Partien aus {sourceFile} liegen schon im Bestand — die werden uebersprungen.");

    var started = DateTime.UtcNow;
    int read = 0, written = 0, skippedEmpty = 0, skippedKnown = 0;
    var batch = new List<LibraryGame>(batchSize);
    var db2 = NewDb();

    try
    {
        foreach (var game in PgnFileReader.Read(path))
        {
            if (read >= limit) break;
            read++;

            var row = LibraryGameReader.From(game.Pgn, game.Headers, game.MoveText, sourceFile);
            if (row is null) { skippedEmpty++; continue; }

            var key = row.MovesHash + "" + (row.Annotator ?? "");
            if (!known.Add(key)) { skippedKnown++; continue; }

            batch.Add(row);
            if (batch.Count >= batchSize)
            {
                written += await FlushAsync(batch);
                batch.Clear();
                await db2.DisposeAsync();
                db2 = NewDb();
                if (written % 10000 == 0)
                    Console.WriteLine($"  {written:N0} geschrieben ({Rate(started, written)})");
            }
        }
        if (batch.Count > 0) written += await FlushAsync(batch);
    }
    finally { await db2.DisposeAsync(); }

    Console.WriteLine($"Gelesen {read:N0} · geschrieben {written:N0} · ohne Zuege {skippedEmpty:N0} · schon bekannt {skippedKnown:N0}");
    Console.WriteLine($"Dauer {DateTime.UtcNow - started:hh\\:mm\\:ss}");
    return 0;

    async Task<int> FlushAsync(List<LibraryGame> rows)
    {
        db2.LibraryGames.AddRange(rows);
        await db2.SaveChangesAsync();
        return rows.Count;
    }
}

// ===== 2. Dubletten =========================================================

async Task<int> DedupeAsync()
{
    // Dieselbe Zugfolge kommt in einer Zeitschriften-Sammlung mehrfach vor, jedes Mal von
    // jemand anderem kommentiert. Behalten wird die Fassung mit den MEISTEN kommentierten
    // Halbzuegen — das ist die, die fuer die Punktepartie etwas hergibt. Die anderen bleiben
    // stehen und zeigen auf sie; geloescht wird nichts, die zweite Meinung kann die bessere sein.
    await using var db = NewDb();
    var groups = await db.LibraryGames
        .Where(g => g.MovesHash != null && g.Status != LibraryGameStatus.Imported)
        .GroupBy(g => g.MovesHash)
        .Where(x => x.Count() > 1)
        .Select(x => x.Key)
        .ToListAsync();

    Console.WriteLine($"{groups.Count:N0} Zugfolgen kommen mehrfach vor.");
    var marked = 0;
    foreach (var chunk in groups.Chunk(200))
    {
        await using var scope = NewDb();
        var rows = await scope.LibraryGames
            .Where(g => chunk.Contains(g.MovesHash) && g.Status != LibraryGameStatus.Imported)
            .ToListAsync();

        foreach (var group in rows.GroupBy(r => r.MovesHash))
        {
            var keeper = group
                .OrderByDescending(r => r.CommentedPlies ?? 0)
                .ThenByDescending(r => r.CommentChars ?? 0)
                .ThenBy(r => r.Id)
                .First();
            foreach (var other in group.Where(r => r.Id != keeper.Id))
            {
                if (other.Status == LibraryGameStatus.Duplicate && other.DuplicateOfId == keeper.Id) continue;
                other.Status = LibraryGameStatus.Duplicate;
                other.DuplicateOfId = keeper.Id;
                other.UpdatedAt = DateTime.UtcNow;
                marked++;
            }
            // Der Behalter koennte aus einem frueheren Lauf als Dublette markiert sein.
            if (keeper.Status == LibraryGameStatus.Duplicate)
            {
                keeper.Status = LibraryGameStatus.New;
                keeper.DuplicateOfId = null;
                keeper.UpdatedAt = DateTime.UtcNow;
            }
        }
        scope.ChangeTracker.DetectChanges();
        await scope.SaveChangesAsync();
    }
    Console.WriteLine($"{marked:N0} Zeilen als Dublette markiert.");
    return 0;
}

// ===== 2b. Eroeffnungszeile + erster Kommentar ==============================

async Task<int> OpeningsAsync()
{
    // Nachtrag fuer den Altbestand: beide Spalten fallen beim Einlesen ohnehin an, es gab sie nur
    // noch nicht. Gerechnet wird aus dem gespeicherten PGN — die Quelldatei wird nicht gebraucht.
    var done = 0;
    var lastId = 0;
    while (true)
    {
        await using var db = NewDb();
        var rows = await db.LibraryGames
            .Where(g => g.Id > lastId && g.OpeningLine == null)
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

// ===== 3. Sprache ===========================================================

async Task<int> LanguagesAsync()
{
    // Der Text einer Partie steht im PGN, und das ist die grosse Spalte — geholt wird deshalb
    // seitenweise und nur einmal. Geschrieben wird GRUPPIERT: 130 000 einzelne UPDATEs waeren
    // 130 000 Umlaeufe, waehrend es je Seite nur eine Handvoll verschiedener Sprachen gibt.
    var done = 0;
    while (true)
    {
        await using var db = NewDb();
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

// ===== 4. Eignungsnote ======================================================

async Task<int> ScoreAsync()
{
    // Auch hier gruppiert geschrieben: die Note ist eine Zahl von 0 bis 100, je Seite gibt es also
    // hoechstens hundert verschiedene Werte — und damit hoechstens hundert UPDATEs statt fuenftausend.
    var done = 0;
    var lastId = 0;
    while (true)
    {
        await using var db = NewDb();
        var rows = await db.LibraryGames
            .Where(g => g.Id > lastId)
            .OrderBy(g => g.Id)
            .Take(5000)
            .Select(g => new
            {
                g.Id, g.PlyCount, g.CommentedPlies, g.CommentChars, g.VariationCount,
                g.Result, g.WhiteElo, g.BlackElo,
            })
            .ToListAsync();
        if (rows.Count == 0) break;
        lastId = rows[^1].Id;

        var byScore = new Dictionary<int, List<int>>();
        foreach (var r in rows)
        {
            var score = GuessSuitability.Score(r.PlyCount, r.CommentedPlies, r.CommentChars,
                r.VariationCount, r.Result, r.WhiteElo, r.BlackElo);
            if (!byScore.TryGetValue(score, out var ids)) byScore[score] = ids = new List<int>();
            ids.Add(r.Id);
        }

        foreach (var (score, ids) in byScore)
            await db.LibraryGames.Where(g => ids.Contains(g.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.Score, score));

        done += rows.Count;
        if (done % 25000 == 0) Console.WriteLine($"  {done:N0} bewertet");
    }
    Console.WriteLine($"Note berechnet fuer {done:N0} Partien.");
    return 0;
}

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
// nur EINE Partie weiter (GameAnalysisService.IsOwnersTurnAsync), zwanzig eingereihte Partien
// laufen also nacheinander und nicht zwanzig gleichzeitig.
//
// Angelegt werden nur die Zeilen; die AUFTRAEGE macht die laufende API beim naechsten Pump-Durchgang
// (GameAnalysisPumpService, Vorgabe alle 20 s). Genau deshalb startet dieses Werkzeug keine zweite
// API-Instanz: die stritte sich mit dem Auftrags-Worker um die Engines.
async Task<int> QueueAsync()
{
    var count = int.TryParse(args.ElementAtOrDefault(1), out var n) ? n : IntArg("--count") ?? 20;
    var userId = IntArg("--user");
    var perAnnotator = IntArg("--per-annotator") ?? LibraryPicks.DefaultPerAnnotator;
    var depth = IntArg("--depth") ?? GameAnalysisDefaults.GuessTargetDepth;
    // Ein Durchgang bindet die Engine fuer Stunden — die Auswahl laesst sich vorher ansehen.
    var dryRun = args.Contains("--dry-run");
    if (userId is null || count <= 0)
    {
        Console.Error.WriteLine("Aufruf: queue [anzahl] --user <id> [--per-annotator n] [--depth d] [--dry-run]");
        return 1;
    }

    await using var db = NewDb();
    var comments = new CommentSetService(db, NullLogger<CommentSetService>.Instance);

    // Wer rechnet: erst die EIGENE Hintergrund-Engine des Nutzers, sonst die Haus-Engine eines
    // Admins — dieselbe Reihenfolge wie GameAnalysisService.ResolveGuessEngineOwnerAsync.
    var own = await db.LichessEngineCredentials.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId);
    int? engineOwner = own is not null && own.BackgroundEngines.Count > 0 ? userId : null;
    if (engineOwner is null)
    {
        var house = await db.LichessEngineCredentials.AsNoTracking()
            .Where(c => c.ShareAsHouseEngine && c.BackgroundEngineIds != null && c.User!.IsAdmin)
            .OrderBy(c => c.UserId)
            .ToListAsync();
        engineOwner = house.FirstOrDefault(c => c.BackgroundEngines.Count > 0)?.UserId;
    }
    if (engineOwner is null)
    {
        Console.Error.WriteLine($"Nutzer {userId} hat keine Hintergrund-Engine, und es gibt keine Haus-Engine.");
        return 1;
    }

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
    var wanted = IntArg("--game");
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

    var picks = wanted is not null
        ? pool
        : LibraryPicks.Best(pool.Where(g => !taken.Contains(g.Id)), count, perAnnotator);
    if (picks.Count == 0)
    {
        Console.WriteLine("Nichts einzureihen — der Bestand ist leer oder alles ist schon angefordert.");
        return 0;
    }

    int queued = 0, unplayable = 0;
    foreach (var game in picks)
    {
        var parsed = GamePlies.Parse(game.Pgn, GameAnalysisDefaults.MaxPlies);
        if (parsed is null) { unplayable++; continue; }
        var (header, plies) = parsed.Value;

        var analysis = new GameAnalysis
        {
            UserId = userId.Value,
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

        if (!dryRun)
        {
            db.GameAnalyses.Add(analysis);
            await db.SaveChangesAsync();
            // Die Anmerkungen gleich in ihre Sprachen zerlegen — dasselbe tut die API beim
            // Einreihen ueber die Oberflaeche (GameAnalysisService.CreateAsync).
            await comments.EnsureSourceAsync(analysis.Id);
            queued++;
        }
        Console.WriteLine($"  #{(dryRun ? game.Id : analysis.Id),-6} {plies.Count,3} Halbzuege · {game.CommentedPlies,3} kommentiert · {game.Annotator} · {analysis.Title}");
    }

    Console.WriteLine((dryRun ? $"Probelauf: {picks.Count:N0} Partien waeren dran" : $"Eingereiht: {queued:N0} Partien")
        + $" (Tiefe {depth}, {GameAnalysisDefaults.MultiPv} Linien)"
        + (unplayable > 0 ? $" · {unplayable} ohne spielbare Zuege uebersprungen" : ""));
    return 0;
}

// ===== Kommentare nach Sprachen trennen =====================================

// Die Anmerkungen einer eingereihten Partie in ihre Sprachen zerlegen und getrennt vom PGN ablegen
// (Tabellen CommentSets/CommentTexts).
//
//   comments [--limit n] [--probe n] [--library n]
//
// Fuer NEUE Partien passiert das von selbst beim Einreihen (GameAnalysisService.CreateAsync); dieser
// Befehl holt den Altbestand nach. Wiederholbar: was schon Saetze hat, wird uebersprungen.
async Task<int> CommentsAsync()
{
    var limit = IntArg("--limit") ?? int.MaxValue;
    var probe = IntArg("--probe");
    var library = IntArg("--library");
    await using var db = NewDb();
    if (probe is int n) return await ProbeAsync(db, n);
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
async Task<int> ProbeAsync(AppDbContext db, int count)
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
async Task<int> LibraryCommentsAsync(AppDbContext db, int top)
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
        if (done % 100 == 0) Console.WriteLine($"  {done:N0} zerlegt ({Rate(started, done)})");
    }
    Console.WriteLine($"Rohbestand zerlegt: {done:N0} Partien · {sets:N0} Saetze · Dauer {DateTime.UtcNow - started:hh\\:mm\\:ss}");
    return 0;
}

// ===== Uebersetzen ==========================================================

// Die Anmerkungen eingereihter Partien in eine weitere Sprache uebersetzen lassen.
//
//   translate --to de [--limit n] [--force] [--game <analyse-id>] [--library n]
//
// Braucht ANTHROPIC__APIKEY (bzw. Anthropic:ApiKey) in der Umgebung — ohne Schluessel passiert
// nichts. Uebersetzt wird immer aus der QUELLE, nie aus einer Uebersetzung, und die Quelle wird
// nie ueberschrieben: es entsteht ein eigener Satz mit Herkunft „Machine".
async Task<int> TranslateAsync()
{
    var target = StringArg("--to");
    if (string.IsNullOrWhiteSpace(target))
    {
        Console.Error.WriteLine("Aufruf: translate --to <sprache> [--limit n] [--force] [--game <analyse-id>]");
        return 1;
    }
    var limit = IntArg("--limit") ?? int.MaxValue;
    var force = args.Contains("--force");
    var one = IntArg("--game");

    var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
    await using var db = NewDb();
    var claude = new ClaudeJsonClient(config, NullLogger<ClaudeJsonClient>.Instance);
    if (!claude.IsConfigured)
    {
        Console.Error.WriteLine("Anthropic:ApiKey fehlt — ohne Schluessel wird nicht uebersetzt.");
        return 1;
    }
    var service = new CommentTranslationService(db, claude, NullLogger<CommentTranslationService>.Instance);

    // Der Rohbestand (Saetze ohne Analyse) oder die eingereihten Partien.
    var library = IntArg("--library");
    List<int> ids;
    var fromLibrary = library is not null;
    if (fromLibrary)
    {
        // Nur Partien, die ueberhaupt Saetze haben und die Zielsprache noch nicht fuehren —
        // sonst laeuft der Durchgang durch zehntausende Zeilen, um nichts zu tun.
        var have = await db.CommentSets.AsNoTracking()
            .Where(s => s.Language == target && s.LibraryGameId != null)
            .Select(s => s.LibraryGameId!.Value).ToListAsync();
        var known = have.ToHashSet();
        ids = (await db.CommentSets.AsNoTracking()
                .Where(s => s.LibraryGameId != null)
                .Select(s => s.LibraryGameId!.Value)
                .Distinct()
                .ToListAsync())
            .Where(id => !known.Contains(id))
            .Take(library!.Value)
            .ToList();
    }
    else
    {
        ids = one is int only ? [only] : await db.GameAnalyses.AsNoTracking()
            .OrderBy(g => g.Id).Select(g => g.Id).ToListAsync();
    }

    int done = 0, lines = 0;
    var started = DateTime.UtcNow;
    foreach (var id in ids)
    {
        if (done >= limit) break;
        var written = fromLibrary
            ? await service.TranslateLibraryGameAsync(id, target!, force)
            : await service.TranslateAsync(id, target!, force);
        if (written == 0) continue;
        done++;
        lines += written;
        Console.WriteLine($"  #{id,-6} {written,3} Anmerkungen nach {target}");
    }
    Console.WriteLine($"Uebersetzt: {done:N0} Partien · {lines:N0} Zeilen · Dauer {DateTime.UtcNow - started:hh\\:mm\\:ss}");
    return 0;
}

// ===== Uebersicht ===========================================================

async Task<int> StatsAsync()
{
    await using var db = NewDb();
    var total = await db.LibraryGames.CountAsync();
    Console.WriteLine($"Partien im Bestand: {total:N0}");
    if (total == 0) return 0;

    foreach (var g in await db.LibraryGames.GroupBy(x => x.Status)
                 .Select(x => new { Status = x.Key, Count = x.Count() }).ToListAsync())
        Console.WriteLine($"  {g.Status,-12} {g.Count,8:N0}");

    Console.WriteLine($"mit Kommentaren : {await db.LibraryGames.CountAsync(g => g.CommentedPlies > 0):N0}");
    Console.WriteLine($"mit Eroeffnung  : {await db.LibraryGames.CountAsync(g => g.OpeningLine != null):N0}");
    Console.WriteLine($"mit 1. Kommentar: {await db.LibraryGames.CountAsync(g => g.FirstCommentedPly != null):N0}");
    Console.WriteLine($"mit Sprache     : {await db.LibraryGames.CountAsync(g => g.Languages != null):N0}");
    Console.WriteLine($"mit Note        : {await db.LibraryGames.CountAsync(g => g.Score != null):N0}");
    return 0;
}

string? StringArg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}

int? IntArg(string name)
{
    var i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var v) ? v : null;
}

static string Rate(DateTime started, int done)
{
    var seconds = (DateTime.UtcNow - started).TotalSeconds;
    return seconds <= 0 ? "" : $"{done / seconds:N0}/s";
}
