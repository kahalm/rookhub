# Meisterpartien-Suche (`/api/library-games/search`, Seite `/master-games`, 1.038.0)

Suche über den ganzen Rohbestand (`LibraryGames`, ~130 000 kommentierte Meisterpartien aus Büchern und Sammlungen,
siehe `rohbestand.md`) — nicht nur über die eigenen Partien. Dienst: `Services/Library/LibraryGameSearchService.cs`
(Eingaberegeln in `LibraryGameSearchInput`), Oberfläche `features/master-games/`, Menüpunkt `master-games` (Vorgabe
`All`, Gruppe „Analyse & Sammlung“).

| Methode | Endpoint | Auth | Zweck |
|---|---|---|---|
| GET | `/api/library-games/search?q=&player=&event=&yearFrom=&yearTo=&result=&eco=&sort=&page=&pageSize=` | **AllowAnonymous** + RL `anonymous-puzzle` | Eine Seite Treffer (Kopfdaten, OHNE Züge), dazu `total`, `maxPage`, `ignored` |
| GET | `/api/library-games/{id}/view?lang=` | Auth | (bestehend) die Partie mit Anmerkungen — das öffnet ein Klick auf eine Trefferzeile im Nachspiel-Dialog |

**Sichtbarkeit wie die bestehende Bestandssuche** (`GET /api/library-games`): Kopfdaten für jeden, Dubletten und
Aussortiertes fallen heraus; die Züge gibt es erst angemeldet über `/view`. Ohne Konto führt der Klick zur Anmeldung.

## Filter

| Parameter | Bedeutung | Spalte / Index |
|---|---|---|
| `q` | Freitext über Spieler, Turnier, Kommentator | Volltext `FT_LibraryGames_SearchText` |
| `player` | Spieler, egal welche Farbe; mehrere Wörter je bei Weiß ODER Schwarz („Carlsen Anand“ = gegeneinander) | Volltext, danach `LIKE` auf `White`/`Black` der wenigen Volltext-Treffer |
| `event` | Turnier (`[Event]`) | Volltext, danach `LIKE` auf `Event` |
| `yearFrom`/`yearTo` | Jahr bzw. Bereich (vertauscht wird gerade gerückt, geklemmt 1000–2100) | `PlayedYear` (neu, Index) |
| `result` | `white`/`black`/`draw` (auch `1-0`/`0-1`/`1/2-1/2`) | `Result` |
| `eco` | Code `B90`, Präfix `B9`/`B`, Bereich `B20-B99` | `Eco` (neu indiziert) |
| `sort` | `best` (Eignungsnote, Vorgabe), `newest`, `oldest` | `Score` bzw. `PlayedYear` (+ Id) |

Alle Wörter der drei Textfelder gehen in EINEN Volltext-Ausdruck (`+wort*` je Wort, Wortanfang). Deckel:
`MaxFieldLength` 100 Zeichen je Feld, `MaxTerms` 8 Wörter, `pageSize` ≤ 50 (Vorgabe 25), geblättert höchstens bis
Zeile `MaxOffset` 5 000 (`maxPage`; `total` zählt trotzdem alles). Nicht gefiltert — und in `ignored` gemeldet — wird
mit Wörtern unter drei Zeichen (`innodb_ft_min_token_size`), InnoDB-Stoppwörtern („the“, „for“, „und“ … stehen nicht im
Index; ein Pflicht-Wort `+the` fände NIE etwas), Wörtern über dem Deckel, unlesbarem ECO-Code und unbekanntem Ergebnis.

## `PlayedYear` (Migration `LibraryPlayedYear`)

`PlayedOn` ist bei Partien mit unvollständigem Datum („1904.??.??“) leer — am echten Bestand 25 612 von 130 572. Die
neue Spalte `PlayedYear` (smallint, Index) trägt das Jahr aus `[Date]`, sonst `[EventDate]`
(`LibraryGameReader.YearOf`, beim Einlesen); die Migration trägt den Bestand per SQL aus den ersten 4 000 Zeichen des
PGN nach (gemessen 2,5–8 s). Danach haben alle 130 572 Zeilen ein Jahr (1610–2025). Dazu kam ein Index auf `Eco`.

## Gemessen (Kopie des Prod-Bestands, 130 572 Zeilen, 2026-11-28)

| Abfrage | Zählen | Seite |
|---|---|---|
| Spieler „carlsen“ | 7 ms | 5 ms |
| Jahr 1920–1930 | 6 ms | 14 ms |
| ECO `B90` / `B` | 3 / 104 ms | 3 / 1 ms |
| Ergebnis allein | 70–110 ms | < 1 ms |
| ECO `B9` + Jahr + Remis | 19 ms | 131 ms |
| ungünstigste Kombination (seltener Filter, nach Note sortiert: Optimierer läuft den Score-Index ab) | 120 ms | 0,38 s |

Tests: `tests/RookHub.Api.Tests/Library/LibraryGameSearchServiceTests.cs` (InMemory: Regeln, Deckel, Sichtbarkeit),
`tests/RookHub.Api.IntegrationTests/LibraryGameSearchSqlTests.cs` (MariaDB: MATCH, Stoppwort, ECO-Bereich),
`features/master-games/master-games.component.spec.ts`.
