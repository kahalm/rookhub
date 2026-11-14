namespace RookHub.Api.Services.Games;

/// <summary>
/// Reine Brettgeometrie aus dem Figurenfeld einer FEN (0.971.0): welche Felder eine Figur bestreicht und wer ein Feld
/// bestreicht — ohne Zugrecht, ohne Fesselungen, ohne Schachgebot. Gebraucht für GEPRÜFTE Fakten der Fehler-Erklärungen
/// (<see cref="ExplanationFacts.CheckForks"/>: „der Springer gibt Schach und greift zugleich die Dame an“; die
/// Figurenliste und „welche Figur deckte das Feld“ im Zugvergleich), weil die Schachbibliothek nur legale Züge der Seite
/// am Zug kennt: nach einem Schachgebot lässt sich die Stellung nicht „mit derselben Seite am Zug“ lesen
/// (so arbeitet <see cref="ExplanationFacts.FirstMoveAttacks"/> und bleibt dort leer).
/// <para>„Bestreichen“ schließt das erste besetzte Feld einer Linie ein, gleich welcher Farbe — eine eigene Figur dort
/// ist gedeckt, eine fremde angegriffen.</para>
/// </summary>
internal sealed class BoardGeometry
{
    private readonly char[] _cells = new char[64];   // Index = Linie + 8 · Reihe (a1 = 0), '\0' = leer

    private BoardGeometry() { }

    /// <summary><c>null</c> = das Figurenfeld der FEN ist nicht lesbar.</summary>
    public static BoardGeometry? FromFen(string? fen)
    {
        if (string.IsNullOrWhiteSpace(fen)) return null;
        var rows = fen.Trim().Split(' ')[0].Split('/');
        if (rows.Length != 8) return null;
        var g = new BoardGeometry();
        for (var r = 0; r < 8; r++)
        {
            var file = 0;
            foreach (var ch in rows[r])
            {
                if (char.IsDigit(ch)) { file += ch - '0'; continue; }
                if (file > 7 || "pnbrqkPNBRQK".IndexOf(ch) < 0) return null;
                g._cells[file + 8 * (7 - r)] = ch;
                file++;
            }
            if (file != 8) return null;
        }
        return g;
    }

    private static int Index(string square) =>
        square.Length == 2 && square[0] is >= 'a' and <= 'h' && square[1] is >= '1' and <= '8'
            ? square[0] - 'a' + 8 * (square[1] - '1') : -1;

    private static string Name(int index) => $"{(char)('a' + index % 8)}{(char)('1' + index / 8)}";

    /// <summary>Figur auf dem Feld (FEN-Buchstabe, groß = Weiß), <c>'\0'</c> = leer oder kein Feld.</summary>
    public char PieceAt(string square) => Index(square) is >= 0 and var i ? _cells[i] : '\0';

    /// <summary>Alle Figuren einer Farbe mit Feld, in Brettreihenfolge (a1 … h8).</summary>
    public List<(char Piece, string Square)> Pieces(bool white)
    {
        var result = new List<(char, string)>();
        for (var i = 0; i < 64; i++)
            if (_cells[i] != '\0' && char.IsUpper(_cells[i]) == white) result.Add((_cells[i], Name(i)));
        return result;
    }

    private static readonly (int, int)[] KnightSteps = [(1, 2), (2, 1), (2, -1), (1, -2), (-1, -2), (-2, -1), (-2, 1), (-1, 2)];
    private static readonly (int, int)[] Diagonals = [(1, 1), (1, -1), (-1, -1), (-1, 1)];
    private static readonly (int, int)[] Straights = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>Die Felder, die die Figur auf <paramref name="from"/> bestreicht (leer, wenn dort nichts steht).</summary>
    public List<string> AttackedFrom(string from)
    {
        var result = new List<string>();
        var start = Index(from);
        if (start < 0 || _cells[start] == '\0') return result;
        var piece = _cells[start];
        int file = start % 8, rank = start / 8;

        void Step(int df, int dr)
        {
            int f = file + df, r = rank + dr;
            if (f is >= 0 and <= 7 && r is >= 0 and <= 7) result.Add(Name(f + 8 * r));
        }
        void Slide((int, int)[] directions)
        {
            foreach (var (df, dr) in directions)
                for (int f = file + df, r = rank + dr; f is >= 0 and <= 7 && r is >= 0 and <= 7; f += df, r += dr)
                {
                    result.Add(Name(f + 8 * r));
                    if (_cells[f + 8 * r] != '\0') break;
                }
        }

        switch (char.ToLowerInvariant(piece))
        {
            case 'p':
                var dir = char.IsUpper(piece) ? 1 : -1;
                Step(-1, dir);
                Step(1, dir);
                break;
            case 'n':
                foreach (var (df, dr) in KnightSteps) Step(df, dr);
                break;
            case 'k':
                foreach (var (df, dr) in Diagonals) Step(df, dr);
                foreach (var (df, dr) in Straights) Step(df, dr);
                break;
            case 'b': Slide(Diagonals); break;
            case 'r': Slide(Straights); break;
            case 'q': Slide(Diagonals); Slide(Straights); break;
        }
        return result;
    }

    /// <summary>Die Figuren einer Farbe, die das Feld bestreichen (decken bzw. angreifen).</summary>
    public List<(char Piece, string Square)> Attackers(string target, bool white) =>
        Pieces(white).Where(p => AttackedFrom(p.Square).Contains(target)).ToList();
}
