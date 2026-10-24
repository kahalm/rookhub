using System.Text.Json;
using System.Text.Json.Serialization;
using Chess;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.Social;
using RookHub.Api.DTOs.Scoresheet;
using RookHub.Api.DTOs.League;
using static RookHub.Api.Services.Scoresheet.ScoresheetScanRows;

namespace RookHub.Api.Services.Scoresheet;

/// <summary>
/// Korrekturweg der Einlesung (Zerlegung Z6 aus <see cref="ScoresheetScanService"/>): Stand je Halbzug einer eigenen
/// Partie bzw. einer Vereinspartie (aus dem Archiv), Rest neu aufbereiten ohne Modell, Stand nach dem Speichern ablegen.
/// </summary>
internal sealed class ScoresheetCorrection
{
    private readonly AppDbContext _db;

    internal ScoresheetCorrection(AppDbContext db) => _db = db;

    /// <summary>Das Foto einer eigenen Partie (Seite <paramref name="page"/>, ab 1); <c>null</c>, wenn es keins gibt, die
    /// Seite fehlt oder die Partie fremd ist.</summary>
    public async Task<(byte[] Data, string ContentType, string FileName, int PageCount)?> PhotoForGameAsync(int userId, int gameId,
        int page = 1)
    {
        var head = await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.SavedGameId == gameId && s.UserId == userId).Select(s => new { s.Id, s.PageCount }).FirstOrDefaultAsync();
        if (head == null) return null;
        var id = head.Id;
        var p = page <= 1
            ? await _db.ScoresheetScans.AsNoTracking().Where(s => s.Id == id)
                .Select(s => new { s.Photo, s.ContentType, s.FileName }).FirstOrDefaultAsync()
            : await _db.ScoresheetScanPages.AsNoTracking().Where(x => x.ScoresheetScanId == id && x.Page == page)
                .Select(x => new { x.Photo, x.ContentType, x.FileName }).FirstOrDefaultAsync();
        if (p == null || p.Photo.Length == 0) return null;
        var ext = p.ContentType switch { "image/png" => ".png", "image/webp" => ".webp", _ => ".jpg" };
        var suffix = page <= 1 ? "" : $"-{page}";
        return (p.Photo, p.ContentType, p.FileName ?? $"partieformular-{id}{suffix}{ext}", Math.Max(1, head.PageCount));
    }

    // ── Korrekturseite ────────────────────────────────────────────────

    /// <summary>Formular-Einträge + Stand je Halbzug einer eigenen, eingelesenen Partie.</summary>
    public async Task<ScoresheetEditStateDto?> EditStateAsync(int userId, int gameId)
    {
        var scan = await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.SavedGameId == gameId && s.UserId == userId)
            .Select(s => new { s.Id, s.NotationLanguage, s.TranscriptionJson, s.ResolutionJson, s.PageCount })
            .FirstOrDefaultAsync();
        if (scan == null) return null;
        var stored = Deserialize(scan.ResolutionJson);
        var t = ScoresheetTranscription.Parse(scan.TranscriptionJson);
        return new ScoresheetEditStateDto
        {
            ScanId = scan.Id,
            NotationLanguage = stored?.Language ?? scan.NotationLanguage,
            Written = t?.Moves.Select(m => m.Written).ToList() ?? new(),
            Boxes = t?.NormalizedBoxes() ?? new(),
            PageCount = Math.Max(1, scan.PageCount),
            Pages = t?.EntryPages() ?? new(),
            Plies = stored?.Plies ?? new(),
            Unresolved = stored?.Unresolved ?? new(),
            UnresolvedFrom = stored?.UnresolvedFrom,
        };
    }

    /// <summary>
    /// Den Rest ab einer festgelegten Stelle neu aufbereiten: die Züge in <paramref name="prefix"/> stehen fest
    /// (bestätigt, gewählt oder selbst eingegeben), die Formular-Einträge ab <paramref name="writtenFrom"/> werden
    /// neu aufgelöst — samt Unsicherheiten und Ausgängen. Kein Modell-Aufruf: das ist schnell und kostet nichts.
    /// </summary>
    /// <returns><c>null</c>, wenn es keine Einlesung zur Partie gibt; <see cref="ArgumentException"/> bei einem
    /// illegalen Präfix.</returns>
    public async Task<ScoresheetResolveResultDto?> ResolveRestAsync(int userId, int gameId, IReadOnlyList<string> prefix,
        int writtenFrom)
    {
        CheckPrefix(prefix);
        var scan = await _db.ScoresheetScans.AsNoTracking()
            .Where(s => s.SavedGameId == gameId && s.UserId == userId)
            .Select(s => new { s.NotationLanguage, s.TranscriptionJson, s.ResolutionJson })
            .FirstOrDefaultAsync();
        if (scan == null) return null;
        var t = ScoresheetTranscription.Parse(scan.TranscriptionJson);
        if (t == null) return null;
        var scanned = t.Scanned();
        var language = Deserialize(scan.ResolutionJson)?.Language ?? scan.NotationLanguage;

        var legalPrefix = SavedGameService.LegalSans(prefix);
        var from = Math.Clamp(writtenFrom, 0, scanned.Count);
        var r = ScoresheetResolver.Resolve(scanned, new ScoresheetResolver.Options(ScoresheetNotation.Find(language)),
            legalPrefix, from);
        return new ScoresheetResolveResultDto { Plies = r.Plies, Unresolved = r.Unresolved, UnresolvedFrom = r.StuckAt };
    }

    /// <summary>Das Präfix wird Halbzug für Halbzug mit allen legalen Zügen samt SAN nachgespielt, und Pendelzüge enden
    /// nie von selbst — ohne Deckel kostete eine Anfrage (auch ohne Konto, mit Teilen-Link und Schlüssel) so viel CPU,
    /// wie der Rumpf Züge fasst (Codereview 2026-09-29, A6-014). <see cref="ArgumentException"/> wie beim illegalen Zug.</summary>
    internal static void CheckPrefix(IReadOnlyList<string> prefix)
    {
        if (prefix.Count > ScoresheetScanService.MaxPrefixPlies) throw new ArgumentException($"Too many moves (max {ScoresheetScanService.MaxPrefixPlies} plies).");
    }

    // ── Formular einer Vereinspartie wieder öffnen (0.660.0, „Korrigieren" wie beim ersten Beheben) ──

    private IQueryable<ScoresheetScanArchive> ClubArchive(int clubGameId)
    {
        var now = DateTime.UtcNow;
        var scanIds = _db.ScoresheetScanArchives.Where(a => a.LeagueClubGameId == clubGameId && a.Page == 1 && a.View == 0 && a.ExpiresAt > now)
            .Select(a => a.ScoresheetScanId);
        return _db.ScoresheetScanArchives.Where(a => scanIds.Contains(a.ScoresheetScanId));
    }

    /// <summary>Die Hauptfotos (eine Zeile je Seite) des aufbewahrten Formulars — ohne die weiteren Fotos (0.756.0).</summary>
    private IQueryable<ScoresheetScanArchive> ClubPages(int clubGameId) => ClubArchive(clubGameId).Where(a => a.View == 0);

    /// <summary>Je Seite (Index = Seite − 1), wie viele weitere Fotos das aufbewahrte Formular hat (0.756.0).</summary>
    private async Task<List<int>> ClubViewCountsAsync(int clubGameId, int pageCount, CancellationToken ct)
    {
        var counts = await ClubArchive(clubGameId).Where(a => a.View > 0).GroupBy(a => a.Page)
            .Select(g => new { Page = g.Key, N = g.Count() }).ToListAsync(ct);
        return Enumerable.Range(1, pageCount).Select(p => counts.FirstOrDefault(c => c.Page == p)?.N ?? 0).ToList();
    }

    /// <summary>Gibt es zur Vereinspartie noch das aufbewahrte Formular (Foto + Lesung)?</summary>
    public Task<bool> HasClubSheetAsync(int clubGameId, CancellationToken ct = default) =>
        ClubArchive(clubGameId).AnyAsync(ct);

    /// <summary>
    /// Formular-Einträge + Stand je Halbzug der Vereinspartie aus dem Archiv — dieselbe Form wie bei einer eigenen Partie
    /// (<see cref="EditStateAsync"/>), damit dieselbe Korrektur-Oberfläche sie öffnet. <c>null</c> = nichts aufbewahrt.
    /// </summary>
    public async Task<ScoresheetEditStateDto?> ClubEditStateAsync(int clubGameId, CancellationToken ct = default)
    {
        var rows = await ClubPages(clubGameId).AsNoTracking()
            .Select(a => new { a.Page, a.ScoresheetScanId, a.NotationLanguage, a.TranscriptionJson, a.ResolutionJson }).ToListAsync(ct);
        var first = rows.FirstOrDefault(r => r.Page == 1);
        if (first == null) return null;
        var stored = Deserialize(first.ResolutionJson);
        var t = ScoresheetTranscription.Parse(first.TranscriptionJson);
        return new ScoresheetEditStateDto
        {
            ScanId = first.ScoresheetScanId,
            NotationLanguage = stored?.Language ?? first.NotationLanguage ?? "auto",
            Written = t?.Moves.Select(m => m.Written).ToList() ?? new(),
            Boxes = t?.NormalizedBoxes() ?? new(),
            PageCount = Math.Max(1, rows.Count),
            Pages = t?.EntryPages() ?? new(),
            Plies = stored?.Plies ?? new(),
            Unresolved = stored?.Unresolved ?? new(),
            UnresolvedFrom = stored?.UnresolvedFrom,
            ViewCounts = await ClubViewCountsAsync(clubGameId, Math.Max(1, rows.Count), ct),
        };
    }

    /// <summary>Foto einer Seite (ab 1) des aufbewahrten Formulars der Vereinspartie; <paramref name="view"/> ab 1 = ein weiteres
    /// Foto derselben Seite (0.756.0).</summary>
    public async Task<(byte[] Data, string ContentType, int PageCount)?> ClubPhotoAsync(int clubGameId, int page, CancellationToken ct = default,
        int view = 0)
    {
        var count = await ClubPages(clubGameId).CountAsync(ct);
        if (count == 0) return null;
        var p = await ClubArchive(clubGameId).AsNoTracking().Where(a => a.Page == Math.Max(1, page) && a.View == Math.Max(0, view))
            .Select(a => new { a.Photo, a.ContentType }).FirstOrDefaultAsync(ct);
        return p == null || p.Photo.Length == 0 ? null : (p.Photo, p.ContentType, count);
    }

    /// <summary>Wie <see cref="ResolveRestAsync"/>, über das aufbewahrte Formular der Vereinspartie.</summary>
    public async Task<ScoresheetResolveResultDto?> ResolveClubRestAsync(int clubGameId, IReadOnlyList<string> prefix, int writtenFrom,
        CancellationToken ct = default)
    {
        CheckPrefix(prefix);
        var a = await ClubPages(clubGameId).AsNoTracking().Where(x => x.Page == 1)
            .Select(x => new { x.NotationLanguage, x.TranscriptionJson, x.ResolutionJson }).FirstOrDefaultAsync(ct);
        var t = ScoresheetTranscription.Parse(a?.TranscriptionJson);
        if (a == null || t == null) return null;
        var scanned = t.Scanned();
        var language = Deserialize(a.ResolutionJson)?.Language ?? a.NotationLanguage;
        var r = ScoresheetResolver.Resolve(scanned, new ScoresheetResolver.Options(ScoresheetNotation.Find(language)),
            SavedGameService.LegalSans(prefix), Math.Clamp(writtenFrom, 0, scanned.Count));
        return new ScoresheetResolveResultDto { Plies = r.Plies, Unresolved = r.Unresolved, UnresolvedFrom = r.StuckAt };
    }

    /// <summary>Nach einer Korrektur den Stand je Halbzug und die PGN im Archiv nachziehen (die Lesung selbst bleibt).</summary>
    public async Task SaveClubEditStateAsync(int clubGameId, List<ScoresheetPly>? plies, string finalPgn, CancellationToken ct = default)
    {
        var a = await ClubPages(clubGameId).Where(x => x.Page == 1).FirstOrDefaultAsync(ct);
        if (a == null) return;
        a.FinalPgn = finalPgn;
        if (plies != null)
        {
            var stored = Deserialize(a.ResolutionJson) ?? new StoredResolution { Language = a.NotationLanguage };
            stored.Plies = plies.Take(600).ToList();
            a.ResolutionJson = JsonSerializer.Serialize(stored, Json);
        }
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Nach dem Speichern der Korrekturseite: deren Stand je Halbzug ablegen (Anzeige-Zustand).</summary>
    public async Task SaveEditStateAsync(int userId, int gameId, List<ScoresheetPly> plies)
    {
        var scan = await _db.ScoresheetScans.FirstOrDefaultAsync(s => s.SavedGameId == gameId && s.UserId == userId);
        if (scan == null) return;
        var stored = Deserialize(scan.ResolutionJson) ?? new StoredResolution { Language = scan.NotationLanguage };
        stored.Plies = plies.Take(600).ToList();
        // Was nach dem letzten gespeicherten Zug noch offen ist, bestimmt die Seite nicht mehr — das Speichern
        // übernimmt nur legale Züge; offene Einträge dahinter bleiben stehen, wie sie waren.
        scan.ResolutionJson = JsonSerializer.Serialize(stored, Json);
        await _db.SaveChangesAsync();
    }
}
