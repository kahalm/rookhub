using RookHub.Api.DTOs.League;
using RookHub.Api.Models;
using RookHub.Api.Services.Scoresheet;
using static RookHub.Api.Services.League.ClubPgnText;

namespace RookHub.Api.Services.League;

/// <summary>
/// Wer ist welche Seite? (aus <see cref="LeagueClubService"/> gezogen, Zerlegung Z14): der Abgleich einer Seite gegen
/// Meldelisten, gemerkte Zuordnungen und Megabase, welche Seite der Hochladende spielt, was der Nutzer festgelegt hat,
/// und daraus die zu speichernde Zeile samt Ersetzen durch den Vereinsnamen (<see cref="Build"/>). Dazu die Regel
/// „dieselbe Partie in einer anderen Fassung“ (<see cref="SameVersion"/>). Rein, ohne Datenbank — alles Nachgeschlagene
/// kommt in <see cref="Lookups"/>.
/// </summary>
internal static class ClubSideResolver
{
    internal sealed record Owner(string? Fide, string?[] Names)
    {
        public static readonly Owner Nobody = new(null, Array.Empty<string?>());
    }

    /// <summary>Welche Seite spielt der Hochladende? FIDE-ID vor Name; bei Gleichstand keine.</summary>
    internal static string? OwnerSideOf(Owner o, string? white, string? black, string? whiteFide, string? blackFide,
        LeagueRosterIndex.Hit hw, LeagueRosterIndex.Hit hb, LeagueMegaPlayers.Hit? mw, LeagueMegaPlayers.Hit? mb)
    {
        if (o.Fide != null)
        {
            var w = o.Fide == whiteFide || o.Fide == hw.Person?.Fide || o.Fide == mw?.Fide;
            var b = o.Fide == blackFide || o.Fide == hb.Person?.Fide || o.Fide == mb?.Fide;
            if (w != b) return w ? "white" : "black";
        }
        return o.Names.Length == 0 ? null : ScoresheetScanService.GuessOwnerSide(white, black, o.Names);
    }

    /// <summary>Eine Seite, wie sie gespeichert würde. <see cref="Fide"/> = gewählte FIDE-ID eines Spielers, der KEIN
    /// Ligaspieler ist; <see cref="Mega"/> = so ein Spieler, im Megabase-Verzeichnis gefunden — dann ist die Seite
    /// „bekannt", auch ohne Liga.</summary>
    internal sealed record Side(string? Name, LeagueRosterIndex.Hit Hit, int? Elo, bool Replace, string? Fide = null,
        LeagueMegaPlayers.Hit? Mega = null, bool Alias = false)
    {
        public bool Known => Hit.League || Mega != null;

        /// <summary>Wer es ist (für die gemerkten Zuordnungen): der Ligaspieler bzw. der Megabase-Eintrag.</summary>
        public LeagueNameAliases.Entry? Identity => Hit.Person is { } p ? new(p.Fide, p.Name)
            : Mega is { } m ? new(m.Fide, m.Name) : null;
    }

    /// <summary>Alles, was ein Abgleich nachschlägt: Meldelisten, Megabase-Verzeichnis, gemerkte Zuordnungen.</summary>
    internal sealed record Lookups(LeagueRosterIndex Roster, LeagueMegaPlayers.Lookup Mega,
        IReadOnlyDictionary<string, LeagueNameAliases.Entry> Aliases);

