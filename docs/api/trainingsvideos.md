# Trainingsvideos / Trainerstunden (privat je Konto, 0.986.0)

> Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md. Oberfläche: `docs/frontend/trainerstunden.md`.

Aufgezeichnete Trainingsstunden (Teams-Mitschnitte mit dem Trainer) als **private** Sammlung eines Kontos: Video,
Zusammenfassung, besprochene Partien, Segmente (Zeitspanne + Stellung + Trainerkommentar), Fragen und Notizen. Eine
Stunde gehört genau **einem** Konto (`TrainingLessons.UserId`); sehen darf sie nur dieses Konto und jeder Admin — eine
fremde Stunde ist 404. Nichts davon ist öffentlich: keine Teilen-Links, keine Link-Vorschau, nicht in Sitemap/Suche
(das Video antwortet zusätzlich mit `X-Robots-Tag: noindex`). Menüpunkt `training-videos` („Trainerstunden“, Gruppe
Training), Vorgabe **nur Admins** (`MenuVisibilityLevel.Admin`; für ein Nicht-Admin-Konto in der Menüverwaltung auf
„Gruppen“ stellen).

## Austauschformat (eine JSON-Datei je Stunde, `<datum>.json`)

Schema 1 (bzw. 2 = Schema 1 + optionale Felder), Zeiten als `h:mm:ss`-Zeichenketten, unbekannte Felder werden ignoriert
(`Services/TrainingVideos/TrainingLessonFile.cs`, Beispiel `tests/RookHub.Api.Tests/Fixtures/TrainingVideos/2026-10-27.json`):

- `date` (JJJJ-MM-TT, Schlüssel der Stunde je Konto), `video.file` (reiner Dateiname im Video-Ordner, `<datum>.mp4`;
  `video.originalFile` wird ignoriert), `video.durationSec`, `summary`.
- `games[]`: `id` (`g1`), `kind` own|lesson|exercise, Kopfdaten, `from`/`to`, `pgn`, `analysisPgn` (voller Analysebaum,
  Schema 2), `rookhubGameId` (= `SavedGames.Id` des Kontos; fremde/fehlende Id → Warnung im Bericht), `matchConfidence`.
- `segments[]`: `t`/`tEnd`, `gameId`, `fen` (ungültig → Segment ohne Stellung, gezählt), `move` (SAN), `moveKind`
  played|analysis (Schema 2), `comment` (geglättet), `quote` (Rohtranskript), `quoteFrom`/`quoteTo` (Schema 2),
  `variations`, `tags[]`, `question` {prompt, answer, answerLine, answerT, explanation}.
- `notes[]`: `t`, `gameId`, `category` schwaeche|eroeffnung|plan|rechnen|zeit|psychologie|endspiel|allgemein (unbekannt →
  allgemein; `endspiel` erst ab 0.987.0 — vorher importierte Endspiel-Notizen stehen als `allgemein` da, bis die Stunde
  neu importiert wird), `text`, `quote`.

## Zwei Einträge je Stunde: private / general (0.988.0)

Eine Stunde (Datum) wird beim Import in bis zu **zwei Einträge** (`TrainingLessons.Part`) geteilt, beide mit demselben
Video und denselben Zeitstempeln:

- **`private`** („Private Partien“): Partien `kind: own` (die eigenen Partien mit den Kommentaren des Trainers) und ihre
  Segmente/Fragen.
- **`general`** („Allgemeine Partien“): Partien `lesson`/`exercise` und ihre Segmente/Fragen.

