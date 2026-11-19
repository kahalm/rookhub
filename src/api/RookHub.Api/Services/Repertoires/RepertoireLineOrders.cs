namespace RookHub.Api.Services.Repertoires;

/// <summary>
/// Die erlaubten Reihenfolgen der Linien im Repertoire-Trainer (0.996.0, je Nutzer und Repertoire einstellbar).
/// Der Server speichert nur den Wert; gereiht wird im Frontend (dort liegen Linien, Fälligkeit und Häufigkeiten).
///
/// <para><b>Handgespiegelt</b>: dieselbe Liste steht im Frontend als <c>LINE_ORDERS</c>
/// (<c>features/repertoire/repertoire-line-order.ts</c>). Beide Seiten nageln sie mit LITERALEN Werten fest
/// (<c>RepertoireLineOrdersTests</c> ↔ <c>repertoire-line-order.spec.ts</c>) — ein Wert, den nur eine Seite kennt,
/// wäre eine Auswahl, die beim Speichern mit 400 scheitert bzw. beim Lesen still auf die Vorgabe fällt.</para>
/// </summary>
public static class RepertoireLineOrders
{
    /// <summary>Wenig trainierte zuerst: kürzeste Wiederkehr bei richtiger Lösung zuerst (Stufe 0 vor 1 vor 2 …). Vorgabe.</summary>
    public const string LeastTrained = "leastTrained";
    /// <summary>Viel trainierte zuerst — die Umkehrung.</summary>
    public const string MostTrained = "mostTrained";
    /// <summary>Zufällig (gemischt).</summary>
    public const string Random = "random";
    /// <summary>Reihenfolge im Repertoire (PGN).</summary>
    public const string Repertoire = "repertoire";
    /// <summary>Häufigste zuerst (Lichess-Explorer) — der frühere Geräte-Schalter des Trainers.</summary>
    public const string Frequency = "frequency";

    /// <summary>Gilt, solange der Nutzer für das Repertoire nichts gewählt hat. Gespeichert wird sie nicht —
    /// „nichts gewählt“ bleibt <c>null</c>, damit das Frontend den alten Geräte-Schalter „Häufigste zuerst“ weiter ehren kann.</summary>
    public const string Default = LeastTrained;

    public static readonly IReadOnlyList<string> All = [LeastTrained, MostTrained, Random, Repertoire, Frequency];

    /// <summary>Genau einer der erlaubten Werte (Groß-/Kleinschreibung zählt, kein Trimmen).</summary>
    public static bool IsValid(string? order) => order is not null && All.Contains(order, StringComparer.Ordinal);
}
