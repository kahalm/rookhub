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
/// Einlesungen für die Vereins-Datenbank (LeagueHub, Zerlegung Z6 aus <see cref="ScoresheetScanService"/>): wem sie
/// gehören (<see cref="ScoresheetScanService.ScanActor"/>), Listen, Stand zur Korrektur, Fotos, Abschluss samt Archiv und
/// das Zusammenführen zweier Einlesungen desselben Formulars.
/// </summary>
internal sealed class LeagueScoresheetScans
{
    private readonly AppDbContext _db;
    private readonly ILogger<ScoresheetScanService> _logger;

    internal LeagueScoresheetScans(AppDbContext db, ILogger<ScoresheetScanService> logger)
    {
        _db = db;
        _logger = logger;
    }

    internal IQueryable<ScoresheetScan> LeagueOwned(IQueryable<ScoresheetScan> q, ScoresheetScanService.ScanActor a)
    {
        q = q.Where(s => s.Purpose == ScoresheetScan.PurposeLeague);
        if (a.ClubId is int club) q = q.Where(s => s.ClubId == club);
        if (a.Manager) return q;
        if (a.UserId is int uid) return q.Where(s => s.UserId == uid);
        var key = a.Key ?? "";
        return q.Where(s => s.UserId == null && s.AccessKey == key && key != "");
    }

    /// <summary>Die offenen Liga-Einlesungen (neueste zuerst) — eines Kontos bzw. OHNE Konto die, deren Schlüssel der
    /// Browser mitbringt. Verworfene/übernommene tragen kein Foto mehr und fehlen.</summary>
    public async Task<List<ScoresheetScanDto>> LeagueScansAsync(int userId, int? clubId = null) =>
        await WithNamesAsync((await LeagueOwned(ScanHeads(_db), ScoresheetScanService.ScanActor.User(userId) with { ClubId = clubId }).Where(s => s.FileName != DiscardedMark)
            .OrderByDescending(s => s.CreatedAt).Take(10).ToListAsync()).Select(ToDto).ToList());

