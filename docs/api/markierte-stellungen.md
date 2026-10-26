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