    /// <summary>
    /// Wer ist das? (1) Eine FIDE-ID aus der Partie, die ein Ligaspieler trägt; (2) eine gemerkte Zuordnung dieses
    /// Namens (<see cref="LeagueNameAliases"/> — sie schlägt den Namensabgleich, dafür wurde sie ja korrigiert); (3) die
    /// Meldelisten; (4) die Megabase. Zeigt eine Zuordnung auf jemanden, den Liste und Verzeichnis nicht (mehr) kennen,
    /// gilt ihr gemerkter Name samt FIDE-ID.
    /// </summary>
    internal static (LeagueRosterIndex.Hit Hit, LeagueMegaPlayers.Hit? Mega, bool Alias) Resolve(string? name, string? fide, Lookups lk)
    {
        var hit = lk.Roster.Match(name, fide);
        if (lk.Roster.ByFide(fide) is null && lk.Aliases.TryGetValue(LeagueNameAliases.KeyOf(name), out var a))
        {
            if (a.Fide != null && lk.Roster.ByFide(a.Fide) is { } p) return (new LeagueRosterIndex.Hit(true, p, new[] { p }), null, true);
            if (a.Fide != null) return (LeagueRosterIndex.None, lk.Mega.ByFide(a.Fide) ?? new LeagueMegaPlayers.Hit(a.Name, a.Fide), true);
            var byName = lk.Roster.Match(a.Name, null);
            if (byName.League && !byName.Ambiguous) return (byName with { LastNameOnly = false }, null, true);
            return (LeagueRosterIndex.None, lk.Mega.ByName(a.Name) ?? new LeagueMegaPlayers.Hit(a.Name, null), true);
        }
        return (hit, MegaOf(hit, name, fide, lk.Mega), false);
    }

    /// <summary>Wer kein Ligaspieler ist, wird im Megabase-Verzeichnis gesucht: über die FIDE-ID, sonst den Namen. Eine
    /// FIDE-ID, die das Verzeichnis nicht kennt, lässt den Namen nur gelten, wenn er ihr nicht widerspricht.</summary>
    internal static LeagueMegaPlayers.Hit? MegaOf(LeagueRosterIndex.Hit hit, string? name, string? fide, LeagueMegaPlayers.Lookup mega)
    {
        if (hit.League) return null;
        if (string.IsNullOrWhiteSpace(fide)) return mega.ByName(name);
        return mega.ByFide(fide) ?? (mega.ByName(name) is { } n && (n.Fide is null || n.Fide == fide.Trim())
            ? n with { Fide = fide.Trim() is { Length: <= 16 } f ? f : n.Fide } : null);
    }

    /// <summary>Die Vorgabe der Übersicht: Abgleich über Kopfzeile, ersetzt wird ein Spieler von Schwaz und der
    /// Hochladende selbst (Wunsch 2026-09-28: „alle Spieler vom Verein Schwaz").</summary>
    internal static (Side White, Side Black, string? OwnerSide) Defaults(Parsed p, Owner owner, Lookups lk)
    {
        var (hw, mw, aw) = Resolve(p.H("White"), p.H("WhiteFideId"), lk);
        var (hb, mb, ab) = Resolve(p.H("Black"), p.H("BlackFideId"), lk);
        var ownerSide = OwnerSideOf(owner, p.H("White"), p.H("Black"), p.H("WhiteFideId"), p.H("BlackFideId"), hw, hb, mw, mb);
        return (new Side(p.H("White"), hw, p.Elo("WhiteElo"), hw.OwnClub || ownerSide == "white", null, mw, aw),
            new Side(p.H("Black"), hb, p.Elo("BlackElo"), hb.OwnClub || ownerSide == "black", null, mb, ab), ownerSide);
    }

    /// <summary>Was der Nutzer für eine Seite festgelegt hat: ein gewählter Ligaspieler (FIDE-ID) schlägt den Namen, ein
    /// getippter Name wird neu abgeglichen, ohne Angabe gilt die Kopfzeile.
    ///
    /// <para><b>Ein Spieler des eigenen Vereins wird IMMER ersetzt</b> (<see cref="LeagueRosterIndex.Hit.OwnClub"/>), egal was
    /// der Nutzer angehakt hat — hier, an der EINEN Stelle, durch die alle drei Wege gehen: PGN-Import (mit und ohne Konto,
    /// Teilen-Link), einzelne Partie bzw. Formular-Übernahme (<c>AddGame</c>) und Korrigieren (<c>UpdateAsync</c>). Vorher
    /// erzwang nur das Korrigieren die Ersetzung; beim Hochladen genügte <c>replace:false</c>, obwohl die Teilen-Seite
    /// „wird immer ersetzt“ zusichert (Codereview 2026-09-29 F7-008, Entscheidung 2026-10-25). <c>Replace</c> des Nutzers
    /// kann also nur zusätzlich ersetzen (z. B. sich selbst oder einen Gast), nie einen eigenen Spieler nennen.</para></summary>
    internal static Side Decided(LeagueClubSideDecision? d, string? raw, string? rawFide, int? elo, Lookups lk)
    {
        var s = DecidedAsChosen(d, raw, rawFide, elo, lk);
        return s.Hit.OwnClub && !s.Replace ? s with { Replace = true } : s;
    }

