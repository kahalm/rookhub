using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Messages;

namespace RookHub.Api.Services;

/// <summary>
/// Ein Anthropic-Modell mit EIGENEM Schlüssel und EIGENEM Modell für genau einen Zweck — heute nur die Begründungen der
/// automatischen Fehler-Vergleiche der Vereinspartien über Claude Fable (0.911.0, <see cref="Engines.ClubFableExplanations"/>).
/// Bietet nur <see cref="CompleteJsonAsync"/> (strukturierte Ausgabe nach Schema); Tipps und Übersetzung bleiben beim
/// <see cref="ClaudeJsonClient"/> bzw. der eigenen Hardware (<c>.claude/rules/llm-text-clients.md</c>).
///
/// <para>Fable denkt immer (kein <c>Thinking</c>-Parameter, sonst 400); die Tiefe steuert <c>OutputConfig.Effort</c>, und das
/// Denken zählt in <c>max_tokens</c> mit. Eine Ablehnung (<c>refusal</c>), ein Abbruch am Deckel oder ein Fehler liefert
/// <c>null</c> — der Aufrufer fällt dann auf das lokale Modell zurück. Verbrauch (Tokens) steht je Aufruf im Log; der Schlüssel
/// und die Antwort nie.</para>
/// </summary>
public sealed class AnthropicJsonClient : IClaudeJsonClient
{
    private readonly Anthropic.AnthropicClient? _client;
    private readonly ILogger _logger;
    private readonly string _model;
    private readonly string _effort;

    /// <param name="apiKey">Schlüssel; leer oder Platzhalter (<see cref="SecretConfigCheck.Usable"/>) = aus.</param>
    /// <param name="effort"><c>low</c>/<c>medium</c>/<c>high</c>/<c>xhigh</c>/<c>max</c>; Unbekanntes = <c>low</c>.</param>
    public AnthropicJsonClient(string? apiKey, string model, ILogger logger, string? effort = null)
    {
        _logger = logger;
        _model = model;
        _effort = (effort ?? "low").Trim().ToLowerInvariant();
        if (SecretConfigCheck.Usable(apiKey) is { } key) _client = new Anthropic.AnthropicClient { ApiKey = key };
    }

    public bool IsConfigured => _client != null;
    public string TranslationModel => _model;
    public Task<string?> GenerateHintsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);
    public Task<string?> TranslateCommentsJsonAsync(string system, string userPrompt, CancellationToken ct = default) => Task.FromResult<string?>(null);

    public async Task<string?> CompleteJsonAsync(string purpose, string system, string userPrompt, JsonNode schema, int maxTokens,
        CancellationToken ct = default)
    {
        if (_client == null) return null;
        try
        {
            var parameters = new MessageCreateParams
            {
                Model = _model,
                MaxTokens = maxTokens,
                System = system,
                OutputConfig = new OutputConfig { Format = new JsonOutputFormat { Schema = SchemaOf(schema) }, Effort = EffortOf(_effort) },
                Messages = [new() { Role = Role.User, Content = userPrompt }],
            };
            var response = await _client.Messages.Create(parameters, ct);
            _logger.LogInformation("Anthropic {Purpose} ({Model}): {Input} Eingabe-, {Output} Ausgabe-Tokens, Ende {Stop}",
                purpose, _model, response.Usage.InputTokens, response.Usage.OutputTokens, response.StopReason?.ToString());
            if (response.StopReason == "refusal")
            {
                _logger.LogWarning("Anthropic {Purpose}: abgelehnt (refusal)", purpose);
                return null;
            }
            return response.Content.Select(b => b.Value).OfType<TextBlock>().FirstOrDefault()?.Text;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning("Anthropic {Purpose} ({Model}) fehlgeschlagen: {Error}", purpose, _model, ex.GetType().Name + ": " + ex.Message);
            return null;
        }
    }

    internal static Effort EffortOf(string effort) => effort switch
    {
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "max" => Effort.Max,
        _ => Effort.Low,
    };

    internal static Dictionary<string, JsonElement> SchemaOf(JsonNode schema) =>
        schema.AsObject().ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value));
}
