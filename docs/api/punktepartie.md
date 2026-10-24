# Punktepartie (`/guess`) — eine Meisterpartie Zug fuer Zug erraten

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Punktepartie (`/guess`) — eine Meisterpartie Zug fuer Zug erraten“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Der Nutzer uebernimmt EINE Seite einer analysierten Partie und raet ab einem bestimmten Halbzug
jeden Zug; gewertet wird gegen den TATSAECHLICHEN Partiezug (`GuessScoring`), die Engine urteilt nur
ueber die Alternativen — die Kandidatenlisten stehen fertig in der `GameAnalysis`, hier laeuft keine
Engine mehr.

**Die eiserne Regel: die Fortsetzung verlaesst den Server nicht.** Ausgeliefert wird immer nur die
aktuelle Stellung; der Partiezug kommt erst als ANTWORT auf den Rateversuch. Genau deshalb liegt der
Fortschritt in einer SITZUNG am Server — auch ohne Anmeldung. Ein Client, der selbst mitzaehlt,
muesste dem Server sagen, bei welchem Halbzug er steht, und koennte damit jeden Zug der Partie
einzeln abfragen.

**Ohne Anmeldung** (seit 0.459.0) laeuft alles ueber `…/anonymous` mit der Sitzungskennung des
Browsers (`GuessSession.UserId` ist NULLBAR, daneben `AnonymousSessionId` — dasselbe Muster wie bei
den anonymen Puzzle-Versuchen). Spielbar ist dort ausschliesslich der **kuratierte Bestand**
(`GameAnalysis.IsPublic`): an einer fremden privaten Analyse kommt niemand vorbei, weil
`StartAsync` anonym gar nicht erst nach einem Besitzer fragt. Die Kennung kommt aus
`core/anon-session.ts` und muss `ValidationConstants.SessionIdPattern` erfuellen (Hex, 32–36) — die
Mindestlaenge ist die einzige Schranke zwischen zwei anonymen Durchlaeufen. Nach Login/Registrierung
uebernimmt `POST /api/guess-sessions/claim-session` `{ sessionId }` → `{ claimed }` die anonymen Durchlaeufe des
Browsers ins Konto (wie Puzzle, Buch-Puzzle, Endless); danach gilt der Deckel je Besitzer (Codereview N11-003).

