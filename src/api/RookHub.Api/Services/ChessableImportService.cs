using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Führt einen asynchronen Chessable-Kurs-Import durch: holt den Kurs (tief) von piratechess
/// und legt ihn entweder als persönliches Repertoire (PGN, Mode "None") oder als persönliches
/// Buch (Puzzles, Mode "FirstKeyMove" → erster Key-Zug trainierbar) für den User an.
///
/// Robust gegen Neustarts: der Fortschritt wird in der DB gecheckpointet (Phase + bereits
/// geholtes PGN + ResultId). Ein nach einem Crash/Deploy resumter Job überspringt den teuren
/// Chessable-Fetch (PGN ist persistiert) und legt nichts doppelt an (idempotente Schritte).
/// </summary>
public class ChessableImportService
{
    public const int MaxAttempts = 3;

    private readonly AppDbContext _db;
    private readonly EncryptionService _encryption;
    private readonly ChessableProxyService _proxy;
    private readonly RepertoireService _repertoires;
    private readonly PgnImportService _pgnImport;
    private readonly ILogger<ChessableImportService> _logger;

    public ChessableImportService(
        AppDbContext db,
        EncryptionService encryption,
        ChessableProxyService proxy,
        RepertoireService repertoires,
        PgnImportService pgnImport,
        ILogger<ChessableImportService> logger)
    {
        _db = db;
        _encryption = encryption;
        _proxy = proxy;
        _repertoires = repertoires;
        _pgnImport = pgnImport;
        _logger = logger;
    }

    /// <summary>Verarbeitet (oder resumt) den Import-Job <paramref name="importId"/>.</summary>
    public async Task RunAsync(int importId, CancellationToken ct = default)
    {
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is null)
        {
            _logger.LogWarning("ChessableImport {Id} nicht gefunden", importId);
            return;
        }
        if (import.Status != "running")
            return; // bereits abgeschlossen oder fehlgeschlagen — nichts zu tun

        import.Attempts++;
        if (import.Attempts > MaxAttempts)
        {
            await FailAsync(import, $"Abgebrochen nach {MaxAttempts} Versuchen", ct);
            return;
        }
        await _db.SaveChangesAsync(ct);

        try
        {
            // --- Phase 1: Kurs holen (Checkpoint: PGN wird persistiert) ---
            var pgn = import.FetchedPgn;
            if (string.IsNullOrEmpty(pgn))
            {
                var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == import.UserId, ct);
                if (cred is null)
                {
                    await FailAsync(import, "Kein Chessable-Bearer gespeichert", ct);
                    return;
                }
                var bearer = _encryption.Decrypt(cred.EncryptedBearer);
                var mode = import.Target == "book" ? "FirstKeyMove" : "None";

                import.Phase = "fetching";
                await _db.SaveChangesAsync(ct);

                // Async Job bei piratechess starten (oder bei Resume den gemerkten weiterpollen).
                if (string.IsNullOrEmpty(import.FetchJobId))
                {
                    var start = await _proxy.StartCourseFetchAsync(bearer, import.Bid, mode, ct);
                    import.FetchJobId = start.JobId;
                    await _db.SaveChangesAsync(ct);
                }

                // Poll-Schleife (max ~15 min bei 2,5 s Takt) — Fortschritt in die DB schreiben.
                for (int i = 0; i < 360 && string.IsNullOrEmpty(pgn); i++)
                {
                    // Externen Abbruch/Pause erkennen (anderer Request setzt Status) — Checkpoint bleibt erhalten.
                    await _db.Entry(import).ReloadAsync(ct);
                    if (import.Status != "running")
                    {
                        _logger.LogInformation("Chessable-Import {Id} während Hol-Phase {Status}", import.Id, import.Status);
                        return;
                    }

                    var prog = await _proxy.GetCourseProgressAsync(import.FetchJobId!, ct);
                    if (prog is null)
                    {
                        // Job weg (piratechess-Neustart) → neu starten und weiter pollen.
                        var start = await _proxy.StartCourseFetchAsync(bearer, import.Bid, mode, ct);
                        import.FetchJobId = start.JobId;
                        await _db.SaveChangesAsync(ct);
                        await Task.Delay(2500, ct);
                        continue;
                    }

                    import.ChaptersDone = prog.ChaptersDone;
                    import.ChaptersTotal = prog.ChaptersTotal;
                    import.LinesDone = prog.LinesDone;

                    if (prog.Status == "completed")
                    {
                        pgn = prog.Pgn ?? "";
                        import.LineCount = prog.LineCount;
                        if (string.IsNullOrWhiteSpace(import.CourseName))
                            import.CourseName = !string.IsNullOrWhiteSpace(prog.CourseName) ? prog.CourseName! : $"Chessable {import.Bid}";
                        import.FetchedPgn = pgn;
                        await _db.SaveChangesAsync(ct); // Checkpoint: ab hier kein erneuter Fetch nötig
                        break;
                    }
                    if (prog.Status == "failed")
                        throw new InvalidOperationException(prog.Error ?? "Kurs-Abruf fehlgeschlagen");

                    await _db.SaveChangesAsync(ct); // Fortschritt fürs Frontend sichtbar machen
                    await Task.Delay(2500, ct);
                }

                if (string.IsNullOrEmpty(pgn))
                    throw new TimeoutException("Zeitüberschreitung beim Kurs-Abruf");
            }

