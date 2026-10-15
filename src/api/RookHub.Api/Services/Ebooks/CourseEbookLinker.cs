using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Ebooks;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Kurs ↔ E-Book (Wunsch 2026-10-15): ordnet jede Kurslinie dem erkannten Diagramm im Buch zu, damit der Löser „Im
/// Buch“ öffnen kann. Abgleich über die Figurenstellung (FEN-Feld 1) — der Linie selbst ODER nach ihrem ersten Zug
/// (Chessable-Linien beginnen oft mit dem Gegnerzug, der die Aufgabe erst herstellt; das Buch zeigt die Stellung danach).
/// Steht dieselbe Stellung mehrfach im Buch, gewinnt das erste Diagramm ab der Stelle der vorigen Linie (Kurs und Buch
/// laufen in derselben Reihenfolge). Ohne Buchangabe wird unter allen sichtbaren Büchern mit Diagrammen das mit den
/// meisten Treffern genommen (mindestens <see cref="MinAutoMatches"/>).
/// </summary>
public class CourseEbookLinker
{
    /// <summary>So viele Treffer braucht ein Buch mindestens, damit die Suche es nimmt (Zufallstreffer der
    /// Grundstellung u. Ä. sollen kein Buch verknüpfen).</summary>
    public const int MinAutoMatches = 3;

    private readonly AppDbContext _db;

    public CourseEbookLinker(AppDbContext db) => _db = db;

    public async Task<CourseEbookLinkResultDto> LinkAsync(int userId, int bookId, bool isAdmin, int? ebookId,
        CancellationToken ct = default)
    {
        await CourseAccess.LoadManageableAsync(_db, userId, bookId, isAdmin, ct);
        var lines = await _db.BookPuzzles.Where(bp => bp.BookId == bookId).InReadingOrder()
            .Select(bp => new { bp.Id, bp.Fen, bp.Moves }).ToListAsync(ct);
        var keys = lines.Select(l => (l.Id, Keys: LineKeys(l.Fen, l.Moves))).ToList();

        var visible = EbookAccess.Visible(_db, userId, isAdmin).Where(e => e.DiagramState == EbookDiagramState.Done);
        if (ebookId != null) visible = visible.Where(e => e.Id == ebookId);
        var diagrams = await _db.EbookDiagrams.Where(d => visible.Any(e => e.Id == d.EbookId))
            .OrderBy(d => d.EbookId).ThenBy(d => d.Page).ThenBy(d => d.Y).ThenBy(d => d.X)
            .Select(d => new { d.Id, d.EbookId, d.Fen, d.Page }).ToListAsync(ct);
        if (ebookId != null && diagrams.Count == 0 && !await visible.AnyAsync(ct))
            throw new NotFoundException("E-book not found.");

        // Je Buch: Stellung → Diagramme in Lesereihenfolge (Index in der Buchliste).
        var byBook = diagrams.GroupBy(d => d.EbookId).ToDictionary(g => g.Key, g => g.Select(d => (d.Id, Key: Placement(d.Fen))).ToList());
        int? best = null; List<(int LineId, int DiagramId)> bestPairs = new();
        foreach (var (eid, list) in byBook)
        {
            var pairs = Match(keys, list);
            if (pairs.Count > bestPairs.Count) { best = eid; bestPairs = pairs; }
        }
        var info = diagrams.ToDictionary(d => d.Id);
        var result = new CourseEbookLinkResultDto { Lines = lines.Count };
        if (best == null || (ebookId == null && bestPairs.Count < MinAutoMatches)) return result;

        // Verweise dieses Kurses auf DIESES Buch ersetzen (andere Bücher bleiben).
        var lineIds = lines.Select(l => l.Id).ToList();
        _db.CourseLineEbookRefs.RemoveRange(await _db.CourseLineEbookRefs
            .Where(r => lineIds.Contains(r.BookPuzzleId) && r.EbookId == best).ToListAsync(ct));
        var now = DateTime.UtcNow;
        _db.CourseLineEbookRefs.AddRange(bestPairs.Select(p => new CourseLineEbookRef
        {
            BookPuzzleId = p.LineId, EbookId = best.Value, EbookDiagramId = p.DiagramId,
            Placement = Placement(info[p.DiagramId].Fen), Page = info[p.DiagramId].Page, CreatedAt = now,
        }));
        await _db.SaveChangesAsync(ct);

        result.EbookId = best;
        result.EbookTitle = await _db.Ebooks.Where(e => e.Id == best).Select(e => e.Title).FirstOrDefaultAsync(ct);
        result.Linked = bestPairs.Count;
        return result;
    }

