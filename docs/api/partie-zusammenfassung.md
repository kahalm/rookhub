# Partie-Zusammenfassung durch Claude (0.913.0)

Wunsch 2026-11-08: „auf Wunsch vom Admin soll Claude auch eine Zusammenfassung der Partie machen – eine eigene Webseite,
die bei der Partie verlinkt ist […] zusätzlich von den Schlüsselstellungen auch noch Diagramme“. Vorbild war der Versuch
mit Vereinspartie 236 (Fable-Erklärungen der Fehler-Vergleiche, 0.908.0).

## Ablauf

1. Ein **Admin** fordert auf der Partieseite (`/club-games/{id}`) „Zusammenfassung anfordern“ an →
   `POST /api/league/club/games/{id}/summary`. Eine Zeile je Partie (`ClubGameSummaries`); neu anfordern geht jederzeit,
   die alte Fassung bleibt lesbar, bis die neue da ist.
2. Der **Formular-Watcher** (`~/claude/formulare-bot/watch.sh`, Schiene `summary`, jede Minute) findet den Auftrag in der
   DB (Status 0, oder „läuft“ länger als 90 min), meldet `POST /api/admin/club-game-summaries/{id}/start` und lässt Claude
   headless schreiben (Modell `SUMMARY_MODEL`, Vorgabe `fable`; kein Anthropic-Schlüssel des Servers). Claude holt die
   Daten mit `fetch-game` über die normalen Lesewege (`GET api/league/club/games/{id}`, `…/evals`, `…/comparisons`),
   prüft Behauptungen mit `board`, Diagramme mit `check-summary` und liefert mit `PUT /api/admin/club-game-summaries/{id}`
   `{ markdown, model }` ab (oder `PUT …/{id}/fail { error }`). Anweisungen: `~/claude/formulare-bot/instructions-summary.md`.
3. Die Partieseite zeigt „Zusammenfassung“ → `/club-games/{id}/summary` (eigene Seite, `ClubGameSummaryComponent`).

## Endpunkte

| Weg | Recht | Zweck |
|---|---|---|
| `GET api/league/club/games/{id}/summary` | `league.view`, Verein der Partie | `{ status: none\|requested\|running\|done\|failed, markdown, doneAt, requestedAt, stale, model, error }` |
| `POST api/league/club/games/{id}/summary` | Admin (sonst 403) | anfordern / neu anfordern |
| `GET api/admin/club-game-summaries` | Admin | offene Aufträge `[{ clubGameId, clubId, requestedAt, requestedByUserId }]` |
| `POST api/admin/club-game-summaries/{id}/start` | Admin | übernehmen; 409 wenn nicht offen |
| `PUT api/admin/club-game-summaries/{id}` | Admin | `{ markdown, model }` abliefern (≤ 200 000 Zeichen) |
| `PUT api/admin/club-game-summaries/{id}/fail` | Admin | `{ error }` |

`stale` = die Partie wurde nach dem Schreiben korrigiert (`MovesHash` weicht ab). Die Zeile geht mit der Partie (Cascade).
Über Teilen-Links gibt es die Zusammenfassung nicht.

## Markdown-Format

Teilmenge, gerendert über das Template (nie `innerHTML`, `summary-markdown.util.ts`): `#`/`##`/`###`, Absätze, Listen
(`-`, `1.`), Zitat `>`, `---`, einfache `|`-Tabellen, `**fett**`, `*kursiv*`, `` `Code` ``. Diagramme:

````
```diagram
ply: 43            Stellung NACH 43 Halbzügen der Partie (0 = Anfang) — die Seite rechnet die FEN selbst
fen: <FEN>         statt ply: Stellung aus einer Variante
arrows: c1a3 a3d6  optional, Feldpaare (grün)
caption: Satz      optional, **fett** geht
orientation: black optional; sonst aus Sicht des eigenen Vereins (ownSide)
```
````

Ein Diagramm mit `ply` außerhalb der Partie wird nicht gezeigt; unter ihm steht „In der Partie zeigen“ (`?ply=`).
