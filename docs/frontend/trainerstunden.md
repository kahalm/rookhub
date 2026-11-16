# Trainerstunden (Trainingsvideos, 0.986.0)

Privat je Konto, Menüpunkt `training-videos` (Vorgabe Admin-Stufe, `MenuRegistry`). API und Datenmodell:
`docs/api/trainingsvideos.md`. Gestaltung: `docs/DESIGN.md` 8.1 („Trainerstunden“). Keine Sitemap-/OG-/SEO-Einträge.

## Routen (`app.routes.ts`, alle `authGuard` + `menuGuard('training-videos')`, Literale vor `:id`)

| Weg | Komponente | Zweck |
|---|---|---|
| `/training-videos` | `TrainingVideosListComponent` | Stundenliste + Suche; `?fen=` startet die Stellungssuche |
| `/training-videos/questions` | `TrainingDrillComponent` (`data.source = 'questions'`) | Fragen-Satz über alle Stunden |
| `/training-videos/:id/drill` | `TrainingDrillComponent` | Nachspielen einer Stunde, `?seg=` = Startstellung |
| `/training-videos/:id` | `TrainingLessonComponent` | Video + Brett + Segmentliste, `?t=` (Sekunde) und `?seg=` springen |

## Bausteine (`features/training-videos/`)

- `training-videos.service.ts` — alle Aufrufe unter `/api/training-videos`, Typen der Antworten, `accessToken()` für
  `<video src>` (Muster `EbookService`: einmal holen, parallele Anfragen teilen sich eine, neu ab < 3 h Restlaufzeit,
  bei An-/Abmelden vergessen), `videoUrl(id, token)`, `trainingGameLabel` („Weiß – Schwarz“, PGN-„?“ → „Unbekannt“).
- `training-drill.util.ts` — reine Regeln des Nachspielens (Spec daneben): Aufgabe = `moveKind === 'played'` mit Zug
  ODER Frage; gesuchter Zug = `question.answer ?? move`, nur wenn er aus der FEN legal ist (tolerant gegen `+`, `!`,
  Zugnummer, `0-0`), sonst „nur ansehen“; richtig → `solved` (Fehlversuche werden mitgeschickt), drei Fehlversuche oder
  „Auflösen“ → `wrong`, „Überspringen“ → `skipped`; Ausschnitt = `quoteStartSec ?? startSec` bis `quoteEndSec ?? endSec ??
  +60 s`; `activeSegmentAt` (zuletzt begonnenes Segment, das die Videozeit enthält — Segmente überlappen); Gruppenköpfe
  beim Wechsel von Partie/Stunde.
- `training-videos-list.component.ts` — Karten der Stunden, ein Suchfeld „Wort oder FEN“ (FEN wird erkannt, auch nur das
  Figurenfeld; Text ab drei Buchstaben), „Brett aufbauen“ (`training-position-dialog.component.ts`, der Stellungs-Editor
  des Analysebretts). Stellungssuche läuft über die Adresse (`?fen=`), Treffer mit gleichem Zugrecht zuerst. Suchen als
  `Subject` + `switchMap` mit `catchError` innen.
- `training-lesson.component.ts` — natives `<video controls preload="metadata">`; `timeupdate`/`loadedmetadata` hängen
  per `addEventListener` am Element (kein Template-Listener → kein Neuzeichnen 4×/s), das Signal der Auswahl ändert sich
  nur, wenn ein anderes Segment dran ist; die Liste scrollt nur ihren eigenen Kasten (≥ 900 px). Sprünge vor
  `loadedmetadata` werden gemerkt. Partien: Link zur RookHub-Partie (`rookhubGameId`), „Analyse öffnen“ übergibt den
  ChessBase-Analysebaum (`analysisPgns`, sonst `pgns`) als Router-State `pgn` an `/analysis` — dort wie „In Analyse
  öffnen“ der Partienliste (angemeldet: eigener Linien-Baum mit Varianten). Ein eigener Varianten-Viewer auf der
  Stundenseite wurde NICHT gebaut.
- `training-drill.component.ts` — Brett zur Seite am Zug gedreht, `playable` nur während der Aufgabe; ein falscher Zug
  steht 0,7 s und springt zurück (die Meldung bleibt als Text). Stand je Aufgabe per `PUT …/progress`; Fehler → Snackbar.
  Die gespielte Liste ist ein Schnappschuss (beim Speichern verschiebt sich nichts unter dem Index); „Nur Falsche
  wiederholen“ filtert nach dem Ausgang dieses Durchgangs, sonst nach dem gespeicherten Stand.
- `training-clip-player.component.ts` — „Erklärung hören“: EIN `<video preload="none">`, Quelle erst beim ersten Abspielen,
  spult auf den Anfang, hält bei `currentTime ≥ Ende` an; „nur Ton“ blendet das Bild aus (Vorgabe = `EinkService.enabled()`).
  Neue Stellung → hält an.

## Einbindung außerhalb

- Partieseite `/games/:id` (nur `own`, nur mit sichtbarem Menüpunkt; `MenuService` wird erst dort per `Injector` geholt):
  `features/games/trainer-comments.component.ts` in der Seitenspalte vor „In welchen Repertoires?“. Lädt einmal je Partie
  `games/{id}/comments`, zeigt die Kommentare zu `fenIndex = currentMoveIndex + 1` (als Eingabe, damit die OnPush-Karte
  mitzieht), Nebenvarianten (`matchedBy = 'variation'`) mit eigenem Mini-Brett, Links ins Video und zum Nachspielen; darüber
  die Sprungzeile aller kommentierten Züge (`goTo` → `service.goToMove(fenIndex - 1)`). Ohne Kommentare: nichts.
- ⋮-Menü der Stellung (`features/analysis/position-menu.component.ts`, Analysebrett und Partieseite): „Im Training
  besprochen?“ → `/training-videos?fen=…`, nur mit sichtbarem Menüpunkt (beim Öffnen des Menüs gefragt).
- Navbar (Gruppe Training, nach Woodpecker), Anmelde-Hinweis (`login.component.ts` `AREA_KEYS`), Menü-Verwaltung
  (`admin.menu.items.training-videos`), Hilfe (`MENU_HELP` + Abschnitt `trainingVideos`, sichtbar für alle wie jeder
  Hilfe-Abschnitt; der Text nennt die Freischaltung).
- `MenuService`/`TrainingVideosService` lesen `currentUser$` mit `?.` — Specs mit schlankem `AuthService`-Ersatz bauen sie.

## Texte

`trainingVideos.*` (+ `nav.trainingVideos`, `admin.menu.items.training-videos`, `help.s.trainingVideos`) in allen 25
Sprachen. Züge immer über `| san` bzw. `localizeSan` (Meldungen im TS).
