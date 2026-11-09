namespace RookHub.Api.DTOs.Engines;

/// <summary>Eine Buchstelle eines Merksatzes (0.917.0); <see cref="Accessible"/> = der Aufrufer darf das Buch öffnen (sonst
/// nur Autor/Titel/Stelle/Zitat, kein Link).</summary>
public sealed record ChessMaximSourceDto(int EbookId, string? Author, string? Title, string? Year, string? Location, int? Page,
    int? Percent, string? Quote, bool Accessible);

/// <summary>Ein Merksatz der kuratierten Liste mit „wann er passt“ (Erklärung) und seinen Buchstellen. Beide Sprachen kommen mit, die
/// Oberfläche wählt: so wechselt die Anzeige sofort mit der Sprache, ohne neuen Abruf.</summary>
public sealed record ChessMaximDto(string Id, string Text, string Category, string When, string TextEn, string CategoryEn, string WhenEn,
    List<ChessMaximSourceDto> Sources);
