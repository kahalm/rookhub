namespace RookHub.Api.DTOs;

/// <summary>Anfrage: „Welche Stellungen in meinen Repertoires ähneln dieser hier?"</summary>
public class SimilarPositionsRequestDto
{
    /// <summary>FEN der Ausgangsstellung (nur Stellungsfeld + Seite am Zug werden ausgewertet).</summary>
    public string Fen { get; set; } = string.Empty;

    /// <summary>Zu durchsuchende Repertoires; leer/fehlend = alle lesbaren.</summary>
    public List<int>? RepertoireIds { get; set; }

    /// <summary>Gewichtung: <c>struktur</c> | <c>ausgewogen</c> (Default) | <c>stellungsbild</c>.</summary>
    public string? Preset { get; set; }

    /// <summary>Auch farbvertauschte Spiegelbilder finden (Default <c>true</c>).</summary>
    public bool? IncludeMirrored { get; set; }

    /// <summary>Nur Stellungen mit derselben Seite am Zug (Default <c>false</c> = reiner Strukturvergleich).</summary>
    public bool? SameSideToMove { get; set; }

    /// <summary>Optionaler Zug, den der Nutzer in der Anfragestellung erwägt („ich denke hier über
    /// 12.Sd5 nach — wo geht der noch?"). Treffer, an denen dieser Zug auch der Repertoirezug ist,
    /// rücken nach vorn; ausgeschlossen wird ohne <see cref="OnlyWithMove"/> nichts.</summary>
    public SimilarMoveInputDto? Move { get; set; }

    /// <summary>Nur Treffer zeigen, an denen der mitgegebene Zug auch gespielt wird (Default
    /// <c>false</c> = der Zug ist ein Bonus, kein Filter). Ohne auflösbaren Zug wirkungslos.</summary>
    public bool? OnlyWithMove { get; set; }

    /// <summary>Mindest-Score 0…100. Fehlt er, setzt ihn der Server JE VOREINSTELLUNG
    /// (<c>struktur</c> 67 / <c>ausgewogen</c> 75 / <c>stellungsbild</c> 79 — die drei Gewichtungen
    /// erzeugen verschiedene Wertebereiche; Herleitung samt Messtabelle in
    /// <c>RepertoireSimilarityService.DefaultMinScoreFor</c>). Ein hier gesetzter Wert schlägt ihn.</summary>
    public int? MinScore { get; set; }

    /// <summary>Maximale Trefferzahl 1…100 (Default 25).</summary>
    public int? Limit { get; set; }
}

/// <summary>
/// Der erwogene Zug in der Anfragestellung. Entweder <see cref="From"/>+<see cref="To"/> (so wie das
/// Brett ihn liefert — BEIDE Felder, sonst zählt die Angabe nicht) ODER <see cref="San"/> — SAN wird
/// gegen die Anfrage-FEN aufgelöst. Verglichen wird immer from→to (+ Umwandlungsfigur), nie die
/// SAN-Zeichenkette.
/// </summary>
public class SimilarMoveInputDto
{
    /// <summary>Ausgangsfeld, z. B. <c>"c3"</c>.</summary>
    public string? From { get; set; }
    /// <summary>Zielfeld, z. B. <c>"d5"</c>. Ohne <see cref="From"/> (und ohne <see cref="San"/>) ist
    /// der Zug NICHT auflösbar — die Figurenart bliebe unbekannt und der Zug könnte nie treffen;
    /// die Antwort meldet dann <c>move: null</c> und <c>onlyWithMove</c> bleibt wirkungslos.</summary>
    public string? To { get; set; }
    /// <summary>Umwandlungsfigur <c>"q"|"r"|"b"|"n"</c> (nur bei Bauernumwandlung). Fehlt sie, wird
    /// sie beim Vergleich nicht geprüft — eine Brett-Oberfläche, die sie nicht mitschickt, verliert
    /// so keinen Treffer.</summary>
    public string? Promotion { get; set; }
    /// <summary>Alternativ der Zug in SAN (<c>"Nd5"</c>, <c>"Nbd2"</c>, <c>"a8=Q"</c>). Wird gegen die
    /// Anfrage-FEN aufgelöst; ist die FEN illegal oder das SAN mehrdeutig, bleibt nur das Zielfeld +
    /// die Figurenart übrig (dann ist höchstens die schwächere Stufe <c>sameTarget</c> möglich).</summary>
    public string? San { get; set; }
}

/// <summary>Der tatsächlich verwendete Anfragezug (aufgelöst) — <c>null</c>, wenn keiner mitkam oder
/// er nicht auflösbar war (dann wirkt auch <c>onlyWithMove</c> nicht).</summary>
public class SimilarMoveEchoDto
{
    /// <summary>Ausgangsfeld; <c>null</c>, wenn nur das Zielfeld ermittelbar war.</summary>
    public string? From { get; set; }
    public string To { get; set; } = string.Empty;
    public string? Promotion { get; set; }
    /// <summary>Figurenart <c>"p"|"n"|"b"|"r"|"q"|"k"</c>, soweit bekannt.</summary>
    public string? Piece { get; set; }
}

