using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Die Sprache der Kommentare — Auswahlkriterium des Rohbestands, nicht Nebensache: eine glaenzend
/// kommentierte Partie nuetzt nichts, wenn der Text niemandem etwas sagt.
/// </summary>
public class CommentLanguageTests
{
    private const string German =
        "Der Zug ist stark, aber nicht der beste. Weiss muss hier noch mit der Dame arbeiten, " +
        "sonst kann Schwarz die Stellung halten und der Angriff verpufft. Das ist sehr wichtig.";

    private const string English =
        "The move is strong but not the best. White should work with the queen here, and that is " +
        "very important, because black would hold the position after this and the attack has gone.";

    [Fact]
    public void Detect_erkenntDeutschUndEnglisch()
    {
        Assert.Equal("de", CommentLanguage.Detect(German));
        Assert.Equal("en", CommentLanguage.Detect(English));
    }

    [Fact]
    public void Detect_erkenntWeitereSprachen()
    {
        Assert.Equal("es", CommentLanguage.Detect(
            "La jugada es muy fuerte pero no la mejor, porque las blancas tienen una posicion " +
            "mejor despues de esta jugada y las negras no pueden hacer nada para cambiar eso."));
        Assert.Equal("fr", CommentLanguage.Detect(
            "Le coup est tres fort mais pas le meilleur, car les blancs ont une meilleure position " +
            "apres cette suite et les noirs ne peuvent pas faire grand chose dans cette partie."));
    }

    /// <summary>Kyrillisch entscheidet die Schrift, nicht die Wortliste — dort ist die Frage schon
    /// mit dem ersten Buchstaben beantwortet.</summary>
    [Fact]
    public void Detect_erkenntKyrillischAnDerSchrift()
        => Assert.Equal("ru", CommentLanguage.Detect(
            "Этот ход очень силён, но не лучший, потому что белые получают лучшую позицию " +
            "после этого продолжения и чёрные уже ничего не могут изменить в этой партии."));

    /// <summary>Schachnotation ist sprachlos — daraus darf keine Sprache abgeleitet werden.</summary>
    [Fact]
    public void Detect_notationAlleinGibtNichtsHer()
    {
        Assert.Null(CommentLanguage.Detect("1. e4 e5 2. Nf3 Nc6 3. Bb5 a6 4. Ba4 Nf6 1-0"));
        Assert.Null(CommentLanguage.Detect("+-"));
        Assert.Null(CommentLanguage.Detect(""));
    }

    /// <summary>Sammlungen mischen Sprachen. Stehen zwei etwa gleich stark im Text, werden beide
    /// genannt — sonst behauptet die Spalte etwas, das nur zur Haelfte stimmt.</summary>
    [Fact]
    public void Detect_nenntZweiSprachen_wennBeideDrinstehen()
    {
        var mixed = CommentLanguage.Detect(German + " " + English);

        Assert.NotNull(mixed);
        Assert.Contains("de", mixed!);
        Assert.Contains("en", mixed);
    }

    [Fact]
    public void CommentText_holtNurDieKlammern()
    {
        const string pgn = "[White \"A\"]\n\n1. e4 {stark} e5 (1... c5 {sizilianisch}) 2. Nf3 Nc6 1-0";

        var text = CommentLanguage.CommentText(pgn);

        Assert.Contains("stark", text);
        Assert.Contains("sizilianisch", text);
        Assert.DoesNotContain("Nf3", text);
        Assert.DoesNotContain("White", text);
    }

    /// <summary>Fuer die Frage nach der Sprache reichen ein paar tausend Zeichen. Eine Partie mit
    /// 60 KB Analyse deswegen ganz durchzugehen waere verschwendet.</summary>
    [Fact]
    public void CommentText_haeltSichAnDenDeckel()
    {
        var pgn = string.Concat(Enumerable.Repeat("1. e4 {" + new string('x', 500) + "} e5 ", 50));

        Assert.True(CommentLanguage.CommentText(pgn, 1000).Length <= 1000 + 50);
    }
}
