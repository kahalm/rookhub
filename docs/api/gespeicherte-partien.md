# Gespeicherte Partien (auth + öffentlicher Teilen-Link)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Gespeicherte Partien (auth + öffentlicher Teilen-Link)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Bereich „Partien" (`/games`): zeigt die über die RepCheck-Extension von chess.com/lichess gespeicherten Partien (dazu eingelesene Formulare und hochgeladene PGNs). Das ⋮-Menü der Partieseite bietet seit 0.553.0 „PGN kopieren" und „PGN herunterladen" — für JEDEN Betrachter, auch auf dem Teilen-Link (das PGN liegt dort ohnehin im Browser). Seit 0.592.0 dazu „Im Analysebrett öffnen" (ganzes PGN per Router-State, „Zurück" führt auf die Partie); das Analysebrett nimmt eine Partie außerdem über `?pgn=` an (für Sprünge von LeagueHub, der Router-State kommt über Seitengrenzen nicht an) und lässt das PGN nach einem Sprung im PGN-Feld stehen. Nachspielen als eigene SEITE `/games/{id}` (seit 0.513.0 dieselbe Komponente wie der Teilen-Link `/g/{token}`, `data.mode = 'own'`; vorher ein PGN-Viewer-Dialog — der Nutzer wollte eine Seite), „In Analyse öffnen" (PGN via Router-State an `/analysis`), Löschen, und Teilen über einen eindeutigen öffentlichen Link `/g/{shareToken}` (kein Login). Logik in `SavedGameService`; Menü-Key `games` (Default `Registered`).

