using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
        // Themen kommen als CSV aus der Spalte und werden ERST NACH dem Laden zerlegt — `ParseThemes`
        // ist C# und ließe sich nicht in SQL übersetzen.
        var raw = await _db.Worksheets
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.IsClipboard)
            .ThenByDescending(w => w.UpdatedAt)
            .Select(w => new
            {
                w.Id, w.Name, w.IsClipboard, w.PerPage, w.Themes, w.ShareToken, w.CreatedAt, w.UpdatedAt,
                ItemCount = w.Items.Count,
            })
            .ToListAsync();

        var rows = raw.Select(w => new WorksheetSummaryDto
        {
            Id = w.Id,
            Name = w.Name,
            IsClipboard = w.IsClipboard,
            PerPage = w.PerPage,
            ItemCount = w.ItemCount,
            Themes = ParseThemes(w.Themes),
            ShareToken = w.ShareToken,
            CreatedAt = w.CreatedAt,
            UpdatedAt = w.UpdatedAt,
        }).ToList();

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
        if (dto.Themes != null) sheet.Themes = string.Join(',', NormalizeThemes(dto.Themes));
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
                SolutionMoves = CleanUciMoves(dto.SolutionMoves),
                SourceThemes = CleanSourceThemes(dto.SourceThemes),
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

    // ===== Teilen =====

    /// <summary>
    /// Öffentlichen Link einschalten (idempotent: ein vorhandener bleibt, damit gedruckte QR-Codes
    /// gültig bleiben). Die Zwischenablage teilt man nicht — sie ist Arbeitsfläche, ihr Inhalt
    /// wechselt ständig; ein Link darauf zeigte morgen etwas anderes.
    /// </summary>
    public async Task<string?> ShareAsync(int userId, int id)
    {
        var sheet = await _db.Worksheets.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (sheet == null || sheet.IsClipboard) return null;

        if (string.IsNullOrEmpty(sheet.ShareToken))
        {
            sheet.ShareToken = await NewUniqueTokenAsync();
            sheet.SharedAt = DateTime.UtcNow;
            sheet.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }
        return sheet.ShareToken;
    }

    /// <summary>Link abschalten. Ein späteres Teilen erzeugt ein NEUES Token — gedruckte QR-Codes
    /// laufen danach ins Leere, und genau dafür ist das Abschalten da.</summary>
    public async Task<bool> UnshareAsync(int userId, int id)
    {
        var sheet = await _db.Worksheets.FirstOrDefaultAsync(w => w.Id == id && w.UserId == userId);
        if (sheet == null) return false;

        sheet.ShareToken = null;
        sheet.SharedAt = null;
        sheet.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return true;
    }

    /// <summary>Das geteilte Blatt hinter dem Link (ohne Anmeldung); <c>null</c> bei unbekanntem Token.</summary>
    public async Task<SharedWorksheetDto?> GetSharedAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var sheet = await _db.Worksheets
            .Include(w => w.Items)
            .FirstOrDefaultAsync(w => w.ShareToken == token);
        if (sheet == null) return null;

        return new SharedWorksheetDto
        {
            Name = sheet.Name,
            Items = sheet.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => new SharedWorksheetItemDto
            {
                Fen = i.Fen,
                Orientation = i.Orientation,
                Heading = i.Heading,
                Text = i.Text,
                SolutionMoves = i.SolutionMoves,
            }).ToList(),
        };
    }

    private async Task<string> NewUniqueTokenAsync()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var token = NewToken();
            if (!await _db.Worksheets.AnyAsync(w => w.ShareToken == token)) return token;
        }
        return NewToken();   // extrem unwahrscheinlicher Kollisions-Fallback
    }

    /// <summary>URL-sicheres Zufallstoken (~22 Zeichen aus 16 Bytes) — wie beim Partie-/Linien-Link.</summary>
    private static string NewToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
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

    /// <summary>
    /// Lösungszüge säubern: nur echte UCI-Halbzüge (<c>e2e4</c>, <c>e7e8q</c>) bleiben stehen. Der
    /// Client schickt sie, also wird hier geprüft statt vertraut — was hier landet, geht später
    /// ohne Anmeldung über den geteilten Link wieder hinaus.
    /// </summary>
    public static string CleanUciMoves(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var moves = value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(m => UciMove.IsMatch(m))
            .ToList();

        var joined = string.Join(' ', moves);
        if (joined.Length <= 1000) return joined;

        // Deckel der Spalte: lieber die Lösung hinten kappen als den Satz abschneiden.
        while (moves.Count > 0 && string.Join(' ', moves).Length > 1000) moves.RemoveAt(moves.Count - 1);
        return string.Join(' ', moves);
    }

    private static readonly Regex UciMove = new("^[a-h][1-8][a-h][1-8][qrbnQRBN]?$", RegexOptions.Compiled);

    /// <summary>
    /// Themen säubern: getrimmt, ohne Kommas (die trennen die Spalte), höchstens 40 Zeichen je
    /// Thema und 12 Themen je Blatt, dedupliziert OHNE Rücksicht auf Groß-/Kleinschreibung (die
    /// erste Schreibweise gewinnt — „Gabel" und „gabel" sind dasselbe Fach).
    /// </summary>
    public static List<string> NormalizeThemes(IEnumerable<string>? themes)
    {
        var result = new List<string>();
        foreach (var raw in themes ?? Enumerable.Empty<string>())
        {
            var theme = (raw ?? string.Empty).Replace(',', ' ').Trim();
            if (theme.Length == 0) continue;
            if (theme.Length > 40) theme = theme[..40].Trim();
            if (result.Any(t => string.Equals(t, theme, StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(theme);
            if (result.Count >= 12) break;
        }
        return result;
    }

    /// <summary>Themen-CSV → Liste (leer bleibt leer; anders als beim Kurs gibt es keinen Default).</summary>
    public static List<string> ParseThemes(string? csv)
        => string.IsNullOrWhiteSpace(csv)
            ? new List<string>()
            : NormalizeThemes(csv.Split(',', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Quellthemen des Puzzles (leerzeichengetrennt) auf die Spaltenbreite bringen.</summary>
    private static string CleanSourceThemes(string? value)
    {
        var cleaned = string.Join(' ', (value ?? string.Empty)
            .Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (cleaned.Length <= 200) return cleaned;

        // Auf der Spaltenbreite kappen, aber am Wortende — ein halbes Thema schlägt niemand vor.
        var cut = cleaned.LastIndexOf(' ', 199);
        return cut > 0 ? cleaned[..cut] : cleaned[..200];
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
        Themes = ParseThemes(sheet.Themes),
        ShareToken = sheet.ShareToken,
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
        SolutionMoves = item.SolutionMoves,
        SourceThemes = item.SourceThemes,
        Source = item.Source.ToString(),
        SourceId = item.SourceId,
        BookId = item.BookId,
    };
}
