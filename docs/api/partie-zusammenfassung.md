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

## „Zusammenfassung“ neben den Ligapartien in LeagueHub (0.967.0)

Wunsch 2026-11-14: „zeig im ⋮ … neben den Partien einen Eintrag für Summary — der soll nur sichtbar sein, wenn eine vorhanden ist“.

- **Feld `hasSummary`** je Brettpaarung in `GET /api/league/{tnr}/round/{round}/games?team=` und `GET /api/league/s/{token}/games`
  (`LeagueFixtureGames.Pairing`), dazu am `game` jedes Bretts der Aufstellungen (`…/round/{round}/lineups`,
  `/api/league/s/{token}/lineups`, Vereinsübersicht `club/overview`; `LeagueGameMoves.LineupGame`). `true` nur, wenn die
  Vereinspartie des Bretts eine Zeile mit Status **fertig** UND nicht leerem Text hat — nicht bei angefordert, in Arbeit oder
  gescheitert, auch wenn ein alter Text noch lesbar wäre (wie die Partienliste: eine offene Neuanforderung schlägt den alten
  Text). Archivierte Partien hängen an keinem Brett. EINE zusätzliche Abfrage je Aufruf (`WithSummariesAsync`, über den
  eindeutigen Index `ClubGameId`), keine je Brett.
- **Wer es bekommt** — kein neues Recht: das Kennzeichen reicht genau so weit wie der Text. Angemeldet (`league.view`) kommen
  nur Vereinspartien des EIGENEN Vereins als `clubGameId` in die Paarungen; genau die liest `GET …/club/games/{id}/summary`.
  Über den Teilen-Link liest `…/s/{token}/club/games/{id}/summary` nur die Partien der Begegnung des Links — `/s/{token}/games`
  zeigt ohnehin nur diese, und die Aufstellungen der ganzen Runde setzen `hasSummary` über den Link nur an der Begegnung des
  Links (`summariesOnlyForTeam`), an den anderen Begegnungen des Vereins bleibt es `false`.
- **Oberfläche** (LeagueHub): ⋮-Menü der Brettpaarung in `shared/fixture-view.component.ts` (Ligaseite und Teilen-Seite),
  Eintrag „Zusammenfassung“ hinter „Nachspielen“; in `shared/board-game.component.ts` („Alle Aufstellungen der Runde“,
  Vereinsübersicht) als Link hinter „Analyse“. Ziel wie „Analyse“: RookHub per `HandoffService.jumpToRookHub` —
  angemeldet `club-games/{id}/summary` (Einmal-Code), über den Teilen-Link `s/{token}/club-games/{id}/summary` ohne Anmeldung.
  Ohne bekannte RookHub-Adresse (localhost) fehlt der Eintrag wie „Analyse“. Die Texte sind wie überall in LeagueHub fest deutsch.

## Markdown-Format

Teilmenge, gerendert über das Template (nie `innerHTML`, `summary-markdown.util.ts`): `#`/`##`/`###`, Absätze, Listen
(`-`, `1.`), Zitat `>`, `---`, einfache `|`-Tabellen, `**fett**`, `*kursiv*`, `` `Code` ``, Links `[Text](Ziel)` nur mit
einem Ziel der Erlaubt-Liste (siehe „Links auf Meisterpartien“ unten). Diagramme:

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

### Links auf Meisterpartien (0.980.0)

Wunsch 2026-11-15: der Text nennt dort, wo die Partie die Meisterpartien verlässt, 1–2 Beispielpartien aus dem Explorer
(„Kosakowski – Nie (2025)“) — „am besten mit einem Link“. `parseInline` liest `[Text](Ziel)`; zum Link wird es NUR, wenn das
Ziel als Ganzes eines dieser zwei Muster ist (`summaryLink`, `summary-markdown.util.ts`):

| Ziel | Muster | Verhalten |
|---|---|---|
| Meisterpartie des lokalen Explorers | `/explorer/game/masters/<id>` mit optionalem `?ply=<Halbzug>`; `<id>` = genau 8 Zeichen `A–Z a–z 0–9`, `<Halbzug>` 1–3 Ziffern | seitenintern über den Router, selber Tab |
| Partie auf Lichess | `https://lichess.org/<id>` mit optionalem `/white` bzw. `/black` und `#<Halbzug>` | neuer Tab, `rel="noopener noreferrer"` |

Beispiel (echte Partie, Stellung nach 1.e4 c5 2.Sf3 Sc6 3.c3 g6 4.Lc4 Lg7 5.O-O e6 6.Te1 Sge7 7.d4 cxd4 = 14 Halbzüge):
`[Kosakowski – Nie (2025)](/explorer/game/masters/6kBV5lDP?ply=14)`. Der Pfad ohne `?ply=` steht fertig im Feld `path` von
`GET /api/explorer/games` (nur bei lokalen Meisterpartien gesetzt, dort ist `url` leer; `docs/api/repertoires.md`); bei
Online-Partien steht das Lichess-Ziel wie bisher in `url`. Wer den Text schreibt, nimmt also `path`, sonst `url`, und hängt
bei `path` nach Wunsch `?ply=` an.

Alles andere bleibt wörtlich als Text stehen (`[Text](Ziel)`): andere Adressen, `http://`, `javascript:`, andere Pfade der
Seite, Zusätze hinter dem Muster (weitere Parameter, `#…`, Titel in Anführungszeichen). Der Linktext ist reiner Text: kein
Stil darin (fett/kursiv um den GANZEN Link geht: `**[Text](Ziel)**`), kein `]`, und Züge im Linktext werden nicht klickbar
gemacht — umgekehrt läuft das Klickbar-Machen der Züge links und rechts des Links unverändert weiter, auch im Abschnitt
„Die Partie“ und in dessen Klammern. Das Linkziel trägt nur die Kennung der Meisterpartie, nichts über die Partie, von der
man kommt; es ist von allen fünf Wegen auf die Seite aus dasselbe (absoluter Pfad).