Regeln (`TrainingVideoImporter.SplitParts`): Hat die Stunde nur eine Art, gibt es nur diesen Eintrag (ohne jede Partie:
einen privaten). **Segmente ohne (bekannte) Partie** (Einleitung, Plauderei, allgemeine Erklärung) gehören zum Teil ihrer
Nachbarn in der Zeitfolge — stimmen voriger und nächster zugeordneter Nachbar überein oder gibt es nur einen, dorthin;
liegen sie zwischen den Teilen (unklar), zum privaten. **Notizen** (meist Aussagen über die eigenen Schwächen) gehen
geschlossen zum privaten Teil, auch wenn sie eine Lehrpartie nennen; gibt es keinen privaten, zum einzigen Teil. Das
Notizbuch und die Notiz-Suche schlagen die Partie einer Notiz deshalb über alle Teile derselben Stunde nach (Konto +
Datum, `SessionGamesAsync`); `gameLessonId` nennt den Eintrag der Partie.

**Übungsstand** zieht beim Re-Import mit (`MoveProgress`): jede Zeile geht zu dem Eintrag desselben Datums, der ein
Segment mit ihrem `SegmentKey` hat (der bisherige zuerst); passt keines mehr, bleibt sie, wo sie ist (bzw. geht zum
ersten neuen Eintrag, wenn ihrer wegfällt). Treffen zwei Zeilen desselben Kontos aufeinander, gilt der jüngere Versuch,
`plays` wird addiert — es geht nichts verloren, auch nicht der Stand anderer Konten (Admins). Bestehende Zeilen werden
weiterverwendet (gleicher Teil zuerst, sonst die übrige — so wird die alte ungeteilte Stunde zum privaten Teil und
behält Id und Links); übrige Zeilen werden erst nach dem Umzug gelöscht. Die Migration `TrainingLessonParts` setzt
Altbestand auf `private`, Stunden ganz ohne eigene Partie gleich auf `general`; gemischte Stunden teilt erst der
Re-Import.

## Tabellen

`TrainingLessons` (UNIQUE UserId+Date+Part) → `TrainingLessonGames`, `TrainingSegments` (→ `TrainingQuestions` 1:1),
`TrainingNotes`; `TrainingSegmentProgress` (Übungsstand je Konto). Details: `docs/datenbank-schema.md`.

- **Stellungssuche** über `TrainingSegments.BoardFen` = nur das Figurenfeld der FEN (indiziert, `TrainingBoardKey`):
  Zugrecht/Rochade/Zähler zählen nicht (die Aufbereitung rät sie teils); `sameSide` im Treffer sagt, ob auch das
  Zugrecht stimmt.
- **Volltext** über `SearchText` (Kommentar + Zitat bzw. Text + Zitat) mit MariaDB-`FULLTEXT` (Migration per SQL, Muster
  `LibrarySearchText`), Boolean-Modus, jedes Wort Pflicht und als **Wortanfang** (`+läufer*`). Mindestlänge wie im
  Frontend: zusammen mindestens drei Buchstaben/Ziffern; Wörter unter drei Zeichen (InnoDB indiziert sie nicht) laufen
  zusätzlich als Teilzeichenkette (LIKE). MariaDB
  hat keinen deutschen Stemmer — Groß/Klein und Umlaute vergleicht die Sortierung, Beugungen fängt nur der Wortanfang.
  InMemory-Tests nehmen eine Contains-Krücke; die echte Abfrage prüft `TrainingVideoSqlTests` (IntegrationTests).
- **Übungsstand** (`TrainingSegmentProgress`) hängt NICHT an der Segment-Id (die ein Re-Import neu vergibt), sondern an
  Stunde + `SegmentKey` = `<startSec>|<figurenfeld>` — ein Re-Import behält ihn, solange Zeit und Stellung gleich bleiben.

## Import (Admin, idempotent je Konto + Datum + Teil)

