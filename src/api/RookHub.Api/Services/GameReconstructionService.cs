using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// „Partie rekonstruieren": Bruchstücke einer Partie aufzeichnen, ergänzen und ordnen.
///
/// <para>Jede schreibende Operation gibt die GANZE Rekonstruktion zurück. Grund: die Auswertung der Kette
/// (<see cref="ReconstructionChain"/>) hängt an allen Teilen gemeinsam — ein eingefügtes Teil in der
/// Mitte ändert Gültigkeit und Halbzug-Nummern aller folgenden. Ein DTO nur des geänderten Teils
/// wäre danach an mehreren Stellen falsch.</para>
/// </summary>
public class GameReconstructionService
{
    /// <summary>Mehr Rekonstruktionen legt ein Konto nicht an (Schutz vor Müll, nicht vor Nutzung).</summary>
    public const int MaxPerUser = 50;

    /// <summary>Mehr Teile hat keine Partie — 200 Bruchstücke sind schon mehr als Halbzüge üblich sind.</summary>
    public const int MaxParts = 200;

    private readonly AppDbContext _db;
    public GameReconstructionService(AppDbContext db) => _db = db;

    public async Task<List<ReconstructionListItemDto>> ListAsync(int userId, CancellationToken ct = default)
    {
        var rows = await _db.GameReconstructions
            .Where(r => r.UserId == userId)
            .Include(r => r.Parts)
            .OrderByDescending(r => r.UpdatedAt)
            .ToListAsync(ct);
        return rows.Select(r => ToListItem(r, ReconstructionChain.Analyze(r.Parts))).ToList();
    }

    public async Task<ReconstructionDetailDto?> GetAsync(int userId, int id, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        return row == null ? null : ToDetail(row);
    }

    /// <summary>Legt eine leere Rekonstruktion an. Wirft, wenn der Deckel erreicht ist.</summary>
    public async Task<ReconstructionDetailDto> CreateAsync(int userId, ReconstructionHeadRequest req, CancellationToken ct = default)
    {
        var count = await _db.GameReconstructions.CountAsync(r => r.UserId == userId, ct);
        if (count >= MaxPerUser) throw new InvalidOperationException("too-many");

        var row = new GameReconstruction { UserId = userId };
        ApplyHead(row, req);
        _db.GameReconstructions.Add(row);
        await _db.SaveChangesAsync(ct);
        return ToDetail(row);
    }

    public async Task<ReconstructionDetailDto?> UpdateHeadAsync(int userId, int id, ReconstructionHeadRequest req, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        if (row == null) return null;
        ApplyHead(row, req);
        await SaveTouchedAsync(row, ct);
        return ToDetail(row);
    }

    public async Task<bool> DeleteAsync(int userId, int id, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        if (row == null) return false;
        _db.GameReconstructionParts.RemoveRange(row.Parts);
        _db.GameReconstructions.Remove(row);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Hängt ein Teil ans Ende. Wirft <see cref="ArgumentException"/> bei unbrauchbarem Inhalt.</summary>
    public async Task<ReconstructionDetailDto?> AddPartAsync(int userId, int id, ReconstructionPartRequest req, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        if (row == null) return null;
        if (row.Parts.Count >= MaxParts) throw new InvalidOperationException("too-many-parts");

        var part = new GameReconstructionPart
        {
            GameReconstructionId = row.Id,
            Ordinal = row.Parts.Count == 0 ? 0 : row.Parts.Max(p => p.Ordinal) + 1,
        };
        ApplyPart(part, req);
        row.Parts.Add(part);
        _db.GameReconstructionParts.Add(part);
        await SaveTouchedAsync(row, ct);
        return ToDetail(row);
    }

    public async Task<ReconstructionDetailDto?> UpdatePartAsync(int userId, int id, int partId, ReconstructionPartRequest req, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        var part = row?.Parts.FirstOrDefault(p => p.Id == partId);
        if (row == null || part == null) return null;
        ApplyPart(part, req);
        part.UpdatedAt = DateTime.UtcNow;
        await SaveTouchedAsync(row, ct);
        return ToDetail(row);
    }

    public async Task<ReconstructionDetailDto?> DeletePartAsync(int userId, int id, int partId, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        var part = row?.Parts.FirstOrDefault(p => p.Id == partId);
        if (row == null || part == null) return null;
        row.Parts.Remove(part);
        _db.GameReconstructionParts.Remove(part);
        Renumber(row);
        await SaveTouchedAsync(row, ct);
        return ToDetail(row);
    }

    /// <summary>
    /// Setzt die Reihenfolge neu. Ids, die nicht zur Rekonstruktion gehören, werden ignoriert;
    /// FEHLENDE Teile bleiben in ihrer bisherigen Reihenfolge hinten dran — eine unvollständige
    /// Liste darf nichts verschwinden lassen.
    /// </summary>
    public async Task<ReconstructionDetailDto?> ReorderAsync(int userId, int id, IEnumerable<int> partIds, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        if (row == null) return null;

        var byId = row.Parts.ToDictionary(p => p.Id);
        var ordered = new List<GameReconstructionPart>();
        foreach (var partId in partIds.Distinct())
            if (byId.TryGetValue(partId, out var part)) { ordered.Add(part); byId.Remove(partId); }
        ordered.AddRange(row.Parts.Where(p => byId.ContainsKey(p.Id)).OrderBy(p => p.Ordinal));

        for (var i = 0; i < ordered.Count; i++) ordered[i].Ordinal = i;
        await SaveTouchedAsync(row, ct);
        return ToDetail(row);
    }

    // ----- intern -----

    private Task<GameReconstruction?> LoadAsync(int userId, int id, CancellationToken ct) =>
        _db.GameReconstructions
            .Include(r => r.Parts)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);