/// <summary>Antwort: die besten Treffer (je Linie höchstens einer), absteigend nach Score.</summary>
public class SimilarPositionsResultDto
{
    public List<SimilarPositionMatchDto> Matches { get; set; } = new();
    /// <summary>Tatsächlich verwendete Voreinstellung (kanonisiert).</summary>
    public string Preset { get; set; } = "ausgewogen";
    /// <summary>Tatsächlich verwendeter Mindest-Score (nach Deckelung).</summary>
    public int MinScore { get; set; }
    /// <summary>Tatsächlich verwendetes Limit (nach Deckelung).</summary>
    public int Limit { get; set; }
    /// <summary>Wie viele Stellungen verglichen wurden (nach Materialschranke) — Diagnose/Anzeige.</summary>
    public int Compared { get; set; }
    /// <summary>Der aufgelöste Anfragezug (<c>null</c> = keiner mitgegeben oder nicht auflösbar).</summary>
    public SimilarMoveEchoDto? Move { get; set; }
    /// <summary>Ob nur Treffer mit passendem Zug geliefert wurden (wirkt nur mit <see cref="Move"/>).</summary>
    public bool OnlyWithMove { get; set; }
}

/// <summary>Ein Treffer: die ähnlichste Stellung EINER Repertoire-Linie.</summary>
public class SimilarPositionMatchDto
{
    public int RepertoireId { get; set; }
    public string RepertoireName { get; set; } = string.Empty;
    /// <summary>Kapitel = PGN-<c>[Black]</c>-Header (Chessable-Konvention). Kann leer sein.</summary>
    public string Chapter { get; set; } = string.Empty;
    /// <summary>Linienname = PGN-<c>[White]</c>-Header. Kann leer sein.</summary>
    public string LineName { get; set; } = string.Empty;
    /// <summary>0-basierter Index der Linie innerhalb des kombinierten Repertoire-PGN
    /// (gleiche Zählung wie bei der Stellungssuche → <c>parsePgnText</c>).</summary>
    public int GameIndex { get; set; }
    /// <summary>Halbzüge bis zur Stellung auf der Hauptlinie; <c>-1</c>, wenn sie nur in einer
    /// Variante vorkommt (gleiche Konvention wie <c>position-lookup</c>).</summary>
    public int Ply { get; set; }
    /// <summary>FEN der gefundenen Stellung (ungespiegelt, so wie sie im Repertoire steht).</summary>
    public string Fen { get; set; } = string.Empty;
    /// <summary>Endwert 0…100 = Stellungswert plus Zug-Bonus (siehe <see cref="MoveMatch"/>).
    /// Ohne Zug-Treffer identisch mit <see cref="PositionScore"/>.</summary>
    public int Score { get; set; }
    /// <summary>Reiner Stellungswert 0…100 OHNE Zug-Bonus — bleibt sichtbar, damit man sieht, wie
    /// viel vom Endwert aus der Stellung und wie viel aus dem Zug kommt.</summary>
    public int PositionScore { get; set; }
    /// <summary>Trefferstufe des mitgegebenen Zugs: <c>"exact"</c> (dort steht genau dieser Zug:
    /// gleiches from→to samt Umwandlungsfigur, unabhängig von der SAN-Schreibweise),
    /// <c>"sameTarget"</c> (gleiche Figurenart aufs gleiche Zielfeld, aber von woanders) oder
    /// <c>null</c> (kein Treffer bzw. kein Zug angefragt).</summary>
    public string? MoveMatch { get; set; }
    /// <summary>Der an dieser Stelle im Repertoire gespielte Zug in SAN, so wie er im PGN steht
    /// (bei Treffer der treffende, sonst der Hauptzug). Nur gefüllt, wenn die Anfrage einen Zug
    /// enthielt — sonst werden die Fortsetzungen gar nicht erst aufgelöst.</summary>
    public string? MoveSan { get; set; }
    /// <summary>Ausgangsfeld des gemeldeten Zugs (<c>"c3"</c>), in der Orientierung der TREFFER-FEN
    /// — bei <see cref="Mirrored"/> also farbvertauscht zur Anfrage.</summary>
    public string? MoveFrom { get; set; }
    /// <summary>Zielfeld des gemeldeten Zugs (<c>"d5"</c>).</summary>
    public string? MoveTo { get; set; }
    /// <summary>Umwandlungsfigur des gemeldeten Zugs, falls es eine Umwandlung ist.</summary>
    public string? MovePromotion { get; set; }
    /// <summary><c>true</c>, wenn der Wert aus dem farbvertauschten Vergleich stammt.</summary>
    public bool Mirrored { get; set; }
    /// <summary>Die vier Einzelwerte — ohne sie kann der Nutzer einem Treffer weder trauen noch
    /// sagen, wo die Metrik danebenliegt.</summary>
    public SimilarityBreakdownDto Breakdown { get; set; } = new();
}

/// <summary>Die vier Komponenten der Metrik, je 0…100 (100 = identisch).</summary>
public class SimilarityBreakdownDto
{
    /// <summary>Bauerngerüst (Gewicht je nach Voreinstellung 30–75 %).</summary>
    public int Pawns { get; set; }
    /// <summary>Materialverteilung.</summary>
    public int Material { get; set; }
    /// <summary>Figurenplatzierung (Abstand zur nächsten gleichartigen Figur) — <c>null</c>, wenn auf
    /// keinem der beiden Bretter eine Nicht-Bauern-Figur steht (reines Bauernendspiel). Die
    /// Komponente fließt dann NICHT ein; ihr Gewicht liegt auf den übrigen.</summary>
    public int? Pieces { get; set; }
    /// <summary>Königsstellung (Rochadeseite + Abstand) — <c>null</c>, wenn mindestens eine der
    /// beiden Stellungen gar keinen König hat (illegale Chessable-Diagramm-FEN). Die Komponente
    /// fließt dann NICHT ein; ihr Gewicht liegt auf den anderen dreien.</summary>
    public int? King { get; set; }
}
