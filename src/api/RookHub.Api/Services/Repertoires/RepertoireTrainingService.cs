using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Accounts;
using RookHub.Api.DTOs.Repertoires;

namespace RookHub.Api.Services.Repertoires;

/// <summary>
/// Spaced-Repetition-Scheduling für den Repertoire-Trainer als feste 9-Stufen-Leiter (seit v0.245,
/// ersetzt die SM-2-Variante). SR-Einheit ist die ganze PGN-LINIE: richtig gespielt → +1 Stufe,
/// ein Fehler irgendwo in der Linie → zurück auf Stufe 1. Jede Stufe hat ein Intervall, das
/// bestimmt, wann die Linie wieder fällig wird; Intervalle sind pro Nutzer (global) einstellbar und
/// pro Repertoire übersteuerbar. Das Frontend liefert einen stabilen Linien-Schlüssel und ermittelt
/// aus den hier gelieferten Zuständen die fälligen Linien.
/// </summary>
public class RepertoireTrainingService
{
    private readonly AppDbContext _db;
    public RepertoireTrainingService(AppDbContext db) => _db = db;

    /// <summary>Eingebaute Standard-Intervalle der 9 Stufen (Vorgabe des Nutzers).</summary>
    public static readonly List<SrLevelDto> DefaultLevels = new()
    {
        new(4, "h"), new(10, "h"), new(24, "h"),
        new(2.5, "d"), new(1, "w"), new(2.5, "w"),
        new(1.5, "mo"), new(3, "mo"), new(6, "mo"),
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    // ===== Konfiguration =====

    public static double HoursOf(SrLevelDto l) => l.Unit switch
    {
        "h" => l.Value,
        "d" => l.Value * 24,
        "w" => l.Value * 24 * 7,
        "mo" => l.Value * 24 * 30,
        _ => l.Value,
    };

    /// <summary>Genau 9 Stufen, jeder Wert &gt; 0 und Einheit gültig.</summary>
    public static bool ValidLevels(List<SrLevelDto>? levels) =>
        levels is { Count: 9 } &&
        levels.All(l => l.Value > 0 && l.Value <= 100_000 && l.Unit is "h" or "d" or "w" or "mo");

    private static List<SrLevelDto>? ParseLevels(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var levels = JsonSerializer.Deserialize<List<SrLevelDto>>(json, JsonOpts);
            return ValidLevels(levels) ? levels : null;
        }
        catch { return null; }
    }

    /// <summary>Effektive Konfiguration eines Repertoires (Override &gt; global &gt; Default) samt
    /// beider Ebenen, damit das Frontend sie bearbeiten kann. Null wenn das Repertoire nicht dem
    /// User gehört.</summary>
    public async Task<SrConfigDto?> GetConfigAsync(int userId, int repertoireId, CancellationToken ct = default)
    {
        // Trainieren erlaubt für Besitzer ODER Empfänger einer Freigabe; die pro-Repertoire-Intervalle
        // (rep.SrIntervalsJson) sind die des Besitzers, die globalen die des trainierenden Users.
        if (!await CanTrainAsync(userId, repertoireId, ct)) return null;
        var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == repertoireId, ct);
        if (rep == null) return null;

        var userLevels = ParseLevels((await GetSettingsAsync(userId, ct))?.IntervalsJson);
        var repLevels = ParseLevels(rep.SrIntervalsJson);

