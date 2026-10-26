namespace RookHub.Api.Models;

/// <summary>
/// Eine vom Nutzer mit „+" markierte, besonders gute Stellung (0.749.0, Wunsch 2026-10-11) — aus der Partie-Analyse, dem
/// Analysebrett oder dem Fehler-Training. Vorrat für ein späteres Feature, das diese Stellungen ALLEN zum Nachspielen
/// anbietet; bis dahin sieht sie nur der Markierende.
///
/// <para>Wer markiert hat, steht in <see cref="UserId"/> bzw. <see cref="AnonSessionId"/> (Besucher ohne Anmeldung, seit 0.847.0).</para>
///
/// <para>Eine Zeile je Nutzer und Stellung (<see cref="PositionKey"/> = FEN ohne Zugzähler): dieselbe Stellung aus einer
/// anderen Partie ist dieselbe Markierung. Die Herkunft ist Beiwerk — wo sie fehlt (Analysebrett), bleibt die Stellung
/// trotzdem brauchbar. Keine Fremdschlüssel auf die Partien: eine gelöschte Partie soll die Markierung nicht mitnehmen.</para>
/// </summary>
public class MarkedPosition
{
    public int Id { get; set; }

    /// <summary>Wer markiert hat: das Konto — oder <c>null</c>, dann ein Besucher ohne Anmeldung (<see cref="AnonSessionId"/>).</summary>
    public int? UserId { get; set; }
    public AppUser? User { get; set; }

    /// <summary>
    /// Ohne Anmeldung (0.847.0, Wunsch 2026-10-26: „auch anonymen Usern — notiere, wer es war"): die anonyme Kennung der App
    /// (<c>X-Visitor-Id</c>, dieselbe wie bei Puzzles/E-Books). Beim Anmelden wandern die Markierungen ins Konto
    /// (<see cref="Services.Games.MarkedPositionService.ClaimAnonymousAsync"/>); genau eines von beiden ist gesetzt.
    /// </summary>
    public string? AnonSessionId { get; set; }

    /// <summary>Brett, Seite am Zug, Rochade, en passant — der Vergleichsschlüssel (eindeutig je Nutzer).</summary>
    public string PositionKey { get; set; } = string.Empty;

    /// <summary>Die volle FEN, wie markiert (normalisiert, mit Zählern).</summary>
    public string Fen { get; set; } = string.Empty;

    /// <summary>Wo markiert: <c>analysis</c> (Partie-Analyse), <c>board</c> (Analysebrett), <c>mistake</c> (Fehler-Training).</summary>
    public string Context { get; set; } = "analysis";

    /// <summary>Eigene gespeicherte Partie, aus der die Stellung stammt (sonst <c>null</c>).</summary>
    public int? SavedGameId { get; set; }

    /// <summary>LeagueHub-Vereinspartie, aus der die Stellung stammt.</summary>
    public int? LeagueClubGameId { get; set; }

    /// <summary>Geteilte Partie (<c>/g/{token}</c>) eines anderen Nutzers.</summary>
    public string? ShareToken { get; set; }

    /// <summary>Halbzüge bis zu dieser Stellung in der Partie (0 = Grundstellung); <c>null</c> = nicht aus einer Partie.</summary>
    public int? Ply { get; set; }

    /// <summary>Der bessere Zug (UCI), wenn bekannt — im Fehler-Training die Lösung der Aufgabe.</summary>
    public string? BestUci { get; set; }

    /// <summary>In einer Nebenvariante markiert (0.848.0): Halbzug der Partie, an dem die Variante abzweigt (0 = Grundstellung).
    /// Dann ist <see cref="Ply"/> leer — die Stellung steht in keiner Partie, ist aber über den Abzweig erreichbar.</summary>
    public int? VariationFromPly { get; set; }

    /// <summary>Die Züge der Variante ab <see cref="VariationFromPly"/> bis zur markierten Stellung (UCI, Leerzeichen).</summary>
    public string? VariationUci { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
