using System.Diagnostics;
using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>
/// Feindliche Eingaben für die Kommentar-/Marker-Muster des <see cref="PgnParser"/> (Codereview 2026-09-29,
/// N3-009). Im Rückverfolgungs-Modus rechneten <c>\{[^}]*\}</c> (nicht geschlossene „{"),
/// <c>\[%\w+[^\]]*\]</c>, <c>\[%(cal|csl)\s+([^\]]*)\]</c> und <c>\[%alt\s+([^\]]*)\]</c> (überlappende
/// Quantoren) QUADRATISCH: 640 000 Zeichen kosteten je Muster 4,5–7,7 s, 5 Mio. — der Deckel des
/// LeagueHub-Imports, ohne Anmeldung über den Teilen-Link erreichbar — Minuten. Mit NonBacktracking ist
/// jeder Aufruf linear; die Grenze unten lässt Faktor 50 Luft für einen langsamen CI-Rechner, die alte
/// Fassung reißt sie um ein Vielfaches.
/// </summary>
public class PgnParserHostileInputTests
{
    private const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
    private const int N = 1_000_000;
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(2);

    public static IEnumerable<object[]> Cases() => new[]
    {
        // Nur „{", nie geschlossen — Hauptvariante wie im LeagueHub-Import (CommentRegex).
        new object[] { "mainline-open-braces", (Action)(() => PgnParser.ExtractMainlineSans(new string('{', N))) },
        new object[] { "first-comment-open-braces", (Action)(() => PgnParser.ExtractFirstComment(new string('{', N))) },
        // „[%" + ein langes Wort ohne „]" in einem Kommentar (AnnotationRegex, eine einzige Startstelle).
        new object[] { "annotation-long-word", (Action)(() => PgnParser.ExtractMoveComments("{[%" + new string('a', N) + "}")) },
        // Viele „[%a" ohne „]" (AnnotationRegex, viele Startstellen).
        new object[] { "annotation-many-starts", (Action)(() => PgnParser.ExtractMoveComments("{" + string.Concat(Enumerable.Repeat("[%a", N / 3)) + "}")) },
        // „[%cal" + Leerraum ohne „]" (CalCslRegex).
        new object[] { "calcsl-long-space", (Action)(() => PgnParser.ExtractMoveShapes("1. e4 {[%cal" + new string(' ', N) + "}")) },
        // „[%alt" + Leerraum ohne „]" (AltRegex).
        new object[] { "alt-long-space", (Action)(() => PgnParser.ExtractAltMoves(Start, "1. e4 {[%alt" + new string(' ', N) + "} e5 *")) },
        // Download-Pfad: interne Marker ohne „]" (InternalMarkerRegex).
        new object[] { "internal-marker-many-starts", (Action)(() => PgnParser.StripInternalMarkers(string.Concat(Enumerable.Repeat("[%alt", N / 5)))) },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void HostileInput_StaysLinear(string name, Action run)
    {
        run();                                   // Aufwärmen: der Automat wird beim ersten Aufruf gebaut
        var sw = Stopwatch.StartNew();
        run();
        sw.Stop();
        Assert.True(sw.Elapsed < Limit, $"{name}: {sw.ElapsedMilliseconds} ms für {N} Zeichen");
    }

    /// <summary>Der ganze Weg des LeagueHub-Imports (Partien trennen, Hauptvariante, Nachspielen) über eine
    /// 5-Mio.-Zeichen-Eingabe aus „{" — genau das, was ein anonymer Aufruf über den Teilen-Link schicken darf
    /// (<c>LeagueClubService.MaxImportChars</c>). Endet als unlesbare Partie, nicht als Minuten CPU.</summary>
    [Fact]
    public void LeagueImportPath_OpenBracesAtCap_StaysFast()
    {
        var pgn = "[Event \"x\"]\n[White \"a\"]\n[Black \"b\"]\n\n" + new string('{', 5_000_000 - 40);
        var sw = Stopwatch.StartNew();
        foreach (var (_, moveText) in PgnParser.SplitGames(pgn))
        {
            PgnParser.ExtractMainlineSans(moveText);
            Assert.Null(PgnParser.TryExtractUciMainline(Start, moveText));
        }
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(5), $"{sw.ElapsedMilliseconds} ms");
    }

    /// <summary>Gruppen wie bisher: gieriges <c>\s+</c> vor der Gruppe (führender Leerraum gehört nicht zur
    /// Gruppe), der Treffer endet an der ERSTEN „]".</summary>
    [Fact]
    public void Markers_KeepTheirMatchesAndGroups()
    {
        Assert.Equal("a b", PgnParser.ExtractFirstComment("{ a [%tqu x] b } 1. e4"));
        Assert.Equal("{ x", PgnParser.ExtractFirstComment("{ { x } y }"));
        var shapes = PgnParser.ExtractMoveShapes("1. e4 {[%cal \t Ge2e4, Rd2d4 ][%csl Yd4]}");
        Assert.NotNull(shapes);
        Assert.Equal(new[] { "e2>e4:green", "d2>d4:red", "d4>:yellow" },
            shapes![0].Select(s => $"{s.O}>{s.D}:{s.B}"));
        var alts = PgnParser.ExtractAltMoves(Start, "1. e4 {[%alt \t d4  c4]} e5 *");
        Assert.Equal(new[] { "d2d4", "c2c4" }, alts![0]);
        Assert.Equal("1. e4 { keep } e5", PgnParser.StripInternalMarkers("1. e4 {[%ALT d4] keep [%info]} {[%alt c4]} e5"));
    }
}
