using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs.Repertoires;
using RookHub.Api.Models;
using RookHub.Api.Services.Courses;
using RookHub.Api.Services.Repertoires;

namespace RookHub.Api.Services.Chessable;

/// <summary>
/// Schreibt ein fertiges Kurs-PGN ins Ziel eines Import-Datensatzes — Buch (Puzzles, dedupliziert per LineId) oder
/// Repertoire (in-place ersetzt bzw. neu angelegt, Geschwister desselben Kurses mitgezogen). Gemeinsames
/// Import-Primitiv von Server-Abruf und Browser-Import; Teil von
/// <see cref="ChessableImportService"/> (Zerlegung Z3).
/// </summary>
internal sealed class ChessableTargetWriter
{
    private readonly AppDbContext _db;
    private readonly RepertoireService _repertoires;
    private readonly PgnImportService _pgnImport;

    public ChessableTargetWriter(AppDbContext db, RepertoireService repertoires, PgnImportService pgnImport)
    {
        _db = db;
        _repertoires = repertoires;
        _pgnImport = pgnImport;
    }

    public async Task ImportAsRepertoireAsync(ChessableImport import, string pgn, string courseName, CancellationToken ct)
    {
        // In-place-Re-Import (Reprocess-Re-Fetch): das frische PGN ersetzt ein BESTEHENDES Repertoire,
        // damit dessen Id und damit der Trainings-Fortschritt erhalten bleiben.
        if (import.TargetRepertoireId is int target
            && await _db.Repertoires.AnyAsync(r => r.Id == target && r.UserId == import.UserId, ct))
        {
            await ReplaceRepertoireFileAsync(target, import.UserId, ChessableImportTargets.RepertoireFileName(import.Bid), pgn, ct);
            import.ResultId = target;
            import.Imported = import.LineCount;
            import.Skipped = 0;
            import.Invalid = 0;
            // Mehrere Repertoires desselben Users können auf demselben Chessable-Kurs (bid) beruhen. Der
            // (Owner,bid)-Dedup holt den Kurs aber nur EINMAL → die Geschwister blieben sonst veraltet.
            // Darum das frische PGN auch in alle Geschwister-Repertoires desselben bid schreiben.
            await BumpSiblingRepertoiresAsync(import, pgn, target, ct);
            return;
        }

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
                Name = ChessableImportTargets.Trunc(courseName, 200),
                Description = $"Aus Chessable importiert (bid {import.Bid})",
                Kind = RepertoireKind.Opening,
                IsPublic = false,
                // Importierte Kurse standardmäßig NICHT von der RepCheck-Extension verwenden.
                UseForExtension = false
            });
            repId = rep.Id;
            import.ResultId = repId;
            await _db.SaveChangesAsync(ct); // Checkpoint: Repertoire-Anlage
        }

        // Datei nur hochladen, wenn noch keine da ist (idempotent bei Resume).
        if (!await _db.RepertoireFiles.AnyAsync(f => f.RepertoireId == repId, ct))
        {
            using var ms = new MemoryStream(Encoding.UTF8.GetBytes(pgn));
            await _repertoires.UploadFileAsync(repId, import.UserId, ChessableImportTargets.RepertoireFileName(import.Bid), ms);
        }

        import.Imported = import.LineCount;
        import.Skipped = 0;
        import.Invalid = 0;
    }

    /// <summary>Ersetzt das PGN eines bestehenden Repertoires IN-PLACE (Id/Trainings-Fortschritt
    /// bleiben): erst die neue Datei hochladen, DANN die alten löschen — die Reihenfolge ist die
    /// Invariante „nie ein Zeitfenster mit 0 Dateien" und lag vorher als Copy-Paste in Ziel- und
    /// Geschwister-Pfad (eine Änderung an Ordnung/Fehlerbehandlung hätte nur einen Pfad erreicht).</summary>
    private async Task ReplaceRepertoireFileAsync(int repId, int userId, string fileName, string pgn, CancellationToken ct)
    {
        var oldIds = await _db.RepertoireFiles.Where(f => f.RepertoireId == repId).Select(f => f.Id).ToListAsync(ct);
        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(pgn)))
            await _repertoires.UploadFileAsync(repId, userId, fileName, ms); // hebt ImportVersion + ChessableCourseId
        if (oldIds.Count > 0)
        {
            var olds = await _db.RepertoireFiles.Where(f => oldIds.Contains(f.Id)).ToListAsync(ct);
            _db.RepertoireFiles.RemoveRange(olds);
            await _db.SaveChangesAsync(ct);
        }
    }

    /// <summary>Schreibt das frisch geholte PGN zusätzlich in alle WEITEREN Repertoires desselben Users, die
    /// auf demselben Chessable-Kurs (bid) beruhen (per ChessableCourseId oder Dateiname), außer dem bereits
    /// aktualisierten <paramref name="target"/>. In-place (Id/Fortschritt bleiben) → UploadFileAsync hebt die
    /// ImportVersion. Ohne das blieben Kurs-Duplikate nach „Alle aktualisieren" veraltet (Dedup holt 1×).</summary>
    private async Task BumpSiblingRepertoiresAsync(ChessableImport import, string pgn, int target, CancellationToken ct)
    {
        var fileName = ChessableImportTargets.RepertoireFileName(import.Bid);
        var siblingIds = await _db.Repertoires
            .Where(r => r.UserId == import.UserId && r.Id != target
                && (r.ChessableCourseId == import.Bid || r.Files.Any(f => f.FileName == fileName)))
            .Select(r => r.Id).ToListAsync(ct);

        foreach (var repId in siblingIds)
            await ReplaceRepertoireFileAsync(repId, import.UserId, fileName, pgn, ct);
    }

    public async Task ImportAsBookAsync(ChessableImport import, string pgn, string courseName, CancellationToken ct)
    {
        // Pro-User-eindeutiger Dateiname; PgnImportService dedupliziert per LineId → von Natur aus
        // idempotent, ein Resume legt dieselben Puzzles nicht doppelt an.
        var fileName = ChessableImportTargets.BookFileName(import.UserId, import.Bid);
        // Ein Browser-Import („Mitschnitt importieren") trägt oft nur einzelne Kapitel: zusammenführen statt ersetzen.
        // Der Server-Abruf liefert dagegen immer den ganzen Kurs und ersetzt die Quelle wie bisher.
        var res = await _pgnImport.ImportFileAsync(fileName, pgn, ct, mergeSourcePgn: import.FromBrowser,
            ownerUserId: import.UserId);

        var book = await _db.Books.FirstOrDefaultAsync(b => b.Id == res.BookId, ct);
        if (book is not null)
        {
            book.OwnerUserId = import.UserId;
            book.DisplayName = ChessableImportTargets.Trunc(courseName, 200);
            book.Tags = "chessable";
            book.UpdatedAt = DateTime.UtcNow;
        }

        import.ResultId = res.BookId;
        // Gesamtzahl der Puzzles im Buch (korrekt auch beim Resume, wo res.Imported nur die neuen zählt).
        import.Imported = await _db.BookPuzzles.CountAsync(bp => bp.BookId == res.BookId, ct);
        import.Skipped = res.Skipped;
        import.Invalid = res.Invalid;
    }
}
