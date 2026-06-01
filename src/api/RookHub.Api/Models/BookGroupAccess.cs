namespace RookHub.Api.Models;

/// <summary>
/// Erlaubt einer <see cref="Group"/> den Zugriff auf ein <see cref="Book"/> als Kurs.
/// n:m zwischen Book und Group (Composite PK BookId+GroupId). Admins sehen ohnehin alle
/// Bücher; für Nicht-Admins entscheidet die Schnittmenge ihrer Gruppen mit diesen Einträgen,
/// ob ein Buch im „Kurse"-Menü sichtbar ist.
/// </summary>
public class BookGroupAccess
{
    public int BookId { get; set; }
    public Book? Book { get; set; }

    public int GroupId { get; set; }
    public Group? Group { get; set; }
}
