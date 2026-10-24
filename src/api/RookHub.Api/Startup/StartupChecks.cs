using RookHub.Api.Services;
using RookHub.Api.Services.Accounts;

namespace RookHub.Api.Startup;

/// <summary>Startabbruch bei unbrauchbaren Schlüsseln, noch bevor ein Dienst sie braucht.</summary>
public static class StartupChecks
{
    public static void ThrowIfCryptoKeysInvalid(IConfiguration configuration, IHostEnvironment environment)
    {
        // Fail-fast gegen einen LEEREN Verschlüsselungs-Schlüssel. `EncryptionService` ist ein
        // Singleton und wird erst beim ersten Chessable-Request erzeugt — ein Konfigurationsfehler
        // fiele sonst mitten im Betrieb auf statt beim Start. Genau dieser Fall ist realistisch:
        // `${ENCRYPTION_KEY:-}` in einer Compose-Datei liefert bei ungesetzter Variable einen
        // LEERSTRING (nicht "fehlt"), und ohne diese Prüfung würde damit mit SHA256("") — einem
        // öffentlich bekannten Fixwert — „verschlüsselt". Ein komplett FEHLENDER Schlüssel ist nur mit
        // Chessable:Enabled=false erlaubt (A3-017): früher galt er pauschal als erlaubt, der Dienst warf dann
        // aber bei jeder Auflösung, und die ganze Extension-API antwortete 500. Ohne Chessable wirft er jetzt
        // erst beim Ver-/Entschlüsseln (Lichess-Engine-Zugang speichern), der Rest läuft.
        var configuredEncryptionKey = configuration["Encryption:Key"];
        if (configuredEncryptionKey != null && string.IsNullOrWhiteSpace(configuredEncryptionKey))
            throw new InvalidOperationException(
                "Encryption:Key ist gesetzt, aber leer. Entweder einen echten Schlüssel hinterlegen " +
                "(ENCRYPTION_KEY) oder die Variable ganz weglassen — ein leerer Wert würde den " +
                "gespeicherten Chessable-Bearer nur zum Schein verschlüsseln.");
        EncryptionService.ThrowIfKeyRequiredButMissing(configuration);
        // Platzhalter aus den öffentlichen Beispiel-Dateien („change_me…", „your_…") als JWT- oder
        // Verschlüsselungsschlüssel: in Production Startabbruch. Der JWT-Platzhalter hat 43 Byte und
        // bestand die Längenprüfung in AuthSetup — jeder Leser des Repos hätte sich ein Admin-JWT signieren können.
        SecretConfigCheck.ThrowIfCryptoKeyIsPlaceholder(configuration, environment);
    }
}
