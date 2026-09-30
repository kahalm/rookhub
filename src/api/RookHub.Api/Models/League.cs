namespace RookHub.Api.Models;

// LeagueHub — Tiroler Mannschaftsmeisterschaft (TMM): Aufstellungs-Prognosen je Brett.
//
// Das Schema folgt bewusst der Vorlage aus der Python-Fassung (~/claude/league-analyzer, tmm.sqlite):
// Zeilen sind so gespeichert, wie chess-results sie liefert (Teamnamen als Text, keine Vereins-Tabelle).
// Die Spielgemeinschaften wechseln ihre Namen fast jede Saison — eine normalisierte Vereins-Tabelle
// hätte mehr Fehler eingebaut als verhindert; die Zuordnung macht LeagueClub.Canonical.

/// <summary>Eine Liga einer Saison (= ein chess-results-Turnier), z. B. „TMM Landesliga 2026/2027".</summary>
public class LeagueTournament
{
    /// <summary>chess-results-Turniernummer (tnr…aspx) — PK.</summary>
    public int Tnr { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>„2026/27".</summary>
    public string Season { get; set; } = string.Empty;
    /// <summary>1 = Landesliga, 2 = 1. Klasse, 3 = 2. Klasse, 4 = Gebietsklasse.</summary>
    public int Level { get; set; }
    public string League { get; set; } = string.Empty;
    /// <summary>„Ost", „West", „Aufstiegs-Playoff" … oder leer.</summary>
    public string Grp { get; set; } = string.Empty;
    /// <summary>„Liga" oder „Playoff" — trainiert und prognostiziert wird nur „Liga".</summary>
    public string Stage { get; set; } = "Liga";
    /// <summary>Abgebrochene Saison (Corona 2021/22) — zählt nicht.</summary>
    public bool Aborted { get; set; }
    public string? Start { get; set; }
    public string? End { get; set; }
    public int? Rounds { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Datum einer Runde (aus der Brettpaarungs-Seite, „1. Runde am 03.10.2026").</summary>
public class LeagueRound
{
    public int Id { get; set; }
    public int Tnr { get; set; }
    public int Round { get; set; }
    public DateOnly? Date { get; set; }
}

/// <summary>Mannschaftskampf. <see cref="Away"/> = „spielfrei" bei Freilos.</summary>
public class LeagueMatch
{
    public int Id { get; set; }
    public int Tnr { get; set; }
    public int Round { get; set; }
    public int? MatchNo { get; set; }
    public string Home { get; set; } = string.Empty;
    public string Away { get; set; } = string.Empty;
    public double? HomePts { get; set; }
    public double? AwayPts { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public string? Venue { get; set; }
}

/// <summary>Eine Brettpartie. Spieler sind leer (null) bei „Brett nicht besetzt".</summary>
public class LeagueGame
{
    public int Id { get; set; }
    public int Tnr { get; set; }
    public int Round { get; set; }
    public int MatchNo { get; set; }
    public int Board { get; set; }
    public string HomeTeam { get; set; } = string.Empty;
    public string AwayTeam { get; set; } = string.Empty;
    public string? HomePlayer { get; set; }
    public string? AwayPlayer { get; set; }
    public string? HomeTitle { get; set; }
    public string? AwayTitle { get; set; }
    /// <summary>„w" oder „s" (Farbe des Heimspielers).</summary>
    public string? HomeColor { get; set; }
    public string Result { get; set; } = string.Empty;
    public double? HomeScore { get; set; }
    public double? AwayScore { get; set; }
    /// <summary>0 = gespielt, 1 = kampflos (+/-), 2 = beide nicht angetreten („- - -", z. B. Corona-Abbruch).</summary>
    public int Forfeit { get; set; }
    public string? HomeFide { get; set; }
    public string? AwayFide { get; set; }
    public int? HomeRb { get; set; }
    public int? AwayRb { get; set; }
    public int? HomeElo { get; set; }
    public int? AwayElo { get; set; }
    public string? PgnId { get; set; }
}

/// <summary>Ein gemeldeter Spieler eines Teams (Startrangliste art=16), samt Einsatz-Statistik (art=20).</summary>
public class LeaguePlayer
{
    public int Id { get; set; }
    public int Tnr { get; set; }
    public string Team { get; set; } = string.Empty;
    /// <summary>Platz in der Meldeliste — die Bretter folgen dieser Reihenfolge.</summary>
    public int? RosterBoard { get; set; }
    public int? StartNr { get; set; }
    public string? Title { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Name ohne akademische Titel, klein („schnabl, andreas") — Schlüssel zu den Brettpaarungen.</summary>
    public string NameKey { get; set; } = string.Empty;
    public string? FideId { get; set; }
    public int? EloI { get; set; }
    public int? EloN { get; set; }
    public string? Fed { get; set; }
    public double? Points { get; set; }
    public int? Games { get; set; }
    public int? EloPerf { get; set; }
}

/// <summary>Spielerkarte (Eröffnungsprofil als JSON) + alle Turnierpartien als PGN, je FIDE-ID.</summary>
public class LeaguePlayerProfile
{
    public string FideId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int GameCount { get; set; }
    /// <summary>Kurzprofil wie es die Oberfläche zeigt (weiß/schwarz, Linien, letzte Partien).</summary>
    public string ProfileJson { get; set; } = "{}";
    /// <summary>Alle Partien (Lumbra + chess-results, Dubletten entfernt), neueste zuerst.</summary>
    public string Pgn { get; set; } = string.Empty;
    /// <summary>Wann die chess-results-Partien zuletzt geholt wurden (Nachladen, wenn älter als 14 Tage).</summary>
    public DateTime? CrFetchedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>Ein Online-Konto, das der Spieler SELBST offengelegt hat (Klarname im Profil …).</summary>
public class LeagueOnlineAccount
{
    public int Id { get; set; }
    public string FideId { get; set; } = string.Empty;
    /// <summary>Kürzel der Seite aus <c>LeagueOnlineSites</c> — heute „lichess" oder „chess.com".</summary>
    public string Site { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    /// <summary>„sicher" (= gesichert) oder „wahrscheinlich" (= unsicher). Teilen-Links zeigen nur „sicher".</summary>
    public string Confidence { get; set; } = string.Empty;
    /// <summary>Kommentar — beim Import aus Python die Belege, in LeagueHub frei (0.605.0).</summary>
    public string? Evidence { get; set; }
    /// <summary>In LeagueHub angelegt oder bearbeitet — ein erneuter Import des Bündels lässt diese Zeile stehen.</summary>
    public bool Manual { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // ── Abruf der Partien (LeagueOnlineSync, 0.605.0) ──
    /// <summary>Letzter Abruf (auch ein gescheiterter); <c>null</c> = noch nie.</summary>
    public DateTime? SyncedAt { get; set; }
    /// <summary>Bis wohin geholt ist: Lichess <c>createdAt</c>, chess.com <c>end_time</c> der jüngsten Partie (ms).</summary>
    public long SyncCursor { get; set; }
    public string? SyncError { get; set; }
    /// <summary>Beim letzten Abruf blieb etwas übrig (Deckel je Lauf) — gleich wieder dran, nicht erst nach dem Intervall.</summary>
    public bool SyncMore { get; set; }
    /// <summary>Gespeicherte Partien dieses Kontos.</summary>
    public int GameCount { get; set; }
}

/// <summary>
/// Eine Online-Partie eines Ligaspielers (0.605.0, Wunsch 2026-09-30: „im Hintergrund holst du die Spiele dieser User und
/// legst sie in der DB ab"). Nur Standardschach ab der Grundstellung; die Züge als englische SAN (Hauptvariante), dazu die
/// ersten <c>LeagueOnlineSync.LineMaxPlies</c> Halbzüge gesondert für den Eröffnungsbaum. Getrennt vom Profil-PGN: das hält
/// die Brettpartien, und ein Blitzspieler hätte es mit tausenden Partien aufgebläht.
/// </summary>
public class LeagueOnlineGame
{
    public long Id { get; set; }
    public int AccountId { get; set; }
    public LeagueOnlineAccount Account { get; set; } = null!;
    /// <summary>Denormalisiert — der Baum fragt je Spieler, nicht je Konto.</summary>
    public string FideId { get; set; } = string.Empty;
    /// <summary>Kennung der Partie auf der Seite (Lichess-Id, chess.com-Nummer).</summary>
    public string ExternalId { get; set; } = string.Empty;
    public DateTime PlayedAt { get; set; }
    /// <summary>bullet, blitz, rapid, classical, correspondence (<c>LeagueOnlineSync.Speeds</c>).</summary>
    public string Speed { get; set; } = string.Empty;
    public bool Rated { get; set; }
    /// <summary>Der Spieler hatte Weiß.</summary>
    public bool White { get; set; }
    /// <summary>„1-0", „0-1" oder „1/2-1/2".</summary>
    public string Result { get; set; } = string.Empty;
    public string? Opponent { get; set; }
    public int? OpponentRating { get; set; }
    public int? PlayerRating { get; set; }
    /// <summary>Die ersten Halbzüge mit Leerzeichen („e4 e5 Nf3") — der Baum sucht darin per Präfix.</summary>
    public string Line { get; set; } = string.Empty;
    /// <summary>Die ganze Hauptvariante in SAN.</summary>
    public string Moves { get; set; } = string.Empty;
    public int Plies { get; set; }
}

/// <summary>Öffentlicher Teilen-Link auf genau eine Begegnung (Token = Geheimnis, 144 Bit).</summary>
public class LeagueShare
{
    public string Token { get; set; } = string.Empty;
    public int Tnr { get; set; }
    public int Round { get; set; }
    public string Team { get; set; } = string.Empty;
    public DateOnly Expires { get; set; }
    public int? CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>Fertig gerechnete Ansicht einer Liga (Prognosen aller Begegnungen) — beim Aktualisieren neu.</summary>
public class LeagueView
{
    public int Tnr { get; set; }
    public string Json { get; set; } = "{}";
    public DateTime GeneratedAt { get; set; }
}

/// <summary>
/// Eine Partie aus der VEREINS-Datenbank (von Mitgliedern hochgeladen: PGN oder Partieformular). Mindestens eine Seite
/// ist ein Ligaspieler, sonst wird sie gar nicht angenommen. Datum nur als JAHR.
///
/// <para><b>Anonymisiert</b> (<see cref="Anonymized"/>, Häkchen „Meinen Namen durch Schwaz ersetzen"): die Seite des
/// Hochladenden heißt „Schwaz", ohne Elo und FIDE-ID, und es wird WEDER gespeichert, wer dahinter steht, NOCH wer
/// hochgeladen hat (<see cref="UploadedByUserId"/> und <see cref="CreatedAt"/> bleiben leer, Veranstaltung fällt weg) —
/// Wunsch des Nutzers, damit man nicht gegen die eigenen Spieler vorbereiten kann.</para>
/// </summary>
public class LeagueClubGame
{
    public int Id { get; set; }
    public int? Year { get; set; }
    public string White { get; set; } = string.Empty;
    public string Black { get; set; } = string.Empty;
    public string? WhiteFide { get; set; }
    public string? BlackFide { get; set; }
    public int? WhiteElo { get; set; }
    public int? BlackElo { get; set; }
    public string Result { get; set; } = "*";
    public string? Event { get; set; }
    public int Plies { get; set; }
    /// <summary>Die Partie als PGN (nur Hauptvariante, Kopf auf das Nötige beschränkt).</summary>
    public string Pgn { get; set; } = string.Empty;
    /// <summary>SHA-256 (hex) über die Hauptvariante — erkennt dieselbe Partie ein zweites Mal.</summary>
    public string MovesHash { get; set; } = string.Empty;
    public bool Anonymized { get; set; }
    /// <summary>Wer hochgeladen hat — nur bei NICHT anonymisierten Partien (dann darf er sie selbst löschen).</summary>
    public int? UploadedByUserId { get; set; }
    public DateTime? CreatedAt { get; set; }
}

/// <summary>
/// Eine gemerkte Namens-Zuordnung der Vereins-Datenbank (Wunsch 2026-09-28: „wenn ich einen Spieler umbenenne, merk dir
/// das zum Original und matche das zukünftig bei allen selbst"): so, wie ein Name in einem PGN stand
/// (<see cref="NameKey"/>, klein, ohne Akzente und Titel), gehört er zu diesem Spieler — Ligaspieler oder aus der
/// Megabase. Gilt für ALLE künftigen Abgleiche. Bewusst OHNE Verweis auf eine Partie oder den, der korrigiert hat: sonst
/// ließe sich über die Zuordnung doch nachvollziehen, wer hinter einem „Schwaz" steht.
/// </summary>
public class LeagueNameAlias
{
    public int Id { get; set; }
    public string NameKey { get; set; } = string.Empty;
    /// <summary>FIDE-ID des Spielers; fehlt bei Ligaspielern ohne ID oder Megabase-Einträgen ohne ID.</summary>
    public string? Fide { get; set; }
    /// <summary>Der Name, wie er gespeichert wird (Meldeliste bzw. Megabase).</summary>
    public string Name { get; set; } = string.Empty;
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// Ein Spieler aus der ChessBase-Megabase (ganzes Verzeichnis, nicht nur Ligaspieler) — für die Namenssuche beim
/// Korrigieren in der Vereins-Datenbank („mit Häkchen über alle Spieler der Megabase", Wunsch 2026-09-28). Eingespielt
/// über <c>POST /api/league/admin/mega-players</c> (Skript <c>scan_mega_players.py</c>), ersetzt jedes Mal alles.
/// </summary>
public class LeagueMegaPlayer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Name klein, ohne Akzente („hengl, philip") — Präfix-Suche über den Index.</summary>
    public string NameKey { get; set; } = string.Empty;
    public string? FideId { get; set; }
    public int Games { get; set; }
    public int? LastYear { get; set; }
    public int? MaxElo { get; set; }
}

/// <summary>
/// Eine eingereichte Partieliste, die noch nicht (ganz) importiert ist (0.595.0, Wunsch 2026-09-28: „wenn jemand eine neue
/// Ligapartie einträgt — egal wie — soll sie gleich online abgelegt werden, damit ein Admin den Import fertigstellen kann,
/// wenn er keine Lust mehr hat"; analog zu den Partieformularen). Angelegt, sobald die Übersicht gelesen wird; trägt den
/// Rohtext, den Stand der Übersicht (Korrekturen, für den Server opak) und welche Partien schon importiert sind.
/// <para>Mit dem Abschluss (alles importiert) oder Verwerfen wird die Zeile GELÖSCHT — der Rohtext nennt die Spieler von
/// Schwaz noch mit Namen, und das soll nicht liegen bleiben. Ohne Bewegung nach <c>LeagueClubDraftService.Retention</c> ebenso.</para>
/// </summary>
public class LeagueClubDraft
{
    public int Id { get; set; }
    /// <summary>Wer eingereicht hat; <c>null</c> = über einen Teilen-Link ohne Konto (dann gehört er dem <see cref="AccessKey"/>).</summary>
    public int? UserId { get; set; }
    /// <summary>Geheimer Schlüssel des Browsers (32 Hex) — nur ohne Konto.</summary>
    public string? AccessKey { get; set; }
    /// <summary>HMAC der IP (ohne Konto) — für den Deckel offener Entwürfe je Adresse; die Adresse selbst steht nirgends.</summary>
    public string? AnonIpHash { get; set; }
    /// <summary>Woher: <c>datei</c>, <c>text</c>, <c>lichess</c>, <c>rookhub</c>.</summary>
    public string? Source { get; set; }
    /// <summary>Dateiname bzw. Adresse der Studie — zum Wiedererkennen in der Liste.</summary>
    public string? Label { get; set; }
    public string Pgn { get; set; } = string.Empty;
    /// <summary>Der Stand der Übersicht (JSON der Seite, opak).</summary>
    public string? StateJson { get; set; }
    /// <summary>Nummern der schon importierten Partien (CSV).</summary>
    public string? Imported { get; set; }
    public int GameCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
