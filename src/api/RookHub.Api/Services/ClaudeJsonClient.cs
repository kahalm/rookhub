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

    /// <summary>
    /// Uebersetzt die Zug-Anmerkungen einer Partie: hinein geht <c>{items:[{ply,text}]}</c>, heraus
    /// kommt dasselbe in der Zielsprache. <c>null</c> bei fehlendem Key, Fehler oder Ablehnung.
    ///
    /// <para>Eigene Methode und nicht der Tipp-Aufruf mit anderem Prompt: hier gilt ein anderes
    /// Schema, und vor allem eine andere Groessenordnung — eine dicht kommentierte Partie hat
    /// mehrere tausend Woerter, die vier Zeilen eines Tipps nicht.</para>
    /// </summary>
    Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default);
}

/// <summary>Echte Implementierung über die offizielle Anthropic-C#-SDK (Claude Opus 5, structured output).</summary>
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
                // claude-opus-5: empfohlener Default bei identischem Preis zu Opus 4.8. Die SDK-Version
                // (12.35.1) kennt noch keine Model.ClaudeOpus5-Konstante → Model-Id als String
                // (implizite Konvertierung; das SDK reicht die Id unverändert an die API durch).
                Model = "claude-opus-5",
                MaxTokens = 4096,
                System = system,
                Thinking = new ThinkingConfigAdaptive(),
                OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
                Messages = [new() { Role = Role.User, Content = userPrompt }],
            };

            // ct durchreichen: sonst läuft die Generierung bei Shutdown/Abbruch (Hintergrund-Queue)
            // ungebremst weiter, bis die API antwortet.
            var response = await _client.Messages.Create(parameters, ct);
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

    /// <summary>Deckel fuer eine Uebersetzungs-Fuhre. Die Antwort ist ungefaehr so lang wie die
    /// Vorlage; der Aufrufer teilt lange Partien ohnehin auf (<c>CommentTranslationService</c>).</summary>
    private const int TranslateMaxTokens = 16000;

    public async Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt,
        CancellationToken ct = default)
    {
        if (_client == null) return null;
        try
        {
            var schema = new Dictionary<string, JsonElement>
            {
                ["type"] = JsonSerializer.SerializeToElement("object"),
                ["properties"] = JsonSerializer.SerializeToElement(new
                {
                    items = new
                    {
                        type = "array",
                        items = new
                        {
                            type = "object",
                            properties = new { ply = new { type = "integer" }, text = new { type = "string" } },
                            required = new[] { "ply", "text" },
                            additionalProperties = false,
                        },
                    },
                }),
                ["required"] = JsonSerializer.SerializeToElement(new[] { "items" }),
                ["additionalProperties"] = JsonSerializer.SerializeToElement(false),
            };

            var parameters = new MessageCreateParams
            {
                Model = "claude-opus-5",
                MaxTokens = TranslateMaxTokens,
                System = system,
                OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = schema } },
                Messages = [new() { Role = Role.User, Content = userPrompt }],
            };

            var response = await _client.Messages.Create(parameters, ct);
            if (response.StopReason == "refusal")
            {
                _logger.LogWarning("Uebersetzung abgelehnt (refusal).");
                return null;
            }
            return response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Uebersetzung via Claude fehlgeschlagen.");
            return null;
        }
    }
}
