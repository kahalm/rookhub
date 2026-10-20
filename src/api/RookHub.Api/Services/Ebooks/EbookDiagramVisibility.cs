using RookHub.Api.Models;

namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Diagramme mit unplausibler Brettlesung (0.811.0, „ergänzen“): statt das Bild zu verwerfen, legt die Erkennung im
/// Ergänzen-Modus ein Diagramm mit der rohen Lesung und <see cref="EbookDiagram.FontFamily"/> = <see cref="Unread"/> an. Solche
/// Diagramme sind für Nutzer UNSICHTBAR (Leser, Lösungsprüfung, Kurse, Zählung), bis jemand sie korrigiert und die Familie
/// ändert (z. B. <c>opus</c>). Nur Admin-/Werkzeug-Abfragen sehen sie.
/// </summary>
public static class EbookDiagramVisibility
{
    public const string Unread = "unread";

    /// <summary>Nur die Diagramme, die Nutzer sehen dürfen (alles außer <see cref="Unread"/>).</summary>
    public static IQueryable<EbookDiagram> Readable(this IQueryable<EbookDiagram> diagrams)
        => diagrams.Where(d => d.FontFamily == null || d.FontFamily != Unread);

    public static bool IsReadable(EbookDiagram d) => d.FontFamily != Unread;
}
