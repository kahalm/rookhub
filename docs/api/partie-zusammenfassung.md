# Partie-Zusammenfassung durch Claude (0.913.0)

Wunsch 2026-11-08: „auf Wunsch vom Admin soll Claude auch eine Zusammenfassung der Partie machen – eine eigene Webseite,
die bei der Partie verlinkt ist […] zusätzlich von den Schlüsselstellungen auch noch Diagramme“. Vorbild war der Versuch
mit Vereinspartie 236 (Fable-Erklärungen der Fehler-Vergleiche, 0.908.0).

## Ablauf

1. Ein **Admin** fordert auf der Partieseite (`/club-games/{id}`) „Zusammenfassung anfordern“ an →
   `POST /api/league/club/games/{id}/summary`. Eine Zeile je Partie (`ClubGameSummaries`); neu anfordern geht jederzeit,
   die alte Fassung bleibt lesbar, bis die neue da ist.
2. **Erst wenn die Hintergrund-Rechnung durch ist** (0.914.0, Wunsch 2026-11-08): Hauptanalyse fertig, Vertiefung auf
   Tiefe 30 fertig (`RefinedAt`) und — bei `ClubMistakeComparisons:Enabled` — alle Fehler-Vergleiche angelegt
   (`MistakeComparisonsAt`) und gerechnet (fertig oder gescheitert). Eine gescheiterte Analyse blockiert nicht.
   `ClubGameSummaries.ReadinessAsync`; die Warteschlange gibt nur bereite Partien her, `start` lehnt andere mit 409 ab.
   Bis dahin liefert `GET …/summary` bei offener Anforderung `readiness { ready, hasAnalysis, analysisDone, refining,
   refinedPositions, totalPositions, comparisonsPlanned, comparisonsDone, comparisonsTotal }`; die Kopfzeile zeigt
   „wartet auf die Hintergrund-Analyse … Vertiefung 56/62 Stellungen · Fehler-Vergleiche 0/11“ und fragt jede Minute nach.
3. Der **Formular-Watcher** (`~/claude/formulare-bot/watch.sh`, Schiene `summary`, jede Minute) findet den Auftrag in der
   DB (Status 0/1 in der DB als billige Vorprüfung, dann `GET api/admin/club-game-summaries`), meldet `POST /api/admin/club-game-summaries/{id}/start` und lässt Claude
   headless schreiben (Modell `SUMMARY_MODEL`, Vorgabe `fable`; kein Anthropic-Schlüssel des Servers). Claude holt die
   Daten mit `fetch-game` über die normalen Lesewege (`GET api/league/club/games/{id}`, `…/evals`, `…/comparisons`),
   prüft Behauptungen mit `board`, Diagramme mit `check-summary` und liefert mit `PUT /api/admin/club-game-summaries/{id}`
   `{ markdown, model }` ab (oder `PUT …/{id}/fail { error }`). Anweisungen: `~/claude/formulare-bot/instructions-summary.md`.
4. Die Partieseite zeigt „Zusammenfassung“ → `/club-games/{id}/summary` (eigene Seite, `ClubGameSummaryComponent`).
   Seit 0.928.0 steht daneben ein Seitenbrett (`SummaryGameBoardComponent`): Hauptlinie mit Zugliste, Knöpfen und ←/→, „Am Brett zeigen“ an einem Diagramm springt auf dessen `ply`. Rechnet aus dem schon geladenen PGN, keine eigene Anfrage.

## Endpunkte

| Weg | Recht | Zweck |
|---|---|---|
| `GET api/league/club/games/{id}/summary` | `league.view`, Verein der Partie | `{ status: none\|requested\|running\|done\|failed, markdown, doneAt, requestedAt, stale, model, error }` |
| `POST api/league/club/games/{id}/summary` | Admin (sonst 403) | anfordern / neu anfordern |
| `GET api/admin/club-game-summaries` | Admin | offene Aufträge `[{ clubGameId, clubId, requestedAt, requestedByUserId }]` |
| `POST api/admin/club-game-summaries/{id}/start` | Admin | übernehmen; 409 wenn nicht offen |
| `PUT api/admin/club-game-summaries/{id}` | Admin | `{ markdown, model }` abliefern (≤ 200 000 Zeichen); auch ohne `start` (Handlauf), aber nur, wenn es die Zeile schon gibt (sonst 404, R4-005) |
| `PUT api/admin/club-game-summaries/{id}/fail` | Admin | `{ error }` |
| `GET api/league/s/{token}/club/games/{id}/summary` | anonym über den Teilen-Link (Partie muss zum Link gehören, sonst 404) | nur der fertige Text: `{ status: done, markdown, doneAt, stale }` bzw. `{ status: none }` — ohne Warte-Stand, Modell und Fehler |

