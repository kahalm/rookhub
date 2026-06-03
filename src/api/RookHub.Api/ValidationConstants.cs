namespace RookHub.Api;

/// <summary>Gemeinsame Validierungs-Konstanten (vermeidet duplizierte Magic-Patterns).</summary>
public static class ValidationConstants
{
    /// <summary>
    /// Anonyme Session-/Visitor-Id: Hex-Ziffern + Bindestrich, 1–36 Zeichen (UUID-Form).
    /// Genutzt für Endless-/Puzzle-/Buch-Anon-Sessions und den X-Visitor-Id-Header.
    /// </summary>
    public const string SessionIdPattern = @"^[a-fA-F0-9\-]{1,36}$";
}
