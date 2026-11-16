using System.Globalization;
using System.Text;
using Chess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RookHub.Api.Data;
using RookHub.Api.DTOs.TrainingVideos;
using RookHub.Api.Exceptions;
using RookHub.Api.Models;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.TrainingVideos;

/// <summary>
/// Lesen der Trainingsvideos: Stundenliste, Stundenseite, Stellungs- und Volltextsuche, Trainerkommentare an einer
/// RookHub-Partie und die Video-Datei. Sichtbar ist eine Stunde NUR ihrem Konto und Admins — jede Abfrage läuft über
/// <see cref="Visible"/>; eine fremde Stunde ist 404, nicht 403 (verrät nicht, dass es sie gibt).
/// </summary>
public class TrainingVideoService
{
    public const int MaxHits = 100;

    private readonly AppDbContext _db;
    private readonly TrainingVideoOptions _options;

    public TrainingVideoService(AppDbContext db, IOptions<TrainingVideoOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    private IQueryable<TrainingLesson> Visible(int userId, bool isAdmin) =>
        _db.TrainingLessons.Where(l => isAdmin || l.UserId == userId);

    /// <summary>Hat das Konto überhaupt Stunden? (Menü/Kachel, Partieseite fragt nur dann nach Kommentaren.)</summary>
    public Task<bool> AnyAsync(int userId, bool isAdmin, CancellationToken ct) => Visible(userId, isAdmin).AnyAsync(ct);

    public async Task<List<TrainingLessonListItemDto>> ListAsync(int userId, bool isAdmin, CancellationToken ct)
    {
        var rows = await Visible(userId, isAdmin)
            .OrderByDescending(l => l.Date)
            .Select(l => new
            {
                l.Id, l.Date, l.Summary, l.DurationSec, l.VideoFile, l.UserId,
                Owner = l.User != null ? l.User.Username : null,
                Segments = l.Segments.Count,
                Games = l.Games.OrderBy(g => g.Position).ToList(),
            })
            .ToListAsync(ct);
        return rows.Select(r => new TrainingLessonListItemDto(r.Id, Iso(r.Date), r.Summary, r.DurationSec, HasVideo(r.VideoFile),
            r.Segments, r.Games.Select(ToDto).ToList(), isAdmin && r.UserId != userId ? r.Owner : null)).ToList();
    }

    public async Task<TrainingLessonDto> GetAsync(int userId, bool isAdmin, int id, CancellationToken ct)
    {
        var l = await Visible(userId, isAdmin).AsNoTracking()
            .Include(x => x.Games).Include(x => x.Segments).ThenInclude(s => s.Question).Include(x => x.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Trainingsstunde nicht gefunden.");
        var games = l.Games.OrderBy(g => g.Position).ToList();
        var progress = await ProgressAsync(userId, new List<int> { l.Id }, ct);
        var positions = await GamePositionsAsync(new List<int> { l.Id }, ct);
        return new TrainingLessonDto(l.Id, Iso(l.Date), l.Summary, l.DurationSec, HasVideo(l.VideoFile),
            isAdmin && l.UserId != userId ? l.User?.Username : null,
            games.Select(ToDto).ToList(),
            l.Segments.OrderBy(s => s.Position).Select(s => ToDto(s, progress, positions)).ToList(),
            games.Where(g => g.Pgn != null).ToDictionary(g => g.Key, g => g.Pgn!),
            games.Where(g => g.AnalysisPgn != null).ToDictionary(g => g.Key, g => g.AnalysisPgn!));
    }

    /// <summary>„Im Training besprochen?“ — alle Segmente mit demselben Figurenfeld (Zugrecht als Zusatzangabe).</summary>
    public async Task<TrainingSearchResultDto> SearchPositionAsync(int userId, bool isAdmin, string? fen, CancellationToken ct)
    {
        var board = TrainingBoardKey.Of(fen) ?? throw new DomainValidationException("Keine gültige Stellung.");
        var side = TrainingBoardKey.Side(fen);
        var lessons = Visible(userId, isAdmin);
        var rows = await _db.TrainingSegments
            .Where(s => s.BoardFen == board && lessons.Any(l => l.Id == s.LessonId))
            .OrderByDescending(s => s.Lesson!.Date).ThenBy(s => s.Position)
            .Take(MaxHits + 1)
            .Select(s => new { s.Id, s.LessonId, s.Lesson!.Date, s.StartSec, s.Fen, s.Move, s.Comment, s.Quote, s.GameKey })
            .ToListAsync(ct);
        var labels = await GameLabelsAsync(rows.Select(r => r.LessonId).Distinct().ToList(), ct);
        var hits = rows.Take(MaxHits).Select(r => new TrainingSearchHitDto("segment", r.LessonId, Iso(r.Date), r.Id, r.StartSec, r.Fen,
            r.Move, r.Comment ?? r.Quote, Label(labels, r.LessonId, r.GameKey), null,
            side == null ? null : TrainingBoardKey.Side(r.Fen) == side)).ToList();
        return new TrainingSearchResultDto(hits, rows.Count > MaxHits);
    }

    /// <summary>Volltext über Kommentar + Zitat der Segmente und Text + Zitat der Notizen (MariaDB FULLTEXT, Boolean-Modus,
    /// jedes Wort Pflicht und als Wortanfang: „Läufer“ findet „Läuferpaar“).</summary>
    public async Task<TrainingSearchResultDto> SearchTextAsync(int userId, bool isAdmin, string? query, CancellationToken ct)
    {
        // Mindestlänge wie im Frontend: zusammen mindestens drei Buchstaben/Ziffern. Wörter ab drei Zeichen laufen über den
        // Volltext-Index (InnoDB indiziert kürzere nicht), kürzere als Teilzeichenkette (LIKE) — „Sf 3“ findet trotzdem.
        if (LetterCount(query) < 3) throw new DomainValidationException("Suchbegriff zu kurz (mindestens drei Buchstaben).");
        var all = Words(query, minLength: 1);
        var words = all.Where(w => w.Length >= 3).ToList();
        var shortWords = all.Where(w => w.Length < 3).ToList();
        var lessons = Visible(userId, isAdmin);
        var segments = _db.TrainingSegments.Where(s => s.SearchText != null && lessons.Any(l => l.Id == s.LessonId));
        var notes = _db.TrainingNotes.Where(n => n.SearchText != null && lessons.Any(l => l.Id == n.LessonId));
        foreach (var w in shortWords)
        {
            segments = segments.Where(s => s.SearchText!.ToLower().Contains(w));
            notes = notes.Where(n => n.SearchText!.ToLower().Contains(w));
        }
        if (words.Count == 0) { /* nur kurze Wörter: die LIKE-Bedingungen oben reichen */ }
        else if (_db.Database.IsRelational())
        {
            var term = string.Join(' ', words.Select(w => "+" + w + "*"));
            segments = segments.Where(s => EF.Functions.Match(s.SearchText!, term, MySqlMatchSearchMode.Boolean) > 0);
            notes = notes.Where(n => EF.Functions.Match(n.SearchText!, term, MySqlMatchSearchMode.Boolean) > 0);
        }
        else
        {
            // Test-Krücke: InMemory kennt kein MATCH (CLAUDE.md, InMemory-Lücke) — die echte Abfrage läuft gegen MariaDB.
            foreach (var w in words)
            {
                segments = segments.Where(s => s.SearchText!.ToLower().Contains(w));
                notes = notes.Where(n => n.SearchText!.ToLower().Contains(w));
            }
        }
        var segRows = await segments.OrderByDescending(s => s.Lesson!.Date).ThenBy(s => s.Position).Take(MaxHits + 1)
            .Select(s => new { s.Id, s.LessonId, s.Lesson!.Date, s.StartSec, s.Fen, s.Move, s.Comment, s.Quote, s.GameKey })
            .ToListAsync(ct);
        var noteRows = await notes.OrderByDescending(n => n.Lesson!.Date).ThenBy(n => n.Position).Take(MaxHits + 1)
            .Select(n => new { n.LessonId, n.Lesson!.Date, n.StartSec, n.Text, n.Category, n.GameKey })
            .ToListAsync(ct);
        var labels = await GameLabelsAsync(segRows.Select(r => r.LessonId).Concat(noteRows.Select(r => r.LessonId)).Distinct().ToList(), ct);
        var hits = segRows.Take(MaxHits).Select(r => new TrainingSearchHitDto("segment", r.LessonId, Iso(r.Date), r.Id, r.StartSec, r.Fen,
                r.Move, PickText(r.Comment, r.Quote, words), Label(labels, r.LessonId, r.GameKey), null, null))
            .Concat(noteRows.Take(MaxHits).Select(r => new TrainingSearchHitDto("note", r.LessonId, Iso(r.Date), null, r.StartSec, null,
                null, r.Text, Label(labels, r.LessonId, r.GameKey), r.Category, null)))
            .ToList();
        return new TrainingSearchResultDto(hits, segRows.Count > MaxHits || noteRows.Count > MaxHits);
    }

    /// <summary>
    /// Trainerkommentare an einer RookHub-Partie: alle Segmente der sichtbaren Stunden, deren Partie auf diese
    /// <see cref="SavedGame"/> zeigt, je per FEN-Vergleich einer Stellung der Partie zugeordnet. Nennt das Segment einen
    /// Zug und ist er der Partiezug aus dieser Stellung, hängt der Kommentar an der Stellung NACH dem Zug (dort, wo die
    /// Partieseite den Zug zeigt); sonst an der Stellung selbst. Wiederholt sich eine Stellung, gewinnt die erste ab dem
    /// zuletzt zugeordneten Segment (die Stunde bespricht die Partie in Zugfolge). Segmente mit <c>moveKind: analysis</c>
    /// hängen immer an der Stellung selbst (<c>MatchedBy = analysis</c>); mehrere Segmente dürfen dieselbe Stellung tragen.
    /// </summary>
    public async Task<TrainingGameCommentsDto> GameCommentsAsync(int userId, bool isAdmin, int gameId, CancellationToken ct)
    {
        var lessons = Visible(userId, isAdmin);
        var links = await _db.TrainingLessonGames
            .Where(g => g.RookhubGameId == gameId && lessons.Any(l => l.Id == g.LessonId))
            .Select(g => new { g.LessonId, g.Key, g.Lesson!.Date, g.Lesson.VideoFile })
            .ToListAsync(ct);
        if (links.Count == 0) return new TrainingGameCommentsDto(new(), 0);

        var pgn = await _db.SavedGames.Where(s => s.Id == gameId).Select(s => s.Pgn).FirstOrDefaultAsync(ct);
        var positions = Positions(pgn);
        if (positions == null) return new TrainingGameCommentsDto(new(), 0);

        var lessonIds = links.Select(l => l.LessonId).Distinct().ToList();
        var keys = links.Select(l => l.Key).Distinct().ToList();
        var segments = await _db.TrainingSegments
            .Where(s => lessonIds.Contains(s.LessonId) && s.GameKey != null && keys.Contains(s.GameKey) && s.BoardFen != null)
            .OrderBy(s => s.LessonId).ThenBy(s => s.Position)
            .ToListAsync(ct);

        var items = new List<TrainingGameCommentDto>();
        var unmatched = 0;
        foreach (var lessonGroup in segments.GroupBy(s => s.LessonId))
        {
            var link = links.First(l => l.LessonId == lessonGroup.Key);
            var last = 0;
            var any = false;
            foreach (var s in lessonGroup)
            {
                if (!links.Any(l => l.LessonId == s.LessonId && l.Key == s.GameKey)) continue;
                var match = Match(positions, s, last);
                // Eine Stellung aus einer Variante (kommt in der Partie nicht vor): an die zuletzt besprochene Partiestellung,
                // die Seite zeigt dann ihr eigenes Brett. Vor dem ersten Treffer gibt es dafür keinen Platz.
                if (match == null && any) match = (last, "variation");
                if (match == null) { unmatched++; continue; }
                any = true;
                last = match.Value.Index;
                items.Add(new TrainingGameCommentDto(match.Value.Index, match.Value.By, s.LessonId, Iso(link.Date), s.Id, s.StartSec, s.EndSec,
                    s.Fen, s.Move, s.MoveKind, s.Comment ?? s.Quote, s.Variations, Tags(s.Tags), HasVideo(link.VideoFile)));
            }
        }
        return new TrainingGameCommentsDto(items.OrderBy(i => i.FenIndex).ThenBy(i => i.Date, StringComparer.Ordinal).ThenBy(i => i.StartSec).ToList(), unmatched);
    }

    /// <summary>Pfad der Video-Datei einer sichtbaren Stunde; 404 ohne Stunde, ohne Video-Ordner oder ohne Datei.</summary>
    public async Task<string> ResolveVideoAsync(int userId, bool isAdmin, int id, CancellationToken ct)
    {
        var file = await Visible(userId, isAdmin).Where(l => l.Id == id).Select(l => l.VideoFile).FirstOrDefaultAsync(ct);
        var path = VideoPath(file) ?? throw new NotFoundException("Video nicht gefunden.");
        return path;
    }

    // ---------------------------------------------------------------- Nachspielen + Fragen-Satz

    /// <summary>Eigener Stand an einer Stellung speichern (letzter Versuch zählt; <c>Plays</c> zählt mit).</summary>
    public async Task<TrainingProgressDto> SetProgressAsync(int userId, bool isAdmin, int lessonId, int segmentId,
        TrainingProgressUpdateDto dto, CancellationToken ct)
    {
        if (dto.Status is not ("solved" or "wrong" or "skipped"))
            throw new DomainValidationException("Status muss solved, wrong oder skipped sein.");
        var seg = await _db.TrainingSegments
            .Where(s => s.Id == segmentId && s.LessonId == lessonId && Visible(userId, isAdmin).Any(l => l.Id == s.LessonId))
            .Select(s => new { s.StartSec, s.BoardFen })
            .FirstOrDefaultAsync(ct) ?? throw new NotFoundException("Stellung nicht gefunden.");
        var key = SegmentKey(seg.StartSec, seg.BoardFen);
        var row = await _db.TrainingSegmentProgress.FirstOrDefaultAsync(p => p.UserId == userId && p.LessonId == lessonId && p.SegmentKey == key, ct);
        if (row == null)
        {
            row = new TrainingSegmentProgress { UserId = userId, LessonId = lessonId, SegmentKey = key };
            _db.TrainingSegmentProgress.Add(row);
        }
        row.Status = dto.Status;
        row.Attempts = Math.Clamp(dto.Attempts, 0, 99);
        row.Plays++;
        row.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return new TrainingProgressDto(row.Status, row.Attempts, row.Plays, row.UpdatedAt);
    }

    /// <summary>Stundenübergreifender Satz: alle Segmente mit Frage (gültige Stellung), neueste Stunde zuerst, in
    /// Zeitfolge; <paramref name="onlyWrong"/> = nur zuletzt falsch beantwortete.</summary>
    public async Task<List<TrainingDrillItemDto>> QuestionSetAsync(int userId, bool isAdmin, bool onlyWrong, CancellationToken ct)
    {
        var lessons = Visible(userId, isAdmin);
        var rows = await _db.TrainingSegments.AsNoTracking()
            .Where(s => s.Question != null && s.Fen != null && lessons.Any(l => l.Id == s.LessonId))
            .Include(s => s.Question).Include(s => s.Lesson)
            .OrderByDescending(s => s.Lesson!.Date).ThenBy(s => s.Position)
            .ToListAsync(ct);
        var lessonIds = rows.Select(r => r.LessonId).Distinct().ToList();
        var progress = await ProgressAsync(userId, lessonIds, ct);
        var labels = await GameLabelsAsync(lessonIds, ct);
        var positions = await GamePositionsAsync(lessonIds, ct);
        var gameIds = await _db.TrainingLessonGames.Where(g => lessonIds.Contains(g.LessonId) && g.RookhubGameId != null)
            .Select(g => new { g.LessonId, g.Key, g.RookhubGameId }).ToListAsync(ct);
        var gameIdOf = gameIds.ToDictionary(g => (g.LessonId, g.Key), g => g.RookhubGameId);
        var items = rows.Select(s => new TrainingDrillItemDto(s.LessonId, Iso(s.Lesson!.Date), HasVideo(s.Lesson.VideoFile),
            Label(labels, s.LessonId, s.GameKey),
            s.GameKey != null && gameIdOf.TryGetValue((s.LessonId, s.GameKey), out var gid) ? gid : null,
            ToDto(s, progress, positions))).ToList();
        return onlyWrong ? items.Where(i => i.Segment.Progress?.Status == "wrong").ToList() : items;
    }

    /// <summary>Stabiler Schlüssel einer Stellung über Re-Importe hinweg.</summary>
    public static string SegmentKey(int startSec, string? boardFen) =>
        startSec.ToString(CultureInfo.InvariantCulture) + "|" + (boardFen ?? string.Empty);

    private async Task<Dictionary<(int, string), TrainingSegmentProgress>> ProgressAsync(int userId, List<int> lessonIds, CancellationToken ct)
    {
        if (lessonIds.Count == 0) return new();
        var rows = await _db.TrainingSegmentProgress.AsNoTracking()
            .Where(p => p.UserId == userId && lessonIds.Contains(p.LessonId)).ToListAsync(ct);
        return rows.ToDictionary(p => (p.LessonId, p.SegmentKey));
    }

    /// <summary>Stellungen der Partien der Stunden: die RookHub-Partie (nur wenn sie dem Konto der Stunde gehört), sonst das
    /// PGN der Stunde. Partien ohne beides fehlen.</summary>
    private async Task<Dictionary<(int, string), GamePositions>> GamePositionsAsync(List<int> lessonIds, CancellationToken ct)
    {
        if (lessonIds.Count == 0) return new();
        var games = await _db.TrainingLessonGames.Where(g => lessonIds.Contains(g.LessonId))
            .Select(g => new { g.LessonId, g.Key, g.Pgn, g.RookhubGameId, Owner = g.Lesson!.UserId }).ToListAsync(ct);
        var ids = games.Where(g => g.RookhubGameId != null).Select(g => g.RookhubGameId!.Value).Distinct().ToList();
        var saved = ids.Count == 0 ? new() : await _db.SavedGames.Where(x => ids.Contains(x.Id))
            .Select(x => new { x.Id, x.UserId, x.Pgn }).ToDictionaryAsync(x => x.Id, ct);
        var result = new Dictionary<(int, string), GamePositions>();
        foreach (var g in games)
        {
            var pgn = g.RookhubGameId is int id && saved.TryGetValue(id, out var sg) && sg.UserId == g.Owner ? sg.Pgn : g.Pgn;
            if (Positions(pgn) is { } pos) result[(g.LessonId, g.Key)] = pos;
        }
        return result;
    }

    private static TrainingSegmentDto ToDto(TrainingSegment s, Dictionary<(int, string), TrainingSegmentProgress> progress,
        Dictionary<(int, string), GamePositions> positions)
    {
        progress.TryGetValue((s.LessonId, SegmentKey(s.StartSec, s.BoardFen)), out var p);
        var game = s.GameKey != null && positions.TryGetValue((s.LessonId, s.GameKey), out var gp) ? gp : null;
        var task = DrillTask(s, game);
        return new TrainingSegmentDto(s.Id, s.StartSec, s.EndSec, s.GameKey, s.Fen, s.Move, s.MoveKind, s.Comment, s.Quote,
            s.Variations, Tags(s.Tags), s.Question != null, s.QuoteStartSec, s.QuoteEndSec,
            s.Question == null ? null : new TrainingQuestionDto(s.Question.Prompt, s.Question.Answer, s.Question.AnswerLine,
                s.Question.AnswerSec, s.Question.Explanation),
            p == null ? null : new TrainingProgressDto(p.Status, p.Attempts, p.Plays, p.UpdatedAt),
            task?.Fen, task?.Move);
    }

    // ---------------------------------------------------------------- Zuordnung Segment → Stellung der Partie

    /// <summary>Stellungen der Partie (Index 0 = Anfangsstellung, n = nach dem n-ten Halbzug) samt dem Zug AUS jeder Stellung.</summary>
    internal static GamePositions? Positions(string? pgn)
    {
        var parsed = GamePlies.Parse(pgn, maxPlies: 600);
        if (parsed == null) return null;
        var plies = parsed.Value.Plies;
        var boards = new List<string>(plies.Count + 1);
        var sides = new List<char>(plies.Count + 1);
        var sans = new List<string?>(plies.Count + 1);
        var fens = new List<string>(plies.Count + 1);
        foreach (var p in plies)
        {
            fens.Add(p.Fen);
            boards.Add(TrainingBoardKey.Of(p.Fen)!);
            sides.Add(TrainingBoardKey.Side(p.Fen) ?? 'w');
            sans.Add(p.San);
        }
        // Endstellung: letzten Zug ausführen.
        try
        {
            var lastPly = plies[^1];
            var board = ChessBoard.LoadFromFen(lastPly.Fen);
            var legal = Array.Find(board.Moves(generateSan: true), m => GamePlies.ToUci(m) == lastPly.Uci);
            if (legal != null)
            {
                board.Move(legal);
                var fen = board.ToFen();
                fens.Add(fen);
                boards.Add(TrainingBoardKey.Of(fen)!);
                sides.Add(TrainingBoardKey.Side(fen) ?? 'w');
                sans.Add(null);
            }
        }
        catch { /* ohne Endstellung weiter */ }
        return new GamePositions(boards, sides, sans, fens);
    }

    internal static (int Index, string By)? Match(GamePositions game, TrainingSegment s, int from)
    {
        var candidates = new List<int>();
        for (var k = 0; k < game.Boards.Count; k++)
            if (game.Boards[k] == s.BoardFen) candidates.Add(k);
        if (candidates.Count == 0) return null;
        var ordered = candidates.Where(k => k >= from).Concat(candidates.Where(k => k < from)).ToList();
        var move = SanKey(s.Move);
        // Ein Analysezug gehört als Variante an die Stellung selbst, nie an einen Partiezug (moveKind „analysis“).
        if (s.MoveKind == "analysis")
        {
            var analysisSide = TrainingBoardKey.Side(s.Fen);
            foreach (var k in ordered)
                if (analysisSide == null || game.Sides[k] == analysisSide) return (k, "analysis");
            return (ordered[0], "analysis");
        }
        if (move != null)
        {
            // Der genannte Zug wird AUS dieser Stellung gespielt → Kommentar an der Stellung nach dem Zug.
            foreach (var k in ordered)
                if (SanKey(game.SanFrom[k]) == move) return (k + 1, "move");
            // Oder die FEN zeigt schon die Stellung NACH dem genannten Zug.
            foreach (var k in ordered)
                if (k > 0 && SanKey(game.SanFrom[k - 1]) == move) return (k, "move");
        }
        var side = TrainingBoardKey.Side(s.Fen);
        foreach (var k in ordered)
            if (side == null || game.Sides[k] == side) return (k, "position");
        return (ordered[0], "position");
    }

    /// <summary>Stellungen einer Partie: Figurenfeld, Zugrecht, volle FEN und der Zug AUS jeder Stellung (Endstellung: null).</summary>
    internal sealed record GamePositions(List<string> Boards, List<char> Sides, List<string?> SanFrom, List<string> Fens);

    /// <summary>
    /// Die Aufgabe eines Segments im Nachspielen: Stellung VOR dem gesuchten Zug + der Zug (SAN des Bretts); <c>null</c> =
    /// keine Aufgabe (nur ansehen). Gesucht ist die Antwort der Frage, sonst der gespielte Partiezug (<c>moveKind
    /// played</c>). Die FEN der Aufbereitung zeigt teils die Stellung NACH dem Zug: dann liefert die Partie (RookHub-Partie
    /// bzw. PGN der Stunde) die Stellung davor. Eine Frage wird zuerst aus ihrer eigenen Stellung gelesen (die Antwort ist
    /// oft kein Partiezug), ein Partiezug zuerst aus der Partie.
    /// </summary>
    internal static (string Fen, string Move)? DrillTask(TrainingSegment s, GamePositions? game)
    {
        var question = s.Question?.Answer;
        var played = s.MoveKind == "played" ? s.Move : null;
        var target = SanKey(question ?? played);
        if (target == null || s.Fen == null) return null;
        if (question != null && LegalFrom(s.Fen, target) is { } q) return (s.Fen, q);
        if (game != null && s.BoardFen != null)
        {
            for (var k = 0; k < game.Boards.Count; k++)
            {
                if (game.Boards[k] != s.BoardFen) continue;
                if (SanKey(game.SanFrom[k]) == target) return (game.Fens[k], game.SanFrom[k]!);
                if (k > 0 && SanKey(game.SanFrom[k - 1]) == target) return (game.Fens[k - 1], game.SanFrom[k - 1]!);
            }
        }
        return LegalFrom(s.Fen, target) is { } m ? (s.Fen, m) : null;
    }

    /// <summary>Der legale Zug zu <paramref name="sanKey"/> aus <paramref name="fen"/> (SAN des Bretts) oder <c>null</c>.</summary>
    private static string? LegalFrom(string fen, string sanKey)
    {
        try
        {
            var board = ChessBoard.LoadFromFen(fen);
            var hit = Array.Find(board.Moves(generateSan: true), m => SanKey(m.San) == sanKey);
            return hit?.San;
        }
        catch { return null; }
    }

    /// <summary>SAN ohne Schach-/Wertungszeichen, Rochade mit O, für den Vergleich.</summary>
    internal static string? SanKey(string? san)
    {
        if (string.IsNullOrWhiteSpace(san)) return null;
        var sb = new StringBuilder();
        var text = san.Trim();
        var dot = text.LastIndexOf('.');
        if (dot >= 0) text = text[(dot + 1)..];      // „12...Nf3“ → „Nf3“
        foreach (var c in text)
        {
            if (c is '+' or '#' or '!' or '?' or ' ' or '=' or '…') continue;
            sb.Append(c == '0' ? 'O' : c);
        }
        return sb.Length == 0 ? null : sb.ToString();
    }

    // ---------------------------------------------------------------- Helfer

    private bool HasVideo(string? file) => VideoPath(file) != null;

    private string? VideoPath(string? file)
    {
        if (string.IsNullOrWhiteSpace(file) || string.IsNullOrWhiteSpace(_options.VideoRoot)) return null;
        if (Path.GetFileName(file) != file) return null;
        var root = Path.GetFullPath(_options.VideoRoot);
        var path = Path.GetFullPath(Path.Combine(root, file));
        if (!path.StartsWith(root, StringComparison.Ordinal)) return null;
        return File.Exists(path) ? path : null;
    }

    private async Task<Dictionary<(int, string), string>> GameLabelsAsync(List<int> lessonIds, CancellationToken ct)
    {
        if (lessonIds.Count == 0) return new();
        var games = await _db.TrainingLessonGames.Where(g => lessonIds.Contains(g.LessonId))
            .Select(g => new { g.LessonId, g.Key, g.White, g.Black }).ToListAsync(ct);
        return games.ToDictionary(g => (g.LessonId, g.Key), g => $"{g.White ?? "?"} – {g.Black ?? "?"}");
    }

    private static string? Label(Dictionary<(int, string), string> labels, int lessonId, string? key) =>
        key != null && labels.TryGetValue((lessonId, key), out var l) ? l : null;

    private static string? PickText(string? comment, string? quote, List<string> words)
    {
        if (comment != null && words.Any(w => comment.Contains(w, StringComparison.OrdinalIgnoreCase))) return comment;
        if (quote != null && words.Any(w => quote.Contains(w, StringComparison.OrdinalIgnoreCase))) return quote;
        return comment ?? quote;
    }

    /// <summary>Suchwörter: Buchstaben/Ziffern, mindestens drei Zeichen (InnoDB-Mindestlänge), klein, höchstens acht.</summary>
    /// <summary>Buchstaben und Ziffern im Suchbegriff (wie das Frontend zählt: <c>/[\p{L}\p{N}]/</c>).</summary>
    internal static int LetterCount(string? query) => query?.Count(char.IsLetterOrDigit) ?? 0;

    internal static List<string> Words(string? query, int minLength = 3)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        var words = new List<string>();
        var sb = new StringBuilder();
        foreach (var c in query + " ")
        {
            if (char.IsLetterOrDigit(c)) { sb.Append(char.ToLowerInvariant(c)); continue; }
            if (sb.Length >= minLength && !words.Contains(sb.ToString())) words.Add(sb.ToString());
            sb.Clear();
        }
        return words.Take(8).ToList();
    }

    private static List<string> Tags(string? tags) =>
        string.IsNullOrEmpty(tags) ? new() : tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static TrainingGameDto ToDto(TrainingLessonGame g) => new(g.Key, g.Kind, g.White, g.Black, g.Event, g.Year, g.Eco,
        g.Result, g.FromSec, g.ToSec, g.RookhubGameId, g.MatchConfidence, g.Pgn != null);
}