`stale` = die Partie wurde nach dem Schreiben korrigiert (`MovesHash` weicht ab). Die Zeile geht mit der Partie (Cascade).
Seit 09.11. gibt es die FERTIGE Zusammenfassung auch über den Teilen-Link (`LeagueClubController` unter
`api/league/s/{token}/club`, Route der App `s/:token/club-games/:id/summary`, `ClubGameSummaryComponent` im
`shareToken`-Modus): lesend, ohne Anfordern; Warte-Stand, Modell und Fehlertext bleiben den Vereinsmitgliedern vorbehalten.
Wer den Link hat, liest also den Text der Zusammenfassung.

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

## Vereinspartien, auf die jemand wartet (0.922.0)

Die Hauptanalyse einer Vereinspartie legt der Stapel an (`MasterAnalysisScheduler`), also nur außerhalb der Sperrzeiten
und wenn Plätze frei sind. Eine angeforderte Zusammenfassung stand deshalb tagsüber still (Partie 239 am 09.11., Montag
früh hochgeladen). Jetzt legt der Takt VOR beiden Sperren die Analyse jeder Partie an, auf die jemand wartet (offene
Zusammenfassung oder Eintrag in „Meine Partien“), als `GameAnalysis.Expedited`: Herkunft und Listen bleiben, gerechnet wird
aber wie „Partie analysieren“ (`GameAnalysisOrigins.RunsAsBatch`). Eine schon im Stapel wartende Analyse mit offener
Zusammenfassung wird samt eingereihter Aufträge umgestellt. Der Nachtrag läuft beim nächsten Takt von selbst.

Archivierte Vereinspartien (durch ein neueres Formular ersetzt, `ArchivedAt`) haben bewusst keine Analyse mehr.

## Eigene Partien (0.923.0)

Der Besitzer fordert auf `/games/:id` an (`POST /api/games/{id}/summary`, `{ lang }`), gelesen wird auf
`/games/:id/summary` (`GET /api/games/{id}/summary`), angefordert nur von ihm. `OwnGameSummary`, je Partie eine Zeile.

Verbundene Vereinspartie (0.954.0): `GET /api/games/{id}/summary` liefert, solange die eigene Partie keine eigene Zusammenfassung mit Text oder offener Anforderung hat, den Stand der verbundenen Vereinspartie (`source: club`) — angefordert/beim Schreiben mit `readiness` und `runningSeconds`, fertig mit Text — und `clubGameId`, wenn der Nutzer die Vereinspartie sehen darf (Admin oder `league.view` + Mitglied ihres Vereins); die Kopfzeile zeigt dann Sanduhr + Zähler bzw. den Link nach `/club-games/{id}/summary` statt „Zusammenfassung anfordern“. Ohne diese Rechte nur ein fertiger Text, ohne `clubGameId` (Entscheidung E7, wie bisher), nichts Offenes. Eine gescheiterte eigene Anforderung ohne Text tritt hinter eine Vereins-Zusammenfassung zurück. Gleiche Regel in der Liste (`docs/api/gespeicherte-partien.md`, „Zusammenfassung in der Liste“).

Teilen-Link (0.934.0): `/g/:token/summary` liest `GET /api/games/shared/{token}/summary` (ohne Anmeldung, globaler IP-Limiter) → `{ status: done|none, markdown, doneAt, stale, model }`, 404 bei unbekanntem Link. Ohne Text `none`, auch wenn eine Anforderung offen oder gescheitert ist; ein früherer Text bleibt sichtbar. Nichts zu Kontingent, Anforderung, Fehler, und **kein** Rückfall auf den Text einer verbundenen Vereinspartie (der gehört nicht zu diesem Link). Wer den Link hat, liest damit den Text — auch den an den Besitzer gerichteten Schluss.

- Kontingent: `OwnGameSummaries:DailyPerUser` (Vorgabe 2) je Konto und UTC-Tag, Admins frei. Eine noch offene Anforderung
  kostet nichts; verbraucht → 429 `code: summary_quota`. Die Zählung steht in `OwnGameSummaryRequests` und überlebt das
  Löschen der Partie. Gebucht wird VOR der Analyse und danach nachgezählt (`BookAsync`, R4-004: über dem Deckel → zurück,
  von parallelen Anforderungen kommt die früher geschriebene durch); startet die Analyse nicht, geht die Buchung zurück.
- Züge geändert (Korrigieren, Heilen per Re-Save; `SavedGameCorrection.MovesChangedAsync`, R4-013): eine offene Anforderung
  bricht ab (`failed`, `error: "movesChanged"`), die Buchung von heute geht zurück ins Kontingent; ein fertiger Text bleibt
  und gilt als veraltet.
- Fehlt die Analyse, stößt das Anfordern „Partie analysieren“ an. Nur Partien ab der Grundstellung (400 `summary_unsupported_start`).
- Solange die Anforderung offen ist, bekommt die Analyse Fehler-Vergleiche (`ClubMistakeComparisons.OwnSummaryAnalyses`, vor
  dem Altbestand) und Fable-Begründungen (gemeinsamer Tagesdeckel). Ohne Anforderung gibt es beides für eigene Partien nicht.
