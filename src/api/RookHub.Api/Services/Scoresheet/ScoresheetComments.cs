namespace RookHub.Api.Services.Scoresheet;

/// <summary>
/// Die PGN-Kommentare einer gelesenen Partie (zweite Zerlegungsrunde W7b aus <see cref="ScoresheetProcessor"/>). Rein:
/// keine Datenbank, kein Modell.
/// </summary>
public static class ScoresheetComments
{
    /// <summary>
    /// Kommentare im PGN: wo der Zug vom Formular abweicht (Lesefehler repariert, Eintrag erschlossen), steht,
    /// was DASTAND — dieselbe Auskunft, die man beim Nachspielen braucht, um dem Zug zu misstrauen. Die nicht
    /// aufgelösten Einträge hängen am letzten Zug.
    /// </summary>
    public static Dictionary<int, string> For(ScoresheetResolution r)
    {
        var comments = new Dictionary<int, string>();
        for (var i = 0; i < r.Plies.Count; i++)
        {
            var p = r.Plies[i];
            if (p.Match is ScoresheetResolver.Matches.Fuzzy or ScoresheetResolver.Matches.Guess)
                comments[i] = $"sheet: {(string.IsNullOrWhiteSpace(p.Written) ? "?" : p.Written)}";
            else if (p.Match == ScoresheetResolver.Matches.Inserted)
                comments[i] = "sheet: —"; // stand nicht auf dem Formular
        }
        // Doppelt notierte Einträge hängen am Zug davor.
        foreach (var skip in r.Skipped)
        {
            var at = Math.Max(0, skip.AfterPly - 1);
            if (at >= r.Plies.Count) continue;
            var text = "sheet, extra: " + skip.Written;
            comments[at] = comments.TryGetValue(at, out var c0) ? c0 + " | " + text : text;
        }
        if (r.Unresolved.Count > 0 && r.Plies.Count > 0)
        {
            var text = "sheet, not resolved: " + string.Join(' ', r.Unresolved);
            var last = r.Plies.Count - 1;
            comments[last] = comments.TryGetValue(last, out var c) ? c + " | " + text : text;
        }
        // Notierte Restzeit (1.015.0) vorn im Kommentar, wie Lichess sie schreibt: {[%clk 1:12:00] sheet: Sf3}.
        for (var i = 0; i < r.Plies.Count; i++)
            if (PgnClock.Combine(ScoresheetClock.Parse(r.Plies[i].Clock), comments.GetValueOrDefault(i)) is { } withClock)
                comments[i] = withClock;
        return comments;
    }
}
