namespace RookHub.Api.Services;

/// <summary>
/// Die Praefix-Suche ueber eine Eroeffnungszeile (<c>OpeningLine</c>, „e4 e5 Nf3"): „alle Partien,
/// die nach dieser Zugfolge weitergehen". Gesucht wird mit <c>LIKE 'praefix %'</c>, weil genau diese
/// Form den Index trifft (Messwerte in <see cref="GuessOpeningTree"/>) — und der Praefix kommt aus
/// ANONYMER Eingabe (<c>/api/guess-tree</c>, <c>/api/library-games</c>, <c>/api/game-analyses/public</c>).
///
/// <para>Bis 0.624.0 bauten vier Stellen das Muster als <c>prefix + " %"</c> selbst und maskierten
/// nichts: <c>?line=_4</c> traf „e4 …" UND „d4 …" und zaehlte beide Eroeffnungen in einen Ast,
/// <c>?line=%</c> den ganzen Bestand (Codereview 2026-09-29, A6-017). <see cref="SimilarGamesService"/>
/// nimmt <c>StartsWith</c> (dort maskiert EF selbst); die Stellen hier bleiben bei <c>LIKE</c> mit
/// fertig berechnetem Muster, damit die Abfrage ein einfacher Parameter bleibt.</para>
/// </summary>
public static class OpeningLines
{
    /// <summary>
    /// Escape-Zeichen der Muster — fuer <c>EF.Functions.Like(…, …, LikeEscape)</c> und als
    /// <c>ESCAPE '!'</c> im rohen SQL. Bewusst NICHT der Backslash: ob MariaDB ihn in einem
    /// String-Literal selbst schon verschluckt, haengt am <c>sql_mode</c> (<c>NO_BACKSLASH_ESCAPES</c>),
    /// und ein „!" steht nach <see cref="GuessOpeningTree.Normalize"/> ohnehin nie in der Zeile.
    /// </summary>
    public const string LikeEscape = "!";

    /// <summary>LIKE-Muster fuer „geht nach <paramref name="prefix"/> weiter": der maskierte Praefix
    /// plus <c>" %"</c>. Die Partie, die GENAU dort endet, fragt der Aufrufer mit <c>== prefix</c> dazu.</summary>
    public static string ContinuationPattern(string prefix) => EscapeLike(prefix) + " %";

    /// <summary><c>%</c>, <c>_</c> und das Escape-Zeichen selbst als Literal.</summary>
    internal static string EscapeLike(string text)
        => text.Replace(LikeEscape, LikeEscape + LikeEscape)
               .Replace("%", LikeEscape + "%")
               .Replace("_", LikeEscape + "_");
}
