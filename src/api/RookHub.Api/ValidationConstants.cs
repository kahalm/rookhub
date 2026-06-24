namespace RookHub.Api;

/// <summary>Gemeinsame Validierungs-Konstanten (vermeidet duplizierte Magic-Patterns).</summary>
public static class ValidationConstants
{
    /// <summary>
    /// Anonyme Session-/Visitor-Id: Hex-Ziffern + Bindestrich, 32–36 Zeichen (UUID-Form).
    /// Genutzt für Endless-/Puzzle-/Buch-Anon-Sessions und den X-Visitor-Id-Header.
    ///
    /// Die Untergrenze von 32 Zeichen härtet gegen IDOR: anonyme Puzzle-/Endless-Stats sind nur
    /// über die Session-Id partitioniert, und ein zu kurzer/erratbarer Wert (z. B. "1", "abc")
    /// ließe sich fremde Sessions claimen/überschreiben. Alle Clients vergeben die Id per
    /// <c>crypto.randomUUID()</c> (36 Zeichen, 122 Bit Entropie) bzw. UUID-ohne-Bindestriche
    /// (32 Hex) → effektiv unerratbar; die Mindestlänge bindet Schreibzugriffe an diese Entropie,
    /// ohne bestehende Clients zu brechen.
    /// </summary>
    public const string SessionIdPattern = @"^[a-fA-F0-9\-]{32,36}$";
}
