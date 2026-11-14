namespace RookHub.Api.DTOs.Courses;

/// <summary>
/// Eingabe für <c>POST /api/courses/personal-tactics/from-game</c>: die Partie auf demselben Weg, auf dem die Seite ihre
/// Empfohlenen Übungen liest. <c>Kind</c>: <c>club</c> (<c>Id</c>), <c>own</c> (<c>Id</c>), <c>club-share</c> (Liga-Teilen-Link:
/// <c>Token</c> + <c>Id</c>), <c>club-link</c> (<c>/cg/{token}</c>: <c>Token</c>), <c>own-share</c> (<c>/g/{token}</c>: <c>Token</c>).
/// <c>Lang</c> = UI-Sprache, bestimmt nur den Namen des Kurses beim ersten Anlegen.
/// </summary>
public class PersonalTacticsFromGameDto
{
    public string? Kind { get; set; }
    public int? Id { get; set; }
    public string? Token { get; set; }
    public string? Lang { get; set; }
}

/// <summary>Das Kapitel der Partie im Kurs „Persönliche Taktiken“. <c>ChapterIndex</c> = Kapitel-Index des Lösers
/// (<c>/courses/{bookId}/chapter/{index}/sequential</c>); <c>null</c> nur, wenn das Kapitel keine abfragbare Linie mehr hat.
/// <c>Created</c> = eben angelegt (sonst gab es das Kapitel schon).</summary>
public class PersonalTacticsChapterDto
{
    public int BookId { get; set; }
    public int? ChapterIndex { get; set; }
    public string? Chapter { get; set; }
    public bool Created { get; set; }
    public int Lines { get; set; }
}
