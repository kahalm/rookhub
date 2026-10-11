# Meisterpartien im Hintergrund analysieren (2026-09-28) — `MasterAnalysisScheduler`

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → Meisterpartien im Hintergrund analysieren (2026-09-28) — `MasterAnalysisScheduler`“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.

Wunsch: „zu den gleichen Zeiten wie die Übersetzung auch Analyse der Meisterpartien — auf allen 16 Direktengines, aber
wenn ein anderer Auftrag reinkommt, hat der Vorrang". Drei Bausteine:
- **Takt** (`Services/Library/MasterAnalysisScheduler.cs`, Hosted Service): außerhalb der Sperrzeiten der Spark (`QuietHours`,
  dieselbe Angabe `TextLlm:QuietHours` wie Übersetzung und Texte — Vorgabe Mo–Do 08–17, Fr 08–14 gesperrt) legt er
  Bibliothekspartien ohne Analyse als `GameAnalysisOrigin.Library` an — erst die kommentierten, dann der Rest, je nach
  Id; eine neue erst, wenn die laufenden zusammen weniger offene Stellungen haben als der Besitzer Hintergrund-Engines
  (derselbe „Schwanz" wie 0.543.0). Besitzer = Haus-Engine-Besitzer (Admin mit „als Haus-Engine teilen"), abweichend
  `MasterAnalysis:OwnerUserId`; abschalten mit `MasterAnalysis:Enabled=false`. Tiefe/Linien wie eine angeforderte
  Bibliothekspartie (`GuessTargetDepth`, 5 Linien, `CreateLibraryBatchAsync`). In der Sperrzeit reiht die laufende
  Partie keine Stellungen ein (`EnqueueNextAsync`); was schon eingereiht ist, läuft zu Ende.
- **Vorrang**: jeder Auftrag einer `Library`-Analyse ist `Background`. Neu seit diesem Stand: ein normaler Auftrag
  VERDRÄNGT einen laufenden Hintergrund-Auftrag auf seiner Engine (`IAnalysisJobControl.PreemptBackground`, aufgerufen in
  `AnalysisJobService.CreateAsync`) — wie der Live-Vorrang auf `Paused`, ohne Fehlversuch; vorher wartete er, bis die
  Vertiefung/Meisterpartie auf dieser Engine fertig war. Gilt auch für die Vertiefung eigener Partien.
- **Sichtbarkeit**: `Library`-Analysen gehören dem Besitzer, sind aber für ALLE lesbar (`GetPlayableHeadAsync`,
  Punktepartie starten, Bibliothek: `InPool`/`RequestAsync` nimmt sie statt neu zu rechnen) — und bewusst NICHT
  `IsPublic`: `ListPublicAsync` (Punktepartie-Bestand) ist ungeblättert und läge sonst unter >100 000 Partien begraben.
  Nicht in `ListAsync` des Besitzers, nicht in seiner Auftragsliste (`/api/analysis-jobs`), nicht in seiner
  Partien-Reihenfolge (`IsOwnersTurnAsync`, `OpenFirstPassPliesAsync`) — sonst hielte eine Meisterpartie seine eigene auf.
- **Speicher**: ~500 B je Stellung + ~15 KB je Analyse (PGN-Kopie) → bei einigen tausend Partien je Woche grob
  +0,4 GB/Woche, beim ganzen Bestand (130 544 Partien, 94 881 kommentiert) ~10 GB. Plattenstand im Blick behalten.
