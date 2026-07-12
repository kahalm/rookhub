namespace RookHub.Api.Models;

/// <summary>
/// Feste, an Features gekoppelte Berechtigungs-Schlüssel (NICHT frei in der DB editierbar — Code ist
/// die Quelle der Wahrheit). Rollen (DB-Daten) werden aus diesen Konstanten bestückt; das Backend
/// prüft immer PERMISSIONS, nie Rollennamen → Endpoints bleiben stabil, wenn Rollen umdefiniert werden.
///
/// Granularität = ein Schlüssel je Admin-Bereich (deckt die bisher via <c>[Authorize(Roles="Admin")]</c>
/// bzw. <c>IsAdmin</c> geschützten Flächen ab). Die System-Rolle „admin" trägt ALLE Permissions
/// (Superuser); künftige Rollen (Trainer/Moderator) bekommen Teilmengen.
/// </summary>
public static class Permissions
{
    /// <summary>Nutzerverwaltung: Liste, Admin-Toggle, Impersonation, Konto-Löschung, Passwort-Reset.</summary>
    public const string UsersManage = "users.manage";

    /// <summary>Bücher/Kurse (Admin): Import, Löschen, Gruppen-Freigabe, Tipps generieren, Tag-Backfill.</summary>
    public const string BooksManage = "books.manage";

    /// <summary>Standard-Puzzles (Admin): Import, Tag-Backfill, En-passant-Tagging.</summary>
    public const string PuzzlesManage = "puzzles.manage";

    /// <summary>Tagespuzzle (Admin): Regenerieren, Einzel-Hint-Regeneration.</summary>
    public const string DailyManage = "daily.manage";

    /// <summary>Wochenposts (Admin): Anlegen/Ändern/Löschen, Aus-Kapitel, Spieler-Breakdown.</summary>
    public const string WeeklyPostsManage = "weeklyposts.manage";

    /// <summary>Gruppen (Admin): CRUD, Mitglieder, Trainingsziel-Vorlagen.</summary>
    public const string GroupsManage = "groups.manage";

    /// <summary>Direktnachrichten Admin-Seite: Threads sehen/beantworten/übernehmen.</summary>
    public const string MessagesAdmin = "messages.admin";

    /// <summary>Chessable-Admin: Importe aller User, Kurse „im Namen eines Users" holen.</summary>
    public const string ChessableAdmin = "chessable.admin";

    /// <summary>Admin-CI/Actions-Übersicht (GitHub-Actions-Läufe).</summary>
    public const string CiView = "ci.view";

    /// <summary>Menü-Sichtbarkeit konfigurieren.</summary>
    public const string MenuManage = "menu.manage";

    /// <summary>Katalog-Freigaben (CatalogGrants) verwalten.</summary>
    public const string CatalogManage = "catalog.manage";

    /// <summary>Rollen &amp; Berechtigungen verwalten (Rollen anlegen/bearbeiten/zuweisen). Sensibel —
    /// wer das hat, kann sich effektiv beliebige weitere Rechte geben; standardmäßig nur die admin-Rolle.</summary>
    public const string RolesManage = "roles.manage";

    /// <summary>Alle bekannten Permission-Schlüssel — Basis fürs Seeden der „admin"-Superuser-Rolle.</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        UsersManage, BooksManage, PuzzlesManage, DailyManage, WeeklyPostsManage,
        GroupsManage, MessagesAdmin, ChessableAdmin, CiView, MenuManage, CatalogManage, RolesManage,
    };
}
