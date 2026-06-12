using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Führt einen asynchronen Chessable-Kurs-Import durch: holt den Kurs (tief) von piratechess
/// und legt ihn entweder als persönliches Repertoire (PGN, Mode "None") oder als persönliches
/// Buch (Puzzles, Mode "FirstKeyMove" → erster Key-Zug trainierbar) für den User an. Läuft im
/// Hintergrund-Worker; der Fortschritt steht im <see cref="ChessableImport"/>-Satz.
/// </summary>
public class ChessableImportService
{
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

    /// <summary>Verarbeitet den Import-Job <paramref name="importId"/> (im Hintergrund-Scope).</summary>
    public async Task RunAsync(int importId, CancellationToken ct = default)
    {
        var import = await _db.ChessableImports.FirstOrDefaultAsync(i => i.Id == importId, ct);
        if (import is null)
        {
            _logger.LogWarning("ChessableImport {Id} nicht gefunden", importId);
            return;
        }

        try
        {
            var cred = await _db.ChessableCredentials.FirstOrDefaultAsync(c => c.UserId == import.UserId, ct);
            if (cred is null)
            {
                await FailAsync(import, "Kein Chessable-Bearer gespeichert", ct);
                return;
            }
            var bearer = _encryption.Decrypt(cred.EncryptedBearer);

            var mode = import.Target == "book" ? "FirstKeyMove" : "None";
            var course = await _proxy.FetchCourseAsync(bearer, import.Bid, mode, ct);

            var courseName = !string.IsNullOrWhiteSpace(import.CourseName)
                ? import.CourseName
                : (!string.IsNullOrWhiteSpace(course.Name) ? course.Name : $"Chessable {import.Bid}");
            import.CourseName = courseName;

            if (import.Target == "repertoire")
                await ImportAsRepertoireAsync(import, course, courseName, ct);
            else
                await ImportAsBookAsync(import, course, courseName, ct);

            import.Status = "completed";
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
            _logger.LogWarning(ex, "Chessable-Import {Id} fehlgeschlagen", import.Id);
            await FailAsync(import, ex.Message, ct);
        }
    }

    private async Task ImportAsRepertoireAsync(ChessableImport import, ChessableCourseDataDto course, string courseName, CancellationToken ct)
    {
        var rep = await _repertoires.CreateAsync(import.UserId, new CreateRepertoireDto
        {
            Name = courseName.Length > 200 ? courseName[..200] : courseName,
            Description = $"Aus Chessable importiert (bid {import.Bid})",
            Kind = RepertoireKind.Opening,
            IsPublic = false
        });

        var fileName = $"chessable-{import.Bid}.pgn";
        using var ms = new MemoryStream(Encoding.UTF8.GetBytes(course.Pgn));
        await _repertoires.UploadFileAsync(rep.Id, import.UserId, fileName, ms);

        import.ResultId = rep.Id;
        import.Imported = course.LineCount;
        import.Skipped = 0;
        import.Invalid = 0;
    }

    private async Task ImportAsBookAsync(ChessableImport import, ChessableCourseDataDto course, string courseName, CancellationToken ct)
    {
        // Pro-User-eindeutiger Dateiname, damit derselbe Kurs mehrerer User nicht kollidiert.
        var fileName = $"chessable-u{import.UserId}-{import.Bid}.pgn";
        var res = await _pgnImport.ImportFileAsync(fileName, course.Pgn, ct);

        // Das gerade angelegte Buch zu einem persönlichen Buch des Users machen + Anzeigenamen setzen.
        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == res.BookId, ct);
        if (book is not null)
        {
            book.OwnerUserId = import.UserId;
            book.DisplayName = courseName.Length > 200 ? courseName[..200] : courseName;
            book.Tags = "chessable";
            book.UpdatedAt = DateTime.UtcNow;
        }

        import.ResultId = res.BookId;
        import.Imported = res.Imported;
        import.Skipped = res.Skipped;
        import.Invalid = res.Invalid;
    }

    private async Task FailAsync(ChessableImport import, string error, CancellationToken ct)
    {
        import.Status = "failed";
        import.Error = error.Length > 1000 ? error[..1000] : error;
        import.CompletedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
    }
}
