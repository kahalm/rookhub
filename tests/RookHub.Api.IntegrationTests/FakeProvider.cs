using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace RookHub.Api.IntegrationTests;

/// <summary>
/// C#-Nachbau des offiziellen Lichess-Providers (<c>example-provider.py</c>, gepinnt d0eeb242) für den
/// eigenen Broker — mit genau seinem Verhalten auf der Leitung:
/// <list type="bullet">
/// <item>Registrierung: <c>GET /api/external-engine</c>, Eintrag GLEICHEN Namens → <c>PUT</c>, sonst <c>POST</c>
/// (<c>{ name, maxThreads, maxHash, variants, providerSecret }</c>), Bearer = API-Token.</item>
/// <item>Long-Poll: <c>POST /api/external-engine/work { providerSecret }</c> — MIT Bearer (die Poll-Sitzung
/// des Providers trägt ihn), 12 s Frist, 204 = nochmal.</item>
/// <item>Upload: <c>POST /api/external-engine/work/{id}</c> OHNE Authentifizierung, CHUNKED (keine Länge),
/// eine Zeile je <c>\n</c>, bis das Skript endet — oder bis der Server antwortet: wie aiohttp wird die
/// Antwort GLEICHZEITIG gelesen und der Schreiber abgebrochen, sobald sie da ist. Deshalb über einen rohen
/// Socket: <c>HttpClient</c> liefert unter HTTP/1.1 keine Antwort, solange der Rumpf noch geschrieben wird.</item>
/// </list>
/// Was die „Engine" hochlädt, bestimmt das Skript des Tests.
/// </summary>
internal sealed class FakeProvider : IAsyncDisposable
{
    public delegate Task Script(JsonElement job, Func<string, Task> writeLine, CancellationToken ct);

    public sealed record Upload(string JobId, HttpStatusCode Status, bool ScriptFinished, DateTime RespondedAt);

    private readonly HttpClient _http;
    private readonly string _token;
    private readonly string _name;
    private readonly string _secret = "fake-provider-" + Guid.NewGuid().ToString("N");
    private readonly Script _script;
    private readonly CancellationTokenSource _stop = new();
    private readonly Channel<Upload> _uploads = Channel.CreateUnbounded<Upload>();
    private Task? _loop;
    private int _polls;

    public FakeProvider(HttpClient http, string token, string name, Script script)
    {
        _http = http;
        _token = token;
        _name = name;
        _script = script;
    }

    public string? EngineId { get; private set; }
    public int Polls => Volatile.Read(ref _polls);

    public async Task RegisterAsync()
    {
        using var list = new HttpRequestMessage(HttpMethod.Get, "/api/external-engine");
        list.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var listed = await _http.SendAsync(list);
        listed.EnsureSuccessStatusCode();
        var engines = await listed.Content.ReadFromJsonAsync<JsonElement>();
        var existing = engines.EnumerateArray().FirstOrDefault(e => e.GetProperty("name").GetString() == _name);

        var registration = new { name = _name, maxThreads = 4, maxHash = 256, variants = new[] { "chess" }, providerSecret = _secret };
        using var save = existing.ValueKind == JsonValueKind.Object
            ? new HttpRequestMessage(HttpMethod.Put, $"/api/external-engine/{existing.GetProperty("id").GetString()}")
            : new HttpRequestMessage(HttpMethod.Post, "/api/external-engine");
        save.Content = JsonContent.Create(registration);
        save.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        using var saved = await _http.SendAsync(save);
        saved.EnsureSuccessStatusCode();
        EngineId = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString();
    }

    public void Start() => _loop = Task.Run(LoopAsync);

    public async Task<Upload> NextUploadAsync() =>
        await _uploads.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30));

    public async Task WaitForPollsAsync(int n)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (Polls < n && DateTime.UtcNow < deadline) await Task.Delay(50);
        if (Polls < n) throw new TimeoutException("Provider hat nicht gepollt");
    }

    private async Task LoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            JsonElement job;
            try
            {
                using var poll = new HttpRequestMessage(HttpMethod.Post, "/api/external-engine/work")
                {
                    Content = JsonContent.Create(new { providerSecret = _secret }),
                };
                poll.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));
                using var res = await _http.SendAsync(poll, timeout.Token);
                Interlocked.Increment(ref _polls);
                if (res.StatusCode != HttpStatusCode.OK) continue;
                job = await res.Content.ReadFromJsonAsync<JsonElement>(_stop.Token);
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested) { return; }
            catch (Exception) when (!_stop.IsCancellationRequested)
            {
                await Task.Delay(200);
                continue;
            }
            await UploadAsync(job);
        }
    }

    private async Task UploadAsync(JsonElement job)
    {
        var id = job.GetProperty("id").GetString()!;
        var baseAddress = _http.BaseAddress!;
        var finished = false;
        HttpStatusCode status = 0;
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(baseAddress.Host, baseAddress.Port, _stop.Token);
            var stream = tcp.GetStream();
            var head = $"POST /api/external-engine/work/{id} HTTP/1.1\r\nHost: {baseAddress.Authority}\r\n"
                       + "Content-Type: application/octet-stream\r\nTransfer-Encoding: chunked\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head), _stop.Token);

            using var writer = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
            var response = ReadStatusAsync(stream, _stop.Token);
            var writing = Task.Run(async () =>
            {
                try
                {
                    await _script(job, async line =>
                    {
                        var data = Encoding.UTF8.GetBytes(line + "\n");
                        await stream.WriteAsync(Encoding.ASCII.GetBytes($"{data.Length:X}\r\n"), writer.Token);
                        await stream.WriteAsync(data, writer.Token);
                        await stream.WriteAsync("\r\n"u8.ToArray(), writer.Token);
                        await stream.FlushAsync(writer.Token);
                    }, writer.Token);
                    await stream.WriteAsync("0\r\n\r\n"u8.ToArray(), writer.Token);
                    finished = true;
                }
                catch (Exception) { /* Server hat geantwortet oder Verbindung zu — wie aiohttp: Schreiber endet */ }
            });

            status = await response;
            writer.Cancel();                              // aiohttp: Antwort da → Schreiber abbrechen
            await writing;
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return;
        }
        catch (Exception) when (!_stop.IsCancellationRequested)
        {
            status = 0;
        }
        _uploads.Writer.TryWrite(new Upload(id, status, finished, DateTime.UtcNow));
    }

    /// <summary>Liest die Statuszeile und die Kopfzeilen der Antwort (der Rumpf ist bei 200/404 leer bzw. egal).</summary>
    private static async Task<HttpStatusCode> ReadStatusAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new List<byte>();
        var one = new byte[1];
        while (true)
        {
            if (await stream.ReadAsync(one, ct) == 0) throw new IOException("Verbindung ohne Antwort geschlossen");
            buffer.Add(one[0]);
            var n = buffer.Count;
            if (n >= 4 && buffer[n - 4] == '\r' && buffer[n - 3] == '\n' && buffer[n - 2] == '\r' && buffer[n - 1] == '\n') break;
        }
        var statusLine = Encoding.ASCII.GetString(buffer.ToArray()).Split("\r\n")[0];   // "HTTP/1.1 200 OK"
        return (HttpStatusCode)int.Parse(statusLine.Split(' ')[1]);
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        if (_loop is not null)
        {
            try { await _loop.WaitAsync(TimeSpan.FromSeconds(15)); }
            catch (Exception) { /* Aufräumen */ }
        }
        _http.Dispose();
    }
}
