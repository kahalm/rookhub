using System.Globalization;
using System.Text.RegularExpressions;
using Chess;
using RookHub.Api.DTOs.Games;
using RookHub.Api.Services.Games;

namespace RookHub.Api.Services.Og;

/// <summary>
/// Karten mit Text für die Link-Vorschau (2026-11-09, <c>docs/api/link-vorschau.md</c>): Partie, markierte Stellung einer Partie,
/// rekonstruierte Partie, Repertoire-Linie. Texte auf dem Bild sind ENGLISCH wie die Trainingskarte (Wunsch 2026-10-21: die
/// Vorschau sieht jeder, gleich welche Sprache); LeagueHub-Karten deutsch (eine Tiroler/bayerische Liga-Seite).
/// </summary>
public partial class OgMetaService
{
    /// <summary>Stand der Bildgestaltung in der Bild-Adresse (<c>?c=</c>): Bilder sind eine Woche „immutable" gecacht und
    /// Messenger merken sie nach der Adresse — eine neue Gestaltung braucht eine neue Adresse.</summary>
    internal const string CardVersion = "2";

    /// <summary>Deckel beim Nachspielen für das Bild (eine Partie mit mehr Halbzügen zeigt die Stellung nach diesen).</summary>
    internal const int MaxPlies = 1000;

    /// <summary>Wie lange ein Bild eines widerrufbaren/bearbeitbaren Links gecacht werden darf.</summary>
    internal static readonly TimeSpan EditableMaxAge = TimeSpan.FromMinutes(10);
    /// <summary>… eines Links mit festem Inhalt, der nur abläuft oder widerrufen wird.</summary>
    internal static readonly TimeSpan ExpiringMaxAge = TimeSpan.FromHours(1);

    private static readonly Regex EventTag = new(@"\[Event\s+""(?<v>[^""]*)""\]", RegexOptions.Compiled);

    /// <summary>„12. Nf5" bzw. „12… Nf5" für den Halbzug <paramref name="ply"/> (1-basiert); Zugnummer ab der Startstellung.</summary>
    internal static string MoveLabel(IReadOnlyList<GamePlies.Ply> plies, int ply)
    {
        var p = plies[ply - 1];
        var white = FenFields.WhiteToMove(p.Fen);
        var fields = p.Fen.Split(' ');
        var number = fields.Length >= 6 && int.TryParse(fields[5], out var n) ? n : (ply + 1) / 2;
        return white ? $"{number}. {p.San}" : $"{number}… {p.San}";
    }

    /// <summary>Ein brauchbarer Ereignis-Name aus dem PGN-Kopf (ohne „?", „-" und die nichtssagenden Lichess-/chess.com-Namen).</summary>
    internal static string? EventOf(string? pgn)
    {
        if (string.IsNullOrWhiteSpace(pgn)) return null;
        var m = EventTag.Match(pgn);
        if (!m.Success) return null;
        var v = m.Groups["v"].Value.Trim();
        return v is "" or "?" or "-" || v.StartsWith("Casual ", StringComparison.OrdinalIgnoreCase)
            || v.StartsWith("Rated ", StringComparison.OrdinalIgnoreCase) || v.Equals("Live Chess", StringComparison.OrdinalIgnoreCase)
            ? null : v;
    }

    private static string Player(string? name) => string.IsNullOrWhiteSpace(name) ? "?" : name.Trim();