`TrainingVideoImporter`: gibt es die Einträge des Kontos zum Datum schon, bleiben ihre Zeilen (Id, Links, Übungsstand),
Partien/Segmente/Fragen/Notizen werden ersetzt (Teilung siehe oben). Bericht zusätzlich: `parts[]` (`part`, `lessonId`,
`replaced`, Zähler je Eintrag) und `progressMoved`; `lessonId` = der erste Teil. **`dryRun` ist Vorgabe** (`true`): geprüft, gezählt, nichts geschrieben.
Bericht je Datei: Zähler, ungültige FENs, ob das Video im Video-Ordner liegt, Warnungen, Fehler (Fehler im Inhalt sind
200 mit `ok: false`, kein 4xx).

| Methode | Endpoint | Zweck |
|---|---|---|
| POST | `/api/admin/training-videos/import?user=<name\|id>&dryRun=true` | eine Datei als Rumpf (bis 10 MB) |
| POST | `/api/admin/training-videos/import-folder?user=…&dryRun=true` | alle `*.json` aus `TrainingVideos:DataRoot` |

Skript (über `~/bin/rookhub-prod-api`, gedrosselt; ohne `--apply` Probelauf):
`tools/trainingvideos/import.py /srv/media/claude-sessions/schachvideos/aufbereitet --user kahalm [--dev] [--apply]`.
Prod-Import nur auf Zuruf des Users, vorher Probelauf.

## Lesen (auth, nur eigene Stunden bzw. Admin)

| Methode | Endpoint | Zweck |
|---|---|---|
| GET | `/api/training-videos` | Einträge (neueste zuerst, privat vor allgemein; je Eintrag `part`; `ownerName` nur für Admins bei fremden Stunden) |
| GET | `/api/training-videos/{id}` | Eintrag: `part`, `otherParts[]` (`id`, `part` der übrigen Teile desselben Datums), Partien, `pgns`, `analysisPgns`, Segmente mit Frage und eigenem Stand |
| GET | `/api/training-videos/search/position?fen=` | „Im Training besprochen?“ — volle FEN oder nur Figurenfeld, ≤ 100 Treffer |
| GET | `/api/training-videos/search/text?q=` | Volltext über Segmente und Notizen, ≤ 100 je Art (Notiz-Treffer mit `noteId`; alle Treffer mit `part`, auch die Stellungssuche) |
| GET | `/api/training-videos/notebook` | Trainer-Notizbuch: alle Notizen, wiederkehrende Themen, Eröffnungsempfehlungen (0.987.0, siehe unten) |
| GET | `/api/training-videos/games/{gameId}/comments` | Trainerkommentare an einer RookHub-Partie, je Stellung |
| GET | `/api/training-videos/questions?onlyWrong=` | stundenübergreifender Fragen-Satz (je Eintrag `rookhubGameId`, falls zugeordnet) |
| PUT | `/api/training-videos/{id}/segments/{segmentId}/progress` | `{ status: solved\|wrong\|skipped, attempts }` |
| GET | `/api/training-videos/access-token` | Ausweis `?t=` für das Video (12 h, DataProtection, eigener Zweck) |
| GET | `/api/training-videos/{id}/video?t=` | mp4 mit Bereichsanfragen; `[AllowAnonymous]` nur wegen `<video src>` — Ausweis oder Bearer, Besitz je Anfrage |

**Zuordnung Segment → Partiezug** (`GameCommentsAsync`): `fenIndex` 0 = Anfangsstellung, n = nach dem n-ten Halbzug.
Nennt ein `played`-Segment (oder eines ohne `moveKind`) einen Zug, der AUS seiner Stellung gespielt wurde, hängt der
Kommentar an der Stellung NACH dem Zug (`move`); zeigt die FEN schon die Stellung danach, dort (`move`). `analysis`-Segmente
hängen immer an ihrer Stellung (`analysis`). Ohne Zug: an der Stellung (`position`, Zugrecht bevorzugt). Kommt die
Stellung in der Partie nicht vor (Variante), hängt das Segment an der zuletzt zugeordneten Partiestellung
(`variation`, mit eigener `fen`). Wiederholungen: erste passende Stellung ab dem zuletzt zugeordneten Segment.

