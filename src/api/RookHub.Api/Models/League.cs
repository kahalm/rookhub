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
    /// <summary>„lichess" oder „chess.com".</summary>
    public string Site { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    /// <summary>„sicher" oder „wahrscheinlich".</summary>
    public string Confidence { get; set; } = string.Empty;
    public string? Evidence { get; set; }
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
