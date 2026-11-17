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
/// Liest eine aufbereitete Trainingsstunde (<see cref="TrainingLessonFile"/>, <c>schema: 1</c>) für EIN Konto ein und
/// teilt sie in bis zu zwei Einträge (<see cref="SplitParts"/>: private = eigene Partien, general = Lehrbeispiele/Übungen).
/// Idempotent je Konto + Datum + Teil: bestehende Zeilen bleiben erhalten (Id und Links), alle Partien, Segmente, Fragen
/// und Notizen werden durch die neuen ersetzt, der Übungsstand zieht mit seinen Segmenten um (<see cref="MoveProgress"/>).
/// Im Probelauf (<c>dryRun</c>) wird alles geprüft und gezählt, aber nichts geschrieben.
/// </summary>
public class TrainingVideoImporter
{
    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "own", "lesson", "exercise" };
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
        { "schwaeche", "eroeffnung", "plan", "rechnen", "zeit", "psychologie", "endspiel", "allgemein" };
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

        // Sprecherwechsel: gelten fürs ganze Video, also für jeden Teil gleich. Fehlt die Angabe, bleiben vorhandene.
        var turns = TrainingSpeakerTurns.Normalize(file.SpeakerTurns, warnings);
        var turnsJson = turns == null ? null : TrainingSpeakerTurns.Serialize(turns);
        lesson.SpeakerTurns = turnsJson;

        // Teilen: eigene Partien → „private“, Lehrbeispiele/Übungen → „general“ (zwei Einträge, dasselbe Video).
        var parts = SplitParts(lesson);

        var existing = await _db.TrainingLessons
            .Include(l => l.Games).Include(l => l.Segments).ThenInclude(s => s.Question).Include(l => l.Notes)
            .Where(l => l.UserId == ownerId && l.Date == date)
            .ToListAsync(ct);

        // Bestehende Zeilen weiterverwenden (stabile Id, Links bleiben): zuerst gleicher Teil, dann eine übrige Zeile
        // (die alte ungeteilte Stunde wird so zu einem der Teile). Was übrig bleibt, verschwindet — erst NACHDEM ihr
        // Übungsstand umgezogen ist.
        var keep = new Dictionary<string, TrainingLesson>(StringComparer.Ordinal);
        var free = existing.OrderBy(e => e.Part == TrainingLessonParts.Private ? 0 : 1).ThenBy(e => e.Id).ToList();
        foreach (var part in parts)
        {
            var hit = free.FirstOrDefault(e => e.Part == part.Part);
            if (hit != null) { keep[part.Part] = hit; free.Remove(hit); }
        }
        foreach (var part in parts.Where(p => !keep.ContainsKey(p.Part)))
        {
            if (free.Count == 0) break;
            keep[part.Part] = free[0];
            free.RemoveAt(0);
        }

        var progressMoved = 0;
        if (!dryRun)
        {
            var progress = existing.Count == 0 ? new List<TrainingSegmentProgress>()
                : await _db.TrainingSegmentProgress.Where(p => existing.Select(e => e.Id).Contains(p.LessonId)).ToListAsync(ct);

            var final = new List<TrainingLesson>();
            foreach (var part in parts)
            {
                if (keep.TryGetValue(part.Part, out var row))
                {
                    // Zeile behalten, Kinder ersetzen. Explizit löschen — InMemory kaskadiert nicht.
                    RemoveChildren(row);
                    row.Part = part.Part;
                    row.VideoFile = part.VideoFile;
                    row.DurationSec = part.DurationSec;
                    row.Summary = part.Summary;
                    row.Schema = part.Schema;
                    row.ImportedAt = part.ImportedAt;
                    row.Games = part.Games;
                    row.Segments = part.Segments;
                    row.Notes = part.Notes;
                    if (turns != null) row.SpeakerTurns = turnsJson;
                    final.Add(row);
                }
                else
                {
                    // Neuer Teil ohne Angabe: die Wechsel eines bisherigen Teils desselben Videos übernehmen.
                    if (turns == null) part.SpeakerTurns = existing.Select(e => e.SpeakerTurns).FirstOrDefault(t => t != null);
                    _db.TrainingLessons.Add(part);
                    final.Add(part);
                }
            }

            progressMoved = MoveProgress(progress, existing, final);

            foreach (var gone in free)
            {
                RemoveChildren(gone);
                // Teilen-Links des wegfallenden Eintrags gehen mit (InMemory kaskadiert nicht); behaltene Zeilen behalten ihre.
                _db.TrainingLessonShares.RemoveRange(await _db.TrainingLessonShares.Where(x => x.LessonId == gone.Id).ToListAsync(ct));
                _db.TrainingLessons.Remove(gone);
            }
            await _db.SaveChangesAsync(ct);
            _log?.LogInformation("Trainingsstunde {Date} für Konto {UserId} eingelesen ({Parts} Teile, {Segments} Segmente, {Moved} Übungsstände umgezogen, ersetzt: {Replaced})",
                date, ownerId, final.Count, lesson.Segments.Count, progressMoved, existing.Count > 0);
        }

        var partReports = parts.Select(p =>
        {
            var row = keep.GetValueOrDefault(p.Part);
            int? id = dryRun ? row?.Id : (row?.Id ?? p.Id);
            return new TrainingImportPartDto(p.Part, id, row != null, p.Games.Count, p.Segments.Count,
                p.Segments.Count(s => s.Question != null), p.Notes.Count);
        }).ToList();
        if (!dryRun && free.Count > 0) warnings.Add($"{free.Count} alte Einträge dieses Datums entfernt (Übungsstand übernommen).");
        return new TrainingImportReportDto(date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), dryRun, true, existing.Count > 0,
            partReports.FirstOrDefault()?.LessonId, lesson.Games.Count, lesson.Segments.Count, questions, lesson.Notes.Count, invalidFens,
            videoFound, warnings, new(), partReports, progressMoved, turns?.Count);
    }

    /// <summary>
    /// Teilt die eingelesene Stunde in ihre Einträge: Partien nach Art (<see cref="TrainingLessonParts.OfKind"/>), Segmente
    /// mit ihrer Partie. Segmente ohne (bekannte) Partie — Einleitung, Plauderei, allgemeine Erklärung — gehören zum Teil
    /// ihrer Nachbarn in der Zeitfolge: stimmen der vorige und der nächste zugeordnete Nachbar überein (oder gibt es nur
    /// einen), dorthin; sonst (unklar) zum privaten Teil. Notizen sind meist Aussagen über die eigenen Schwächen und gehen
    /// geschlossen zum privaten Teil — gibt es keinen, zum einzigen Teil. Hat eine Stunde nur eine Art, gibt es nur einen
    /// Eintrag; ohne jede Partie einen privaten. Reihenfolge: privat vor allgemein.
    /// </summary>
    internal static List<TrainingLesson> SplitParts(TrainingLesson all)
    {
        var partOf = all.Games.ToDictionary(g => g.Key, g => TrainingLessonParts.OfKind(g.Kind), StringComparer.Ordinal);
        var gameParts = partOf.Values.ToHashSet(StringComparer.Ordinal);
        var fallback = gameParts.Count == 0 || gameParts.Contains(TrainingLessonParts.Private)
            ? TrainingLessonParts.Private : TrainingLessonParts.General;

        // Zeitfolge (bei gleicher Zeit die Dateireihenfolge) — darin die Nachbarn suchen.
        var ordered = all.Segments.OrderBy(s => s.StartSec).ThenBy(s => s.Position).ToList();
        string? Own(TrainingSegment s) => s.GameKey != null && partOf.TryGetValue(s.GameKey, out var p) ? p : null;
        var assigned = new Dictionary<TrainingSegment, string>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var part = Own(ordered[i]);
            if (part == null)
            {
                string? before = null, after = null;
                for (var k = i - 1; k >= 0 && before == null; k--) before = Own(ordered[k]);
                for (var k = i + 1; k < ordered.Count && after == null; k++) after = Own(ordered[k]);
                part = before == after || after == null ? before ?? fallback
                    : before == null ? after
                    : TrainingLessonParts.Private;   // zwischen den Teilen: unklar → privat
            }
            assigned[ordered[i]] = part;
        }

        var needed = gameParts.Concat(assigned.Values).Distinct(StringComparer.Ordinal).ToList();
        if (needed.Count == 0) needed.Add(TrainingLessonParts.Private);
        var noteTarget = needed.Contains(TrainingLessonParts.Private) ? TrainingLessonParts.Private : needed[0];

        return needed.OrderBy(p => p == TrainingLessonParts.Private ? 0 : 1).Select(part => new TrainingLesson
        {
            UserId = all.UserId,
            Date = all.Date,
            Part = part,
            VideoFile = all.VideoFile,
            DurationSec = all.DurationSec,
            Summary = all.Summary,
            Schema = all.Schema,
            ImportedAt = all.ImportedAt,
            SpeakerTurns = all.SpeakerTurns,
            Games = all.Games.Where(g => partOf[g.Key] == part).ToList(),
            Segments = all.Segments.Where(s => assigned[s] == part).ToList(),
            Notes = part == noteTarget ? all.Notes.ToList() : new(),
        }).ToList();
    }

    /// <summary>
    /// Übungsstand (aller Konten) der alten Einträge dieses Datums auf die neuen verteilen: jede Zeile zu dem Eintrag, der
    /// ein Segment mit ihrem Schlüssel (Startzeit + Figurenfeld) hat — der bisherige zuerst. Kein Segment passt mehr: bleibt
    /// im bisherigen Eintrag, oder (wenn der wegfällt) zum ersten neuen. Treffen zwei Zeilen desselben Kontos aufeinander,
    /// zählt der jüngere Versuch, die Spiele werden addiert. Nichts geht verloren. Rückgabe: Anzahl umgezogener Zeilen.
    /// </summary>
    internal int MoveProgress(List<TrainingSegmentProgress> progress, List<TrainingLesson> existing, List<TrainingLesson> final)
    {
        if (progress.Count == 0 || final.Count == 0) return 0;
        var keysOf = final.ToDictionary(l => l, l => l.Segments.Select(s => TrainingVideoService.SegmentKey(s.StartSec, s.BoardFen))
            .ToHashSet(StringComparer.Ordinal));
        var byId = existing.ToDictionary(e => e.Id);
        // Wo liegt welche Zeile am Ende? (Konto, Eintrag, Schlüssel) → Zeile
        var placed = new Dictionary<(int, TrainingLesson, string), TrainingSegmentProgress>();
        var moving = new List<(TrainingSegmentProgress Row, TrainingLesson Dest)>();
        foreach (var p in progress)
        {
            var current = byId.GetValueOrDefault(p.LessonId);
            var currentKept = current != null && keysOf.ContainsKey(current);
            TrainingLesson dest;
            if (currentKept && keysOf[current!].Contains(p.SegmentKey)) dest = current!;
            else dest = final.FirstOrDefault(l => keysOf[l].Contains(p.SegmentKey)) ?? (currentKept ? current! : final[0]);
            if (dest == current) placed[(p.UserId, dest, p.SegmentKey)] = p;
            else moving.Add((p, dest));
        }
        var moved = 0;
        foreach (var (row, dest) in moving)
        {
            if (placed.TryGetValue((row.UserId, dest, row.SegmentKey), out var there))
            {
                if (row.UpdatedAt > there.UpdatedAt)
                {
                    there.Status = row.Status;
                    there.Attempts = row.Attempts;
                    there.UpdatedAt = row.UpdatedAt;
                }
                there.Plays += row.Plays;
                _db.TrainingSegmentProgress.Remove(row);
            }
            else
            {
                row.Lesson = dest;
                if (dest.Id != 0) row.LessonId = dest.Id;
                placed[(row.UserId, dest, row.SegmentKey)] = row;
            }
            moved++;
        }
        return moved;
    }

    private void RemoveChildren(TrainingLesson row)
    {
        _db.TrainingQuestions.RemoveRange(row.Segments.Where(s => s.Question != null).Select(s => s.Question!));
        _db.TrainingSegments.RemoveRange(row.Segments);
        _db.TrainingLessonGames.RemoveRange(row.Games);
        _db.TrainingNotes.RemoveRange(row.Notes);
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
            var report = await ImportWithSpeakersAsync(ownerId, json, SpeakerFileOf(path), dryRun, ct);
            reports.Add(report.Ok ? report : report with { Date = report.Date ?? Path.GetFileNameWithoutExtension(path) });
            _db.ChangeTracker.Clear();
        }
        return reports;
    }

    /// <summary>Die Sprecher-Datei zu einer Stunde: <c>&lt;ordner&gt;/sprecher/&lt;datum&gt;.json</c> (fehlt = <c>null</c>).
    /// Der Unterordner stört den Ordner-Import nicht — der liest nur <c>*.json</c> direkt im Ordner.</summary>
    internal static string? SpeakerFileOf(string lessonPath)
    {
        var dir = Path.GetDirectoryName(lessonPath);
        if (dir == null) return null;
        var path = Path.Combine(dir, "sprecher", Path.GetFileName(lessonPath));
        return File.Exists(path) ? path : null;
    }

    /// <summary>Wie <see cref="ImportAsync(int, string, bool, CancellationToken)"/>, aber die Sprecherwechsel kommen aus der
    /// Sprecher-Datei, wenn die Stunde selbst keine trägt.</summary>
    internal async Task<TrainingImportReportDto> ImportWithSpeakersAsync(int ownerId, string json, string? speakerPath, bool dryRun,
        CancellationToken ct)
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
        if (file.SpeakerTurns == null && speakerPath != null)
        {
            try
            {
                var sp = JsonSerializer.Deserialize<TrainingSpeakerFile>(await File.ReadAllTextAsync(speakerPath, ct), TrainingLessonFile.Json);
                if (sp?.Turns != null) file = file with { SpeakerTurns = sp.Turns };
            }
            catch (JsonException ex)
            {
                var report = await ImportAsync(ownerId, file, dryRun, ct);
                report.Warnings.Add($"Sprecher-Datei unlesbar: {ex.Message}");
                return report;
            }
        }
        return await ImportAsync(ownerId, file, dryRun, ct);
    }

    private static TrainingImportReportDto Failed(string? date, bool dryRun, string error) =>
        new(date, dryRun, false, false, null, 0, 0, 0, 0, 0, false, new(), new() { error }, new(), 0);

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
