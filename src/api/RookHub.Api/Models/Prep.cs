namespace RookHub.Api.Models;

// Spielervorbereitung („Prep", 2026-10-01): ein Partiebestand aus ChessBase-Megabase und Lumbras GigaBase, in dem sich
// JEDER Spieler suchen lässt — nicht nur die Tiroler Ligaspieler von LeagueHub. Gespeichert werden nur Kopfdaten und
// die Hauptvariante; Kommentare, Varianten und NAGs fallen beim Einlesen weg (Plan PREP_PLAN_2026-10-01, Abschnitt 1).
//
// Bewusst schmal, weil der volle Bestand über 15 Mio. Zeilen hat und auf derselben Platte liegt wie die Prod-DB:
// keine Fremdschlüssel (jeder kostete einen eigenen Index), Turnier/Ort als eigene Tabelle, das Datum als Zahl.

/// <summary>
/// Ein Spieler des Bestands. Identität: die FIDE-ID, wenn eine Partie sie nennt; sonst der Namensschlüssel —
/// zwei Namensvettern ohne FIDE-ID sind dann EIN Spieler (wie im Megabase-Verzeichnis von LeagueHub).
/// </summary>
public class PrepPlayer
{
    public int Id { get; set; }
    /// <summary>Der Name, wie die erste Partie ihn schreibt („Carlsen, Magnus").</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Klein, ohne Akzente und Titel („carlsen, magnus") — Präfix-Suche über den Index.</summary>
    public string NameKey { get; set; } = string.Empty;
    public string? FideId { get; set; }
    /// <summary>Hash der Identität (<c>#FIDE</c> bzw. Namensschlüssel), eindeutig — daran findet das Einlesen den Spieler
    /// wieder, ohne einen langen Text-Index zu brauchen.</summary>
    public long KeyHash { get; set; }
    /// <summary>Partien im Bestand (wird beim Einlesen mitgezählt).</summary>
    public int Games { get; set; }
    public short? FirstYear { get; set; }
    public short? LastYear { get; set; }
    public short? MaxElo { get; set; }
}

/// <summary>Turnier + Ort — wiederholen sich tausendfach; als Text je Partie wären sie der größte Posten nach den Zügen.</summary>
public class PrepEvent
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Site { get; set; }
    /// <summary>Hash aus Name + Ort, eindeutig.</summary>
    public long KeyHash { get; set; }
}

/// <summary>
/// Eine Partie — EINE Zeile, auch wenn Megabase und Lumbra sie beide führen (<see cref="Sources"/>).
/// Dieselbe Partie = gleiche Zugfolge (<see cref="MovesHash"/>) UND dieselben zwei Spieler.
/// </summary>
public class PrepGame
{
    public long Id { get; set; }
    /// <summary><c>null</c> = Spieler unbekannt („?", „NN").</summary>
    public int? WhiteId { get; set; }
    public int? BlackId { get; set; }
    public short? WhiteElo { get; set; }
    public short? BlackElo { get; set; }
    /// <summary><see cref="PrepResult"/>.</summary>
    public byte Result { get; set; }
    /// <summary>Datum als Zahl JJJJMMTT, unbekannter Monat/Tag = 00 („1975.??.??" → 19750000). <c>null</c> = kein Jahr.
    /// Sortierbar, und das Jahr ist <c>PlayedOn / 10000</c>.</summary>
    public int? PlayedOn { get; set; }
    public int? EventId { get; set; }
    public string? Round { get; set; }
    /// <summary>Drei Zeichen („B90"); Lumbras Unterteilung („C47d") fällt weg.</summary>
    public string? Eco { get; set; }
    public short Plies { get; set; }
    /// <summary>Hauptvariante als SAN mit Leerzeichen, ohne Zugnummern, Schach-/Matt-Zeichen und Ergebnis („e4 e5 Nf3").</summary>
    public string Moves { get; set; } = string.Empty;
    /// <summary>Die ersten 8 Byte von SHA-256 über <see cref="Moves"/>.</summary>
    public long MovesHash { get; set; }
    /// <summary>Bits aus <see cref="PrepSources"/>.</summary>
    public byte Sources { get; set; }
}

/// <summary>Ein eingelesenes Paket. (Quelle, Paket) ist eindeutig: ein doppelt geschicktes Paket wird erkannt und nicht
/// noch einmal verbucht, ein abgebrochener Lauf setzt beim ersten fehlenden Paket fort.</summary>
public class PrepImport
{
    public int Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public int Chunk { get; set; }
    /// <summary>Nummer der ersten Partie des Pakets in der Quelle (0-basiert) — fängt einen Lauf mit anderer Paketgröße ab.</summary>
    public long FirstGame { get; set; }
    public int Read { get; set; }
    public int Added { get; set; }
    public int Duplicates { get; set; }
    public int Discarded { get; set; }
    /// <summary>Verworfene nach Grund als JSON (<c>{"fen":2,"noMoves":1}</c>).</summary>
    public string? DiscardReasons { get; set; }
    public int Millis { get; set; }
    public DateTime CreatedAt { get; set; }
}

public static class PrepSources
{
    public const byte Mega = 1;
    public const byte Lumbra = 2;

    /// <summary>Name in der Adresse → Bit; unbekannt = 0.</summary>
    public static byte Parse(string? source) => (source ?? "").Trim().ToLowerInvariant() switch
    {
        "mega" => Mega,
        "lumbra" => Lumbra,
        _ => 0,
    };

    public static string Name(byte bit) => bit switch { Mega => "Mega", Lumbra => "Lumbra", _ => "?" };
}

public static class PrepResult
{
    public const byte Unknown = 0, WhiteWins = 1, BlackWins = 2, Draw = 3;

    public static byte Parse(string? r) => r?.Trim() switch
    {
        "1-0" => WhiteWins,
        "0-1" => BlackWins,
        "1/2-1/2" or "½-½" => Draw,
        _ => Unknown,
    };

    public static string Text(byte r) => r switch { WhiteWins => "1-0", BlackWins => "0-1", Draw => "1/2-1/2", _ => "*" };
}
