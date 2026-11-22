using Chess;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Engines;

/// <summary>
/// Kodierung für Maia-3 auf dem Server — Zeile für Zeile nach <c>maia-encoding.ts</c> des Analysebretts
/// (<c>src/frontend/app/src/app/features/analysis/maia/</c>), rein, ohne Netz. Wer dort etwas ändert, ändert es hier mit;
/// <c>MaiaServiceTests</c> vergleicht beide über die Fixture <c>Fixtures/maia-parity.json</c> (erzeugt von
/// <c>tools/maia-parity/generate.mjs</c> mit der JS-Fassung und onnxruntime-web).
///
/// <para>Das Modell sieht die Stellung IMMER aus Sicht von Weiß: ist Schwarz am Zug, werden die Reihen umgedreht und die
/// Farben getauscht, und jeder Zug wird gespiegelt in den Index übersetzt. 4352 Zug-Indizes nach Formel: 4096
/// Von-Nach-Paare, danach 256 Umwandlungen (nur 7.→8. Reihe). Rochaderechte und en passant stehen nicht in den Tokens;
/// die Legalität kommt allein aus Gera.Chess (Rochade als Königszug e1g1 wie bei chess.js).</para>
/// </summary>
internal static class MaiaEncoding
{
    /// <summary>Kanal 0..11 je Figur: erst Weiß (PNBRQK), dann Schwarz (pnbrqk).</summary>
    private const string Pieces = "PNBRQKpnbrqk";
    /// <summary>Reihenfolge der Umwandlungsfiguren im Index-Block ab 4096.</summary>
    private const string Promo = "qrbn";
    public const int MoveCount = 4352;

    private static int Sq(string s, int at) => (s[at + 1] - '1') * 8 + (s[at] - 'a');

    /// <summary>Index eines UCI-Zugs in <c>logits_move</c> (aus WEISS-Sicht, also nach dem Spiegeln).</summary>
    public static int MoveIndex(string uci) => uci.Length == 4
        ? Sq(uci, 0) * 64 + Sq(uci, 2)
        : 4096 + (uci[0] - 'a') * 32 + (uci[2] - 'a') * 4 + Promo.IndexOf(uci[4]);

    private static string MirrorSq(string s, int at) => $"{s[at]}{(char)('1' + ('8' - s[at + 1]))}";

    /// <summary>Zug an der Mitte des Bretts spiegeln (Reihe r → 9 − r, Linie und Umwandlung bleiben).</summary>
    public static string MirrorUci(string uci) => MirrorSq(uci, 0) + MirrorSq(uci, 2) + uci[4..];

    /// <summary>Figurenfeld einer FEN spiegeln: Reihen in umgekehrter Reihenfolge, Groß/klein getauscht.</summary>
    public static string MirrorPlacement(string placement) => string.Join('/', placement.Split('/').Reverse()
        .Select(row => new string(row.Select(c => char.IsLetter(c) ? (char.IsUpper(c) ? char.ToLowerInvariant(c) : char.ToUpperInvariant(c)) : c).ToArray())));

    /// <summary>Figurenfeld → Eingabe <c>tokens</c> (64 Felder × 12 Kanäle, eine 1 je Figur).</summary>
    public static float[] BoardTokens(string placement)
    {
        var t = new float[64 * 12];
        var rows = placement.Split('/');
        for (var r = 0; r < 8 && r < rows.Length; r++)
        {
            var rank = 7 - r;
            var file = 0;
            foreach (var c in rows[r])
            {
                if (c is >= '1' and <= '8') { file += c - '0'; continue; }
                var channel = Pieces.IndexOf(c);
                if (channel >= 0 && file < 8) t[(rank * 8 + file) * 12 + channel] = 1;
                file++;
            }
        }
        return t;
    }

    /// <summary>Eine kodierte Stellung: Eingabe fürs Netz, legale Züge (UCI in ECHTER Brett-Sicht) und ihr Index.</summary>
    internal sealed record Encoding(float[] Tokens, IReadOnlyList<string> Legal, IReadOnlyList<int> Indices);

    /// <summary>Stellung kodieren; <c>null</c> bei unlesbarer FEN.</summary>
    public static Encoding? Encode(string fen)
    {
        var parts = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        List<string> legal;
        try { legal = ChessBoard.LoadFromFen(fen).Moves().Select(GamePlies.ToUci).Distinct().ToList(); }
        catch { return null; }
        var black = !FenFields.WhiteToMove(fen);
        return new Encoding(BoardTokens(black ? MirrorPlacement(parts[0]) : parts[0]), legal,
            legal.Select(u => MoveIndex(black ? MirrorUci(u) : u)).ToList());
    }

    /// <summary>Softmax NUR über die legalen Züge (Temperatur 1), absteigend sortiert — wie <c>policyFromLogits</c>.</summary>
    public static List<MaiaMove> Policy(ReadOnlySpan<float> logits, Encoding enc)
    {
        if (enc.Legal.Count == 0) return [];
        var values = new double[enc.Legal.Count];
        for (var k = 0; k < values.Length; k++)
            values[k] = enc.Indices[k] >= 0 && enc.Indices[k] < logits.Length ? logits[enc.Indices[k]] : double.NegativeInfinity;
        var max = values.Max();
        var exps = double.IsFinite(max) ? values.Select(v => Math.Exp(v - max)).ToArray() : values.Select(_ => 1.0).ToArray();
        var sum = exps.Sum();
        return enc.Legal.Select((uci, k) => new MaiaMove(uci, exps[k] / sum))
            .OrderByDescending(m => m.P).ThenBy(m => m.Uci, StringComparer.Ordinal).ToList();
    }
}

/// <summary>Ein Zug, wie Maia ihn einem Menschen zutraut: UCI (echte Brett-Sicht) und Wahrscheinlichkeit 0..1.</summary>
public sealed record MaiaMove(string Uci, double P);