        var effective = repLevels ?? userLevels ?? DefaultLevels;
        var source = repLevels != null ? "repertoire" : userLevels != null ? "user" : "default";
        // Ein Wert, den die Liste nicht (mehr) kennt, gilt als „nichts gewählt“ — das Frontend fällt auf die Vorgabe.
        var pref = await _db.RepertoireTrainingPrefs.AsNoTracking()
            .Where(p => p.UserId == userId && p.RepertoireId == repertoireId)
            .Select(p => new { p.LineOrder, p.ChapterColorsJson }).FirstOrDefaultAsync(ct);
        var lineOrder = pref?.LineOrder;
        return new SrConfigDto(effective, userLevels ?? DefaultLevels, repLevels, source)
        {
            // Ein gespeichertes „opponent“ bleibt stehen, auch wenn der Gegner später fehlt — das Frontend fällt dann
            // auf die Vorgabe (HasOpponent sagt ihm, ob der Wert gilt).
            LineOrder = RepertoireLineOrders.IsValid(lineOrder) ? lineOrder : null,
            HasOpponent = RepertoireOpponentDto.Parse(rep.OpponentJson) is not null,
            ChapterColors = ReadChapterColors(pref?.ChapterColorsJson),
        };
    }

    public const int MaxChapterColors = 1000;
    public const int MaxChapterNameLength = 200;

    /// <summary>Eigene Kapitelfarben setzen (1.020.2): übergebene Kapitel überschreiben, die übrigen bleiben. null =
    /// kein Lesezugriff (404), false = ungültig (Farbe nicht w/b, Name zu lang, zu viele).</summary>
    public async Task<bool?> SetChapterColorsAsync(int userId, int repertoireId, IReadOnlyDictionary<string, string> colors, CancellationToken ct = default)
    {
        if (!await CanTrainAsync(userId, repertoireId, ct)) return null;
        if (colors.Count > MaxChapterColors
            || colors.Any(kv => kv.Key is null || kv.Key.Length > MaxChapterNameLength || kv.Value is not ("w" or "b")))
            return false;
        for (var attempt = 0; ; attempt++)
        {
            var pref = await _db.RepertoireTrainingPrefs.FirstOrDefaultAsync(p => p.UserId == userId && p.RepertoireId == repertoireId, ct);
            var merged = ReadChapterColors(pref?.ChapterColorsJson) ?? new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (k, v) in colors) merged[k] = v;
            if (merged.Count > MaxChapterColors) return false;
            var json = System.Text.Json.JsonSerializer.Serialize(merged);
            if (pref == null)
                _db.RepertoireTrainingPrefs.Add(new RepertoireTrainingPref { UserId = userId, RepertoireId = repertoireId, ChapterColorsJson = json });
            else { pref.ChapterColorsJson = json; pref.UpdatedAt = DateTime.UtcNow; }
            try { await _db.SaveChangesAsync(ct); return true; }
            catch (DbUpdateException ex) when (pref == null && attempt == 0 && AuthService.IsUniqueViolation(ex)) { _db.ChangeTracker.Clear(); }
        }
    }

    private static Dictionary<string, string>? ReadChapterColors(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var d = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return d?.Where(kv => kv.Value is "w" or "b").ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        }
        catch (System.Text.Json.JsonException) { return null; }
    }

    /// <summary>Reihenfolge der Linien im Trainer für DIESEN Nutzer und dieses Repertoire setzen (0.996.0) — erlaubt für
    /// Besitzer UND Freigabe-Empfänger, jeder mit eigenem Wert (wie der SR-Fortschritt). Null ohne Lesezugriff bzw. bei
    /// unbekanntem Repertoire, false bei einem Wert außerhalb von <see cref="RepertoireLineOrders.All"/> — und bei
    /// <c>opponent</c> für ein Repertoire ohne gespeicherten Gegner (<see cref="RepertoireLineOrders.IsValidFor"/>).</summary>
    public async Task<bool?> SetLineOrderAsync(int userId, int repertoireId, string? order, CancellationToken ct = default)
    {
        if (!await CanTrainAsync(userId, repertoireId, ct)) return null;
        if (!RepertoireLineOrders.IsValid(order)) return false;
        if (order == RepertoireLineOrders.Opponent)
        {
            var opponentJson = await _db.Repertoires.Where(r => r.Id == repertoireId).Select(r => r.OpponentJson).FirstOrDefaultAsync(ct);
            if (!RepertoireLineOrders.IsValidFor(order, RepertoireOpponentDto.Parse(opponentJson) is not null)) return false;
        }
        if (await UpsertLineOrderAsync(userId, repertoireId, order!, ct)) return true;
        // Wettlauf auf dem Unique-Index (UserId, RepertoireId) — zwei Geräte stellen gleichzeitig um: den verworfenen
        // Insert vergessen, die Zeile des anderen laden und den eigenen Wert hineinschreiben (der letzte gewinnt).
        _db.ChangeTracker.Clear();
        if (!await UpsertLineOrderAsync(userId, repertoireId, order!, ct))
            throw new DbUpdateException("Line order could not be saved.");
        return true;
    }

    /// <summary>false = der Insert stieß auf die Zeile eines parallelen Aufrufs.</summary>
    private async Task<bool> UpsertLineOrderAsync(int userId, int repertoireId, string order, CancellationToken ct)
    {
        var pref = await _db.RepertoireTrainingPrefs
            .FirstOrDefaultAsync(p => p.UserId == userId && p.RepertoireId == repertoireId, ct);
        if (pref == null)
            _db.RepertoireTrainingPrefs.Add(new RepertoireTrainingPref { UserId = userId, RepertoireId = repertoireId, LineOrder = order });
        else { pref.LineOrder = order; pref.UpdatedAt = DateTime.UtcNow; }
        try { await _db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateException ex) when (pref == null && AuthService.IsUniqueViolation(ex)) { return false; }
    }

    /// <summary>Globale Nutzer-Intervalle der 9 Stufen (fällt auf die eingebauten Defaults zurück).</summary>
    public async Task<List<SrLevelDto>> GetUserConfigAsync(int userId, CancellationToken ct = default)
        => ParseLevels((await GetSettingsAsync(userId, ct))?.IntervalsJson) ?? DefaultLevels;

    /// <summary>Setzt die globalen Nutzer-Intervalle (null → löscht die Einstellung = Defaults).
    /// Gibt false zurück, wenn die Stufen ungültig sind.</summary>
    public async Task<bool> SetUserConfigAsync(int userId, List<SrLevelDto>? levels, CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(userId, ct);
        if (levels == null)
        {
            if (settings != null) { _db.RepertoireSrSettings.Remove(settings); await _db.SaveChangesAsync(ct); }
            return true;
        }
        if (!ValidLevels(levels)) return false;
        var json = JsonSerializer.Serialize(levels, JsonOpts);
        if (settings == null)
        {
            settings = new RepertoireSrSettings { UserId = userId, IntervalsJson = json };
            _db.RepertoireSrSettings.Add(settings);
        }
        else { settings.IntervalsJson = json; settings.UpdatedAt = DateTime.UtcNow; }
        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Setzt den pro-Repertoire-Override (null → löschen = wieder globale Defaults).
    /// Gibt null zurück, wenn das Repertoire nicht dem User gehört, false bei ungültigen Stufen.</summary>
    public async Task<bool?> SetRepertoireConfigAsync(int userId, int repertoireId, List<SrLevelDto>? levels, CancellationToken ct = default)
    {
        var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == repertoireId && r.UserId == userId, ct);
        if (rep == null) return null;
        if (levels != null && !ValidLevels(levels)) return false;
        rep.SrIntervalsJson = levels == null ? null : JsonSerializer.Serialize(levels, JsonOpts);
        await _db.SaveChangesAsync(ct);
        return true;
    }

    private Task<RepertoireSrSettings?> GetSettingsAsync(int userId, CancellationToken ct) =>
        _db.RepertoireSrSettings.FirstOrDefaultAsync(s => s.UserId == userId, ct);

    // ===== Linien-Zustände + Review =====

    /// <summary>Alle Linien-SR-Zustände des Users für ein eigenes Repertoire; null wenn das
    /// Repertoire nicht existiert / nicht dem User gehört.</summary>
    public async Task<List<LineStateDto>?> GetLineStatesAsync(int userId, int repertoireId, CancellationToken ct = default)
    {
        if (!await CanTrainAsync(userId, repertoireId, ct)) return null;
        return await _db.RepertoireCardStates
            .Where(c => c.UserId == userId && c.RepertoireId == repertoireId)
            .Select(c => new LineStateDto(c.CardKey, c.Level, c.Reps, c.Lapses, c.DueAt, c.LastReviewedAt, c.InPool, c.Paused))
            .ToListAsync(ct);
    }

    /// <summary>Bewertet eine geübte Linie (legt den Zustand bei Bedarf an) und plant sie neu.
    /// Null wenn das Repertoire nicht dem User gehört.</summary>
    public async Task<LineStateDto?> ReviewLineAsync(int userId, int repertoireId, LineReviewRequest req, CancellationToken ct = default)
    {
        // Besitzer ODER Empfänger einer Freigabe (eigener Fortschritt); Intervalle wie in GetConfigAsync.
        if (!await CanTrainAsync(userId, repertoireId, ct)) return null;
        var rep = await _db.Repertoires.FirstOrDefaultAsync(r => r.Id == repertoireId, ct);
        if (rep == null) return null;

        var userLevels = ParseLevels((await GetSettingsAsync(userId, ct))?.IntervalsJson);
        var hours = (ParseLevels(rep.SrIntervalsJson) ?? userLevels ?? DefaultLevels)
            .Select(HoursOf).ToArray();

        var card = await _db.RepertoireCardStates
            .FirstOrDefaultAsync(c => c.UserId == userId && c.RepertoireId == repertoireId && c.CardKey == req.LineKey, ct);
        if (card == null)
        {
            card = new RepertoireCardState { UserId = userId, RepertoireId = repertoireId, CardKey = req.LineKey };
            _db.RepertoireCardStates.Add(card);
        }
        card.InPool = true;   // eine bewertete Linie ist im Pool
        ScheduleLevel(card, req.Correct, hours, DateTime.UtcNow, req.Hinted);
        if (!string.IsNullOrEmpty(req.Label)) card.ExpectedMove = req.Label;

        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            // Race auf dem Unique-Index (UserId, RepertoireId, CardKey): parallelen Insert verwerfen,
            // vorhandene Zeile laden und erneut planen (idempotent).
            _db.ChangeTracker.Clear();
            card = await _db.RepertoireCardStates
                .FirstOrDefaultAsync(c => c.UserId == userId && c.RepertoireId == repertoireId && c.CardKey == req.LineKey, ct);
            if (card == null) throw;
            card.InPool = true;
            ScheduleLevel(card, req.Correct, hours, DateTime.UtcNow, req.Hinted);
            if (!string.IsNullOrEmpty(req.Label)) card.ExpectedMove = req.Label;
            await _db.SaveChangesAsync(ct);
        }

        return new LineStateDto(card.CardKey, card.Level, card.Reps, card.Lapses, card.DueAt, card.LastReviewedAt, card.InPool, card.Paused);
    }

    /// <summary>Pausiert/aktiviert einen Satz Linien (Kapitel = dessen Linien-Schlüssel). Legt für
    /// zu pausierende, noch unbekannte Linien einen Zustand an (damit die Pause erhalten bleibt).
    /// Gibt die Anzahl betroffener Linien zurück, null wenn nicht eigenes Repertoire.</summary>
    public async Task<int?> SetPausedAsync(int userId, int repertoireId, List<string> lineKeys, bool paused, CancellationToken ct = default)
    {
        if (!await CanTrainAsync(userId, repertoireId, ct)) return null;
        var keys = lineKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (keys.Count == 0) return 0;
        var now = DateTime.UtcNow;
        var existing = await _db.RepertoireCardStates
            .Where(c => c.UserId == userId && c.RepertoireId == repertoireId && keys.Contains(c.CardKey))
            .ToListAsync(ct);
        var have = existing.Select(c => c.CardKey).ToHashSet();
        foreach (var c in existing) c.Paused = paused;
        if (paused)
        {
            foreach (var k in keys.Where(k => !have.Contains(k)))
                _db.RepertoireCardStates.Add(new RepertoireCardState
                {
                    UserId = userId, RepertoireId = repertoireId, CardKey = k,
                    Level = 0, InPool = false, Paused = true, DueAt = now,
                });
        }
        await _db.SaveChangesAsync(ct);
        return keys.Count;
    }

    /// <summary>Macht bereits im Pool befindliche Linien sofort fällig (DueAt = jetzt) und hebt eine
    /// etwaige Pause auf. Leere Liste = ganzer Kurs (alle Pool-Zustände). Null wenn nicht eigenes
    /// Repertoire.</summary>
    public async Task<int?> MakeDueAsync(int userId, int repertoireId, List<string> lineKeys, CancellationToken ct = default)
    {
        if (!await CanTrainAsync(userId, repertoireId, ct)) return null;
        var keys = lineKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        var q = _db.RepertoireCardStates.Where(c => c.UserId == userId && c.RepertoireId == repertoireId && c.InPool);
        if (keys.Count > 0) q = q.Where(c => keys.Contains(c.CardKey));
        var rows = await q.ToListAsync(ct);
        var now = DateTime.UtcNow;
        foreach (var c in rows) { c.DueAt = now; c.Paused = false; }
        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }

    /// <summary>Nimmt Linien in den Übungspool auf (Learn/„In Pool aufnehmen") — sofort fällig, Pause
    /// aufgehoben; Stufe bleibt (neue Linie = Stufe 0). Legt fehlende Zustände an. Gibt die Anzahl
    /// betroffener Linien zurück, null wenn nicht eigenes Repertoire.</summary>
    public async Task<int?> PromoteAsync(int userId, int repertoireId, List<string> lineKeys, CancellationToken ct = default)
    {
        if (!await CanTrainAsync(userId, repertoireId, ct)) return null;
        var keys = lineKeys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().ToList();
        if (keys.Count == 0) return 0;
        var now = DateTime.UtcNow;
        var existing = await _db.RepertoireCardStates
            .Where(c => c.UserId == userId && c.RepertoireId == repertoireId && keys.Contains(c.CardKey))
            .ToListAsync(ct);
        var have = existing.Select(c => c.CardKey).ToHashSet();
        foreach (var c in existing) { c.InPool = true; c.Paused = false; c.DueAt = now; }
        foreach (var k in keys.Where(k => !have.Contains(k)))
            _db.RepertoireCardStates.Add(new RepertoireCardState
            {
                UserId = userId, RepertoireId = repertoireId, CardKey = k,
                Level = 0, InPool = true, Paused = false, DueAt = now,
            });
        await _db.SaveChangesAsync(ct);
        return keys.Count;
    }

    /// <summary>9-Stufen-Leiter: richtig → +1 Stufe (max 9), falsch → Stufe 1. Fälligkeit aus dem
    /// Intervall der neuen Stufe. <paramref name="hoursByLevel"/> hat 9 Einträge (Index 0 = Stufe 1).</summary>
    internal static void ScheduleLevel(RepertoireCardState card, bool correct, double[] hoursByLevel, DateTime now, bool hinted = false)
    {
        if (correct && hinted) card.Level = Math.Clamp(card.Level, 1, 9);   // mit Tipp: bleibt stehen (kein Vorrücken, kein Fehler)
        else if (correct) { card.Level = Math.Min(Math.Max(card.Level, 0) + 1, 9); card.Reps++; }
        else { card.Level = 1; card.Lapses++; }
        var idx = Math.Clamp(card.Level - 1, 0, hoursByLevel.Length - 1);
        card.DueAt = now.AddHours(hoursByLevel[idx]);
        card.LastReviewedAt = now;
    }

    /// <summary>Löscht sämtliche Linien-SR-Zustände des Users für dieses Repertoire. Gibt die Anzahl
    /// gelöschter Zeilen zurück, oder null wenn das Repertoire nicht dem User gehört.</summary>
    public async Task<int?> ResetAsync(int userId, int repertoireId, CancellationToken ct = default)
    {
        if (!await CanTrainAsync(userId, repertoireId, ct)) return null;
        var cards = await _db.RepertoireCardStates
            .Where(c => c.UserId == userId && c.RepertoireId == repertoireId)
            .ToListAsync(ct);
        if (cards.Count == 0) return 0;
        _db.RepertoireCardStates.RemoveRange(cards);
        await _db.SaveChangesAsync(ct);
        return cards.Count;
    }

    /// <summary>Darf der User dieses Repertoire TRAINIEREN? Besitzer ODER Empfänger einer Freigabe.
    /// Der SR-Fortschritt (RepertoireCardState) ist ohnehin pro User — ein geteiltes Repertoire
    /// trainiert der Empfänger mit eigenem Fortschritt. (Das Bearbeiten der pro-Repertoire-Intervalle
    /// bleibt in <see cref="SetRepertoireConfigAsync"/> owner-only.)</summary>
    private Task<bool> CanTrainAsync(int userId, int repertoireId, CancellationToken ct)
        => RepertoireAccess.CanReadAsync(_db, repertoireId, userId, ct);
}
