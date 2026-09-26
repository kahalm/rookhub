using System.ComponentModel.DataAnnotations;

namespace RookHub.Api.DTOs;

/// <summary>Eingabe für „Partie speichern" (<c>POST /api/extension/games</c>).
/// Die Extension schickt die SAN-Zugliste der aktuellen Partie plus Best-Effort-Metadaten;
/// der Server baut daraus das PGN. Zeitstempel/ShareToken werden serverseitig gesetzt.</summary>
public class SaveGameInputDto
{
    /// <summary>Herkunft: <c>chess.com</c> oder <c>lichess</c>.</summary>
    [Required]
    [MaxLength(20)]
    public string Source { get; set; } = string.Empty;

    /// <summary>SAN-Zugliste der Hauptlinie (z. B. <c>["e4","e5","Nf3"]</c>).</summary>
    [Required]
    public List<string> Moves { get; set; } = new();

    [MaxLength(120)]
    public string? ExternalId { get; set; }

    /// <summary>Nach dem Speichern gleich analysieren (Uebersicht auf chess.com/lichess, 0.524.0). Fehlt eine
    /// Engine oder laeuft schon eine Analyse, bleibt die Partie trotzdem gespeichert.</summary>
    public bool Analyze { get; set; }

    [MaxLength(120)]
    public string? White { get; set; }

    [MaxLength(120)]
    public string? Black { get; set; }

    [MaxLength(12)]
    public string? Result { get; set; }

    [MaxLength(1000)]
    public string? SourceUrl { get; set; }

    public DateTime? PlayedAt { get; set; }

    /// <summary>Elo/Rating des Weißspielers auf der Plattform (Best-Effort von der Extension gelesen).</summary>
    public int? WhiteElo { get; set; }

    /// <summary>Elo/Rating des Schwarzspielers auf der Plattform.</summary>
    public int? BlackElo { get; set; }

    /// <summary>Bedenkzeit in PGN-Schreibweise (<c>180+2</c>, <c>600</c>, <c>1/86400</c>), wie die
    /// Plattform sie nennt — die Liste zeigt daraus „3 + 2". Unbekannt → weglassen.</summary>
    [MaxLength(32)]
    public string? TimeControl { get; set; }
}