**Aufgabe im Nachspielen** (`TaskFen`/`TaskMove` je Segment, `DrillTask`): gesucht ist die Antwort der Frage, sonst der
gespielte Partiezug (`moveKind: played`). `TaskFen` ist immer die Stellung VOR dem Zug, `TaskMove` der legale Zug von dort.
Zeigt die FEN der Aufbereitung schon die Stellung danach, rechnet der Server über die Partie (RookHub-Partie des Kontos,
sonst PGN der Stunde) zurück; eine Frage wird zuerst aus ihrer eigenen Stellung gelesen. Ist der Zug nirgends legal: beide
`null` (nur ansehen).

## Teilen-Links (Wunsch 2026-11-17, `TrainingLessonShares`)

Ein EINZELNER Eintrag (ein Teil einer Stunde) lässt sich per Link mit Token zeitlich begrenzt teilen. Tabelle
`TrainingLessonShares` (LessonId FK Cascade, Token ≤64 UNIQUE, CreatedAt, ExpiresAt, RevokedAt). Token = 256 Bit
Base64URL (43 Zeichen, `SecretTokens.NewRaw`), **im Klartext** gespeichert wie beim Vorbild `SavedGame.AnonShareToken`
(`AnonGameShares`) — der Besitzer soll einen bestehenden Link wieder kopieren können. Mehrere Links je Eintrag, höchstens
20 gleichzeitig aktive. Geteilt wird `/tl/{token}`.

| Methode | Endpoint | Zweck |
|---|---|---|
| GET | `/api/training-videos/{id}/shares` | aktive Links des EIGENEN Eintrags (`id`, `token`, `createdAt`, `expiresAt`), neueste zuerst |
| POST | `/api/training-videos/{id}/shares` | `{ days: 1\|7\|30 }` (fehlt: 7) → neuer Link; 400 andere Frist / zu viele aktive |
| DELETE | `/api/training-videos/{id}/shares/{shareId}` | widerrufen (204) |
| GET | `/api/training-videos/shared/{token}` | **ohne Anmeldung**: der Eintrag für den Gast |
| GET | `/api/training-videos/shared/{token}/video` | **ohne Anmeldung**: mp4 mit Bereichsanfragen, Rate-Limit `ebook-file` |

- Anlegen/Listen/Widerrufen nur durch das Konto des Eintrags — auch ein Admin teilt keine fremden Stunden (404).
- Gast-Antworten: unbekanntes Token 404, abgelaufen oder widerrufen **410** (`{ message, code: share_expired }`), sonst nichts
  Näheres. Jede Gast-Anfrage — auch jede Bereichsanfrage des Videos — schlägt das Token neu nach (Widerruf wirkt sofort);
  die Id des Eintrags kommt nur aus der Link-Zeile, eine Id in der Adresse gibt es auf dem Gast-Weg nicht. Ein Token, das
  nicht wie eines aussieht (Base64URL, 20–64 Zeichen), wird gar nicht nachgeschlagen. Die Stunde: globaler Rate-Limiter je IP.
- **Was der Gast sieht** (`TrainingVideoService.GetForGuestAsync`): Datum, Teil, Zusammenfassung, Video, Segmente mit
  Kommentar/Zitat/Varianten/Frage, Partien der Stunde mit `pgns`/`analysisPgns`, Aufgaben fürs Nachspielen.
  **Nicht**: Notizen (Notizbuch), Übungsstand (`progress` immer null), andere Teile (`otherParts` leer), RookHub-Partien
  des Besitzers (`rookhubGameId`/`matchConfidence` null), Konto- oder Besitzername. Spielernamen in den Partien der
  Stunde bleiben (sie sind der Inhalt).
- Video: der Dateiname kommt nur aus der DB-Zeile; `VideoPath` lässt nur einen reinen Dateinamen zu und vergleicht mit
  Trenner gegen den Video-Ordner. `Cache-Control: private, max-age=300`, `X-Robots-Tag: noindex`.
