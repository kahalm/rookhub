namespace RookHub.Api.Services.Scoresheet;

/// <summary>Ein hochgeladenes Foto (eine Seite des Formulars).</summary>
/// <param name="SheetPage">Seite des Formulars, zu der das Foto gehört (0.736.0); 0 = jedes Foto eine eigene Seite.</param>
public sealed record ScoresheetUpload(byte[] Data, string? ContentType, string? FileName, int SheetPage = 0);
