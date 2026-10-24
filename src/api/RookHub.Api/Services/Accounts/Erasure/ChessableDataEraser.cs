using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Chessable: gemerkte Stellungen, Rohdaten, Themen und Import-Aufträge.</summary>
internal static class ChessableDataEraser
{
    /// <summary>Auf chessable.com gemerkte Stellungen.</summary>
    internal static async Task RememberedPositionsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Auf chessable.com gemerkte Stellungen (Kursname/Quell-URL) — persönlich, keine Statistik.
        db.RememberedPositions.RemoveRange(await db.RememberedPositions.Where(r => r.UserId == userId).ToListAsync());
    }

    /// <summary>getReview-Linien, Sitzungszüge, schwierige Züge — relational ohne Laden (sofort).</summary>
    internal static async Task RawDataAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Chessable-Rohdaten des Nutzers: getReview-Linien, Sitzungszüge und „schwierige Züge" sind
        // fremder Kursinhalt, an die Person gebunden — keine Statistik, die anonym weiterleben könnte.
        // Ohne sie zu laden: LONGTEXT-JSON bis zum Kontingent je Konto (Altbestand auch weit mehr), das
        // RemoveRange(ToListAsync()) holte alles in die API, nur um es zu löschen. Relational ein DELETE je
        // Tabelle — läuft sofort wie DeleteBookAsync oben, ein Wiederholungslauf findet danach nichts mehr;
        // InMemory (Tests) kennt kein ExecuteDelete.
        if (db.Database.IsRelational())
        {
            await db.ChessableReviewLines.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await db.ChessableSessionMoves.Where(x => x.UserId == userId).ExecuteDeleteAsync();
            await db.ChessableProblemMoves.Where(x => x.UserId == userId).ExecuteDeleteAsync();
        }
        else
        {
            db.ChessableReviewLines.RemoveRange(await db.ChessableReviewLines.Where(x => x.UserId == userId).ToListAsync());
            db.ChessableSessionMoves.RemoveRange(await db.ChessableSessionMoves.Where(x => x.UserId == userId).ToListAsync());
            db.ChessableProblemMoves.RemoveRange(await db.ChessableProblemMoves.Where(x => x.UserId == userId).ToListAsync());
        }
    }

    /// <summary>Themenzuordnung und Import-Aufträge.</summary>
    internal static async Task ThemesAndImportsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Chessable-Themenzuordnung + Import-Aufträge: benennen die Kurse der fremden Bibliothek.
        db.ChessableCourseThemes.RemoveRange(await db.ChessableCourseThemes.Where(x => x.UserId == userId).ToListAsync());
        db.ChessableImports.RemoveRange(await db.ChessableImports
            .Where(x => x.UserId == userId || x.BearerUserId == userId).ToListAsync());
    }
}
