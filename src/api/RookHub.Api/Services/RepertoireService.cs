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
    public const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    // S-13: PGN-Heuristik. Linear (kein katastrophisches Backtracking); 2s-Timeout als Defense-in-depth.
    // Tag-Pair wie [Event "..."] bzw. echter erster Zug 1. e4 / 1. Nf3 / 1. O-O — nicht blosse Teilstrings.
    private static readonly Regex PgnTagPair = new(@"\[[A-Za-z][A-Za-z0-9_]*\s+""", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    private static readonly Regex PgnFirstMove = new(@"\b1\.\s*(O-O|[NBRQKa-h])", RegexOptions.Compiled, TimeSpan.FromSeconds(2));
    public const int MaxRepertoiresPerUser = 500;
    public const int MaxFilesPerRepertoire = 1000;

    public RepertoireService(AppDbContext db, RepertoireAnalyzeService analyzeCache)
    {
        _db = db;
        _analyzeCache = analyzeCache;
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
        return await _db.Repertoires
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
    }

    public async Task<RepertoireDetailDto> GetByIdAsync(int id, int userId)
    {
        var rep = await _db.Repertoires
            .Include(r => r.Files)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId)
            ?? throw new KeyNotFoundException("Repertoire not found.");

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
            ChessableCourseId = rep.ChessableCourseId
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

    public async Task<RepertoireDto> UpdateAsync(int id, int userId, UpdateRepertoireDto dto)
    {
        var rep = await _db.Repertoires
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId)
            ?? throw new KeyNotFoundException("Repertoire not found.");

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

        _db.Repertoires.Remove(rep);
        await _db.SaveChangesAsync();
        _analyzeCache.Invalidate(userId);
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
        _analyzeCache.Invalidate(userId);

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
        var file = await _db.RepertoireFiles
            .Include(f => f.Repertoire)
            .FirstOrDefaultAsync(f => f.Id == fileId && f.RepertoireId == repertoireId && f.Repertoire.UserId == userId)
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
        _analyzeCache.Invalidate(userId);
    }

    public async Task<string> GetCombinedPgnAsync(int repertoireId, int userId)
    {
        var rep = await _db.Repertoires
            .Include(r => r.Files)
            .FirstOrDefaultAsync(r => r.Id == repertoireId && r.UserId == userId)
            ?? throw new KeyNotFoundException("Repertoire not found.");

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
