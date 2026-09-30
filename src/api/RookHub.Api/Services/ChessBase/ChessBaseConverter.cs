namespace RookHub.Api.Services.ChessBase;

/// <summary>
/// Eine ChessBase-Datenbank (klassisch <c>.cbh</c> oder <c>.2cbh</c>) → PGN der Hauptvarianten, für die Vereins-Datenbank
/// (Wunsch 2026-09-29: „ein Import für 2cbh und cbh zusätzlich zu PGN — bau es selbst in C# nach"). Danach läuft alles
/// wie bei einer PGN-Datei (Übersicht, Entwurf, Import in Portionen). Eigener Code nach der reverse-engineerten
/// Beschreibung in Morphy (<c>format/v1</c>, <c>format/v2</c>); ChessBase dokumentiert beide Formate nicht.
/// </summary>
public static class ChessBaseConverter
{
    /// <summary>Was die Umwandlung ergibt: das PGN, die Partien (auch die übersprungenen, mit Grund) und was fehlt.</summary>
    public sealed record Result(ChessBaseFormat Format, string Pgn, IReadOnlyList<ChessBaseGame> Games, int Deleted, int Texts, bool Truncated)
    {
        public int Converted => Games.Count(g => g.Error == null);
    }

    /// <param name="ct">Bricht das Lesen ab — auch mitten im Zugstrom einer Partie (<see cref="ChessBaseImportService"/>
    /// hängt sein Zeitbudget daran).</param>
    public static Result Convert(ChessBaseFiles files, int maxGames, CancellationToken ct = default)
    {
        var read = files.Format == ChessBaseFormat.Cb2 ? Cb2Reader.Read(files, maxGames, ct) : CbhReader.Read(files, maxGames, ct);
        return new Result(read.Format, ChessBaseFields.ToPgn(read.Games), read.Games, read.Deleted, read.Texts, read.Truncated);
    }
}
