using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RookHub.Api.Tests;

/// <summary>
/// Nachgebauter OpenAI-kompatibler Server (<c>chat/completions</c>) für die Formular-Leser auf eigener Hardware:
/// merkt sich jede Anfrage (Adresse, Anmeldung, Rumpf als JSON) und antwortet der Reihe nach aus
/// <see cref="Replies"/> — ist die Schlange leer, mit 500.
/// </summary>
public sealed class ChatCompletionHandler : HttpMessageHandler
{
    public sealed record Request(string Url, string? Authorization, JsonNode Body);

    public List<Request> Requests { get; } = new();
    public Queue<(HttpStatusCode Status, string Body)> Replies { get; } = new();

    public ChatCompletionHandler Reply(string content, string finish = "stop", int input = 1200, int output = 300)
    {
        Replies.Enqueue((HttpStatusCode.OK, Completion(content, finish, input, output)));
        return this;
    }

    public ChatCompletionHandler Fail(HttpStatusCode status, string body = "{\"error\":\"nope\"}")
    {
        Replies.Enqueue((status, body));
        return this;
    }

    public static string Completion(string content, string finish = "stop", int input = 1200, int output = 300)
        => JsonSerializer.Serialize(new
        {
            choices = new[] { new { index = 0, message = new { role = "assistant", content }, finish_reason = finish } },
            usage = new { prompt_tokens = input, completion_tokens = output },
        });

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new Request(request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), JsonNode.Parse(body)!));
        var (status, text) = Replies.Count > 0 ? Replies.Dequeue() : (HttpStatusCode.InternalServerError, "{}");
        return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    }
}