- Bereitschaft wie bei Vereinspartien (`ClubGameSummaries.ReadinessOfAnalysisAsync`).
- `GET /api/games/{id}/comparisons[/{comparisonId}]` liest die Vergleiche für den Besitzer; Vergleichsseite mit `?ownGame=`.
- Verbundene Vereinspartie ohne eigenen Text: `GET …/summary` liefert den Vereinstext mit `source: "club"`.

Watcher: `GET /api/admin/own-game-summaries` → `[{ savedGameId, userId, requestedAt }]`, dazu `POST …/{id}/start`,
`PUT …/{id}`, `PUT …/{id}/fail`, `GET …/{id}/game|evals|comparisons` (Form wie bei Vereinspartien). Abliefern und Fehlschlag
nur für einen laufenden Auftrag (nach `start`), sonst 409 (R4-005); `game` gibt Namen und Event ohne Steuerzeichen und auf
100 Zeichen gekürzt (`OwnGameSummaries.WatcherText`) — sie landen im Claude-Prompt. In `watch.sh` und `bin/*` trägt
eine eigene Partie die Kennung `o<id>`; gegen eine ältere API (404, fehlende Tabelle) bleibt die Schiene bei den Vereinspartien.


**Zähler (0.926.1, ab 0.933.1 auch beim Warten, Phasen seit 2026-11-09)**: Die Stand-Antwort trägt `runningSeconds`, vom Server gerechnet: bei `requested`, solange `readiness.ready` falsch ist, die Sekunden seit `RequestedAt`; bei `requested` mit fertiger Rechnung `null` (kein Zähler, Anzeige „in der Warteschlange“; ohne Migration, einen Zeitpunkt „Rechnung fertig“ gibt es nicht); bei `running` die Sekunden seit `StartedAt` (beim LLM beginnt die Zeit bei 0), sonst `null`. Die Kopfzeile zählt daraus je Sekunde hoch und fragt, solange etwas offen ist, alle 10 s nach dem Stand.

## Merksätze und Empfohlene Literatur (0.929.0)

Claude schreibt `> **Merksatz:** <kennung>` (nur aus `merksaetze.md`, siehe `docs/api/merksaetze.md`); die Seite zeigt den festen Wortlaut mit (i), Unbekanntes (alte, erfundene Sätze) gar nicht. Letzter Abschnitt „Empfohlene Literatur“: 2–3 `book`-Blöcke mit `id:` und `why:`, nur Bücher aus den Merksatz-Belegen (`EbookSuggestions`). `GET /api/admin/ebook-suggestions/candidates?game=237|o143` (Watcher, ohne Sperren des Anfordernden), `GET /api/ebook-suggestions?ids=` (offen: Titel, `accessible`, `blocked`), `PUT|DELETE /api/ebook-suggestions/{id}/block` (je Konto, Tabelle `EbookSuggestionBlocks`).

## Literatur je Seite und Spielstärke (0.931.0)

Wunsch 2026-11-09: „die empfehlungen sollen getrennt für weiß & schwarz sein“. Unter `## Empfohlene Literatur` stehen `### Für Weiß` und `### Für Schwarz` mit je 2–3 `book`-Blöcken, passend zu Stärke und Fehlern dieses Spielers; `check_summary.py` verlangt das, sobald `buecher.md` Bücher hat. Eine Überschrift verschwindet auf der Seite, wenn unter ihr nur gesperrte Bücher stehen (`visibleSummaryBlocks`).

`GET /api/admin/ebook-suggestions/strength?game=237|o143` (Admin) → `{ white, black }`, je Seite `{ elo, band }` oder `null`; unbekannte Partie 404. Reihenfolge: Elo der Partie → Elo der Ligapaarung → Meldeliste über die FIDE-ID (Saison am nächsten zum Partiejahr). Die anonymisierte Seite kommt nur als `band` („1700–1799“, `elo` = `null`), nie mit Name oder FIDE-ID; intern zählen dafür `*RealFide` und, ohne feste Zuordnung, die einzige genaue Paarung des `LeaguePairingFinder` (nichts wird gespeichert). Der Text nennt die Stärke dieser Seite nicht (Vorgabe an Claude, `check_summary.py` weist das Band im Text ab). Eigene Partien: nur die gespeicherte Elo.

## Klickbare Züge und Varianten am Seitenbrett (0.932.0)

Züge im Text (Überschriften, Absätze, Listen, Zitate, Tabellen, Bildunterschriften) werden über `linkSummaryMoves` klickbar, sobald es das Seitenbrett gibt. Nummer + Seite treffen zuerst die zuletzt genannte Stellung, wenn sie neben der Partie liegt (Kontext einer Variante), dann den tatsächlichen Partiezug, dann zuletzt genannte Stellungen des Absatzes, dann die Partiestellung als Abzweig (`explanationParts(text, root, game, shared)`); liegt die angesetzte Stellung bis zu 6 Halbzüge vor der Folge, füllt der zuletzt genannte Zug gleicher Nummer und Seite die Lücke, sonst der Partiezug (wie im Zugvergleich, `docs/frontend/eigene-fehler-nachspielen.md`). Illegales bleibt Text. Eine Folge, die die Partie verlässt, wird auf dem Brett zur Variante ab dem Abzweig. Eigene Züge auf dem Brett ergeben ebenfalls eine Variante. Varianten liegen nur im Speicher der Seite, ohne Schreibzugriff, und verschwinden beim Blättern in der Partie, beim Neuladen oder Verlassen.

