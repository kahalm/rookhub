using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace RookHub.Api.Services;

/// <summary>
/// SMTP-basierter E-Mail-Versand via MailKit.
///
/// Konfiguration (appsettings / env, Prefix <c>Email__</c>):
/// - <c>Email:SmtpHost</c>      SMTP-Server (leer = Versand deaktiviert → Log-Fallback)
/// - <c>Email:SmtpPort</c>      Port (Default 587)
/// - <c>Email:SmtpUser</c>      Login-User (optional; leer = anonym)
/// - <c>Email:SmtpPassword</c>  Login-Passwort
/// - <c>Email:FromAddress</c>   Absender-Adresse (Default: SmtpUser)
/// - <c>Email:FromName</c>      Absender-Anzeigename (Default „RookHub")
/// - <c>Email:UseStartTls</c>   true = STARTTLS (Default), false = nur bei Port 465 implizites SSL
///
/// Ohne <c>SmtpHost</c> ist <see cref="IsEnabled"/> false; <see cref="SendAsync"/> loggt die
/// Mail dann nur (inkl. Reset-Link) statt sie zu verschicken — so funktioniert der Flow lokal
/// ohne Mailserver.
/// </summary>
public class SmtpEmailSender : IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<SmtpEmailSender> _logger;
    private readonly IHostEnvironment _env;

    public SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger, IHostEnvironment env)
    {
        _config = config;
        _logger = logger;
        _env = env;
    }

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_config["Email:SmtpHost"]);

    public async Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        if (!IsEnabled)
        {
            if (_env.IsDevelopment())
            {
                // Dev-Fallback: kein Mailserver konfiguriert → Mail inkl. Reset-Link loggen,
                // damit der Flow lokal ohne Mailserver nachvollziehbar ist.
                _logger.LogWarning(
                    "Email disabled (no Email:SmtpHost) — would send to {To}: {Subject}\n{Body}",
                    toEmail, subject, textBody);
            }
            else
            {
                // Ausserhalb Development NIE den Klartext-Body (enthält den Roh-Token-Reset-Link)
                // nach ES schreiben. Nur die Tatsache loggen — ein fehlender SmtpHost in Prod ist
                // ein Konfigurationsfehler, kein erwarteter Zustand.
                _logger.LogError(
                    "Email disabled (no Email:SmtpHost) but a mail to {To} ({Subject}) was requested — " +
                    "message dropped. This is a misconfiguration in a non-Development environment.",
                    toEmail, subject);
            }
            return;
        }

        var fromAddress = _config["Email:FromAddress"] ?? _config["Email:SmtpUser"]
            ?? throw new InvalidOperationException("Email:FromAddress or Email:SmtpUser must be configured.");
        var fromName = _config["Email:FromName"] ?? "RookHub";

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(fromName, fromAddress));
        message.To.Add(MailboxAddress.Parse(toEmail));
        message.Subject = subject;
        message.Body = new BodyBuilder { HtmlBody = htmlBody, TextBody = textBody }.ToMessageBody();

        var host = _config["Email:SmtpHost"]!;
        var port = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
        var useStartTls = !bool.TryParse(_config["Email:UseStartTls"], out var s) || s;
        // Port 465 = implizites SSL; sonst STARTTLS (Default) bzw. unverschluesselt, wenn explizit aus.
        var socketOptions = port == 465
            ? SecureSocketOptions.SslOnConnect
            : useStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

        using var client = new SmtpClient();
        await client.ConnectAsync(host, port, socketOptions, ct);

        var user = _config["Email:SmtpUser"];
        var password = _config["Email:SmtpPassword"];
        if (!string.IsNullOrEmpty(user))
            await client.AuthenticateAsync(user, password, ct);

        await client.SendAsync(message, ct);
        await client.DisconnectAsync(true, ct);

        _logger.LogInformation("Email sent to {To}: {Subject}", toEmail, subject);
    }
}
