using System.Globalization;
using System.Text.Json.Nodes;
using RookHub.Api.Services.Games;
using RookHub.Api.Services.League;

namespace RookHub.Api.Services.Og;

/// <summary>
/// LeagueHub-Karten (eigene Shell über <c>X-Og-Site: leaguehub</c>): die geteilte Begegnung <c>/s/{token}</c> (Aufstellungs-
/// Prognose bzw. Aufstellung) und eine einzelne Ligapartie <c>/partie/{token}</c>. Grundsatz: das Bild zeigt NUR, was die
/// Seite des Links ohne Anmeldung selbst zeigt — dieselben Dienste (<see cref="LeagueService.PublicShareAsync"/>,
/// <see cref="LeagueGameShares.ReadAsync"/>), dieselbe Anonymisierung (eine anonymisierte Vereinspartie kommt ohne Brett und
/// Mannschaften, mit dem Anonymisierungs-Namen). Texte deutsch wie die Seite.
/// </summary>
public partial class OgMetaService
{
    /// <summary><c>tn</c> im Client: „Spg Fügen-Mayrhofen/Zillertal/" → „Spg Fügen-Mayrhofen/Zillertal".</summary>
    internal static string TeamName(string? s) => (s ?? string.Empty).TrimEnd(' ', '\t', '/', '-');

    private static string? Str(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s) ? s : null;
    private static int? Int(JsonNode? n) => n is JsonValue v && v.TryGetValue<int>(out var i) ? i : n is JsonValue d && d.TryGetValue<double>(out var x) ? (int)x : null;
    private static double? Dbl(JsonNode? n) => n is JsonValue v && v.TryGetValue<double>(out var d) ? d : null;
    private static bool? Bool(JsonNode? n) => n is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    /// <summary>„62 %" — wie <c>pct</c> im Client.</summary>
    internal static string Pct(double p) => $"{Math.Round(p * 100).ToString(CultureInfo.InvariantCulture)} %";

    /// <summary>Heim – Gast der geteilten Begegnung aus Sicht der Mannschaft des Links.</summary>
    internal static (string Home, string Away) Pairing(JsonObject share)
    {
        var team = TeamName(Str(share["team"]));
        var fx = share["fixture"] as JsonObject;
        var opp = TeamName(Str(fx?["opp"]));
        if (opp.Length == 0) opp = "?";
        return Bool(fx?["home"]) == false ? (opp, team) : (team, opp);
    }

    /// <summary>Karte der geteilten Begegnung: Kopf (Liga, Runde, Paarung, Termin), darunter je Brett der wahrscheinlichste Spieler
    /// des Gegners mit Prozent — bzw. nach der Runde der, der wirklich spielte.</summary>
    internal static OgCard LeagueFixtureCard(JsonObject share)
    {
        var fx = share["fixture"] as JsonObject ?? new JsonObject();
        var (home, away) = Pairing(share);
        var opp = TeamName(Str(fx["opp"])) is { Length: > 0 } o ? o : "Gegner";
        var played = Str(fx["status"]) == "played";
        var when = new[] { Str(fx["date"]), Str(fx["time"]), Str(fx["venue"]) }.Where(x => x != null).ToList();
        var rows = new List<OgTableRow>();
        foreach (var b in (fx["boards"] as JsonArray ?? []).OfType<JsonObject>())
        {
            var board = Int(b["board"])?.ToString(CultureInfo.InvariantCulture) ?? "?";
            bool? white = Str(b["opp_color"]) switch { "w" => true, "s" => false, _ => null };
            if (played && b["actual"] is JsonObject a)
                rows.Add(new OgTableRow(board, white, Str(a["n"]) ?? "?", Int(a["elo"])?.ToString(CultureInfo.InvariantCulture)));
            else if ((b["cand"] as JsonArray)?.OfType<JsonObject>().FirstOrDefault() is { } c)
                rows.Add(new OgTableRow(board, white, Str(c["n"]) ?? "?", Int(c["elo"])?.ToString(CultureInfo.InvariantCulture),
                    Dbl(c["p"]) is double p ? Pct(p) : null));
        }
        const int maxRows = 8;
        string caption;
        if (Bool(fx["bye"]) == true) caption = "Spielfrei";
        else if (rows.Count == 0) caption = "Noch keine Prognose";
        else if (played) caption = $"Aufstellung {opp}";
        else caption = Bool(fx["provisional"]) == true ? $"Prognose {opp} (vorläufig)" : $"Wahrscheinlichste Aufstellung {opp}";
        return new OgCard
        {
            Brand = OgBrand.LeagueHub,
            Kicker = string.Join(" · ", new[] { Str(share["league"]), Int(share["round"]) is int r ? $"Runde {r}" : null }.Where(x => x != null)),
            Title = played && Str(fx["score"]) is { } score ? $"{home} {score} {away}" : $"{home} – {away}",
            Lines = when.Count > 0 ? [string.Join(" · ", when)] : [],
            Table = new OgTable(caption, rows.Take(maxRows).ToList(), Math.Max(0, rows.Count - maxRows)),
        };
    }