    /// <summary>
    /// Namen der gelesenen Partie in die Liste (0.659.3, gemeldet 2026-10-05: „Deine Formulare" zeigte „? – ?", obwohl die
    /// Prüfseite die Namen kannte). <see cref="ScanHeads"/> lässt die Antwort des Modells absichtlich weg; für die fertigen
    /// Einlesungen der Liste werden nur deren Weiß/Schwarz nachgelesen.
    /// </summary>
    private async Task<List<ScoresheetScanDto>> WithNamesAsync(List<ScoresheetScanDto> dtos, CancellationToken ct = default)
    {
        var ids = dtos.Where(d => d.Status == "done" && d.White == null && d.Black == null).Select(d => d.Id).ToList();
        if (ids.Count == 0) return dtos;
        var json = await _db.ScoresheetScans.AsNoTracking().Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.TranscriptionJson }).ToDictionaryAsync(s => s.Id, s => s.TranscriptionJson, ct);
        foreach (var d in dtos)
        {
            if (!json.TryGetValue(d.Id, out var j) || ScoresheetTranscription.Parse(j) is not { } t) continue;
            d.White = Blank(t.White);
            d.Black = Blank(t.Black);
        }
        return dtos;
    }

    /// <summary>Alle offenen Liga-Einlesungen (auch ohne Konto über einen Teilen-Link) — für die Verwalter, jüngste zuerst.</summary>
    public async Task<List<LeagueOpenScanDto>> LeagueOpenScansAsync(int viewerId, int? clubId = null, CancellationToken ct = default)
    {
        var rows = (await LeagueOwned(ScanHeads(_db), ScoresheetScanService.ScanActor.ManagerOf(viewerId) with { ClubId = clubId }).Where(s => s.FileName != DiscardedMark)
                .OrderByDescending(s => s.CreatedAt).Take(50).ToListAsync(ct))
            .Select(s => new LeagueOpenScanDto { Scan = ToDto(s), ViaShareLink = s.UserId == null, Mine = s.UserId == viewerId }).ToList();
        await WithNamesAsync(rows.Select(r => r.Scan).ToList(), ct);
        return rows;
    }

    public async Task<List<(string Key, ScoresheetScanDto Scan)>> LeagueScansByKeysAsync(IEnumerable<string> keys, int? clubId = null)
    {
        var list = keys.Where(k => !string.IsNullOrWhiteSpace(k)).Distinct().Take(20).ToList();
        var scans = await ScanHeads(_db).Where(s => s.UserId == null && s.Purpose == ScoresheetScan.PurposeLeague
                && (clubId == null || s.ClubId == clubId)
                && s.AccessKey != null && list.Contains(s.AccessKey) && s.FileName != DiscardedMark)
            .OrderByDescending(s => s.CreatedAt).ToListAsync();
        var pairs = scans.Select(s => (s.AccessKey!, ToDto(s))).ToList();
        await WithNamesAsync(pairs.Select(p => p.Item2).ToList());
        return pairs;
    }


    /// <summary>Stand einer Liga-Einlesung für die Korrektur in LeagueHub: Formular-Einträge, Züge, Kopfdaten wie gelesen
    /// und welche Seite der Nutzer vermutlich spielte. <c>null</c> = fremd, unbekannt oder verworfen.</summary>
    public async Task<LeagueScanStateDto?> LeagueScanStateAsync(ScoresheetScanService.ScanActor actor, int? scanId, CancellationToken ct = default)
    {
        var q = LeagueOwned(ScanHeads(_db), actor).Where(s => s.FileName != DiscardedMark);
        if (scanId is int id) q = q.Where(s => s.Id == id);
        var scan = await q.FirstOrDefaultAsync(ct);
        if (scan == null) return null;
        var dto = new LeagueScanStateDto { Scan = ToDto(scan), PageCount = Math.Max(1, scan.PageCount) };
        dto.ViewCounts = await ViewCountsAsync(_db, scan.Id, dto.PageCount, ct);
        if (scan.Status != ScoresheetScanStatus.Done) return dto;
        var json = await _db.ScoresheetScans.AsNoTracking().Where(s => s.Id == scan.Id)
            .Select(s => s.TranscriptionJson).FirstOrDefaultAsync(ct);
        var stored = Deserialize(scan.ResolutionJson);
        var t = ScoresheetTranscription.Parse(json);
        dto.NotationLanguage = stored?.Language ?? scan.NotationLanguage;
        dto.Written = t?.Moves.Select(m => m.Written).ToList() ?? new();
        dto.Boxes = t?.NormalizedBoxes() ?? new();
        dto.Pages = t?.EntryPages() ?? new();
        dto.PageCount = Math.Max(1, scan.PageCount);
        dto.Plies = stored?.Plies ?? new();
        dto.Unresolved = stored?.Unresolved ?? new();
        dto.UnresolvedFrom = stored?.UnresolvedFrom;
        dto.White = Blank(t?.White);
        dto.Black = Blank(t?.Black);
        dto.Event = Blank(t?.Event);
        dto.Date = Blank(t?.DateIso) ?? Blank(t?.Date);
        dto.Result = t?.Result;
        // Beim Hochladen gewählt schlägt geraten (wie beim Einlesen in „Meine Partien").
        dto.OwnerSide = scan.OwnerSide is "white" or "black" ? scan.OwnerSide
            : t == null || scan.UserId is not int uid ? null : await ScoresheetOwnerSide.GuessOwnerSideAsync(_db, uid, t, ct);
        return dto;
    }

    /// <summary>Das Foto einer Liga-Einlesung; <paramref name="view"/> ab 1 = ein weiteres Foto derselben Seite (0.736.0).</summary>
    public async Task<(byte[] Data, string ContentType)?> LeagueScanPhotoAsync(ScoresheetScanService.ScanActor actor, int? scanId, int page = 1, int view = 0)
    {
        var q = LeagueOwned(_db.ScoresheetScans.AsNoTracking(), actor);
        if (scanId is int id) q = q.Where(s => s.Id == id);
        if (view > 0)
            return await q.Select(s => (int?)s.Id).FirstOrDefaultAsync() is int owner ? await ViewPhotoAsync(_db, owner, page, view) : null;
        if (page > 1)
        {
            // Seite 2+ (0.690.1): erst die Einlesung über die Eigentumsregel finden, dann ihr Blatt
            var owned = await q.Select(s => (int?)s.Id).FirstOrDefaultAsync();
            if (owned is not int sid) return null;
            var extra = await _db.ScoresheetScanPages.AsNoTracking().Where(x => x.ScoresheetScanId == sid && x.Page == page)
                .Select(x => new { x.Photo, x.ContentType }).FirstOrDefaultAsync();
            return extra == null || extra.Photo.Length == 0 ? null : (extra.Photo, extra.ContentType);
        }
        var p = await q.Select(s => new { s.Photo, s.ContentType }).FirstOrDefaultAsync();
        return p == null || p.Photo.Length == 0 ? null : (p.Photo, p.ContentType);
    }

    /// <summary>Wie <see cref="ScoresheetCorrection.ResolveRestAsync"/>, für eine Liga-Einlesung (ohne gespeicherte Partie).</summary>
    public async Task<ScoresheetResolveResultDto?> ResolveLeagueRestAsync(ScoresheetScanService.ScanActor actor, int? scanId, IReadOnlyList<string> prefix,
        int writtenFrom)
    {
        ScoresheetCorrection.CheckPrefix(prefix);
        var q = LeagueOwned(_db.ScoresheetScans.AsNoTracking(), actor);
        if (scanId is int id) q = q.Where(s => s.Id == id);
        var scan = await q.Select(s => new { s.NotationLanguage, s.TranscriptionJson, s.ResolutionJson }).FirstOrDefaultAsync();
        var t = ScoresheetTranscription.Parse(scan?.TranscriptionJson);
        if (scan == null || t == null) return null;
        var scanned = t.Scanned();
        var language = Deserialize(scan.ResolutionJson)?.Language ?? scan.NotationLanguage;
        var r = ScoresheetResolver.Resolve(scanned, new ScoresheetResolver.Options(ScoresheetNotation.Find(language)),
            SavedGameService.LegalSans(prefix), Math.Clamp(writtenFrom, 0, scanned.Count));
        return new ScoresheetResolveResultDto { Plies = r.Plies, Unresolved = r.Unresolved, UnresolvedFrom = r.StuckAt };
    }

    /// <summary>
    /// Liga-Einlesung abschließen (übernommen ODER verworfen): Foto, Antwort des Modells und Stand gehen, die Zeile bleibt
    /// mit Zeitpunkt und Kosten fürs Tageskontingent und die Kostenbremse stehen — wie beim Löschen einer Partie.
    /// Danach verbindet nichts mehr die Einlesung mit der Partie, die daraus wurde (auch der Schlüssel geht).
    /// <c>false</c> = fremd/unbekannt.
    /// </summary>
    public async Task<bool> CloseLeagueScanAsync(ScoresheetScanService.ScanActor actor, int? scanId, string? finalPgn = null, int? clubGameId = null)
    {
        var q = LeagueOwned(_db.ScoresheetScans, actor);
        if (scanId is int id) q = q.Where(s => s.Id == id);
        var key = await q.Select(s => new { s.Id, s.UserId, s.SavedGameId }).FirstOrDefaultAsync();
        if (key == null) return false;
        await ArchiveAsync(key.Id, finalPgn, clubGameId);
        DetachWithoutLoading(_db, new[] { (key.Id, key.UserId, key.SavedGameId) });
        RemovePagesWithoutLoading(_db, await PageKeysAsync(_db, new[] { key.Id }));
        RemoveViewsWithoutLoading(_db, await ViewKeysAsync(_db, new[] { key.Id }));
        var scan = _db.ScoresheetScans.Local.First(s => s.Id == key.Id);
        scan.FileName = DiscardedMark;
        scan.AccessKey = null;
        _db.Entry(scan).Property(nameof(ScoresheetScan.AccessKey)).IsModified = true;
        await _db.SaveChangesAsync();
        return true;
    }


    /// <summary>
    /// Foto(s), Antwort des Modells und Auflösung einer Liga-Einlesung ins Archiv kopieren, bevor
    /// <see cref="CloseLeagueScanAsync"/> sie an der Einlesung leert (0.655.0) — einmal je Einlesung (ein zweiter Abschluss
    /// findet nichts mehr). Räumt dabei Abgelaufenes weg. Speichert nicht selbst.
    /// </summary>
    private async Task ArchiveAsync(int scanId, string? finalPgn, int? clubGameId = null)
    {
        var now = DateTime.UtcNow;
        await PurgeExpiredArchiveAsync(now);
        if (await _db.ScoresheetScanArchives.AnyAsync(a => a.ScoresheetScanId == scanId)) return;
        var s = await _db.ScoresheetScans.AsNoTracking().Where(x => x.Id == scanId)
            .Select(x => new { x.Photo, x.ContentType, x.TranscriptionJson, x.ResolutionJson, x.Model, x.NotationLanguage })
            .FirstOrDefaultAsync();
        if (s == null || (s.Photo.Length == 0 && s.TranscriptionJson == null)) return;
        var outcome = finalPgn != null ? "saved" : "discarded";
        _db.ScoresheetScanArchives.Add(new ScoresheetScanArchive
        {
            ScoresheetScanId = scanId, Page = 1, Photo = s.Photo, ContentType = s.ContentType,
            TranscriptionJson = s.TranscriptionJson, ResolutionJson = s.ResolutionJson, FinalPgn = finalPgn, Outcome = outcome,
            LeagueClubGameId = finalPgn != null ? clubGameId : null,
            Model = s.Model, NotationLanguage = s.NotationLanguage, ArchivedAt = now, ExpiresAt = now + ScoresheetScanService.ArchiveRetention,
        });
        var pages = await _db.ScoresheetScanPages.AsNoTracking().Where(p => p.ScoresheetScanId == scanId)
            .Select(p => new { p.Page, p.Photo, p.ContentType }).ToListAsync();
        foreach (var p in pages)
            _db.ScoresheetScanArchives.Add(new ScoresheetScanArchive
            {
                ScoresheetScanId = scanId, Page = p.Page, Photo = p.Photo, ContentType = p.ContentType, Outcome = outcome,
                Model = s.Model, NotationLanguage = s.NotationLanguage, ArchivedAt = now, ExpiresAt = now + ScoresheetScanService.ArchiveRetention,
            });
        // Die weiteren Fotos je Seite (0.756.0) — z. B. das zweite Exemplar derselben Partie; „Korrigieren" zeigt sie wieder.
        var views = await _db.ScoresheetScanViews.AsNoTracking().Where(v => v.ScoresheetScanId == scanId)
            .Select(v => new { v.Page, v.View, v.Photo, v.ContentType }).ToListAsync();
        foreach (var v in views)
            _db.ScoresheetScanArchives.Add(new ScoresheetScanArchive
            {
                ScoresheetScanId = scanId, Page = v.Page, View = v.View, Photo = v.Photo, ContentType = v.ContentType, Outcome = outcome,
                Model = s.Model, NotationLanguage = s.NotationLanguage, ArchivedAt = now, ExpiresAt = now + ScoresheetScanService.ArchiveRetention,
            });
    }

    /// <summary>Abgelaufene Archiv-Einträge löschen (ohne die Fotos zu laden).</summary>
    private async Task PurgeExpiredArchiveAsync(DateTime now)
    {
        var expired = await _db.ScoresheetScanArchives.Where(a => a.ExpiresAt < now).Select(a => a.Id).Take(500).ToListAsync();
        foreach (var id in expired)
        {
            var stub = _db.ScoresheetScanArchives.Local.FirstOrDefault(a => a.Id == id) ?? new ScoresheetScanArchive { Id = id };
            if (_db.Entry(stub).State == EntityState.Detached) _db.ScoresheetScanArchives.Attach(stub);
            _db.ScoresheetScanArchives.Remove(stub);
        }
    }

    /// <summary>
    /// Zwei Liga-Einlesungen DESSELBEN Formulars zusammenführen (0.736.0, Wunsch 2026-10-10: „ich habe 2 Bilder bereits
    /// analysiert, will dort die Funktionen zusammenführen"): die Fotos von <paramref name="otherId"/> werden weitere Fotos der
    /// passenden Seiten von <paramref name="targetId"/> (Seite p → p, über die Seitenzahl hinaus die letzte), die andere
    /// Einlesung wird wie verworfen geschlossen (Archiv, kein Foto mehr), und die Ziel-Einlesung wartet wieder — der Leser
    /// liest sie mit allen Fotos neu. Gründe: <c>notFound</c> (fremd/unbekannt/geschlossen), <c>same</c>, <c>busy</c>
    /// (eine wird gerade gelesen), <c>tooManyViews</c> (über <see cref="ScoresheetScanService.MaxPhotosPerPage"/> je Seite).
    /// </summary>
    public async Task<string?> MergeLeagueScansAsync(ScoresheetScanService.ScanActor actor, int targetId, int otherId, CancellationToken ct = default)
    {
        if (targetId == otherId) return "same";
        var heads = await LeagueOwned(_db.ScoresheetScans.AsNoTracking(), actor)
            // Kein „Photo.Length > 0" hier: das übersetzt MariaDB nicht (Prod 0.736.0, 500) — eine
            // geschlossene Einlesung erkennt man am Dateinamen „∅".
            .Where(s => (s.Id == targetId || s.Id == otherId) && s.FileName != DiscardedMark)
            .Select(s => new { s.Id, s.Status, s.PageCount }).ToListAsync(ct);
        var target = heads.FirstOrDefault(h => h.Id == targetId);
        var other = heads.FirstOrDefault(h => h.Id == otherId);
        if (target == null || other == null) return "notFound";
        if (target.Status == ScoresheetScanStatus.Running || other.Status == ScoresheetScanStatus.Running) return "busy";

        var moved = new List<(int Page, byte[] Photo, string Type, string? Name)>();
        var main = await _db.ScoresheetScans.AsNoTracking().Where(s => s.Id == otherId)
            .Select(s => new { s.Photo, s.ContentType, s.FileName }).FirstAsync(ct);
        moved.Add((1, main.Photo, main.ContentType, main.FileName));
        moved.AddRange((await _db.ScoresheetScanPages.AsNoTracking().Where(p => p.ScoresheetScanId == otherId).OrderBy(p => p.Page)
            .Select(p => new { p.Page, p.Photo, p.ContentType, p.FileName }).ToListAsync(ct))
            .Select(p => (p.Page, p.Photo, p.ContentType, p.FileName)));
        moved.AddRange((await _db.ScoresheetScanViews.AsNoTracking().Where(v => v.ScoresheetScanId == otherId)
            .OrderBy(v => v.Page).ThenBy(v => v.View)
            .Select(v => new { v.Page, v.Photo, v.ContentType, v.FileName }).ToListAsync(ct))
            .Select(v => (v.Page, v.Photo, v.ContentType, v.FileName)));

        var pages = Math.Max(1, target.PageCount);
        var existing = await _db.ScoresheetScanViews.AsNoTracking().Where(v => v.ScoresheetScanId == targetId)
            .Select(v => new { v.Page, v.View }).ToListAsync(ct);
        var next = Enumerable.Range(1, pages).ToDictionary(p => p,
            p => existing.Where(e => e.Page == p).Select(e => e.View).DefaultIfEmpty(0).Max() + 1);
        var added = new List<ScoresheetScanView>();
        foreach (var m in moved.Where(m => m.Photo.Length > 0))
        {
            var page = Math.Clamp(m.Page, 1, pages);
            if (next[page] >= ScoresheetScanService.MaxPhotosPerPage) return "tooManyViews";
            added.Add(new ScoresheetScanView
            {
                ScoresheetScanId = targetId, Page = page, View = next[page]++, Photo = m.Photo, ContentType = m.Type, FileName = m.Name,
            });
        }
        _db.ScoresheetScanViews.AddRange(added);

        var scan = await _db.ScoresheetScans.FirstAsync(s => s.Id == targetId, ct);
        scan.Status = ScoresheetScanStatus.Pending;
        scan.StartedAt = null;
        scan.FinishedAt = null;
        scan.Error = null;
        // Neu eingereiht (0.737.2): die Uhr der Seite („wartet seit …") zählt ab hier, nicht ab dem ersten Hochladen (gemeldet:
        // „276:02"), und der Leser von außen sieht nur Einlesungen der letzten 7 Tage. Kontingent: die Zeile zählt weiter einmal.
        scan.CreatedAt = DateTime.UtcNow;
        // Die andere schließen wie „verworfen" (Archiv, Foto weg); speichert alles zusammen.
        await CloseLeagueScanAsync(actor, otherId);
        _logger.LogInformation("Formular-Einlesung {OtherId} in {TargetId} zusammengeführt ({Photos} Foto(s)) — wartet wieder",
            otherId, targetId, added.Count);
        return null;
    }
}
