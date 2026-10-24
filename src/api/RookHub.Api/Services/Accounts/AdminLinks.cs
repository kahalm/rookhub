namespace RookHub.Api.Services.Accounts;

/// <summary>
/// Glocken-/Nachrichtenlinks in den Admin-Bereich — EINE Stelle für die Tab-Keys (F5-005-Nachlauf). Seit der Admin-Bereich
/// je Recht aufgeht, öffnet ein Link ohne <c>?tab=</c> den ERSTEN erlaubten Tab, nicht den, um den es geht. Die Keys
/// spiegeln <c>ADMIN_TAB_KEYS</c> in <c>src/frontend/app/src/app/features/admin/admin-tabs.ts</c>;
/// <c>AdminLinksTests</c> liest die Datei und hält beide Seiten gleich.
/// </summary>
public static class AdminLinks
{
    /// <summary>Tab-Keys, die das Backend verlinkt (Teilmenge von <c>ADMIN_TAB_KEYS</c>).</summary>
    public const string UsersTab = "users";
    public const string MessagesTab = "messages";
    public const string CourseDlTab = "courseDl";
    public const string UploadsTab = "uploads";

    /// <summary>Nutzerverwaltung (users.manage) — Glocke „neu registriert".</summary>
    public const string Users = "/admin?tab=" + UsersTab;
    /// <summary>Chessable-Kurse/-Konten (chessable.admin) — Glocken „Bearer hinterlegt" und „neuer Kurs".</summary>
    public const string CourseDl = "/admin?tab=" + CourseDlTab;
    /// <summary>LeagueHub-Stapel-Uploads (messages.admin).</summary>
    public const string Uploads = "/admin?tab=" + UploadsTab;

    /// <summary>Nachrichten-Tab mit geöffneter Konversation des Users (messages.admin).</summary>
    public static string MessagesThread(int userId) => $"/admin?tab={MessagesTab}&thread={userId}";

    /// <summary>Alle Keys, die oben vorkommen — für den Spiegeltest.</summary>
    public static readonly IReadOnlyList<string> LinkedTabs = [UsersTab, MessagesTab, CourseDlTab, UploadsTab];
}
