# Stellungsfilter (`/api/guess-tree`) — der Eroeffnungsbaum der Punktepartie

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Stellungsfilter (`/api/guess-tree`) — der Eroeffnungsbaum der Punktepartie“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Die Bestandssuche daneben beantwortet „ich weiss, wie die Partie heisst". Die andere Frage, die
man vor dem Ueben stellt, ist „was gibt es zu MEINER Eroeffnung?" — und das ist keine Textsuche.
Der Knopf „Nach Stellung filtern" oeffnet ein Brett in der Grundstellung und daneben den Baum:
je Zug, wie viele Partien ihn spielen; ein Klick geht hinein, die Liste darunter zeigt die Partien
zu dieser Stellung.

**Zwei Quellen, EIN Baum** (`onlyPlayable`): die freigegebenen `GameAnalysis`-Partien (sofort
spielbar) oder der ganze Rohbestand (`LibraryGame`, 130 000 Partien — von dort wird angefordert).
Der Umschalter ist der Punkt: ohne ihn fuehrt der Baum entweder in eine fast leere Auswahl oder
auf Partien, die man nicht spielen kann.

**Beide Seiten fuehren dieselbe normalisierte Zeile** (`OpeningLine`, die ersten 30 Halbzuege,
„e4 e5 Nf3"), und deshalb ist es ein Baum mit zwei Zaehlungen und nicht zwei Baeume. Bewertungs-
und Schachzeichen fallen beim Normalisieren weg: „Nf3+" und „Nf3" sind derselbe Zug, und ein Baum,
der sie trennt, hat zwei Aeste fuer eine Stellung. `GameAnalysis.OpeningLine` ist seit 0.474.0
eine eigene indizierte Spalte — der Baum fragt bei JEDEM Klick, und aus den Stellungszeilen
gerechnet waere das ein Selbst-Verbund ueber zehn Halbzuege. Nachgetragen wird sie mit
`tools/LibraryImport analysis-openings` (aus den Stellungszeilen, nicht aus dem PGN: dort steht
der Zug schon geprueft da).

| Methode | Endpoint | Auth | Zweck |
|---|---|---|---|
| GET | `/api/guess-tree?line=&onlyPlayable=` | **AllowAnonymous** + RL | Die Fortsetzungen nach `line` mit Partienzahl, haeufigste zuerst (hoechstens `MaxMoves` = 40), dazu `total` |

**Gezaehlt wird in der DATENBANK, und die Zahlen sind exakt** (0.475.2). Die erste Fassung holte
bis zu 20 000 Zeilen und zaehlte sie clientseitig. Der Deckel galt auf JEDER Ebene — nicht nur in
der Grundstellung, wie der Kommentar dort behauptete — und er war keine Stichprobe, sondern die
ersten 20 000 Zeilen nach Id, also nach Importreihenfolge. Am echten Bestand log damit alles bis zu
der Tiefe, ab der die Treffermenge unter den Deckel faellt: die Grundstellung meldete 20 000 statt
130 572 Partien, und die 9761 Partien, die dort bei `e4` standen, wurden nach dem Klick auf `e4`
wieder zu 20 000. Eine Zahl, die sich unter der Hand aendert, ist schlimmer als keine.

Jetzt zaehlt ein `GROUP BY` ueber `SUBSTRING_INDEX(SUBSTRING(OpeningLine, n), ' ', 1)`, die
Gesamtzahl ein `COUNT(*)`. Bewusst rohes SQL (`SqlQueryRaw`): `SUBSTRING_INDEX` uebersetzt kein
Anbieter einheitlich, und die InMemory-Datenbank der Tests kennt es nicht — dort laeuft der
clientseitige Weg weiter (`IsRelational()`-Weiche wie bei `LibraryGameService`). Der Praefix geht
als PARAMETER hinein.

**Dafuer braucht es einen ABDECKENDEN Index** (`IX_LibraryGames_Status_OpeningLine`,
`IX_GameAnalyses_IsPublic_OpeningLine`, Migration `OpeningTreeIndexes`). Mit dem Praefix-Index
allein waehlt MariaDB bei `LIKE 'e4 %'` — das ist die halbe Tabelle — den vollen Tabellenscan, und
der laeuft ueber die LONGTEXT-Spalte mit den PGNs. Gemessen auf Dev (130 572 Partien):

| Abfrage | ohne den Index | mit |
|---|---|---|
| Grundstellung | 15,8 s | 0,13 s |
| nach `1.e4` | 22,4 s | 0,25 s |

**Eine `OpeningLine` bekommt nur, wer in der GRUNDSTELLUNG anfaengt** (`LibraryGameReader.StartsFromInitialPosition`,
angewandt in `LibraryGameReader` und `GameAnalysisService.CreateAsync`). Eine Eroeffnungszeile
beschreibt einen Weg aus der Grundstellung; bei einer Vorgabepartie, einer Studie oder Chess960
stand der erste Zug der Partie damit als Fortsetzung an der WURZEL des Baums, wo es ihn gar nicht
gibt. Auf Prod war das ein `Kc6` (Partie 462, Stiller–Welz, Potsdam 1995) — ein Klick darauf konnte
nur mit „dieser Zug geht nicht" antworten. Solche Partien fallen aus dem Baum und bleiben ueber die
Namenssuche erreichbar; `openings` und `analysis-openings` RAEUMEN die Zeilen des Altbestands
entsprechend weg, bevor sie nachtragen.

**Ein Brett in einem DIALOG misst sich zu klein** (0.475.3, gilt app-weit). Material blendet einen
Dialog mit `transform: scale()` ein, und Chessground misst mit `getBoundingClientRect()` — das
liefert die SKALIERTE Groesse. Ein 300-px-Kasten misst waehrend der Einblendung 240, und diese 240
schreibt Chessground als feste Pixelzahl in sein inneres `cg-container`. Danach korrigiert es
nichts mehr: der ResizeObserver sieht die LAYOUT-Groesse des Wrappers, und die hat sich nie
geaendert. Sichtbar war das als Vollbild-Knopf sechzig Pixel neben dem Brett und einer gleich
grossen Luecke darunter. `ChessBoardComponent.fitToHost` misst deshalb nach (rAF, 200 ms, 500 ms),
vergleicht gegen das INNERE Element und duldet dabei Chessgrounds Rasterung auf ein Vielfaches von
acht (in einem 300-px-Kasten steht es richtigerweise auf 296) — ohne diese Schranke zeichnete jeder
Anlauf das Brett neu.

**Der Dialog laedt ohne Umbau nach** (`position-filter-dialog.component.ts`): der vorige Stand
bleibt stehen und wird nur abgeblendet, oben laeuft ein duenner Balken. Vorher setzte jeder Klick
beide Listen auf einen Spinner — der Dialog fiel auf halbe Hoehe zusammen und das Brett sprang
dabei in der Groesse, bei JEDEM Zug. Eine Antwort, deren `line`/`onlyPlayable` nicht mehr zum
angesehenen Stand passt, wird verworfen (wer schnell klickt, hat zwei Abfragen unterwegs). Die
Partieliste darunter nennt die Anzahl und blaettert (25 je Seite); auf schmalen Geraeten bekommt
das Brett 42 % der Breite statt aller — mit 300 px Brett blieben auf einem 400-px-Geraet genau zwei
Zuege sichtbar.

**Baum und Liste muessen DIESELBE Menge zaehlen** (0.475.7/.8). Der Stellungsfilter schickt
`byPosition=true` mit, die Namenssuche nicht. An diesem Schalter haengt, wer mitgezaehlt wird: eine Partie ohne `OpeningLine` (eigene
Ausgangsstellung) erreicht keine Stellung des Baums und darf in seiner Liste nicht auftauchen —
in der Namenssuche schon, denn dort ist sie zu finden und anzufordern. Und die Statusregel ist
in `GuessOpeningTree` dieselbe wie in `LibraryGameService` (weder `Rejected` noch `Duplicate`).
Ohne beides stand auf Prod „130 028 Partien erreichen diese Stellung“ ueber einer Liste mit
der Ueberschrift „Partien (130 544)“ — die 516 Partien ohne Zeile.

Bewusst ein eigener Schalter und nicht die Frage, ob `line` null ist: die Modellbindung von
ASP.NET macht aus einem leeren `?line=` ein `null`, und die Grundstellung ist genau dieser Fall.
Der erste Anlauf (0.475.7) unterschied daran und wirkte deshalb ueberall AUSSER an der Wurzel —
auf Prod nachgemessen: in der Spanischen stimmten die Zahlen, in der Grundstellung nicht.

Die beiden Listen nehmen denselben Filter entgegen: `GET /api/game-analyses/public?line=` und
`GET /api/library-games?line=`. Gesucht wird ueber ein PRAEFIX (`LIKE 'e4 e5 Nf3%'`) — der
Platzhalter steht hinten, also trifft es den Index.

**Das Brett fuehrt der Dialog selbst mit** (chess.js): der Baum liefert nur Zugnamen. Ein Zug, den
die Stellung nicht hergibt — eine Partie mit abweichender Ausgangsstellung kann so einen liefern —
verschiebt die Linie deshalb nicht, sondern sagt es.
