using System.Globalization;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.TrainingVideos;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Services.TrainingVideos;

/// <summary>
/// Das Trainer-Notizbuch: alle Notizen der sichtbaren Stunden (eigenes Konto bzw. Admin: alle) stundenübergreifend, dazu
/// die wiederkehrenden Themen (<see cref="TrainingNoteThemes"/>) und die Eröffnungsempfehlungen — jede Notiz der Kategorie
/// <c>eroeffnung</c> mit der Stellung des nächstgelegenen Segments derselben Partie und dem Abgleich mit dem EIGENEN
/// Repertoire des anfragenden Kontos (<see cref="RepertoireBoardContinuations"/>). Die Merksätze (kuratierte Liste)
/// bleiben davon unberührt — Notizen werden nirgends dorthin übernommen.
/// </summary>
public class TrainingNotebookService
{
    private readonly AppDbContext _db;
    private readonly TrainingVideoService _videos;
    private readonly RepertoireBoardContinuations _repertoire;

    public TrainingNotebookService(AppDbContext db, TrainingVideoService videos, RepertoireBoardContinuations repertoire)
    {
        _db = db;
        _videos = videos;
        _repertoire = repertoire;
    }

    public async Task<TrainingNotebookDto> GetAsync(int userId, bool isAdmin, CancellationToken ct)
    {
        var lessons = await _db.TrainingLessons.AsNoTracking()
            .Where(l => isAdmin || l.UserId == userId)
            .Select(l => new { l.Id, l.UserId, l.Date, l.VideoFile, l.DurationSec })
            .ToListAsync(ct);
        if (lessons.Count == 0) return new TrainingNotebookDto(new(), new(), new(), false, false);
        var ids = lessons.Select(l => l.Id).ToList();
        var lessonOf = lessons.ToDictionary(l => l.Id);

        var notes = await _db.TrainingNotes.AsNoTracking().Where(n => ids.Contains(n.LessonId)).ToListAsync(ct);
        // Notizen stehen im privaten Teil der Stunde, ihre Partie kann im allgemeinen Teil desselben Datums liegen:
        // Partien daher je Stunde (Konto + Datum) über alle Teile nachschlagen.
        var gameOf = await _videos.SessionGamesAsync(lessons.Select(l => (l.UserId, l.Date)).Distinct().ToList(), ct);
        var video = lessons.ToDictionary(l => l.Id, l => _videos.HasVideo(l.VideoFile));

        // Neueste Stunde zuerst, in der Stunde nach Zeit/Reihenfolge.
        notes = notes.OrderByDescending(n => lessonOf[n.LessonId].Date).ThenBy(n => n.StartSec ?? int.MaxValue)
            .ThenBy(n => n.Position).ToList();
        var noteDtos = notes.Select(n =>
        {
            var l = lessonOf[n.LessonId];
            var g = n.GameKey != null && gameOf.TryGetValue((l.UserId, l.Date, n.GameKey), out var x) ? x : null;
            return new TrainingNotebookNoteDto(n.Id, n.LessonId, Iso(l.Date), n.StartSec, n.Category, n.Text, n.Quote, n.GameKey,
                g?.White, g?.Black, g?.Kind, g?.RookhubGameId, video[n.LessonId], l.DurationSec, g?.LessonId);
        }).ToList();

        var themes = TrainingNoteThemes.Find(notes
                .Select(n => new TrainingNoteThemes.Input(n.Id, n.LessonId, lessonOf[n.LessonId].Date, n.Position, n.Category, n.Text))
                .ToList())
            .Select(t => new TrainingNotebookThemeDto(t.Lessons, t.RepresentativeId, t.Category, t.NoteIds))
            .ToList();

        var sessionOf = lessons.ToDictionary(l => l.Id, l => (l.UserId, l.Date));
        var (openings, hasRepertoire, truncated) = await OpeningsAsync(userId, notes.Where(n => n.Category == "eroeffnung").ToList(),
            sessionOf, ct);
        return new TrainingNotebookDto(noteDtos, themes, openings, hasRepertoire, truncated);
    }

    private async Task<(List<TrainingNotebookOpeningDto>, bool, bool)> OpeningsAsync(int userId, List<Models.TrainingNote> notes,
        Dictionary<int, (int UserId, DateOnly Date)> sessionOf, CancellationToken ct)
    {
        if (notes.Count == 0) return (new(), false, false);
        // Segmente derselben Partie in allen Teilen der Stunde (die Partie einer Notiz kann im allgemeinen Teil liegen).
        var sessions = notes.Select(n => sessionOf[n.LessonId]).ToHashSet();
        var lessonIds = sessionOf.Where(kv => sessions.Contains(kv.Value)).Select(kv => kv.Key).ToList();
        var segments = await _db.TrainingSegments.AsNoTracking()
            .Where(s => lessonIds.Contains(s.LessonId) && s.Fen != null && s.GameKey != null)
            .Select(s => new { s.Id, s.LessonId, s.GameKey, s.StartSec, s.EndSec, s.Fen, s.BoardFen, s.Move })
            .ToListAsync(ct);

        var picked = notes.Select(n =>
        {
            var session = sessionOf[n.LessonId];
            var same = segments.Where(s => sessionOf[s.LessonId] == session && s.GameKey == n.GameKey)
                .OrderBy(s => s.StartSec).ToList();
            var seg = Nearest(same.Select(s => (s.StartSec, s.EndSec)).ToList(), n.StartSec) is int k ? same[k] : null;
            return (Note: n, Segment: seg);
        }).ToList();

        var boards = picked.Where(p => p.Segment?.BoardFen != null).Select(p => p.Segment!.BoardFen!).Distinct().ToList();
        var found = await _repertoire.FindAsync(userId, boards, ct);

        var result = picked.Select(p =>
        {
            var hits = new List<TrainingNotebookRepertoireDto>();
            if (p.Segment?.BoardFen is { } board && found.ByBoard.TryGetValue(board, out var list))
            {
                var side = TrainingBoardKey.Side(p.Segment.Fen);
                hits = list
                    .Select(h => new TrainingNotebookRepertoireDto(h.RepertoireId, h.RepertoireName, h.Chapter, h.LineName,
                        h.GameIndex, h.Ply, h.Moves, side == null || h.Side == side))
                    .OrderByDescending(h => h.SameSide).ThenBy(h => h.RepertoireName, StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();
            }
            return new TrainingNotebookOpeningDto(p.Note.Id, p.Segment?.Fen, p.Segment?.Id, p.Segment?.Move, hits);
        }).ToList();
        return (result, found.HasOwn, found.Truncated);
    }

    /// <summary>Index des Segments zur Notizzeit: zuerst eines, dessen Spanne die Zeit enthält (das zuletzt begonnene),
    /// sonst das mit dem kleinsten Abstand seines Beginns (bei Gleichstand das frühere). Ohne Zeit: das erste.</summary>
    internal static int? Nearest(IReadOnlyList<(int Start, int? End)> segments, int? t)
    {
        if (segments.Count == 0) return null;
        if (t is not int at) return 0;
        int? containing = null;
        for (var i = 0; i < segments.Count; i++)
        {
            var (start, end) = segments[i];
            if (start <= at && end is int e && at <= e && (containing == null || start >= segments[containing.Value].Start))
                containing = i;
        }
        if (containing != null) return containing;
        var best = 0;
        for (var i = 1; i < segments.Count; i++)
        {
            var d = Math.Abs(segments[i].Start - at);
            var bd = Math.Abs(segments[best].Start - at);
            if (d < bd || (d == bd && segments[i].Start < segments[best].Start)) best = i;
        }
        return best;
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
