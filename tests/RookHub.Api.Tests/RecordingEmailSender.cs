using RookHub.Api.Services;

namespace RookHub.Api.Tests;

/// <summary>Merkt sich jede „verschickte" Mail, statt sie zu senden — für Tests, die prüfen, OB und
/// WOHIN eine Mail ginge. Mit <see cref="Fail"/> wirft der Versand wie ein ausgefallener SMTP-Server.</summary>
internal sealed class RecordingEmailSender : IEmailSender
{
    public sealed record SentMail(string To, string Subject, string Html, string Text);

    public List<SentMail> Sent { get; } = new();
    public bool Fail { get; init; }
    public bool IsEnabled => true;

    public Task SendAsync(string toEmail, string subject, string htmlBody, string textBody, CancellationToken ct = default)
    {
        if (Fail) throw new InvalidOperationException("SMTP down");
        Sent.Add(new SentMail(toEmail, subject, htmlBody, textBody));
        return Task.CompletedTask;
    }
}
