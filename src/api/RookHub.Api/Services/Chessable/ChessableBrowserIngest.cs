using RookHub.Api.DTOs.Chessable;

namespace RookHub.Api.Services.Chessable;

/// <summary>Wie der Controller einen Chunk beantwortet (Statuscode-Wahl bleibt im Controller).</summary>
public enum ChessableChunkOutcomeKind
{
    /// <summary>200 mit <see cref="ChessableChunkOutcome.Ack"/> (Sitzung läuft weiter bzw. Abbruch quittiert).</summary>
    Ack,
    /// <summary>200 mit <see cref="ChessableChunkOutcome.Result"/> (finaler Chunk, Sitzung abgeschlossen).</summary>
    Done,
    /// <summary>400 mit <see cref="ChessableChunkOutcome.Message"/>.</summary>
    BadRequest,
    /// <summary>403: Linien aus dem geteilten Cache für einen nicht bestätigten Kurs verlangt.</summary>
    CacheFillForbidden,
    /// <summary>Parser-Fehler von piratechess — der Controller übersetzt <see cref="ChessableChunkOutcome.ProxyError"/>.</summary>
    ProxyError,
}

/// <summary>Ergebnis von <see cref="ChessableImportService.HandleIngestChunkAsync"/>.</summary>
public sealed record ChessableChunkOutcome(
    ChessableChunkOutcomeKind Kind,
    ChessableIngestChunkAck? Ack = null,
    ChessableIngestResultDto? Result = null,
    string? Message = null,
    ChessableProxyException? ProxyError = null);

/// <summary>
/// Ablauf eines Chunks des kapitelweisen Browser-Imports (<c>POST /api/extension/chessable/ingest/chunk</c>): Sitzung
/// holen/anlegen, Kapitel prüfen, parsen, Kapitelnummern fortschreiben, anhängen, beim finalen Chunk abschließen.
/// Stand bis Codereview A3-012 im <c>ExtensionController</c>; Teil von <see cref="ChessableImportService"/>
/// (Zerlegung Z3). Die Sitzungsablage (Singleton) und der Logger des Controllers kommen je Aufruf herein, damit
/// weder die DI-Registrierung noch die Logquelle der Meldungen sich ändern.
/// </summary>
internal sealed class ChessableBrowserIngest
{
    private readonly ChessableBrowserImport _browserImport;
    private readonly ChessableProxyService _proxy;

    public ChessableBrowserIngest(ChessableBrowserImport browserImport, ChessableProxyService proxy)
    {
        _browserImport = browserImport;
        _proxy = proxy;
    }

    private static ChessableChunkOutcome Bad(string? message) => new(ChessableChunkOutcomeKind.BadRequest, Message: message);