    /// <summary>Verweise aller Linien des Kurses auf Bücher, die der Aufrufer sehen darf. 404 ohne Kurszugang.
    /// Verweise, deren Diagramm verschwunden ist oder nicht mehr dieselbe Stellung zeigt (Buch neu gelesen), werden
    /// vorher über die Stellung neu zugeordnet; was sich nicht wiederfindet, fehlt in der Antwort (bleibt aber gespeichert).</summary>
    public async Task<List<CourseLineEbookRefDto>> GetRefsAsync(int userId, int bookId, bool isAdmin, CancellationToken ct = default)
    {
        if (!await CourseAccess.CanAccessAsync(_db, userId, bookId, isAdmin, ct)) throw new NotFoundException("Book not found.");
        var visible = EbookAccess.Visible(_db, userId, isAdmin).Select(e => e.Id);
        var mine = _db.CourseLineEbookRefs.Where(r => r.BookPuzzle!.BookId == bookId && visible.Contains(r.EbookId));

        var check = await mine.Select(r => new { r.EbookId, r.Placement, Fen = r.EbookDiagram == null ? null : r.EbookDiagram.Fen }).ToListAsync(ct);
        foreach (var eid in check.Where(c => c.Fen == null || Placement(c.Fen) != c.Placement).Select(c => c.EbookId).Distinct())
            await RelinkEbookAsync(_db, eid, ct);

        return await mine.Where(r => r.EbookDiagramId != null)
            .OrderBy(r => r.Id)
            .Select(r => new CourseLineEbookRefDto
            {
                LineId = r.BookPuzzleId, EbookId = r.EbookId, EbookTitle = r.Ebook!.Title,
                DiagramId = r.EbookDiagramId!.Value, Page = r.EbookDiagram!.Page, Label = r.EbookDiagram.Label,
            })
            .ToListAsync(ct);
    }

    /// <summary>
    /// Hängt alle Kurs-Verweise auf das Buch <paramref name="ebookId"/> an dessen AKTUELLE Diagramme: ein Verweis behält
    /// sein Diagramm, solange es dieselbe Stellung zeigt; sonst gewinnt das Diagramm mit derselben Stellung auf der
    /// nächstgelegenen Seite (bei Gleichstand das frühere). Findet sich keines, bleibt der Verweis mit <c>null</c> liegen
    /// und kommt beim nächsten Neulesen wieder. Aufgerufen nach dem Neulesen (Worker) und beim Abruf.
    /// </summary>
    public static async Task<int> RelinkEbookAsync(AppDbContext db, int ebookId, CancellationToken ct = default)
    {
        var refs = await db.CourseLineEbookRefs.Where(r => r.EbookId == ebookId).ToListAsync(ct);
        if (refs.Count == 0) return 0;
        var diagrams = await db.EbookDiagrams.Where(d => d.EbookId == ebookId)
            .OrderBy(d => d.Page).ThenBy(d => d.Y).ThenBy(d => d.X)
            .Select(d => new { d.Id, d.Page, d.Fen }).ToListAsync(ct);
        var byId = diagrams.ToDictionary(d => d.Id);
        var byKey = diagrams.GroupBy(d => Placement(d.Fen)).ToDictionary(g => g.Key, g => g.ToList());
        int changed = 0;
        foreach (var r in refs)
        {
            if (r.EbookDiagramId is int cur && byId.TryGetValue(cur, out var still) && Placement(still.Fen) == r.Placement) continue;
            int? next = byKey.TryGetValue(r.Placement, out var cands)
                ? cands.OrderBy(d => Math.Abs(d.Page - r.Page)).First().Id
                : null;
            if (next == r.EbookDiagramId) continue;
            r.EbookDiagramId = next;
            changed++;
        }
        if (changed > 0) await db.SaveChangesAsync(ct);
        return changed;
    }

