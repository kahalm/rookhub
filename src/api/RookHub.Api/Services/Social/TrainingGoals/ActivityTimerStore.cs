using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Social;
using RookHub.Api.Models;

namespace RookHub.Api.Services.Social.TrainingGoals;

/// <summary>Aktivitäts-Vorlagen und der laufende Timer (höchstens einer je User); Stoppen erzeugt einen manuellen Eintrag.</summary>
internal sealed class ActivityTimerStore
{
    private readonly AppDbContext _db;
    private readonly Action<int> _invalidateDailySeries;

    /// <param name="invalidateDailySeries">verwirft die gecachte Tagesreihe des Users (Fassade, Codereview N9-005).</param>
    public ActivityTimerStore(AppDbContext db, Action<int> invalidateDailySeries)
    {
        _db = db;
        _invalidateDailySeries = invalidateDailySeries;
    }

    private void InvalidateDailySeries(int userId) => _invalidateDailySeries(userId);

    // ----- Aktivitäts-Vorlagen + Timer -------------------------------------
    //
    // Vorlagen (ActivityPreset) sind wiederverwendbare Kurztexte („Coaching mit Trainer X", „Buch Y")
    // mit einer <see cref="ManualActivityKind"/> — vom User selbst gepflegt, per Dashboard-„+"-Knopf
    // schnell startbar. Der Timer (ActivityTimer, max. 1 je User) läuft solange, bis der User ihn
    // stoppt (Backdate möglich via <see cref="StopActivityTimerDto.EndedAt"/>, falls das Ausschalten
    // vergessen wurde) — dann entsteht ein <see cref="ManualActivity"/>-Eintrag mit gerundeten Minuten
    // und die Vorlage bleibt für den nächsten Start bestehen. Vorlagen dürfen nur Minuten-Arten
    // tragen (OtbGame ist als getakteter Timer nicht sinnvoll — dafür bleibt der bestehende Manual-Add).

    private static bool IsTimerKind(ManualActivityKind kind) => kind switch
    {
        ManualActivityKind.OfflinePuzzle => true,
        ManualActivityKind.OfflineStudy => true,
        ManualActivityKind.Coaching => true,
        _ => false,
    };

