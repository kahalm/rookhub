using System.Text;

namespace RookHub.Api.Services.Tournaments.Live;

/// <summary>
/// Zerlegt den Lichess-Strom <c>/api/stream/broadcast/round/{id}.pgn</c> in ganze Partien. Der Strom kommt in beliebigen
/// Stücken (mitten in einer Zeile, mitten in einem Kommentar); eine Partie ist fertig, sobald ihr Zugtext mit dem
/// Ergebnis-Token endet (<c>*</c> für laufende Partien) — Lichess schreibt danach Leerzeilen, aber auf die wird nicht
/// gewartet: die letzte Partie vor einer Pause soll sofort ankommen. Fängt eine neue Kopfzeile an, bevor ein Ergebnis kam,
/// gilt die alte Partie ebenso als fertig. Ein Zerleger je Verbindung (nicht threadsicher).
/// </summary>
public sealed class BroadcastPgnSplitter
{
    private readonly StringBuilder _line = new();
    private readonly StringBuilder _game = new();
    private bool _hasHeaders;
    private bool _inMoves;
    private int _braceDepth;

    /// <summary>Höchstlänge einer Partie — schützt vor einem Strom, der nie ein Ergebnis schreibt.</summary>
    public const int MaxGameChars = 512 * 1024;

    /// <summary>Ein Stück des Stroms → die damit fertig gewordenen Partien (PGN-Text je Partie).</summary>
    public List<string> Feed(ReadOnlySpan<char> chunk)
    {
        var done = new List<string>();
        foreach (var c in chunk)
        {
            if (c == '\n') { Line(_line.ToString().TrimEnd('\r'), done); _line.Clear(); }
            else _line.Append(c);
        }
        return done;
    }

    /// <summary>Ende des Stroms: was noch im Puffer steht (letzte Zeile ohne Umbruch, Partie ohne Ergebnis).</summary>
    public List<string> Flush()
    {
        var done = new List<string>();
        if (_line.Length > 0) { Line(_line.ToString().TrimEnd('\r'), done); _line.Clear(); }
        Emit(done);
        return done;
    }

    private void Line(string line, List<string> done)
    {
        var trimmed = line.Trim();
        if (_braceDepth == 0 && trimmed.StartsWith('[') && trimmed.EndsWith(']') && !trimmed.StartsWith("[%"))
        {
            if (_inMoves) Emit(done);
            _game.Append(trimmed).Append('\n');
            _hasHeaders = true;
            return;
        }
        if (trimmed.Length == 0)
        {
            if (_hasHeaders && !_inMoves) _game.Append('\n');
            return;
        }
        if (!_hasHeaders && _braceDepth == 0) return;                 // Zugtext ohne Kopf (Strom-Anfang mitten in einer Partie)
        if (!_inMoves) { _inMoves = true; _game.Append('\n'); }
        _game.Append(trimmed).Append('\n');
        foreach (var c in trimmed)
        {
            if (c == '{') _braceDepth++;
            else if (c == '}' && _braceDepth > 0) _braceDepth--;
        }
        if (_braceDepth == 0 && EndsWithResult(trimmed)) Emit(done);
        else if (_game.Length > MaxGameChars) Reset();
    }

    private static bool EndsWithResult(string s)
    {
        var last = s.LastIndexOf(' ');
        var tok = last < 0 ? s : s[(last + 1)..];
        return PgnTokens.IsResultToken(tok);
    }

    private void Emit(List<string> done)
    {
        if (_hasHeaders) done.Add(_game.ToString());
        Reset();
    }

    private void Reset()
    {
        _game.Clear();
        _hasHeaders = false;
        _inMoves = false;
        _braceDepth = 0;
    }
}
