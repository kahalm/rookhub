namespace RookHub.Api.Exceptions;

/// <summary>
/// Stabile, maschinenlesbare Fehlercodes im Feld <c>code</c> neben <c>message</c> (Codereview 2026-09-29, F5-019).
///
/// <para><b>Warum:</b> Die Oberfläche zeigte <c>err.error.message</c> roh — und die Sprache hing am Dienst, nicht an der
/// UI: der deutsche Nutzer las „A friendship or request already exists.", der kroatische Admin „Eine Rolle mit diesem
/// Key existiert bereits.". Mit dem Code übersetzt das Frontend selbst (<c>apiErrors.&lt;code&gt;</c> in den
/// i18n-Dateien, <c>core/api-error.ts</c>); <c>message</c> bleibt unverändert und ist der Rückfall für Clients, die den
/// Code (noch) nicht kennen — Extension, Bot, ältere PWA-Stände.</para>
///
/// <para><b>Vertrag:</b> Ein Code wird nie umbenannt oder für etwas anderes wiederverwendet; snake_case wie die
/// älteren Codes (<c>repertoire_empty</c>, <c>api_token_scope</c>). Ein neuer Code braucht einen Schlüssel
/// <c>apiErrors.&lt;code&gt;</c> in en/de/hr/hu, sonst zeigt das Frontend weiter die Meldung.</para>
/// </summary>
public static class ApiErrorCodes
{
    public const string RateLimited = "rate_limited";

    public const string LoginInvalid = "login_invalid";
    public const string LoginThrottled = "login_throttled";
    public const string AccountLocked = "account_locked";

    public const string UserNotFound = "user_not_found";
    public const string FriendRequestSelf = "friend_request_self";
    public const string FriendshipExists = "friendship_exists";

    public const string TokenLimitReached = "token_limit_reached";

    public const string AdminSelfDelete = "admin_self_delete";
    public const string AdminSelfChange = "admin_self_change";
    public const string UserDeleteReferences = "user_delete_references";

    public const string RoleKeyTaken = "role_key_taken";
    public const string RoleSystemProtected = "role_system_protected";
    public const string GroupNameTaken = "group_name_taken";

    public const string BookAliasInvalid = "book_alias_invalid";
    public const string BookAliasReserved = "book_alias_reserved";
    public const string BookAliasTaken = "book_alias_taken";

    /// <summary>Kurs-Upload mit einer PGN ohne eine einzige Kurs-Linie (keine Partie mit [FEN] UND [Round]) —
    /// meist ein Eröffnungsrepertoire aus ChessBase/Lichess, das unter „Repertoires“ hingehört.</summary>
    public const string CoursePgnIsRepertoire = "course_pgn_is_repertoire";
    /// <summary>Kurs-Upload, aus dem keine einzige spielbare Linie entstand (Linien da, aber alle unbrauchbar).</summary>
    public const string CourseNoLines = "course_no_lines";
}