**Der erste relevante Zug** (0.467.0, `GuessStartPly`): wo das Raten anfaengt, war bis dahin eine
KONSTANTE (`DefaultSkipPlies` = 8, im Code als „grober Platzhalter" vermerkt) — bei einem scharfen
Gambit mitten im Gefecht, bei einer geschlossenen Eroeffnung noch reines Buchwissen. Jetzt
entscheidet der FRUEHERE von zwei Hinweisen: (1) die Eroeffnung verlaesst das Buch — der erste
Halbzug, dessen Zugfolge in weniger als `RareBelowGames` (20) Partien des Rohbestands vorkommt
(`LibraryGame.OpeningLine`, Praefix-Suche auf dem Index, BINAER gesucht: fuenf Abfragen statt
dreissig); (2) der Kommentator faengt an zu reden (`LibraryGameReader` liefert den ersten
kommentierten Halbzug gleich mit). Gedeckelt auf `Earliest` (6) bis `Latest` (40) und nie hinter
das Partieende. **Die beiden Hinweise werden VERSCHIEDEN umgerechnet** (0.468.0): beim
Eroeffnungs-Hinweis wird der erste Zug ausserhalb des Buchs GERATEN (minus eins), beim
Kommentar-Hinweis wird der kommentierte Zug noch VORGESPIELT und der Zug danach geraten. Sonst
gehoerte der Kommentar zu dem Zug, der gerade gesucht ist, und duerfte nicht gezeigt werden — der
Hinweis, der den Einstieg bestimmt hat, waere ausgerechnet der einzige unsichtbare. Das Brett zeigt
ihn beim Aufgehen der Sitzung (`showOpeningNote`). **Die Untergrenze ist noetig**: Sammlungen setzen den ersten Kommentar oft an den
ERSTEN Zug, und dort steht dann eine Quellenangabe statt einer Erklaerung. Ohne Bestand (frische
Installation, `MinLibrarySize` 1000) greift der erste Hinweis nicht und die alte Vorgabe traegt
weiter. Das Ergebnis haengt an der PARTIE und wird in `GameAnalysis.SuggestedStartPly` gemerkt.

**Ein Partiezug, den die Engine nicht unter ihren besten fuehrt, wird jetzt GESPIELT** (0.467.0).
Vorher uebersprang `AdvanceToPlayableAsync` die Stellung wortlos — und das traf ausgerechnet die
interessanten Zuege: ein Opfer, das die Engine erst zwei Zuege spaeter versteht, ist genau der Zug,
den man raten moechte. Die Bewertung liegt laengst da: die Stellung NACH dem Partiezug ist selbst
gerechnet, und ihre beste Bewertung ist die des GEGNERS — mit umgekehrtem Vorzeichen die des
gespielten Zuges (`GuessSessionService.GameMoveEvalAsync`, durchgereicht als
`GuessScoring.Evaluate(..., gameEvalPawns)`). Uebersprungen wird nur noch, was sich so auch nicht
bewerten laesst: der letzte Zug der Partie (keine Folgestellung) und eine aufgegebene Folgestellung.
Der Deckel „ungelistete Zuege sind hoechstens gleichwertig" gilt dabei NICHT fuer den Partiezug
selbst — sonst braechte ausgerechnet der gesuchte Zug statt der vollen Wertung ein „gleichwertig".

**Die Seite waehlt man im Bestand NICHT** (`CreateGuessSessionRequest.GuessWhite` weglassen): man
uebernimmt die des GEWINNERS, das ist der Sinn der Uebung. `GuessSessionService.WinnerSideAsync`
nimmt dafuer das ERGEBNIS, sonst die BEWERTUNG der letzten gerechneten Stellung (ab 1,5 Bauern
Unterschied) — und faellt sonst auf Weiss zurueck. Der Umweg ueber die Bewertung ist kein Sonderfall:
die zehn Meisterpartien aus Capablancas *Chess Fundamentals* tragen in der Kopfzeile nur `*`, weil
das Buch die Ergebnisse allein im Fliesstext nennt. Bei den EIGENEN Analysen bleibt die Wahl.

**Ein Deckel je Besitzer** (`MaxSessionsPerOwner` = 50, raeumt zuerst beendete, dann die aeltesten laufenden Durchlaeufe weg — Codereview A6-015): `POST`
legt auch ohne Anmeldung Zeilen an, und ohne Deckel waechst die Tabelle mit allem, was der
Rate-Limiter durchlaesst.

**Eigene Partien einwerfen** (0.463.0, nur angemeldet): auf der Seite steht ein PGN-Feld, das eine
`GameAnalysis` mit `Origin = Guess` anlegt — und zwar OHNE Tiefen- und Linien-Regler.
`GameAnalysisDefaults.GuessTargetDepth` (20) setzt der Server; die Anfrage
(`CreateGuessGameRequest`) nimmt die Tiefe gar nicht erst entgegen, das Verbergen im Formular ist
also kein Vorhang vor einem offenen Feld. Zwei Gruende: erstens rechnet meist NICHT die eigene
Maschine (siehe Haus-Engine), und ein Regler waere Selbstbedienung an fremder Rechenzeit — Tiefe 40
kostet grob das Zehnfache von 30. Zweitens braucht die Punktepartie die Tiefe nicht: gewertet wird
gegen den TATSAECHLICH gespielten Zug, die Engine liefert nur die Rangfolge der Alternativen. Wer
die Tiefe wirklich will, reiht die Partie weiter ueber `/analysis/games` von Hand ein — dort aendert
sich nichts.

**Die Haus-Engine.** Analyseauftraege laufen ueber Token und External-Engine-Registrierung des
AUFTRAGGEBERS; ein normal registrierter Nutzer hat keine, sein PGN prallte an „No background engine
configured" ab. Ein Admin kann seine Hintergrund-Engines deshalb freigeben
(`LichessEngineCredential.ShareAsHouseEngine`, `PUT /api/engine/house`, Haekchen in der
Engine-Karte des Profils). `GameAnalysisService.ResolveGuessEngineOwnerAsync` nimmt dann zuerst die
EIGENE Engine des Einwerfers und faellt sonst auf die Haus-Engine zurueck; die Freigabe gilt nur,
solange das Konto Admin ist. Geprueft wird sie zusaetzlich vor jedem Nachlegen und im Worker
(`EngineOwnerResolver.IsHouseEngineSharedAsync`, Codereview A4-004): ohne Freigabe scheitern Analyse und offene
Auftraege mit „Haus-Engine nicht mehr freigegeben“, die Vertiefung endet mit dem Ergebnis des ersten Durchgangs. Der Auftrag BLEIBT dabei beim Einwerfer — nur `EngineOwnerUserId`
(neu an `GameAnalysis` UND `AnalysisJob`, `null` = Besitzer) sagt dem Worker, wessen Token und
Engine er benutzt. Den Auftrag stattdessen dem Haus-Konto zu geben waere einfacher gewesen und
falsch: Deckel (`MaxOpenJobsPerUser` = 150), Trimmer, Auftragsliste und Sekundenanzeige haengen alle
am Besitzer — alle Einwerfer teilten sich dann hundertfuenfzig Plaetze, einer koennte alle aussperren, und
in der Auftragsliste des Admins staenden fremde Partien. `PickBackgroundEngineAsync` zaehlt die
kuerzeste Schlange entsprechend nach ENGINE-BESITZER (`EngineOwnerUserId ?? UserId`).

**Eine Partie nach der anderen** (0.466.0): die Pumpe fuettert je NUTZER immer nur EINE Partie
weiter — die aelteste mit ungerechneten Stellungen (`GameAnalysisService.IsOwnersTurnAsync`).
Vorher fuetterte sie jede unfertige Partie bis zum Block-Limit; bei fuenfzig offenen Auftraegen je
Nutzer liefen damit vier bis fuenf nebeneinander und alle wurden gleich langsam fertig. Das ist die
schlechteste Aufteilung: dieselbe Engine-Zeit, aber man wartet auf JEDE Partie das Fuenffache,
statt nach einem Fuenftel die erste spielen zu koennen — und eine halb gerechnete Partie ist fuer
die Punktepartie nichts wert. Gemessen wird an den STELLUNGEN, nicht am Status; gescheiterte
Partien blockieren nicht; laufende Auftraege einer anderen Partie werden nicht abgebrochen, sie
laufen aus. Je Nutzer und nicht global, damit sich zwei Leute nicht gegenseitig ausbremsen.

**Der Schwanz einer Partie (0.543.0)**: die Regel „eine nach der anderen" hat eine Ausnahme — haben
alle aelteren unfertigen Partien des Nutzers ZUSAMMEN weniger offene Stellungen, als Engines da sind
(`GameAnalysisService.EngineSlotsAsync`: die Hintergrund-Engines des Engine-Besitzers, bei fest
gewaehlter Engine 1), bekommt die naechste Partie schon Auftraege. Sonst stuenden am Ende jeder Partie
Engines still: gemessen am 2026-09-26 auf Prod mit 16 Engines endete jede Partie mit 20–30 s, in denen
15 Engines nichts taten — bei Partien von drei Minuten ein Fuenftel der Zeit. Die aeltere Partie wird
trotzdem ZUERST fertig, ihre Auftraege stehen vorn (FIFO nach `CreatedAt` in `PickNextForEngineAsync`).
Dieselbe Schwelle gilt fuer die Vertiefung (`IsOwnersRefineTurnAsync`): sie wartet, solange der erste
Durchgang mindestens so viele offene Stellungen hat wie Engines — und seit 0.647.0 auch unter den Vertiefungen
selbst (die naechste beginnt am Schwanz der aelteren, siehe „Zwei Durchgänge"). Zweite Haelfte derselben Messung: der
Worker holte den naechsten Auftrag fuer eine frei gewordene Engine erst beim naechsten Tick (5 s) —
seit 0.543.0 weckt ein beendeter Lauf die Schleife (`WakeSignal`), und `GET
/api/game-analyses/throughput` traegt die SPITZE der letzten 24 h (`MaxRunningEngines24h`,
`MaxNodesPerSecond24h`, Stundenkoerbe in `AnalysisJobLive`, nur Arbeitsspeicher): am Schwanz einer
Partie rechnet oft nur eine Engine, und „engines: 1 · 1 164 kN/s" allein sah wie ein Ausfall aus.

**Welche Einstellung wirklich schneller ist, wurde am 2026-09-13 auf Prod AUSGEMESSEN** — drei
Fenster zu je zwanzig Minuten, und das Ergebnis widerlegt die naheliegende Annahme:

| Engines | Block | Tiefe je Engine | Stellungen je Stunde |
|---|---|---|---|
| 16 | 32 | 2 | 957 |
| **5** | **32** | **6–7** | **1596** |
| 5 | 96 | 19 | 1104 |
| 16 | 96 | 6 | 786 |

Die letzte Zeile hat dieselbe Schlangentiefe wie die zweite und liefert die HAELFTE — nicht die
Tiefe entscheidet also, sondern die ZAHL der Engines, und zwar gegenlaeufig. Belegt ist davon der
Anteil, der auf die 503-Wechsel geht: im 16-Engine-Fenster 156 Wechsel gegen null in beiden
5-Engine-Fenstern, und jeder legt einen Auftrag `EngineSwitchBackoffSeconds` (15 s) schlafen.
Warum ein GROESSERER Block bei gleicher Engine-Zahl schadet, ist offen. Vorbehalt: jedes Fenster
umfasst sechs bis sieben verschiedene Partien, und die rechnen unterschiedlich schnell — die
Reihenfolge ist konsistent, die Zahlen sind es nicht auf zehn Prozent.

**Wer hier dreht, misst nach.** Diese drei wirken zusammen und nicht einzeln:
`MaxOpenJobsPerGame`, die Laenge der Engine-Liste im Profil und `MaxOpenJobsPerUser`.

**Der 503 des Brokers ist KEINE Ausfallmeldung** — er heisst „fuer diese Engine ist gerade kein
Provider frei“ und trifft auch Engines, die nachweislich rechnen (am selben Tag direkt
angesprochen: alle antworten 200). Aus einer Haeufung auf einer Maschine auf deren Ausfall zu
schliessen war falsch: beim Reihum-Wechsel landet ein pendelnder Auftrag zwoelf von sechzehn Malen
dort, sie klopft also dreimal so oft an.

**Wer nicht pollt, bekommt keine Arbeit** (0.711.0, gemeldet 2026-10-07 an /games/61): `EngineAvailability.UsableAsync`
lässt direkt angemeldete Engines (`rhe_`) weg, deren `LastSeenAt` älter als `OfflineAfter` (3 min) oder leer ist — gilt
für neue Aufträge (`PickBackgroundEngineAsync`), Slots/Stapel (`GameAnalysisService`, `MasterAnalysisScheduler`) UND den
Wechsel nach einem 503 (`SwitchEngineAsync` → `NextEngineAfter(…, usable)`: reihum zur nächsten LAUFENDEN). Vorher lagen
die Aufträge einer Partie auf den 16 Engines eines seit einem Tag ausgeschalteten PCs (leerste Schlange gewinnt) und
wanderten nach jedem 503 mit 15 s Pause zur nächsten ausgeschalteten. Lichess-Engines (`eei_`) kennen keinen Poll und
bleiben; ist gar keine Engine brauchbar, gilt wie bisher die ganze Liste (der Auftrag wartet).

**Wie viele Engines wirklich rechnen, entscheidet `MaxOpenJobsPerGame`** (32 seit 0.475.10 — die gemessene
Bestkonfiguration; 0.475.9 kurz 96, davor 32, davor 12) —
nicht die Zahl der hinterlegten Hintergrund-Engines. Der Worker nimmt je Engine EINE Suche; jede Engine
ueber die Zahl der offenen Auftraege hinaus steht still. Am 2026-09-12 auf Prod nachgemessen: sechzehn
Engines hinterlegt, zwoelf Auftraege auf zwoelf Engines, vier dauerhaft untaetig. Der Ueberhang ueber die
Engine-Zahl ist Absicht: laeuft eine Suche aus, nimmt die Engine sofort den naechsten Auftrag statt bis zu
einen Pump-Durchgang (20 s) zu warten. Der Block bleibt unter `MaxOpenJobsPerUser` (150), damit daneben
von Hand eingereiht werden kann.

**Der Deckel des Einwurfs** (`MaxOpenGuessGamesPerUser` = 5) zaehlt `Origin = Guess` UND (seit 0.512.0)
`Origin = SavedGame` zusammen — beide rechnen auf fremder Rechenzeit, getrennte Deckel waeren doppelt so viele
Plaetze — und nur Partien, die noch rechnen (`Pending`/`Running`); gescheiterte sperren niemanden aus. Von Hand
ueber `/analysis/games` eingereihte Partien bleiben ungezaehlt: dort rechnet die eigene Maschine. Die Pumpe
(„eine Partie nach der anderen") ist je Nutzer und unterscheidet die Urspruenge nicht.