## „Die Partie“ und Brett am Handy (0.959.0)

> Seit 0.963.0 gilt die eingesetzte Zugliste nur noch für Zusammenfassungen ohne verschmolzenen Abschnitt (siehe unten).

Wunsch 2026-11-13: „ich hätt gern dass die zusammenfassung auch die ganze partie abbildet … zum durchklicken“. Die Seite
setzt den Block **„Die Partie“** (`games.summary.theGame`) selbst ein — aus dem PGN der Partie, nicht aus dem Markdown;
er gilt damit für jede bestehende Zusammenfassung. Platz (`gameBlockIndex` in `summary-markdown.util.ts`): hinter den
Abschnitt `## Zusammenfassung` (Überschrift auch in anderen Sprachen, „Summary“ …), also vor die nächste `##`; fehlt er,
hinter die Kopfzeile unter dem `#`-Titel, ohne Titel an den Anfang. Inhalt: Zugpaare (`gameMovePairs`, Nummer aus der FEN
vor dem Zug — eine Partie ab FEN mit Schwarz am Zug beginnt mit „17…“), jeder Halbzug ein Zug-Knopf (`.gm`, Figurenbuchstaben
der Oberflächensprache über `SanPipe` wie die Zugliste des Bretts), am Ende das Ergebnis (nicht bei `*`). Der Zug, den das
Seitenbrett zeigt, ist hervorgehoben. Leeres oder ungültiges PGN → kein Brett, kein Block. Zug-Klassen (!!/!) trägt der
Block nicht: die Seite lädt keine Analyse.

**Am Handy (unter 900 px)** springt ein Klick auf einen Zug (Text, „Die Partie“, „Am Brett zeigen“) nicht mehr zum
Seitenbrett am Ende, sondern öffnet `SummaryBoardSheetComponent` — `MatDialog` als Blatt nach DESIGN 5.6, darin dasselbe
`SummaryGameBoardComponent` (`inSheet`, Startsprung `jump`) mit Blättern, Zugliste, Varianten und eigenen Zügen. Schließen
per X, Hintergrund, Esc, Wischen am Griff nach unten oder Zurück-Taste (eigener Verlaufseintrag). Leseposition und
Seitenbrett bleiben unverändert. **Engine**: Knopf `memory` am Brett (Desktop und Blatt; im Blatt beim Öffnen an) —
`LiveEngineSession` mit Stockfish im Browser, rechnet die Stellung auf dem Brett, drei Linien + Pfeil. Die Engine ist eine
statische Datei der App, läuft also auch über die Teilen-Links ohne Anmeldung; externe Engines bietet die Seite nicht an.

## „Die Partie“ als ein Text (0.963.0)

Wunsch 2026-11-14: „ich mag nicht einfach die zugliste aufgelistet bekommen sondern es soll die partie mit dem ganzen ersten
block gemerged werden – sprich sowas wie die Partie begann als Französisch: e4 e6 … und dann alle züge immer wieder
unterbrochen von text und untervarianten“ (Mockup `~/claude/formulare-bot/mockups/partie-244-verschmolzen.html`).

**Format** (kein neues Markdown-Element, keine Migration): der Watcher schreibt statt `## Zusammenfassung` den Abschnitt
`## Die Partie` als gewöhnliche Absätze. Darin stehen ALLE Partiezüge der Reihe nach mit Zugnummer im Fließtext, dazwischen
Erklärung; Untervarianten in runden Klammern; Zugzeichen (`!!`, `!`, `?`, `??`, `?!`) direkt am Partiezug:

```
## Die Partie

Dann stellte 18.Dd4 die Dame dem Turm d8 gegenüber, dazwischen steht nur der Springer d5. (Besser 18.Sbd4 La4 19.Da5 Ld7
20.0-0 b6 21.Da6 mit etwa gleicher Stellung, −0,2) Schwarz wich dem Tausch aus und griff zugleich g2 an: 18...Dg6 19.0-0
Sc3!! Der Abzug öffnet die d-Linie und die Diagonale c6–f3 zugleich.
```

**Seite** (`summary-game-text.util.ts`, `mergeGameSection`): findet den Abschnitt (`## Die Partie`, auch „Partieverlauf“,
„The game“) und liest seine Absätze gegen das PGN — auf die Schreibweise verlässt sie sich nicht:

- Eine Zugfolge beginnt an einer Zugnummer (`12.Sd3`, `12...b6`, `12…b6`) und läuft, solange legale Züge folgen
  (`readSequence` aus `explanation-lines.ts`, deutsche und englische Figurenbuchstaben).
