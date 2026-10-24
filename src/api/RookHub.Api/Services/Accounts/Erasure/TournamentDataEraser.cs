using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Turniere: Abos, Favoriten, Einstellungen, Suchprofile, verfolgte Spieler, Runden-Monitor.</summary>
internal static class TournamentDataEraser
{
    /// <summary>Abos, Favoriten, Einstellungen, Suchprofile, Verzeichnis-Ausblendungen, verfolgte Spieler.</summary>
    internal static async Task FollowingAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        db.TournamentSubscriptions.RemoveRange(await db.TournamentSubscriptions.Where(s => s.UserId == userId).ToListAsync());
        db.TournamentFavorites.RemoveRange(await db.TournamentFavorites.Where(f => f.UserId == userId).ToListAsync());
        db.TournamentFavoriteDismissals.RemoveRange(await db.TournamentFavoriteDismissals.Where(d => d.UserId == userId).ToListAsync());
        db.TournamentUserSettings.RemoveRange(await db.TournamentUserSettings.Where(s => s.UserId == userId).ToListAsync());
        // Turnier-Suchprofile: sie tragen die Koordinaten des Wohnorts und benannte Zweitorte
        // ("Ferienhaus Kaernten") — das ist persoenlicher als jedes Turnier-Abo.
        db.TournamentSearchProfiles.RemoveRange(await db.TournamentSearchProfiles.Where(p => p.UserId == userId).ToListAsync());
        db.TournamentDirectoryIgnores.RemoveRange(await db.TournamentDirectoryIgnores.Where(i => i.UserId == userId).ToListAsync());
        // Wen jemand verfolgt hat, ist eine Auskunft ueber ihn — und ueber die Verfolgten. Der
        // geholte Verlauf bleibt (der gehoert dem Spieler, nicht dem Konto), die Liste geht.
        db.TrackedPlayers.RemoveRange(await db.TrackedPlayers.Where(t => t.UserId == userId).ToListAsync());
    }

    /// <summary>Turnier-Runden-Monitor.</summary>
    internal static async Task MonitorsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Turnier-Runden-Monitor: personenbezogenes Beobachten eines Turniers (löst Benachrichtigungen
        // aus) — nach der Löschung gibt es niemanden mehr, der benachrichtigt werden möchte.
        db.TournamentMonitors.RemoveRange(await db.TournamentMonitors.Where(x => x.UserId == userId).ToListAsync());
    }
}
