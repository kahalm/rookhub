using System.Globalization;

namespace RookHub.Api.Services;

/// <summary>
/// EINE Form fuer Chessable-Kennungen (Codereview 2026-09-29, A3-013). Vorher stand die bid-Pruefung an vier Stellen
/// und die oid-Pruefung in ZWEI Semantiken an acht: „int &gt; 0" (Extension-Ingest, Linien-Cache, piratechess) gegen
/// „hoechstens 32 ASCII-Ziffern" (Senken problem-moves/session-moves/review-lines, line-trained). Nach der zweiten
/// waren <c>00123</c> und <c>123</c> zwei Linien, und eine 20-stellige oid wurde gespeichert, fiel spaeter aber still
/// aus dem Cache-Weg. Jetzt gilt ueberall die Regel von piratechess (<c>BrowserCourseAssembler.TryParseOid</c>,
/// Spiegeltest mit denselben Literalen in <c>ChessableIdsTests</c>), gespeichert wird die kanonische Form.
/// </summary>
public static class ChessableIds
{
    /// <summary>Hoechstlaenge einer Kurs-bid (Spalten und DTOs: MaxLength(12)).</summary>
    public const int MaxBidLength = 12;

    /// <summary>Kurs-bid: 1–12 ASCII-Ziffern.</summary>
    public static bool IsValidBid(string? bid) => bid is { Length: > 0 and <= MaxBidLength } && bid.All(char.IsAsciiDigit);

    /// <summary>Linien-oid: positive 32-Bit-Ganzzahl aus reinen ASCII-Ziffern (kein Vorzeichen, kein Leerraum) —
    /// genau die Regel von piratechess.</summary>
    public static bool TryParseOid(string? raw, out int oid)
        => int.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out oid) && oid > 0;

    /// <summary>Kanonische Schreibweise einer gueltigen oid (ohne fuehrende Nullen), sonst <c>null</c>.</summary>
    public static string? CanonicalOid(string? raw)
        => TryParseOid(raw, out var oid) ? oid.ToString(CultureInfo.InvariantCulture) : null;
}