- Außerhalb von Klammern setzt sie an der Partiestellung dieser Zugnummer an; in einer Klammer zuerst an einer Stellung, die
  dieselbe Klammer schon erreicht hat (Fortsetzung/Abzweig der Variante), sonst an der Partiestellung. Mit der schließenden
  Klammer endet der Zusammenhang.
- **Partiezug** ist, was von dort Zug für Zug der Partie folgt (trägt seinen Halbzug `ply`); alles andere ist
  **Variantenzug** (trägt die Zugfolge ab Partieanfang). Was nirgends legal ist (Tippfehler), bleibt Text.
- Block `gameText` mit Läufen `{ variation, parts }`: eine Klammer MIT Zügen ist ein Varianten-Lauf (kleiner, kursiv,
  gedämpft), eine ohne („(−3,9)“) bleibt Text. Zugzeichen werden vom Zug getrennt und färben ihn (`!`/`!!` Erfolg, `?`/`??`/`?!`
  Fehler, `!?` ohne Farbe). Das Ergebnis setzt die Seite hinter den letzten Partiezug.
- Klick: Partiezug → `showOnBoard({ ply })` (am Seitenbrett hervorgehoben), Variantenzug → `showOnBoard({ line, at })`;
  ab 900 px Seitenbrett, darunter das Blatt mit Engine — wie bisher. Gerendert über das Template, nie `innerHTML`.
- **Alte Zusammenfassungen**: ohne den Abschnitt — oder wenn er weniger als die Hälfte der Partiezüge enthält — bleibt alles
  wie bisher, die Seite setzt die Zugliste (Abschnitt oben) ein. Mit verschmolzenem Abschnitt entfällt die Zugliste.

**Watcher** (`~/claude/formulare-bot`): `lib/game_text.py` prüft vor dem Abliefern streng (`check-summary`): genau ein
Abschnitt `## Die Partie`, kein `## Zusammenfassung`; außerhalb von Klammern nur Partiezüge, jeder genau einmal, lückenlos
und in Reihenfolge; in Klammern nur legale Züge, ohne Zugzeichen; Zugzeichen am Partiezug genau wie die Zug-Klasse der
Partieseite (`classes.txt`: B `!!`, G `!`, M `?`, P `??` Pflicht, I `?!` erlaubt), sonst keins; kein Ergebnis im Text; keine
Zeile, die als nummerierte Liste gelesen würde (`1. Sf3`). Hinweis (kein Fehler) ab 20 Halbzügen am Stück ohne Text.
`digest.md` zeigt dafür jetzt alle Zugzeichen am Zug. Tests: `python3 -m unittest discover -s tests` dort.

**Reihenfolge beim Ausrollen**: Die alte Seite (vor 0.963.0) zeigt einen neuen Text zwar lesbar (Züge als `.mv`-Knöpfe), setzt
aber zusätzlich die Zugliste ein — deshalb liegen die neuen Bot-Dateien als `*.neu` bzw. `*_merged.py` daneben und werden erst
nach dem Prod-Deploy mit `~/claude/formulare-bot/aktiviere-verschmolzen.sh` eingeschaltet (tauscht `instructions-summary.md`,
`bin/check-summary`, `bin/fetch-game` per `mv`, Sicherung als `.bak-<Datum>`; `watch.sh` bleibt unberührt).

## Teilen-Link einer Vereinspartie (`/cg/{token}`, 2026-11-09)

„Teilen-Link kopieren“ auf `/club-games/{id}` (Recht `league.view`, wie „Partie teilen“ der Ligapartie) legt `LeagueClubGames.ShareToken` an; gültig bis zum Widerruf (⋮ → „Teilen-Link widerrufen“, Token wird gelöscht, ein neues Teilen vergibt ein neues) oder bis die Partie archiviert wird.

| Endpunkt | Wer | Antwort |
| --- | --- | --- |
| `GET/POST/DELETE api/league/club/games/{id}/share` | `league.view`, Verein der Partie | `{ token }` bzw. 204 |
| `GET api/league/s-club-game/{token}` | anonym | nur Namen, Elo, Ergebnis, Jahr, PGN, Event, eigene Seite (keine Id, keine Paarung) |
| `…/evals`, `…/comparisons`, `…/summary` | anonym | Bewertungen; Vergleiche ohne Titel/Fehler; nur fertiger Text (ohne Modell und Stand) |

Anonymisierte Partie: PGN neu gebaut (nur Hauptvariante, ohne Event und Elo der ersetzten Seite); `Event` leer. Im Text der Zusammenfassung werden „Runde N“/„Brett N“ gelöscht (der Watcher bekommt `leagueGameLabel`); steht ein interner Name (jeder Namensteil ab drei Zeichen) oder die interne FIDE-ID darin, gibt es `none`. Die Seite reicht bei einem Treffer keinen Text heraus — lieber keine Zusammenfassung als ein Klarname.

## Empfohlene Übungen (0.960.0)

