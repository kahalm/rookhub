using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class RepertoireService
{
    private readonly AppDbContext _db;
    private readonly RepertoireAnalyzeService _analyzeCache;
    private readonly RepertoirePositionLookupService? _positionLookup;
    private readonly FriendService _friends;
    private readonly NotificationService _notifications;
    public const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    // S-13: PGN-Heuristik. Linear (kein katastrophisches Backtracking); 2s-Timeout als Defense-in-depth.
    // Tag-Pair wie [Event "..."] bzw. echter erster Zug 1. e4 / 1. Nf3 / 1. O-O — nicht blosse Teilstrings.
    private static readonly Regex PgnTagPair = new(@"\[[A-Za-z][A-Za-z0-9_]*\s+""", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex PgnFirstMove = new(@"\b1\.\s*(O-O|[NBRQKa-h])", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    public const int MaxRepertoiresPerUser = 500;
    public const int MaxFilesPerRepertoire = 1000;

    // FriendService/NotificationService sind optional, damit bestehende Test-Konstruktionen ohne
    // Änderung kompilieren; im DI-Container werden immer die echten Instanzen injiziert.
    public RepertoireService(AppDbContext db, RepertoireAnalyzeService analyzeCache, FriendService? friends = null, NotificationService? notifications = null, RepertoirePositionLookupService? positionLookup = null)
    {
        _db = db;
        _analyzeCache = analyzeCache;
        _positionLookup = positionLookup;
        _notifications = notifications ?? new NotificationService(db);
        _friends = friends ?? new FriendService(db, _notifications);
    }

    /// <summary>Beide Repertoire-Positions-Caches eines Users invalidieren (Extension-Analyse + Stellungssuche).</summary>
    private void InvalidateCaches(int userId)
    {
        _analyzeCache.Invalidate(userId);
        _positionLookup?.Invalidate(userId);
    }

    /// <summary>Stellungssuche-Cache aller Empfänger eines geteilten Repertoires invalidieren — deren
    /// Index enthält dieses Repertoire ebenfalls (Extension-Analyse nutzt nur eigene, daher hier nicht).
    /// <paramref name="recipientIds"/> wird bei Lösch-Operationen VOR dem Entfernen der Shares übergeben.</summary>
    private async Task InvalidateRecipientPositionCachesAsync(int repertoireId, IEnumerable<int>? recipientIds = null)
    {
        if (_positionLookup == null) return;
        var ids = recipientIds?.ToList() ?? await _db.RepertoireShares
            .Where(s => s.RepertoireId == repertoireId)
            .Select(s => s.RecipientId)
            .ToListAsync();
        foreach (var rid in ids) _positionLookup.Invalidate(rid);
    }

    /// <summary>Gehört das Repertoire dem User? (Schreib-/Verwaltungs-Rechte — nur der Besitzer.)</summary>
    public Task<bool> IsOwnerAsync(int repertoireId, int userId)
        => _db.Repertoires.AnyAsync(r => r.Id == repertoireId && r.UserId == userId);

    /// <summary>Darf der User das Repertoire LESEN/trainieren? Besitzer ODER Empfänger einer Freigabe.</summary>
    public async Task<bool> CanAccessAsync(int repertoireId, int userId)
    {
        if (await _db.Repertoires.AnyAsync(r => r.Id == repertoireId && r.UserId == userId)) return true;
        return await _db.RepertoireShares.AnyAsync(s => s.RepertoireId == repertoireId && s.RecipientId == userId);
    }

    /// <summary>
    /// S-13-Heuristik: Sieht der Inhalt nach echtem PGN aus (Tag-Pair ODER echter erster Zug)?
    /// Wiederverwendbar von anderen PGN-Uploads (z. B. Wochenpost), damit die Validierung
    /// nicht dupliziert wird.
    /// </summary>
    public static bool LooksLikePgn(string content) =>
        PgnTagPair.IsMatch(content) || PgnFirstMove.IsMatch(content);

    public async Task<List<RepertoireDto>> GetAllAsync(int userId)
    {
        var owned = await _db.Repertoires
            .Where(r => r.UserId == userId)
            .Select(r => new RepertoireDto
            {
                Id = r.Id,
                Name = r.Name,
                Description = r.Description,
                IsPublic = r.IsPublic,
                Kind = r.Kind,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                FileCount = r.Files.Count,
                UseForExtension = r.UseForExtension,
                ChessableCourseId = r.ChessableCourseId
            })
            .ToListAsync();

        // Von anderen mit mir geteilte Repertoires (Sektion „Mit mir geteilt" + „von X"-Badge).
        var shared = await _db.RepertoireShares
            .Where(s => s.RecipientId == userId)
            .Select(s => new RepertoireDto
            {
                Id = s.Repertoire.Id,
                Name = s.Repertoire.Name,
                Description = s.Repertoire.Description,
                IsPublic = s.Repertoire.IsPublic,
                Kind = s.Repertoire.Kind,
                CreatedAt = s.Repertoire.CreatedAt,
                UpdatedAt = s.Repertoire.UpdatedAt,
                FileCount = s.Repertoire.Files.Count,
                UseForExtension = false, // Geteilte Repertoires werden NICHT von MEINER Extension analysiert.
                ChessableCourseId = s.Repertoire.ChessableCourseId,
                IsShared = true,
                SharedByUsername = s.Owner.Username
            })
            .ToListAsync();

        return owned.Concat(shared).ToList();
    }

    public async Task<RepertoireDetailDto> GetByIdAsync(int id, int userId)
    {
        // Lesend: Besitzer ODER Empfänger einer Freigabe (Bearbeiten bleibt in UpdateAsync owner-only).
        var rep = await _db.Repertoires
            .Include(r => r.Files)
            .FirstOrDefaultAsync(r => r.Id == id)
            ?? throw new KeyNotFoundException("Repertoire not found.");
        if (rep.UserId != userId && !await CanAccessAsync(id, userId))
            throw new KeyNotFoundException("Repertoire not found.");

        return new RepertoireDetailDto
        {
            Id = rep.Id,
            Name = rep.Name,
            Description = rep.Description,
            IsPublic = rep.IsPublic,
            Kind = rep.Kind,
            CreatedAt = rep.CreatedAt,
            UpdatedAt = rep.UpdatedAt,
            Files = rep.Files.Select(f => new RepertoireFileDto
            {
                Id = f.Id,
                FileName = f.FileName,
                FileSize = f.FileSize,
                UploadedAt = f.UploadedAt
            }).ToList(),
            UseForExtension = rep.UseForExtension,
            ChessableCourseId = rep.ChessableCourseId,
            IsOwner = rep.UserId == userId
        };
    }

    public async Task<RepertoireDto> CreateAsync(int userId, CreateRepertoireDto dto)
    {
        var count = await _db.Repertoires.CountAsync(r => r.UserId == userId);
        if (count >= MaxRepertoiresPerUser)
            throw new InvalidOperationException($"Maximum of {MaxRepertoiresPerUser} repertoires per user reached.");

        var rep = new Repertoire
        {
            UserId = userId,
            Name = dto.Name,
            Description = dto.Description,
            IsPublic = dto.IsPublic,
            Kind = dto.Kind,
            UseForExtension = dto.UseForExtension,
            ChessableCourseId = string.IsNullOrWhiteSpace(dto.ChessableCourseId) ? null : dto.ChessableCourseId
        };

        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();

        return new RepertoireDto
        {
            Id = rep.Id,
            Name = rep.Name,
            Description = rep.Description,
            IsPublic = rep.IsPublic,
            Kind = rep.Kind,
            CreatedAt = rep.CreatedAt,
            UpdatedAt = rep.UpdatedAt,
            FileCount = 0,
            UseForExtension = rep.UseForExtension,
            ChessableCourseId = rep.ChessableCourseId
        };
    }

    /// <summary>Legt ein Repertoire aus fertigem PGN an (eine Datei) — für „Kurs → Repertoire umwandeln".
    /// <see cref="Repertoire.UseForExtension"/> standardmäßig aus (wie bei importierten Chessable-Kursen);
    /// im Bearbeiten-Dialog aktivierbar. Wirft <see cref="InvalidOperationException"/> bei Nicht-PGN.</summary>
    public async Task<RepertoireDto> CreateFromPgnAsync(int userId, string name, string fileName, string pgn,
        RepertoireKind kind = RepertoireKind.None)
    {
        if (string.IsNullOrWhiteSpace(pgn) || !LooksLikePgn(pgn))
            throw new InvalidOperationException("The content does not look like a valid PGN.");
        var trimmed = string.IsNullOrWhiteSpace(name) ? "Repertoire" : name.Trim();
        if (trimmed.Length > 200) trimmed = trimmed[..200];

        var dto = await CreateAsync(userId, new CreateRepertoireDto { Name = trimmed, Kind = kind, UseForExtension = false });
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(pgn));
        await UploadFileAsync(dto.Id, userId, fileName, stream);
        dto.FileCount = 1;
        return dto;
    }

    public async Task<RepertoireDto> UpdateAsync(int id, int userId, UpdateRepertoireDto dto)
    {
        var rep = await _db.Repertoires
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId)
            ?? throw new KeyNotFoundException("Repertoire not found.");

        var nameChanged = dto.Name != null && dto.Name != rep.Name;
        if (dto.Name != null) rep.Name = dto.Name;
        if (dto.Description != null) rep.Description = dto.Description;
        if (dto.IsPublic.HasValue) rep.IsPublic = dto.IsPublic.Value;
        var kindChanged = dto.Kind.HasValue && dto.Kind.Value != rep.Kind;
        if (dto.Kind.HasValue) rep.Kind = dto.Kind.Value;
        // Aenderung am Extension-Flag aendert das analysierte Positions-Set → Cache verwerfen.
        var extChanged = dto.UseForExtension.HasValue && dto.UseForExtension.Value != rep.UseForExtension;
        if (dto.UseForExtension.HasValue) rep.UseForExtension = dto.UseForExtension.Value;
        if (dto.UpdateChessableCourseId)
            rep.ChessableCourseId = string.IsNullOrWhiteSpace(dto.ChessableCourseId) ? null : dto.ChessableCourseId;
        rep.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        if (kindChanged || extChanged) _analyzeCache.Invalidate(userId);
        // Stellungssuche zeigt Name/Kind je Treffer → auch bei Namens-/Kind-Änderung verwerfen
        // (eigener Cache + der aller Freigabe-Empfänger).
        if (kindChanged || nameChanged)
        {
            _positionLookup?.Invalidate(userId);
            await InvalidateRecipientPositionCachesAsync(id);
        }

        var fileCount = await _db.RepertoireFiles.CountAsync(f => f.RepertoireId == id);

        return new RepertoireDto
        {
            Id = rep.Id,
            Name = rep.Name,
            Description = rep.Description,
            IsPublic = rep.IsPublic,
            Kind = rep.Kind,
            CreatedAt = rep.CreatedAt,
            UpdatedAt = rep.UpdatedAt,
            FileCount = fileCount,
            UseForExtension = rep.UseForExtension,
            ChessableCourseId = rep.ChessableCourseId
        };
    }

    public async Task DeleteAsync(int id, int userId)
    {
        var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId)
            ?? throw new KeyNotFoundException("Repertoire not found.");

        // Empfänger VOR dem Entfernen der Shares merken (danach nicht mehr abfragbar).
        var recipientIds = await _db.RepertoireShares
            .Where(s => s.RepertoireId == id).Select(s => s.RecipientId).ToListAsync();
        // Freigaben explizit entfernen (FK-Cascade greift bei InMemory-Tests nicht).
        _db.RepertoireShares.RemoveRange(_db.RepertoireShares.Where(s => s.RepertoireId == id));
        _db.Repertoires.Remove(rep);
        await _db.SaveChangesAsync();
        InvalidateCaches(userId);
        await InvalidateRecipientPositionCachesAsync(id, recipientIds);
    }

    // --- Repertoire mit ausgewählten Personen teilen (analog CourseService) -------------------

    private async Task<Repertoire> EnsureOwnedAsync(int userId, int repertoireId)
    {
        var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == repertoireId)
            ?? throw new KeyNotFoundException("Repertoire not found.");
        if (rep.UserId != userId)
            throw new UnauthorizedAccessException("Only the owner can share this repertoire.");
        return rep;
    }

    /// <summary>Teilt ein eigenes Repertoire mit mehreren (befreundeten) Nutzern. Idempotent:
    /// bereits geteilte → <c>duplicate</c>, Fremde → <c>not_friends</c>, unbekannte → <c>not_found</c>,
    /// man selbst → <c>self</c>. Legt je neuem Empfänger eine In-App-Benachrichtigung an.
    /// Admins dürfen an Nicht-Freunde teilen.</summary>
    public async Task<RepertoireShareResultDto> ShareAsync(int userId, int repertoireId, List<int> recipientUserIds, bool isAdmin)
    {
        var rep = await EnsureOwnedAsync(userId, repertoireId);

        var result = new RepertoireShareResultDto();
        var distinct = recipientUserIds.Distinct().ToList();
        if (distinct.Count == 0) return result;

        var existing = (await _db.AppUsers.Where(u => distinct.Contains(u.Id)).Select(u => u.Id).ToListAsync()).ToHashSet();
        var friendIds = await _friends.GetAcceptedFriendIdsAsync(userId, distinct);
        var alreadyShared = (await _db.RepertoireShares
            .Where(s => s.RepertoireId == repertoireId && distinct.Contains(s.RecipientId))
            .Select(s => s.RecipientId)
            .ToListAsync()).ToHashSet();

        var toNotify = new List<int>();
        foreach (var rid in distinct)
        {
            if (rid == userId) { result.Skipped.Add(new RepertoireShareSkipDto { UserId = rid, Reason = "self" }); continue; }
            if (!existing.Contains(rid)) { result.Skipped.Add(new RepertoireShareSkipDto { UserId = rid, Reason = "not_found" }); continue; }
            if (!isAdmin && !friendIds.Contains(rid)) { result.Skipped.Add(new RepertoireShareSkipDto { UserId = rid, Reason = "not_friends" }); continue; }
            if (alreadyShared.Contains(rid)) { result.Skipped.Add(new RepertoireShareSkipDto { UserId = rid, Reason = "duplicate" }); continue; }

            _db.RepertoireShares.Add(new RepertoireShare { RepertoireId = repertoireId, OwnerId = userId, RecipientId = rid, SharedAt = DateTime.UtcNow });
            toNotify.Add(rid);
            result.Shared++;
        }

        if (result.Shared > 0)
        {
            try { await _db.SaveChangesAsync(); }
            catch (DbUpdateException)
            {
                // Race: derselbe (Repertoire, Empfänger) parallel → Unique-Index. Idempotent behandeln.
                _db.ChangeTracker.Clear();
                return await ShareAsync(userId, repertoireId, recipientUserIds, isAdmin);
            }

            var ownerName = await _db.AppUsers.Where(u => u.Id == userId).Select(u => u.Username).FirstOrDefaultAsync() ?? "?";
            await _notifications.CreateManyAsync(toNotify, NotificationType.RepertoireShared,
                new Dictionary<string, string> { ["username"] = ownerName, ["repertoireName"] = rep.Name }, "/repertoires");

            // Neue Empfänger: Stellungssuche-Cache verwerfen, damit das Repertoire dort sofort auftaucht.
            foreach (var rid in toNotify) _positionLookup?.Invalidate(rid);
        }

        return result;
    }

    /// <summary>Mit welchen Nutzern ist dieses eigene Repertoire aktuell geteilt? (Für den Dialog.)</summary>
    public async Task<List<RepertoireShareRecipientDto>> GetShareRecipientsAsync(int userId, int repertoireId)
    {
        await EnsureOwnedAsync(userId, repertoireId);
        return await _db.RepertoireShares
            .Where(s => s.RepertoireId == repertoireId && s.OwnerId == userId)
            .OrderBy(s => s.SharedAt)
            .Select(s => new RepertoireShareRecipientDto
            {
                UserId = s.RecipientId,
                Username = s.Recipient.Username,
                DisplayName = s.Recipient.Profile != null ? s.Recipient.Profile.DisplayName : null,
                SharedAt = s.SharedAt,
            })
            .ToListAsync();
    }

    /// <summary>Nimmt die Freigabe für einen Empfänger zurück (idempotent). Nur der Besitzer.</summary>
    public async Task UnshareAsync(int userId, int repertoireId, int recipientId)
    {
        await EnsureOwnedAsync(userId, repertoireId);
        var share = await _db.RepertoireShares.FirstOrDefaultAsync(s =>
            s.RepertoireId == repertoireId && s.OwnerId == userId && s.RecipientId == recipientId);
        if (share == null) return;
        _db.RepertoireShares.Remove(share);
        await _db.SaveChangesAsync();
        // Empfänger verliert Zugriff → Stellungssuche-Cache verwerfen, damit das Repertoire verschwindet.
        _positionLookup?.Invalidate(recipientId);
    }

    public async Task<RepertoireFileDto> UploadFileAsync(int repertoireId, int userId, string fileName, Stream fileStream)
    {
        var rep = await _db.Repertoires
            .Include(r => r.Files)
            .FirstOrDefaultAsync(r => r.Id == repertoireId && r.UserId == userId)
            ?? throw new KeyNotFoundException("Repertoire not found.");

        if (rep.Files.Count >= MaxFilesPerRepertoire)
            throw new InvalidOperationException($"Maximum of {MaxFilesPerRepertoire} files per repertoire reached.");

        if (fileStream.CanSeek && fileStream.Length > MaxFileSize)
            throw new InvalidOperationException($"File size exceeds maximum of {MaxFileSize / 1024 / 1024} MB.");

        // S-14: Sanitize filename to prevent path traversal
        var safeFileName = Path.GetFileName(fileName);
        safeFileName = Regex.Replace(safeFileName, @"[^a-zA-Z0-9_.-]", "_");
        if (string.IsNullOrWhiteSpace(safeFileName)) safeFileName = "upload.pgn";

        using var reader = new StreamReader(fileStream);
        var content = await reader.ReadToEndAsync();

        // S-24: Content-length check after ReadToEnd (for non-seekable streams)
        if (content.Length > MaxFileSize)
            throw new InvalidOperationException($"File content exceeds maximum of {MaxFileSize / 1024 / 1024} MB.");

        // S-13: PGN content validation — verlangt ein echtes Tag-Pair ODER einen echten ersten Zug,
        // nicht nur die Teilstrings "[Event" / "1." irgendwo (die auch beliebigen Text durchlassen).
        if (!LooksLikePgn(content))
            throw new InvalidOperationException("File does not appear to be valid PGN content.");

        // Chessable-Kurs-ID aus [Site]-Tag (piratechess-Export) automatisch übernehmen,
        // wenn noch keine ID am Repertoire gesetzt ist.
        if (rep.ChessableCourseId == null)
        {
            var siteMatch = Regex.Match(content, @"\[Site\s+""https://www\.chessable\.com/course/(\d+)/?""", RegexOptions.None, TimeSpan.FromSeconds(2));
            if (siteMatch.Success)
                rep.ChessableCourseId = siteMatch.Groups[1].Value;
        }

        var file = new RepertoireFile
        {
            RepertoireId = repertoireId,
            FileName = safeFileName,
            PgnContent = content,
            FileSize = fileStream.CanSeek ? fileStream.Length : content.Length
        };

        _db.RepertoireFiles.Add(file);
        rep.UpdatedAt = DateTime.UtcNow;
        rep.ImportVersion = ImportPipeline.CurrentVersion; // frisch aufbereiteter Inhalt = aktuelle Pipeline
        await _db.SaveChangesAsync();
        InvalidateCaches(userId);
        await InvalidateRecipientPositionCachesAsync(repertoireId);

        return new RepertoireFileDto
        {
            Id = file.Id,
            FileName = file.FileName,
            FileSize = file.FileSize,
            UploadedAt = file.UploadedAt
        };
    }

    public async Task<(string FileName, string Content)> DownloadFileAsync(int repertoireId, int fileId, int userId)
    {
        // Lesend: Besitzer ODER Empfänger einer Freigabe.
        var file = await _db.RepertoireFiles
            .Include(f => f.Repertoire)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.RepertoireId == repertoireId &&
                (f.Repertoire.UserId == userId ||
                 _db.RepertoireShares.Any(s => s.RepertoireId == repertoireId && s.RecipientId == userId)))
            ?? throw new KeyNotFoundException("File not found.");

        return (file.FileName, file.PgnContent);
    }

    public async Task DeleteFileAsync(int repertoireId, int fileId, int userId)
    {
        var file = await _db.RepertoireFiles
            .Include(f => f.Repertoire)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.RepertoireId == repertoireId && f.Repertoire.UserId == userId)
            ?? throw new KeyNotFoundException("File not found.");

        _db.RepertoireFiles.Remove(file);
        file.Repertoire.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        InvalidateCaches(userId);
        await InvalidateRecipientPositionCachesAsync(repertoireId);
    }

    public async Task<string> GetCombinedPgnAsync(int repertoireId, int userId)
    {
        // Lesend: Besitzer ODER Empfänger einer Freigabe (Recipient braucht das PGN zum Trainieren).
        var rep = await _db.Repertoires
            .Include(r => r.Files)
            .FirstOrDefaultAsync(r => r.Id == repertoireId)
            ?? throw new KeyNotFoundException("Repertoire not found.");
        if (rep.UserId != userId && !await CanAccessAsync(repertoireId, userId))
            throw new KeyNotFoundException("Repertoire not found.");

        return string.Join("\n\n", rep.Files.Select(f => f.PgnContent));
    }

    /// <summary>
    /// Liste der Repertoires fuer einen Extension-Client. <paramref name="kind"/> filtert auf
    /// eine Kategorie (z. B. <see cref="RepertoireKind.Opening"/>); <c>null</c> = alle Kinds.
    /// </summary>
    public async Task<List<ExtensionRepertoireDto>> GetExtensionListAsync(int userId, RepertoireKind? kind = null)
    {
        var q = _db.Repertoires.Where(r => r.UserId == userId && r.UseForExtension);
        if (kind.HasValue)
            q = q.Where(r => r.Kind == kind.Value);
        return await q
            .Select(r => new ExtensionRepertoireDto
            {
                Id = r.Id,
                Name = r.Name,
                FileCount = r.Files.Count,
                Kind = r.Kind,
                TotalSizeBytes = r.Files.Sum(f => f.FileSize),
                ChessableCourseId = r.ChessableCourseId
            })
            .ToListAsync();
    }
}
