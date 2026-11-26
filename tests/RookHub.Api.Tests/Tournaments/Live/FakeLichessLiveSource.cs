using System.Runtime.CompilerServices;
using System.Threading.Channels;
using RookHub.Api.Services.Tournaments.Live;

namespace RookHub.Api.Tests.Tournaments.Live;

/// <summary>Lichess ohne Netz: Antworten je Pfad, Ströme als Kanäle, die der Test befüllt. Zählt die Abrufe.</summary>
internal sealed class FakeLichessLiveSource : ILichessLiveSource
{
    public Dictionary<string, string?> RoundPgn { get; } = new();
    public Dictionary<string, string?> Tours { get; } = new();
    public Dictionary<string, string?> ToursOfRound { get; } = new();
    public Dictionary<string, string?> Searches { get; } = new();
    public Dictionary<string, string?> CloudEvals { get; } = new();
    public string? Top { get; set; } = """{"active":[],"upcoming":[],"past":{"currentPageResults":[]}}""";

    public int StreamsOpened;
    public int StreamsClosed;
    public int RoundPgnCalls;
    public int SearchCalls;
    public int CloudEvalCalls;
    public readonly List<string> StreamedRounds = [];

    private readonly Dictionary<string, Channel<string>> _streams = new();

    /// <summary>Der Kanal hinter dem (nächsten) Strom einer Runde.</summary>
    public Channel<string> StreamOf(string roundId)
    {
        lock (_streams)
        {
            if (!_streams.TryGetValue(roundId, out var ch)) _streams[roundId] = ch = Channel.CreateUnbounded<string>();
            return ch;
        }
    }

    public Task<string?> GetRoundPgnAsync(string roundId, CancellationToken ct)
    {
        Interlocked.Increment(ref RoundPgnCalls);
        return Task.FromResult(RoundPgn.GetValueOrDefault(roundId));
    }

    public async IAsyncEnumerable<string> StreamRoundAsync(string roundId, [EnumeratorCancellation] CancellationToken ct)
    {
        Interlocked.Increment(ref StreamsOpened);
        lock (StreamedRounds) StreamedRounds.Add(roundId);
        var ch = StreamOf(roundId);
        try
        {
            while (await ch.Reader.WaitToReadAsync(ct))
                while (ch.Reader.TryRead(out var s)) yield return s;
        }
        finally
        {
            Interlocked.Increment(ref StreamsClosed);
            lock (_streams) _streams.Remove(roundId);
        }
    }

    public Task<string?> GetTourAsync(string tourId, CancellationToken ct) => Task.FromResult(Tours.GetValueOrDefault(tourId));
    public int TourOfRoundCalls;
    public Task<string?> GetTourOfRoundAsync(string roundId, CancellationToken ct)
    {
        Interlocked.Increment(ref TourOfRoundCalls);
        return Task.FromResult(ToursOfRound.GetValueOrDefault(roundId));
    }
    public Task<string?> GetTopAsync(CancellationToken ct) => Task.FromResult(Top);

    public Task<string?> SearchAsync(string query, CancellationToken ct)
    {
        Interlocked.Increment(ref SearchCalls);
        return Task.FromResult(Searches.GetValueOrDefault(query) ?? """{"currentPageResults":[],"nextPage":null}""");
    }

    public Task<string?> CloudEvalAsync(string fen, CancellationToken ct)
    {
        Interlocked.Increment(ref CloudEvalCalls);
        return Task.FromResult(CloudEvals.GetValueOrDefault(fen));
    }
}

/// <summary>Bausteine für Übertragungs-PGN wie im echten Strom (österreichische Bundesliga, 25.11.2026).</summary>
internal static class LivePgn
{
    public static string Game(string gameId, string white, string black, string moves, string result = "*",
        string? whiteFide = null, string? blackFide = null, string? whiteTeam = null, string? blackTeam = null, string round = "1.1") =>
        $"[Event \"1. Bundesliga AUT 2026/2027\"]\n[Site \"Mayrhofen, Austria\"]\n[Round \"{round}\"]\n[White \"{white}\"]\n[Black \"{black}\"]\n"
        + $"[Result \"{result}\"]\n[WhiteElo \"2616\"]\n[WhiteTitle \"GM\"]\n"
        + (whiteTeam is null ? "" : $"[WhiteTeam \"{whiteTeam}\"]\n") + (whiteFide is null ? "" : $"[WhiteFideId \"{whiteFide}\"]\n")
        + "[BlackElo \"2462\"]\n"
        + (blackTeam is null ? "" : $"[BlackTeam \"{blackTeam}\"]\n") + (blackFide is null ? "" : $"[BlackFideId \"{blackFide}\"]\n")
        + "[Variant \"Standard\"]\n"
        + $"[GameURL \"https://lichess.org/broadcast/1-bundesliga-austria-20262027/round-1/L3MX2oE3/{gameId}\"]\n\n"
        + moves + " " + result + "\n\n\n";
}
