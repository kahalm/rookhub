using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// EINE Quelle für die Lese-Zugriffsregel auf Repertoires: Besitzer ODER Empfänger einer
/// <see cref="RepertoireShare"/>-Freigabe. Vorher lag das Prädikat in vier Kopien
/// (RepertoireService, RepertoireTrainingService, Datei-Download, Positionssuche) — eine Änderung
/// der Sharing-Semantik (z. B. widerrufbare/ablaufende oder Gruppen-Freigaben) hätte still nur
/// einen Teil der Pfade erreicht (Autorisierungs-Drift zwischen Ansehen/Trainieren/Download/Suche).
/// </summary>
public static class RepertoireAccess
{
    /// <summary>Alle Repertoires, die der User lesen/trainieren darf (eigene + mit ihm geteilte).</summary>
    public static IQueryable<Repertoire> ReadableBy(AppDbContext db, int userId) =>
        db.Repertoires.Where(r => r.UserId == userId
            || db.RepertoireShares.Any(s => s.RepertoireId == r.Id && s.RecipientId == userId));

    /// <summary>Darf der User dieses Repertoire lesen/trainieren?</summary>
    public static Task<bool> CanReadAsync(AppDbContext db, int repertoireId, int userId, CancellationToken ct = default) =>
        ReadableBy(db, userId).AnyAsync(r => r.Id == repertoireId, ct);
}
