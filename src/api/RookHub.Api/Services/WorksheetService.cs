using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Aufgabenblätter: die Zwischenablage (Sammelkorb) und die benannten Blätter eines Nutzers.
///
/// <para>Stellungen werden beim Senden AUSGESCHRIEBEN (FEN + Ausrichtung) statt verlinkt — ein Blatt
/// ist damit ein fester Ausdruck-Entwurf, den ein Neuimport des Kurses nicht mehr verändert. Die
/// Herkunft (Quelle/ID/Buch) bleibt nur als Vermerk erhalten.</para>
/// </summary>
public class WorksheetService
{
    /// <summary>Obergrenze je Blatt (40 volle Seiten à 6 Diagramme) — hält Druckansicht und Antwort klein.</summary>
    public const int MaxItemsPerSheet = 240;

    /// <summary>Erlaubte Dichten (Diagramme je A4-Seite).</summary>
    public static readonly int[] AllowedPerPage = { 2, 4, 6 };

    private readonly AppDbContext _db;

    public WorksheetService(AppDbContext db) => _db = db;

    /// <summary>Die eine Zwischenablage des Nutzers; legt sie beim ersten Zugriff an.</summary>
    public async Task<Worksheet> GetOrCreateClipboardAsync(int userId)
    {
        var clip = await _db.Worksheets.FirstOrDefaultAsync(w => w.UserId == userId && w.IsClipboard);
        if (clip != null) return clip;

        clip = new Worksheet { UserId = userId, IsClipboard = true, Name = string.Empty };
        _db.Worksheets.Add(clip);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Zwei parallele Tabs haben gleichzeitig gesendet → die zuerst angelegte Ablage gewinnt.
            _db.Entry(clip).State = EntityState.Detached;
            var winner = await _db.Worksheets.FirstOrDefaultAsync(w => w.UserId == userId && w.IsClipboard);
            if (winner == null) throw;   // kein Rennen, sondern ein echter Schreibfehler
            clip = winner;
        }
        return clip;
    }

    /// <summary>Übersicht: Zwischenablage zuerst, danach die benannten Blätter (zuletzt geändert zuerst).</summary>
    public async Task<List<WorksheetSummaryDto>> ListAsync(int userId)
    {
        var rows = await _db.Worksheets
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.IsClipboard)
            .ThenByDescending(w => w.UpdatedAt)
            .Select(w => new WorksheetSummaryDto
            {
                Id = w.Id,
                Name = w.Name,
                IsClipboard = w.IsClipboard,
                PerPage = w.PerPage,
                ItemCount = w.Items.Count,
                CreatedAt = w.CreatedAt,
                UpdatedAt = w.UpdatedAt,
            })
            .ToListAsync();

        // Ohne Zwischenablage wirkt die Übersicht leer, obwohl es sie immer gibt → anlegen und voranstellen.
        if (!rows.Any(r => r.IsClipboard))
        {
            var clip = await GetOrCreateClipboardAsync(userId);
            rows.Insert(0, new WorksheetSummaryDto
            {
                Id = clip.Id, Name = clip.Name, IsClipboard = true, PerPage = clip.PerPage,
                ItemCount = 0, CreatedAt = clip.CreatedAt, UpdatedAt = clip.UpdatedAt,
            });
        }
        return rows;
    }

    /// <summary>Ein Blatt samt Stellungen; <c>null</c>, wenn es dem Nutzer nicht gehört.</summary>
    public async Task<WorksheetDto?> GetAsync(int userId, int id)
    {
        var sheet = await LoadAsync(userId, id);
        return sheet == null ? null : ToDto(sheet);
    }

    /// <summary>Die Zwischenablage samt Stellungen (legt sie bei Bedarf an).</summary>
    public async Task<WorksheetDto> GetClipboardAsync(int userId)
    {
        var clip = await GetOrCreateClipboardAsync(userId);
        return await GetAsync(userId, clip.Id) ?? ToDto(clip);
    }

    /// <summary>Leeres benanntes Blatt anlegen.</summary>
    public async Task<WorksheetDto> CreateAsync(int userId, string name, int? perPage)
    {
        var sheet = new Worksheet
        {
            UserId = userId,
            Name = CleanName(name),
            PerPage = NormalizePerPage(perPage),
        };
        _db.Worksheets.Add(sheet);
        await _db.SaveChangesAsync();
        return ToDto(sheet);
    }

    /// <summary>
    /// „Als Aufgabenblatt speichern": die Stellungen der Zwischenablage WANDERN in ein neues benanntes
    /// Blatt — danach ist die Ablage leer und frei fürs nächste Blatt.
    /// </summary>
    public async Task<WorksheetDto?> SaveClipboardAsAsync(int userId, string name, int? perPage)
    {
        var clip = await LoadAsync(userId, (await GetOrCreateClipboardAsync(userId)).Id);
        if (clip == null || clip.Items.Count == 0) return null;

        var sheet = new Worksheet
        {
            UserId = userId,
            Name = CleanName(name),
            PerPage = NormalizePerPage(perPage ?? clip.PerPage),
        };
        _db.Worksheets.Add(sheet);
        await _db.SaveChangesAsync();

        var order = 0;
        foreach (var item in clip.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
        {
            item.WorksheetId = sheet.Id;
            item.SortOrder = order++;
        }
        clip.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await GetAsync(userId, sheet.Id);
    }

    /// <summary>Umbenennen und/oder Dichte ändern. Die Zwischenablage behält ihren (leeren) Namen.</summary>
    public async Task<WorksheetDto?> UpdateAsync(int userId, int id, UpdateWorksheetDto dto)
    {
        var sheet = await LoadAsync(userId, id);
        if (sheet == null) return null;

        if (dto.Name != null && !sheet.IsClipboard) sheet.Name = CleanName(dto.Name);
        if (dto.PerPage.HasValue) sheet.PerPage = NormalizePerPage(dto.PerPage);
        sheet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(sheet);
    }

    /// <summary>Blatt löschen. Die Zwischenablage wird nur GELEERT (sie gehört zum Nutzer, nicht zum Blatt).</summary>
    public async Task<bool> DeleteAsync(int userId, int id)
    {
        var sheet = await LoadAsync(userId, id);
        if (sheet == null) return false;

        if (sheet.IsClipboard)
        {
            _db.WorksheetItems.RemoveRange(sheet.Items);
            sheet.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            _db.Worksheets.Remove(sheet);
        }
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Alle Stellungen eines Blatts entfernen (das Blatt selbst bleibt).</summary>
    public async Task<WorksheetDto?> ClearAsync(int userId, int id)
    {
        var sheet = await LoadAsync(userId, id);
        if (sheet == null) return null;

        _db.WorksheetItems.RemoveRange(sheet.Items);
        sheet.Items.Clear();
        sheet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(sheet);
    }

    /// <summary>
    /// „An Aufgabenblatt senden": Stellungen anhängen. <paramref name="worksheetId"/> <c>null</c>/<c>0</c>
    /// zielt auf die Zwischenablage. Schon vorhandene Stellungen (gleiche FEN + Ausrichtung) werden
    /// übersprungen, damit zweimal Senden nicht doppelt druckt.
    /// </summary>
    public async Task<AddWorksheetItemsResultDto?> AddItemsAsync(int userId, int? worksheetId, List<NewWorksheetItemDto> items)
    {
        var target = worksheetId is null or 0
            ? await GetOrCreateClipboardAsync(userId)
            : await _db.Worksheets.FirstOrDefaultAsync(w => w.Id == worksheetId && w.UserId == userId);
        if (target == null) return null;

        var existing = await _db.WorksheetItems
            .Where(i => i.WorksheetId == target.Id)
            .Select(i => new { i.Fen, i.Orientation, i.SortOrder })
            .ToListAsync();

        var seen = existing.Select(e => Key(e.Fen, e.Orientation)).ToHashSet();
        var order = existing.Count == 0 ? 0 : existing.Max(e => e.SortOrder) + 1;
        var count = existing.Count;

        var result = new AddWorksheetItemsResultDto
        {
            WorksheetId = target.Id, Name = target.Name, IsClipboard = target.IsClipboard,
        };

        foreach (var dto in items)
        {
            var fen = (dto.Fen ?? string.Empty).Trim();
            if (!IsPlausibleFen(fen)) { result.Skipped++; continue; }

            var orientation = NormalizeOrientation(dto.Orientation);
            if (!seen.Add(Key(fen, orientation))) { result.Skipped++; continue; }
            if (count >= MaxItemsPerSheet) { result.Full = true; break; }

            _db.WorksheetItems.Add(new WorksheetItem
            {
                WorksheetId = target.Id,
                SortOrder = order++,
                Fen = fen,
                Orientation = orientation,
                Heading = Clean(dto.Heading, 200),
                Text = Clean(dto.Text, 2000),
                Source = dto.Source,
                SourceId = dto.SourceId,
                BookId = dto.BookId,
            });
            result.Added++;
            count++;
        }

        target.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        result.Total = count;
        return result;
    }

    /// <summary>Überschrift/Begleittext/Ausrichtung einer Aufgabe ändern (nur gesetzte Felder wirken).</summary>
    public async Task<WorksheetItemDto?> UpdateItemAsync(int userId, int id, int itemId, UpdateWorksheetItemDto dto)
    {
        var item = await FindItemAsync(userId, id, itemId);
        if (item == null) return null;

        if (dto.Heading != null) item.Heading = Clean(dto.Heading, 200);
        if (dto.Text != null) item.Text = Clean(dto.Text, 2000);
        if (dto.Orientation != null) item.Orientation = NormalizeOrientation(dto.Orientation);
        item.Worksheet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(item);
    }

    /// <summary>Eine Aufgabe vom Blatt nehmen.</summary>
    public async Task<bool> DeleteItemAsync(int userId, int id, int itemId)
    {
        var item = await FindItemAsync(userId, id, itemId);
        if (item == null) return false;

        _db.WorksheetItems.Remove(item);
        item.Worksheet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Neue Reihenfolge setzen. Übergeben werden die Item-IDs in Wunsch-Abfolge; unbekannte IDs
    /// werden ignoriert, nicht genannte Aufgaben hängen sich hinten an (eine halbe Liste vom Client
    /// darf keine Stellung verschwinden lassen).
    /// </summary>
    public async Task<WorksheetDto?> ReorderAsync(int userId, int id, List<int> itemIds)
    {
        var sheet = await LoadAsync(userId, id);
        if (sheet == null) return null;

        var byId = sheet.Items.ToDictionary(i => i.Id);
        var order = 0;
        foreach (var itemId in itemIds.Distinct())
            if (byId.Remove(itemId, out var item)) item.SortOrder = order++;
        foreach (var rest in byId.Values.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
            rest.SortOrder = order++;

        sheet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return ToDto(sheet);
    }

    // ===== intern =====

    private Task<Worksheet?> LoadAsync(int userId, int id)
        => _db.Worksheets.Include(w => w.Items).FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);

    private async Task<WorksheetItem?> FindItemAsync(int userId, int id, int itemId)
        => await _db.WorksheetItems
            .Include(i => i.Worksheet)
            .FirstOrDefaultAsync(i => i.Id == itemId && i.WorksheetId == id && i.Worksheet.UserId == userId);

    private static string Key(string fen, string orientation) => fen + '|' + orientation;

    /// <summary>Erlaubte Dichte erzwingen (2/4/6); alles andere fällt auf 6 zurück.</summary>
    public static int NormalizePerPage(int? perPage)
        => perPage.HasValue && AllowedPerPage.Contains(perPage.Value) ? perPage.Value : 6;

    private static string NormalizeOrientation(string? orientation)
        => string.Equals(orientation?.Trim(), "black", StringComparison.OrdinalIgnoreCase) ? "black" : "white";

    private static string CleanName(string name)
    {
        var cleaned = (name ?? string.Empty).Trim();
        return cleaned.Length > 120 ? cleaned[..120] : cleaned;
    }

    private static string Clean(string? value, int max)
    {
        var cleaned = (value ?? string.Empty).Trim();
        return cleaned.Length > max ? cleaned[..max] : cleaned;
    }

    /// <summary>
    /// Grobprüfung der FEN: acht durch <c>/</c> getrennte Reihen und eine Zugpartei. BEWUSST ohne
    /// Legalitätsprüfung — Kurse enthalten Muster-Diagramme mit absichtlich illegalen Stellungen
    /// (zwei Könige zu wenig o. ä.), und die sollen aufs Blatt dürfen.
    /// </summary>
    public static bool IsPlausibleFen(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen) || fen.Length > 120) return false;
        var parts = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return false;
        if (parts[0].Split('/').Length != 8) return false;
        return parts[1] is "w" or "b";
    }

    private static WorksheetDto ToDto(Worksheet sheet) => new()
    {
        Id = sheet.Id,
        Name = sheet.Name,
        IsClipboard = sheet.IsClipboard,
        PerPage = sheet.PerPage,
        ItemCount = sheet.Items.Count,
        CreatedAt = sheet.CreatedAt,
        UpdatedAt = sheet.UpdatedAt,
        Items = sheet.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(ToDto).ToList(),
    };

    private static WorksheetItemDto ToDto(WorksheetItem item) => new()
    {
        Id = item.Id,
        SortOrder = item.SortOrder,
        Fen = item.Fen,
        Orientation = item.Orientation,
        Heading = item.Heading,
        Text = item.Text,
        Source = item.Source.ToString(),
        SourceId = item.SourceId,
        BookId = item.BookId,
    };
}
