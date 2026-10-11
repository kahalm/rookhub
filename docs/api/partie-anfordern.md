# Partie anfordern (`/api/library-games`) — der Rohbestand als Nachschlagewerk

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Partie anfordern (`/api/library-games`) — der Rohbestand als Nachschlagewerk“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Bis 0.469.0 sah nur das Wartungswerkzeug in `LibraryGames`. Damit war die Auswahl aus 130 000
Partien die Aufgabe genau einer Person mit Datenbankzugang. Der Knopf „Partie anfordern" auf der
Punktepartie-Seite oeffnet den Bestand: suchen, und einzelne Partien in die Warteschlange stellen.

| Methode | Endpoint | Auth | Zweck |
|---|---|---|---|
| GET | `/api/library-games?q=&language=&minCommentedPlies=&page=&pageSize=&line=` | **AllowAnonymous** + RL | Eine Seite der Bestandssuche, nach Eignungsnote sortiert; je Zeile `inPool`/`requested`/`gameAnalysisId`. **Ohne die Zuege** |
| POST | `/api/library-games/{id}/request` | Auth | Diese Partie rechnen lassen — derselbe Weg wie ein eingeworfenes PGN (feste Tiefe 20, Haus-Engine, Deckel 5). Liegt sie schon spielbar da, kommt die vorhandene Analyse zurueck (`alreadyPlayable`), es wird NICHTS doppelt gerechnet. 400 mit `reason` ∈ `not-found`/`too-many-open`/`no-engine`/`invalid-pgn` |

**Suchen darf jeder, anfordern nur angemeldet** (0.475.5). Die Suche liefert Kopfdaten — Spieler,
Turnier, Jahr, Kommentator, Kommentardichte — und ausdruecklich KEINE Zuege und keine Anmerkungen;
das ist derselbe Zuschnitt, den `/api/guess-tree` ohnehin anonym ausliefert, und ohne ihn fuehrte der
Stellungsfilter einen Besucher ohne Konto in eine leere Liste. `MarkKnownAsync` bekommt anonym die
UserId 0: die gibt es nicht, also bleibt „schon angefordert“ ueberall falsch und nur „liegt im Bestand“ traegt. Das ANFORDERN bleibt angemeldet — es verbraucht Rechenzeit,
die jemandem gehoert — und der Dialog sagt den Grund an der Stelle, wo der Knopf waere.

**Gesucht wird ueber einen VOLLTEXT-Index** (`LibraryGame.SearchText` = Spieler, Turnier und
Kommentator kleingeschrieben in EINER Spalte). Am echten Bestand gemessen (2026-09-11, 130 572
Zeilen):

| Weg | Dauer |
|---|---|
| `LIKE '%Capablanca%'` ueber eine der vier Einzelspalten | 4,5 s |
| dasselbe ueber eine schmale indizierte Spalte | 0,9 s |
| … und mit `ORDER BY Score DESC LIMIT 25` | **50 s** (der Optimierer nimmt den Score-Index und sucht sich zeilenweise durch) |
| Volltext-Index, Seite samt Sortierung | **4 ms** (Zaehlen 63 ms) |

Der Preis ist, dass WORTANFAENGE gesucht werden: „Capa" findet „Capablanca", „blanca" nicht — fuer
Namen ist das die Suche, die Leute ohnehin tippen. Der Ausdruck wird in
`LibraryGameService.BooleanTerm` gebaut (jedes Wort Pflicht, das letzte mit Stern); die Operatoren
der Boolean-Syntax werden WEGGEWORFEN, sonst waere ein Bindestrich in einem Doppelnamen eine Suche,
die das Gegenteil meint. Woerter unter `MinQueryLength` (3) filtern gar nicht, weil MariaDBs
Volltext-Index sie nicht aufnimmt. **Der Index steht als SQL in der Migration** — EF kann diese
Indexart nicht ausdruecken; `Down()` raeumt ihn wieder weg.

**Die Zuordnung ist VIELE Analysen zu EINER Bibliothekspartie** (`GameAnalysis.LibraryGameId`, kein
Fremdschluessel — der Rohbestand ist ein Arbeitsvorrat, der auch neu eingelesen werden kann): fordern
zwei Leute dieselbe Partie an, bekommt jeder seine eigene. Der umgekehrte Verweis
(`LibraryGame.GameAnalysisId`) koennte immer nur einen halten und bleibt der Kuratierungs-Vermerk.

**Die angeforderte Partie ist PRIVAT** (wie ein eingeworfenes PGN); oeffentlich macht sie erst ein
Admin ueber `PUT /api/game-analyses/{id}/public`. Das ist bewusst so: die Kommentare stammen aus
einer gekauften Sammlung, und „jeder Nutzer kann etwas in den oeffentlichen Bestand schieben" waere
eine andere Entscheidung als „ich rechne mir meine Partie durch".

**Befuellt wird mit `tools/LibraryImport`** (Wartungswerkzeug, kein Teil des API-Images; das
Docker-Image baut nur aus `src/api/RookHub.Api`). Vier Schritte, jeder fuer sich wiederholbar:
`import <datei>` · `dedupe` · `openings` · `languages` · `score`, dazu `stats`, `comments` und `resplit`
(zweisprachige Partien, die als einsprachig vermerkt sind — siehe „Anmerkungen in mehreren Sprachen").

**`queue [anzahl] --user <id>`** ist der Massen-Weg zu dem, was auf der Seite der Knopf „Partie
anfordern" je Partie tut: die besten Partien des Bestands als `GameAnalysis` mit `Origin = Guess`
und fester Tiefe einreihen. Die Auswahl trifft `Services/LibraryPicks.cs` — Note, dann
kommentierte Halbzuege, dann Textmenge, und hoechstens `--per-annotator` (2) Partien je
Kommentator: an der Spitze stehen tausende Partien mit Note 100, und nach der Zeilennummer
sortiert bekaeme man den Bestand alphabetisch nach Kommentator. Der Deckel von fuenf offenen
Partien gilt hier bewusst NICHT (er ist eine Fairness-Regel zwischen Nutzern an der Oberflaeche);
die Reihenfolge bleibt trotzdem gewahrt, weil die Pumpe je Nutzer immer nur EINE Partie
weiterfuettert. Angelegt werden nur die Zeilen — die AUFTRAEGE macht die laufende API beim
naechsten Pump-Durchgang. `--dry-run` zeigt die Auswahl, ohne etwas einzureihen; `--game <id>` reiht genau EINE benannte
Bibliothekspartie ein (der Weg fuer „rechne mir diese hier", ohne die Rangfolge). Verbindung ueber
`ConnectionStrings__DefaultConnection`. Es startet KEINE API-Instanz, sondern oeffnet nur einen
DbContext — eine zweite `RookHub.Api` gegen dieselbe Datenbank streitet sich mit dem
Auftrags-Worker um die Engines.

Die Liste der eigenen Partien auf der Punktepartie-Seite zeigt seither AUCH die noch rechnenden (mit
Fortschrittsbalken, „Spielen" bis zur ersten gerechneten Stellung gesperrt) und frischt sich alle
10 s auf, solange eine offen ist. Vorher standen dort nur Partien mit mindestens einer gerechneten
Stellung — die gerade eingeworfene waere fuer Minuten spurlos verschwunden und ein zweites Mal
eingeworfen worden.
