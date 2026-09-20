using Chess;
using RookHub.Api.Models;

namespace RookHub.Api.Services;

/// <summary>
/// Verkettet die Bruchstücke einer Rekonstruktion: Welches Teil lässt sich prüfen, welche Stellung
/// steht danach auf dem Brett, und wo klafft eine Lücke?
///
/// <para>Reine Funktion (kein DB, keine Engine) — die ganze Auswertung hängt nur an der Liste der
/// Teile und ist damit ohne Infrastruktur testbar.</para>
///
/// <para><b>Die Grundannahme ist LÜCKE, nicht Anschluss.</b> Wer eine Partie rekonstruiert, hat
/// Bruchstücke von verschiedenen Stellen — hingen sie aneinander, wären sie EIN Teil. Ein Teil
/// schließt deshalb nur dann an seinen Vorgänger an, wenn es das ausdrücklich sagt
/// (<see cref="GameReconstructionPart.ContinuesPrevious"/>); das erste Teil hängt an der
/// Grundstellung.</para>
///
/// <para>Daraus folgt die zweite Regel: <b>ein Bruchstück ohne Anschluss wird nicht als falsch
/// gemeldet.</b> Ohne die Stellung davor lässt sich eine Zugfolge weder bestätigen noch widerlegen
/// — und genau diese fehlende Stellung ist ja das, was rekonstruiert werden soll. Solche Teile
/// tragen <see cref="PartChain.Anchored"/> = false und sonst keinen Vorwurf.</para>
/// </summary>
public static class ReconstructionChain
{
    /// <summary>Höchstens so viele Halbzüge je Zugfolge werden gespielt (Schutz vor Endlos-Eingaben).</summary>
    public const int MaxPliesPerPart = 400;

    /// <summary>Das Ergebnis der Auswertung EINES Teils.</summary>
    /// <param name="PartId">Id des Teils (0 für noch nicht gespeicherte Vorschau-Teile).</param>
    /// <param name="Anchored">Ist bekannt, welche Stellung VOR diesem Teil steht?</param>
    /// <param name="Valid">Ließ sich das Teil spielen bzw. laden? Bei fehlendem Anschluss false ohne Vorwurf.</param>
    /// <param name="StartFen">Stellung vor dem Teil (null, wenn nicht verankert).</param>
    /// <param name="EndFen">Stellung nach dem Teil (bei einer Stellung: sie selbst).</param>
    /// <param name="PlyCount">Zahl der Halbzüge dieses Teils (bei einer Stellung 0).</param>
    /// <param name="StartPly">Halbzug, an dem das Teil beginnt — nur solange die Kette ab der
    /// Grundstellung durchgehend ist, sonst null (eine Nummer nach einer Lücke wäre geraten).</param>
    /// <param name="FirstBadMove">Der erste Zug, der sich nicht spielen ließ (sonst null).</param>
    /// <param name="Mismatch">Das Teil behauptet den Anschluss, passt aber nicht zur Stellung davor.</param>
    public record PartChain(
        int PartId, bool Anchored, bool Valid, string? StartFen, string? EndFen,
        int PlyCount, int? StartPly, string? FirstBadMove, bool Mismatch = false);

    /// <summary>Das Gesamtbild einer Rekonstruktion.</summary>
    /// <param name="Parts">Je Teil ein Eintrag, in Reihenfolge.</param>
    /// <param name="KnownPlies">Halbzüge der durchgehenden Kette AB DER GRUNDSTELLUNG.</param>
    /// <param name="PrefixSan">Genau diese Züge als SAN-Folge — der Teil der Partie, der schon steht.</param>
    /// <param name="Gaps">Zahl der Stellen, an denen die Partie eine Lücke hat.</param>
    public record Result(IReadOnlyList<PartChain> Parts, int KnownPlies, string PrefixSan, int Gaps);

    /// <summary>Zerlegt einen Zugtext in einzelne SAN-Züge: Zugnummern, Ergebnis und Kommentare weg.</summary>
    public static List<string> SplitMoves(string? text)
    {
        var moves = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return moves;

        var cleaned = System.Text.RegularExpressions.Regex.Replace(text, @"\{[^}]*\}", " ");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\([^()]*\)", " ");
        foreach (var raw in cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.Trim();
            if (token.Length == 0) continue;
            if (System.Text.RegularExpressions.Regex.IsMatch(token, @"^\d+\.*$")) continue;   // Zugnummer
            if (token is "1-0" or "0-1" or "1/2-1/2" or "*") continue;                        // Ergebnis
            if (token.StartsWith('$')) continue;                                              // NAG
            var stuck = System.Text.RegularExpressions.Regex.Match(token, @"^\d+\.+(?<san>.+)$");
            if (stuck.Success) token = stuck.Groups["san"].Value;                             // „12.e4"
            if (token.Length > 0) moves.Add(token);
        }
        return moves;
    }

