namespace RookHub.Api.Services;

/// <summary>
/// Versendet transaktionale E-Mails (aktuell: Passwort-Reset). Implementierungen:
/// <see cref="SmtpEmailSender"/> (echtes SMTP, wenn konfiguriert) bzw. der eingebaute
/// Log-Fallback, der bei fehlender Konfiguration die Mail nur ins Log schreibt (Dev).
/// </summary>
public interface IEmailSender
{
    /// <summary>True, wenn ein echter SMTP-Versand konfiguriert ist (sonst Log-Fallback).</summary>
    bool IsEnabled { get; }

    /// <summary>Schickt eine E-Mail. Wirft bei SMTP-Fehlern (Aufrufer behandelt/loggt).</summary>
    Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default);
}