Das Ziel selbst: Seite „Meisterpartie“ (`features/explorer/master-game.component.ts`, Route `explorer/game/masters/:id` ohne
Guard) — Kopf aus den Kopfzeilen des PGN (Spieler, Elo, Ergebnis, Turnier · Ort · Jahr), darunter das Seitenbrett der
Zusammenfassung (`SummaryGameBoardComponent`: Blättern, Zugliste, Engine, „+“, eigene Züge als Variante). Daten:
`GET /api/explorer/master-games/{id}` → `{ id, pgn }`, ohne Anmeldung, je Adresse gedrosselt (`anonymous-tournament`),
Kennung nicht 8 Zeichen aus Ziffern/Buchstaben → 400, unbekannt → 404 (beides zeigt „gibt es nicht“), Explorer nicht
eingerichtet/erreichbar → 503 (Ladefehler mit „Wiederholen“). Die Partie kommt aus `masters/pgn/{id}` des lokalen Explorers
(`LocalExplorerClient.FetchMasterPgnAsync`) und bleibt einen Tag im Arbeitsspeicher. Tests: `summary-markdown.util.spec.ts`
(„Links“), `summary-game-text.util.spec.ts`, `club-game-summary.component.spec.ts` („Links auf Meisterpartien“),
`master-game.component.spec.ts` ↔ `ExplorerMasterGameTests`, `RepertoireExplorerServiceTests`.

## Vereinspartien, auf die jemand wartet (0.922.0)

Die Hauptanalyse einer Vereinspartie legt der Stapel an (`MasterAnalysisScheduler`), also nur außerhalb der Sperrzeiten
und wenn Plätze frei sind. Eine angeforderte Zusammenfassung stand deshalb tagsüber still (Partie 239 am 09.11., Montag
früh hochgeladen). Jetzt legt der Takt VOR beiden Sperren die Analyse jeder Partie an, auf die jemand wartet (offene
Zusammenfassung oder Eintrag in „Meine Partien“), als `GameAnalysis.Expedited`: Herkunft und Listen bleiben, gerechnet wird
aber wie „Partie analysieren“ (`GameAnalysisOrigins.RunsAsBatch`). Eine schon im Stapel wartende Analyse mit offener
Zusammenfassung wird samt eingereihter Aufträge umgestellt. Der Nachtrag läuft beim nächsten Takt von selbst.

Archivierte Vereinspartien (durch ein neueres Formular ersetzt, `ArchivedAt`) haben bewusst keine Analyse mehr.

## Automatisch für neue Ligapartien der laufenden Saison (0.966.0)

Wunsch 2026-11-14: „neue aktuelle Ligapartien automatisch Summary erzeugen“. Wird eine Vereinspartie **neu angelegt** und ist
sie eine Ligapartie der laufenden Saison, legt der Server die Anforderung selbst an — dieselbe Zeile wie nach dem Klick eines
Admins (Status `requested`), danach läuft alles wie unter „Ablauf“ (Analyse wird vorgezogen, der Watcher schreibt). Gleiches,
wenn eine vorhandene Partie erst **nachträglich** einer Ligapaarung der laufenden Saison zugeordnet wird. Regeln an EINER
Stelle: `Services/League/ClubGameAutoSummary.cs`, gerufen nur aus `ClubGameWriter`:

- `SaveAsync` — darüber läuft jedes Anlegen: PGN-/ChessBase-Import (`POST api/league/club/games/import`; ChessBase wird vorher nur nach PGN gewandelt), Partie aus einem
  Partieformular bzw. von Hand (`POST api/league/club/games`), beides auch über den Teilen-Link. Ersetzt ein neueres Formular
  eine Partie, ist die neue Fassung eine neue Partie (bekommt eine Anforderung); die archivierte behält ihre Zeile.
- `AssignPairingIfUnlinkedAsync` (`POST …/games/{id}/pairing/auto`, manual-scan des Watchers mit `clubGameId`) und
  `UpdateAsync` mit `leagueGameId` (Paarung von Hand gewählt oder umgehängt).

**Bedingungen** (alle):

1. **Ligapartie der laufenden Saison**: die Partie trägt eine Ligapaarung (`LeagueGameId` bzw. Schlüssel Tnr/Runde/Begegnung/
   Brett, aufgelöst über `LeagueGameLinks.ResolveAsync` — eine vorläufige Zuordnung zur laufenden Runde zählt, eine tote Id
   nicht), und die Liga dieser Paarung (`LeagueTournaments.Tnr`) gehört zur jüngsten Saison ihrer Region
   (`LeagueOnlineRegions.CurrentSeasonTnrsAsync`: je Region — Tirol/Österreich, Bayern — die größte `Season`).
2. Nicht archiviert (`ArchivedAt`) und noch **keine** Zeile in `ClubGameSummaries` (egal welcher Status).
3. **Partiedatum** höchstens `MaxAgeDays` Tage zurück: der volle Tag aus dem Upload (PGN-Kopf `Date`, Datum des Formulars),
   sonst der Rundentermin der Paarung (`LeagueRounds.Date`), sonst heute. Die gespeicherte Partie trägt nur das Jahr.
4. **Tagesdeckel**: heute (UTC) weniger als `DailyLimit` automatische Anforderungen, über alle Vereine — gezählt an
   `ClubGameSummaries.AutoRequestedAt` (Migration `ClubGameSummaryAutoRequestedAt`; bleibt bei späterem Neu-Anfordern stehen,
   von Hand angeforderte Zeilen haben `null` und zählen nicht). Kein Sperren: zwei gleichzeitige Uploads können den Deckel um
   wenige überschreiten.

