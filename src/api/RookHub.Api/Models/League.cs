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
    /// <summary>
    /// Wer das Konto eingetragen hat (0.630.0, Wunsch: „beim Spieler vermerken, wer ihn hinzugefügt hat, in dem Fall dann anonym"):
    /// der Nutzername, <see cref="LeagueOnlineAccountService.Anonymous"/> über einen Teilen-Link, <c>null</c> bei älteren Konten und
    /// denen der Suche.
    /// </summary>
    public string? AddedBy { get; set; }
    /// <summary>Über welchen Teilen-Link eingetragen (SHA-256 hex des Tokens wie bei den Vereinspartien) — der Link selbst steht nirgends.</summary>
    public string? AddedShareHash { get; set; }

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

/// <summary>
/// Ein vorgeschlagenes Online-Konto (0.607.0, Wunsch 2026-09-30): LeagueHub sucht auf Lichess und chess.com nach Konten, deren
/// Name zum Spieler passt, und legt sie hier ab — ein Verwalter übernimmt sie (dann ein <see cref="LeagueOnlineAccount"/>) oder
/// verwirft sie. Verworfene bleiben stehen, damit derselbe Vorschlag nicht wiederkommt.
/// </summary>
public class LeagueAccountSuggestion
{
    public int Id { get; set; }
    public string FideId { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    /// <summary>Wie stark die Hinweise sind — sortiert die Liste; die Gründe stehen in <see cref="Evidence"/>.</summary>
    public int Score { get; set; }
    /// <summary>Die Hinweise als Satz („Klarname im Profil; Land Österreich") — wird beim Übernehmen der Kommentar.</summary>
    public string Evidence { get; set; } = string.Empty;
    /// <summary>Der Name, der im Profil steht (falls einer).</summary>
    public string? ProfileName { get; set; }
    public string? Location { get; set; }
    /// <summary>Zuletzt auf der Seite gesehen.</summary>
    public DateTime? LastActive { get; set; }
    public LeagueSuggestionStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    /// <summary>Wer vorgeschlagen hat: <c>null</c> = die Namenssuche (<c>LeagueAccountFinder</c>), <c>team</c> = die Team-Suche
    /// (<c>LeagueTeamScout</c>, 0.612.0). Die Namenssuche räumt beim erneuten Suchen nur IHRE offenen Vorschläge weg.</summary>
    public string? Source { get; set; }
}

public enum LeagueSuggestionStatus { Open = 0, Rejected = 1 }

/// <summary>Wann für einen Spieler zuletzt nach Konten gesucht wurde — und sein Jahrgang (Minderjährige werden nie gesucht).</summary>
public class LeagueAccountScan
{
    public string FideId { get; set; } = string.Empty;
    public int? BirthYear { get; set; }
    /// <summary>Föderation laut FIDE — ein Profil aus diesem Land ist kein Widerspruch.</summary>
    public string? Federation { get; set; }
    public DateTime ScannedAt { get; set; }
    /// <summary>Warum nicht gesucht wurde („minderjährig", „Jahrgang unbekannt") oder was schiefging.</summary>
    public string? Note { get; set; }
    public int Found { get; set; }
    /// <summary>Mit welcher Fassung der Regeln gesucht wurde (<c>LeagueAccountFinder.CurrentVersion</c>) — ältere werden einmal
    /// neu gesucht, sonst gälte eine geänderte Regel erst nach 90 Tagen.</summary>
    public int Version { get; set; }
}

/// <summary>
/// Eine Lichess-Übertragung (Broadcast) eines Turniers am Brett, deren Partien in die Spielerkarten eingespielt werden
/// (0.608.0). Die Partien tragen die FIDE-ID beider Spieler — zugeordnet wird darüber, nicht über Namen.
/// </summary>
public class LeagueBroadcast
{
    /// <summary>Kennung des Turniers auf Lichess (8 Zeichen).</summary>
    public string TourId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Location { get; set; }
    public DateTime? StartsAt { get; set; }
    public DateTime? EndsAt { get; set; }
    /// <summary>Von Hand hinzugefügt (sonst über die Suche gefunden).</summary>
    public bool Manual { get; set; }
    public DateTime FoundAt { get; set; }
    /// <summary>Zuletzt eingespielt; <c>null</c> = noch nie.</summary>
    public DateTime? ImportedAt { get; set; }
    /// <summary>Alle Runden vorbei und eingespielt — wird nicht mehr geholt.</summary>
    public bool Finished { get; set; }
    /// <summary>Partien mit Ligaspielern beim letzten Einspielen.</summary>
    public int Games { get; set; }
    public string? Error { get; set; }
}

/// <summary>
/// Ein Lichess-Konto aus dem Umfeld der Tiroler Vereine (0.612.0): Mitglied eines Tiroler Lichess-Teams oder Spieler für ein
/// solches in einem Team-Battle (Online-TMM 2021, Quarantäne-Liga …). Die Team-Suche (<c>LeagueTeamScout</c>) prüft jedes Konto
/// einmal — Klarname im Profil, sonst Stellungen gegen die Spieler des Vereins — und legt Treffer als Vorschläge ab.
/// </summary>
public class LeagueScoutAccount
{
    /// <summary>Lichess-Kennung (klein).</summary>
    public string UserName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    /// <summary>Die Tiroler Teams, in denen es Mitglied ist (durch „; " getrennt).</summary>
    public string? Teams { get; set; }
    /// <summary>Für welches Team es in einem Team-Battle gespielt hat — der Verein, gegen dessen Spieler die Stellungen zählen.</summary>
    public string? PlayedFor { get; set; }
    /// <summary>In welchen Team-Battles (Serie ohne Runde, „Online TMM 2021", „Lichess Quarantäne-Liga 7C"; durch „; " getrennt,
    /// 0.619.0) — für die Konto-Prüfung (i).</summary>
    public string? Events { get; set; }
    public DateTime FoundAt { get; set; }
    public DateTime? CheckedAt { get; set; }
    /// <summary>Was die Prüfung ergab („Vorschlag für …", „kein Klarname, zu wenige Partien" …).</summary>
    public string? Result { get; set; }
}

/// <summary>
/// Ein Online-Konto, das ein Spieler SELBST gemeldet hat (0.619.0) — z. B. auf der Meldeliste der Online-TMM 2021 des Tiroler
/// Landesverbands (Klarname + Lichess-Name, mit Zustimmung veröffentlicht). Die Konto-Prüfung (i) zeigt es als stärksten Beleg.
/// </summary>
public class LeagueSelfReport
{
    public int Id { get; set; }
    public string FideId { get; set; } = string.Empty;
    public string Site { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    /// <summary>Woher („Meldeliste Online-TMM 2021").</summary>
    public string Source { get; set; } = string.Empty;
    /// <summary>Für welches Team gemeldet.</summary>
    public string? Team { get; set; }
    /// <summary>
    /// <c>null</c> = der Spieler hat es SELBST gemeldet. Sonst der Name dessen, der die Zuordnung gemeldet hat (0.629.0, z. B. „Ranni"
    /// mit seiner Vorbereitungs-Liste vom 01.10.2026) — im (i) eine eigene Zeile „Gemeldet von …", nicht „Selbstmeldung".
    /// </summary>
    public string? Reporter { get; set; }
    /// <summary>Anmerkung des Meldenden („bestätigt, eigene Prüfung", „uninteressant — keine brauchbaren Partien").</summary>
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
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
///
/// <para><b>Über einen Teilen-Link</b> (ohne Konto) trägt die Zeile den Link als SHA-256 (<see cref="UploadShareHash"/>) —
/// auch bei anonymisierten Partien: der Link ist der Weg, nicht die Person, und nur so entfernt ein Verwalter alles, was
/// über einen weitergereichten Link hereinkam (Codereview 2026-09-29, A2-009).</para>
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
    /// <summary>Über welchen Teilen-Link hochgeladen (SHA-256 hex des Tokens, <c>LeagueClubService.ShareHashOf</c>) —
    /// <c>null</c> = angemeldet hochgeladen. Der Link selbst steht nirgends.</summary>
    public string? UploadShareHash { get; set; }
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