    public async Task<ChessableChunkOutcome> HandleChunkAsync(ChessableIngestSessionStore sessions, int userId,
        ChessableIngestChunkRequest dto, ILogger log, CancellationToken ct)
    {
        var target = ChessableImportTargets.Normalize(dto.Target);

        // Abbruch aus dem Browser (Chessable-Sperre, Nutzer stoppt): Sitzung schliessen und den Datensatz
        // mit der Bilanz des Erreichten als abgebrochen markieren — statt bis zum Ablauf der Sitzung
        // (30 min) auf „laeuft" zu stehen. Die Linien selbst sind laengst importiert und bleiben.
        if (dto.Aborted)
        {
            var dropped = sessions.Discard(userId, dto.SessionId);
            if (dropped?.ImportId is int abortedId)
                await _browserImport.FailBrowserImportAsync(abortedId,
                    $"Im Browser abgebrochen — {dropped.ChaptersDone} Kapitel, {dropped.Imported} Linien übernommen.", ct);
            return new(ChessableChunkOutcomeKind.Ack,
                Ack: new ChessableIngestChunkAck(true, dropped?.ChaptersDone ?? 0, dropped?.LinesSeen ?? 0, dropped?.Imported ?? 0));
        }

        // Kursname aus den Linien selbst (game.name) vor dem der Extension — siehe ChessableLineJson.
        var courseName = ChessableLineJson.ResolveCourseName(dto.CourseName, dto.Chapter?.Lines);
        var (session, storeError) = sessions.GetOrCreate(userId, dto.SessionId, dto.Bid, target, courseName);
        if (session is null)
        {
            // Abgelehnte Chunks waren bisher nur als nackter 400 im Zugriffslog zu sehen — der Grund stand
            // ausschliesslich im Quelltext (am 2026-09-19 die halbe Fehlersuche gekostet).
            log.LogWarning("Browser-Import abgelehnt (User {UserId}, bid {Bid}): {Reason}", userId, dto.Bid, storeError);
            return Bad(storeError);
        }

        // Kapitel SOFORT parsen und anhängen (ein finaler Chunk darf leer sein, wenn das letzte Kapitel
        // schon zuvor kam). Nichts wird zwischengelagert: was geholt ist, steht danach in der Datenbank.
        if (dto.Chapter is { } chapter && (chapter.Lines?.Count ?? 0) > 0)
        {
            var stop = await ImportChapterAsync(sessions, session, userId, dto, chapter, target, courseName, log, ct);
            if (stop is not null) return stop;
        }

        if (!dto.Final)
            return new(ChessableChunkOutcomeKind.Ack,
                Ack: new ChessableIngestChunkAck(false, session.ChaptersDone, session.LinesSeen, session.Imported));

        // Final: Sitzung schliessen. Das Importierte steht laengst in der Datenbank.
        var taken = sessions.Take(userId, dto.SessionId);
        if (taken?.ImportId is not int importId)
            return Bad("No captured lines in session.");

        var done = await _browserImport.FinishBrowserImportAsync(importId, ct);
        if (done is null) return Bad("Import record vanished.");
        return new(ChessableChunkOutcomeKind.Done, Result: new ChessableIngestResultDto(done.Id, done.Target, done.ResultId,
            done.CourseName, done.Imported, done.Skipped, done.Invalid, taken.LinesSeen));
    }

    /// <summary>Ein Kapitel prüfen, parsen und anhängen. <c>null</c> = weiter; sonst die Antwort, mit der der Chunk endet.</summary>
    private async Task<ChessableChunkOutcome?> ImportChapterAsync(ChessableIngestSessionStore sessions,
        ChessableIngestSessionStore.Session session, int userId, ChessableIngestChunkRequest dto,
        ChessableIngestChapter chapter, string target, string? courseName, ILogger log, CancellationToken ct)
    {
        if (ChessableIngestRules.ValidateLineOids(new[] { chapter }) is { } shapeError)
        {
            var dropped = sessions.Discard(userId, dto.SessionId);
            if (dropped?.ImportId is int brokenId)
                await _browserImport.FailBrowserImportAsync(brokenId,
                    $"Ungültiger Chunk ({shapeError}) — {dropped.ChaptersDone} Kapitel, {dropped.Imported} Linien übernommen.", ct);
            return Bad(shapeError);
        }
        if (ChessableIngestRules.WantsCacheFill(new[] { chapter }) && !await _browserImport.HasVerifiedCourseAsync(userId, dto.Bid, ct))
        {
            var dropped = sessions.Discard(userId, dto.SessionId);
            if (dropped?.ImportId is int refusedId)
                await _browserImport.FailBrowserImportAsync(refusedId,
                    $"Linien aus dem geteilten Cache für einen nicht bestätigten Kurs verlangt — {dropped.ChaptersDone} Kapitel, {dropped.Imported} Linien übernommen.", ct);
            return new(ChessableChunkOutcomeKind.CacheFillForbidden);
        }

        ChessableCourseDataDto parsed;
        try
        {
            parsed = await ParseWithBackoffAsync(dto, target, chapter, ct);
        }
        catch (ChessableProxyException ex)
        {
            log.LogWarning(ex, "Browser-Import: Parser-Fehler (User {UserId}, bid {Bid})", userId, dto.Bid);
            return new(ChessableChunkOutcomeKind.ProxyError, ProxyError: ex);
        }

        if (!string.IsNullOrWhiteSpace(parsed.Pgn))
            await AppendChapterAsync(sessions, session, userId, dto, parsed, target, courseName, ct);
        return null;
    }

