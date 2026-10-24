using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Soziales: Freundschaften, Admin-Direktnachrichten, Push und Benachrichtigungen.</summary>
internal static class SocialDataEraser
{
    /// <summary>Freundschaften in beide Richtungen (FK Restrict).</summary>
    internal static async Task FriendshipsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        db.Friendships.RemoveRange(
            await db.Friendships.Where(f => f.RequesterId == userId || f.AddresseeId == userId).ToListAsync());
    }

    /// <summary>Admin-Direktnachrichten, Web-Push, Verteiler der Kalk-Serie, eigene Benachrichtigungen.</summary>
    internal static async Task MessagesAndNotificationsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Admin-Direktnachrichten: die Nachrichtentexte des Users sind PII und müssen mit dem Konto
        // verschwinden (beide Richtungen des Threads — der Kontext der Admin-Antworten identifiziert
        // den User genauso). Thread-Metadaten (Claim) hängen daran und gehen mit.
        db.AdminMessages.RemoveRange(await db.AdminMessages.Where(m => m.UserId == userId).ToListAsync());
        db.MessageThreads.RemoveRange(await db.MessageThreads.Where(t => t.UserId == userId).ToListAsync());
        // Web-Push: Endpunkt + Schlüssel des GERÄTS. Ohne diese Zeilen schickt der Server weiter
        // Benachrichtigungen an das Gerät eines gelöschten Kontos — der Verteiler der Kalkulations-Serie
        // sammelt die Nutzer-Id ja weiter ein, solange die Mitgliedschaft steht (deshalb auch die).
        db.UserPushSubscriptions.RemoveRange(await db.UserPushSubscriptions.Where(x => x.UserId == userId).ToListAsync());
        db.NotificationPushSettings.RemoveRange(await db.NotificationPushSettings.Where(x => x.UserId == userId).ToListAsync());
        db.CalcSeriesMembers.RemoveRange(await db.CalcSeriesMembers.Where(x => x.UserId == userId).ToListAsync());
        // In-App-Benachrichtigungen tragen Freitext-Daten (Kurs-/Absendernamen) → PII.
        db.Notifications.RemoveRange(await db.Notifications.Where(x => x.UserId == userId).ToListAsync());
    }
}