Über Altersgrenze oder Deckel wird still nichts angefordert (Log-Zeile auf Information, „Zusammenfassung: Vereinspartie … nicht
automatisch angefordert“); die Partie wird normal angelegt, ein Admin kann von Hand anfordern. Ein Fehler beim automatischen
Anfordern (Warnung im Log) lässt das Anlegen nie scheitern. **Kein Nachholen**: der Bestand wird nie durchgesehen — weder beim
Start noch nach „Daten aktualisieren“ (`LeagueGameLinks.HealAsync`, das Bestätigen vorläufiger Zuordnungen fordern nichts an).

Anforderer (`RequestedByUserId`): wer die Partie anlegt bzw. zuordnet; über den Teilen-Link ohne Konto `null`. Oberfläche
unverändert: Sanduhr und Zähler der Kopfzeile sehen wie bisher nur Admins (`canRequest`), alle anderen sehen erst den Link zur
fertigen Zusammenfassung.

| Einstellung | Vorgabe | Bedeutung |
|---|---|---|
| `ClubGameSummaries:Auto:Enabled` | `true` | automatisches Anfordern ein/aus |
| `ClubGameSummaries:Auto:MaxAgeDays` | `30` | so alt darf das Partiedatum höchstens sein |
| `ClubGameSummaries:Auto:DailyLimit` | `20` | automatische Anforderungen je UTC-Tag insgesamt (ein Lauf kostet etwa 3,50 $) |

Übrige Einstellungen der Zusammenfassungen: `OwnGameSummaries:DailyPerUser` (unten), `ClubMistakeComparisons:Enabled` (oben).
Tests: `ClubGameAutoSummaryTests`.

## Eigene Partien (0.923.0)

Der Besitzer fordert auf `/games/:id` an (`POST /api/games/{id}/summary`, `{ lang }`), gelesen wird auf
`/games/:id/summary` (`GET /api/games/{id}/summary`), angefordert nur von ihm. `OwnGameSummary`, je Partie eine Zeile.

Verbundene Vereinspartie (0.954.0): `GET /api/games/{id}/summary` liefert, solange die eigene Partie keine eigene Zusammenfassung mit Text oder offener Anforderung hat, den Stand der verbundenen Vereinspartie (`source: club`) — angefordert/beim Schreiben mit `readiness` und `runningSeconds`, fertig mit Text — und `clubGameId`, wenn der Nutzer die Vereinspartie sehen darf (Admin oder `league.view` + Mitglied ihres Vereins); die Kopfzeile zeigt dann Sanduhr + Zähler bzw. den Link nach `/club-games/{id}/summary` statt „Zusammenfassung anfordern“. Ohne diese Rechte nur ein fertiger Text, ohne `clubGameId` (Entscheidung E7, wie bisher), nichts Offenes. Eine gescheiterte eigene Anforderung ohne Text tritt hinter eine Vereins-Zusammenfassung zurück. Gleiche Regel in der Liste (`docs/api/gespeicherte-partien.md`, „Zusammenfassung in der Liste“).

Teilen-Link (0.934.0): `/g/:token/summary` liest `GET /api/games/shared/{token}/summary` (ohne Anmeldung, globaler IP-Limiter) → `{ status: done|none, markdown, doneAt, stale, model }`, 404 bei unbekanntem Link. Ohne Text `none`, auch wenn eine Anforderung offen oder gescheitert ist; ein früherer Text bleibt sichtbar. Nichts zu Kontingent, Anforderung, Fehler, und **kein** Rückfall auf den Text einer verbundenen Vereinspartie (der gehört nicht zu diesem Link). Wer den Link hat, liest damit den Text — auch den an den Besitzer gerichteten Schluss.

**Teilen aus der Zusammenfassung + anonymer Link (0.984.0, Wunsch 2026-11-15: „da fehlt mir ein teilen link (der auch anonym teilt)“).**
Die Seite hat im Kopf ein ⋮ (nur angemeldet auf `/games/:id/summary` bzw. `/club-games/:id/summary`, nur mit fertigem Text):

| Eintrag | Wo | Was |
|---|---|---|
| Zur Partie | beide | wie der Kopfknopf (der am Handy entfällt, sobald es das ⋮ gibt) |
| Link teilen | eigene Partie | `/g/{token}/summary` — das Token hat jede gespeicherte Partie, kein Serveraufruf |
| Link teilen | Vereinspartie, Recht `league.view` | `/cg/{token}/summary`, `POST api/league/club/games/{id}/share` (ein bestehender Link bleibt) |
| Anonym teilen | eigene Partie | `/ga/{token}/summary`, `POST api/games/{id}/summary/anon-share` → `{ token, summaryAvailable }` |
| Teilen-Link widerrufen | eigene Partie | `DELETE api/games/{id}/share` — der normale UND der anonyme Link sind danach tot |

Am Handy (grobes Zeigegerät und `navigator.share` vorhanden) öffnet sich das Teilen-Blatt, sonst geht der Link in die Zwischenablage
(`GameActionsService.shareLink`). Ohne EIGENEN Text (keiner, oder nur der der verbundenen Vereinspartie, `source: club`) gibt es die
Einträge nicht — die Links der eigenen Partie zeigen den Vereinstext nicht. Für Vereinspartien gibt es kein „Anonym teilen“: dort wird
schon beim Hochladen anonymisiert („Schwaz“), siehe „Teilen-Link einer Vereinspartie“.

**Anonymer Link** (`Services/Games/AnonGameShares.cs`): eigenes Token `SavedGames.AnonShareToken` (Migration
`SavedGameAnonShareToken`, erst auf Zuruf angelegt, eindeutig auch gegenüber `ShareToken`), eigener Weg `/ga/` — bewusst NICHT eine
Marke am normalen Link: an `/g/{token}` hängen Partieseite, Bewertungen, Erklärungen, Roasts und die Link-Vorschau, alle mit Namen.
Hinter `/ga/` gibt es nur zwei Lesewege (ohne Anmeldung, globaler IP-Limiter), keine Partieseite und keine eigene Link-Vorschau
(die OG-Weiche greift nicht; ein Crawler sieht die allgemeine Seite):