            var courseName = !string.IsNullOrWhiteSpace(import.CourseName) ? import.CourseName : $"Chessable {import.Bid}";

            // --- Phase 2: Import (idempotent) ---
            import.Phase = "importing";
            await _db.SaveChangesAsync(ct);

            if (import.Target == "repertoire")
                await ImportAsRepertoireAsync(import, pgn, courseName, ct);
            else
                await ImportAsBookAsync(import, pgn, courseName, ct);

            import.Status = "completed";
            import.Phase = "done";
            import.FetchedPgn = null; // Checkpoint nicht mehr nötig → Platz freigeben
            import.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation(
                "Chessable-Import {Id} fertig: {Target} '{Name}' (bid {Bid}), imported={Imported}",
                import.Id, import.Target, courseName, import.Bid, import.Imported);
        }
        catch (ChessableProxyException ex)
        {
            await FailAsync(import, ex.Message, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chessable-Import {Id} fehlgeschlagen (Versuch {Attempt})", import.Id, import.Attempts);
            await FailAsync(import, ex.Message, ct);
        }
    }

    private async Task ImportAsRepertoireAsync(ChessableImport import, string pgn, string courseName, CancellationToken ct)
    {
        // Resume: Repertoire evtl. in einem vorigen Versuch schon angelegt → nicht doppeln.
        int repId;
        if (import.ResultId is int existing
            && await _db.Repertoires.AnyAsync(r => r.Id == existing && r.UserId == import.UserId, ct))
        {
            repId = existing;
        }
        else
        {
            var rep = await _repertoires.CreateAsync(import.UserId, new CreateRepertoireDto
            {
                Name = Trunc(courseName, 200),
                Description = $"Aus Chessable importiert (bid {import.Bid})",
                Kind = RepertoireKind.Opening,
                IsPublic = false
            });
            repId = rep.Id;
            import.ResultId = repId;
            await _db.SaveChangesAsync(ct); // Checkpoint: Repertoire-Anlage
        }

        // Datei nur hochladen, wenn noch keine da ist (idempotent bei Resume).
        if (!await _db.RepertoireFiles.AnyAsync(f => f.RepertoireId == repId, ct))
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(pgn));
            await _repertoires.UploadFileAsync(repId, import.UserId, $"chessable-{import.Bid}.pgn", ms);
        }

        import.Imported = import.LineCount;
        import.Skipped = 0;
        import.Invalid = 0;
    }

    private async Task ImportAsBookAsync(ChessableImport import, string pgn, string courseName, CancellationToken ct)
    {
        // Pro-User-eindeutiger Dateiname; PgnImportService dedupliziert per LineId → von Natur aus
        // idempotent, ein Resume legt dieselben Puzzles nicht doppelt an.
        var fileName = $"chessable-u{import.UserId}-{import.Bid}.pgn";
        var res = await _pgnImport.ImportFileAsync(fileName, pgn, ct);

        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == res.BookId, ct);
        if (book is not null)
        {
            book.OwnerUserId = import.UserId;
            book.DisplayName = Trunc(courseName, 200);
            book.Tags = "chessable";
            book.UpdatedAt = DateTime.UtcNow;
        }

        import.ResultId = res.BookId;
        // Gesamtzahl der Puzzles im Buch (korrekt auch beim Resume, wo res.Imported nur die neuen zählt).
        import.Imported = await _db.BookPuzzles.CountAsync(bp => bp.BookId == res.BookId, ct);
        import.Skipped = res.Skipped;
        import.Invalid = res.Invalid;
    }

    private async Task FailAsync(ChessableImport import, string error, CancellationToken ct)
    {
        import.Status = "failed";
        import.Error = Trunc(error, 1000);
        import.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }

    private static string Trunc(string s, int max) => s.Length > max ? s[..max] : s;
}