- **Vereinspartien zuerst** (0.588.0, Wunsch „wirf die Partien aus dem Vereinsverzeichnis auch immer in die Analyse"):
  jede Partie der LeagueHub-Vereins-Datenbank (`LeagueClubGame`) ohne Analyse kommt VOR der nächsten Meisterpartie dran —
  `GameAnalysisOrigin.Club = 4`, verknüpft über `GameAnalysis.LeagueClubGameId` (Index, kein FK), angelegt mit
  `CreateClubBatchAsync` (Titel aus dem PGN-Kopf). Dieselben Zeiten, Engines und derselbe Vorrang; beide Etiketten
  zählen gemeinsam in „offene Stellungen < Engines" und stehen in jeder Ausschluss-Abfrage ausgeschrieben
  (`Origin != Library && Origin != Club`; für C#-Seiten `GameAnalysisOrigins.IsBatch`). Kein Zeiger wie bei den
  Meisterpartien: je Takt die kleinste Id ohne Analyse (es sind wenige, neue kommen jederzeit dazu). **Anders als eine
  Meisterpartie NICHT für alle lesbar** (`GetPlayableHeadAsync`/Guess-Start lassen `Club` weg) — die Vereins-Datenbank
  sieht nur, wer in LeagueHub `league.view` hat. **Die Analyse trägt die Namen der Partie**: `LeagueClubService`
  bekommt `GameAnalysisService` (optional, Tests ohne) — `DeleteAsync` löscht vorher die Analyse samt offenen Aufträgen
  (`DeleteForClubGameAsync`), `UpdateAsync` zieht Namen/Ergebnis/Veranstaltung/Titel/PGN nach (`SyncClubGameAsync`,
  die Züge ändert eine Korrektur nie) — sonst bliebe ein zu „Schwaz" korrigierter Name in der Analyse stehen.
- **Für alle im Verein** (0.593.0, Wunsch „die Partien sollen allen aus dem Verein zur Verfügung stehen"): die Analyse
  liest jeder mit `league.view` — der Zugang hängt an der PARTIE, nicht am Besitzer der Analyse. `GET
  /api/league/club/games` trägt je Zeile `analysis` (Stand + Genauigkeit wie in „Meine Partien"), `GET
  …/games/{id}` dieselbe Zeile mit frischem Stand, `GET …/games/{id}/evals` die Bewertungen (ohne Buchzüge). Gelesen wird über
  `Services/Games/GameEvalsStore.cs` — DIESELBEN zwei Wege wie bei den gespeicherten Partien (`SavedGameService` nutzt ihn
  seither auch): `ReadAsync` (Kurve/Zug-Klassen) und `StatesAsync` (Listen-Stand samt Genauigkeits-Nachtrag). Welche
  Analyse zu einer Vereinspartie gehört: die jüngste mit `Origin = Club`. LeagueHub: Spalte „Analyse" (Genauigkeit
  Weiß · Schwarz bzw. Fortschritt) und „Nachspielen" (klappt `lh-game-replay` mit `evalsUrl` auf — darunter RookHubs
  `GameReviewComponent` mit `[withExplanations]="false"`: „Warum war das ein Fehler?" gibt es für Vereinspartien nicht,
  jede Nachfrage wäre ein 404). Über Teilen-Links bleibt die Vereins-Datenbank unlesbar.
- **„Analyse" = RookHubs Partieseite** (0.653.0, Wunsch 2026-10-04: „sollte das Spiel wie aus Meine Games aufmachen, mit
  unten den vorberechneten Werten"): der Knopf springt per Einmal-Code auf `/club-games/{id}` — `SharedGameComponent` mit
  `data.mode = 'club'`: Partie aus `GET /api/league/club/games/{id}`, Kurve aus `…/evals`, kein „Partie analysieren", keine
  Erklärungen, Jahr statt Datum. Das Recht prüft nur der Server (`league.view`, sonst „konnte nicht geladen werden").
- **Eine kopierte Vereinspartie wird NICHT ein zweites Mal gerechnet** (0.653.0, Wunsch: „wenn jemand das Game lokal kopiert,
  soll es nur einmal analysiert werden"): `SavedGameService.ClubAnalysisForMovesAsync` findet die nicht gescheiterte
  Club-Analyse einer Vereinspartie mit GENAU denselben Zügen ab der Grundstellung (`LeagueClubService.HashOf` über die SAN,
  dieselbe Schreibweise wie beim Upload). Der PGN-Import (`POST /api/games/import`, also auch „Zu meinen Partien" aus
  LeagueHub) verknüpft sie gleich beim Anlegen, „Partie analysieren" nimmt sie als dritte Stufe vor dem Neu-Einwerfen.
  Preis: die Kopie bekommt die Vereins-Analyse (Tiefe 20, keine Vertiefung auf 30). Die umgekehrte Richtung (eine schon
  analysierte eigene Partie wird in die Vereins-Datenbank hochgeladen) rechnet weiterhin neu.
- **Liga-Partien danach** (0.665.0, Wunsch „analysier im Hintergrund auch alle Partien für LeagueHub — zumindest von
  Spielern, die aktuell in der Liga mitspielen; erst die neuesten (2 Jahre zurück), dann bevorzugt die Gegner von Schwaz
  nächste Runde, dann der Rest, und erst dann wieder die Meisterpartien"): `Services/League/LeagueAnalysisQueue.cs`
  (Singleton, im Takt NACH den Vereinspartien, VOR den Meisterpartien). Quelle sind die Profile
  (`LeaguePlayerProfile.Pgn`) aller Spieler mit FIDE-ID auf einer Meldeliste der laufenden Saison, nur Partien der letzten
  `Years` (2) Jahre ab der Grundstellung; zuerst die Meldeliste des Gegners jeder Schwazer Mannschaft in ihrer kleinsten
  Runde ohne Ergebnis, dann alle übrigen, je neueste zuerst. Dieselbe Partie in zwei Profilen zählt einmal. Die Liste lebt
  im Speicher und wird stündlich neu gebaut. Angelegt mit `GameAnalysisOrigin.League = 5` (`CreateLeagueBatchAsync`,
  `IsBatch`, dieselben Ausschlüsse wie Library/Club — nicht für alle lesbar, in keiner Liste) und dem Zug-Schlüssel
  `GameAnalysis.MovesHash` (= `LeagueClubService.HashOf` der kanonischen SAN, Index). Übersprungen wird, was schon eine
  nicht gescheiterte Analyse mit diesem Schlüssel oder eine Club-Analyse derselben Züge hat; `ClubAnalysisForMovesAsync`
  nimmt Liga-Analysen über `MovesHash` mit (Kopie nach „Meine Partien", „Analyse" auf der Spielerkarte).
