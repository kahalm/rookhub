using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

public class RepertoireService
{
    private readonly AppDbContext _db;
    private const long MaxFileSize = 10 * 1024 * 1024; // 10 MB

    public RepertoireService(AppDbContext db) => _db = db;

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
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
                FileCount = r.Files.Count
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
            CreatedAt = rep.CreatedAt,
            UpdatedAt = rep.UpdatedAt,
            Files = rep.Files.Select(f => new RepertoireFileDto
            {
                Id = f.Id,
                FileName = f.FileName,
                FileSize = f.FileSize,
                UploadedAt = f.UploadedAt
            }).ToList()
        };
    }

    public async Task<RepertoireDto> CreateAsync(int userId, CreateRepertoireDto dto)
    {
        var rep = new Repertoire
        {
            UserId = userId,
            Name = dto.Name,
            Description = dto.Description,
            IsPublic = dto.IsPublic
        };

        _db.Repertoires.Add(rep);
        await _db.SaveChangesAsync();

        return new RepertoireDto
        {
            Id = rep.Id,
            Name = rep.Name,
            Description = rep.Description,
            IsPublic = rep.IsPublic,
            CreatedAt = rep.CreatedAt,
            UpdatedAt = rep.UpdatedAt,
            FileCount = 0
        };
    }

    public async Task<RepertoireDto> UpdateAsync(int id, int userId, UpdateRepertoireDto dto)
    {
        var rep = await _db.Repertoires
            .Include(r => r.Files)
            .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId)
            ?? throw new KeyNotFoundException("Repertoire not found.");

        if (dto.Name != null) rep.Name = dto.Name;
        if (dto.Description != null) rep.Description = dto.Description;
        if (dto.IsPublic.HasValue) rep.IsPublic = dto.IsPublic.Value;
        rep.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return new RepertoireDto
        {
            Id = rep.Id,
            Name = rep.Name,
            Description = rep.Description,
            IsPublic = rep.IsPublic,
            CreatedAt = rep.CreatedAt,
            UpdatedAt = rep.UpdatedAt,
            FileCount = rep.Files.Count
        };
    }

    public async Task DeleteAsync(int id, int userId)
    {
        var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId)
            ?? throw new KeyNotFoundException("Repertoire not found.");

        _db.Repertoires.Remove(rep);
        await _db.SaveChangesAsync();
    }

    public async Task<RepertoireFileDto> UploadFileAsync(int repertoireId, int userId, string fileName, Stream fileStream)
    {
        var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == repertoireId && r.UserId == userId)
            ?? throw new KeyNotFoundException("Repertoire not found.");

        using var reader = new StreamReader(fileStream);
        var content = await reader.ReadToEndAsync();

        if (content.Length > MaxFileSize)
            throw new InvalidOperationException($"File size exceeds maximum of {MaxFileSize / 1024 / 1024} MB.");

        var file = new RepertoireFile
        {
            RepertoireId = repertoireId,
            FileName = fileName,
            PgnContent = content,
            FileSize = content.Length
        };

        _db.RepertoireFiles.Add(file);
        rep.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

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
    }

    public async Task<string> GetCombinedPgnAsync(int repertoireId, int userId)
    {
        var rep = await _db.Repertoires
            .Include(r => r.Files)
            .FirstOrDefaultAsync(r => r.Id == repertoireId && r.UserId == userId)
            ?? throw new KeyNotFoundException("Repertoire not found.");

        return string.Join("\n\n", rep.Files.Select(f => f.PgnContent));
    }

    public async Task<List<ExtensionRepertoireDto>> GetExtensionListAsync(int userId)
    {
        return await _db.Repertoires
            .Where(r => r.UserId == userId)
            .Select(r => new ExtensionRepertoireDto
            {
                Id = r.Id,
                Name = r.Name,
                FileCount = r.Files.Count
            })
            .ToListAsync();
    }
}
