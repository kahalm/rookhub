namespace RookHub.Api.Services;

/// <summary>
/// In welche Sprachen ein Kurs uebersetzt werden darf: die 25 Oberflaechensprachen (Entscheidung des Nutzers,
/// 2026-09-26 — „jeder, in jede der 25"). Handgespiegelt von <c>SUPPORTED_LANGS</c> in
/// <c>src/frontend/app/src/app/core/locale.service.ts</c>; beide Seiten halten die Liste mit LITERALEN Werten in
/// einem Test fest (<c>CourseTranslationLanguagesTests</c> ↔ <c>locale.service.spec.ts</c>) — kommt eine Sprache
/// dazu, muss sie auf beiden Seiten dazukommen, sonst bietet die Oberflaeche eine Sprache an, die der Server mit
/// <c>unsupported-language</c> ablehnt.
///
/// <para>Die QUELLSPRACHE eines Kurses ist davon unabhaengig (<see cref="Models.Book.CommentLanguage"/>, nur auf die
/// Form geprueft): ein Kurs darf auf Daenisch geschrieben sein, uebersetzt wird nur in eine Oberflaechensprache.</para>
/// </summary>
public static class CourseTranslationLanguages
{
    public static readonly IReadOnlyList<string> Supported =
    [
        "en", "de", "hr", "es", "fr", "it", "pt", "nl", "sv", "pl", "cs", "ro", "hu",
        "el", "tr", "ru", "uk", "ar", "fa", "hi", "id", "vi", "zh", "ja", "ko",
    ];

    private static readonly HashSet<string> Set = new(Supported, StringComparer.Ordinal);

    /// <summary>Das Kuerzel in der Form, in der es gespeichert wird (klein, getrimmt) — <c>null</c>, wenn es keine der
    /// 25 Sprachen ist.</summary>
    public static string? Normalize(string? language)
    {
        var lang = CourseCommentLocalizer.NormalizeLanguage(language);
        return lang is not null && Set.Contains(lang) ? lang : null;
    }

    /// <summary>Die Sprachen der Automatik aus der Einstellung (<c>CourseTranslation:AutoLanguages</c>, Komma-Liste) —
    /// unbekannte und doppelte fallen weg, die Reihenfolge bleibt.</summary>
    public static IReadOnlyList<string> ParseList(string? csv)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(csv)) return list;
        foreach (var part in csv.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Normalize(part) is { } lang && !list.Contains(lang)) list.Add(lang);
        return list;
    }
}