| Weg | Antwort |
|---|---|
| `GET api/games/anon/{token}` | `{ pgn, result, ownerSide }` — das PGN neu gebaut: nur `Result` (und `SetUp`/`FEN` bei abweichender Ausgangsstellung) + Hauptvariante; keine Namen, Elo, Daten, Orte, Quelle, Kommentare, Varianten, kein „Kurz erzählt“ |
| `GET api/games/anon/{token}/summary` | `{ status: done\|none, markdown, doneAt, stale, model }` — Text über `SafeSummary` |

`SafeSummary` — der Text nennt die Spieler in der Überschrift („# Nachname, Vorname – Nachname, Vorname (1-0)“), im Fließtext heißen
die Seiten „Weiß“/„Schwarz“ (Bestand 15.11.: 45 von 45 Texten, alle kommen sauber durch):

1. In den **Überschriften** werden die Namen der Seiten (Spalten `White`/`Black`, PGN-Kopf, Klarnamen einer verbundenen
   Vereinspartie) durch „Weiß“/„Schwarz“ ersetzt (englisch „White“/„Black“; Sprache = `ReviewLanguage`, sonst nach dem Text) — samt
   Titeln („Dr.“, „GM“), Genitiv und kurzen Namensteilen, die an einem langen hängen („De Vreugt“, „Chen, Yan Xi“).
2. „Runde N“/„Brett N“ fällt überall weg (wie beim Link der Vereinspartie).
3. Steht danach noch irgendwo — Fließtext, Bildunterschrift, Buchbegründung — ein Namensteil als ganzes Wort (ab drei Zeichen; bei
   Namen aus nur kürzeren Teilen ab zwei), gibt es **keinen Text** (`none`). Geprüft werden beide Seiten, der Besitzer (Kontoname,
   Vor-/Nachname, Anzeigename, chess.com-/Lichess-/Discord-Name), die verbundene Vereinspartie und `WhiteTeam`/`BlackTeam`. Im
   Fließtext wird bewusst nicht ersetzt: ein Spieler „Bauer“ machte aus jedem Bauern einen „Weiß“ — lieber kein Text als ein
   Klarname oder ein verdrehter Satz (dieselbe Vorsicht wie `ClubGameLinks.SafeSummary`). `summaryAvailable: false` sagt das dem
   Teilenden sofort (Hinweis statt „kopiert“); der Link zeigt dann die Partie ohne Namen und den Leersatz.

Was der Empfänger trotzdem sieht: die Züge, das Ergebnis, aus wessen Sicht das Brett steht (`ownerSide`), Elo-Zahlen und
Eröffnung, soweit der Text sie nennt, die Namen der Meisterpartien in den Beispiel-Links, das Modell. Wer die Partie kennt, erkennt
sie an den Zügen. Die Seite (`mode: 'own-anon'`): Kopf „Weiß – Schwarz“, kein Zurück-Pfeil, kein „Zur Partie“, kein „In der Partie
zeigen“, kein „Taktiken trainieren“, „+“ markiert nur die Stellung. Tests: `AnonGameSharesTests`, `club-game-summary.component.spec.ts`
(„Teilen-Menü“).

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

### Rechenaufwand der Verlinkung (0.978.1)

Die Verlinkung läuft in zwei `computed` der Seite (`mergedSection`, `linkedBlocks`), also vor der ersten Anzeige des Texts.
Gemeldet 2026-11-15: die Seite „braucht beim ersten Start sehr lang“ — die API antwortete in 5–20 ms, danach blockierte EIN
Longtask die Seite rund eine Sekunde (Handy: ein Vielfaches). Ursache war nicht die Zahl der Proben, sondern ihr Preis:
`moveOf` (`explanation-lines.ts`) fragte je Wort `parseKeyboardMove`, und das rief `chess.moves({ verbose: true })` — chess.js
baut dabei für JEDEN legalen Zug ein volles Zug-Objekt (SAN, zwei FENs, Schach-/Matt-Probe, je eine weitere Zuggenerierung),
rund 1,2 ms je Stellung statt 0,1 ms für die bloße SAN-Liste. Dazu kam je erkanntem Zug ein zweites `new Chess` + `move`, und
`readSequence` prüft jeden Zug zweimal (Vorschau, dann der Zug selbst).

Seitdem:

- `shared/chess/legal-moves.ts` merkt je FEN die SAN-Liste (`chess.moves()` ohne `verbose`) und rechnet von/nach/Stellung
  danach nur für den Zug aus, der gebraucht wird (`legalMove`), ebenfalls gemerkt. Gedeckelt auf 2 000 Stellungen.
  `parseKeyboardMove` (auch Tastatur-Eingabe am Aufgabenbrett) liest daraus; die volle Liste entsteht nur noch für
  Koordinaten-Eingaben („g1f3“).
- `explanation-lines.ts` merkt die Antwort je (Stellung, Wort) und je (Stellung, SAN); davon hat auch der Zugvergleich
  („Warum war das ein Fehler?“, `mistake-comparison`) etwas.
- Am Ergebnis ändert sich nichts: `summary-linking.spec.ts` vergleicht eine echte Zusammenfassung (36 Züge, 360 klickbare
  Züge, Namen ersetzt) Block für Block mit der Verlinkung der Fassung davor (`summary-linking.fixture.ts`) und deckelt die
  Aufrufe von chess.js.

Gemessen an dieser Partie (Rechner des Stacks unter Last, Chromium ohne Kopf, leerer Cache): Zuggenerierungen von chess.js
für die Verlinkung 24 774 → 865, `moves()` 696 → 196; Verlinkung im Profil ~900 ms → ~60 ms; Blockade zwischen Antwort und
Text 850–900 ms → 170–185 ms, mit vierfach gedrosselter CPU ~7,2 s → 0,8–2,0 s. Der Rest ist der Aufbau der Seite selbst
(Bretter der Diagramme, 360 Zug-Knöpfe), nicht mehr die Verlinkung.

Bewusst NICHT gebaut: den Text zuerst ohne Züge zeigen und sie danach in Portionen klickbar machen. Ein Zug-Knopf ist anders
gesetzt als Fließtext (Schriftstärke; im Abschnitt „Die Partie“ zusätzlich Zeilenhöhe, fette Partiezüge, kleinere Varianten,
Ort des Blocks) — die Zeilen brächen beim Nachziehen neu um. Wird das doch einmal nötig, zuerst dort gleiche Maße herstellen.

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
**„+“ — Stellung markieren** (0.972.0, Wunsch 2026-11-14): hinter dem Engine-Knopf steht `app-mark-position-button` (Desktop
und Blatt), derselbe wie auf der Partieseite. Er markiert die Stellung auf dem Brett — Partiestellung mit Halbzug, Variante
(aus dem Text oder selbst gezogen) mit Abzweig + Zügen — mit der Herkunft der jeweiligen Ansicht (`markSource`); grün =
schon markiert, Klick öffnet Schlagworte + Kommentar. Diagramme mit eigener `fen` (Stellung aus einer Variante) kommen nicht
aufs Seitenbrett und haben keinen eigenen Knopf. Details: `markierte-stellungen.md`.

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

> **Seit 0.973.0 zeigt die Seite die Übungen nicht mehr** (Wunsch 2026-11-14: „train am ende der summary gefällt mir, aber übersicht
> darüber kann weg“): Überschrift, Stellen, Karten, Buchangaben und der Löser auf der Seite sind entfernt; geblieben ist der Knopf
> „Taktiken trainieren“ am Ende des Texts (`docs/frontend/empfohlene-uebungen.md`). Die Endpunkte unten sind unverändert — das Frontend
> ruft `…/summary/exercises` nur noch ab, um zu entscheiden, ob der Knopf erscheint (angemeldet mit Kurs-Zugang; anonym gar nicht), und
> `PersonalTacticsCourse` baut aus derselben Auswahl das Kapitel. Was unten „die Seite zeigt“ beschreibt, ist der Stand bis 0.972.x.

Wunsch 2026-11-13: „am ende eine kleine sammlung von empfohlenen übungen … z. B. M2/M3 mit so motiven (Df7 Kf6 und bauer auf f4, aber
kann dann natürlich auch ein springer sein der das deckt)“; „direkt auf der seite lösbar“; „erzwinge keine motive/aufgaben — wenn es bei
einer partie nichts gibt ists auch vollkommen in ordnung“. Die Seite setzt den Abschnitt selbst hinter den Text (nach der Literatur) —
aus den Daten der Partie, ohne Claude, also auch für alle schon fertigen Zusammenfassungen.

**Stellen** (`GameExercises.Occurrences`, beide Seiten): *verpasstes Matt* — die Seite am Zug hatte laut Analyse Matt in ≤ 5, nach ihrem
Zug steht der Gegner nicht mehr matt bzw. die Partie endet ohne Matt (Bild aus der Engine-Linie); *gespieltes Matt* — die Partie endet
matt (Bild aus der Schlussstellung, Stelle = Beginn der Mattfolge). Gleiches Bild mehrfach → eine Gruppe (`alsoPlies`), höchstens 8.
Seit 0.961.0 auch **taktische Fehler**, seit 0.962.0 zuerst **Buchbeispiele**, seit 0.976.0 **Mattdrohung**, **Matt am Schluss** und
**Grundreihe** (alles unten).

**Mattbild** (`Services/Puzzles/MatePattern.cs`, Regelstand `Version`): Schlussstellung relativ zum mattgesetzten König, farb- und
spiegelunabhängig — Lage des Königs (Ecke/Grundreihe/Rand/offen), schachgebende Figur(en) mit Lage (Kontakt diagonal/gerade, Linie,
Springer, Bauer) und Deckung, die acht Nachbarfelder (Rand / eigener Stein / vom Angreifer beherrscht). **Streng** = mit den Typen der
deckenden und beherrschenden Figuren und dem genauen Versatz, **Familie** = ohne Typen: „Dame g7 gedeckt vom Bauern f6“ und „… vom
Springer h5“ sind dieselbe Familie.

**Auswahl je Stelle** (bis 6 LÖSBARE): gleiche strenge Signatur, dann gleiche Familie; was dann noch fehlt, aus dem Thema — bei
einem Matt erst das benannte Mattbild (`backRankMate`, `smotheredMate`), dann `mateInN`; bei einer Taktik das Lichess-Thema des Motivs
(bis 0.975.x griff das Thema nur, wenn Signatur und Familie GAR nichts lieferten — eine Stelle konnte mit einer einzigen Aufgabe
dastehen). Ein „Beispiel im Buch“ ohne bestätigte Lösung zählt seit 0.976.0 nicht zu den sechs. Rating-Fenster ±250 um die Stärke der Seite, die das Motiv hatte
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

Antwort `{ groups: [{ ply, white, reason: missedMate|playedMate|forcedMate|mateThreat|missedTactic|allowedTactic, mateIn, theme, alsoPlies, exercises: [{ id, fen, moves, rating, themes,
match: strict|family|theme, source: lichess|book, book }] }] }` — Lichess: `fen` vor dem Zug des Gegners, `moves` = Gegnerzug + Lösung
(Lichess-Form), `book` = `null`; Buch (0.962.0) siehe „Buchbeispiele“. Ohne Analyse leer.

**„Taktiken trainieren“** (0.968.0; seit 0.973.0 für sich am Ende des Texts): nur angemeldet — legt die lösbaren Aufgaben der Partie als Kapitel im Kurs
„Persönliche Taktiken“ an (einer je Konto, ein Kapitel je Partie) und öffnet es im Kurs-Löser: `POST /api/courses/personal-tactics/from-game`,
`docs/api/kurse.md` → „Persönliche Taktiken“.

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
Linie klar gewinnt (≥ +2,0 oder, seit 0.976.0, ≥ 2,0 vor dem zweitbesten Zug; kein Matt) UND ein Motiv erkannt wird (seit 0.976.0
mit den Partie-Regeln `DetectInGame`, nächster Abschnitt). Reihenfolge: verpasstes Matt, dann Taktik nach Bewertungsverlust,
gespieltes Matt zuletzt. Antwort wie oben, `reason` `missedTactic`/`allowedTactic`, `theme` (z. B. `fork`), `mateIn` = `null`; die
Karten sagen „Gleiches Motiv, gleiche Figuren“ (streng) bzw. „Gleiches Motiv“; Thema-Rückfall = Lichess-Tag des Themas.

**Signaturen der Aufgaben**: derselbe `PuzzleSignatureWorker` rechnet nach den Mattbildern die Motive aller beliebten (≥ 80), oft
gespielten (≥ 200) Aufgaben OHNE Matt (`PuzzleSignatures`, `Kind` 2–9 je Thema, Fortschritt `SystemSettings.PuzzleSignatures.TacticCursor`).
Prod: ~2,4 Mio. Aufgaben, gemessen ~20 µs je Aufgabe, ~27 % tragen ein Motiv → ~0,65 Mio. Zeilen; ~2 400 Schritte ≈ 3 h nach den Mattbildern.

### Mehr Motive: Mattdrohung, Matt am Schluss, Grundreihe (0.976.0)

Befund 2026-11-14 an Vereinspartie 251 („hat nur 5 Gabeln als Taktik — ist das auf einen Block limitiert?“ … „da könnt ich mir 5
Gabeln, 5x Matt, 5 mal Grundlinie mindestens vorstellen“): eine einzige Gruppe. Limitiert war nichts — die Erkennung fand die
anderen Motive nicht. Warum (aktuelle Hauptanalyse, Tiefe 30, fünf Linien):

| Stelle | Beste Linie | Warum bis 0.975.x keine Stelle |
|---|---|---|
| 10.Lg5? (statt 10.dxc5) | +1,45, Bauerngewinn | unter +2,0, kein Motiv — bleibt so (kein taktisches Motiv) |
| 16...d4? (statt 16...Sxe3) | +1,25 | unter +2,0, positionell — bleibt so |
| 19...a6?? / 20...dxc3 (statt ...e4) | +2,9 / +3,6 | kein Motiv am ersten Zug; der Doppelangriff ...Lxc3 steht erst im 7. Halbzug hinter ruhigen Bauernzügen — bleibt so |
| 23...Tf5? → 24.Sd6 | +1,5 | unter +2,0; die Gabel 25.Sf7+ steht nur in der Nebenlinie 24...Txg5 — bleibt so |
| 28...Tc8?? → 29.Sd6 (29.Td1??) | +6,7 | **gefunden** (Gabel `fork|N|RR`) — die eine Gruppe |
| 29...Se7?? bis 33.La5? | +5,0 … +6,5 | gewinnt klar, aber durch Druck (7. Reihe, Fesselung an einen Turm) — kein erkanntes Motiv, bleibt so |
| 36...g5? → 37.Sd6 (37.Td8) | +6,5 | Gabel nicht „eingelöst“: der Turm verkauft sich (37...Txg4+ 38.Kxg4), geschlagen wird also kein Gabelziel; das Matt 37...Tg8 38.Sf7# steht nur im Antwort-Test des Vergleichs, den die Übungen nicht lasen |
| 41.Td6?? (statt 41.Td8+ Lf8 42.Lb4) | −1,4 (zweitbester Zug −6,8) | unter +2,0 — die Taktik holt Material ZURÜCK; und „Grundreihe“ kannte der Erkenner nicht |
| 41...Ta4? / 42.Txa6? (42.Td8+) | −3,8 (zweitbester −5,0) | unter +2,0, Abstand nur 1,2 — bleibt so (dasselbe Motiv zählt über den 41. und 45. Zug) |
| 44...Sxc3?? → 45.Ta8+ Lf8 46.Lxc3+ (45.Lb6??) | −0,1 (zweitbester −7,4) | wie 41.Td6: unter +2,0, Grundreihe unbekannt |

Die Schlüsselstellung „27...T3f6?? — drei Züge lang hängt die Qualität“ der Zusammenfassung gibt es in der neu eingelesenen Partie
nicht mehr (dort steht 27...T3f7; der Text stammt von der Fassung davor).

**Was dazukam** — jede Stelle bleibt ein Motiv DIESER Partie („erzwinge keine motive“ gilt weiter):

- **Mattdrohung** (`reason: mateThreat`, `Services/Puzzles/MateThreats.cs`): an den beiden Stellungen jedes Fehlers (davor: der beste
  Zug des Ziehenden; danach: der beste Zug des Gegners), wenn die beste Linie selbst kein Matt ist. (1) Der beste Zug **droht Matt in
  einem Zug** — er schlägt nichts und gibt kein Schach, und dürfte die Seite noch einmal ziehen, setzte sie matt (ein Schlagzug zählt
  nicht: „zurückschlagen, sonst Matt“ ist ein Abtausch). (2) Droht er nichts: **eine naheliegende Antwort läuft ins Matt in 1** — im
  Schach jede legale Antwort außer der der Engine-Linie, sonst das Schlagen der gezogenen Figur und die Züge der Figuren, die sie
  angreift und die das etwas angeht (wertvoller als sie oder ungedeckt): 37.Sd6 Tg8 38.Sf7#. Rein am Brett, ohne Engine — gilt also
  auch für eigene Partien ohne Fehler-Vergleiche. (3) **Zusatzlinien der Fehler-Vergleiche** (`GameExercises.SideLinesOf`, Matt in
  ≤ 5): gerechnete Zeilen `BestMoveTry` (verlockende Antwort auf den besten Zug, 0.974.0) und `Reply` (Antwort-Test; nur wenn die
  Antwort nach dem besten Zug naheliegt wie in (2) — der Test spielt die Antworten auf den FEHLZUG, das kann nach dem besten Zug auch
  „stellt die Dame ein“ sein) → Stelle = der Vergleich, Seite = wer dort am Zug war; `Defence` (andere Verteidigung in der
  Widerlegung des Fehlzugs, 0.971.0) → Stelle = die Stellung nach dem Fehlzug, Seite des Widerlegenden. Gelesen werden nur Zeilen mit
  Mattbewertung (`ResultJson LIKE '%"mate"%'`), und nur, wenn der Vergleich zur Stellung der Analyse passt. Je Stellung und Seite
  höchstens EINE Mattdrohung (Brett vor Zusatzlinie, kürzestes Matt). Mattbild und Auswahl wie beim verpassten Matt; Reihenfolge wie
  die Taktik desselben Fehlers, direkt davor.
- **Matt am Schluss** (`reason: forcedMate`): die Partie endet ohne Matt, die Endstellung (Zeile ohne Zug, seit 0.689.0) steht aber
  auf Matt in ≤ 5 — aufgegeben vor dem Matt. Bild aus der Engine-Linie der Endstellung, Stelle = Beginn der Mattfolge, Seite = wer
  mattsetzt; eingeordnet wie das gespielte Matt (zuletzt). Bis 0.975.x fiel das durch: kein Matt gespielt, keines verpasst.
- **Benanntes Mattbild** (`MatePattern.ThemeOf`, Feld `theme` der Gruppe): `smotheredMate` (ein Springer gibt allein Schach, jedes
  Nachbarfeld des Königs trägt einen eigenen Stein) und `backRankMate` (König auf seiner Grundreihe, Turm/Dame geben auf dieser Reihe
  Schach, die Felder direkt vor ihm tragen eigene Steine) — eigene Umsetzung nach den dokumentierten Themen-Definitionen. Gemessen
  an 9 341 beliebten Prod-Matt-Aufgaben (14.11.): `smotheredMate` 142 von 142 wie Lichess (keine Abweichung); `backRankMate` findet
  637 von 638 der Lichess-Aufgaben, dazu 423, die Lichess nicht so nennt — durchgesehen: Damenmatts auf der Grundreihe, bei denen
  die Dame zugleich einen der Steine vor dem König angreift (Lichess etikettiert enger; nach der Definition sind es
  Grundreihenmatts). Das Thema steuert nur das Auffüllen, die Signatur (`MatePattern.Version`) ist unverändert.
- **Partie-Erkennung der Motive** (`TacticMotifs.DetectInGame`; die Signaturen der Aufgaben und Buchdiagramme bleiben, wie sie
  sind — `TacticMotifs.Version` 1, kein Neurechnen):
  - **Grundreihe** (`theme: backRankMate`, streng `backRank|R` bzw. `|Q`): Turm oder Dame gibt auf der Grundreihe des Königs
    Schach, der König hat KEINEN Zug (es bleibt nur Dazwischenziehen oder Schlagen), und die Linie gewinnt in acht Halbzügen
    mindestens 2 Bauerneinheiten. Lichess kennt die Grundreihe nur als Matt — die Aufgaben dazu kommen deshalb aus dem Thema
    `backRankMate`; eigene Signatur-Zeilen gibt es nicht (`TacticMotifs.HasSignatures`).
  - **Gabel mit Desperado**: eingelöst ist die Gabel auch, wenn eine gegabelte Figur sich verkauft und dort geschlagen wird.
  - **Hängende Figur**: die Aufgaben-Regel („kurze Lösung“, ≤ 3 Halbzüge) traf Engine-Linien nie, die sind immer lang. In der
    Partie zählt die Bilanz: ungedeckte Figur (ab Leichtfigur) geschlagen, nach der Antwort UND nach acht Halbzügen ≥ 2 im Plus;
    ein Zurückschlagen (der Zug davor — in der Partie oder in der Linie — schlug auf demselben Feld) nur, wenn es mindestens 2
    mehr holt, als dort verloren ging (Läufer schlägt gedeckten Bauern und geht verloren = hängende Figur; Figurentausch nicht).
  - **Motiv nach forcierender Einleitung**: steht am ersten Zug keines und schlägt er oder gibt Schach, wird am zweiten, dann am
    dritten eigenen Zug gesucht (Vereinspartie 245: 26.Txc8+ Kxc8 27.Tc1+ Kd8 28.Sf7+ Kd7 29.Sxh6 → `fork|N|KR`); die Bilanz der
    Einleitung zählt mit (ein Opfer vorweg ist kein Gewinn). Dafür sieht die Partie-Erkennung **8 statt 6 Halbzüge** an
    (`TacticMotifs.GamePlies`; der Beleg ist genau diese Linie — Gabel im 5., eingelöst im 7. Halbzug); Umwandlungen zählen
    weiter nur in den ersten sechs.
- **Schwelle**: +2,0 bleibt (`MinTacticCp`). Dazu zählt die beste Linie auch, wenn sie mindestens 2,0 besser ist als der
  ZWEITBESTE Zug (`MinTacticGap`; läuft der in ein Matt, gilt der Abstand als erfüllt) — die Taktik, die Material zurückholt oder
  die Partie rettet. Beleg: 251, 41.Td8+ (−1,4 statt −6,8) und 45.Ta8+ (−0,1 statt −7,4). Der Abstand ALLEIN macht keine Stelle:
  in 31 Prod-Partien gab es 28 Fehlerstellen „unter +2,0, aber ≥ 2,0 vor dem zweitbesten Zug“ ohne Motiv (einzige Rückschläge,
  einzige Verteidigungen) — sie bleiben ohne Übung. Die drei Motive unter +2,0 mit kleinerem Abstand (Fesselung −0,4/1,4;
  Abzug +1,8/1,6; Bauerngabel 0,0/0,04) bleiben ebenfalls draußen.

**Nicht umgesetzt, mit Grund**: Ablenkung, Hinlenkung, Beseitigung des Verteidigers (unscharf; `capturingDefender` traf am 13.11.
nur 23 % des Lichess-Themas); gefangene Figur (`trappedPiece` — bräuchte wie die übrigen Motive erst eine Messung gegen das
Lichess-Thema; in der Stichprobe ein Fall, Partie 224, 34.Lg5 fängt die Dame); Fesselung an einen Turm oder eine ungedeckte Figur
(die Fesselungs-Regel verlangt König oder Dame dahinter und stimmte schon so nur zu 61 %); Matt aus weiteren Kandidatenlinien
derselben Stellung (kam in der Stichprobe nicht vor: steht der beste Zug nicht auf Matt, tun es die anderen auch nicht); eigene
Signaturen für die Grundreihe (hieße 2,4 Mio. Aufgaben neu rechnen — das Lichess-Thema liefert im Fenster genug).

**Wirkung** (31 Vereinspartien 221–251 der Prod, Hauptanalyse, 14.11.; Aufgaben über den Lichess-Weg bei Zielstärke 1500, ohne
Bücher): Gruppen 7 → 17, Partien mit mindestens einer Gruppe 7 → 10, Aufgaben 42 → 102. Neu: 3 Mattdrohungen (251 ersticktes Matt
nach 29./37.Sd6 Tg8; 231 19.Sg5+ hxg5 20.Dh3#; 250 8...Dh4 droht Dxf2#), 3 × Matt am Schluss (231, 240, 248), 2 × hängende Figur
(229 20.Dxg4, 250 9.Sxf5), 1 × Grundreihe (251), 1 × Gabel nach Einleitung (245); die Gabel der 251 steht jetzt an zwei Stellen
(29. und 37. Zug). Alle zehn neuen Stellen am Brett nachgespielt (python-chess: Matt ist Matt, die hängende Figur ist ungedeckt,
die Bilanz stimmt). 21 der 31 Partien bleiben ohne Übung: ihre Fehler sind positionell oder tragen keines der erkannten Motive.
**251** liefert drei Gruppen — Grundreihe (45. Zug, auch 41.), Gabel `fork|N|RR` (37. Zug, auch 29.), Mattdrohung ersticktes Matt
(29. Zug, auch 37.) —, im Regressionstest mit den echten Daten (`tests/…/Fixtures/GameExercises/club-game-251*.tsv`).

**Noch nicht an echten Daten geprüft**: die Zeilen `Defence` und `BestMoveTry` — die Prod trug am 14.11. nur `Candidate` und
`Reply` (0.971.0/0.974.0 noch nicht ausgerollt); abgesichert sind sie mit gestellten Zeilen (`GameExercisesTests`,
`PuzzleSignatureSqlTests` gegen MariaDB).

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
zusammenbleibt, stammt aus derselben Stelle. Höchstens 2 Beispiele je Stelle, lösbare vor Beispielen. Danach Lichess wie oben, bis
sechs LÖSBARE beisammen sind (seit 0.976.0 zählen die Beispiele dabei nicht mit — eine Stelle kann also sechs lösbare Aufgaben und
dazu bis zu zwei Beispiele tragen; bis 0.975.x griff der Thema-Rückfall nur, wenn auch kein Buch traf).

**Rechte je Weg**: angemeldet = das Konto (+ `ebooks.manage`/Admin = alle Bücher), auch über die Teilen-Links; ohne Anmeldung (Teilen-Links
`/g/{token}`, `/cg/{token}`, Liga-Link) nur, was die Bibliothek ohne Anmeldung zeigt — die für „Alle“ freigegebenen Bücher und eigene
anonyme Uploads (`X-Visitor-Id`). `BaseApiController.EbookViewerAsync`.

**Antwort** je Buchkarte: `source: book`, `id` = Diagramm, `fen` = Diagrammstellung, `moves` = Buchlinie ab der Diagrammstellung (leer bei
Beispielen), `rating` 0, `book: { ebookId, title, author, chapter, page, pdfPage, label, solvable, opponentFirst }`. Seit 0.973.1 heißt
`solvable`: Buchlösung bestätigt UND Löserseite bestimmbar (`BookSolverLine`, `docs/api/kurse.md` → „Der Löser spielt nie die Seite, die
verliert“); `opponentFirst: true` = `moves[0]` ist der Gegnerzug (wie bei Lichess), sonst ist der Löser in der Diagrammstellung am Zug. Der Leser-Sprung ist
`/ebooks/{ebookId}?page=…&fmt=pdf&diagram=…` (PDF) bzw. `?diagram=…` (EPUB), wie „Im Buch“ aus dem Kurs.

Prod-Partie SavedGame 221 (simuliert 13.11., Admin-Sicht): „15. verpasstes Matt in 4“ (Familie „Dame im Kontakt, offener König“) —
streng 0, Familie 76 Diagramme in 58 Büchern; gewählt (lösbar) Willemze „The Chess Toolbox“ („Part V: Solutions to exercises“),
Ramesh „Improve Your Chess Calculation“ („Chapter 3 The analytical process“), Geller „1500 Forced Mates“ S. 971 („Chapter 31. Test
Yourself!“), Grodzensky „The Lubyanka Gambit“ S. 169, dazu zwei Beispiele aus Problembüchern (Ott u. a. „Kunstwerke auf 64 Feldern“ S. 75,
Loyd „The Puzzle King“ S. 184); ohne Anmeldung nur zwei Beispiele aus den für alle freigegebenen Büchern.