    /// <summary>Karte einer geteilten Ligapartie: Endstellung mit Zugmarke, Spieler (anonymisiert, wie die Seite), Ergebnis,
    /// Liga/Runde und — nur ohne Anonymisierung — Brett und Mannschaften.</summary>
    internal static OgCard LeagueGameCard(LeagueGameShares.SharedGame g)
    {
        var plies = GamePlies.Parse(g.Pgn, MaxPlies)?.Plies ?? [];
        var lines = new List<string>();
        if (g.Home != null && g.Away != null) lines.Add($"{TeamName(g.Home)} – {TeamName(g.Away)}");
        return new OgCard
        {
            Brand = OgBrand.LeagueHub,
            Boards = [new OgDiagram(EndFenFromPgn(g.Pgn), false, plies.Count > 0 ? plies[^1].Uci : null)],
            Kicker = string.Join(" · ", new[] { g.League, $"Runde {g.Round}", g.Board is int b ? $"Brett {b}" : null }.Where(x => !string.IsNullOrWhiteSpace(x))),
            Players = [new OgPlayer(true, Player(g.White), g.WhiteElo), new OgPlayer(false, Player(g.Black), g.BlackElo)],
            Result = string.IsNullOrWhiteSpace(g.Result) || g.Result == "*" ? null : g.Result,
            Lines = lines,
        };
    }

    private async Task<OgPage?> ResolveLeaguePageAsync(string kind, string id, string img, string canonical, CancellationToken ct)
    {
        switch (kind)
        {
            case "league":
            {
                if (_league is null || await _league.PublicShareAsync(id, ct) is not { } share) return null;
                var (home, away) = Pairing(share);
                var fx = share["fixture"] as JsonObject;
                var played = Str(fx?["status"]) == "played";
                var league = Str(share["league"]) ?? "Liga";
                var round = Int(share["round"]);
                var desc = played
                    ? $"{league}, Runde {round}: Aufstellung und Partien der Begegnung"
                    : $"Aufstellungs-Prognose {league}, Runde {round}: wer an welchem Brett erwartet wird{(Str(fx?["date"]) is { } d ? $" · {d}" : "")}";
                return new OgPage($"{home} – {away}", desc, $"{img}?c={CardVersion}", canonical, SiteName: "LeagueHub");
            }
            case "lgame":
            {
                if (_leagueGames is null || await _leagueGames.ReadAsync(id, ct) is not { } g) return null;
                var title = $"{Player(g.White)}{(g.WhiteElo is int we ? $" ({we})" : "")} – {Player(g.Black)}{(g.BlackElo is int be ? $" ({be})" : "")}";
                var desc = string.Join(" · ", new[] { g.Result, g.League, $"Runde {g.Round}", "Partie auf LeagueHub nachspielen" }.Where(x => !string.IsNullOrWhiteSpace(x)));
                return new OgPage(title, desc, $"{img}?c={CardVersion}", canonical, "article", "LeagueHub");
            }
        }
        return null;
    }

    private async Task<OgBoard?> ResolveLeagueBoardAsync(string kind, string id, CancellationToken ct)
    {
        switch (kind)
        {
            case "league":
            {
                if (_league is null || await _league.PublicShareAsync(id, ct) is not { } share) return null;
                // Die Prognose wird nach jeder Runde neu gerechnet, der Link läuft ab oder wird widerrufen: kurz cachen.
                return new OgBoard(StartFen, false, Card: LeagueFixtureCard(share), MaxAge: EditableMaxAge);
            }
            case "lgame":
            {
                if (_leagueGames is null || await _leagueGames.ReadAsync(id, ct) is not { } g) return null;
                var card = LeagueGameCard(g);
                return new OgBoard(card.Boards[0].Fen, false, Card: card, MaxAge: ExpiringMaxAge);
            }
        }
        return null;
    }
}
