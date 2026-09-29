using System.Text.Json;
using System.Text.RegularExpressions;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Analyse-Verlauf des Analysebretts (0.603.0): „merk dir eine History der letzten 20 Analysen von jedem User — diese
/// sollen auch irgendwo ausgewählt werden können" und „innerhalb einer Analyse will ich Stellungen mit einem Stern
/// markieren, damit ich schnell zu diesen springen kann".
///
/// <para>Das Brett speichert gedrosselt, solange man arbeitet; eine Analyse ist eine Ausgangsstellung samt Zugfolge. Die
/// Oberfläche hält die Kennung ihres Eintrags und schickt sie mit — sonst entstünde bei jedem Zug ein neuer. Ohne Kennung
/// (neue Analyse) wird ein Eintrag mit GLEICHER Stellung und GLEICHEN Zügen wiederverwendet: dieselbe Partie zweimal aus
/// „Meine Partien" geöffnet ist eine Analyse, nicht zwei. Die Züge werden nachgespielt; eine Zugfolge, die dort nicht geht,
/// ist ein 400 und kein halber Eintrag.</para>
///
/// <para>Seit 0.604.0 („so hätt ichs bei uns auch gern in der Analyse, inkl. der Variationen + Hauptlinie") ist eine
/// Analyse ein ZUGBAUM: Hauptlinie, Varianten, Sterne und die zuletzt gesehenen Bewertungen, in der flachen Form von
/// <see cref="AnalysisTreeDto"/> (<see cref="CheckTree"/> prüft jeden Zug in seiner Stellung). <see cref="AnalysisHistoryEntry.Moves"/>
/// bleibt die Hauptlinie.</para>
/// </summary>
public sealed class AnalysisHistoryService
{
    public const int MaxPerUser = 20;
    public const int MaxPlies = 600;
    public const int MaxStars = 60;
    /// <summary>Züge im ganzen Baum — jeder wird beim Speichern nachgespielt, und gespeichert wird alle 1,5 s.</summary>
    public const int MaxNodes = 1500;
    public const int PreviewPlies = 8;

    private static readonly JsonSerializerOptions TreeJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly Regex EvalPattern = new(@"^(#-?\d{1,3}|[+-]?\d{1,3}\.\d{1,2})$", RegexOptions.CultureInvariant);

    private readonly AppDbContext _db;

    public AnalysisHistoryService(AppDbContext db) { _db = db; }

    public async Task<List<AnalysisHistoryEntryDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        var rows = await _db.AnalysisHistoryEntries.AsNoTracking().Where(h => h.UserId == userId)
            .OrderByDescending(h => h.UpdatedAt).ThenByDescending(h => h.Id).Take(MaxPerUser).ToListAsync(ct);
        return rows.Select(r => ToDto(r, withTree: false)).ToList();
    }

    public async Task<AnalysisHistoryEntryDto?> GetAsync(int userId, int id, CancellationToken ct = default)
    {
        var row = await _db.AnalysisHistoryEntries.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId, ct);
        return row is null ? null : ToDto(row, withTree: true);
    }

    /// <summary>Anlegen oder fortschreiben; wirft <see cref="ArgumentException"/> bei unlesbarer Stellung oder einem Baum,
    /// der nicht aufgeht (<see cref="CheckTree"/>).</summary>
    public async Task<AnalysisHistoryEntryDto> SaveAsync(int userId, SaveAnalysisHistoryRequest req, CancellationToken ct = default)
    {
        var fen = (req.StartFen ?? string.Empty).Trim();
        if (fen.Length is 0 or > 120 || !AnalysisJobService.IsLegalFen(fen)) throw new ArgumentException("Invalid FEN");
        var tree = CheckTree(fen, req.Tree, req.Current);
        var joined = string.Join(' ', tree.Mainline);
        var title = string.IsNullOrWhiteSpace(req.Title) ? null : req.Title.Trim();
        if (title is { Length: > 200 }) title = title[..200];

        AnalysisHistoryEntry? row = null;
        if (req.Id is int id) row = await _db.AnalysisHistoryEntries.FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId, ct);
        row ??= await _db.AnalysisHistoryEntries.FirstOrDefaultAsync(h => h.UserId == userId && h.StartFen == fen && h.Moves == joined, ct);
        var now = DateTime.UtcNow;
        if (row is null)
        {
            row = new AnalysisHistoryEntry { UserId = userId, CreatedAt = now };
            _db.AnalysisHistoryEntries.Add(row);
        }
        row.StartFen = fen;
        row.Moves = joined;
        row.MoveCount = tree.Mainline.Count;
        row.TreeJson = JsonSerializer.Serialize(tree.Tree, TreeJson);
        row.Current = tree.Current;
        row.Ply = tree.Ply;
        row.NodeCount = tree.NodeCount;
        row.StarCount = tree.StarCount;
        row.Title = title ?? row.Title;
        row.UpdatedAt = now;
        await _db.SaveChangesAsync(ct);

        // Höchstens MaxPerUser — die zuletzt angefassten bleiben.
        var old = await _db.AnalysisHistoryEntries.Where(h => h.UserId == userId)
            .OrderByDescending(h => h.UpdatedAt).ThenByDescending(h => h.Id).Skip(MaxPerUser).ToListAsync(ct);
        if (old.Count > 0)
        {
            _db.AnalysisHistoryEntries.RemoveRange(old);
            await _db.SaveChangesAsync(ct);
        }
        return ToDto(row, withTree: true);
    }

    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var row = await _db.AnalysisHistoryEntries.FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId, ct);
        if (row is null) return false;
        _db.AnalysisHistoryEntries.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Ein geprüfter Baum: gespeicherte Form, Hauptlinie, der Knoten, an dem man stand, samt Tiefe, Zähler.</summary>
    internal sealed record CheckedTree(AnalysisTreeDto Tree, List<string> Mainline, int Current, int Ply, int NodeCount, int StarCount);

    /// <summary>
    /// Den Baum prüfen und in die gespeicherte Form bringen: jeder Zug wird in der Stellung seines Elternknotens gespielt
    /// (Rochade als e1g1), ein Zug, der dort nicht geht, ein Elternverweis nach vorn, derselbe Zug zweimal unter einem
    /// Knoten, mehr als <see cref="MaxNodes"/> Züge, <see cref="MaxPlies"/> Halbzüge Tiefe oder <see cref="MaxStars"/>
    /// Sterne sind ein <see cref="ArgumentException"/>. Unbrauchbare Bewertungen fallen still weg; ein unbekannter
    /// <paramref name="current"/> ist die Ausgangsstellung.
    /// </summary>
    internal static CheckedTree CheckTree(string fen, AnalysisTreeDto? tree, int current)
    {
        var input = tree?.N ?? [];
        if (input.Count > MaxNodes) throw new ArgumentException($"At most {MaxNodes} moves");
        var nodes = new List<AnalysisTreeNodeDto>(input.Count);
        var fens = new string[input.Count];
        var depth = new int[input.Count];
        var firstChild = new Dictionary<int, int>();
        var seen = new HashSet<(int, string)>();
        var stars = tree?.S == true ? 1 : 0;
        ChessBoard? board = null;
        var boardAt = int.MinValue;   // wessen Stellung das Brett gerade hält (-1 = Ausgangsstellung)
        try
        {
            for (var i = 0; i < input.Count; i++)
            {
                var n = input[i] ?? throw new ArgumentException("Invalid tree");
                var p = n.P;
                if (p < -1 || p >= i) throw new ArgumentException("Invalid tree");
                var d = p == -1 ? 1 : depth[p] + 1;
                if (d > MaxPlies) throw new ArgumentException($"At most {MaxPlies} moves in a line");
                // In der flachen Form folgt das erste Kind direkt auf seinen Elternknoten — nur an Verzweigungen neu laden.
                if (board is null || boardAt != p) board = ChessBoard.LoadFromFen(p == -1 ? fen : fens[p]);
                var move = MoveComparisonService.FindMove(board.Moves(), n.U) ?? throw new ArgumentException("Illegal move");
                var uci = GamePlies.ToUci(move);
                if (!seen.Add((p, uci))) throw new ArgumentException("Duplicate move");
                board.Move(move);
                fens[i] = board.ToFen();
                boardAt = i;
                depth[i] = d;
                firstChild.TryAdd(p, i);
                var starred = n.S == true;
                if (starred) stars++;
                var eval = n.E?.Trim();
                nodes.Add(new AnalysisTreeNodeDto { P = p, U = uci, S = starred ? true : null, E = eval is not null && EvalPattern.IsMatch(eval) ? eval : null });
            }
        }
        catch (ArgumentException) { throw; }
        catch (Exception) { throw new ArgumentException("Illegal move sequence"); }
        if (stars > MaxStars) throw new ArgumentException($"At most {MaxStars} stars");

        var mainline = new List<string>();
        for (var at = -1; firstChild.TryGetValue(at, out var c); at = c) mainline.Add(nodes[c].U);
        var cur = current >= 0 && current < nodes.Count ? current : -1;
        return new CheckedTree(new AnalysisTreeDto { S = tree?.S == true ? true : null, N = nodes }, mainline, cur,
            cur < 0 ? 0 : depth[cur], nodes.Count, stars);
    }

    /// <summary>Eine Zugfolge als Baum ohne Varianten (Einträge von 0.603.0 ohne <see cref="AnalysisHistoryEntry.TreeJson"/>).</summary>
    internal static AnalysisTreeDto ChainTree(IReadOnlyList<string> moves)
        => new() { N = moves.Select((u, i) => new AnalysisTreeNodeDto { P = i - 1, U = u }).ToList() };

    private static AnalysisHistoryEntryDto ToDto(AnalysisHistoryEntry h, bool withTree)
    {
        var moves = h.Moves.Length == 0 ? [] : h.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var preview = moves.Count == 0 ? "" : GameRecapService.Numbered(h.StartFen, GameMistakes.LineSans(h.StartFen, moves, PreviewPlies));
        AnalysisTreeDto? tree = null;
        if (withTree)
        {
            try { tree = h.TreeJson is null ? null : JsonSerializer.Deserialize<AnalysisTreeDto>(h.TreeJson, TreeJson); }
            catch (JsonException) { tree = null; }
            tree ??= ChainTree(moves);
        }
        var nodeCount = h.TreeJson is null ? moves.Count : h.NodeCount;
        return new AnalysisHistoryEntryDto(h.Id, h.StartFen, moves, h.Ply, h.Title, preview, h.MoveCount, nodeCount, h.StarCount,
            tree, h.TreeJson is null ? Math.Min(h.Ply, moves.Count) - 1 : h.Current, h.CreatedAt, h.UpdatedAt);
    }
}