    /// <summary>Linien in Kursreihenfolge gegen die Diagramme EINES Buchs (Lesereihenfolge).</summary>
    internal static List<(int LineId, int DiagramId)> Match(IReadOnlyList<(int Id, string[] Keys)> lines, IReadOnlyList<(int Id, string Key)> diagrams)
    {
        var index = new Dictionary<string, List<int>>();
        for (int i = 0; i < diagrams.Count; i++)
        {
            if (!index.TryGetValue(diagrams[i].Key, out var l)) index[diagrams[i].Key] = l = new List<int>();
            l.Add(i);
        }
        var pairs = new List<(int, int)>();
        int cursor = 0;
        foreach (var (lineId, keys) in lines)
        {
            int pick = -1;
            foreach (var k in keys)
            {
                if (!index.TryGetValue(k, out var idx)) continue;
                var after = idx.FirstOrDefault(i => i >= cursor, -1);
                pick = after >= 0 ? after : idx[0];
                break;
            }
            if (pick < 0) continue;
            pairs.Add((lineId, diagrams[pick].Id));
            cursor = pick;
        }
        return pairs;
    }

    /// <summary>Figurenstellung der Linie und nach ihrem ersten Zug (UCI).</summary>
    internal static string[] LineKeys(string fen, string? moves)
    {
        var first = Placement(fen);
        var move = (moves ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var after = move == null ? null : PlacementAfter(first, move);
        return after == null ? new[] { first } : new[] { first, after };
    }

    internal static string Placement(string fen) => fen.Trim().Split(' ')[0];

    /// <summary>Figurenstellung nach einem UCI-Zug (Rochade, en passant, Umwandlung); <c>null</c> bei Unsinn.</summary>
    internal static string? PlacementAfter(string placement, string uci)
    {
        if (uci.Length < 4) return null;
        var b = new char[8, 8];   // [Reihe 0 = 8. Reihe, Linie]
        var rows = placement.Split('/');
        if (rows.Length != 8) return null;
        for (int r = 0; r < 8; r++)
        {
            int f = 0;
            foreach (var ch in rows[r])
            {
                if (char.IsDigit(ch)) { for (int k = 0; k < ch - '0' && f < 8; k++) b[r, f++] = '.'; }
                else if (f < 8) b[r, f++] = ch;
            }
            for (; f < 8; f++) b[r, f] = '.';
        }
        int ff = uci[0] - 'a', fr = 8 - (uci[1] - '0'), tf = uci[2] - 'a', tr = 8 - (uci[3] - '0');
        if (ff is < 0 or > 7 || tf is < 0 or > 7 || fr is < 0 or > 7 || tr is < 0 or > 7) return null;
        var p = b[fr, ff];
        if (p == '.') return null;
        if (char.ToLower(p) == 'p' && ff != tf && b[tr, tf] == '.') b[fr, tf] = '.';            // en passant
        if (char.ToLower(p) == 'k' && Math.Abs(tf - ff) == 2)                                    // Rochade: Turm mitziehen
        {
            int rf = tf > ff ? 7 : 0, rt = tf > ff ? 5 : 3;
            b[fr, rt] = b[fr, rf]; b[fr, rf] = '.';
        }
        b[fr, ff] = '.';
        b[tr, tf] = uci.Length >= 5 ? (char.IsUpper(p) ? char.ToUpper(uci[4]) : char.ToLower(uci[4])) : p;
        var sb = new System.Text.StringBuilder();
        for (int r = 0; r < 8; r++)
        {
            int empty = 0;
            for (int f = 0; f < 8; f++)
            {
                if (b[r, f] == '.') { empty++; continue; }
                if (empty > 0) { sb.Append(empty); empty = 0; }
                sb.Append(b[r, f]);
            }
            if (empty > 0) sb.Append(empty);
            if (r < 7) sb.Append('/');
        }
        return sb.ToString();
    }
}
