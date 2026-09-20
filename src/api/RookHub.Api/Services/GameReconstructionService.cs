using System.Security.Cryptography;
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

        var before = req.InsertBeforePartId is int beforeId
            ? row.Parts.FirstOrDefault(p => p.Id == beforeId)
            : null;
        var part = new GameReconstructionPart
        {
            GameReconstructionId = row.Id,
            Ordinal = before?.Ordinal ?? (row.Parts.Count == 0 ? 0 : row.Parts.Max(p => p.Ordinal) + 1),
        };
        ApplyPart(part, req);
        if (before != null)
            foreach (var later in row.Parts.Where(p => p.Ordinal >= before.Ordinal)) later.Ordinal++;
        row.Parts.Add(part);
        _db.GameReconstructionParts.Add(part);
        Renumber(row);
        await SaveTouchedAsync(row, ct);
        return ToDetail(row);
    }

    public async Task<ReconstructionDetailDto?> UpdatePartAsync(int userId, int id, int partId, ReconstructionPartRequest req, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        var part = row?.Parts.FirstOrDefault(p => p.Id == partId);
        if (row == null || part == null) return null;
        ApplyPart(part, req);
        // Was ein Mensch angefasst hat, ist kein Vorschlag mehr — auch dann nicht, wenn er nur
        // eine Kleinigkeit geändert hat. („dann passe ich sie an und die generierte Linie ist weg")
        part.Generated = false;
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

    /// <summary>
    /// Sucht die Züge, die die Lücke VOR dem Teil <paramref name="partId"/> schließen.
    ///
    /// <para>Gesucht wird von der Stellung am Ende des vorigen Teils zur Stellung dieses Teils —
    /// beides muss also bekannt sein. Das Ziel ist deshalb immer ein STELLUNGS-Teil: eine Zugfolge
    /// nach einer Lücke hat selbst keine bekannte Ausgangsstellung, und genau die wäre das Ziel.</para>
    /// </summary>
    public async Task<ReconstructionGapResultDto?> SolveGapAsync(int userId, int id, int partId, int? maxPlies, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        if (row == null) return null;
        // Vorschläge stehen zwar in der Liste, sind aber keine Aufzeichnung — das Teil DAVOR ist
        // immer das letzte aufgezeichnete, sonst suchte die zweite Suche ab einem Vorschlag.
        var ordered = Recorded(row);
        var index = ordered.FindIndex(p => p.Id == partId);
        if (index < 0) return null;

        var plies = Math.Clamp(maxPlies ?? GapSolver.DefaultMaxPlies, 1, GapSolver.MaxSearchPlies);
        var dto = new ReconstructionGapResultDto { PartId = partId, MaxPlies = plies };

        var part = ordered[index];
        if (index == 0) { dto.Reason = "no-previous"; return dto; }
        if (part.ContinuesPrevious) { dto.Reason = "no-gap"; return dto; }
        if (part.Kind != ReconstructionPartKind.Position) { dto.Reason = "target-not-a-position"; return dto; }

        var chain = ReconstructionChain.Analyze(row.Parts);
        var previous = chain.Parts.FirstOrDefault(c => c.PartId == ordered[index - 1].Id);
        dto.FromFen = previous?.EndFen;
        dto.ToFen = part.Fen;
        if (dto.FromFen == null) { dto.Reason = "no-anchor"; return dto; }

        var result = GapSolver.Solve(dto.FromFen, dto.ToFen, plies);
        dto.Nodes = result.Nodes;
        dto.BudgetExhausted = result.BudgetExhausted;
        dto.DeepestSearched = result.DeepestSearched;
        dto.Reason = result.Reason;
        dto.Solutions = result.Solutions
            .Select(x => new ReconstructionGapSolutionDto { San = x.San, Plies = x.Plies })
            .ToList();
        return dto;
    }

    /// <summary>
    /// Übernimmt einen Weg durch die Lücke: die Züge kommen als eigenes Teil VOR
    /// <paramref name="partId"/>, und beide Teile schließen danach nahtlos an.
    ///
    /// <para>Nachgeprüft wird hier NOCH EINMAL (spielbar ab der Stellung davor, endet auf der
    /// Zielstellung) — die Züge kommen aus einer Antwort, aber ankommen tut ein Request. Passt es
    /// nicht, entsteht gar kein Teil: eine halb eingefügte Kette wäre schlimmer als keine.</para>
    /// </summary>
    public async Task<ReconstructionDetailDto?> ApplyGapAsync(int userId, int id, int partId, string? moves, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        if (row == null) return null;
        var ordered = Recorded(row);
        var index = ordered.FindIndex(p => p.Id == partId);
        if (index < 0) return null;
        if (ordered.Count >= MaxParts) throw new InvalidOperationException("too-many-parts");

        var part = ordered[index];
        if (index == 0) throw new ArgumentException("no-previous");
        if (part.ContinuesPrevious) throw new ArgumentException("no-gap");
        if (part.Kind != ReconstructionPartKind.Position) throw new ArgumentException("target-not-a-position");

        var sans = ReconstructionChain.SplitMoves(moves);
        if (sans.Count == 0) throw new ArgumentException("no-moves");

        var chain = ReconstructionChain.Analyze(row.Parts);
        var fromFen = chain.Parts.FirstOrDefault(c => c.PartId == ordered[index - 1].Id)?.EndFen;
        if (fromFen == null) throw new ArgumentException("no-anchor");
        if (!ReconstructionChain.IsLoadableFen(part.Fen)) throw new ArgumentException("invalid-fen");

        var board = Chess.ChessBoard.LoadFromFen(fromFen);
        foreach (var san in sans)
        {
            var ok = false;
            try { ok = board.Move(san); } catch { ok = false; }
            if (!ok) throw new ArgumentException("does-not-fit");
        }
        if (!GapSolver.Matches(board.ToFen(), part.Fen!)) throw new ArgumentException("does-not-fit");

        RemoveProposalsBefore(row, part);
        var bridge = new GameReconstructionPart
        {
            GameReconstructionId = row.Id,
            Ordinal = part.Ordinal,
            Kind = ReconstructionPartKind.Moves,
            Moves = Cut(string.Join(' ', sans), 4000),
            ContinuesPrevious = true,
            // Die Züge sind GEFUNDEN, nicht erinnert: meist führen mehrere Wege in dieselbe Stellung.
            // Das eingesetzte Teil gilt deshalb als unsicher, bis der Mensch es bestätigt.
            Certain = false,
        };
        foreach (var later in row.Parts.Where(p => p.Ordinal >= part.Ordinal)) later.Ordinal++;
        part.ContinuesPrevious = true;   // die Lücke ist zu — ab jetzt hängt das Teil an den Zügen
        part.UpdatedAt = DateTime.UtcNow;
        row.Parts.Add(bridge);
        _db.GameReconstructionParts.Add(bridge);
        Renumber(row);
        await SaveTouchedAsync(row, ct);
        return ToDetail(row);
    }

    /// <summary>
    /// Sucht die Wege durch die Lücke und SETZT sie als Vorschläge in die Liste (vor
    /// <paramref name="partId"/>). Vorschläge zählen nicht zur Partie — die Lücke bleibt offen,
    /// bis ein Mensch etwas übernimmt; sie sind zum Durchsehen da.
    /// </summary>
    public async Task<ReconstructionGapProposalDto?> ProposeGapAsync(int userId, int id, int partId, int? maxPlies, CancellationToken ct = default)
    {
        var search = await SolveGapAsync(userId, id, partId, maxPlies, ct);
        if (search == null) return null;

        var row = (await LoadAsync(userId, id, ct))!;
        var target = row.Parts.First(p => p.Id == partId);
        RemoveProposalsBefore(row, target);

        var inserted = 0;
        foreach (var solution in search.Solutions)
        {
            if (row.Parts.Count >= MaxParts) break;
            var proposal = new GameReconstructionPart
            {
                GameReconstructionId = row.Id,
                Ordinal = target.Ordinal,
                Kind = ReconstructionPartKind.Moves,
                Moves = Cut(solution.San, 4000),
                ContinuesPrevious = true,   // er hängt an der Stellung davor — das ist der Sinn der Suche
                Certain = false,
                Generated = true,
            };
            foreach (var later in row.Parts.Where(p => p.Ordinal >= target.Ordinal)) later.Ordinal++;
            row.Parts.Add(proposal);
            _db.GameReconstructionParts.Add(proposal);
            inserted++;
        }
        Renumber(row);
        if (inserted > 0) await SaveTouchedAsync(row, ct);

        return new ReconstructionGapProposalDto
        {
            PartId = partId, MaxPlies = search.MaxPlies, Nodes = search.Nodes,
            BudgetExhausted = search.BudgetExhausted, DeepestSearched = search.DeepestSearched,
            Reason = search.Reason,
            Inserted = inserted, Detail = ToDetail(row),
        };
    }

    /// <summary>Verwirft die Vorschläge VOR diesem Teil (sie sind nur ein Angebot).</summary>
    public async Task<ReconstructionDetailDto?> DiscardProposalsAsync(int userId, int id, int partId, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        var target = row?.Parts.FirstOrDefault(p => p.Id == partId);
        if (row == null || target == null) return null;
        if (RemoveProposalsBefore(row, target) > 0)
        {
            Renumber(row);
            await SaveTouchedAsync(row, ct);
        }
        return ToDetail(row);
    }

    /// <summary>
    /// Eine Stellung AUS einem Vorschlag als eigenes Teil übernehmen („die stimmt") bzw. die
    /// korrigierte Fassung davon. Sie kommt vor <paramref name="partId"/> in die Liste und teilt
    /// die Lücke damit in zwei kleinere — die übrigen Vorschläge dieser Lücke fallen weg, denn sie
    /// beantworten eine Frage, die so nicht mehr gestellt ist.
    /// </summary>
    public async Task<ReconstructionDetailDto?> AddWaypointAsync(int userId, int id, int partId, string? fen, bool certain, CancellationToken ct = default)
    {
        var row = await LoadAsync(userId, id, ct);
        var target = row?.Parts.FirstOrDefault(p => p.Id == partId);
        if (row == null || target == null) return null;
        var clean = (fen ?? string.Empty).Trim();
        if (!ReconstructionChain.IsLoadableFen(clean)) throw new ArgumentException("invalid-fen");
        if (row.Parts.Count(p => !p.Generated) >= MaxParts) throw new InvalidOperationException("too-many-parts");

        RemoveProposalsBefore(row, target);
        var waypoint = new GameReconstructionPart
        {
            GameReconstructionId = row.Id,
            Ordinal = target.Ordinal,
            Kind = ReconstructionPartKind.Position,
            Fen = clean,
            Certain = certain,
        };
        foreach (var later in row.Parts.Where(p => p.Ordinal >= target.Ordinal)) later.Ordinal++;
        row.Parts.Add(waypoint);
        _db.GameReconstructionParts.Add(waypoint);
        Renumber(row);
        await SaveTouchedAsync(row, ct);
        return ToDetail(row);
    }

    /// <summary>Die AUFGEZEICHNETEN Teile in ihrer Reihenfolge (ohne die Vorschläge der Suche).</summary>
    private static List<GameReconstructionPart> Recorded(GameReconstruction row)
        => row.Parts.Where(p => !p.Generated).OrderBy(p => p.Ordinal).ToList();

    /// <summary>Entfernt die unmittelbar vor <paramref name="target"/> stehenden Vorschläge.</summary>
    private int RemoveProposalsBefore(GameReconstruction row, GameReconstructionPart target)
    {
        var ordered = row.Parts.OrderBy(p => p.Ordinal).ToList();
        var index = ordered.IndexOf(target);
        var removed = 0;
        for (var i = index - 1; i >= 0 && ordered[i].Generated; i--)
        {
            row.Parts.Remove(ordered[i]);
            _db.GameReconstructionParts.Remove(ordered[i]);
            removed++;
        }
        return removed;
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
        part.Certain = req.Certain ?? true;
        // Bei einer Stellung steht die Seite am Zug in der FEN — zwei Quellen für dieselbe Aussage
        // wären eine, die irgendwann widerspricht.
        part.BlackToMove = req.Kind == ReconstructionPartKind.Moves && (req.BlackToMove ?? false);
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

    // ===== Teilen =====

    /// <summary>
    /// Den öffentlichen Link einschalten (idempotent: ein vorhandenes Token bleibt, damit ein
    /// schon verschickter Link gültig bleibt). <c>null</c>, wenn es die Rekonstruktion nicht gibt.
    /// </summary>
    public async Task<string?> ShareAsync(int userId, int id, CancellationToken ct = default)
    {
        var row = await _db.GameReconstructions.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (row == null) return null;

        if (string.IsNullOrEmpty(row.ShareToken))
        {
            row.ShareToken = await NewUniqueTokenAsync(ct);
            row.SharedAt = DateTime.UtcNow;
            row.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
        }
        return row.ShareToken;
    }

    /// <summary>Den Link abschalten. Ein späteres Teilen erzeugt ein NEUES Token — der alte Link
    /// läuft danach ins Leere, und genau dafür ist das Abschalten da.</summary>
    public async Task<bool> UnshareAsync(int userId, int id, CancellationToken ct = default)
    {
        var row = await _db.GameReconstructions.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId, ct);
        if (row == null) return false;

        row.ShareToken = null;
        row.SharedAt = null;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>
    /// Die geteilte Partie hinter dem Link (ohne Anmeldung); <c>null</c> bei unbekanntem Token.
    ///
    /// <para>Gezeigt wird der aktuelle Stand mit allen aufgezeichneten Teilen — die Vorschläge der
    /// Lückensuche bleiben draußen: sie sind Arbeitsstand des Besitzers, nicht die Partie.</para>
    /// </summary>
    public async Task<SharedReconstructionDto?> GetSharedAsync(string? token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var row = await _db.GameReconstructions
            .Include(r => r.Parts)
            .FirstOrDefaultAsync(r => r.ShareToken == token, ct);
        if (row == null) return null;

        var chain = ReconstructionChain.Analyze(row.Parts);
        var byId = chain.Parts.ToDictionary(c => c.PartId);
        var dto = new SharedReconstructionDto
        {
            Title = row.Title, White = row.White, Black = row.Black, Event = row.Event,
            PlayedOn = row.PlayedOn, Result = row.Result, Note = row.Note,
            KnownPlies = chain.KnownPlies, Gaps = chain.Gaps, PrefixSan = chain.PrefixSan,
            UpdatedAt = row.UpdatedAt,
        };
        foreach (var part in row.Parts.Where(p => !p.Generated).OrderBy(p => p.Ordinal))
        {
            byId.TryGetValue(part.Id, out var c);
            dto.Parts.Add(new SharedReconstructionPartDto
            {
                Kind = part.Kind, Moves = part.Moves, Fen = part.Fen,
                ContinuesPrevious = part.ContinuesPrevious, Certain = part.Certain,
                BlackToMove = part.BlackToMove, Note = part.Note,
                StartFen = c?.StartFen, EndFen = c?.EndFen,
                PlyCount = c?.PlyCount ?? 0, StartPly = c?.StartPly,
            });
        }
        return dto;
    }

    private async Task<string> NewUniqueTokenAsync(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var token = NewToken();
            if (!await _db.GameReconstructions.AnyAsync(r => r.ShareToken == token, ct)) return token;
        }
        return NewToken();   // extrem unwahrscheinlicher Kollisions-Fallback
    }

    /// <summary>URL-sicheres Zufallstoken (~22 Zeichen aus 16 Bytes) — wie beim Partie-/Blatt-Link.</summary>
    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
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
            UpdatedAt = row.UpdatedAt, PrefixSan = chain.PrefixSan, ShareToken = row.ShareToken,
        };
        foreach (var part in row.Parts.OrderBy(p => p.Ordinal))
        {
            byId.TryGetValue(part.Id, out var c);
            dto.Parts.Add(new ReconstructionPartDto
            {
                Id = part.Id, Ordinal = part.Ordinal, Kind = part.Kind, Moves = part.Moves, Fen = part.Fen,
                FromPly = part.FromPly, ContinuesPrevious = part.ContinuesPrevious,
                Certain = part.Certain, BlackToMove = part.BlackToMove,
                Generated = part.Generated, Note = part.Note,
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
