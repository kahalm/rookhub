using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Kündigt die Freigabe terminierter Serien-Ausgaben an den privaten Verteiler an (Phase 3b), rein
/// IN-APP (Navbar-Glocke) — Mitglieder zur öffentlichen Freigabe, als Tester markierte Mitglieder schon
/// zum früheren Tester-Termin. Idempotent über die Marker
/// <see cref="CalcEdition.TesterAnnouncedAt"/>/<see cref="CalcEdition.PublishAnnouncedAt"/>: jede Ausgabe
/// löst je Kanal höchstens EINE Runde aus. Läuft im Hintergrund (<see cref="CalcSeriesAnnounceScheduler"/>).
///
/// <para>Bewusst KEIN Mail-Kanal: es gibt (noch) kein Mail-Opt-out/Präferenz-Modell — eine Rundmail an
/// den Verteiler wäre unerbetenes Bulk-Mailing. Sobald ein Opt-out existiert, kann Mail hier andocken.</para>
/// </summary>
public class CalcSeriesAnnounceService
{
    private readonly AppDbContext _db;
    private readonly NotificationService _notifications;

    public CalcSeriesAnnounceService(AppDbContext db, NotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    /// <summary>Ein Durchlauf: fällige Ausgaben finden, Empfänger benachrichtigen, Marker setzen.
    /// Gibt die Zahl der ausgelösten Ankündigungs-Runden zurück (Tester- und/oder Öffentlich-Runde).</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;

        // Ausgaben mit mindestens einem offenen Ereignis: Tester-Vorschau erreicht (und noch nicht
        // öffentlich) ODER öffentliche Freigabe erreicht — je noch nicht angekündigt.
        var due = await _db.CalcEditions
            .Where(e => (e.TesterAnnouncedAt == null && e.TesterPreviewAt != null && e.TesterPreviewAt <= now && e.PublishAt > now)
                     || (e.PublishAnnouncedAt == null && e.PublishAt <= now))
            .ToListAsync(ct);
        if (due.Count == 0) return 0;

        var announced = 0;
        foreach (var e in due)
        {
            var bookName = await _db.Books.Where(b => b.Id == e.BookId).Select(b => b.DisplayName).FirstOrDefaultAsync(ct) ?? string.Empty;
            var data = new Dictionary<string, string> { ["book"] = bookName, ["chapter"] = e.Chapter };
            var link = $"/courses/{e.BookId}";

            // Tester-Vorschau (nur solange noch nicht öffentlich): als Tester markierte Mitglieder.
            // WER benachrichtigt wurde, wird auf der Ausgabe festgehalten (TesterAnnouncedUserIds) —
            // die öffentliche Runde schließt GENAU diese aus, unabhängig vom später ggf. geänderten
            // IsTester-Flag oder neu hinzugekommenen Mitgliedern.
            if (e.TesterAnnouncedAt == null && e.TesterPreviewAt is DateTime tp && tp <= now && e.PublishAt > now)
            {
                var testers = await _db.CalcSeriesMembers.Where(m => m.BookId == e.BookId && m.IsTester)
                    .Select(m => m.UserId).ToListAsync(ct);
                e.TesterAnnouncedAt = now;   // Marker VOR dem Versand: CreateManyAsync speichert beides im selben Kontext atomar.
                e.TesterAnnouncedUserIds = string.Join(",", testers);
                await _notifications.CreateManyAsync(testers, NotificationType.CalcSeriesEditionReleased, data, link);
                announced++;
            }

            // Öffentliche Freigabe: alle Mitglieder — außer den bereits in der Tester-Runde informierten
            // (exakt über die gespeicherte Empfängerliste, nicht über das veränderliche IsTester-Flag).
            if (e.PublishAnnouncedAt == null && e.PublishAt <= now)
            {
                var alreadyNotified = ParseUserIds(e.TesterAnnouncedUserIds);
                var members = await _db.CalcSeriesMembers.Where(m => m.BookId == e.BookId)
                    .Select(m => m.UserId).ToListAsync(ct);
                var recipients = members.Where(id => !alreadyNotified.Contains(id)).ToList();
                e.PublishAnnouncedAt = now;
                await _notifications.CreateManyAsync(recipients, NotificationType.CalcSeriesEditionReleased, data, link);
                announced++;
            }
        }

        // Marker sichern (CreateManyAsync speichert bei LEEREN Empfängern nicht — dann persistiert erst dieser Save).
        await _db.SaveChangesAsync(ct);
        return announced;
    }

    /// <summary>CSV der in der Tester-Runde benachrichtigten UserIds → Menge. Null/leer → leere Menge.</summary>
    private static HashSet<int> ParseUserIds(string? csv)
    {
        var set = new HashSet<int>();
        if (string.IsNullOrEmpty(csv)) return set;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (int.TryParse(part, out var id)) set.Add(id);
        return set;
    }
}