| Methode | Endpoint | Auth | Zweck |
|---------|----------|------|-------|
| GET | `/api/games?take=200` | Auth | Eigene gespeicherte Partien (neueste zuerst, ohne PGN). Seit 0.515.0 je Partie `analysis` = Stand der VERKNÜPFTEN Analyse (`status`, `analyzed`/`total`, `accuracyWhite`/`accuracyBlack` bei `done`) oder `null` — EINE gruppierte Zählung über alle verknüpften Ids, kein Abruf je Partie; fertige Analysen ohne abgelegte Genauigkeit (vor 0.515.0) werden dabei nachgerechnet (`AccuracyBackfillPerCall` = 10 je Aufruf) |
| GET | `/api/games/shared/{token}` | AllowAnonymous | Öffentliche Sicht einer geteilten Partie inkl. PGN (ohne Besitzer-Daten). Literal-Route VOR `{id}`. Seit 0.526.3 `ownGameId` = die Partie-Id, NUR wenn der angemeldete Aufrufer der Besitzer ist — die Seite leitet dann auf `/games/{id}` um (eigener Link = dieselbe Ansicht wie aus „Partien") |
| GET | `/api/games/shared/{token}/evals` | AllowAnonymous | Bewertungen der geteilten Partie für Kurve/Genauigkeit/Zug-Klassen (`GameEvalsDto`). Anonym NUR die vom Besitzer verknüpfte Analyse; angemeldet ersatzweise die EIGENE mit gleichem PGN. Globaler IP-Limiter wie `GET shared/{token}` |
| POST | `/api/games/shared/{token}/analyze` | Auth | „Partie analysieren" auf der geteilten Partie — jeder Angemeldete. Antwort `GameAnalyzeResultDto { analysis, reason?, reused }`, Absage 400 `{ reason, message }` wie `POST /api/game-analyses/guess` |
| GET | `/api/games/{id}` | Auth | Detail einer eigenen Partie inkl. PGN (Nachspielen/Analysieren) |
| GET | `/api/games/{id}/evals?book=0` | Auth | Bewertungen einer eigenen Partie (Nachspiel-Dialog). **`?book=0` (0.664.0) lässt die Buchzüge weg** — sie brauchen das Stellungs-Set der Repertoires, das nach 5 min Leerlauf neu gebaut wird (gemessen 2–4 s bei 13 MB PGN, Prod 05.10., sonst ~5 ms) und hielt die Antwort auf. Die Seite fragt erst `?book=0`, dann einmal ohne Parameter und trägt `bookPlies` nach (gilt ebenso für `…/shared/{token}/evals`) |
| POST | `/api/games/{id}/analyze` | Auth | „Partie analysieren" an einer eigenen Partie (Liste + Nachspiel-Dialog). Rumpf optional `{ lang }` (0.540.0, auch beim Teilen-Link, gemerkt nur beim Besitzer als `SavedGame.ReviewLanguage`) — darin entstehen danach Erklärungen und Roasts |
| GET | `/api/games/{id}/mistakes` | Auth | Stand des Fehler-Trainings dieser Partie (`total`/`solved`/`open`/`solvedPlies`); 404, solange nie trainiert |
| POST | `/api/games/{id}/mistakes` | Auth | Fortschritt melden `{ total, solved[] }` — die in DIESEM Durchlauf SELBST gefundenen Halbzüge. **Additiv und idempotent**: der Server vereinigt sie mit dem Stand, ein zweiter Durchlauf nimmt nichts weg, dieselbe Meldung zweimal ändert nichts. `total` wird auf ≥ gefundene und ≤ 600 geklemmt, Halbzüge außerhalb der Partie fallen weg (`GameMistakeProgressService`). `/api/games` trägt den Stand je Partie als `mistakes` mit; die Übersicht zeigt seit 0.526.3 nur „Fehler nachgespielt" (die Zahlen im Tooltip) |
| DELETE | `/api/games/{id}` | Auth | Eigene Partie löschen |
| — | Klassifizierer (0.661.0) | — | `GET /api/games` und Detail tragen je Partie `classifier1`/`classifier2` (GELTENDER Wert) plus `classifier1Set`/`classifier2Set` (nur vom Nutzer gesetzt). Online-Partien (chess.com/lichess): Seite + Modus, abgeleitet in `GameClassifier` (Modus = Grundzeit + 40 × Inkrement: < 180 s Bullet, < 480 Blitz, < 1500 Rapid, sonst Classical, `1/86400` Daily; ohne Bedenkzeit leer). Ligapartien: Liga + Jahrgang, vom Nutzer gesetzt über `PUT /api/games/{id}` (`classifier1`/`classifier2`: `null` = unverändert, leer = zurücknehmen, ≤ 80 Zeichen; Spalten `SavedGames.Classifier1/2`). `/games` zeigt sie als Chips und filtert je Klassifizierer (clientseitig, `classifier.util.ts`); der Editor schlägt Werte aus den eigenen Partien und den Jahrgang aus dem Datum vor (ab August 2026/27) |
| — | Tags (0.662.0) | — | `tags[]` je Partie in Liste und Detail (nur Besitzer); `PUT /api/games/{id}` mit `tags` (`null` = unverändert, sonst die ganze neue Liste). `GameTags`: höchstens 10 je Partie, je ≤ 30 Zeichen, ohne Doppelte (Groß-/Kleinschreibung egal), Spalte `SavedGames.Tags` zeilengetrennt. `/games`: Chips + Tag-Filter, der Editor schlägt vergebene Tags vor (`tags.util.ts`) |
| — | Klassifizierer der Vereinspartien (0.666.0) | — | `LeagueClubGames.Classifier1/2` (Liga/Jahrgang) — NIE in einer Vereins-DTO ausgeben (Liga + Saison + Gegner machten die „Schwaz“-Seite im Spielplan wieder auffindbar). Sie gehen nur in die Kopie nach „Meine Partien": `ImportPgnAsync` setzt `SavedGame.Classifier1/2` aus der verbundenen Vereinspartie, `LinkClubCopiesAsync` trägt nach (`??=`, überschreibt nichts). Befüllt per Skript `scripts/club-classify-backfill.py` (Gegner-FIDE gegen Spielpläne, nur eindeutige Treffer) |
| POST | `/api/games/import` | Auth | **PGN hochladen** (0.553.0, Knopf „PGN hochladen" auf `/games`: Datei wählen ODER einfügen) `{ pgn, ownerSide? }` → `{ imported, duplicates, truncated, ids[], failed[{ index, white, black, reason }] }`. `ownerSide` (`white`/`black`, sonst ignoriert; 0.644.0, „Partie analysieren" nach dem Maia-Sparring) wird die festgelegte Seite jeder NEU angelegten Partie — eine Dublette bleibt, wie sie ist. Ein alleinstehendes Punkte-Token (chess.js schreibt „4. ... Bc5") ist ein Zugnummern-Rest, kein Zug (`PgnParser.IsDotsOnly`, 0.644.0 — vorher `illegal`). Jede Partie des Textes wird eine eigene Partie mit Quelle `pgn`: HAUPTVARIANTE samt Kommentaren und allen Kopfdaten (Elo, Bedenkzeit, FEN …), Varianten fallen weg (Analyse/Kurve/Fehler-Training arbeiten auf einer Zugfolge). Eine Partie, deren Hauptvariante nicht bis zum Ende legal ist, wird NICHT gekürzt angelegt (`reason` `illegal`/`noMoves`/`tooLong`/`badFen`). Zweimal hochgeladen = einmal da: `ExternalId` = Hash über Seven-Tag-Kopfdaten, FEN und Züge (`SavedGameService.ImportKey`, eindeutig je Nutzer). Deckel `MaxImportGames` 200 (`truncated`), `MaxImportChars` 5 Mio. (400 `tooLarge`; leer → 400 `empty`). Der Dialog schließt bei vollem Erfolg (genau eine neue Partie → gleich geöffnet) und bleibt sonst offen mit der Liste der nicht übernommenen |
| GET | `/api/games/{id}/explanations?lang=` | Auth | „Warum war das ein Fehler?" (0.534.0): gespeicherte Erklärungen der verknüpften Analyse in der Sprache (`{ available, canGenerate, running, language, items[{ ply, class, text, master? }] }`; `master` = der mitgegebene Meisterkommentar, 0.542.0: `{ libraryGameId, white, black, event, year, annotator, text }`) |
| POST | `/api/games/{id}/explanations?lang=` | Auth | Erzeugen anstoßen (Hintergrund, nur Besitzer). 503 `notConfigured` ohne Modell auf eigener Hardware, 404 fremde Partie, 409 ohne fertige verknüpfte Analyse, 429 `{ reason: "tooManyRunning", maxRunning: 2 }` bei zwei laufenden Aufträgen des Kontos (dasselbe Paar Analyse/Sprache noch einmal = „läuft schon", kein 429; Codereview A6-005) |
| GET | `/api/games/shared/{token}/explanations?lang=` | AllowAnonymous | Dasselbe lesend für den Teilen-Link (`canGenerate` immer false) |
| GET | `/api/games/{id}/roasts?lang=` | Auth | „Roast my game" (0.535.0): die gewürfelten Kommentare der eigenen Partie `{ available, hasAnalysis, items[{ id, shared, style, language, text, createdAt }] }`; 404 fremde Partie |
| POST | `/api/games/{id}/roasts?style=&lang=` | Auth | Würfeln (ersetzt den vorigen Text desselben Stils; `style` ∈ friendly/cheeky/russian). Absagen mit `reason`: 503 notConfigured, 404 notFound, 409 noAnalysis, 400 invalidStyle, 429 dailyLimit (`MaxPerDay` 60), 502 failed |
| POST / DELETE | `/api/games/{id}/roasts/share?style=&lang=` | Auth | „Partie mit diesem Roast teilen" (0.742.0): gibt den Roast frei (`GameRoast.SharedAt`) → `{ roastId }` bzw. nimmt die Freigabe zurück (204); 404 fremde Partie / unbekannter Roast |
| GET | `/api/games/shared/{token}/roasts/{roastId}` | Anonym | Der freigegebene Roast hinter `/g/{token}?roast={roastId}` (Figurenbuchstaben der Sprache); 404, wenn er nicht zu DIESER Partie gehört oder nicht (mehr) freigegeben ist |
| GET | `/api/games/{id}/similar` | Auth | „Ähnliche Meisterpartien" (0.544.0, `SimilarGamesService`): `{ opening?, sharedPlies, sharedLine?, items[{ game (wie die Bestandssuche, inPool/requested/gameAnalysisId), sharedPlies, lastSharedMove?, masterMove?, gameMove? }] }` — kommentierte Partien des Rohbestands mit der LÄNGSTEN gemeinsamen Zugfolge (binär gesucht über `OpeningLine`, dort die besten nach Note, aufgefüllt aus flacheren Stufen bis 5, mindestens 4 gemeinsame Halbzüge); 404 fremde Partie. Bewusst über die Zugfolge, nicht über den Kommentar-Index: eine eigene Partie hat keine Kommentare, und der Index hängt an Zugnummern (siehe `MasterComments`) |
| GET | `/api/games/shared/{token}/similar` | AllowAnonymous | Dasselbe für den Teilen-Link (Kopfdaten ohne Züge, derselbe Zuschnitt wie die anonyme Bestandssuche; anonym trägt nur `inPool`) |
| GET | `/api/library-games/{id}/view?lang=` | Auth | „Anschauen" einer Meisterpartie (0.567.0, `LibraryGameService.ViewAsync`) → `{ id, pgn, language, languages }`: PGN aus der HAUPTVARIANTE und den Kommentar-Sätzen in `lang` (`CommentSetService.ForLibraryAsync`: dieselbe Regel wie beim Nachspielen, fehlende Halbzüge aus der Quelle, Quell-Sätze entstehen beim ersten Bedarf), nicht das rohe Quell-PGN (zwei Sprachen hintereinander, ChessBase-Figurenschrift). Nur angemeldet wie das Anfordern; 404 unbekannt/aussortiert/nicht nachspielbar |
| GET | `/api/games/{id}/recap` | Auth | „Kurz erzählt" (0.541.0): die Nacherzählung der eigenen Partie `{ available, hasAnalysis, text?, language?, createdAt?, pending }`. Fehlt sie bei fertiger Analyse (Analyse von vor 0.541.0, gescheiterter Lauf), stößt schon dieser Abruf sie im Hintergrund an (`pending: true`, die Seite fragt alle 15 s nach, höchstens achtmal); 404 fremde Partie. Der Teilen-Link bekommt denselben Text als `recap` in `GET /api/games/shared/{token}` |

**Elo im Kopf** (0.737.0, Wunsch 2026-10-10): Partieseite und Teilen-Link zeigen „Name (Elo n)", der og:title des Teilen-Links
ebenso. Fehlt einer Kopie mit `LeagueClubGameId` die Wertung (PGN-Kopf), liefert `SavedGameService.WithLeagueEloAsync` sie beim
LESEN aus `LeaguePlayers` (FIDE der Seite, bei „Schwaz" die interne; Liga der zugeordneten Paarung zuerst, sonst jüngste
Meldeliste, `EloI ?? EloN`) — nichts wird gespeichert, ein späteres Aktualisieren der Meldeliste wirkt sofort.

**Deckel je Konto** (Codereview 2026-09-29, A6-007): höchstens `SavedGameService.MaxGamesPerUser` (5 000) Partien
und `MaxPgnCharsPerUser` (100 Mio. Zeichen PGN, Summe) je Konto, alle Quellen zusammen. `POST /api/games/import`
meldet jede neue Partie darüber in `failed` mit `reason: "quota"` (HTTP 200); `PUT /api/games/{id}` antwortet bei
wachsendem PGN über dem Zeichendeckel 400 `{ reason: "quota" }` (Kürzen geht immer); `POST /api/extension/games`
400 am Zähldeckel. Züge der Extension höchstens 16 Zeichen (`MaxSanLength`, sonst 400 „Invalid move.").

**„Warum war das ein Fehler?" (0.534.0, `GameMoveExplanationService`).** Zu jedem Fehler der verknüpften Analyse
(Ungenauigkeit/Fehler/grober Fehler/verpasste Chance, die schwersten `MaxPerGame` = 15) schreibt das Sprachmodell auf
EIGENER Hardware ein, zwei Sätze — NUR mit `IClaudeJsonClient.IsLocal` (Spark), über Claude liefe es auf Kosten. Regeln:
* **Fakten statt Rechnen:** `GameMistakes.Find` (Server-SPIEGEL von `classify`/`CLASS_LIMITS`/Miss-Regel in
  `game-review.util.ts`, literale Grenzwerte in `GameMoveExplanationTests` ↔ `game-review.util.spec.ts`) liefert je Fehler
  Stellung, gespielten Zug, Bestzug + Engine-Linie (SAN, Rochade König-schlägt-Turm aufgelöst), Widerlegung (Linie der
  NÄCHSTEN Stellung) und Bewertung vorher/nachher. Das Modell soll erklären, nicht rechnen.
* **Der GRUND, nicht die Bewertung** (0.572.0, `Services/Games/ExplanationFacts.cs`, gemeldet 2026-09-28 an Prod-Partie 34): mit
  Gewinnchance und SAN-Linien begründete das Modell jeden Fehler mit genau dem („ein Fehler, weil er seine Gewinnchancen
  senkt") und erfand Material („starker Bauernvorteil", wo eine Figur fiel). Jetzt bekommt es GEPRÜFTE Fakten: `Situation`
  (die Lage in Worten aus Sicht des LESERS, Stufen ab 2,5 / 1 / 0,4 Bauern: „from clearly worse to lost", „still winning,
  but gave away part of the advantage", „the opponent's move made the win even easier"), `FirstMoveAttacks` (was der erste
  Zug der Antwort bzw. der besseren Linie angreift, ohne Bauern/König) und `LineEvents` (Schach im ersten Zug, Matt,
  Umwandlung, Materialbilanz am Ende — nur, wenn die RICHTIGE Seite vorne liegt: in der Antwort-Linie der Gegner, in der
  besseren der Ziehende; einzelne Schlagfälle bewusst nicht, das Modell verwechselte damit die Seiten). **Die Bilanz der
  Antwort-Linie zählt ab der Stellung VOR dem Fehler, samt dem gespielten Zug** (`LineEvents(…, lead: PlayedSan)`, 0.585.1):
  ab der Stellung danach war ein Zurückschlagen ein Gewinn — Prod-Partie 35, 22.Txc4 dxc4 stand als „Black ends up ahead by
  5", das Modell schrieb „du gewinnst sofort einen Turm". Ohne solche Fakten
  sagt der Auftrag „positional, name the moves, do not invent reasons". Der Auftrag verbietet die Bewertung als Grund, Zahlen
  nur als „+5.8 → +4.4". **Schon verloren = keine Erklärung** (`AlreadyLost`: vorher UND nachher Stufe −3, Wunsch „von −4
  auf −6 muss das nicht kommentiert werden"). **Fassung** `GameMoveExplanation.Revision` (`CurrentRevision` = 2 seit 0.585.1): ältere
  Texte zeigt `GetAsync` nicht, `GenerateAsync` räumt sie weg — wer den Auftrag spürbar ändert, erhöht die Zahl.
* **Meisterkommentar zur SELBEN Stellung** (0.542.0, `Services/Games/MasterComments.cs`): vor dem Schreiben sucht der Dienst
  im Rohbestand kommentierte Partien mit GENAU derselben Zugfolge bis zur Stellung vor dem Fehler (`LibraryGame.OpeningLine`,
  Präfix-Suche, 40 beste nach Note) und gibt den treffendsten Kommentar dem Modell mit: (1) Meister spielte denselben Zug
  und der Kommentator urteilt dazu, (2) der Kommentar nennt den gespielten Zug (meist als Variante, auch in den Buchstaben
  seiner Sprache), (3) Meister spielte den Bestzug, (4) Kommentar zur Stellung davor (≥ 40 Zeichen). Aufgeräumt: Markup,
  ChessBase-Symbolzeichen (kommen als hebräische Buchstaben), Zugketten über drei Tokens → „…", unter 25 Buchstaben Prosa
  = kein Kommentar. Das Modell darf Ideen übernehmen, Züge NUR aus den Engine-Linien; nennt der Text einen Zug aus dem
  Kommentar, fragt der zweite Versuch OHNE Kommentar nach (die Quelle wird dann nicht vermerkt). Gespeichert
  `MasterLibraryGameId` + `MasterText`, die Seite zeigt die Quelle unter der Erklärung (Wortlaut zum Aufklappen).
  **Bewusst KEINE Vektorsuche**: am 2026-09-26 auf Prod gemessen fand der Kommentar-Index zu Erklärungstexten vor allem
  Kommentare mit denselben Zugnummern/Zügen aus ANDEREN Stellungen („23...Qh4 lässt die Grundreihe ungeschützt" →
  „23. Qc2: Under time pressure …") — dem Modell mitgegeben, vermischte es zwei Partien. Preis: Treffer fast nur in der
  Eröffnung (Amateurpartien verlassen den Meisterbestand meist zwischen Halbzug 6 und 16; gesucht wird bis
  `LibraryGameReader.OpeningPlies`, ab dem ersten Halbzug ohne Treffer gar nicht mehr).
* **Figurenbuchstaben beim LESEN** (0.541.1, gilt für Erklärungen, Roasts und „Kurz erzählt"): gespeichert wird in
  englischer SAN (so stehen die Züge in den Fakten, und nur so prüft `MentionsOnly`), `PieceLetters.Convert(text, "en", lang)`
  setzt beim Ausliefern die Buchstaben der Sprache („Sf3"). NIE umgestellt speichern: ein zweites Umstellen machte im
  Französischen/Spanischen/Italienischen aus dem König („R") einen Turm. Qwen auf der Spark stellt sie selbst nie um.
* **Kein erfundener Zug** (`IsGrounded`): jeder Figuren-/Schlag-/Rochadezug im Text muss in diesen Linien stehen (bloße
  Felder wie „e4" zählen nicht — vom Feldnamen im Satz nicht zu unterscheiden); sonst eine Nachfrage, dann verworfen.
* **Speicher an der ANALYSE** (`GameMoveExplanations`), gilt für alle Betrachter; ERZEUGEN nur der Besitzer, im Hintergrund
  (`Task.Run` mit eigenem Scope, `GameExplanationJobs` gegen Doppelstarts; ein Neustart verwirft das Laufende, der nächste
  Klick erzeugt nur das Fehlende), 4 Anfragen parallel.
* **Aus der Sicht des BESITZERS** (0.540.0, `GameMoveExplanation.Viewpoint`): „you" ist immer er — auch bei den Fehlern
  seines Gegners („your opponent", wie er sie nutzt). Vorher schrieb das Modell jeden Fehler an den, der ihn gemacht hatte
  („Your move 14. Nb5" an Weiß, obwohl der Besitzer Schwarz war). Die Seite kommt aus `SavedGameService.DetermineOwnerSide`
  (festgelegt > Plattform-Name), unbekannt = leer = neutral in der dritten Person. Die Fakten nennen deshalb die FARBEN statt
  „the player", die letzte Zeile (`Perspective`) sagt, wer der Leser ist. `GetAsync` liefert nur Texte der AKTUELLEN Sicht
  (legt der Besitzer später eine andere Seite fest, erscheint der Knopf wieder), `GenerateAsync` räumt die der alten weg.
  Migration `ExplanationViewpoint` hat alle Texte von vorher gelöscht.
* **Entstehen von selbst** (0.540.0, `GameReviewTexts` + `IGameReviewTextScheduler`): die Pumpe (`GameAnalysisService`)
  stößt nach dem ersten Durchgang (`Done`) und nach der Vertiefung (`RefinedAt`) je EINMAL an — seit 0.541.0 zuerst die
  Nacherzählung („Kurz erzählt", unten), dann die Erklärungen, dann die drei Roasts (nur fehlende, `GameRoast.Automatic`, nicht im Tagesdeckel). Nach der Vertiefung werden die Erklärungen in
  JEDER vorhandenen Sprache neu geschrieben (Bestzug/Klasse können sich verschieben). Nur für die mit dem BESITZER der
  Analyse verknüpfte Partie. Sprache: `SavedGame.ReviewLanguage` (die Seite schickt `{ lang }` mit „Partie analysieren"),
  sonst die jüngste gemerkte des Nutzers, sonst `en`. Die Pumpe kennt nur die Schnittstelle — die Texte hängen über
  `GameRoastService` → `SavedGameService` wieder an ihr (Zyklus), deshalb löst der Auslöser sie in einem eigenen Scope auf.
* **Sperrzeiten der Spark** (0.546.0, `Services/QuietHours.cs`, Wunsch des Nutzers: „gleiches zeitfenster" wie die
  Übersetzungen): `TextLlm:QuietHours` (Vorgabe `Mon-Thu 08:00-17:00; Fri 08:00-14:00`, Ende ausschließlich, LEER = nie
  gesperrt) in `TextLlm:TimeZone` (Vorgabe `Europe/Vienna` — der Server läuft in UTC, die Sommerzeit verschöbe sonst die
  Fenster). **Sie gilt NUR für die Hintergrund- und Massenläufe, nicht für Aufträge auf Zuruf** (0.585.0, Wunsch des
  Nutzers: „Spark steht unter Tag durchaus für On-demand-Aufträge zur Verfügung — nur nicht für die Patchläufe"). Gesperrt:
  die automatischen Texte nach einer Analyse — der `GameReviewTextScheduler` stellt, was die Pumpe in der Sperrzeit
  anstößt, ZURÜCK (je Analyse einmal, „vertieft" gewinnt beim Zusammenführen) und lässt es nach dem Ende los; ein
  Wartender je Prozess, schläft höchstens 10 min am Stück, ein Neustart verliert die Liste —, der Kurs-Übersetzungsdienst
  (0.548.0, `CourseTranslationWorker`: Aufträge warten, ein Lauf bricht beim Beginn ab und kommt zurück in die Schlange;
  auch ANGEFORDERTE Kurs-Übersetzungen — ein Kurs ist Stunden Arbeit) und der Bibliothekslauf (hält die Fenster außerhalb
  der App über die Schaltuhr `.jobs/spark-uebersetzung.sh` ein). FREI: „Fehler erklären lassen" (`POST …/explanations`),
  „Roast my game" (`POST …/roasts`) und die Nacherzählung beim Öffnen einer Partie (`GET …/recap` → `ScheduleRecap`, läuft
  sofort) — die DTOs tragen dafür kein `quietUntil` mehr, `quietHours` gibt es als Absage nicht mehr. Puzzle-Tipps hingen
  nie an der Sperrzeit. Eine unlesbare Angabe wirft beim Start (`FormatException`) statt still „nie gesperrt" zu bedeuten.
  Die Seite zeigt „wieder ab Fr., 14:00" nur noch bei der Kurs-Übersetzung (`quiet-hours.util.ts`).
* Frontend: `GameReviewComponent` lädt die Erklärungen, sobald die Analyse `done` ist (und bei Sprachwechsel), zeigt den
  Text unter dem Abzeichen des aktuellen Zugs, den Knopf „Fehler erklären lassen" nur mit `canGenerate`, keine Erklärung
  vorhanden und Fehlern in der Partie; fragt alle 5 s nach, solange es läuft; im Fehler-Training aus (nennt den besseren Zug).

**„Roast my game" (0.535.0, `GameRoastService`).** Ein frecher Kommentar zur EIGENEN Partie, ⋮-Menü von `/games/:id`
und der Partienliste (`GameRoastDialogComponent`), nur über das Modell auf eigener Hardware (`IsLocal`) und nur mit fertiger
Analyse. Drei Stile (Wunsch des Nutzers): `friendly`, `cheeky`, `russian` — der gnadenlose sowjetische Trainer, derb, mit
Kraftausdrücken, hinterfragt offen die geistige Kapazität („free for all"); die EINE Grenze im Auftrag: keine Angriffe auf
Herkunft, Nationalität, Religion, Geschlecht, Sexualität, Behinderung — der Text wird geteilt. Fakten: Kopfdaten, Ergebnis
aus Sicht des Spielers (`OwnerSide`), Genauigkeit je Seite, die fünf schlimmsten eigenen Züge mit besserem Zug und
Widerlegung (`GameMistakes`); genannte Züge müssen in der Partie oder diesen Linien stehen
(`GameMoveExplanationService.MentionsOnly`, eine Nachfrage). Synchron (ein Aufruf, Sekunden), je Partie/Sprache/Stil ein
Text („Neu würfeln" ersetzt), `MaxPerDay` 60 je Nutzer. Nichts wird automatisch veröffentlicht: Kopieren (mit Partie-Link)
bzw. Teilen-Blatt des Geräts. **„Partie mit diesem Roast teilen" (0.742.0)** gibt GENAU diesen Text frei (`SharedAt`) und
teilt/kopiert `/g/{token}?roast={id}`: die geteilte Seite zeigt den Roast statt „Kurz erzählt" über der Partie, die
Link-Vorschau (`OgMetaService`, Query `roast`) nimmt ihn als Beschreibung; der eigene Link leitet mit `roast` (und `train`)
auf `/games/:id` weiter. Neu würfeln setzt `SharedAt` zurück — ein geteilter Link zeigt nie still einen anderen Text.

**„Kurz erzählt" (0.541.0, `GameRecapService`).** Die Partie in zwei, drei Sätzen für die Link-Vorschau
(og:description von `/g/{token}` statt „1-0 · lichess · Partie auf RookHub nachspielen") und oben auf der Partieseite —
geschrieben vom Modell auf eigener Hardware (`IsLocal`), im Muster des Roasts. Regeln:
* **Fakten aus der Kurve** (`Course`): die Lage NACH jedem Halbzug in fünf Stufen aus der Gewinnchance von Weiß
  (≥ 80 gewinnt, ≥ 60 besser, > 40 ausgeglichen, gespiegelt), zu Abschnitten zusammengefasst („after 13.Nxe5: Black is
  winning"); ein Abschnitt aus EINEM Halbzug zwischen zwei gleichen fällt weg (Schlagen vor dem Zurückschlagen), mehr als
  `MaxCourseSegments` (8) = Anfang + die letzten. Dazu Kopfdaten, Eröffnungsname aus `[Opening]` bzw. chess.coms
  `[ECOUrl]`, die ersten zehn Halbzüge, Genauigkeit, die drei schwersten Fehler/Misses beider Seiten mit NUMMERIERTEM
  Bestzug und Widerlegung (`Numbered`), das Ende (Matt auf dem Brett > `[Termination]` außer „Normal" > nur das Ergebnis).
* **Dritte Person mit Namen** — den Text liest, wer den Link bekommt. Genannte Züge müssen in der Partie oder den
  Engine-Linien stehen (`MentionsOnly`, eine Nachfrage, sonst kein Text). Gespeichert in englischer SAN; die
  Figurenbuchstaben der Sprache setzt erst das Lesen (`CurrentAsync` → `PieceLetters.Convert`, Regel unten bei den Erklärungen).
* **Speicher je Partie und Sprache** (`GameRecaps`, geht mit der Partie). Gezeigt wird überall DERSELBE Text
  (`GameRecapService.CurrentAsync`): der in der Sprache der Partie (`SavedGame.ReviewLanguage`), sonst der jüngste.
* **Entsteht von selbst**: `GameReviewTexts` schreibt ihn als ERSTES nach der Analyse (ein Aufruf, und wer gleich teilt,
  hat ihn schon), nach der Vertiefung in jeder vorhandenen Sprache neu (`replace`). Für ältere Analysen stößt
  `GET /api/games/{id}/recap` ihn an (`IGameReviewTextScheduler.ScheduleRecap` → `WriteRecapAsync`). Doppelt gleichzeitig
  verhindert `GameExplanationJobs` mit dem Schlüssel (Partie-Id, `recap:<lang>`). Kein Knopf, kein Tagesdeckel.
* **Ergebnis nachgetragen** (0.819.0, Wunsch 2026-10-21): ändert „Korrigieren" (`UpdateAsync`) nur das Ergebnis, wird die
  Nacherzählung gelöscht (sie nennt das Ergebnis, „…with an open result"); Analyse und Fehler-Training bleiben. Beim
  nächsten Öffnen schreibt `GET …/recap` sie neu. Andere Züge löschen wie bisher alles (`MovesChangedAsync`).
* **Vorschaubild mit Kurve** (`OgImageService.RenderBoard(fen, flip, curve)`): mit FERTIGER Analyse rückt das Brett nach
  links, rechts die Kurve wie `EvalGraphComponent` (Lichess-Fläche von der Mittellinie, monotone kubische Glättung,
  linear bis ±10 Bauern, Matt am Rand; Lücken übernehmen den Wert davor), weiter ohne Schrift. Weil das Bild
  `immutable` gecacht ist und Discord & Co. Bilder nach der ADRESSE merken, trägt og:image dann `?v={analysisId}-{refined}`
  (`OgMetaService.CurveVersion`). Eine halbe Kurve (Analyse läuft) kommt nicht ins Bild.

**Karte des Trainingslinks** (0.745.0, Wunsch 2026-10-10: „beim Trainlink eine schöne Karte mit der Evalkurve … Verbessere dich —
spiele deine Fehler neu"): `/g/{token}?train=white|black` (Fehler-Training teilen, 0.741.0) bekommt og:title „Verbessere dich — spiele
deine Fehler neu", eine Beschreibung mit der Zahl der Fehler und das Bild `/api/og/img/train/{side}-{token}.png` (`?v=` wie bei der
Partie). `OgImageService.TrainCard`: Brett links (gedreht bei Schwarz), rechts „Verbessere dich" / „Spiele deine Fehler neu" /
„Die Fehler von …" über der Kurve, die Fehler der Seite als rote Punkte (`OgMetaService.TrainMarks`: ≥ 10 Prozentpunkte
Gewinnchance aus Sicht des Ziehenden, Lichess-Formel). **Das Bild ist damit nicht mehr schriftfrei**: die Schrift ist EINGEBETTET
(`Assets/fonts/DejaVuSans*.ttf`, Lizenz daneben) — das API-Image hat kein fontconfig. Neue Texte auf Bildern nur mit dieser Schrift.

**Bewertungskurve aus der EIGENEN Analyse (0.512.0).** „Partie analysieren" (`/g/…`, Liste, Nachspiel-Dialog)
wirft die Partie über denselben Weg wie die Punktepartie-Seite ein (`GameAnalysisService.CreateForGuessAsync`:
Haus-Engine, fünf Linien, gemeinsamer Deckel — aber **Tiefe 30** statt 20, `GameAnalysisDefaults.SavedGameTargetDepth`
(0.514.2 mit 30 eingeführt, 0.518.0 auf 25 — auf 30 brauchte eine Partie mit 47 Stellungen eine halbe Stunde —, seit 0.555.2 wieder 30, weil
das inzwischen die Vertiefung im Hintergrund ist, siehe „Zwei Durchgänge"): hier ist die Bewertung das Ergebnis, und ein Opfer, das die Engine erst zwei Züge später versteht,
stünde bei 20 als Fehler in der Kurve; eine schon vorhandene Analyse wird trotzdem wiederverwendet, auch eine
flachere), aber mit eigenem Ursprung
**`GameAnalysisOrigin.SavedGame`** — die Analyse gehört zur Partie und steht NICHT in „Eigene Analysen"
(`GameAnalysisService.ListAsync` lässt sie weg, für `/guess` UND `/analysis/games`). Regeln in `SavedGameService`:
* **Verknüpfung** `SavedGame.GameAnalysisId` (kein FK — die Analyse darf gelöscht werden; jeder Leser prüft,
  ob es sie noch gibt, und antwortet sonst `none`). Gesetzt NUR, wenn der BESITZER klickt; ein Gast bekommt
  seine eigene Analyse, die öffentliche Kurve bleibt die des Teilenden.
* **Mehrfach klicken = einmal rechnen** (`AnalyzeCoreAsync`): (1) die verknüpfte, solange nicht `Failed` —
  gleich, wer klickt; (2) sonst eine eigene des Aufrufers mit EXAKT gleichem `Pgn` (auch eine über `/guess`
  eingeworfene), neueste nicht gescheiterte, beim Besitzer verknüpft; (3) erst dann anlegen. Zwei Klicks binnen
  Millisekunden fängt der Client (Knopf gesperrt, solange der Aufruf läuft).
* **Perspektive**: die Kandidatenlisten stehen aus Sicht der Seite am Zug; `GameEvalsDto` liefert ALLES aus
  WEISS-Sicht (`Services/Games/GameEvals.cs`, gedreht mit derselben FEN-Regel wie beim Einlesen). `cp/mate` = bester
  Kandidat, `played*` = Kandidat des Partiezugs (fehlt er unter den fünf → `null`), `second*` = zweiter (seit
  0.514.0 im Client für Great/Brilliant, siehe `src/frontend/CLAUDE.md`), `candidates` = alle (seit 0.519.0,
  für die gleichwertigen Züge in „Eigene Fehler nachspielen"). `final` = gespielter Kandidat der LETZTEN Zeile (für die Endstellung gibt es
  keine Zeile). Nicht gerechnete und aufgegebene (`[]`) Zeilen fehlen in `plies` — der Client lässt dort eine Lücke.
* Weder die Partie noch die Analyse bringen beim Nachfragen ihr PGN mit: der Rückfall (b) vergleicht per
  Unterabfrage in SQL (`QueryTranslationTests.GespeichertePartie_…` prüft die Übersetzung gegen MariaDB).
* Frontend: `features/games/game-review.util.ts` (Formeln), `game-review.component.ts` (lädt, fragt alle 10 s
  nach, solange `pending`/`running`), `shared/pgn-viewer/eval-graph.component.ts` (SVG-Kurve). Siehe
  `src/frontend/CLAUDE.md`.
* **Zwei Durchgänge** (0.523.0, gewünscht 2026-09-24): „Partie analysieren" (`Origin.SavedGame`) rechnet ERST schnell
  (`SavedGameFastDepth` 20, `SavedGameFastMultiPv` 1 — Kurve, Genauigkeit und Fehler stehen nach Minuten, Status `done`),
  DANN im Hintergrund die Vertiefung (`GameAnalysis.RefineDepth` = `SavedGameTargetDepth` 30 seit 0.555.2, vorher 25, `RefineMultiPv` 5): jede
  Stellung wird neu gerechnet und ERSETZT (`GameAnalysisPosition.Refined`), am Ende `RefinedAt` + Genauigkeit neu.
  Regeln: (1) Vertiefungs-Aufträge sind `AnalysisJob.Background` — `PickNextForEngineAsync` nimmt sie erst, wenn kein
  normaler wartet (die warme Hashtabelle zählt nur innerhalb derselben Stufe); (2) je Partie höchstens
  so viele Aufträge offen, wie der Engine-Besitzer Hintergrund-Engines hat (`RefineJobCap`: mindestens `MaxOpenRefineJobsPerGame` = 8, höchstens `MaxOpenJobsPerGame` = 32; bis 0.567.2 fest 8 — bei 16 Engines lag die Hälfte brach);
  (3) vertieft wird erst, wenn KEINE Partie des Nutzers mehr im ersten Durchgang steckt, dann die älteste
  (`IsOwnersRefineTurnAsync`) — seit 0.647.0 mit Schwanz: haben die älteren Vertiefungen ZUSAMMEN weniger unvertiefte
  Stellungen als Engines da sind (`UnrefinedPliesOfOlderGamesAsync`, `TailMayAdvance`), fängt die nächste schon an
  (Anlass 04.10.2026: Analyse 5540 hing eine Stunde an ihrer letzten Stellung, die geteilte Partie dahinter bekam
  keinen Auftrag, 15 von 16 Engines rechneten Meisterpartien); (4) scheitert die Vertiefung einer Stellung, bleibt das
  erste Ergebnis; (5) Zeitgrenze je Stellung (0.647.0, `RefineMaxSecondsPerPosition` = 900 s `AnalysisJob.SecondsSpent`
  über alle Läufe, Warten zählt nicht): danach übernimmt `IngestFinishedAsync` das Erreichte (`ResultJson` bei
  `ReachedDepth`), wenn es tiefer ist als der erste Durchgang, sonst bleibt der erste — der Lauf wird angehalten
  (`AnalysisJobService.Interrupt`) und der Auftrag entfernt. Anlass derselbe: Tiefe 26 nach elf Minuten, zweimal
  von einem schnellen Durchgang verdrängt, jedes Mal von vorn. `GameEvalsDto`
  meldet `Refining`/`Refined`, der Client fragt dann einmal je Minute nach. Punktepartie und von Hand eingereihte Partien
  bleiben bei einem Durchgang (`RefineDepth` null), Altbestand ebenso.
* **Buchzüge** (0.522.0): `GameEvalsDto.BookPlies` = die Halbzüge, deren Stellung DANACH in einem für die Erweiterung
  markierten Repertoire (`UseForExtension`, alle Arten, Zugumstellungen) des AUFRUFERS steht, vor der Abweichung —
  `RepertoireAnalyzeService.BookPliesAsync` (dasselbe gecachte Positions-Set wie die Extension-Abweichungsanalyse).
  Anonym leer — ein Teilen-Link darf nicht verraten, was der Teilende vorbereitet hat. Der Client zeigt die Klasse
  „book" (schlägt jede andere, Grundklasse + Genauigkeit bleiben), der Fehler-Trainer überspringt Buchzüge.
* **Computer-Linien** (0.521.0): `GameEvalCandidateDto.Pv` = die Variante der Engine je Kandidat (UCI ROH vom
  Broker, Rochade ggf. König-schlägt-Turm — der Client spielt sie nach, `normalizeCastlingUci`), abgelegt in
  `CandidatesJson` als `"pv":[…]` (`BrokerCandidates.MaxPvPlies` 16), gelesen mit `BrokerCandidates.PvsFromJson`.
  Die Wertung (`FromJson`) ignoriert das Feld. Analysen von vor 0.521.0 haben keine Varianten — die Partieseite
  zeigt dort je Kandidat nur den Zug mit Bewertung; wer Linien will, lässt neu rechnen (Restart).
* **Restdauer DIESER Partie** (0.517.0, `GameEvalsDto.EtaMinutes`, `GameEvals.EtaMinutes`): solange die Analyse
  läuft, aus den `AnalyzedAt` der jüngsten 12 gerechneten Stellungen (seit 0.521.2 über `AnalysisPace`: nur der
  zusammenhängende Lauf, eine Pause > 15 min davor zählt nicht) — gemessen vom ältesten davon bis JETZT
  (hängt die Engine, wächst die Schätzung), mindestens 60 s Spanne (die Pumpe liefert oft mehrere im selben
  Takt), nie unter 1 min; unter zwei Ergebnissen `null`. Die Wartezeit vor dem ersten Ergebnis zählt nicht.
  Bewusst NICHT `GET /api/game-analyses/throughput`: das ist das Tempo aller Analysen EINES Nutzers und braucht
  Anmeldung — `/g/` ist öffentlich. Die Schreibweise („11 min", „3 h 20 min") teilen sich beide über
  `shared/eta.util.ts`.
* **Genauigkeit je Seite liegt AN DER ANALYSE** (0.515.0, `GameAnalysis.AccuracyWhite/AccuracyBlack`,
  `Services/Games/GameAccuracy.cs` = Server-SPIEGEL der Client-Formeln mit denselben LITERALEN Testwerten in
  `GameAccuracyTests` ↔ `game-review.util.spec.ts`): gerechnet in `PumpOneAsync` beim Übergang auf `Done`, damit die
  Partienliste sie zeigen kann, ohne je Partie die Stellungen zu laden (sie fragt während einer Rechnung alle 10 s).
  Die Liste zeigt statt des Analysieren-Knopfs den Fortschritt in %, fertig die beiden Genauigkeiten neben
  Ergebnis/Zugzahl, gescheitert wieder den Knopf. `GET /api/game-analyses?includeSavedGames=true` nimmt die
  `SavedGame`-Analysen mit — die Seite „Partie-Analysen" zeigt sie (Fortschritt), die Punktepartie-Seite nicht.

Akzeptiert sowohl JWT (User-Login) als auch ApiToken (`Authorization: Bearer rkh_…`). Bei ApiToken muss `scope=extension` sein (sonst 403); ein Token mit Scope `engine` erreicht ausschließlich `/api/external-engine/*` (`PatScopeFenceMiddleware.AllowedPrefixesByScope`, siehe „Eigener Engine-Broker“). Policy-Scheme im Auth-Stack routet das Bearer-Format automatisch zum passenden Handler.

CORS (`ExtensionPolicy`, nur für `ExtensionController`): erlaubt `https://www.chess.com`, `https://lichess.org`, `https://www.chessable.com`, `https://chessable.com` mit `GET`+`POST`, ohne `AllowCredentials` (Auth strikt über Bearer-Header). Gilt für den Userscript-`fetch`-Pfad; die Extension-Variante geht ohnehin CORS-frei über ihren Background-Worker. Die Default-CORS-Policy (Frontend) erlaubt `http://localhost:4200` + `http://localhost:8085`.
