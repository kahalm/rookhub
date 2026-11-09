using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RookHub.Api.Models;
using RookHub.Api.Services.Library;

namespace RookHub.Api.Services.Games;

/// <summary>
/// Freitextsuche der Partienliste (<c>GET /api/games?q=</c>): jedes Wort muss irgendwo treffen (UND), Groß-/Kleinschreibung
/// egal. Durchsucht die Spalten (Weiß, Schwarz, Quelle, Klassifizierer, Tags) und die Kopfzeilen des PGN (Event, Site, Opening,
/// Variation, ECO) — die stehen nirgends als Spalte. Die Züge und Kommentare des PGN werden bewusst NICHT durchsucht.
/// </summary>
internal static class SavedGameSearch
{
    /// <summary>Längere Eingaben werden abgeschnitten; die Oberfläche begrenzt das Feld genauso.</summary>
    internal const int MaxQueryLength = 200;

    /// <summary>Jedes Wort ist eine eigene Bedingung über alle Felder — der Deckel begrenzt die Abfrage.</summary>
    internal const int MaxTerms = 8;

    /// <summary>
    /// Die Wörter der Eingabe; leer = keine Suche.
    /// </summary>
    internal static List<string> Terms(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new();
        var text = query.Length > MaxQueryLength ? query[..MaxQueryLength] : query;
        return text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTerms)
            .ToList();
    }

    /// <summary>
    /// Regulärer Ausdruck „dieses Wort im Wert einer Such-Kopfzeile". Am Textanfang verankert und nur über Zeilen, die mit
    /// <c>[</c> beginnen: die Prüfung endet an der ersten Zugzeile, auch ein langes kommentiertes PGN kostet also nur seinen
    /// Kopf. Läuft in .NET (InMemory) und in MariaDB (PCRE2); <c>(?i)</c> statt RegexOptions, weil der Provider nur das
    /// Muster übersetzt.
    /// </summary>
    internal static string HeaderPattern(string term)
        => @"(?i)\A\s*(?:\[[^\n]*\n)*?\[(?:Event|Site|Opening|Variation|ECO) ""[^""\n]*" + Regex.Escape(term);

    /// <summary>Schränkt die Partien auf die Treffer ein. Der Aufrufer filtert vorher auf den Besitzer (höchstens
    /// <see cref="SavedGameService.MaxGamesPerUser"/> Zeilen) — ein Volltextindex lohnt sich dafür nicht.</summary>
    internal static IQueryable<SavedGame> Apply(IQueryable<SavedGame> games, string? query)
    {
        foreach (var term in Terms(query))
        {
            var like = "%" + OpeningLines.EscapeLike(term) + "%";
            var header = HeaderPattern(term);
            games = games.Where(g =>
                EF.Functions.Like(g.White!, like, OpeningLines.LikeEscape)
                || EF.Functions.Like(g.Black!, like, OpeningLines.LikeEscape)
                || EF.Functions.Like(g.Source, like, OpeningLines.LikeEscape)
                || EF.Functions.Like(g.Classifier1!, like, OpeningLines.LikeEscape)
                || EF.Functions.Like(g.Classifier2!, like, OpeningLines.LikeEscape)
                || EF.Functions.Like(g.Tags!, like, OpeningLines.LikeEscape)
                || Regex.IsMatch(g.Pgn, header));
        }
        return games;
    }
}
