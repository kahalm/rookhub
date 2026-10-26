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
