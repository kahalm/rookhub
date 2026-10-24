using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Konto: Anzeige-Zustand, Zugänge (Gruppen, Tokens, Drittanbieter-Schlüssel, Engines), Rollen.</summary>
internal static class AccountDataEraser
{
    /// <summary>Anzeige-Zustand je Seite.</summary>
    internal static async Task ViewStatesAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        db.UserViewStates.RemoveRange(await db.UserViewStates.Where(v => v.UserId == userId).ToListAsync());
    }

    /// <summary>Gruppen, API-Tokens, Chessable-/Lichess-Schlüssel, Engines, Reset- und Übergabe-Tokens.</summary>
    internal static async Task AccessAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        db.UserGroups.RemoveRange(await db.UserGroups.Where(g => g.UserId == userId).ToListAsync());
        // API-Tokens (chess.com-Extension u. a.) widerrufen — ein gelöschtes Konto behält keinen Zugang.
        db.UserApiTokens.RemoveRange(await db.UserApiTokens.Where(t => t.UserId == userId).ToListAsync());
        // Live-Drittanbieter-Credential + Einmal-Tokens: dürfen nach der Löschung nicht fortbestehen
        // (der Chessable-Bearer bliebe sonst mit dem Server-Key entschlüsselbar).
        db.ChessableCredentials.RemoveRange(await db.ChessableCredentials.Where(c => c.UserId == userId).ToListAsync());
        // Ebenso der Lichess-OAuth-Token der External-Engine-Anbindung: Die Konto-Löschung
        // anonymisiert die AppUser-Zeile IN PLACE, der Cascade-FK feuert hier also NICHT — ohne
        // diese Zeile bliebe ein fremder, weiterhin gültiger Lichess-Token dauerhaft in der DB.
        db.LichessEngineCredentials.RemoveRange(await db.LichessEngineCredentials.Where(c => c.UserId == userId).ToListAsync());
        // Engines „RookHub direkt": ihr Selector nimmt sonst weiter Arbeit an (der Provider pollt ja weiter),
        // und die Zeile trüge den Namen der Maschine eines gelöschten Kontos.
        db.ExternalEngineRegistrations.RemoveRange(await db.ExternalEngineRegistrations.Where(r => r.UserId == userId).ToListAsync());
        // Gemeldete Zeitplaene der Engine-Clients (0.679.0) — haengen am Konto wie die Registrierungen.
        db.EngineClientSchedules.RemoveRange(await db.EngineClientSchedules.Where(s => s.UserId == userId).ToListAsync());
        db.PasswordResetTokens.RemoveRange(await db.PasswordResetTokens.Where(t => t.UserId == userId).ToListAsync());
        // Ein noch offener Uebergabe-Code wuerde sonst nach der Loeschung noch Sekunden lang
        // eine Anmeldung erzeugen (der Einloeser prueft zwar DeletedAt — die Zeile hat hier aber
        // ohnehin nichts mehr verloren).
        db.AuthHandoffTokens.RemoveRange(await db.AuthHandoffTokens.Where(t => t.UserId == userId).ToListAsync());
    }

    /// <summary>Rollen/Rechte.</summary>
    internal static async Task RolesAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Rollen/Rechte: ein gelöschtes Konto behält keine Berechtigung (der Login ist gesperrt, aber
        // die Zeile wäre bei einer Wiederverwendung der Id ein stiller Rechte-Übertrag).
        db.UserRoles.RemoveRange(await db.UserRoles.Where(x => x.UserId == userId).ToListAsync());
    }
}
