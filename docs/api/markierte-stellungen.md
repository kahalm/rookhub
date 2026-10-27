# Markierte Stellungen („+“, auch ohne Anmeldung)

Der „+“-Knopf (`app-mark-position-button`) markiert eine besonders interessante Stellung — in der Partie-Analyse, am
Analysebrett und im Fehler-Training (0.749.0). Vorrat für ein späteres „Stellungen für alle nachspielen“.

| Methode | Route | Anmeldung | Zweck |
|---|---|---|---|
| GET | `/api/positions/marked` | nein | eigene Markierungen, neueste zuerst |
| POST | `/api/positions/marked` | nein | markieren (idempotent); 400 `{ reason }`: `invalidFen`, `limit`, `noVisitor` |
| DELETE | `/api/positions/marked?fen=` | nein | Markierung zurücknehmen (Zugzähler egal) |
| POST | `/api/positions/marked/claim-session` | ja | `{ sessionId }` — anonyme Markierungen ins Konto → `{ claimed }` |

**Wer markiert hat** (0.847.0, Wunsch 2026-10-26: „auch anonymen Usern zur Verfügung stellen — notiere, wer es war“):
`MarkedPositions.UserId` (Konto) ODER `AnonSessionId` (die anonyme Kennung der App aus `X-Visitor-Id`, dieselbe wie bei
Puzzles/E-Books; genau eines ist gesetzt). Ohne Konto und ohne gültige Kennung: nichts (`noVisitor`). Jede neue
Markierung steht zusätzlich im Protokoll („Stellung markiert von user 5 / anon …“). Aufrufer-Typ: `MarkCaller`
(`Services/Games/`). Je Stellung eine Zeile je Konto bzw. je Kennung (eindeutige Indizes `UserId+PositionKey`,
`AnonSessionId+PositionKey`); Deckel 5 000 je Konto, 500 je Kennung. Eine `savedGameId` zählt nur für ihren Besitzer.

**Beim Anmelden** ruft `LoginFollowUpService` `claim-session` mit der Besucher-Kennung: die anonymen Zeilen wandern ins
Konto; hat das Konto die Stellung schon, bleibt seine Zeile (Herkunft ergänzt) und die anonyme fällt weg. Das Frontend
(`MarkedPositionsService`) hält die Schlüssel je Aufrufer und lädt bei einem Kontowechsel neu.

**In einer Nebenvariante markiert** (0.848.0, Wunsch 2026-10-26: „nicht jede interessante Stellung muss in der Partie vorkommen,
kann auch in einer Variation markiert werden“): `POST` nimmt zusätzlich `variationFromPly` (Halbzug der Partie, an dem die
Variante abzweigt) und `variationUci` (ihre Züge bis zur Stellung, UCI mit Leerzeichen, ≤ 1 000 Zeichen) — nur ohne `ply`
und nur zusammen gültig (`MarkedPositionVariation.Clean`, sonst fallen beide weg); gespeichert in
`MarkedPositions.VariationFromPly`/`VariationUci`, ergänzt wie die übrige Herkunft (nie überschrieben). Die Partieseite
(`SharedGameComponent.markOrigin`) schickt sie, sobald man in der eigenen Nebenvariante der Live-Engine steht. Liste und
Antwort tragen beide Felder mit.

**Verwendet von LeagueHub** (0.848.0): `GET /api/league/club/games/{id}/marks` sammelt die Markierungen ALLER Nutzer an einer
Vereinspartie — direkt (`LeagueClubGameId`) oder über eine Kopie in „Meine Partien“ (`SavedGames.LeagueClubGameId`) — und
gibt nur Stellung, Ort und Anzahl heraus, nie wer markiert hat (`Services/League/ClubGameMarks.cs`, Doku
`leaguehub-vereins-datenbank.md`). Index `MarkedPositions.LeagueClubGameId` (Migration `MarkedPositionVariation`).