    /// <summary>Ist das eine Stellung, die sich laden lässt?</summary>
    public static bool IsLoadableFen(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen) || fen.Length > 120) return false;
        try { ChessBoard.LoadFromFen(fen); return true; }
        catch { return false; }
    }

    /// <summary>Dieselbe Stellung? Verglichen werden Brett, Seite am Zug, Rochade und en passant —
    /// nicht die Zähler: „50-Zug-Regel bei 12" und „bei 13" sind dieselbe Stellung auf dem Brett.</summary>
    private static bool SamePosition(string a, string b)
    {
        var x = a.Split(' ');
        var y = b.Split(' ');
        if (x.Length < 4 || y.Length < 4) return a.Trim() == b.Trim();
        return x[0] == y[0] && x[1] == y[1] && x[2] == y[2] && x[3] == y[3];
    }

    /// <summary>Wertet die Teile in ihrer Reihenfolge aus.</summary>
    public static Result Analyze(IEnumerable<GameReconstructionPart> parts)
    {
        var ordered = parts.OrderBy(p => p.Ordinal).ToList();
        var chains = new List<PartChain>(ordered.Count);

        ChessBoard? board = null;          // Stellung nach dem zuletzt ausgewerteten Teil
        var plyFromStart = 0;              // Halbzüge seit der Grundstellung (nur wenn fromStart)
        var fromStart = false;             // hängt die Kette noch lückenlos an der Grundstellung?
        var prefix = new List<string>();   // die Züge dieser durchgehenden Kette
        var prefixClosed = false;          // ab dem ersten Abriss wächst der Vorspann nicht mehr
        var gaps = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            var part = ordered[i];
            var first = i == 0;
            // Anschluss gibt es nur auf Ansage — und nur, wenn davor überhaupt eine Stellung steht.
            var continues = !first && part.ContinuesPrevious && board != null;
            if (!first && !continues) { gaps++; prefixClosed = true; }

            if (part.Kind == ReconstructionPartKind.Position)
            {
                if (!IsLoadableFen(part.Fen))
                {
                    chains.Add(new PartChain(part.Id, true, false, null, null, 0, null, null));
                    board = null; fromStart = false; prefixClosed = true;
                    continue;
                }

                var mismatch = continues && !SamePosition(board!.ToFen(), part.Fen!);
                var keepsStart = first ? PgnParser.IsStartPosition(part.Fen!)
                                       : continues && !mismatch && fromStart;
                board = ChessBoard.LoadFromFen(part.Fen!);
                fromStart = keepsStart;
                if (!fromStart) prefixClosed = true;
                if (first && fromStart) plyFromStart = 0;

                chains.Add(new PartChain(part.Id, true, !mismatch, part.Fen, part.Fen, 0,
                    fromStart ? plyFromStart : null, null, mismatch));
                continue;
            }

            // ----- Zugfolge -----
            var moves = SplitMoves(part.Moves);

            if (first && !part.BlackToMove)
            {
                board = new ChessBoard();     // die ersten Züge einer Partie: ab der Grundstellung
                fromStart = true;
                plyFromStart = 0;
            }
            else if (!continues)   // auch das erste Teil, wenn es mit einem schwarzen Zug beginnt:
                                   // eine Partie fängt so nicht an, also ist es ein Bruchstück
            {
                // Bruchstück mitten aus der Partie: ohne Stellung davor ist es nicht prüfbar.
                chains.Add(new PartChain(part.Id, false, false, null, null, moves.Count, null, null));
                board = null; fromStart = false;
                continue;
            }

            var startFen = board!.ToFen();
            var startPly = fromStart ? plyFromStart : (int?)null;
            string? bad = null;
            var played = 0;
            foreach (var san in moves.Take(MaxPliesPerPart))
            {
                var ok = false;
                try { ok = board.Move(san); } catch { ok = false; }
                if (!ok) { bad = san; break; }
                played++;
                if (fromStart && !prefixClosed) prefix.Add(san);
            }
            if (fromStart) plyFromStart += played;

            if (bad != null)
            {
                // Ab hier ist die Stellung unbekannt — weiterrechnen würde Folgefehler erfinden.
                chains.Add(new PartChain(part.Id, true, false, startFen, null, moves.Count, startPly, bad));
                board = null; fromStart = false; prefixClosed = true;
                continue;
            }

            chains.Add(new PartChain(part.Id, true, true, startFen, board.ToFen(), moves.Count, startPly, null));
        }

        return new Result(chains, prefix.Count, string.Join(' ', prefix), gaps);
    }
}