- Re-Import: behaltene Einträge behalten ihre Links; ein wegfallender Eintrag nimmt sie mit, die Kontolöschung ebenso.

## Trainer-Notizbuch (0.987.0, `TrainingNotebookService`)

`GET notebook` → `{ notes[], themes[], openings[], hasRepertoire, repertoireTruncated }`, sichtbar wie alles hier (eigene
Stunden, Admin alle). Die Merksätze (`docs/api/merksaetze.md`, kuratierte Liste) bleiben unberührt — Notizen werden
NICHT dorthin übernommen.

- **`notes`**: jede Notiz mit Stunde/Datum, `startSec`, Kategorie, Text, Rohzitat, Partie (`gameWhite`/`gameBlack`,
  `gameKind`, `rookhubGameId`), `hasVideo`, `lessonDurationSec` (Ausschnitt-Ende); neueste Stunde zuerst, darin nach Zeit.
- **`themes`** („sagt er immer wieder“, `TrainingNoteThemes`, kein LLM): Stichwörter = Buchstabenfolgen ab 4 Zeichen
  (Züge fallen so heraus), ohne deutsche Füllwörter, Endungen grob gestutzt; Gewicht IDF über alle sichtbaren Notizen;
  Ähnlichkeit = Gewicht der gemeinsamen Stichwörter / Gewicht der kleineren Menge (≥ 2 gemeinsame); Gruppen per
  Average-Linkage ab 0,35 (Single-Linkage verkettete Themen über Brückennotizen). Ein Thema braucht ≥ 2 Stunden,
  `noteId` = Notiz mit der größten Ähnlichkeit zu den übrigen; höchstens 20, nach Stundenzahl. 394 Notizen ≈ 0,1 s.
- **`openings`**: jede Notiz der Kategorie `eroeffnung` mit der Stellung des nächstgelegenen Segments DERSELBEN Partie
  (Segment, dessen Spanne die Notizzeit enthält, sonst kleinster Abstand des Beginns; ohne Partie keine Stellung) und dem
  Abgleich mit dem EIGENEN Repertoire des anfragenden Kontos (`RepertoireBoardContinuations`, geteilte Repertoires
  zählen nicht): verglichen wird nur das Figurenfeld (die Aufbereitung rät Zugrecht/Rochade), je Repertoire ein Treffer
  mit der ersten Linie (Kapitel/Name/`gameIndex`/`ply` für „im Repertoire öffnen“) und allen Zügen dort (Hauptzug +
  abzweigende Varianten), `sameSide` = auch das Zugrecht stimmt; höchstens 3 je Stellung. Ein Lauf über alle Linien,
  Zeitbudget 4 s (`repertoireTruncated`); `hasRepertoire` = das Konto hat eigene Linien.

## Video und Konfiguration

`TrainingVideos__VideoRoot` (mp4, H.264 faststart) und `TrainingVideos__DataRoot` (JSON), beide nur lesend gemountet
(Dev: `/srv/media/claude-sessions/schachvideos/videos` → `/app/trainingvideos`, `…/aufbereitet` → `/app/trainingvideos-data`;
Repo-Compose über `TRAINING_VIDEOS_HOST_PATH`/`TRAINING_VIDEOS_DATA_HOST_PATH`). Dateien müssen für `appuser` (uid 1655)
lesbar sein. Auslieferung `PhysicalFile(..., enableRangeProcessing: true)`, Rate-Limit-Topf wie die E-Book-Dateien
(`ebook-file`, 1500/min je Adresse — Spulen erzeugt viele Bereichsanfragen). Frontend-nginx: eigene Location
`^~ /api/training-videos` mit `proxy_buffering off` und 3600 s (`DeploymentConfigTests`); der NPM davor puffert auf
`location /` (Vorgabe) und reicht Range trotzdem durch.
