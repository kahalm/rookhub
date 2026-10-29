namespace RookHub.Api.Services.Ebooks;

/// <summary>
/// Buch-Übersetzung (0.876.0, Abschnitt <c>EbookTranslation</c>, ENV <c>EbookTranslation__…</c>).
///
/// <para><see cref="Languages"/> schaltet ein: LEER = aus (Vorgabe, auch auf Dev — Dev und Prod teilen sich die Spark, und
/// denselben Bestand zweimal zu übersetzen kostete Wochen Nachtarbeit). Prod bekommt <c>en</c> über
/// <c>EBOOK_TRANSLATION_LANGUAGES</c> in der <c>.env</c>. Mehrere Zielsprachen gehen (Komma-Liste), heute gedacht ist nur <c>en</c>.</para>
/// </summary>
public class EbookTranslationOptions
{
    public const string Section = "EbookTranslation";

    /// <summary>Zielsprachen als Komma-Liste (ISO 639-1, z. B. <c>en</c>); leer = Dienst aus.</summary>
    public string? Languages { get; set; }

    /// <summary>Fuhren je Buch gleichzeitig an der Spark (Vorgabe 8). Die Spark (vLLM, max-num-seqs ≈ 64) teilt ihren Durchsatz
    /// auf gleichzeitige Anfragen auf — mehr parallel lohnt sich bis weit über 8; die Obergrenze für ALLE LLM-Zwecke zusammen ist
    /// aber <c>TextLlm:MaxConcurrent</c> (Vorgabe 8) — beide heben, wenn es schneller gehen soll.</summary>
    public int Parallel { get; set; } = 8;

    /// <summary>So viele Zeichen Text gehen höchstens in eine Anfrage (kleiner als bei Kursen: ein Fehlschlag kostet weniger,
    /// und mehr Anfragen laufen parallel).</summary>
    public int ChunkChars { get; set; } = 3000;

    /// <summary>Höchstens so viele Blöcke je Anfrage.</summary>
    public int ChunkItems { get; set; } = 40;

    /// <summary>Die Zielsprachen bereinigt: klein, zwei Buchstaben, ohne Dubletten, in Reihenfolge.</summary>
    public IReadOnlyList<string> Targets() =>
        (Languages ?? "").Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.ToLowerInvariant())
            .Where(l => l.Length == 2 && l.All(char.IsAsciiLetterLower))
            .Distinct().ToList();
}
