using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Pflege der normalisierten Puzzle-Themen-Tabellen (<see cref="Tag"/>/<see cref="PuzzleTag"/>) — aus
/// <see cref="PuzzleService"/> ausgegliedert: Tag-Sync beim Import, einmaliger Backfill bestehender
/// Puzzles und das Erkennen/Vergeben des „e.p. möglich, aber nicht gespielt"-Themes. Reine Schreib-/
/// Analyse-Logik ohne Bezug zum Puzzle-Abruf oder zur Statistik.
/// </summary>
public class PuzzleTaggingService
{
    private readonly AppDbContext _db;
    private readonly ILogger<PuzzleTaggingService> _logger;

    public PuzzleTaggingService(AppDbContext db, ILogger<PuzzleTaggingService> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Legt für eine Menge bereits gespeicherter Puzzles die Tag/PuzzleTag-Zeilen an (idempotent über den
    /// Tag-Cache). Wird beim Import genutzt. <paramref name="tagCache"/> (Name→Id) lebt über alle Batches.
    /// </summary>
    public async Task SyncPuzzleTagsAsync(List<Puzzle> puzzles, Dictionary<string, int> tagCache, CancellationToken ct)
    {
        static IEnumerable<string> Split(string? s) =>
            string.IsNullOrWhiteSpace(s) ? Array.Empty<string>()
            : s.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var newNames = puzzles.SelectMany(p => Split(p.Themes)).Distinct()
            .Where(n => !tagCache.ContainsKey(n)).ToList();
        if (newNames.Count > 0)
        {
            var existing = await _db.Tags.Where(t => newNames.Contains(t.Name)).ToListAsync(ct);
            foreach (var t in existing) tagCache[t.Name] = t.Id;
            var toCreate = newNames.Where(n => !tagCache.ContainsKey(n)).Select(n => new Tag { Name = n }).ToList();
            if (toCreate.Count > 0)
            {
                _db.Tags.AddRange(toCreate);
                await _db.SaveChangesAsync(ct);
                foreach (var t in toCreate) tagCache[t.Name] = t.Id;
            }
        }

        var links = new List<PuzzleTag>();
        foreach (var p in puzzles)
            foreach (var name in Split(p.Themes).Distinct())
                if (tagCache.TryGetValue(name, out var tagId))
                    links.Add(new PuzzleTag { PuzzleId = p.Id, TagId = tagId, Rating = p.Rating });
        if (links.Count > 0)
        {
            _db.PuzzleTags.AddRange(links);
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>
    /// Befüllt PuzzleTags für alle bereits importierten Puzzles (einmaliger Backfill, batchweise, idempotent).
    /// Bestehende Verknüpfungen werden übersprungen. Gibt die Zahl verarbeiteter Puzzles zurück.
    /// </summary>
    public async Task<int> BackfillPuzzleTagsAsync(int batchSize = 5000, CancellationToken ct = default)
    {
        var tagCache = await _db.Tags.ToDictionaryAsync(t => t.Name, t => t.Id, ct);
        int processed = 0, lastId = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await _db.Puzzles.Where(p => p.Id > lastId).OrderBy(p => p.Id).Take(batchSize)
                .Select(p => new { p.Id, p.Rating, p.Themes }).ToListAsync(ct);
            if (batch.Count == 0) break;
            lastId = batch[^1].Id;

            var ids = batch.Select(b => b.Id).ToList();
            var already = (await _db.PuzzleTags.Where(pt => ids.Contains(pt.PuzzleId))
                .Select(pt => pt.PuzzleId).Distinct().ToListAsync(ct)).ToHashSet();
            var todo = batch.Where(b => !already.Contains(b.Id))
                .Select(b => new Puzzle { Id = b.Id, Rating = b.Rating, Themes = b.Themes }).ToList();

            if (todo.Count > 0)
                await SyncPuzzleTagsAsync(todo, tagCache, ct);

            _db.ChangeTracker.Clear();
            processed += batch.Count;
        }
        _logger.LogInformation("PuzzleTags-Backfill abgeschlossen: {Count} Puzzles verarbeitet.", processed);
        return processed;
    }

    // ---- En-passant-„möglich, aber nicht gespielt"-Theme ------------------------------------

    /// <summary>
    /// Theme-Token für Puzzles, in deren Lösungslinie an mindestens einer Stellung ein En-passant-Schlag
    /// legal verfügbar war, der tatsächlich gespielte Zug dort aber KEIN En passant ist. Bewusst anders
    /// benannt als das Lichess-Theme <c>enPassant</c> (dort IST die Lösung ein e.p.-Schlag).
    /// </summary>
    public const string EnPassantPossibleTheme = "enPassantPossible";

    /// <summary>
    /// Rein/testbar: Läuft die UCI-Lösung ab der FEN durch und meldet <c>true</c>, sobald AN EINEM
    /// LÖSER-ZUG ein legaler En-passant-Schlag verfügbar war, der gespielte Zug selbst aber kein e.p.
    /// ist. Gegnerzüge (die vorgespielte Setup-Antwort) zählen NICHT.
    /// <para><paramref name="firstSolverPly"/> = 0-basierter Index des ERSTEN Löser-Zugs. Für
    /// Standard-Lichess-Puzzles ist das <c>1</c> (moves[0] = Setup-Zug des Gegners, Löser ab moves[1]);
    /// Löser-Züge sind dann die Indizes 1, 3, 5, …</para>
    /// Ungültige FEN/nicht spielbarer Zug ⇒ <c>false</c> (robust, kein Wurf).
    /// </summary>
    public static bool HasUnplayedEnPassant(string? fen, string? uciMoves, int firstSolverPly = 1)
    {
        if (string.IsNullOrWhiteSpace(fen) || string.IsNullOrWhiteSpace(uciMoves)) return false;
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var moves = uciMoves.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < moves.Length; i++)
            {
                var legal = board.Moves(generateSan: false);
                var played = Array.Find(legal, m => MoveToUci(m) == moves[i]);
                if (played is null) break; // UCI passt zu keinem legalen Zug → hier abbrechen

                // Nur an Zügen des Lösers prüfen (firstSolverPly, +2, +4, …).
                var solverTurn = i >= firstSolverPly && (i - firstSolverPly) % 2 == 0;
                if (solverTurn
                    && Array.Exists(legal, m => m.Parameter is MoveEnPassant)
                    && played.Parameter is not MoveEnPassant)
                    return true;

                board.Move(played);
            }
        }
        catch { /* ungültige FEN/UCI → als „nein" behandeln (Robustheit) */ }
        return false;
    }

    private static string MoveToUci(Move m)
    {
        var u = m.OriginalPosition.ToString() + m.NewPosition.ToString();
        var ss = m.Parameter?.ShortStr;
        if (!string.IsNullOrEmpty(ss) && ss.StartsWith('=') && ss.Length >= 2)
            u += char.ToLowerInvariant(ss[1]);
        return u;
    }

    /// <summary>
    /// Scannt (batchweise, idempotent) alle Standard-Puzzles und vergibt das <see cref="EnPassantPossibleTheme"/>
    /// an jene, in deren Lösung ein e.p.-Schlag möglich war, aber nicht gespielt wurde. Das Token wird
    /// sowohl in den <see cref="Puzzle.Themes"/>-String gehängt ALS AUCH als normalisierte Verknüpfung in
    /// die <c>PuzzleTags</c>-Tabelle geschrieben (Filter-Index). Bereits getaggte Puzzles werden übersprungen.
    /// Liefert (gescannt, neu getaggt).
    /// </summary>
    public async Task<(int Scanned, int Tagged)> TagEnPassantPossibleAsync(int batchSize = 2000, CancellationToken ct = default)
    {
        // Tag-Zeile sicherstellen (get-or-create) → TagId für die PuzzleTag-Verknüpfung.
        var tag = await _db.Tags.FirstOrDefaultAsync(t => t.Name == EnPassantPossibleTheme, ct);
        if (tag is null)
        {
            tag = new Tag { Name = EnPassantPossibleTheme };
            _db.Tags.Add(tag);
            await _db.SaveChangesAsync(ct);
        }
        var tagId = tag.Id;

        int scanned = 0, tagged = 0, lastId = 0;
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var batch = await _db.Puzzles.Where(p => p.Id > lastId).OrderBy(p => p.Id)
                .Take(batchSize).ToListAsync(ct);
            if (batch.Count == 0) break;
            lastId = batch[^1].Id;
            scanned += batch.Count;

            // Welche der Batch-Puzzles haben schon einen e.p.-Link? (idempotenter Re-Run)
            var batchIds = batch.Select(p => p.Id).ToList();
            var alreadyLinked = (await _db.PuzzleTags
                .Where(pt => pt.TagId == tagId && batchIds.Contains(pt.PuzzleId))
                .Select(pt => pt.PuzzleId).ToListAsync(ct)).ToHashSet();

            foreach (var p in batch)
            {
                if (alreadyLinked.Contains(p.Id)) continue;
                if (!HasUnplayedEnPassant(p.Fen, p.Moves)) continue;

                // 1) Denormalisierter Themes-String (falls Token fehlt anhängen).
                var themes = (p.Themes ?? string.Empty)
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (!themes.Contains(EnPassantPossibleTheme))
                    p.Themes = string.Join(' ', themes.Append(EnPassantPossibleTheme));
                // 2) Normalisierte Verknüpfung (2. Tabelle) für den Index-Filter.
                _db.PuzzleTags.Add(new PuzzleTag { PuzzleId = p.Id, TagId = tagId, Rating = p.Rating });
                tagged++;
            }

            await _db.SaveChangesAsync(ct);
            _db.ChangeTracker.Clear();
        }

        _logger.LogInformation(
            "En-passant-Theme-Scan abgeschlossen: {Scanned} Puzzles gescannt, {Tagged} neu getaggt ({Theme}).",
            scanned, tagged, EnPassantPossibleTheme);
        return (scanned, tagged);
    }
}
