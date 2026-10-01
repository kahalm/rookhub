using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Data;
using RookHub.Api.DTOs;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Erzeugt und liest öffentliche Nur-Ansehen-Links für einzelne Repertoire-Linien
/// (<c>/l/{token}</c>). Analog zum öffentlichen Partie-Link (<see cref="SavedGameService"/>):
/// die Linie wird als eigenständiges PGN-Snapshot gespeichert, der Link ist unabhängig vom
/// Original-Repertoire.
/// </summary>
public class SharedLineService
{
    private readonly AppDbContext _db;

    public SharedLineService(AppDbContext db) => _db = db;

    /// <summary>Obergrenze für das geteilte Linien-PGN (Zeichen). Eine einzelne Repertoire-Linie ist
    /// real wenige KB groß — der Cap verhindert, dass der öffentliche /l/-Endpoint als Ablage für
    /// beliebig große LONGTEXT-Blobs missbraucht wird.</summary>
    public const int MaxPgnChars = 32 * 1024;

    /// <summary>
    /// Legt einen Teilen-Link für eine Linie des Repertoires <paramref name="repertoireId"/> an.
    /// Zugriff: NUR der Besitzer. Dieselbe Linie erneut geteilt (gleicher PGN-Hash je Besitzer)
    /// liefert den bestehenden Link zurück (kein Duplikat).
    /// Gibt <c>null</c> zurück, wenn kein Zugriff / Repertoire nicht existiert.
    /// </summary>
    /// <exception cref="ArgumentException">PGN größer als <see cref="MaxPgnChars"/> (→ 400).</exception>
    public async Task<ShareLineResultDto?> CreateAsync(int userId, int repertoireId, ShareLineInputDto dto, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(dto.Pgn)) return null;
        if (dto.Pgn.Length > MaxPgnChars)
            throw new ArgumentException($"PGN exceeds the {MaxPgnChars / 1024} KB limit for shared lines.");
        if (!RepertoireService.LooksLikePgn(dto.Pgn)) return null;