Wunsch 2026-11-13: „am ende eine kleine sammlung von empfohlenen übungen … z. B. M2/M3 mit so motiven (Df7 Kf6 und bauer auf f4, aber
kann dann natürlich auch ein springer sein der das deckt)“; „direkt auf der seite lösbar“; „erzwinge keine motive/aufgaben — wenn es bei
einer partie nichts gibt ists auch vollkommen in ordnung“. Die Seite setzt den Abschnitt selbst hinter den Text (nach der Literatur) —
aus den Daten der Partie, ohne Claude, also auch für alle schon fertigen Zusammenfassungen.

**Stellen** (`GameExercises.Occurrences`, beide Seiten): *verpasstes Matt* — die Seite am Zug hatte laut Analyse Matt in ≤ 5, nach ihrem
Zug steht der Gegner nicht mehr matt bzw. die Partie endet ohne Matt (Bild aus der Engine-Linie); *gespieltes Matt* — die Partie endet
matt (Bild aus der Schlussstellung, Stelle = Beginn der Mattfolge). Gleiches Bild mehrfach → eine Gruppe (`alsoPlies`), höchstens 8.
Seit 0.961.0 auch **taktische Fehler**, seit 0.962.0 zuerst **Buchbeispiele** (beides unten).

**Mattbild** (`Services/Puzzles/MatePattern.cs`, Regelstand `Version`): Schlussstellung relativ zum mattgesetzten König, farb- und
spiegelunabhängig — Lage des Königs (Ecke/Grundreihe/Rand/offen), schachgebende Figur(en) mit Lage (Kontakt diagonal/gerade, Linie,
Springer, Bauer) und Deckung, die acht Nachbarfelder (Rand / eigener Stein / vom Angreifer beherrscht). **Streng** = mit den Typen der
deckenden und beherrschenden Figuren und dem genauen Versatz, **Familie** = ohne Typen: „Dame g7 gedeckt vom Bauern f6“ und „… vom
Springer h5“ sind dieselbe Familie.

**Auswahl je Stelle** (bis 6): gleiche strenge Signatur, dann gleiche Familie; NUR wenn beides nichts liefert, Aufgaben des Themas
`mateInN` (auch solange die Signaturen noch nicht berechnet sind). Rating-Fenster ±250 um die Stärke der Seite, die das Motiv hatte
(`EbookSuggestions.StrengthAsync`, ein Band zählt mit seiner Mitte; sonst die andere Seite, sonst 1500), Beliebtheit ≥ 80, ≥ 200 mal
gespielt, Tipps nicht als irreführend markiert. Je Partie und Stelle fest gesät — derselbe Aufruf liefert dieselben Aufgaben. Keine
Stelle → `groups: []`, die Seite zeigt nichts.

| Weg | Recht |
|---|---|
| `GET api/league/club/games/{id}/summary/exercises` | wie `GET …/summary` (`league.view`, Verein der Partie) |
| `GET api/league/s/{token}/club/games/{id}/summary/exercises` | anonym über den Liga-Teilen-Link (Partie der Begegnung) |
| `GET api/league/s-club-game/{token}/summary/exercises` | anonym über den Link der Vereinspartie |
| `GET api/games/{id}/summary/exercises` | Besitzer |
| `GET api/games/shared/{token}/summary/exercises` | anonym über den Teilen-Link |

Antwort `{ groups: [{ ply, white, reason: missedMate|playedMate, mateIn, alsoPlies, exercises: [{ id, fen, moves, rating, themes,
match: strict|family|theme, source: lichess|book, book }] }] }` — Lichess: `fen` vor dem Zug des Gegners, `moves` = Gegnerzug + Lösung
(Lichess-Form), `book` = `null`; Buch (0.962.0) siehe „Buchbeispiele“. Ohne Analyse leer.

**Mattbilder berechnen** (`PuzzleSignatureWorker`, Tabelle `PuzzleSignatures`): startet 5 min nach dem API-Start von selbst, je 3 s
1 000 Matt-Aufgaben (Beliebtheit ≥ 80) in Id-Reihenfolge, Fortschritt `SystemSettings.PuzzleSignatures.MateCursor` (`v1:<PuzzleId>`,
übersteht Neustarts); pausierbar über „Hintergrundjobs pausieren“; ruht in `PuzzleSignatures:QuietHours` (Vorgabe
`Mon-Sun 00:40-01:40; Mon-Sun 04:15-05:00`, Europe/Vienna — Sicherung und Datenbank-Abzug); `PuzzleSignatures:Enabled=false` schaltet
ab. Prod (13.11.): ~1,25 Mio. Aufgaben → ~1 250 Schritte ≈ 1,5–2 h. Danach sieht er alle 30 min nach neuen Aufgaben. Neuer
`MatePattern.Version` → alles wird neu gerechnet.

### Taktische Motive (0.961.0)