/// <summary>Listeneintrag einer gespeicherten Partie (ohne PGN, für die Übersicht).</summary>
public class SavedGameDto
{
    public int Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Result { get; set; }
    public DateTime? PlayedAt { get; set; }
    public string? SourceUrl { get; set; }
    public string ShareToken { get; set; } = string.Empty;
    public int MoveCount { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Wertung der Seiten; <c>null</c> = keine bekannt (Altbestand wird portionsweise
    /// aus dem PGN nachgetragen, siehe <see cref="Models.SavedGame.WhiteElo"/>).</summary>
    public int? WhiteElo { get; set; }

    /// <inheritdoc cref="WhiteElo"/>
    public int? BlackElo { get; set; }

    /// <summary>Bedenkzeit in PGN-Schreibweise (<c>180+2</c>); <c>null</c> = unbekannt.</summary>
    public string? TimeControl { get; set; }

    /// <summary>Die Formular-Einlesung, aus der die Partie stammt (0.529.0) — <c>null</c> = kein Foto. Das ⋮-Menü
    /// bietet damit „Foto anzeigen/herunterladen", die Korrekturseite die Formular-Einträge.</summary>
    public int? ScanId { get; set; }

    /// <summary>Stand der VERKNUEPFTEN Analyse (<see cref="Models.SavedGame.GameAnalysisId"/>); <c>null</c> =
    /// keine verknuepft oder die Analyse gibt es nicht mehr. Die Liste zeigt damit statt des Analysieren-Knopfs
    /// den Fortschritt und, wenn fertig, die Genauigkeit beider Seiten.</summary>
    public SavedGameAnalysisDto? Analysis { get; set; }

    /// <summary>Stand des Fehler-Trainings zu dieser Partie; <c>null</c> = noch nie trainiert.</summary>
    public GameMistakeProgressDto? Mistakes { get; set; }
}

/// <summary>Fortschritt im Fehler-Training einer Partie („4 von 7 · 3 offen").</summary>
public class GameMistakeProgressDto
{
    /// <summary>Aufgaben, die die Analyse hergibt (Seite des Nutzers).</summary>
    public int Total { get; set; }
    /// <summary>Davon selbst gefunden.</summary>
    public int Solved { get; set; }
    /// <summary>Noch offen (<c>Total - Solved</c>, nie negativ).</summary>
    public int Open { get; set; }
    /// <summary>Die gefundenen Halbzuege — der Trainer markiert damit, was schon saß.</summary>
    public List<int> SolvedPlies { get; set; } = new();
    public DateTime LastTrainedAt { get; set; }
}

/// <summary>Meldung des Trainers: Aufgabenzahl und die in diesem Durchlauf selbst gefundenen Halbzuege.
/// Additiv — der Server vereinigt sie mit dem bisherigen Stand.</summary>
public class MistakeProgressInputDto
{
    /// <summary>Wie viele Aufgaben die Partie hergibt (Seite des Nutzers).</summary>
    public int Total { get; set; }
    /// <summary>Selbst gefundene Halbzuege dieses Durchlaufs.</summary>
    public List<int> Solved { get; set; } = new();
}

/// <summary>Kopf der verknuepften Analyse fuer die Partienliste — ohne Stellungen.</summary>
public class SavedGameAnalysisDto
{
    /// <summary><c>pending</c> · <c>running</c> · <c>done</c> · <c>failed</c> (wie <see cref="GameEvalsDto.Status"/>).</summary>
    public string Status { get; set; } = "pending";
    /// <summary>Gerechnete Stellungen (aufgegebene eingeschlossen).</summary>
    public int Analyzed { get; set; }
    /// <summary>Halbzuege der Partie.</summary>
    public int Total { get; set; }
    /// <summary>Genauigkeit in Prozent, nur bei <c>done</c> (<see cref="Services.GameAccuracy"/>); <c>null</c>,
    /// wenn die Seite keinen bewertbaren Zug hat.</summary>
    public double? AccuracyWhite { get; set; }
    public double? AccuracyBlack { get; set; }
}

/// <summary>Detail einer gespeicherten Partie inkl. PGN (Besitzer; zum Nachspielen/Analysieren).</summary>
public class SavedGameDetailDto : SavedGameDto
{
    public string Pgn { get; set; } = string.Empty;

    /// <summary>„white"/„black", wenn der Besitzer einer Seite zuordenbar ist (Plattform-Username im
    /// Profil) — die Partie-Seite dreht das Brett dann aus seiner Sicht. Dieselbe Regel wie
    /// <see cref="SharedGameDto.OwnerSide"/>.</summary>
    public string? OwnerSide { get; set; }
}

/// <summary>Öffentliche Sicht auf eine geteilte Partie (<c>GET /api/games/shared/{token}</c>).
/// Enthält bewusst keine User-/Besitzer-Daten.</summary>
public class SharedGameDto
{
    public string Source { get; set; } = string.Empty;
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Result { get; set; }
    public DateTime? PlayedAt { get; set; }
    public string? SourceUrl { get; set; }
    public string Pgn { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }

    /// <summary>Elo/Rating des Weißspielers (aus dem PGN-Header <c>WhiteElo</c> gelesen).</summary>
    public int? WhiteElo { get; set; }

    /// <summary>Elo/Rating des Schwarzspielers (aus dem PGN-Header <c>BlackElo</c> gelesen).</summary>
    public int? BlackElo { get; set; }

    /// <summary>"white"/"black", wenn der TEILENDE Besitzer über seinen hinterlegten Plattform-
    /// Username (lichess bzw. chess.com, je nach Quelle) einer Seite zuordenbar ist; sonst null.
    /// Steuert die Brett-Orientierung der öffentlichen /g/-Seite + des OG-Vorschaubilds
    /// (Partie aus der Sicht des Teilenden). Kein zusätzlicher Identitäts-Leak — die
    /// Spielernamen stehen ohnehin im DTO.</summary>
    public string? OwnerSide { get; set; }

    /// <summary>Die Id der Partie — NUR wenn der angemeldete Aufrufer ihr Besitzer ist (seit 0.526.3). Oeffnet er den
    /// eigenen Teilen-Link, wechselt die Seite damit auf <c>/games/{id}</c> und sieht dasselbe wie ueber die
    /// Partienliste (gewuenscht 2026-09-24). Fremden und anonymen Aufrufern bleibt die Id verborgen.</summary>
    public int? OwnGameId { get; set; }

    /// <summary>„Kurz erzählt" (0.541.0): die Partie in zwei, drei Sätzen, vom Sprachmodell aus der Analyse des Besitzers
    /// geschrieben (<see cref="Services.GameRecapService"/>) — dieselbe Zeile steht in der Link-Vorschau. <c>null</c>, solange
    /// es keine gibt.</summary>
    public string? Recap { get; set; }
}

/// <summary>Rumpf von „Partie analysieren" (0.540.0, optional): die Sprache der Seite — darin entstehen nach der
/// Analyse die Erklärungen und Roasts (<see cref="Models.SavedGame.ReviewLanguage"/>).</summary>
public class SavedGameAnalyzeRequest
{
    [MaxLength(16)] public string? Lang { get; set; }
}

/// <summary>Antwort auf „Partie analysieren" (<c>POST /api/games/{id}/analyze</c> bzw.
/// <c>…/shared/{token}/analyze</c>): entweder die Analyse — neu angelegt oder wiederverwendet — oder
/// der Grund der Absage (<see cref="GuessUploadReason"/>, dieselben Gruende wie beim Einwurf auf der
/// Punktepartie-Seite).</summary>
public class GameAnalyzeResultDto
{
    /// <summary>Kopfdaten der Analyse, ohne Stellungen; <c>null</c> bei einer Absage.</summary>
    public GameAnalysisDto? Analysis { get; set; }
    public string? Reason { get; set; }
    /// <summary>Es wurde NICHTS neu eingereiht — die Partie war schon (oder wird gerade) gerechnet.
    /// Die Seite sagt das, statt „Analyse gestartet" zu melden.</summary>
    public bool Reused { get; set; }
}

/// <summary>
/// Die Bewertungen einer gespeicherten Partie fuer Kurve, Genauigkeit und Zug-Klassen
/// (<c>GET /api/games/{id}/evals</c>, <c>GET /api/games/shared/{token}/evals</c>).
///
/// <para><b>Alle Bewertungen aus WEISS-Sicht</b> — anders als die Kandidatenlisten der Analyse, die aus
/// Sicht der Seite am Zug stehen (so braucht sie die Punktepartie). Die Kurve zeichnet EINE Linie ueber
/// die ganze Partie; mit dem Vorzeichen der Seite am Zug sprang sie nach jedem Halbzug ueber die
/// Mittellinie. Umgerechnet wird einmal hier und nicht in jedem Leser.</para>
/// </summary>
public class GameEvalsDto
{
    /// <summary><c>none</c> (keine Analyse) · <c>pending</c> · <c>running</c> · <c>done</c> · <c>failed</c>.</summary>
    public string Status { get; set; } = "none";
    /// <summary>Gerechnete Stellungen (aufgegebene eingeschlossen) — der Fortschritt.</summary>
    public int Analyzed { get; set; }
    /// <summary>Halbzuege der Partie (= Zeilen der Analyse).</summary>
    public int Total { get; set; }
    public int TargetDepth { get; set; }
    public int? AnalysisId { get; set; }
    /// <summary>Nur GERECHNETE Stellungen, nach Halbzug sortiert. Offene und aufgegebene fehlen — der
    /// Client laesst dort eine Luecke, statt zu interpolieren.</summary>
    public List<GameEvalPlyDto> Plies { get; set; } = new();
    /// <summary>Bewertung NACH dem letzten Zug (fuer sie gibt es keine eigene Zeile): der gespielte
    /// Kandidat der letzten Zeile; <c>null</c>, solange die nicht gerechnet ist oder der Partiezug
    /// nicht unter den Kandidaten steht.</summary>
    public GameEvalScoreDto? Final { get; set; }
    /// <summary>Hochgerechnete Restdauer in Minuten, solange die Analyse laeuft — aus dem Tempo der
    /// juengsten gerechneten Stellungen DIESER Partie (<see cref="Services.GameEvals.EtaMinutes"/>).
    /// <c>null</c>, solange es noch kein Tempo gibt (weniger als zwei Ergebnisse) oder nichts mehr laeuft.</summary>
    public int? EtaMinutes { get; set; }
    /// <summary>„Buchzüge" (seit 0.522.0): die Halbzüge (0-basiert), die in einem für die Erweiterung markierten
    /// Repertoire des AUFRUFERS stehen, vor der Abweichung — siehe <see cref="Services.RepertoireAnalyzeService.BookPliesAsync"/>.
    /// Leer ohne Anmeldung.</summary>
    public List<int> BookPlies { get; set; } = new();
    /// <summary>Der zweite Durchgang (Vertiefung, seit 0.523.0) laeuft noch: die Analyse ist <c>done</c> und nutzbar,
    /// wird aber Stellung fuer Stellung genauer — der Client fragt dann gemaechlich nach.</summary>
    public bool Refining { get; set; }
    /// <summary>So viele Stellungen sind schon vertieft (0 ohne zweiten Durchgang).</summary>
    public int Refined { get; set; }
}

/// <summary>Eine gerechnete Stellung — die VOR dem Halbzug <see cref="Ply"/> —, Weiß-Sicht.</summary>
public class GameEvalPlyDto
{
    /// <summary>0-basiert wie <c>GameAnalysisPosition.Ply</c>: 0 = vor dem ersten Zug.</summary>
    public int Ply { get; set; }
    /// <summary>Bewertung der Stellung (= bester Kandidat). Genau eines von Cp/Mate.</summary>
    public int? Cp { get; set; }
    public int? Mate { get; set; }
    public int Depth { get; set; }
    public string? BestUci { get; set; }
    /// <summary>Der in der Partie gespielte Zug (Standard-UCI) — damit der Client „bester Zug"
    /// erkennt, ohne die Zugliste selbst in UCI umzurechnen. Kein Geheimnis: er steht im PGN.</summary>
    public string PlayedUci { get; set; } = string.Empty;
    /// <summary>Bewertung des gespielten Zuges aus derselben Suche; <c>null</c>, wenn er nicht unter
    /// den Kandidaten steht.</summary>
    public int? PlayedCp { get; set; }
    public int? PlayedMate { get; set; }
    /// <summary>Zweitbester Kandidat — heute ungenutzt, Grundlage fuer „Great"/„Brilliant" in einem
    /// spaeteren Schritt (nur ein Zug haelt die Stellung).</summary>
    public int? SecondCp { get; set; }
    public int? SecondMate { get; set; }
    /// <summary>ALLE Kandidaten dieser Suche (hoechstens fuenf, bester zuerst), Weiß-Sicht. Fuer
    /// „Eigene Fehler nachspielen": dort zaehlt nicht nur der Bestzug, sondern jeder gleichwertige —
    /// und ob einer gleichwertig ist, weiss nur, wer auch die anderen Bewertungen kennt.</summary>
    public List<GameEvalCandidateDto> Candidates { get; set; } = new();
}

/// <summary>Ein Kandidat der Engine: Zug (Standard-UCI) + Bewertung in Weiß-Sicht.</summary>
public class GameEvalCandidateDto
{
    public string Uci { get; set; } = string.Empty;
    public int? Cp { get; set; }
    public int? Mate { get; set; }
    /// <summary>Die Variante der Engine ab diesem Zug (UCI roh vom Broker, Rochade ggf. als
    /// König-schlägt-Turm — der Client spielt sie nach und schreibt sie um); <c>null</c> bei Analysen von
    /// vor 0.521.0.</summary>
    public List<string>? Pv { get; set; }
}

/// <summary>Eine einzelne Bewertung (Weiß-Sicht), genau eines von <see cref="Cp"/>/<see cref="Mate"/>.</summary>
public class GameEvalScoreDto
{
    public int? Cp { get; set; }
    public int? Mate { get; set; }
}

/// <summary>Anfrage der Uebersicht: welche dieser Partien liegen schon bei RookHub?</summary>
public class KnownGamesInputDto
{
    /// <summary><c>chess.com</c> oder <c>lichess</c>.</summary>
    [Required, MaxLength(20)]
    public string Source { get; set; } = string.Empty;
    /// <summary>Partie-IDs der Plattform, wie sie in den Links der Uebersicht stehen.</summary>
    public List<string> ExternalIds { get; set; } = new();
}

/// <summary>Eine bereits gespeicherte Partie — die Uebersicht zeigt daraufhin ein Haekchen statt des Knopfs.</summary>
public class KnownGameDto
{
    public string ExternalId { get; set; } = string.Empty;
    /// <summary>RookHub-Id (fuer den Link auf <c>/games/{id}</c>).</summary>
    public int Id { get; set; }
    /// <summary>Stand der Analyse; <c>null</c> = keine.</summary>
    public SavedGameAnalysisDto? Analysis { get; set; }
}

/// <summary>„Warum war das ein Fehler?" (0.534.0) — die Erklärungen einer Partie in einer Sprache.</summary>
public class GameExplanationsDto
{
    /// <summary>Ein Modell auf eigener Hardware ist eingerichtet — ohne gibt es die Funktion nicht.</summary>
    public bool Available { get; set; }
    /// <summary>Der Aufrufer darf erzeugen lassen (Besitzer, Analyse fertig, nichts läuft).</summary>
    public bool CanGenerate { get; set; }
    /// <summary>Gerade entstehen Erklärungen — der Client fragt nach.</summary>
    public bool Running { get; set; }
    public string Language { get; set; } = "en";
    public List<GameExplanationDto> Items { get; set; } = new();
}

public class GameExplanationDto
{
    /// <summary>Halbzug (0 = erster Zug), wie in <see cref="GameEvalPlyDto.Ply"/>.</summary>
    public int Ply { get; set; }
    public string Class { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    /// <summary>Der Meisterkommentar zu dieser Stellung, mit dem die Erklärung geschrieben wurde (0.542.0) — oder <c>null</c>.</summary>
    public GameExplanationMasterDto? Master { get; set; }
}

/// <summary>Quelle und Wortlaut eines Meisterkommentars (Kopfdaten der Bibliothekspartie, der Kommentar im Original).</summary>
public class GameExplanationMasterDto
{
    public int LibraryGameId { get; set; }
    public string? White { get; set; }
    public string? Black { get; set; }
    public string? Event { get; set; }
    public int? Year { get; set; }
    public string? Annotator { get; set; }
    public string Text { get; set; } = string.Empty;
}

/// <summary>„Roast my game" (0.535.0) — die gewürfelten Kommentare einer eigenen Partie in einer Sprache.</summary>
public class GameRoastsDto
{
    /// <summary>Ein Modell auf eigener Hardware ist eingerichtet.</summary>
    public bool Available { get; set; }
    /// <summary>Die Partie hat eine fertige Analyse — ohne sie gibt es nichts zu roasten.</summary>
    public bool HasAnalysis { get; set; }
    public List<GameRoastDto> Items { get; set; } = new();
}

public class GameRoastDto
{
    public string Style { get; set; } = string.Empty;
    public string Language { get; set; } = "en";
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>„Kurz erzählt" (0.541.0) — die Nacherzählung einer eigenen Partie (<c>GET /api/games/{id}/recap</c>); dieselbe
/// steht in der Link-Vorschau des Teilen-Links und in <see cref="SharedGameDto.Recap"/>.</summary>
public class GameRecapDto
{
    /// <summary>Ein Modell auf eigener Hardware ist eingerichtet.</summary>
    public bool Available { get; set; }
    /// <summary>Die Partie hat eine fertige Analyse — ohne sie gibt es nichts zu erzählen.</summary>
    public bool HasAnalysis { get; set; }
    public string? Text { get; set; }
    public string? Language { get; set; }
    public DateTime? CreatedAt { get; set; }
    /// <summary>Der Text fehlt noch, entsteht aber gerade (von diesem Aufruf angestoßen) — die Seite fragt später nach.</summary>
    public bool Pending { get; set; }
}
