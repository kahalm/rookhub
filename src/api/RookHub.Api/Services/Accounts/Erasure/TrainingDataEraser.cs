using Microsoft.EntityFrameworkCore;

namespace RookHub.Api.Services.Accounts.Erasure;

/// <summary>Kontolöschung, Bereich Training: Fehler-Fortschritt, Markierungen, Aufgabenblätter, Aktivitäten.</summary>
internal static class TrainingDataEraser
{
    /// <summary>Fehler-Fortschritt, markierte Stellungen, Aufgabenblätter.</summary>
    internal static async Task MarksAndWorksheetsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Aufgabenblätter: Überschriften und Begleittexte sind FREITEXT des Nutzers (und oft
        // über seine Schüler geschrieben) — die gehen mit; die Aufgaben cascaden am Blatt.
        db.GameMistakeProgresses.RemoveRange(await db.GameMistakeProgresses.Where(m => m.UserId == userId).ToListAsync());
        db.MarkedPositions.RemoveRange(await db.MarkedPositions.Where(m => m.UserId == userId).ToListAsync());
        db.Worksheets.RemoveRange(await db.Worksheets.Where(w => w.UserId == userId).ToListAsync());
    }

    /// <summary>Eigene Aktivitäts-Vorlagen und laufende Timer.</summary>
    internal static async Task ActivitiesAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Eigene Aktivitäts-Vorlagen und laufende Timer: Freitext-Bezeichnungen = PII.
        db.ActivityPresets.RemoveRange(await db.ActivityPresets.Where(x => x.UserId == userId).ToListAsync());
        db.ActivityTimers.RemoveRange(await db.ActivityTimers.Where(x => x.UserId == userId).ToListAsync());
    }

    /// <summary>Manuelle Aktivitäten bleiben, ihre Notiz wird geleert.</summary>
    internal static async Task ManualNotesAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        // Manuelle Aktivitäten bleiben als (anonyme) Trainingsstatistik, aber die Freitext-Notiz (PII) wird geleert.
        var manualWithNote = await db.ManualActivities.Where(a => a.UserId == userId && a.Note != null).ToListAsync();
        foreach (var a in manualWithNote) a.Note = null;
    }
}
