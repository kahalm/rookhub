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
        db.MarkedPositionNoteRevisions.RemoveRange(await db.MarkedPositionNoteRevisions.Where(m => m.UserId == userId).ToListAsync());
        db.Worksheets.RemoveRange(await db.Worksheets.Where(w => w.UserId == userId).ToListAsync());
    }

    /// <summary>Trainerstunden (Trainingsvideos): Stunden samt Partien, Segmenten, Fragen, Notizen und der Übungsstand —
    /// Trainerkommentare und Rohtranskripte sind Gesprächsinhalt über den Nutzer. Kinder explizit (InMemory kaskadiert
    /// nicht); die Video-Dateien liegen außerhalb der DB im nur lesend eingehängten Ordner und bleiben dort.</summary>
    internal static async Task TrainingLessonsAsync(UserErasureContext ctx)
    {
        var (db, userId) = (ctx.Db, ctx.UserId);
        db.TrainingSegmentProgress.RemoveRange(await db.TrainingSegmentProgress.Where(p => p.UserId == userId).ToListAsync());
        var lessonIds = await db.TrainingLessons.Where(l => l.UserId == userId).Select(l => l.Id).ToListAsync();
        if (lessonIds.Count == 0) return;
        db.TrainingSegmentProgress.RemoveRange(await db.TrainingSegmentProgress.Where(p => lessonIds.Contains(p.LessonId)).ToListAsync());
        var segmentIds = await db.TrainingSegments.Where(s => lessonIds.Contains(s.LessonId)).Select(s => s.Id).ToListAsync();
        db.TrainingQuestions.RemoveRange(await db.TrainingQuestions.Where(q => segmentIds.Contains(q.SegmentId)).ToListAsync());
        db.TrainingSegments.RemoveRange(await db.TrainingSegments.Where(s => lessonIds.Contains(s.LessonId)).ToListAsync());
        db.TrainingLessonGames.RemoveRange(await db.TrainingLessonGames.Where(g => lessonIds.Contains(g.LessonId)).ToListAsync());
        db.TrainingNotes.RemoveRange(await db.TrainingNotes.Where(n => lessonIds.Contains(n.LessonId)).ToListAsync());
        db.TrainingLessons.RemoveRange(await db.TrainingLessons.Where(l => l.UserId == userId).ToListAsync());
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