    private static void ApplyHead(GameReconstruction row, ReconstructionHeadRequest req)
    {
        var title = (req.Title ?? string.Empty).Trim();
        row.Title = title.Length == 0 ? "?" : Cut(title, 200);
        row.White = Clean(req.White, 120);
        row.Black = Clean(req.Black, 120);
        row.Event = Clean(req.Event, 200);
        row.PlayedOn = req.PlayedOn;
        row.Result = Clean(req.Result, 12);
        row.Note = Clean(req.Note, 2000);
    }

    /// <summary>Inhalt eines Teils prüfen und normalisieren (Zugnummern raus, FEN muss ladbar sein).</summary>
    private static void ApplyPart(GameReconstructionPart part, ReconstructionPartRequest req)
    {
        part.Kind = req.Kind;
        part.FromPly = req.FromPly is >= 0 and <= 600 ? req.FromPly : null;
        part.ContinuesPrevious = req.ContinuesPrevious;
        part.Note = Clean(req.Note, 500);

        if (req.Kind == ReconstructionPartKind.Position)
        {
            var fen = (req.Fen ?? string.Empty).Trim();
            if (!ReconstructionChain.IsLoadableFen(fen)) throw new ArgumentException("invalid-fen");
            part.Fen = fen;
            part.Moves = null;
            return;
        }

        var moves = ReconstructionChain.SplitMoves(req.Moves);
        if (moves.Count == 0) throw new ArgumentException("no-moves");
        part.Moves = Cut(string.Join(' ', moves), 4000);
        part.Fen = null;
    }

    private static void Renumber(GameReconstruction row)
    {
        var i = 0;
        foreach (var part in row.Parts.OrderBy(p => p.Ordinal)) part.Ordinal = i++;
    }

    private async Task SaveTouchedAsync(GameReconstruction row, CancellationToken ct)
    {
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static ReconstructionListItemDto ToListItem(GameReconstruction row, ReconstructionChain.Result chain) => new()
    {
        Id = row.Id,
        Title = row.Title,
        White = row.White,
        Black = row.Black,
        Event = row.Event,
        PlayedOn = row.PlayedOn,
        Result = row.Result,
        PartCount = row.Parts.Count,
        KnownPlies = chain.KnownPlies,
        Gaps = chain.Gaps,
        UpdatedAt = row.UpdatedAt,
    };

    private static ReconstructionDetailDto ToDetail(GameReconstruction row)
    {
        var chain = ReconstructionChain.Analyze(row.Parts);
        var byId = chain.Parts.ToDictionary(c => c.PartId);
        var dto = new ReconstructionDetailDto
        {
            Id = row.Id, Title = row.Title, White = row.White, Black = row.Black, Event = row.Event,
            PlayedOn = row.PlayedOn, Result = row.Result, Note = row.Note,
            PartCount = row.Parts.Count, KnownPlies = chain.KnownPlies, Gaps = chain.Gaps,
            UpdatedAt = row.UpdatedAt, PrefixSan = chain.PrefixSan,
        };
        foreach (var part in row.Parts.OrderBy(p => p.Ordinal))
        {
            byId.TryGetValue(part.Id, out var c);
            dto.Parts.Add(new ReconstructionPartDto
            {
                Id = part.Id, Ordinal = part.Ordinal, Kind = part.Kind, Moves = part.Moves, Fen = part.Fen,
                FromPly = part.FromPly, ContinuesPrevious = part.ContinuesPrevious, Note = part.Note,
                Anchored = c?.Anchored ?? false, Valid = c?.Valid ?? false,
                StartFen = c?.StartFen, EndFen = c?.EndFen, PlyCount = c?.PlyCount ?? 0,
                StartPly = c?.StartPly, FirstBadMove = c?.FirstBadMove, Mismatch = c?.Mismatch ?? false,
            });
        }
        return dto;
    }

    private static string? Clean(string? value, int max)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length == 0 ? null : Cut(text, max);
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}
