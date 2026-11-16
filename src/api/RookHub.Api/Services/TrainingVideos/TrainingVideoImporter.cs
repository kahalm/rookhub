using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.DTOs.TrainingVideos;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;

namespace RookHub.Api.Services.TrainingVideos;

/// <summary>
/// Liest eine aufbereitete Trainingsstunde (<see cref="TrainingLessonFile"/>, <c>schema: 1</c>) für EIN Konto ein.
/// Idempotent je Datum: gibt es die Stunde des Kontos zu diesem Datum schon, bleibt ihre Zeile (und damit ihre Id und
/// jeder Link darauf) erhalten, alle Partien, Segmente, Fragen und Notizen werden durch die neuen ersetzt.
/// Im Probelauf (<c>dryRun</c>) wird alles geprüft und gezählt, aber nichts geschrieben.
/// </summary>
public class TrainingVideoImporter
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "own", "lesson", "exercise" };
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
        { "schwaeche", "eroeffnung", "plan", "rechnen", "zeit", "psychologie", "allgemein" };
    private static readonly HashSet<string> Confidences = new(StringComparer.Ordinal) { "high", "low", "none" };

    private readonly AppDbContext _db;
    private readonly TrainingVideoOptions _options;
    private readonly ILogger<TrainingVideoImporter>? _log;

    public TrainingVideoImporter(AppDbContext db, IOptions<TrainingVideoOptions> options, ILogger<TrainingVideoImporter>? log = null)
    {
        _db = db;
        _options = options.Value;
        _log = log;
    }

    /// <summary>Das Konto, dem importiert wird: Id oder Benutzername. 404, wenn es keins gibt.</summary>
    public async Task<(int Id, string Username)> ResolveOwnerAsync(string? user, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user)) throw new DomainValidationException("Parameter „user“ (Id oder Benutzername) fehlt.");
        var key = user.Trim();
        var q = _db.AppUsers.Where(u => u.DeletedAt == null);
        var row = int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            ? await q.Where(u => u.Id == id).Select(u => new { u.Id, u.Username }).FirstOrDefaultAsync(ct)
            : await q.Where(u => u.Username == key).Select(u => new { u.Id, u.Username }).FirstOrDefaultAsync(ct);
        if (row == null) throw new NotFoundException("Konto nicht gefunden.");
        return (row.Id, row.Username);
    }

    /// <summary>Eine Datei als Text einlesen (Probelauf oder echt). Fehler im Inhalt stehen im Bericht, nicht als Ausnahme.</summary>
    public async Task<TrainingImportReportDto> ImportAsync(int ownerId, string json, bool dryRun, CancellationToken ct)
    {
        TrainingLessonFile? file;
        try
        {
            file = JsonSerializer.Deserialize<TrainingLessonFile>(json, TrainingLessonFile.Json);
        }
        catch (JsonException ex)
        {
            return Failed(null, dryRun, $"JSON unlesbar: {ex.Message}");
        }
        if (file == null) return Failed(null, dryRun, "Leere Datei.");
        return await ImportAsync(ownerId, file, dryRun, ct);
    }

    public async Task<TrainingImportReportDto> ImportAsync(int ownerId, TrainingLessonFile file, bool dryRun, CancellationToken ct)
    {
        var warnings = new List<string>();
        // Schema 2 = Schema 1 + optionale Felder (video.originalFile, moveKind, quoteFrom/quoteTo, analysisPgn).
        if (file.Schema is not (1 or 2)) return Failed(file.Date, dryRun, $"Unbekanntes Schema {file.Schema} (erwartet 1 oder 2).");
        if (!DateOnly.TryParseExact(file.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return Failed(file.Date, dryRun, $"Datum „{file.Date}“ ist nicht JJJJ-MM-TT.");

        // Video: nur ein Dateiname, kein Pfad — die Datei liegt im Video-Ordner.
        string? videoFile = file.Video?.File?.Trim();
        if (string.IsNullOrEmpty(videoFile)) videoFile = null;
        else if (Path.GetFileName(videoFile) != videoFile || videoFile.Contains(".."))
        {
            warnings.Add($"Videodatei „{videoFile}“ ist kein reiner Dateiname — ignoriert.");
            videoFile = null;
        }
        var videoFound = videoFile != null && !string.IsNullOrWhiteSpace(_options.VideoRoot)
            && File.Exists(Path.Combine(_options.VideoRoot, videoFile));
        if (videoFile != null && !videoFound)
            warnings.Add(string.IsNullOrWhiteSpace(_options.VideoRoot)
                ? "Kein Video-Ordner eingestellt (TrainingVideos__VideoRoot) — Video nicht geprüft."
                : $"Video „{videoFile}“ liegt (noch) nicht im Video-Ordner.");

        var lesson = new TrainingLesson
        {
            UserId = ownerId,
            Date = date,
            VideoFile = videoFile,
            DurationSec = file.Video?.DurationSec is double d && d > 0 ? (int)Math.Round(d) : null,
            Summary = Clean(file.Summary),
            Schema = file.Schema,
            ImportedAt = DateTime.UtcNow,
        };

        // Partien
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var pos = 0;
        foreach (var g in file.Games ?? new())
        {
            var key = Cut(g.Id?.Trim(), 32);
            if (string.IsNullOrEmpty(key)) { warnings.Add($"Partie {pos + 1} ohne „id“ — übersprungen."); continue; }
            if (!keys.Add(key)) { warnings.Add($"Partie „{key}“ doppelt — zweite übersprungen."); continue; }
            var kind = g.Kind?.Trim().ToLowerInvariant();
            if (kind == null || !Kinds.Contains(kind)) { warnings.Add($"Partie „{key}“: Art „{g.Kind}“ unbekannt — als „lesson“."); kind = "lesson"; }
            var conf = g.MatchConfidence?.Trim().ToLowerInvariant();
            lesson.Games.Add(new TrainingLessonGame
            {
                Key = key,
                Kind = kind,
                White = Cut(Clean(g.White), 120),
                Black = Cut(Clean(g.Black), 120),
                Event = Cut(Clean(g.Event), 200),
                Year = g.Year is > 0 and < 3000 ? g.Year : null,
                Eco = Cut(Clean(g.Eco), 8),
                Result = Cut(Clean(g.Result), 10),
                FromSec = TrainingTime.ParseSeconds(g.From),
                ToSec = TrainingTime.ParseSeconds(g.To),
                Pgn = Clean(g.Pgn),
                AnalysisPgn = Clean(g.AnalysisPgn),
                RookhubGameId = g.RookhubGameId is > 0 ? g.RookhubGameId : null,
                MatchConfidence = conf != null && Confidences.Contains(conf) ? conf : null,
                Position = pos++,
            });
        }

        // Zugeordnete RookHub-Partien müssen dem Konto gehören — sonst bleibt die Zuordnung stehen, aber mit Warnung.
        var gameIds = lesson.Games.Where(g => g.RookhubGameId != null).Select(g => g.RookhubGameId!.Value).Distinct().ToList();
        if (gameIds.Count > 0)
        {
            var owned = await _db.SavedGames.Where(s => gameIds.Contains(s.Id) && s.UserId == ownerId).Select(s => s.Id).ToListAsync(ct);
            foreach (var missing in gameIds.Except(owned))
                warnings.Add($"RookHub-Partie {missing} gehört nicht zu diesem Konto oder fehlt — Trainerkommentare erscheinen dort nicht.");
        }

        // Segmente
        int invalidFens = 0, questions = 0;
        pos = 0;
        foreach (var s in file.Segments ?? new())
        {
            var start = TrainingTime.ParseSeconds(s.T);
            if (start == null) { warnings.Add($"Segment {pos + 1}: Zeit „{s.T}“ unlesbar — übersprungen."); pos++; continue; }
            string? fen = null, board = null;
            if (!string.IsNullOrWhiteSpace(s.Fen))
            {
                fen = FenListParser.NormalizeFen(s.Fen);
                board = TrainingBoardKey.Of(fen);
                if (fen == null || board == null)
                {
                    invalidFens++;
                    if (invalidFens <= 5) warnings.Add($"Segment {s.T}: FEN ungültig — ohne Stellung eingelesen.");
                    fen = null; board = null;
                }
            }
            var gameKey = Cut(s.GameId?.Trim(), 32);
            if (!string.IsNullOrEmpty(gameKey) && !keys.Contains(gameKey))
                warnings.Add($"Segment {s.T}: Partie „{gameKey}“ gibt es in der Datei nicht.");
            var comment = Clean(s.Comment);
            var quote = Clean(s.Quote);
            var seg = new TrainingSegment
            {
                Position = pos++,
                StartSec = start.Value,
                EndSec = TrainingTime.ParseSeconds(s.TEnd),
                GameKey = string.IsNullOrEmpty(gameKey) ? null : gameKey,
                Fen = fen,
                BoardFen = board,
                Move = Cut(Clean(s.Move), 16),
                MoveKind = s.MoveKind?.Trim().ToLowerInvariant() is "played" or "analysis" ? s.MoveKind.Trim().ToLowerInvariant() : null,
                QuoteStartSec = TrainingTime.ParseSeconds(s.QuoteFrom),
                QuoteEndSec = TrainingTime.ParseSeconds(s.QuoteTo),
                Comment = comment,
                Quote = quote,
                Variations = Clean(s.Variations),
                Tags = Cut(JoinTags(s.Tags), 200),
                SearchText = Join(comment, quote),
            };
            if (s.Question is { } q && !string.IsNullOrWhiteSpace(q.Prompt))
            {
                seg.Question = new TrainingQuestion
                {
                    Prompt = q.Prompt.Trim(),
                    Answer = Cut(Clean(q.Answer), 16),
                    AnswerLine = Clean(q.AnswerLine),
                    AnswerSec = TrainingTime.ParseSeconds(q.AnswerT),
                    Explanation = Clean(q.Explanation),
                };
                questions++;
            }
            lesson.Segments.Add(seg);
        }
        if (invalidFens > 5) warnings.Add($"… insgesamt {invalidFens} ungültige FENs.");

        // Notizen
        pos = 0;
        foreach (var n in file.Notes ?? new())
        {
            var text = Clean(n.Text);
            if (text == null) { pos++; continue; }
            var cat = n.Category?.Trim().ToLowerInvariant();
            if (cat == null || !Categories.Contains(cat)) cat = "allgemein";
            var quote = Clean(n.Quote);
            var gameKey = Cut(n.GameId?.Trim(), 32);
            lesson.Notes.Add(new TrainingNote
            {
                Position = pos++,
                StartSec = TrainingTime.ParseSeconds(n.T),
                GameKey = string.IsNullOrEmpty(gameKey) ? null : gameKey,
                Category = cat,
                Text = text,
                Quote = quote,
                SearchText = Join(text, quote),
            });
        }

        var existing = await _db.TrainingLessons
            .Include(l => l.Games).Include(l => l.Segments).ThenInclude(s => s.Question).Include(l => l.Notes)
            .FirstOrDefaultAsync(l => l.UserId == ownerId && l.Date == date, ct);

        int? lessonId = existing?.Id;
        if (!dryRun)
        {
            if (existing != null)
            {
                // Zeile behalten (stabile Id), Kinder ersetzen. Explizit löschen — InMemory kaskadiert nicht.
                _db.TrainingQuestions.RemoveRange(existing.Segments.Where(s => s.Question != null).Select(s => s.Question!));
                _db.TrainingSegments.RemoveRange(existing.Segments);
                _db.TrainingLessonGames.RemoveRange(existing.Games);
                _db.TrainingNotes.RemoveRange(existing.Notes);
                existing.VideoFile = lesson.VideoFile;
                existing.DurationSec = lesson.DurationSec;
                existing.Summary = lesson.Summary;
                existing.Schema = lesson.Schema;
                existing.ImportedAt = lesson.ImportedAt;
                existing.Games = lesson.Games;
                existing.Segments = lesson.Segments;
                existing.Notes = lesson.Notes;
            }
            else
            {
                _db.TrainingLessons.Add(lesson);
            }
            await _db.SaveChangesAsync(ct);
            lessonId = existing?.Id ?? lesson.Id;
            _log?.LogInformation("Trainingsstunde {Date} für Konto {UserId} eingelesen ({Segments} Segmente, ersetzt: {Replaced})",
                date, ownerId, lesson.Segments.Count, existing != null);
        }

        return new TrainingImportReportDto(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dryRun, true, existing != null,
            lessonId, lesson.Games.Count, lesson.Segments.Count, questions, lesson.Notes.Count, invalidFens, videoFound, warnings, new());
    }

    /// <summary>Alle <c>*.json</c> des Daten-Ordners (<see cref="TrainingVideoOptions.DataRoot"/>), nach Name sortiert.</summary>
    public async Task<List<TrainingImportReportDto>> ImportFolderAsync(int ownerId, bool dryRun, CancellationToken ct)
    {
        var root = _options.DataRoot;
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            throw new DomainValidationException("Kein Daten-Ordner eingestellt oder nicht vorhanden (TrainingVideos__DataRoot).");
        var reports = new List<TrainingImportReportDto>();
        foreach (var path in Directory.GetFiles(root, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var json = await File.ReadAllTextAsync(path, ct);
            var report = await ImportAsync(ownerId, json, dryRun, ct);
            reports.Add(report.Ok ? report : report with { Date = report.Date ?? Path.GetFileNameWithoutExtension(path) });
            _db.ChangeTracker.Clear();
        }
        return reports;
    }

    private static TrainingImportReportDto Failed(string? date, bool dryRun, string error) =>
        new(date, dryRun, false, false, null, 0, 0, 0, 0, 0, false, new(), new() { error });

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string? Cut(string? s, int max) => s == null ? null : (s.Length <= max ? s : s[..max]);

    private static string? Join(string? a, string? b) =>
        a == null ? b : b == null ? a : a + "\n" + b;

    private static string? JoinTags(List<string>? tags)
    {
        if (tags == null) return null;
        var clean = tags.Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim().ToLowerInvariant().Replace(",", ""))
            .Distinct().ToList();
        return clean.Count == 0 ? null : string.Join(',', clean);
    }
}
