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
/// </summary>
public sealed class AnalysisHistoryService
{
    public const int MaxPerUser = 20;
    public const int MaxPlies = 600;
    public const int MaxStars = 60;
    public const int PreviewPlies = 8;

    private readonly AppDbContext _db;

    public AnalysisHistoryService(AppDbContext db) { _db = db; }

    public async Task<List<AnalysisHistoryEntryDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        var rows = await _db.AnalysisHistoryEntries.AsNoTracking().Where(h => h.UserId == userId)
            .OrderByDescending(h => h.UpdatedAt).ThenByDescending(h => h.Id).Take(MaxPerUser).ToListAsync(ct);
        return rows.Select(ToDto).ToList();
    }

    public async Task<AnalysisHistoryEntryDto?> GetAsync(int userId, int id, CancellationToken ct = default)
    {
        var row = await _db.AnalysisHistoryEntries.AsNoTracking().FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId, ct);
        return row is null ? null : ToDto(row);
    }

    /// <summary>Anlegen oder fortschreiben; wirft <see cref="ArgumentException"/> bei unlesbarer Stellung/Zugfolge.</summary>
    public async Task<AnalysisHistoryEntryDto> SaveAsync(int userId, SaveAnalysisHistoryRequest req, CancellationToken ct = default)
    {
        var fen = (req.StartFen ?? string.Empty).Trim();
        if (fen.Length is 0 or > 120 || !AnalysisJobService.IsLegalFen(fen)) throw new ArgumentException("Invalid FEN");
        var raw = req.Moves ?? [];
        if (raw.Count > MaxPlies) throw new ArgumentException($"At most {MaxPlies} moves");
        var moves = Replay(fen, raw) ?? throw new ArgumentException("Illegal move sequence");
        var joined = string.Join(' ', moves);
        var ply = Math.Clamp(req.Ply, 0, moves.Count);
        var stars = (req.Starred ?? []).Where(p => p >= 0 && p <= moves.Count).Distinct().Order().Take(MaxStars).ToList();
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
        row.MoveCount = moves.Count;
        row.Ply = ply;
        row.Title = title ?? row.Title;
        row.Starred = stars.Count == 0 ? null : string.Join(',', stars);
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
        return ToDto(row);
    }

    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var row = await _db.AnalysisHistoryEntries.FirstOrDefaultAsync(h => h.Id == id && h.UserId == userId, ct);
        if (row is null) return false;
        _db.AnalysisHistoryEntries.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Die Züge nachspielen und in der Form von chess.js zurückgeben (Rochade e1g1); <c>null</c>, wenn einer nicht geht.</summary>
    internal static List<string>? Replay(string fen, IReadOnlyList<string> ucis)
    {
        var result = new List<string>(ucis.Count);
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            foreach (var uci in ucis)
            {
                var move = MoveComparisonService.FindMove(board.Moves(), uci);
                if (move is null) return null;
                result.Add(GamePlies.ToUci(move));
                board.Move(move);
            }
        }
        catch { return null; }
        return result;
    }

    private static AnalysisHistoryEntryDto ToDto(AnalysisHistoryEntry h)
    {
        var moves = h.Moves.Length == 0 ? [] : h.Moves.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        var stars = string.IsNullOrEmpty(h.Starred) ? [] : h.Starred.Split(',').Select(int.Parse).ToList();
        var preview = moves.Count == 0 ? "" : GameRecapService.Numbered(h.StartFen, GameMistakes.LineSans(h.StartFen, moves, PreviewPlies));
        return new AnalysisHistoryEntryDto(h.Id, h.StartFen, moves, h.Ply, h.Title, stars, preview, h.MoveCount, h.CreatedAt, h.UpdatedAt);
    }
}
