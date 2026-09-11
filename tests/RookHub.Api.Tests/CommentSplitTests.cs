using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Der zweisprachige Kommentarblock. Das PGN kennt keine Sprachauszeichnung — gemessen am
/// 2026-09-11 traegt KEINE der 130 572 Zeilen des Rohbestands einen <c>[%lang</c>-Marker, ChessBase
/// haengt die Sprachen beim Export schlicht aneinander. Die Beispiele hier sind darum echte Bloecke
/// aus dem Bestand (CBM 212 und 218).
/// </summary>
public class CommentSplitTests
{
    private static readonly string[] EnDe = ["en", "de"];

    /// <summary>Der Normalfall: erst der englische Absatz, direkt dahinter derselbe auf Deutsch.</summary>
    [Fact]
    public void Split_englischDannDeutsch_trenntAmSatzende()
    {
        const string text = "The day before both Arjun and myself had lost our games. Naturally we "
            + "were both coming in this game looking for a fight. Am Tag zuvor hatten sowohl Arjun "
            + "als auch ich unsere Partien verloren. Natürlich waren wir beide in dieser Partie auf "
            + "einen Kampf aus.";

        var parts = CommentSplit.Split(text, EnDe);

        Assert.Equal(2, parts.Count);
        Assert.StartsWith("The day before", parts["en"]);
        Assert.EndsWith("looking for a fight.", parts["en"]);
        Assert.StartsWith("Am Tag zuvor", parts["de"]);
        Assert.EndsWith("Kampf aus.", parts["de"]);
    }

    /// <summary>Die Figurenbuchstaben sind das schaerfere Signal: <c>Be3/Ng4</c> gegen
    /// <c>Le3/Sg4</c>. Hier tragen beide Haelften kaum Funktionswoerter.</summary>
    [Fact]
    public void Split_erkenntDieFigurenbuchstaben()
    {
        const string text = "Discourages Be3, since that is now well met by Ng4. "
            + "Schreckt von Le3 ab, da dies nun gut durch Sg4 beantwortet wird.";

        var parts = CommentSplit.Split(text, EnDe);

        Assert.Equal(2, parts.Count);
        Assert.Contains("Be3", parts["en"]);
        Assert.Contains("Le3", parts["de"]);
    }

    /// <summary>Die Reihenfolge steht nirgends — die Kopfzeile nennt nur, WELCHE Sprachen
    /// vorkommen. Steht Deutsch vorn, muss der Schnitt genauso sitzen.</summary>
    [Fact]
    public void Split_findetAuchDieUmgekehrteReihenfolge()
    {
        const string text = "Die Stellung ist nach diesem Zug sehr gut für Weiss, weil der Turm auf "
            + "der offenen Linie steht. The position is very good for White after this move, because "
            + "the rook stands on the open file.";

        var parts = CommentSplit.Split(text, EnDe);

        Assert.Equal(2, parts.Count);
        Assert.StartsWith("Die Stellung", parts["de"]);
        Assert.StartsWith("The position", parts["en"]);
    }

    /// <summary>Ein einsprachiger Block bleibt ganz — auch wenn die PARTIE zwei Sprachen fuehrt.
    /// Sonst zerschnitte der Schnitt genau die Kommentare, die gar keinen brauchen.</summary>
    [Fact]
    public void Split_einsprachigerBlock_bleibtGanz()
    {
        const string text = "The rook belongs on the open file. From there it attacks the weak pawn "
            + "and supports the advance. White is clearly better here.";

        var parts = CommentSplit.Split(text, EnDe);

        Assert.Single(parts);
        Assert.Equal(text, parts["en"]);
    }

    /// <summary>Zu duenn fuer eine Entscheidung: lieber ganz lassen als raten. Ein halbierter Satz
    /// ist schlimmer als ein zweisprachiger Block.</summary>
    [Fact]
    public void Split_ohneBelege_bleibtGanz()
    {
        var parts = CommentSplit.Split("!? Interessant. Sehr!", EnDe);

        Assert.Single(parts);
    }

    /// <summary>Eine Zugnummer ist kein Satzende: „8. Ng4" hat Punkt, Leerzeichen und einen
    /// Grossbuchstaben — und steht mitten im Satz.</summary>
    [Fact]
    public void Split_zugnummerIstKeinSatzende()
    {
        const string text = "White should continue with 8. Ng4 and then the knight is active and the "
            + "position is good. Weiss sollte mit 8. Sg4 fortsetzen, dann ist der Springer aktiv und "
            + "die Stellung gut.";

        var parts = CommentSplit.Split(text, EnDe);

        Assert.Equal(2, parts.Count);
        Assert.StartsWith("White should", parts["en"]);
        Assert.StartsWith("Weiss sollte", parts["de"]);
    }

    /// <summary>Eine einsprachige Partie wird gar nicht erst befragt.</summary>
    [Fact]
    public void Split_eineSprache_gibtDenBlockZurueck()
    {
        var parts = CommentSplit.Split("Anything at all.", ["en"]);

        Assert.Single(parts);
        Assert.Equal("Anything at all.", parts["en"]);
    }

    [Fact]
    public void Split_leererText_gibtNichts()
        => Assert.Empty(CommentSplit.Split("   ", EnDe));

    /// <summary>Franzoesisch gegen Niederlaendisch: dort sagt ein <c>D</c> (Dame/Dame) nichts, ein
    /// <c>C</c> (Cavalier) und ein <c>P</c> (Paard) dagegen schon.</summary>
    [Fact]
    public void Split_zaehltNurUnterscheidendeFigurenbuchstaben()
    {
        const string text = "Le cavalier arrive avec Cf3 et la position est meilleure pour les blancs. "
            + "Het paard komt met Pf3 en de stelling is beter voor wit.";

        var parts = CommentSplit.Split(text, ["fr", "nl"]);

        Assert.Equal(2, parts.Count);
        Assert.Contains("Cf3", parts["fr"]);
        Assert.Contains("Pf3", parts["nl"]);
    }
}