    private static Side DecidedAsChosen(LeagueClubSideDecision? d, string? raw, string? rawFide, int? elo, Lookups lk)
    {
        d ??= new LeagueClubSideDecision();
        if (lk.Roster.ByFide(d.Fide) is { } p) return new Side(p.Name, new LeagueRosterIndex.Hit(true, p, new[] { p }), elo, d.Replace);
        var typed = !string.IsNullOrWhiteSpace(d.Name);
        var name = typed ? d.Name!.Trim() : raw;
        // Eine FIDE-ID, die kein Ligaspieler trägt (aus dem Megabase-Verzeichnis gewählt): bleibt an der Partie stehen —
        // und schlägt eine gemerkte Zuordnung (der Nutzer hat gerade selbst gewählt).
        var chosen = string.IsNullOrWhiteSpace(d.Fide) ? null : d.Fide.Trim();
        if (chosen != null)
        {
            var hit = lk.Roster.Match(name, chosen);
            return new Side(name, hit, elo, d.Replace, chosen.Length <= 16 ? chosen : null, MegaOf(hit, name, chosen, lk.Mega));
        }
        var (h, m, alias) = Resolve(name, typed ? null : rawFide, lk);
        return new Side(name, h, elo, d.Replace, null, m, alias);
    }

    /// <summary>Aus einer geprüften Zugfolge + den Seiten die zu speichernde Zeile (oder den Ablehnungsgrund).</summary>
    internal static (LeagueClubGame? Game, string? Reason) Build(LeagueClub club, Side w, Side b, IReadOnlyList<string> sans, int? year,
        string? result, string? evt)
    {
        var anon = club.AnonName;
        if (!w.Known && !b.Known) return (null, "noLeaguePlayer");
        if (!(w.Known && !w.Replace) && !(b.Known && !b.Replace)) return (null, "onlyOwnClub");
        (string Name, string? Fide) Real(Side s) =>
            (Clip(s.Hit.Person?.Name ?? s.Mega?.Name ?? LeagueNames.Clean(s.Name), 120) is { Length: > 0 } n ? n : "?",
                s.Hit.Person?.Fide ?? s.Fide ?? s.Mega?.Fide);
        (string Name, string? Fide, int? Elo) Out(Side s) => s.Replace ? (anon, null, null)
            : (Real(s).Name, Real(s).Fide, Elo(s.Elo));
        // Wer hinter „Schwaz" spielt, bleibt intern (Klassenkommentar von LeagueClubGame) — nur, wenn es jemand ist.
        (string? Name, string? Fide) Hidden(Side s) =>
            s.Replace && Real(s) is var (n, f) && (n != "?" && n != anon || f != null) ? (n, f) : (null, null);
        var (wn, wf, we) = Out(w);
        var (bn, bf, be) = Out(b);
        var (wrn, wrf) = Hidden(w);
        var (brn, brf) = Hidden(b);
        var anonymized = w.Replace || b.Replace;
        var game = new LeagueClubGame
        {
            ClubId = club.Id,
            Year = year,
            White = wn, Black = bn, WhiteFide = wf, BlackFide = bf, WhiteElo = we, BlackElo = be,
            Result = result is { } r && ClubPgnText.Results.Contains(r) ? r : "*",
            Event = anonymized ? null : Clip(LeagueNames.Clean(evt), 200) is { Length: > 0 } e && e != "?" ? e : null,
            Plies = sans.Count,
            MovesHash = HashOf(sans),
            Anonymized = anonymized,
            WhiteRealName = wrn, WhiteRealFide = wrf, BlackRealName = brn, BlackRealFide = brf,
        };
        game.Pgn = PgnOf(game, sans);
        return (game, null);
    }

    private static int? Elo(int? e) => e is >= 500 and <= 3000 ? e : null;

    internal static string Clip(string? s, int max) => s is null ? string.Empty : s.Length <= max ? s : s[..max];