Wunsch 2026-11-13: „auch andere Fehler taktischer Natur“, „positionelle Fehler ohne taktisches Motiv bekommen keine Übungen“.
`Services/Puzzles/TacticMotifs.cs` — eigene Umsetzung nach den dokumentierten Lichess-Themen (lichess.org/training/themes); der
Lichess-Tagger `lichess-puzzler/tagger/cook.py` steht unter **AGPL-3.0** und ist NICHT kopiert. Erkannt am ersten Zug der Lösung:
`doubleCheck`, `fork` (Ziele: König, wertvoller oder ungedeckt; kein billigerer Angreifer auf der Gabelfigur; der nächste eigene Zug
schlägt ein Ziel), `skewer` (König/Dame vorn, der nächste eigene Zug schlägt dahinter), `pin` (Leichtfigur oder mehr vor König/Dame,
der nächste eigene Zug schlägt sie), `discoveredAttack` (die gezogene Figur stand zwischen eigener Langschrittlerin und Ziel; Abzugsschach
sofort, sonst muss die Lösung das Ziel holen), `hangingPiece` (kurze Lösung schlägt ungedeckte Leichtfigur oder mehr), `promotion`.
Nicht erkannt (unscharf): Ablenkung, Hinlenkung, Verstellung, Räumung, stiller Zug, Zugzwang, gefangene Figur; `capturingDefender` und
`advancedPawn` waren angelegt, stimmten an 51 714 Prod-Aufgaben aber nur zu 23 % bzw. 55 % mit dem Lichess-Thema überein und sind raus.
Übereinstimmung der übrigen mit dem Lichess-Thema (13.11.): Gabel 87 %, Abzug 93 %, Doppelschach 100 %, Umwandlung 99 %, Spieß 66 %,
Fesselung 61 %, hängende Figur 61 %. **Streng** = Thema + ziehende Figur + Zielfiguren (`fork|N|KR` = Springergabel König + Turm),
**Familie** = Thema.

**Stellen**: je Fehler der Partie (`GameMistakes.Find`, nur Fehler/Patzer/verpasste Chance) die Stellung davor (*verpasste Taktik*:
beste Linie der ziehenden Seite) und danach (*zugelassene Taktik*: beste Linie des Gegners, Stelle auf seiner Seite) — nur wenn die
Linie klar gewinnt (≥ +2,0, kein Matt) UND ein Motiv erkannt wird. Reihenfolge: verpasstes Matt, dann Taktik nach Bewertungsverlust,
gespieltes Matt zuletzt. Antwort wie oben, `reason` `missedTactic`/`allowedTactic`, `theme` (z. B. `fork`), `mateIn` = `null`; die
Karten sagen „Gleiches Motiv, gleiche Figuren“ (streng) bzw. „Gleiches Motiv“; Thema-Rückfall = Lichess-Tag des Themas.

**Signaturen der Aufgaben**: derselbe `PuzzleSignatureWorker` rechnet nach den Mattbildern die Motive aller beliebten (≥ 80), oft
gespielten (≥ 200) Aufgaben OHNE Matt (`PuzzleSignatures`, `Kind` 2–9 je Thema, Fortschritt `SystemSettings.PuzzleSignatures.TacticCursor`).
Prod: ~2,4 Mio. Aufgaben, gemessen ~20 µs je Aufgabe, ~27 % tragen ein Motiv → ~0,65 Mio. Zeilen; ~2 400 Schritte ≈ 3 h nach den Mattbildern.

### Buchbeispiele (0.962.0)

Wunsch 2026-11-13: „wenn du in büchern das motiv findest dann sind beispiele aus dem kapitel > allgemeine probleme“, „ja bau den buch-teil“
(einfache Form), „erzwinge keine motive/aufgaben“. `Services/Puzzles/BookExercises.cs`.

**Signaturen** (`SignaturesOf`): dieselben wie bei den Lichess-Aufgaben, aus Diagrammstellung (Löser am Zug) + Lösung (`SolutionUci`):
endet die Linie matt → Mattbild (streng/Familie, `MateIn` = Züge des Lösers); sonst, nur wenn `EngineEval` ≥ +2,0 (wie die Taktikstellen
der Partie), die Motive des ersten Zugs. Tabelle `EbookDiagramSignatures` (`docs/datenbank-schema.md`). Berechnet werden nur Diagramme
mit **brauchbarer** Linie: bestätigte Buchlösung (`SolutionSource = book`, `SolutionCheck` `Confirmed` 1 oder `Replaced` 3 → **lösbar**)
oder Engine-Lösung (`Engine` 4, auch `Confirmed` mit `SolutionSource = engine` → nur **Beispiel im Buch**). `Doubtful`, `Unchecked`,
`NoPuzzle` und Diagramme ohne Lösung nie (ohne Linie gibt es kein Bild/Motiv). Dazu: lesbar (`FontFamily ≠ unread`), regelgerecht
(`EbookSolutionChecker.IsLegal`); eine reine Engine-Lösung nur bei sicherer Bilderkennung (`Confidence` ≥ 0,9 oder Font-Diagramm) — eine
bestätigte Buchlösung belegt die Stellung selbst. „Bestätigt“ heißt: Stockfish hält den ersten Buchzug für den besten, höchstens 120 cp
schlechter oder noch ≥ +4 gewinnend (`docs/api/ebooks.md`, Stockfish-Prüfung).

