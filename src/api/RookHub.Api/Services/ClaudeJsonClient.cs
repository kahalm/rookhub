using System.Text.Json;
using Anthropic.Models.Messages;

namespace RookHub.Api.Services;

/// <summary>
/// Schmale Abstraktion über die Claude-API für die Tipp-Generierung: liefert für (System, User-Prompt)
/// die rohe JSON-Antwort des Modells (oder <c>null</c> bei nicht konfiguriertem Key / Fehler / Refusal).
/// Hinter einem Interface, damit <see cref="HintGenerationService"/> ohne echten API-Call testbar ist.
/// </summary>
public interface IClaudeJsonClient
{
    /// <summary>True, wenn ein API-Key konfiguriert ist (<c>Anthropic:ApiKey</c>). Sonst ist die
    /// Tipp-Generierung inaktiv und der Stack läuft normal weiter.</summary>
    bool IsConfigured { get; }

    /// <summary>Erzeugt eine JSON-Antwort <c>{hint1,hint2,hint3}</c> (structured output). Null bei Fehler.</summary>
    Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default);
}

/// <summary>Echte Implementierung über die offizielle Anthropic-C#-SDK (Opus 4.8, structured output).</summary>
public class ClaudeJsonClient : IClaudeJsonClient
{
    private readonly Anthropic.AnthropicClient? _client;
    private readonly ILogger<ClaudeJsonClient> _logger;

    public ClaudeJsonClient(IConfiguration config, ILogger<ClaudeJsonClient> logger)
    {
        _logger = logger;
        var key = config["Anthropic:ApiKey"];
        if (!string.IsNullOrWhiteSpace(key))
            _client = new Anthropic.AnthropicClient { ApiKey = key };
        else
            _logger.LogInformation("Anthropic:ApiKey nicht gesetzt — Tipp-Generierung ist inaktiv.");
    }

    public bool IsConfigured => _client != null;

    public async Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default)
    {
        if (_client == null) return null;
        try
        {
            var schema = new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("object"),
                ["properties"] = JsonSerializer.SerializeToElement(new
                {
                    hint1 = new { type = "string" },
                    hint2 = new { type = "string" },
                    hint3 = new { type = "string" },
                }),
                ["required"] = JsonSerializer.SerializeToElement(new[] { "hint1", "hint2", "hint3" }),
                ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            };

            var parameters = new MessageCreateParams
            {
                Model = Model.ClaudeOpus4_8,
                MaxTokens = 4096,
                System = system,
                Thinking = new ThinkingConfigAdaptive(),
                OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
                Messages = [new() { Role = Role.User, Content = userPrompt }],
            };

            var response = await _client.Messages.Create(parameters);
            if (response.StopReason == "refusal")
            {
                _logger.LogWarning("Tipp-Generierung abgelehnt (refusal).");
                return null;
            }
            return response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tipp-Generierung via Claude fehlgeschlagen.");
            return null;
        }
    }
}