    internal static LeagueClubPreviewSideDto SideDto(Side s, bool owner) => new()
    {
        Raw = s.Name, Elo = s.Elo, Match = MatchDto(s.Hit, s.Mega, s.Alias), Owner = owner, Replace = s.Replace,
    };

    internal static LeagueClubSideMatchDto MatchDto(LeagueRosterIndex.Hit h, LeagueMegaPlayers.Hit? mega = null, bool alias = false) => new()
    {
        League = h.League, Ambiguous = h.Ambiguous, Mega = !h.League && mega != null, Alias = alias,
        Name = h.Person?.Name ?? (h.League ? null : mega?.Name), Fide = h.Person?.Fide ?? (h.League ? null : mega?.Fide),
        Club = h.OwnClub, LastNameOnly = h.LastNameOnly,
        Candidates = h.Ambiguous ? h.Candidates.Take(8).Select(PersonDto).ToList() : new(),
    };

    /// <summary>Nicht erkannt (weder Liga noch Megabase noch gemerkt): ähnlich geschriebene Ligaspieler zur Schnellauswahl
    /// (0.596.0). Je PGN-Name einmal gerechnet — in einer Datei steht derselbe Name oft dutzendfach.</summary>
    internal static void AddSimilar(LeagueClubSideMatchDto m, string? raw, LeagueRosterIndex roster,
        Dictionary<string, List<LeagueRosterPersonDto>> cache)
    {
        if (m.League || m.Mega || m.Alias || string.IsNullOrWhiteSpace(raw)) return;
        var key = raw.Trim().ToLowerInvariant();
        if (!cache.TryGetValue(key, out var list))
            cache[key] = list = roster.Similar(raw).Select(PersonDto).ToList();
        m.Similar = list;
    }

    internal static LeagueRosterPersonDto PersonDto(LeagueRosterIndex.Person p) =>
        new() { Name = p.Name, Fide = p.Fide, Teams = p.Teams.Take(3).ToList(), Club = p.OwnClub };

    // ── Dieselbe Partie in einer anderen Fassung ────────────────────

    /// <summary>Wie viele Halbzüge vom Anfang zwei Fassungen gemeinsam haben müssen, um als dieselbe Partie zu gelten
    /// (höchstens so viele, wie die kürzere hat).</summary>
    public const int SameGamePrefixPlies = 16;

    /// <summary>Ist <paramref name="c"/> eine Fassung derselben Partie wie <paramref name="game"/>? Beide fest zugeordnet
    /// (<paramref name="cLive"/>/<paramref name="mine"/> = aufgelöste Paarung, <c>null</c> = keine): gleiche Paarung; sonst
    /// gleiches Jahr, gleiche Spieler und gleicher Anfang (<see cref="SameGamePrefixPlies"/>).</summary>
    internal static bool SameVersion(LeagueClubGame c, int? cLive, LeagueClubGame game, int? mine,
        IReadOnlyList<string> cSans, IReadOnlyList<string> sans) =>
        cLive is { } cid && mine is { } m ? cid == m : SamePlayers(c, game) && SamePrefix(cSans, sans);

    private static bool SamePlayers(LeagueClubGame a, LeagueClubGame b)
    {
        static bool Side(string an, string? af, string bn, string? bf) =>
            af is not null && bf is not null ? af == bf : string.Equals(an.Trim(), bn.Trim(), StringComparison.OrdinalIgnoreCase);
        return a.Year == b.Year
            && Side(a.White, a.WhiteFide ?? a.WhiteRealFide, b.White, b.WhiteFide ?? b.WhiteRealFide)
            && Side(a.Black, a.BlackFide ?? a.BlackRealFide, b.Black, b.BlackFide ?? b.BlackRealFide);
    }

    private static bool SamePrefix(IReadOnlyList<string> a, IReadOnlyList<string> b)
    {
        var n = Math.Min(SameGamePrefixPlies, Math.Min(a.Count, b.Count));
        if (n == 0) return false;
        for (var i = 0; i < n; i++)
            if (!string.Equals(a[i].TrimEnd('+', '#'), b[i].TrimEnd('+', '#'), StringComparison.Ordinal)) return false;
        return true;
    }
}