    /// <summary>Karte einer geteilten Partie: Endstellung bzw. Stellung nach <paramref name="ply"/> Halbzügen mit Zugmarke, Spieler mit
    /// Elo, Ergebnis, Ereignis und Datum, die Kurve der fertigen Analyse.</summary>
    internal static OgCard GameCard(SharedGameDto g, int? ply, IReadOnlyList<double?>? curve)
    {
        var parsed = GamePlies.Parse(g.Pgn, MaxPlies);
        var plies = parsed?.Plies ?? [];
        string fen;
        string? last = null;
        string? at = null;
        if (ply is int n && n > 0 && n <= plies.Count)
        {
            fen = n < plies.Count ? plies[n].Fen : EndFenFromPgn(g.Pgn);
            last = plies[n - 1].Uci;
            at = MoveLabel(plies, n);
        }
        else
        {
            fen = EndFenFromPgn(g.Pgn);
            last = plies.Count > 0 ? plies[^1].Uci : null;
        }
        var evt = EventOf(g.Pgn);
        var lines = new List<string>();
        var where = new[] { evt ?? (string.IsNullOrWhiteSpace(g.Source) ? null : g.Source), g.PlayedAt?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) }
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (where.Count > 0) lines.Add(string.Join(" · ", where));
        return new OgCard
        {
            Boards = [new OgDiagram(fen, g.OwnerSide == "black", last)],
            Kicker = at != null ? $"Position after {at}" : "Chess game",
            Players = [new OgPlayer(true, Player(g.White), g.WhiteElo), new OgPlayer(false, Player(g.Black), g.BlackElo)],
            Result = string.IsNullOrWhiteSpace(g.Result) || g.Result == "*" ? null : g.Result,
            Lines = lines,
            Curve = curve,
        };
    }

    /// <summary>Stellung nach einer SAN-Folge ab der Grundstellung; so weit wie spielbar.</summary>
    internal static string FenAfterSan(string? sanMoves)
    {
        var board = new ChessBoard();
        foreach (var san in (sanMoves ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Regex.IsMatch(san, @"^\d+\.+$")) continue; // Zugnummern
            try { if (!board.Move(san)) break; } catch { break; }
        }
        return board.ToFen();
    }

    /// <summary>Karte einer rekonstruierten Partie: die zuletzt bekannte Stellung (sonst die gesicherten Züge ab der Grundstellung),
    /// Spieler, Ergebnis, wie viel bekannt ist.</summary>
    internal static OgCard ReconCard(SharedReconstructionDto r)
    {
        // Die zuletzt bekannte Stellung (Ende des letzten Teils), sonst das Ende der gesicherten Züge, sonst die Grundstellung.
        var fen = r.Parts.Select(p => p.EndFen ?? p.Fen).LastOrDefault(f => !string.IsNullOrWhiteSpace(f))
            ?? (string.IsNullOrWhiteSpace(r.PrefixSan) ? StartFen : FenAfterSan(r.PrefixSan));
        var lines = new List<string>();
        var where = new[] { r.Event, r.PlayedOn?.ToString("d MMM yyyy", CultureInfo.InvariantCulture) }.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
        if (where.Count > 0) lines.Add(string.Join(" · ", where!));
        lines.Add($"{r.KnownPlies} {(r.KnownPlies == 1 ? "ply" : "plies")} known · {r.Gaps} {(r.Gaps == 1 ? "gap" : "gaps")}");
        var hasPlayers = !string.IsNullOrWhiteSpace(r.White) || !string.IsNullOrWhiteSpace(r.Black);
        return new OgCard
        {
            Boards = [new OgDiagram(fen)],
            Kicker = "Reconstructed game",
            Title = hasPlayers ? null : (string.IsNullOrWhiteSpace(r.Title) ? "Reconstructed game" : r.Title),
            Players = hasPlayers ? [new OgPlayer(true, Player(r.White)), new OgPlayer(false, Player(r.Black))] : [],
            Result = string.IsNullOrWhiteSpace(r.Result) || r.Result == "*" ? null : r.Result,
            Lines = lines,
        };
    }

    /// <summary>Karte einer geteilten Repertoire-Linie: Stellung am Ende der (ersten) Linie mit Zugmarke, Titel, Repertoire, Zuglänge.</summary>
    internal static OgCard LineCard(DTOs.Repertoires.SharedLineDto l)
    {
        var plies = GamePlies.Parse(l.Pgn, MaxPlies)?.Plies ?? [];
        var title = !string.IsNullOrWhiteSpace(l.Title) ? l.Title! : !string.IsNullOrWhiteSpace(l.RepertoireName) ? l.RepertoireName! : "Opening line";
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(l.RepertoireName) && l.RepertoireName != title) lines.Add(l.RepertoireName!);
        if (plies.Count > 0)
        {
            var moves = (plies.Count + 1) / 2;
            lines.Add(l.IsChapter ? $"Chapter · first line {moves} {(moves == 1 ? "move" : "moves")}" : $"{moves} {(moves == 1 ? "move" : "moves")}");
        }
        return new OgCard
        {
            Boards = [new OgDiagram(EndFenFromPgn(l.Pgn), false, plies.Count > 0 ? plies[^1].Uci : null)],
            Kicker = l.IsChapter ? "Repertoire chapter" : "Repertoire line",
            Title = title,
            Lines = lines,
        };
    }

    private async Task<OgPage?> ResolveSharedPageAsync(string kind, string id, string img, string canonical, CancellationToken ct)
    {
        switch (kind)
        {
            case "recon":
            {
                var r = _reconstructions is null ? null : await _reconstructions.GetSharedAsync(id, ct);
                if (r is null) return null;
                var players = !string.IsNullOrWhiteSpace(r.White) || !string.IsNullOrWhiteSpace(r.Black)
                    ? $"{Player(r.White)} – {Player(r.Black)}" : (string.IsNullOrWhiteSpace(r.Title) ? "Reconstructed game" : r.Title);
                var desc = string.Join(" · ", new[] { r.Result is { Length: > 0 } res && res != "*" ? res : null, r.Event,
                    $"reconstructed from memory: {r.KnownPlies} plies known, {r.Gaps} gaps" }.Where(x => !string.IsNullOrWhiteSpace(x)));
                return new OgPage(players, desc, $"{img}?c={CardVersion}", canonical, "article");
            }
            case "line":
            {
                var l = _sharedLines is null ? null : await _sharedLines.GetByTokenAsync(id, ct);
                if (l is null) return null;
                var card = LineCard(l);
                var desc = string.Join(" · ", card.Lines.Append(l.IsChapter ? "repertoire chapter on RookHub" : "replay this opening line on RookHub"));
                return new OgPage(card.Title!, desc, $"{img}?c={CardVersion}", canonical);
            }
        }
        return null;
    }

    private async Task<OgBoard?> ResolveSharedBoardAsync(string kind, string id, CancellationToken ct)
    {
        switch (kind)
        {
            case "recon":
            {
                var r = _reconstructions is null ? null : await _reconstructions.GetSharedAsync(id, ct);
                if (r is null) return null;
                var card = ReconCard(r);
                return new OgBoard(card.Boards[0].Fen, false, Card: card, MaxAge: EditableMaxAge);
            }
            case "line":
            {
                var l = _sharedLines is null ? null : await _sharedLines.GetByTokenAsync(id, ct);
                if (l is null) return null;
                var card = LineCard(l);
                return new OgBoard(card.Boards[0].Fen, false, Card: card, MaxAge: ExpiringMaxAge);
            }
        }
        return null;
    }
}
