using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;
using RookHub.Api.Services;
using RookHub.Tools.LibraryImport;

// ---------------------------------------------------------------------------
// Rohbestand befuellen. Vier Schritte, jeder fuer sich wiederholbar:
//
//   import <datei>   Partien einlesen (Kopfdaten, Kommentar-Merkmale, Zug-Hash)
//   dedupe           gleiche Zugfolgen zusammenfassen (DuplicateOfId, Status)
//   languages        Sprache der Kommentare bestimmen
//   score            Eignungsnote fuer die Punktepartie berechnen
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
    case "languages": return await LanguagesAsync();
    case "score": return await ScoreAsync();
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
    Console.WriteLine($"mit Sprache     : {await db.LibraryGames.CountAsync(g => g.Languages != null):N0}");
    Console.WriteLine($"mit Note        : {await db.LibraryGames.CountAsync(g => g.Score != null):N0}");
    return 0;
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
