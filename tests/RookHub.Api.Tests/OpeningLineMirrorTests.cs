using System.Text;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die <c>OpeningLine</c> entsteht an ZWEI Stellen — im Textdurchgang der Bibliothek
/// (<see cref="LibraryGameReader.Analyse"/>) und aus den nachgespielten Halbzuegen der eigenen
/// Analysen (<see cref="GameAnalysisService.OpeningLineOf"/>) —, und der Eroeffnungsbaum der
/// Punktepartie sucht mit EINEM Praefix ueber beide Spalten
/// (<see cref="GuessOpeningTree"/>, Modus „nur spielbare" gegen <c>GameAnalyses</c>, sonst gegen
/// <c>LibraryGames</c>). Laufen die Formen auseinander, findet derselbe Zug in der einen Quelle
/// nichts.
///
/// <para>Genau das war bis 0.499.12 so: der Textweg behielt die Schachzeichen, der Brettweg warf
/// sie weg, und weil <see cref="GuessOpeningTree.Normalize"/> die Anfrage ebenfalls ohne
/// <c>+</c>/<c>#</c> stellt, endete jeder Ast der Bibliothek am ersten Schachgebot mit „keine
/// Partien" — 36 213 von 130 055 Zeilen waren betroffen.</para>
/// </summary>
public class OpeningLineMirrorTests
{
    /// <summary>Eine Partie mit Schachgebot, Rochade und Schlagzug in den ersten Halbzuegen.</summary>
    private const string PgnMitSchach =
        "[Event \"Test\"]\n[White \"A\"]\n[Black \"B\"]\n[Result \"*\"]\n\n" +
        "1. e4 c5 2. Nf3 d6 3. d4 cxd4 4. Nxd4 Nf6 5. Nc3 a6 6. Bg5 e6 7. f4 Be7 " +
        "8. Qf3 Qc7 9. O-O-O Nbd7 10. Bb5+! axb5 11. Ndxb5 Qb8 12. e5 dxe5 *";

    private static string DurchDenTextweg(string pgn)
    {
        var (_, moveText) = PgnParser.SplitGames(pgn).First();
        return LibraryGameReader.Analyse(moveText).OpeningLine;
    }

    private static string DurchDenBrettweg(string pgn)
    {
        var parsed = GamePlies.Parse(pgn);
        Assert.NotNull(parsed);
        return GameAnalysisService.OpeningLineOf(parsed!.Value.Plies);
    }

    [Fact]
    public void BeideWege_LiefernDieselbeZeile()
    {
        var ausDerBibliothek = DurchDenTextweg(PgnMitSchach);
        var ausDerAnalyse = DurchDenBrettweg(PgnMitSchach);

        Assert.Equal(ausDerAnalyse, ausDerBibliothek);
        Assert.StartsWith("e4 c5 Nf3 d6 d4 cxd4 Nxd4 Nf6 Nc3 a6 Bg5 e6 f4 Be7 Qf3 Qc7 O-O-O Nbd7 Bb5 axb5",
            ausDerBibliothek);
    }

    /// <summary>Der Grund fuer die ganze Uebung: der Baum sucht ohne Schachzeichen, also darf in
    /// KEINER der beiden Spalten eines stehen.</summary>
    [Fact]
    public void KeineZeile_TraegtSchachOderMattzeichen()
    {
        foreach (var zeile in new[] { DurchDenTextweg(PgnMitSchach), DurchDenBrettweg(PgnMitSchach) })
        {
            Assert.DoesNotContain('+', zeile);
            Assert.DoesNotContain('#', zeile);
            Assert.DoesNotContain('!', zeile);
            Assert.DoesNotContain('?', zeile);
        }
    }

    /// <summary>Was der Baum fragt, muss die Spalte auch enthalten: <see cref="GuessOpeningTree.Normalize"/>
    /// raeumt dieselben Zeichen weg — auf einer bereits sauberen Zeile ist es ein No-op.</summary>
    [Fact]
    public void DieZeile_UeberstehtDieNormalisierungDesBaums()
    {
        var zeile = DurchDenTextweg(PgnMitSchach);
        var alsPraefix = string.Join(' ', zeile.Split(' ').Take(GuessOpeningTree.MaxDepth));

        Assert.Equal(alsPraefix, GuessOpeningTree.Normalize(alsPraefix));
    }

    /// <summary>Beide Wege fassen genau <see cref="LibraryGameReader.OpeningPlies"/> Halbzuege —
    /// eine laengere Partie wird an derselben Stelle abgeschnitten.</summary>
    [Fact]
    public void BeideWege_SchneidenBeimSelbenHalbzug()
    {
        var sb = new StringBuilder("[Event \"L\"]\n[Result \"*\"]\n\n");
        var zuege = new[] { "Nf3 Nf6", "Ng1 Ng8" };
        for (var zug = 1; zug <= 20; zug++) sb.Append(zug).Append(". ").Append(zuege[(zug - 1) % 2]).Append(' ');
        sb.Append('*');
        var pgn = sb.ToString();

        var textweg = DurchDenTextweg(pgn);
        var brettweg = DurchDenBrettweg(pgn);

        Assert.Equal(brettweg, textweg);
        Assert.Equal(LibraryGameReader.OpeningPlies, textweg.Split(' ').Length);
    }
}
