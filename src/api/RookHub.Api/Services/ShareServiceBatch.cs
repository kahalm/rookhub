using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;

namespace RookHub.Api.Services;

/// <summary>Ein beim Teilen übersprungener Empfänger samt Grund (self/not_found/not_friends/duplicate).</summary>
public sealed record ShareSkip(int UserId, string Reason);

/// <summary>Ergebnis eines Teilen-Laufs — quellenneutral (Kurs, Repertoire, …); die Services mappen
/// es auf ihr jeweiliges Result-DTO, damit die HTTP-Verträge unverändert bleiben.</summary>
public sealed class ShareBatchOutcome
{
    public int Shared { get; set; }
    public List<ShareSkip> Skipped { get; } = new();
}

/// <summary>
/// Gemeinsame Mechanik für „mit ausgewählten Personen teilen" (Kurs UND Repertoire nutzen sie).
/// FALLE: der Ablauf lag vorher zweimal fast zeilengleich in CourseService/RepertoireService — mit
/// der Folge, dass derselbe Rekursionsbug an ZWEI Stellen gefixt werden musste (v0.317.1) und jede
/// künftige Regeländerung (neuer Skip-Grund, andere Benachrichtigung) still auseinanderlaufen konnte.
/// Alles Quellenspezifische kommt über die Delegates herein; Skip-Gründe, Freundes-Prüfung und der
/// Umgang mit dem Unique-Index-Race leben genau hier — einmal.
/// </summary>
public static class ShareServiceBatch
{
    /// <param name="loadAlreadySharedAsync">Empfänger, mit denen bereits geteilt ist. Wird nach einem
    /// Race ein ZWEITES Mal gefragt — die Antwort muss also frisch aus der DB kommen.</param>
    /// <param name="addShare">Legt die Share-Zeile für einen Empfänger im ChangeTracker an.</param>
    /// <param name="onSharedAsync">Nachlauf nach erfolgreichem Speichern (Benachrichtigungen,
    /// Cache-Invalidierung); bekommt die Liste der NEU beschenkten Empfänger.</param>
    public static async Task<ShareBatchOutcome> ShareAsync(
        AppDbContext db,
        FriendService friends,
        int ownerId,
        List<int> recipientUserIds,
        bool isAdmin,
        Func<List<int>, Task<HashSet<int>>> loadAlreadySharedAsync,
        Action<int> addShare,
        Func<List<int>, Task> onSharedAsync)
    {
        var distinct = recipientUserIds.Distinct().ToList();
        if (distinct.Count == 0) return new ShareBatchOutcome();

        var existing = (await db.AppUsers.Where(u => distinct.Contains(u.Id)).Select(u => u.Id).ToListAsync()).ToHashSet();
        var friendIds = await friends.GetAcceptedFriendIdsAsync(ownerId, distinct);

        // Höchstens EIN zweiter Anlauf, und nur nach einem echten Unique-Index-Race. FALLE: der
        // frühere Catch-Zweig rief die Methode rekursiv erneut auf — bei einer dauerhaften
        // DbUpdateException lief das unbegrenzt weiter (StackOverflow ⇒ Prozess-Abbruch, NICHT
        // abfangbar). Deshalb hier eine Schleife mit Versuchszähler statt Rekursion.
        for (var attempt = 0; ; attempt++)
        {
            var outcome = new ShareBatchOutcome();
            var alreadyShared = await loadAlreadySharedAsync(distinct);
            var toNotify = new List<int>();
            foreach (var rid in distinct)
            {
                if (rid == ownerId) { outcome.Skipped.Add(new ShareSkip(rid, "self")); continue; }
                if (!existing.Contains(rid)) { outcome.Skipped.Add(new ShareSkip(rid, "not_found")); continue; }
                // Admins dürfen auch an Nicht-Freunde teilen; normale Nutzer nur an bestätigte Freunde.
                if (!isAdmin && !friendIds.Contains(rid)) { outcome.Skipped.Add(new ShareSkip(rid, "not_friends")); continue; }
                if (alreadyShared.Contains(rid)) { outcome.Skipped.Add(new ShareSkip(rid, "duplicate")); continue; }

                addShare(rid);
                toNotify.Add(rid);
                outcome.Shared++;
            }

            if (outcome.Shared == 0) return outcome;

            try { await db.SaveChangesAsync(); }
            catch (DbUpdateException ex) when (attempt == 0 && AuthService.IsUniqueViolation(ex))
            {
                // Race: dasselbe (Objekt, Empfänger) parallel geteilt → Unique-Index. Idempotent
                // behandeln: der zweite Durchlauf findet den Empfänger in `alreadyShared` wieder.
                db.ChangeTracker.Clear();
                continue;
            }

            await onSharedAsync(toNotify);
            return outcome;
        }
    }
}
