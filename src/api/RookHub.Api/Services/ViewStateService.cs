using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Anzeige-Zustaende je Nutzer lesen und schreiben (siehe <see cref="UserViewState"/>).
/// </summary>
public class ViewStateService
{
    private readonly AppDbContext _db;

    public ViewStateService(AppDbContext db) => _db = db;

    /// <summary>
    /// Welche Ansichten ihren Zustand hier ablegen duerfen. OHNE diese Liste waere der Endpunkt
    /// ein freier Speicher je Nutzer — mit ihr ist er ein Feature mit bekannten Nutzern.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedKeys =
        new HashSet<string>(StringComparer.Ordinal) { "turnier.directory", "guess.list" };

    /// <summary>
    /// Groesse des Zustands. Die Filterleiste des Turnierkalenders braucht rund 400 Zeichen; 8 KB
    /// lassen viel Luft und sind trotzdem keine Ablage.
    /// </summary>
    public const int MaxJsonLength = 8192;

    public static bool IsAllowedKey(string? key) => key != null && AllowedKeys.Contains(key);

    /// <summary>Der gespeicherte Zustand, oder <c>null</c>.</summary>
    public async Task<string?> GetAsync(int userId, string key, CancellationToken ct = default) =>
        await _db.UserViewStates.AsNoTracking()
            .Where(v => v.UserId == userId && v.ViewKey == key)
            .Select(v => v.Json)
            .FirstOrDefaultAsync(ct);

    /// <summary>
    /// Speichert den Zustand (Upsert je Nutzer und Ansicht). <c>false</c>, wenn der Inhalt kein
    /// JSON-OBJEKT ist oder zu gross — bewusst kein stilles Zurechtbiegen: was hier ankommt, ist
    /// entweder der Zustand der Oberflaeche oder ein Fehler in ihr.
    /// </summary>
    public async Task<bool> SaveAsync(int userId, string key, string? json, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength) return false;
        if (!LooksLikeJsonObject(json)) return false;

        var row = await _db.UserViewStates
            .FirstOrDefaultAsync(v => v.UserId == userId && v.ViewKey == key, ct);
        if (row == null)
        {
            _db.UserViewStates.Add(new UserViewState
            {
                UserId = userId, ViewKey = key, Json = json, UpdatedAt = DateTime.UtcNow,
            });
        }
        else
        {
            row.Json = json;
            row.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
        return true;
    }

    /// <summary>Zustand verwerfen (idempotent) — „wieder von vorn".</summary>
    public async Task DeleteAsync(int userId, string key, CancellationToken ct = default)
    {
        var row = await _db.UserViewStates
            .FirstOrDefaultAsync(v => v.UserId == userId && v.ViewKey == key, ct);
        if (row == null) return;

        _db.UserViewStates.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Ein JSON-OBJEKT, nicht bloss gueltiges JSON: eine Zahl oder eine Zeichenkette waere
    /// ebenfalls gueltig, ist aber kein Anzeige-Zustand.
    /// </summary>
    private static bool LooksLikeJsonObject(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
