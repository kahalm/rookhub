# Turnierverlauf (auth)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Turnierverlauf (auth)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Welche Turniere ein Spieler gespielt hat, welche noch kommen, und in den gespielten Punkte, Platz
und Performance-Rating. Umschaltbar auf Freunde.

**Zwei Quellen, zwei Kostenklassen** (gemessen 2026-09-07 an einem echten Konto): die
chess-results-SPIELERSUCHE liefert in EINEM Abruf ALLE Teilnahmen eines Spielers — vergangene und
kuenftige, je Zeile mit Datum, Platz, Runden-/Teilnehmerzahl und der STARTNUMMER (die steht in
keiner Spalte, nur im Link auf den Namen). Punkte, Performance-Rating und Elo-Aenderung stehen
dagegen nur auf der SPIELERKARTE (`art=9&snr=`), und die kostet EINEN Abruf je Turnier. Bei dem
gemessenen Konto: 23 Turniere, 12 davon gespielt — also 1 + 12 Abrufe fuer die vollstaendige
Historie, rund zwanzig Sekunden hinter dem Rate-Limiter des Crawlers.

Daraus die Aufteilung im `TournamentHistoryService`: die LISTE wird beim Aufruf geholt, wenn sie
aelter als `ListTtl` (12 h) ist — ein Abruf, die Ansicht steht damit sofort mit Termin und Platz.
Die KARTEN laufen ueber die `IBackgroundTaskQueue` nach (`MaxCardsPerRequest` = 25 je Aufruf); ein
abgeschlossenes Turnier aendert sich nie wieder, der Zwischenspeicher gilt also fuer immer. Die
Antwort nennt `pending`, damit die Ansicht sich selbst nachladen kann statt eine halbe Tabelle als
endgueltig auszugeben. Ein KUENFTIGES Turnier bekommt gar keinen Kartenabruf (kein Ergebnis, Platz
in der Trefferliste „-") — das sparte 11 der 23.

**Der Zwischenspeicher haengt am SPIELER, nicht am Konto** (`PlayerTournamentResult.PlayerKey` =
`fide:1693034` bzw. `cr:144749`): die Historie ist fuer jeden dieselbe, ein Freund soll denselben
Speicher benutzen. Die FIDE-Kennung hat Vorrang, weil bei einem AUSLANDS-Turnier in der
chess-results-Ident-Spalte „0" steht und dann allein sie die Identitaet traegt. Ohne Nachnamen im
Profil gibt es keinen Verlauf (Status `noName`) — die Spielersuche sucht ueber den NAMEN, es gibt
dort keine Suche ueber eine Nummer. Ohne Kennung gilt der Name allein, und dann sieht man
Namensgleiche mit; die Auswahl markiert das (`exact: false`).

| Methode | Endpoint | Zweck |
|---------|----------|-------|
| GET | `/api/tournament-history?userIds=` | Verlauf; ohne Parameter der eigene. Fremde Konten muessen **angenommene Freunde** sein (sonst 403, fuer die ganze Anfrage — ein still uebersprungenes Konto waere eine Luecke ohne Erklaerung); max. 20 je Aufruf. Je Konto `status` (`ok`/`noName`/`sourceUnavailable`), `entries` und `pending` |
| GET | `/api/tournament-history/friends` | Welche Freunde ueberhaupt einen Verlauf haben (Nachname im Profil), je mit `exact` — traegt das Profil eine Kennung? |
| GET | `/api/tournament-history/tracked` | Die verfolgten Spieler dieses Kontos (siehe unten) |
| POST | `/api/tournament-history/tracked` | Einen beliebigen Spieler verfolgen `{ lastName, firstName?, fideId?, chessResultsId?, displayName? }` — das, was `GET /api/profile/player-search` zurueckgab. **Idempotent**: derselbe Spielerschluessel gibt den vorhandenen Eintrag zurueck (kein 409 — die Oberflaeche muesste sonst einen Fehler zeigen, wo nichts fehlt). 400 ohne Nachnamen (min. 2 Zeichen) und ab `MaxTracked` (20) Eintraegen, dann mit `limit` im Rumpf |
| DELETE | `/api/tournament-history/tracked/{id}` | Nicht mehr verfolgen (404 bei fremdem Eintrag). Der geholte Verlauf BLEIBT — er gehoert dem Spieler, nicht dem Reiter |
| GET | `/api/tournament-history/tracked/{id}` | Der Verlauf eines verfolgten Spielers (`PlayerHistoryDto` mit `userId: 0`). Eigener Endpunkt statt eines Parameters an `/api/tournament-history`: dort ist die Zahl ein KONTO, hier ein Eintrag der eigenen Liste |

Sichtbarkeit wie bei `/api/friends/{userId}/stats`: die Daten sind auf chess-results oeffentlich,
die VERKNUEPFUNG von Konto und Spielerkennung ist es nicht (eine anonyme Profil-Sicht mit den
Kennungen gibt es bewusst nicht — `GET /api/profile/{username}` ist seit A2-008 entfernt —, und dabei bleibt es).

**Verfolgte Spieler: der Verlauf haengt nicht mehr an KONTEN** (`TrackedPlayers`, seit 0.463.0).
Bis hierher gab es genau zwei Quellen fuer einen Reiter — das eigene Konto und angenommene
Freunde. Die Leute, deren Ergebnisse man tatsaechlich verfolgt, haben aber meist gar kein Konto
hier: das eigene Kind, ein Vereinskamerad, der Gegner der naechsten Runde. Sie einzuladen, damit
man ihre auf chess-results OEFFENTLICH stehenden Turniere sehen kann, ist keine Loesung.

Gespeichert wird eine **SUCHE, kein Personendatensatz**: genau die vier Felder, aus denen
`IdentityOf` einen Spielerschluessel baut (Nachname, Vorname, FIDE-Nummer, chess-results-Nummer).
Damit faellt der Verlauf selbst in denselben Zwischenspeicher wie der eines Kontos — wer denselben
Spieler verfolgt wie jemand anderes, loest keinen zweiten Abruf aus. Angelegt wird aus der
bestehenden Spielersuche (`GET /api/profile/player-search`, chess-results UND FIDE): die
Kennung kennt niemand auswendig, und ohne sie zeigt der Verlauf Namensgleiche mit (`exact:
false`, die Ansicht sagt es).

Zwei Dinge, die dabei nicht kippen duerfen: (1) **Verfolgte laufen im naechtlichen Durchgang MIT**
(`RefreshAllAsync` nimmt ihre Identitaeten zu denen der Profile) — sonst stuende ihr Verlauf nur
so weit, wie ihn jemand durch Ansehen gefuellt hat (gedeckelt auf `MaxCardsPerRequest` je Aufruf),
und bei einem Vielspieler bliebe die Tabelle dauerhaft halb leer. (2) Die Liste ist auf
`MaxTracked` (20) gedeckelt und wird beim Kontoloeschen mit abgeraeumt (`ProfileService`) — jeder
Eintrag kostet den Nachtlauf mindestens einen Seitenabruf, eine Liste ohne Deckel waere ein Weg,
den Crawler mit einem einzigen Konto auszulasten.

**Der Verlauf entsteht im HINTERGRUND** (`PlayerHistoryScheduler`, 04:30 UTC nach dem
Verzeichnis-Sweep, plus ein Lauf zehn Minuten nach dem Start): `RefreshAllAsync` frischt jede
Identitaet mit Nachnamen auf und holt die fehlenden Seiten sequenziell. `PlayerHistory:MaxCardsPerRun`
(200) deckelt die SUMME der Abrufe eines Laufs (Karten + Bedenkzeiten), damit ein Vielspieler die
uebrigen Konten nicht aushungert; die LISTEN laufen auch nach dem Deckel weiter, denn sie machen
neue Turniere ueberhaupt sichtbar. Vorher entstand der Verlauf nur beim Ansehen (25 Karten je
Aufruf, Nachfragen rund eine Minute) — wer die Seite schloss, liess den Rest liegen.

**Laufende Turniere haben einen Zwischenstand** (0.552.0, `NeedsCard`): Ende heute oder später →
Karte holen, danach alle `RunningCardTtl` (2 h), sobald Partien draufstehen, sonst alle
`UpcomingCardTtl` (20 h); vorbei → einmal neu, wenn die Karte noch WÄHREND des Turniers geholt
wurde (`CardFetchedAt`-Datum ≤ Ende). Den Beginn kennt die Trefferliste nicht (eine Liga „läuft"
über Monate) — ob gespielt wird, sagt die Karte; die Ansicht stellt solche Zeilen unter „Läuft
gerade" (`running()` = nicht vorbei UND `hasResult`). Der Nachtlauf filtert dafür im Speicher
(dieselbe Methode), vorgefiltert je Spieler in der Datenbank.

**Danach holt derselbe Lauf die GANZEN Turniere** (`CrawlHistoryTournamentsAsync`, seit 0.551.0):
Teilnehmer und Paarungen standen vorher erst nach einem Klick im Verlauf bereit („wird gerade
geholt", bis zu zwei Minuten). Reihenfolge: zuerst LAUFENDE (Ende zwischen −3 und +14 Tagen) —
die werden jede Nacht neu angefordert, auch wenn sie schon da sind —, dann fehlende, neueste
zuerst. `PlayerHistory:MaxTournamentCrawlsPerRun` (40, 0 = aus) deckelt die Anforderungen eines
Laufs (eine Olympiade sind ~15 Seiten). `HistoryTournamentCrawls` merkt sich je Turnier, ob es
beim Crawler steht (`FoundAt` → nie wieder nachfragen, ausser laufend) und wie oft es erfolglos
angefordert wurde (Pause 7 Tage, nach 3 Versuchen aufgegeben) — sonst fraessen dauerhaft
scheiternde Abrufe jede Nacht den Deckel.

**Auto-Favoriten: man selbst, Freunde UND Verfolgte** (`AutoSubscriptionService.AutoFavoritePlayersAsync`,
seit 0.551.0). Laeuft beim Oeffnen eines Turniers (`GET /api/tournament-favorites?tournamentId=`)
und nachts fuer alle laufenden/kommenden Abos (vorher nur fuer Abos OHNE jeden Favoriten — ein
spaeter angemeldeter Freund bekam dort nie seinen Stern). Ein selbst entfernter Spieler-Stern
landet in `TournamentFavoriteDismissals` und wird nicht wieder gesetzt; wer ihn neu setzt, loescht
die Zeile. Abgleich: tragen BEIDE eine FIDE-ID, entscheidet sie allein (verschieden = nicht der
Kandidat, auch bei gleichem Namen); sonst Nachname exakt + erstes Vornamens-Token.

**Ein Kartenabruf haengt am TERMIN, nicht am Platz.** Frueher stand dort `Rank is not null`, weil
ein kuenftiges Turnier in der Trefferliste auf „-" steht — dasselbe „-" steht dort aber auch bei
jedem MANNSCHAFTSturnier (chess-results weist in der Spielersuche keinen Einzelplatz aus). Am
echten Konto waren das acht von elf offenen Zeilen (Ligen), deren Spielerkarte Punkte und
Performance sehr wohl kennt. Geholt wird ab dem Tag NACH dem Ende, sonst friert ein Zwischenstand
ein; und `MergeAsync` setzt den Platz nur, solange keine Karte da ist (der Kartenwert ist der
genauere).

**Die BEDENKZEIT ist eine zweite Seite** (Crawler `GET /api/tournament-search/tournament-info?id=`):
die Spielersuche fuehrt sie nicht, und ohne sie stehen eine Blitz- und eine
Turnierschach-Performance in derselben Spalte, als waeren sie vergleichbar. Sie landet in
`TournamentTimeControls` (je Turnier einmal) und reist als `speed` mit jedem Verlaufs-Eintrag mit.

**Zwei Fallen, die dabei live aufgetreten sind:**
1. **Ein GET liefert die Turnierdetails NICHT.** chess-results blendet sie bei allem, was laenger
   als fuenf Tage vorbei ist, hinter einem Knopf aus („all links for tournaments older than 5 days
   are shown after clicking the following button") — dahinter ein ASP.NET-Postback auf
   `cb_alleDetails`. Ohne ihn kommt die Startrangliste zurueck und jedes Feld ist `null`.
2. **Das Label traegt die KLASSE**: „Time control (Standard)", nicht „Time control". Diese Angabe
   schlaegt die Ableitung aus dem Freitext (`TournamentSpeedClassifier` rechnet nur, wenn sie
   fehlt) — „90 Min. / 40 Zuege + 30 Min. + 30 Sekunden ab Zug 1" richtig zu addieren ist Raten.

**JEDER gecrawlte Bestand traegt eine FASSUNG, nicht nur einen Zeitstempel.** „Geprueft am 15:50"
sagt NICHT, WOMIT geprueft wurde: faellt ein Parser-Fehler spaeter auf, ist der Vermerk eine Luege,
die jede Wiederholung verhindert — und der Behelf waere ein manueller Schalter, den jemand kennen
und ausfuehren muss. Steht die Fassung unter der aktuellen, holt der naechtliche Durchgang den
Eintrag genau EINMAL nach.

| Datenart | Fassung | Erhoehen, wenn sich aendert |
|---|---|---|
| Spielerkarte | `TournamentHistoryService.CurrentCardVersion` | die Karten-Felder oder ihr Parser |
| Bedenkzeit | `TournamentHistoryService.CurrentTimeControlVersion` | der Weg zu den Turnierdetails oder ihr Parser |
| Rundenplan | `TournamentRoundPlanService.CurrentVersion` | `FindTableByHeaders` / `ParseRoundPlanAsync` |
| Spielort ueber Vereinsnamen | `VenueDisambiguationService.CurrentVersion` | die Aufloesungs-Regel |
| FIDE-Details | `FideEventDetailService.CurrentVersion` | `ParseEventAsync` oder der Weg zum Fragment |
| Polen-Detailseite | `ChessArbiterDirectorySweepService.CurrentDetailVersion` | `ParseDetail` im Crawler liest mehr oder anderes |

**Eine Fassung JE DATENART, keine globale Crawler-Version**: ein Fix am Rundenplan-Parser sagt
nichts ueber die Spielerkarte aus, und eine globale Zahl holte bei jedem Crawler-Release Tausende
Seiten neu (bei ~6 s je Abruf hinter dem Rate-Limiter: Stunden).

**`RoundPlanVersion` wird MIT `RoundPlanCheckedAt` geleert**, wenn der Sweep eine Terminaenderung
sieht: die beiden beantworten verschiedene Fragen („fuer diesen Termin schon nachgesehen" und „mit
welchem Parser"), und eine Fassung ohne Vermerk behauptete etwas ueber einen Abruf, den es nicht
mehr gibt.

Die drei Belege, an denen das Muster entstand: der Bestand haette die nachtraeglich ergaenzte
Partienzahl NIE bekommen; ein Postback-Fehler waere als „dieses Turnier nennt keine Bedenkzeit"
eingefroren; und 452 Eintraege trugen einen Rundenplan-Vermerk aus der Zeit vor der
Parser-Reparatur (tnr1438343: null gespeicherte von neun abrufbaren Terminen).

**Die PARTIENZAHL kommt von der Spielerkarte** (`GamesPlayed`), nicht aus der Rundenzahl: in einer
Liga wird ein Spieler an einem TEIL der Termine aufgestellt, die Trefferliste meldet trotzdem alle
elf Runden. Gezaehlt werden die Kartenzeilen MIT Gegner — ein Freilos ist keine Partie.
