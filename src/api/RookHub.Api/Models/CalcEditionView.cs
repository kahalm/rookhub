namespace RookHub.Api.Models;

/// <summary>
/// „Gesehen"-Vermerk der Kalkulations-Serie (Phase 3): ein Verteiler-Mitglied hat eine Stellung einer
/// terminierten Woche geöffnet. Einmalig je Ausgabe + Nutzer (UNIQUE). Dient dem Autor als Übersicht,
/// wer eine freigegebene Woche schon bearbeitet hat. Besitzer/Admin und öffentliche Betrachter zählen
/// bewusst NICHT — nur die Mitglieder des privaten Verteilers.
/// </summary>
public class CalcEditionView
{
    public int Id { get; set; }

    public int CalcEditionId { get; set; }
    public CalcEdition? Edition { get; set; }

    public int UserId { get; set; }

    public DateTime ViewedAt { get; set; } = DateTime.UtcNow;
}