        var rep = await _db.Repertoires.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == repertoireId, ct);
        if (rep == null) return null;

        // VERHALTENSÄNDERUNG (bewusst): Nur noch der BESITZER darf öffentliche Links anlegen.
        // Vorher durfte auch der Freigabe-Empfänger — damit konnte er den Repertoire-Inhalt dauerhaft
        // öffentlich machen (der Snapshot überlebte sogar den Widerruf der Freigabe). Die Freigabe
        // ist „ansehen/trainieren", nicht „weiterveröffentlichen" (analog: kein Weiterteilen bei
        // RepertoireShares) — also entscheidet über eine ÖFFENTLICHE Kopie allein der Besitzer.
        if (rep.UserId != userId) return null;

        return await StoreAsync(userId, repertoireId, dto.Title, rep.Name, dto.Pgn.Trim(), null, ct);
    }

    /// <summary>
    /// Teilt eine „freistehende" Linie (nicht an ein RookHub-Repertoire gebunden) — genutzt von der
    /// RepCheck-Extension, die die aktuell auf chess.com/lichess gespielte Zugfolge teilt. Der Server
    /// spielt die SAN-Zugliste ab der Grundstellung nach und baut aus den Zügen in der Schreibweise des
    /// Bretts ein PGN. Dedup je Besitzer wie sonst. <c>null</c> bei leerer Zugliste.
    ///
    /// <para>Vorher gingen die Zugtexte ungeprüft ins PGN (nur Trim, höchstens 600) und der Deckel
    /// <see cref="MaxPgnChars"/> galt hier nicht: ein Konto legte je Aufruf bis ~15 MB beliebigen Text als anonym
    /// abrufbaren /l/-Link ab, und Zugtexte mit <c>}</c>, <c>[</c> oder Zeilenumbrüchen schleusten Kopfzeilen in
    /// das angezeigte PGN (Codereview 2026-09-29, N8-003).</para>
    /// </summary>
    /// <exception cref="ArgumentException">Zug zu lang oder nicht legal, PGN größer als <see cref="MaxPgnChars"/> (→ 400).</exception>
    public async Task<ShareLineResultDto?> CreateStandaloneAsync(int userId, IEnumerable<string>? moves, string? title, CancellationToken ct = default)
    {
        var written = (moves ?? Enumerable.Empty<string>())
            .Select(m => (m ?? string.Empty).Trim())
            .Where(m => m.Length > 0)
            .Take(600)
            .ToList();
        if (written.Count == 0) return null;
        // Erst die Länge: die Meldung eines illegalen Zuges nennt den Zug selbst.
        if (written.Any(m => m.Length > SavedGameService.MaxSanLength)) throw new ArgumentException("Invalid move.");
        var sans = SavedGameService.LegalSans(written);
        var pgn = BuildLinePgn(sans, title);
        if (pgn.Length > MaxPgnChars)
            throw new ArgumentException($"PGN exceeds the {MaxPgnChars / 1024} KB limit for shared lines.");
        // Identität einer freistehenden Line = ihre Zugfolge (NICHT der variable Seitentitel) →
        // Dedup über die Züge, damit derselbe Spielstand denselben Link liefert.
        return await StoreAsync(userId, null, title, null, pgn, "ext|" + string.Join(' ', sans), ct);
    }

    private async Task<ShareLineResultDto?> StoreAsync(int userId, int? repertoireId, string? title, string? repertoireName, string pgn, string? dedupSource, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pgn)) return null;
        var hash = Sha256Hex(dedupSource ?? pgn);

        // Dedup je Besitzer über den PGN-Hash → derselbe Link bei erneutem Teilen.
        var existing = await _db.SharedLines.AsNoTracking()
            .FirstOrDefaultAsync(s => s.OwnerUserId == userId && s.LineHash == hash, ct);
        if (existing != null) return new ShareLineResultDto { ShareToken = existing.ShareToken };

        var cleanTitle = string.IsNullOrWhiteSpace(title) ? null : title!.Trim();
        if (cleanTitle is { Length: > 200 }) cleanTitle = cleanTitle[..200];

        var entity = new SharedLine
        {
            OwnerUserId = userId,
            RepertoireId = repertoireId,
            Title = cleanTitle,
            RepertoireName = repertoireName,
            Pgn = pgn,
            LineHash = hash,
            ShareToken = await GenerateUniqueTokenAsync(ct),
            CreatedAt = DateTime.UtcNow,
        };
        _db.SharedLines.Add(entity);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (AuthService.IsUniqueViolation(ex))
        {
            // Race: paralleler Teilen-Klick derselben Linie hat den (Owner,LineHash)-Unique zuerst belegt.
            _db.ChangeTracker.Clear();
            var raced = await _db.SharedLines.AsNoTracking()
                .FirstOrDefaultAsync(s => s.OwnerUserId == userId && s.LineHash == hash, ct);
            if (raced != null) return new ShareLineResultDto { ShareToken = raced.ShareToken };
            throw;
        }
        return new ShareLineResultDto { ShareToken = entity.ShareToken };
    }

    /// <summary>Baut aus einer SAN-Hauptlinie (ab Grundstellung) ein minimales PGN mit Zugnummern.</summary>
    private static string BuildLinePgn(List<string> sans, string? title)
    {
        var evt = string.IsNullOrWhiteSpace(title) ? "Repertoire line" : title!.Trim();
        var sb = new StringBuilder();
        sb.Append(PgnWriter.Tag("Event", evt))
          .Append(PgnWriter.Tag("White", "?"))
          .Append(PgnWriter.Tag("Black", "?"))
          .Append(PgnWriter.Tag("Result", "*"))
          .Append('\n')
          .Append(PgnWriter.MoveText(sans))
          .Append('\n');
        return sb.ToString();
    }

    /// <summary>Öffentliche Sicht über das Token; <c>null</c> wenn unbekannt.</summary>
    public async Task<SharedLineDto?> GetByTokenAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        var s = await _db.SharedLines.AsNoTracking()
            .FirstOrDefaultAsync(x => x.ShareToken == token, ct);
        if (s == null) return null;
        return new SharedLineDto
        {
            ShareToken = s.ShareToken,
            Title = s.Title,
            RepertoireName = s.RepertoireName,
            Pgn = s.Pgn,
            CreatedAt = s.CreatedAt,
        };
    }

    private static string Sha256Hex(string s)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(s));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private Task<string> GenerateUniqueTokenAsync(CancellationToken ct)
        => ShareTokens.NewUniqueAsync(t => _db.SharedLines.AnyAsync(s => s.ShareToken == t, ct));
}