**Kapitel**: aus dem Inhaltsverzeichnis des Buchs wie „Als Kurs anlegen“ (`EbookChapterLookup`: PDF-Lesezeichen, EPUB-nav/ncx,
Fließtext-EPUB), beim Rechnen gespeichert (`Chapter`); ohne Verzeichnis zeigt die Karte die Seite (PDF) bzw. den Abschnitt (EPUB).

**Lauf**: im `PuzzleSignatureWorker` nach Mattbildern und Motiven, je Schritt 1 000 Diagramme mit Lösung in Id-Reihenfolge (gleiche
Drossel, Ruhezeiten, Pause-Schalter), Fortschritt `SystemSettings.PuzzleSignatures.BookCursor` (`v{Matt}.{Motiv}:<DiagrammId>`); eine
Portion ersetzt alle Zeilen ihres Id-Bereichs. Ist der Bestand durch und der Durchgang älter als 7 Tage
(`PuzzleSignatures.BookPassStartedAt`), beginnt ein neuer — so ziehen korrigierte Stellungen und neu geprüfte Lösungen nach. Bis dahin
erkennt die Auswahl veraltete Zeilen am `LineHash` und lässt sie weg. Prod (13.11., Simulation auf den Prod-Daten): 950 150 Diagramme mit
Lösung → ~950 Schritte, Rechenzeit ~25 s gesamt, mit Pausen und Inhaltsverzeichnissen geschätzt 1–2 h; ergibt ~127 000 Zeilen aus
~126 000 Diagrammen, davon ~12 100 lösbar (Mattbild 8 900, Motive 3 400) und ~114 000 nur Beispiel.

**Auswahl je Stelle** (vor Lichess, zusammen höchstens 6, nichts aufgefüllt): gleiche strenge Signatur, dann gleiche Familie; nur Bücher,
die der Betrachter in der Bibliothek sehen darf (`EbookAccess.Visible` als Unterabfrage — andere Bücher liefern weder Stellung noch Titel).
Je Stufe bis 120 Zeilen ab einem je Partie und Stelle gesäten Mischwert (`Shuffle`), geprüft gegen das aktuelle Diagramm (noch da,
`LineHash` gleich, Lösung brauchbar, nicht offen gemeldet: eine `EbookDiagramReports`-Meldung zählt, bis eine spätere
`EbookDiagramCorrections`-Korrektur folgt). Bücher mit lösbarem Diagramm zuerst, dann nach Spielstärke (`TargetMinElo` höchstens 150 über
bis 700 unter der Stärke der Seite = passend, unbekannt danach, sonst zuletzt), dann gesät gemischt; je Buch erst ein Diagramm, in einer
zweiten Runde höchstens ein weiteres aus DEMSELBEN Kapitel (ohne Kapitel ±10 Seiten) — möglichst verschiedene Bücher, und was aus einem Buch
zusammenbleibt, stammt aus derselben Stelle. Höchstens 2 Beispiele je Stelle, lösbare vor Beispielen. Danach Lichess wie oben (der
Thema-Rückfall nur, wenn auch kein Buch traf).

**Rechte je Weg**: angemeldet = das Konto (+ `ebooks.manage`/Admin = alle Bücher), auch über die Teilen-Links; ohne Anmeldung (Teilen-Links
`/g/{token}`, `/cg/{token}`, Liga-Link) nur, was die Bibliothek ohne Anmeldung zeigt — die für „Alle“ freigegebenen Bücher und eigene
anonyme Uploads (`X-Visitor-Id`). `BaseApiController.EbookViewerAsync`.

**Antwort** je Buchkarte: `source: book`, `id` = Diagramm, `fen` = Diagrammstellung, `moves` = Buchlösung ab dem Löser (leer bei
Beispielen), `rating` 0, `book: { ebookId, title, author, chapter, page, pdfPage, label, solvable }`. Der Leser-Sprung ist
`/ebooks/{ebookId}?page=…&fmt=pdf&diagram=…` (PDF) bzw. `?diagram=…` (EPUB), wie „Im Buch“ aus dem Kurs.

Prod-Partie SavedGame 221 (simuliert 13.11., Admin-Sicht): „15. verpasstes Matt in 4“ (Familie „Dame im Kontakt, offener König“) —
streng 0, Familie 76 Diagramme in 58 Büchern; gewählt (lösbar) Willemze „The Chess Toolbox“ („Part V: Solutions to exercises“),
Ramesh „Improve Your Chess Calculation“ („Chapter 3 The analytical process“), Geller „1500 Forced Mates“ S. 971 („Chapter 31. Test
Yourself!“), Grodzensky „The Lubyanka Gambit“ S. 169, dazu zwei Beispiele aus Problembüchern (Ott u. a. „Kunstwerke auf 64 Feldern“ S. 75,
Loyd „The Puzzle King“ S. 184); ohne Anmeldung nur zwei Beispiele aus den für alle freigegebenen Büchern.
