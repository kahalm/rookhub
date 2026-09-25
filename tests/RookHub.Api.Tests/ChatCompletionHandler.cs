using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RookHub.Api.Tests;

/// <summary>
/// Nachgebauter OpenAI-kompatibler Server (<c>chat/completions</c>) für die Formular-Leser auf eigener Hardware:
/// merkt sich jede Anfrage (Adresse, Anmeldung, Rumpf als JSON) und antwortet der Reihe nach — ist die Schlange leer,
/// mit 500. Eine mit <see cref="Reply"/> hinterlegte Antwort kommt als Server-Sent Events, wenn die Anfrage
/// <c>stream: true</c> verlangt (wie bei vLLM), sonst als ganzes JSON; <see cref="Raw"/> liefert den Rumpf wörtlich
/// (ein Server, der den Stream ignoriert).
/// </summary>
public sealed class ChatCompletionHandler : HttpMessageHandler
{
    public sealed record Request(string Url, string? Authorization, JsonNode Body);

    private sealed record Scripted(HttpStatusCode Status, string? Raw, string? Content, string Finish, int Input, int Output);

    private readonly Queue<Scripted> _script = new();

    public List<Request> Requests { get; } = new();

    public ChatCompletionHandler Reply(string content, string finish = "stop", int input = 1200, int output = 300)
    {
        _script.Enqueue(new(HttpStatusCode.OK, null, content, finish, input, output));
        return this;
    }

    public ChatCompletionHandler Fail(HttpStatusCode status, string body = "{\"error\":\"nope\"}")
    {
        _script.Enqueue(new(status, body, null, "", 0, 0));
        return this;
    }

    public ChatCompletionHandler Raw(string body)
    {
        _script.Enqueue(new(HttpStatusCode.OK, body, null, "", 0, 0));
        return this;
    }

    public static string Completion(string content, string finish = "stop", int input = 1200, int output = 300)
        => JsonSerializer.Serialize(new
        {
            choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = finish } },
            usage = new { prompt_tokens = input, completion_tokens = output },
        });

    /// <summary>Wie vLLM: Inhalt in Stücken, ein Denk-Stück dazwischen (gehört nicht in die Antwort), dann der
    /// Stoppgrund, dann die Tokens, dann <c>[DONE]</c>.</summary>
    public static string Stream(string content, string finish, int input, int output)
    {
        var half = content.Length / 2;
        var chunks = new List<object>
        {
            new { choices = new[] { new { index = 0, delta = new { role = "assistant", content = "" } } } },
            new { choices = new[] { new { index = 0, delta = new { reasoning = "thinking…" } } } },
            new { choices = new[] { new { index = 0, delta = new { content = content[..half] } } } },
            new { choices = new[] { new { index = 0, delta = new { content = content[half..] } } } },
            new { choices = new[] { new { index = 0, delta = new { }, finish_reason = finish } } },
            new { choices = Array.Empty<object>(), usage = new { prompt_tokens = input, completion_tokens = output } },
        };
        var sb = new StringBuilder();
        foreach (var c in chunks) sb.Append("data: ").Append(JsonSerializer.Serialize(c)).Append("\n\n");
        return sb.Append("data: [DONE]\n\n").ToString();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        var node = JsonNode.Parse(body)!;
        Requests.Add(new Request(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), node));
        if (_script.Count == 0)
            return new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent("{}") };
        var s = _script.Dequeue();
        if (s.Raw != null)
            return new HttpResponseMessage(s.Status) { Content = new StringContent(s.Raw, Encoding.UTF8, "application/json") };
        var streaming = node["stream"] is JsonValue v && v.TryGetValue<bool>(out var b) && b;
        return new HttpResponseMessage(s.Status)
        {
            Content = streaming
                ? new StringContent(Stream(s.Content!, s.Finish, s.Input, s.Output), Encoding.UTF8, "text/event-stream")
                : new StringContent(Completion(s.Content!, s.Finish, s.Input, s.Output), Encoding.UTF8, "application/json"),
        };
    }
}