    /// <summary>Wartezeiten nach einem 429 des Parsers (I2-005): piratechess drosselt mit einem Fenster für ganz
    /// rookhub und erwartet Backoff — vorher ging der Chunk sofort als 502 an den Browser.</summary>
    private static readonly int[] ParseRetryDelaysMs = { 1000, 3000 };

    private async Task<ChessableCourseDataDto> ParseWithBackoffAsync(ChessableIngestChunkRequest dto, string target,
        ChessableIngestChapter chapter, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await _proxy.ParseCourseAsync(dto.Bid, ChessableImportTargets.ParseMode(target),
                    new[] { chapter }, dto.CourseJson, dto.Complete, ct);
            }
            catch (ChessableProxyException ex) when (ex.Status == System.Net.HttpStatusCode.TooManyRequests
                                                     && attempt < ParseRetryDelaysMs.Length)
            {
                await Task.Delay(ParseRetryDelaysMs[attempt], ct);
            }
        }
    }

    private async Task AppendChapterAsync(ChessableIngestSessionStore sessions, ChessableIngestSessionStore.Session session,
        int userId, ChessableIngestChunkRequest dto, ChessableCourseDataDto parsed, string target, string? courseName,
        CancellationToken ct)
    {
        if (session.ImportId is null)
        {
            var (import, inflight) = await _browserImport.StartBrowserImportAsync(
                userId, dto.Bid, courseName ?? parsed.Name, target, ct);
            sessions.AttachImport(session, import.Id, inflight);
        }

        // Kapitelnummern nahtlos hinter den bisherigen fortschreiben — sonst überschriebe Chunk 2 die
        // Linien von Chunk 1 (LineId = Datei:Round). Siehe ChessableRoundOffset.
        //
        // Ein Kapitel, dessen Linien zusammen über dem Body-Limit liegen, kommt in MEHREREN Chunks mit
        // demselben ChapterKey an (RepCheck ≥ 1.61.0). Eine solche Fortsetzung bleibt in derselben
        // Kapitelnummer und setzt nur die Liniennummer fort: der Parser wirft die Einträge ohne Inhalt
        // aus list.data und begänne sonst wieder bei .002 — also genau auf den Linien von Teil 1.
        var continues = dto.ChapterKey is { Length: > 0 }
            && string.Equals(dto.ChapterKey, session.LastChapterKey, StringComparison.Ordinal)
            && session.CurrentChapter > 0;
        // Erster Chunk einer Sitzung, die der Server nicht kennt — nach einem API-Neustart mitten im Import
        // ist der Versatz im Arbeitsspeicher weg. Träfe der Chunk mit Versatz 0 auf schon importierte
        // Kapitel, setzt er hinter dem Buch fort (ChessableBrowserImport.ResumeChapterOffsetAsync). Nicht bei
        // einem Teil-Import: dort bekommt eine kollidierende neue Linie ohnehin einen freien Platz.
        var previousMaxChapter = session.ChapterOffset;
        if (!continues && session.ChaptersDone == 0 && previousMaxChapter == 0 && target == "book" && !dto.Partial)
            previousMaxChapter = await _browserImport.ResumeChapterOffsetAsync(userId, dto.Bid, parsed.Pgn!, ct);
        var chapterShift = continues
            ? ChessableRoundOffset.SameChapterOffset(session.CurrentChapter, parsed.Pgn)
            : ChessableRoundOffset.NextOffset(previousMaxChapter, parsed.Pgn);
        var shifted = ChessableRoundOffset.Shift(parsed.Pgn!, chapterShift,
            continues ? ChessableRoundOffset.NextLineOffset(session.MaxLineInChapter, parsed.Pgn) : 0);
        var res = await _browserImport.AppendBrowserChunkAsync(
            session.ImportId!.Value, shifted, parsed.LineCount, dto.Partial, ct);
        sessions.NoteChapter(session, ChessableRoundOffset.MaxChapter(shifted),
            parsed.LineCount, res.Imported, res.ResultId,
            dto.ChapterKey, continues, ChessableRoundOffset.MaxLine(shifted));
    }
}