**Schlagworte + Kommentar** (0.862.0, Wunsch 2026-10-27: „für das (+) möchte ich die Stellungen auch klassifizieren — Tags
vergeben, außerdem Kommentare dazu“): `MarkedPositions.Tags` (`;`-getrennt, ≤ 400) und `Comment` (≤ 1 000), Migration
`MarkedPositionNotes`. `PUT /api/positions/marked/notes` `{ fen, tags[], comment }` ersetzt beide (404 nicht markiert, 400
`invalid`: > 12 Schlagworte, eines > 30 Zeichen oder mit `;`, Kommentar zu lang). Feste Codes stehen in
`MarkedPositionService.KnownTags` und gespiegelt in `shared/mark-position/mark-tags.ts` (Übersetzung `markPosition.tag.*`);
alles andere ist ein eigenes Wort und bleibt, wie geschrieben (`CleanTags`: getrimmt, ohne Doppel, Codes klein). Beim
Übernehmen ins Konto gewinnen die eigenen Notizen, fehlende kommen von der anonymen Zeile. Oberfläche: Klick aufs „+“
markiert und öffnet `MarkNotesDialogComponent` (Editor `MarkNotesEditorComponent`, geteilt mit LeagueHub); auf einer
markierten Stellung öffnet er nur das Fenster, dort auch „Markierung entfernen“.

**LeagueHub-Stellungsfenster** (0.862.0, Wünsche 2026-10-27: „Klick aufs (+) zeigt nur das Diagramm — wer am Zug,
Klassifizierung, Bemerkung, hier änderbar; ‹ › durch die Stellungen; Überschrift ‚Interessante Stellung aus Partie/aus
Variante‘“, „in der Übersicht je Runde und je Liga +30“, „auch auf der Prognose-Seite ganz oben“):
`GET …/club/games/{id}/marks` trägt je Stellung `tags` (aller Markierenden), `comments` (der ANDEREN, ohne Namen),
`myTags`/`myComment`/`mine`; `PUT …/club/games/{id}/marks/notes` setzt die eigenen und markiert die Stellung dabei selbst
(Halbzug aus der Partie, sonst Abzweig der vorhandenen Markierung) → Liste neu. `lh-marked-positions-dialog`
(`openMarkedPositions(dialog, games[])`) zeigt die Stellungen einer oder vieler Partien; „In der Partie zeigen“ klappt das
Nachspielen des Bretts auf (`BoardGameComponent.showAt`). „+n“ steht an jeder Partie, in der Übersicht je Liga und Runde und
in den Aufstellungen einer Runde ganz oben (`GameMarkers.markTotal/markedBoards`). Über den Teilen-Link ohne Konto springt das
„+“ an der Partie wie bisher direkt ins Nachspielen.

**Frühere Fassungen** (0.862.1, Wunsch 2026-10-27: „merk dir beim Bearbeiten auch die alten, damit ich ggf. Gelöschtes
wiederherstellen kann“): Bevor `PUT …/notes` Schlagworte/Kommentar ändert oder `DELETE` die Markierung entfernt, hebt
`MarkedPositionService.KeepRevisionAsync` die bisherige Fassung in `MarkedPositionNoteRevisions` auf (Migration
`MarkedPositionNoteRevisions`) — am Aufrufer (Konto/anonyme Kennung) und `PositionKey`, nicht an der Markierung, damit der
Verlauf das Entfernen überlebt; leere Notizen und unveränderte Saves erzeugen keine Fassung, je Stellung höchstens 30.
`GET /api/positions/marked/notes/history?fen=` → `[{ id, tags, comment, createdAt }]`, neueste zuerst (anonym erreichbar wie
die übrigen, nur die eigenen). Der Editor (`MarkNotesEditorComponent`, RookHub und LeagueHub) klappt „Frühere Fassungen“ auf;
„Wiederherstellen“ übernimmt eine in die Felder, gespeichert wird mit „Speichern“. Löschen des Kontos löscht den Verlauf mit,
`claim-session` nimmt ihn ins Konto mit.
