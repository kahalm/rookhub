using RookHub.Api.DTOs.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Og;

/// <summary>
/// Geteilte Vereinspartie <c>/cg/{token}</c> (Link-Vorschau seit 2026-11-22; vorher kannte die nginx-Weiche <c>cg</c> nicht und die
/// Seite hatte GAR KEINE Vorschau). Gleichwertig zur eigenen Partie <c>/g/{token}</c>: Partiekarte mit Kurve, mit <c>?train=white|black</c>
/// die Trainingskarte. Gelesen wird ausschließlich über <see cref="ClubGameLinks.ReadAsync"/> — denselben Dienst wie die Seite selbst:
/// eine anonymisierte Partie kommt dort schon mit dem Anonymisierungs-Namen („Schwaz"), ohne Event, ohne Elo der ersetzten Seite und
/// mit neu gebautem PGN. <see cref="Models.LeagueClubGame.WhiteRealName"/> und Co. erreichen die Vorschau so nie.
/// </summary>
public partial class OgMetaService
{
    /// <summary>Kennung der Partiekarte einer Vereinspartie (<c>/api/og/img/cgame/{token}.png</c>).</summary>
    internal const string ClubGameKind = "cgame";
    /// <summary>Kennung der Trainingskarte einer Vereinspartie (<c>/api/og/img/cgtrain/{side}-{token}.png</c>).</summary>
    internal const string ClubTrainKind = "cgtrain";

    /// <summary>Die geteilte Vereinspartie in der Form der eigenen Partie, damit <see cref="GameCard"/> sie zeichnen kann.</summary>
    internal static SharedGameDto ClubGameAsShared(ClubGameLinks.SharedClubGame c) => new()
    {
        White = c.White, Black = c.Black, Result = c.Result, Pgn = c.Pgn, WhiteElo = c.WhiteElo, BlackElo = c.BlackElo,
        Source = c.Event ?? string.Empty, OwnerSide = c.OwnSide,
    };

    /// <summary>Titel wie bei <c>/g/</c>: „Weiß (Elo 1826) – Schwarz".</summary>
    internal static string GameTitle(string white, int? whiteElo, string black, int? blackElo)
        => $"{white}{(whiteElo is int we ? $" (Elo {we})" : "")} – {black}{(blackElo is int be ? $" (Elo {be})" : "")}";

    /// <summary>Beschreibung der Trainingskarte (englisch wie bei <c>/g/</c>, Wunsch 2026-10-21).</summary>
    internal static string TrainDescription(string white, string black, string side, int mistakes)
    {
        var who = side == "white" ? white : black;
        return mistakes > 0
            ? $"{mistakes} {(mistakes == 1 ? "mistake" : "mistakes")} by {who} in {white} – {black} — find the better moves now."
            : $"{white} – {black}: replay the mistakes of {who} and find the better moves.";
    }

    /// <summary>Brett der Trainingskarte: Endstellung aus der Sicht der trainierten Seite, Kurve, Fehler dieser Seite.</summary>
    internal static OgBoard TrainBoard(string pgn, string? white, string? black, GameEvalsDto? evals, string side)
    {
        var who = side == "white" ? (string.IsNullOrWhiteSpace(white) ? "Weiß" : white!) : (string.IsNullOrWhiteSpace(black) ? "Schwarz" : black!);
        return new OgBoard(EndFenFromPgn(pgn), Flip: side == "black", Curve: CurveOf(evals),
            Train: new OgImageService.TrainCard(TrainHeadline, TrainSubline, TrainMarks(evals, side == "white"), $"Mistakes by {who}"));
    }

    private async Task<OgPage?> ResolveClubGamePageAsync(string token, string? path, string baseUrl, string img, string canonical, CancellationToken ct)
    {
        if (_clubGames is null || await _clubGames.ReadAsync(token, ct) is not { } g) return null;
        var white = Player(g.White);
        var black = Player(g.Black);
        var evals = await _clubGames.EvalsAsync(token, ct);
        var version = CurveVersion(evals);
        if (QueryParam(path, "train") is "white" or "black" && QueryParam(path, "train") is { } side)
        {
            var trainImg = $"{baseUrl}/api/og/img/{ClubTrainKind}/{side}-{Uri.EscapeDataString(token)}.png";
            return new OgPage(TrainTitle, TrainDescription(white, black, side, TrainMarks(evals, side == "white").Count),
                version == null ? $"{trainImg}?l=en" : $"{trainImg}?v={version}&l=en", canonical, "article");
        }
        var desc = string.Join(" · ", new[] { g.Result is { Length: > 0 } r && r != "*" ? r : null, g.Event, "Partie auf RookHub nachspielen" }
            .Where(x => !string.IsNullOrWhiteSpace(x)));
        return new OgPage(GameTitle(white, g.WhiteElo, black, g.BlackElo), desc,
            $"{img}?c={CardVersion}{(version == null ? "" : $"&v={version}")}", canonical, "article");
    }

    private async Task<OgBoard?> ResolveClubGameBoardAsync(string kind, string id, CancellationToken ct)
    {
        if (_clubGames is null) return null;
        if (kind == ClubTrainKind)
        {
            // „white-<token>" bzw. „black-<token>" (das Token selbst darf Bindestriche tragen)
            var dash = id.IndexOf('-');
            if (dash <= 0 || id[..dash] is not ("white" or "black")) return null;
            var token = id[(dash + 1)..];
            if (await _clubGames.ReadAsync(token, ct) is not { } t) return null;
            // Widerrufbarer Link (und Namen lassen sich nachträglich korrigieren): kurz cachen statt eine Woche „immutable".
            return TrainBoard(t.Pgn, t.White, t.Black, await _clubGames.EvalsAsync(token, ct), id[..dash]) with { MaxAge = ExpiringMaxAge };
        }
        if (await _clubGames.ReadAsync(id, ct) is not { } g) return null;
        var curve = CurveOf(await _clubGames.EvalsAsync(id, ct));
        var card = GameCard(ClubGameAsShared(g), null, curve);
        return new OgBoard(card.Boards[0].Fen, card.Boards[0].Flip, curve, Card: card, MaxAge: ExpiringMaxAge);
    }
}
