# Trainerstunden (Trainingsvideos, 0.986.0)

Privat je Konto, Menüpunkt `training-videos` (Vorgabe Admin-Stufe, `MenuRegistry`). API und Datenmodell:
`docs/api/trainingsvideos.md`. Gestaltung: `docs/DESIGN.md` 8.1 („Trainerstunden“). Keine Sitemap-/OG-/SEO-Einträge.

## Routen (`app.routes.ts`, alle `authGuard` + `menuGuard('training-videos')`, Literale vor `:id`)

| Weg | Komponente | Zweck |
|---|---|---|
| `/training-videos` | `TrainingVideosListComponent` | Einträge + Suche; `?part=private\|general` wählt die Gruppe, `?fen=` startet die Stellungssuche |
| `/training-videos/notebook` | `TrainingNotebookComponent` | Trainer-Notizbuch (0.987.0): Themen, Eröffnungsempfehlungen, alle Notizen |
| `/training-videos/questions` | `TrainingDrillComponent` (`data.source = 'questions'`) | Fragen-Satz über alle Stunden |
| `/training-videos/:id/drill` | `TrainingDrillComponent` | Nachspielen einer Stunde, `?seg=` = Startstellung |
| `/training-videos/:id` | `TrainingLessonComponent` | Video + Brett + Segmentliste, `?t=` (Sekunde) und `?seg=` springen |

**Zwei Teile je Stunde** (0.988.0, Server teilt beim Import, siehe `docs/api/trainingsvideos.md`): „Private Partien“
(eigene Partien) und „Allgemeine Partien“ (Lehrbeispiele/Übungen) sind getrennte Einträge mit eigener Id, demselben
Video und denselben Zeitstempeln. Die Liste trennt sie per `mat-button-toggle-group` mit Anzahl (Vorgabe privat, nur
allgemeine Einträge → allgemein; Wahl in `?part=`), jede Karte heißt „Datum · Teil“, Suchtreffer und Fragen-Satz nennen
den Teil. Die Stundenseite zeigt „Datum · Teil“ und einen Knopf „Allgemeine/Private Partien dieser Stunde“
(`otherParts`), der an der aktuellen Videostelle (`?t=`) zum anderen Teil wechselt; die Komponente lädt bei Id-Wechsel
neu (`paramMap`). Der Zurück-Pfeil führt in die Gruppe des Eintrags. Notizen stehen im privaten Teil — der Partie-Link
im Notizbuch geht über `gameLessonId` zum Eintrag der Partie.

**Teilen-Link** (Wunsch 2026-11-17, `docs/api/trainingsvideos.md` → „Teilen-Links“): `/tl/:token` und
`/tl/:token/drill` (ohne Guard, `data.mode = 'share'`) öffnen `TrainingLessonComponent` bzw. `TrainingDrillComponent`
über `getShared(token)`; das Video läuft über `sharedVideoUrl(token)` (kein `access-token`). Gast-Ansicht: kein
Zurück-Pfeil zur Liste, kein Knopf zum anderen Teil, kein „Partie öffnen“, kein Teilen-Feld, Hinweis „nur zum Ansehen“;
„Nachspielen“ bleibt unter `/tl/:token/drill` und speichert NICHTS (auch „Erklärung hören“ über das Token). 404 und 410
zeigen dieselbe Seite „Link abgelaufen“ ohne Weg zur Liste. Der Besitzer sieht unter der Stunde
`TrainingSharePanelComponent`: Frist 1/7/30 Tage (Vorgabe 7, `mat-button-toggle-group`), „Link erstellen“ (kopiert gleich),
aktive Links mit Ablauf, „Kopieren“ und „Widerrufen“ (Rückfrage). Admins bei fremden Stunden (`ownerName`) sehen es nicht.

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
- `training-speaker.util.ts` — Sprecherwechsel (Spec `training-speaker.spec.ts`): `turnIndexAt`, `nextTurn` (0,5 s
  Vorlauf), `previousTurn` (Anfang des laufenden Beitrags, in den ersten 3 s der des vorigen — wie ⏮), `speakerBands`
  (Leiste in Prozent der Videolänge), `speakerName` (`sasa` → „Saša“, `patrik` → „Patrik“, Eigennamen, nicht übersetzt).
  Unter dem Video der Stundenseite (auch Gast `/tl/:token`, nicht im Nachspielen — dort laufen nur kurze Ausschnitte):
  Sprecherleiste (Klick = an den Beginn des Beitrags dort, `aria-hidden`), Legende mit dem gerade Sprechenden fett,
  ⏮ „Zum Anfang des Redebeitrags“ und „Nächster Sprecher“ (Tooltip mit Name + Zeit). Ohne `speakerTurns` fehlt alles.
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
- `training-notebook.component.ts` + `training-notebook.util.ts` (Spec daneben) — Trainer-Notizbuch, Link „Notizbuch“ im
  Kopf der Liste. Oben „Sagt er immer wieder“ (Themen vom Server, stellvertretende Notiz + „in n Stunden“, aufklappbar
  zu allen Stellen), dann „Eröffnungsempfehlungen“ (Mini-Brett 160 px, zur Seite am Zug gedreht; Repertoire-Zeile „Im
  Repertoire „X“: Sd2, Sc3“ bzw. „kommt nicht vor“, ohne eigenes Repertoire gar keine Zeile; „Im Repertoire öffnen“ über
  `LineKeyResolver` wie „In welchen Repertoires?“, „Im Analysebrett öffnen“ = `/analysis?fen=`; erst 6, Rest auf Zuruf),
  dann „Alle Notizen“: Suchfeld (vorhandene Volltextsuche `search/text`, gefiltert auf die `noteId` der Notiz-Treffer),
  Auswahl Art (eigene/Lehrpartie/Übung) und Partie (stundenübergreifend: dieselbe RookHub-Partie bzw. dieselben Namen),
  Kategorien als `mat-chip-listbox` mit Anzahl (feste Reihenfolge, nur belegte; ein Filter, der den gewählten Reiter
  leert, springt auf den ersten), darin je Stunde (Datum als Link zur Stunde). Jede Notiz: Text, Partie-Link (eigene
  RookHub-Partie, sonst Stundenseite `?t=`), „▶ anhören“ klappt den Ausschnitt-Player auf (`autoplay`, 5 s vor bis
  60 s nach der Notiz, nie über das Stundenende; nur einer offen) und „Rohzitat“ (eigener Aufklapper).
- `training-clip-player.component.ts` — „Erklärung hören“: EIN `<video preload="none">`, Quelle erst beim ersten Abspielen,
  spult auf den Anfang, hält bei `currentTime ≥ Ende` an; „nur Ton“ blendet das Bild aus (Vorgabe = `EinkService.enabled()`).
  Neue Stellung → hält an. `autoplay` (Notizbuch) startet gleich nach dem Anzeigen — der Klick auf „▶ anhören“ gilt
  noch als Nutzergeste.

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