    public async Task<List<ActivityPresetDto>> ListPresetsAsync(int userId)
        => await _db.ActivityPresets.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.Id)
            .Select(p => new ActivityPresetDto { Id = p.Id, Label = p.Label, Kind = p.Kind, Theme = p.Theme })
            .ToListAsync();

    public async Task<ActivityPresetDto> AddPresetAsync(int userId, ActivityPresetInputDto dto)
    {
        var label = (dto.Label ?? string.Empty).Trim();
        if (label.Length == 0) throw new ArgumentException("Label darf nicht leer sein.");
        if (!IsTimerKind(dto.Kind)) throw new ArgumentException("Für Timer-Vorlagen sind nur Minuten-Arten erlaubt.");
        var entity = new ActivityPreset
        {
            UserId = userId, Label = label, Kind = dto.Kind, Theme = dto.Theme,
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        _db.ActivityPresets.Add(entity);
        await _db.SaveChangesAsync();
        return new ActivityPresetDto { Id = entity.Id, Label = entity.Label, Kind = entity.Kind, Theme = entity.Theme };
    }

    public async Task<ActivityPresetDto?> UpdatePresetAsync(int userId, int id, ActivityPresetInputDto dto)
    {
        var entity = await _db.ActivityPresets.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (entity == null) return null;
        var label = (dto.Label ?? string.Empty).Trim();
        if (label.Length == 0) throw new ArgumentException("Label darf nicht leer sein.");
        if (!IsTimerKind(dto.Kind)) throw new ArgumentException("Für Timer-Vorlagen sind nur Minuten-Arten erlaubt.");
        entity.Label = label;
        entity.Kind = dto.Kind;
        entity.Theme = dto.Theme;
        entity.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new ActivityPresetDto { Id = entity.Id, Label = entity.Label, Kind = entity.Kind, Theme = entity.Theme };
    }

    public async Task<bool> DeletePresetAsync(int userId, int id)
    {
        var entity = await _db.ActivityPresets.FirstOrDefaultAsync(p => p.Id == id && p.UserId == userId);
        if (entity == null) return false;
        _db.ActivityPresets.Remove(entity);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<ActivityTimerDto?> GetTimerAsync(int userId)
    {
        var t = await _db.ActivityTimers.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId);
        return t == null ? null : ToTimerDto(t);
    }

    /// <summary>Startet einen neuen Timer. Läuft schon einer, ersetzt ihn nur <paramref name="replace"/> (der laufende wird
    /// verworfen, ohne einen ManualActivity-Eintrag); sonst <see cref="ActivityTimerRunningException"/> (→ 409 mit dem
    /// laufenden Timer). Vorher ersetzte der Start immer still — die Rückfrage hing nur am Client-Zustand (F5-008).</summary>
    public async Task<ActivityTimerDto> StartTimerAsync(int userId, StartActivityTimerDto dto, bool replace = false)
    {
        string label;
        ManualActivityKind kind;
        ChessableTheme? theme;

        if (dto.PresetId is int pid)
        {
            var preset = await _db.ActivityPresets.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid && p.UserId == userId)
                ?? throw new ArgumentException("Vorlage nicht gefunden.");
            label = preset.Label;
            kind = preset.Kind;
            theme = dto.Theme ?? preset.Theme;   // Preset-Thema als Default, Client-Override möglich
        }
        else
        {
            label = (dto.Label ?? string.Empty).Trim();
            if (label.Length == 0) throw new ArgumentException("Label darf nicht leer sein.");
            if (dto.Kind is not { } k || !IsTimerKind(k)) throw new ArgumentException("Für Timer sind nur Minuten-Arten erlaubt.");
            kind = k;
            theme = dto.Theme;
        }

        var existing = await _db.ActivityTimers.FirstOrDefaultAsync(x => x.UserId == userId);
        if (existing != null && !replace) throw new ActivityTimerRunningException(ToTimerDto(existing));
        if (existing != null) _db.ActivityTimers.Remove(existing);
        var timer = new ActivityTimer { UserId = userId, Label = label, Kind = kind, Theme = theme, StartedAt = DateTime.UtcNow };
        _db.ActivityTimers.Add(timer);
        await _db.SaveChangesAsync();
        return ToTimerDto(timer);
    }

    /// <summary>Stoppt den laufenden Timer, erzeugt einen <see cref="ManualActivity"/>-Eintrag
    /// mit der gerechneten Dauer (in Minuten, gerundet, geklemmt) und entfernt den Timer. 404 wenn
    /// kein Timer läuft. <see cref="StopActivityTimerDto.StartedAt"/> UND <see cref="StopActivityTimerDto.EndedAt"/>
    /// dürfen zurückdatiert werden — der Client hält Start/Ende/Dauer selbst konsistent, der Server
    /// validiert nur Start ≤ Ende ≤ jetzt.</summary>
    public async Task<ManualActivityDto?> StopTimerAsync(int userId, StopActivityTimerDto dto)
    {
        var timer = await _db.ActivityTimers.FirstOrDefaultAsync(x => x.UserId == userId);
        if (timer == null) return null;

        var now = DateTime.UtcNow;
        var startedAt = timer.StartedAt;
        if (!string.IsNullOrWhiteSpace(dto.StartedAt))
        {
            if (!DateTime.TryParse(dto.StartedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsedStart))
                throw new ArgumentException("Ungültige Startzeit (ISO 8601 erwartet).");
            startedAt = parsedStart.ToUniversalTime();
            if (startedAt > now) throw new ArgumentException("Start darf nicht in der Zukunft liegen.");
        }

        var endedAt = now;
        if (!string.IsNullOrWhiteSpace(dto.EndedAt))
        {
            if (!DateTime.TryParse(dto.EndedAt, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal,
                out var parsedEnd))
                throw new ArgumentException("Ungültiges Enddatum (ISO 8601 erwartet).");
            endedAt = parsedEnd.ToUniversalTime();
            if (endedAt > now) endedAt = now;
        }
        if (endedAt < startedAt) throw new ArgumentException("Ende darf nicht vor dem Start liegen.");

        var seconds = (int)Math.Round((endedAt - startedAt).TotalSeconds);
        var minutes = Math.Max(1, Math.Min(600, (int)Math.Round(seconds / 60.0)));
        var date = DateOnly.FromDateTime(endedAt);

        var manual = new ManualActivity
        {
            UserId = userId,
            Date = date,
            Kind = timer.Kind,
            Amount = minutes,
            Note = ComposeTimerNote(timer.Label, dto.Note),
            Theme = dto.Theme ?? timer.Theme,   // Override > Timer > null
            CreatedAt = DateTime.UtcNow,
        };
        _db.ManualActivities.Add(manual);
        _db.ActivityTimers.Remove(timer);
        await _db.SaveChangesAsync();
        InvalidateDailySeries(userId);

        return ManualActivityLog.ToDto(manual);
    }

    /// <summary>Spaltenbreite von <see cref="ManualActivity.Note"/> (Data/Configurations/TrainingConfigurations.cs,
    /// ManualActivityConfiguration: HasMaxLength(200)).</summary>
    internal const int ManualNoteMaxLength = 200;

    /// <summary>Notiz des Timer-Eintrags: „{Label} — {Notiz}", gekürzt auf <see cref="ManualNoteMaxLength"/>.
    /// Label (bis 100) und Notiz (bis 200) passen zusammen nicht in die Spalte — ungekürzt lehnte MariaDB
    /// den INSERT ab, das Stoppen scheiterte mit 500 und der Timer blieb stehen (Codereview N9-006).
    /// Kein Schnitt mitten in einem Surrogat-Paar.</summary>
    internal static string ComposeTimerNote(string label, string? note)
    {
        var text = string.IsNullOrWhiteSpace(note) ? label : $"{label} — {note.Trim()}";
        if (text.Length <= ManualNoteMaxLength) return text;
        var cut = char.IsHighSurrogate(text[ManualNoteMaxLength - 1]) ? ManualNoteMaxLength - 1 : ManualNoteMaxLength;
        return text[..cut].TrimEnd();
    }

    /// <summary>Wirft den laufenden Timer weg, ohne einen Eintrag zu erzeugen. true wenn etwas
    /// entfernt wurde, sonst false (kein Timer war aktiv).</summary>
    public async Task<bool> DiscardTimerAsync(int userId)
    {
        var timer = await _db.ActivityTimers.FirstOrDefaultAsync(x => x.UserId == userId);
        if (timer == null) return false;
        _db.ActivityTimers.Remove(timer);
        await _db.SaveChangesAsync();
        return true;
    }

    private static ActivityTimerDto ToTimerDto(ActivityTimer t)
    {
        var elapsed = (int)Math.Max(0, Math.Round((DateTime.UtcNow - t.StartedAt).TotalSeconds));
        return new ActivityTimerDto
        {
            Label = t.Label,
            Kind = t.Kind,
            Theme = t.Theme,
            StartedAt = DateTime.SpecifyKind(t.StartedAt, DateTimeKind.Utc).ToString("o"),
            ElapsedSeconds = elapsed,
        };
    }
}
