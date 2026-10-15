using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Brett als eigenständige SVG-Datei aus einer FEN (0.802.0, Fließtext-EPUB): 8 × 8 Felder in den App-Farben, Figuren aus
/// dem eingebetteten cburnett-Satz (<c>Assets/pieces/cburnett</c>, GPLv2+, Colin M.L. Burnett — derselbe wie im OG-Bild und
/// im Brett der App; Herkunft <c>public/CHESS-ASSETS.md</c>). Jede benutzte Figur steht EINMAL als <c>&lt;symbol&gt;</c>
/// in der Datei und wird per <c>&lt;use&gt;</c> gesetzt — die Datei kommt ohne externe Verweise aus (ein <c>&lt;img&gt;</c>
/// lädt keine fremden Ressourcen nach) und bleibt klein (~3–8 KB).
/// </summary>
public static class EbookBoardSvg
{
    private const int Cell = 45;   // = viewBox der cburnett-Figuren
    private const string Light = "#f0d9b5";
    private const string Dark = "#b58863";
    private static readonly ConcurrentDictionary<string, string?> Pieces = new();
    private static readonly Regex Outer = new(@"^\s*<svg[^>]*>(?<inner>.*)</svg>\s*$", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>SVG-Text für die Stellung der FEN (nur das Brettfeld wird gelesen); <c>null</c>, wenn sie nicht 8 × 8 ergibt.</summary>
    public static string? Render(string fen)
    {
        var squares = Squares(fen);
        if (squares == null) return null;
        var size = Cell * 8;
        var sb = new StringBuilder(4096);
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append(CultureInfo.InvariantCulture, $"<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\" viewBox=\"-2 -2 {size + 4} {size + 4}\" width=\"{size + 4}\" height=\"{size + 4}\">");
        sb.Append("<defs>");
        foreach (var name in squares.Where(c => c != '.').Select(FileName).Distinct().OfType<string>())
        {
            var inner = PieceInner(name);
            if (inner != null) sb.Append("<symbol id=\"").Append(name).Append("\" viewBox=\"0 0 45 45\">").Append(inner).Append("</symbol>");
        }
        sb.Append("</defs>");
        sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"-2\" y=\"-2\" width=\"{size + 4}\" height=\"{size + 4}\" fill=\"#5a4632\"/>");
        sb.Append(CultureInfo.InvariantCulture, $"<rect width=\"{size}\" height=\"{size}\" fill=\"{Light}\"/>");
        for (var r = 0; r < 8; r++)
            for (var c = 0; c < 8; c++)
                if ((r + c) % 2 == 1)
                    sb.Append(CultureInfo.InvariantCulture, $"<rect x=\"{c * Cell}\" y=\"{r * Cell}\" width=\"{Cell}\" height=\"{Cell}\" fill=\"{Dark}\"/>");
        for (var i = 0; i < 64; i++)
        {
            var name = FileName(squares[i]);
            if (name == null || PieceInner(name) == null) continue;
            sb.Append(CultureInfo.InvariantCulture, $"<use xlink:href=\"#{name}\" x=\"{i % 8 * Cell}\" y=\"{i / 8 * Cell}\" width=\"{Cell}\" height=\"{Cell}\"/>");
        }
        sb.Append("</svg>\n");
        return sb.ToString();
    }

    /// <summary>FEN-Brett → 64 Zeichen (a8 … h1), '.' = leer; <c>null</c> bei kaputter FEN.</summary>
    internal static char[]? Squares(string fen)
    {
        var board = (fen ?? string.Empty).Trim().Split(' ')[0];
        var res = new char[64];
        var k = 0;
        foreach (var ch in board)
        {
            if (ch == '/') continue;
            if (char.IsAsciiDigit(ch))
            {
                for (var t = 0; t < ch - '0'; t++) { if (k >= 64) return null; res[k++] = '.'; }
            }
            else if ("KQRBNPkqrbnp".Contains(ch)) { if (k >= 64) return null; res[k++] = ch; }
            else return null;
        }
        return k == 64 ? res : null;
    }

    private static string? FileName(char c) => c == '.' ? null : Og.OgImageService.FenCharToFile(c);

    /// <summary>Inhalt der cburnett-SVG (ohne äußeres &lt;svg&gt;), gecacht.</summary>
    private static string? PieceInner(string name) => Pieces.GetOrAdd(name, n =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"RookHub.Api.Assets.pieces.cburnett.{n}.svg");
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        var m = Outer.Match(reader.ReadToEnd());
        return m.Success ? m.Groups["inner"].Value : null;
    });
}
