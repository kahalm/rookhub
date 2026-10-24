# LeagueHub — Vereins-Datenbank (0.573.0, Übersicht/Teilen-Link/Megabase 0.574.0)

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „REST API → LeagueHub — Vereins-Datenbank (0.573.0, Übersicht/Teilen-Link/Megabase 0.574.0)“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Mitglieder von SK Schwaz laden Partien hoch — viele auf einmal als PGN oder EIN Partieformular (Foto, gelesen vom
Formular-Leser, geprüft in LeagueHub) —, angemeldet ODER ohne Konto über einen Teilen-Link, und die Spielerkarten der
Gegner zeigen sie mit (Quelle „Verein"). Regeln (`Services/League/LeagueClubService.cs` = Fassade über `ClubPgnReader` (Einlesen, PGN in `ClubPgnText`),
`ClubSideResolver` (wer ist welche Seite), `ClubGameReader` (Lesen), `ClubGameWriter` (Schreiben); alle vom Nutzer vorgegeben,
2026-09-28):

* **Beide Namen gegen die Meldelisten** (`LeagueRosterIndex`, alle Saisonen; gecacht im allgemeinen `IMemoryCache`,
  Schlüssel Verein + COUNT/MAX(Id) `LeaguePlayers` + COUNT `LeagueTournaments`, 10 min — `LeagueClubService.RosterAsync`,
  Codereview F7-007): FIDE-ID aus der Partie zuerst, dann der
  Name in drei Stufen (alle Namensteile in beliebiger Reihenfolge → Nachname + erster Vorname → Nachname +
  Anfangsbuchstabe), ohne Groß/klein, Akzente, akad. Titel, Umlaute in beiden Schreibweisen. Nennt die Partie NUR einen
  Nachnamen („Kostic", 0.576.0), gilt als vierte Stufe der Nachname allein — eindeutig nur bei genau einem Ligaspieler
  (`Hit.LastNameOnly`, die Übersicht zeigt „nur Nachname — prüfen"), sonst mehrdeutig mit Kandidaten. Mehrdeutig = Ligaspieler
  OHNE FIDE-ID, die Kandidaten gehen zur Auswahl mit. Eine Meldelisten-Zeile OHNE FIDE-ID gehört zu der Person MIT
  FIDE-ID, deren Name genau so lautet, wenn es genau eine gibt (0.575.1 — 2022/23 steht „Hengl Philip" ohne Komma und ID
  neben „Hengl, Philip" mit ID; als zwei Personen war jeder Abgleich „mehrdeutig"). **Dieselbe Person zweimal** (0.597.0,
  gemeldet 2026-09-29): ohne FIDE-ID zählt der NAME (alle Namensteile, Reihenfolge und Komma egal), nicht seine Schreibweise —
  „Lenk Markus" und „Lenk, Markus" waren zwei Personen, auf Dev 123 solcher Paare, jedes „mehrdeutig"; und gleicher Name +
  gleicher Verein (ohne Mannschaftsnummer, `ClubBase`) unter verschiedenen FIDE-IDs ist EIN Mensch, es gilt die ID der
  jüngsten Saison, die übrigen führen über `ByFide` zu ihm (`CanonicalFides`; „Forster, Stephan" 24649651 → 24652091 — die
  alte kennt FIDE nicht —, „Perez Rodriguez" 168265 → 1682865). Namensvettern in verschiedenen Vereinen bleiben mehrdeutig. Eine fremde FIDE-ID in der Partie lässt nur Ligaspieler ohne
  eigene ID als Namenstreffer zu. **Wer in keiner Meldeliste steht, wird im Megabase-Verzeichnis gesucht** (0.576.0,
  Wunsch „standardmäßig auf Megabase matchen, wenn in Tirol kein Treffer"; `LeagueMegaPlayers.Lookup`, EINE Abfrage je
  500 Namen für die ganze Übersicht): über die FIDE-ID, sonst den Namen — „Nachname, Vorname" wie im Verzeichnis, ohne
  Komma beide Reihenfolgen. Eindeutig heißt: die Treffer tragen höchstens EINE FIDE-ID (dann gilt der mit ihr), ganz ohne
  ID nur bei einem einzigen Namen; zwei Namensvettern mit verschiedenen IDs sind „nicht erkannt". So eine Seite heißt
  „nicht in Liga" (`match.mega`), Name und FIDE-ID kommen von dort. **Bekannt = Ligaspieler ODER Megabase**: ist keine
  Seite bekannt → `noLeaguePlayer`; bleibt nach dem Ersetzen keine bekannte übrig → `onlyOwnClub`. Eine Partie, deren
  Gegner nur die Megabase kennt, ist übernehmbar, die Übersicht wählt sie aber NICHT vor (`optionalGame` in
  `import-review.ts`); wer an einer Partie einen Spieler wählt oder einen Namen übernimmt, wählt sie damit zum Import aus.
* **Ähnliche Namen zur Schnellauswahl** (0.596.0, Wunsch „wenn du Namen nicht direkt findest, schau, ob ein ähnlicher Name
  bei den Ligaspielern existiert, und stell ihn zur Schnellauswahl — ohne dass man bearbeiten muss"; `LeagueRosterIndex.Similar`):
  bleibt eine Seite unerkannt (weder Liga noch Megabase noch gemerkte Zuordnung, nicht mehrdeutig), trägt ihr Abgleich
  `similar` = höchstens drei Ligaspieler. Verglichen wird je Namensteil (Reihenfolge egal, Titel weg, Umlaute in beiden
  Schreibweisen) mit Tippfehler-Abstand (Damerau, benachbarte Vertauschung = ein Fehler), erlaubt je Teil nach Länge: unter 5
  Buchstaben KEINER („Wolf" ≠ „Golf"), bis 7 einer, sonst zwei; ein Anfangsbuchstabe muss passen; ein exakter Treffer ist
  keiner (den findet schon der Abgleich). In der Übersicht (`quickPicks` in `import-review.ts`) und der Formular-Prüfung stehen
  sie als „Meintest du …"-Knöpfe unter dem Namen — ein Klick ist `choosePerson`, also samt Weitergabe an gleichnamige Seiten und
  Merken beim Import. Je PGN-Name wird einmal gerechnet (Übersicht mit 500 Partien).
* **Gemerkte Zuordnungen** (0.579.0, Wunsch „wenn ich einen Spieler umbenenne, merk dir das zum Original und matche das
  zukünftig bei allen selbst"; `LeagueNameAliases`, Tabelle `LeagueNameAliases`): landet beim Import eine Korrektur (Spieler
  gewählt oder Name getippt) bei jemand ANDEREM als die Vorgabe, merkt `ImportPgnAsync` den PGN-Namen (`KeyOf`: klein, ohne
  Akzente und Titel) → FIDE-ID + Name; die jüngste Korrektur gewinnt. Abgleich-Reihenfolge (`ClubSideResolver.Resolve`):
  FIDE-ID der Partie, die ein Ligaspieler trägt → gemerkte Zuordnung → Meldelisten → Megabase; eine vom Nutzer gewählte
  FIDE-ID schlägt die Zuordnung. Gilt in Übersicht, Import, Formular-Abgleich (`match`) — für ALLE, auch über Teilen-Links,
  gemerkt wird aber nur mit Konto (ein Teilen-Link soll nicht festlegen, wer ein Name für alle ist). Die Zeile verweist
  bewusst weder auf eine Partie noch auf den, der korrigiert hat. Im selben Import überträgt die Seite eine Korrektur auf jede
  andere noch nicht gesetzte Seite mit demselben PGN-Namen (`ImportReview.propagate`). Ein Formular merkt (noch) nichts — der
  gelesene Name geht nicht mit.
* **Spieler von Schwaz werden durch „Schwaz" ersetzt** (Vorgabe: jeder, der in seiner JÜNGSTEN Saison für Schwaz gemeldet
  ist — `Person.OwnClub`; wer weggegangen ist, ist jetzt ein Gegner —, dazu der Hochladende laut Profil; je Seite
  umschaltbar): ohne Elo und FIDE-ID, die Veranstaltung fällt weg, und es wird **weder gespeichert, wer dahinter steht,
  noch wer hochgeladen hat oder wann** (`UploadedByUserId`/`CreatedAt` leer, auch nicht versteckt). Ohne Konto
  (Teilen-Link) wird der Hochladende nie gespeichert. **Seit 0.648.0 steht der ECHTE Spieler hinter „Schwaz" intern**
  (`LeagueClubGame.WhiteRealName/WhiteRealFide/BlackRealName/BlackRealFide`, Wunsch 2026-10-04: „für spätere
  Auswertungen, niemals in der GUI ausgeben") — gesetzt in `Build` und beim Korrigieren (eine unveränderte Seite behält
  ihn). Diese Spalten gehen in KEIN DTO, kein PGN, keine Analyse, keine Spielerkarte, keinen Teilen-Link, keine Suche
  (`Import_Replaced_KeepsTheRealNameInternally_ButNoOutputCarriesIt`); Häkchen-Text und Datenschutzerklärung nennen es.
  Partien von vor 0.648.0 haben sie leer.
* **Über einen Teilen-Link hochgeladen** (Codereview 2026-09-29, A2-009 — vorher 500 Partien je Aufruf ohne jede Herkunft):
  jede Partie trägt den Link als SHA-256 (`UploadShareHash`, `LeagueClubService.ShareHashOf`; der Link selbst steht
  nirgends) — AUCH eine „Schwaz"-Partie: der Link ist der Weg, nicht die Person, und nur so entfernt der Rückbau alles.
  Den Zeitpunkt bekommt nur eine Partie ohne „Schwaz", eine IP wird bewusst NICHT vermerkt (ein Absender-Pseudonym an der
  Partie widerspräche „der Hochladende wird nie gespeichert"). Gedeckelt (`LeagueShareUploadQuota`, Singleton, im
  Arbeitsspeicher — über `CreatedAt` gezählt, liefen „Schwaz"-Partien durch): höchstens 50 Partien je Aufruf und 200 je
  Link und UTC-Tag, auch das Formular-Add ohne Foto; der Rest steht mit Grund `shareLimit` in `failed` (bzw. 400). Die
  Plätze werden vorher reserviert und, was nicht gespeichert wird, zurückgegeben (ein abgerissener Aufruf verbraucht
  nichts). „Alle Partien dieses Links entfernen": `DELETE /api/league/club/admin/shares/{token}/games` (Verwalter).
  Hash und Deckel hängen am Token der Link-ZEILE (`LeagueService.ValidShareTokenAsync`), nie am Wert aus der Route:
  `LeagueShares.Token` vergleicht in MariaDB groß/klein- und akzent-blind, „abc…"/„Ábc…" sind derselbe gültige Link wie
  „AbC…" — sonst hätte jede Schreibweise ihren eigenen Topf, und der Rückbau per Original fände ihre Partien nicht. Der
  Rückbau löst das Token ebenso über `LeagueShares` auf (ohne Ablauf-Filter; ist die Zeile weg, zählt der Wert, wie er kommt).
* **PGN = zwei Schritte**: `games/preview` liest und gleicht ab, speichert NICHTS — je Partie wer gegen wen, Abgleich,
  Vorgabe „ersetzen", `duplicate`, harter Fehler (`illegal`/`noMoves`/`tooLong`/`fromPosition`). Die Seite
  (`features/club/import-review.ts`, dieselbe Regel wie `Build`) zeigt je Partie, ob sie übernommen wird; der Nutzer
  korrigiert unerkannte/falsche Spieler (Kandidat, Vorschlag aus der Meldeliste, getippter Name), schaltet „ersetzen"
  um, wählt Partien ab. Filter (0.577.0, je mit Anzahl): alle · nicht importiert · noch nicht vorhanden (keine Dublette)
  · nicht erkannt; die Liste zeigt das Turnier (`[Event]`) zum Zuordnen — gespeichert wird es weiter nur ohne „Schwaz". `games/import` bekommt DENSELBEN PGN-Text + je übernommener Partie die Entscheidung
  (`LeagueClubSideDecision`: FIDE-ID eines gewählten Ligaspielers schlägt den Namen; ohne Angabe die Kopfzeile) und
  prüft alles noch einmal. Nummerierung 1-basiert, gleich in beiden Schritten.
  **Große Listen in Paketen** (0.598.1, Wunsch „auch beim PGN-Upload alle einlesen und dann in Paketen anbieten"): passt
  eine Liste nicht in EINE Übersicht (`MaxImportGames` 500, `MaxImportChars` 5 Mio.), teilt die Seite sie VORHER
  (`src-leaguehub/app/core/pgn-portions.ts`, `pgnPortions`: 500 Partien bzw. 4,5 Mio. Zeichen je Paket) — für JEDE Quelle
  (Datei, eingefügt, Lichess-Studie, ChessBase), in `startPreview`. Das erste Paket geht in die Übersicht, die übrigen
  werden danach als offene Listen abgelegt („Datei.pgn (Teil 2 von 3)", Quelle wie das Original); am Deckel der Entwürfe
  (20 je Konto, 5 je IP) sagt die Seite, wie viele fehlen. Getrennt wird mit der Regel des Servers
  (`splitGameBlocks` = SPIEGEL von `PgnParser.SplitGamesCore`: Kopfzeile nach Zugtext, wiederholte Kopfzeile, nichts in
  einem offenen `{…}`), Partien ohne Zugtext zählen nicht — sonst hätte ein Paket beim Server mehr als 500 und würde
  gekappt. Eine Liste, die passt, geht UNVERÄNDERT raus. `truncated` der Übersicht bleibt als Rückfall stehen.
  **Importiert wird PORTIONSWEISE** (0.590.0, Wunsch „damit Progress nicht verloren geht“): die Übersicht liefert je
  Partie ihren eigenen PGN-Text (`games[].pgn`, Kopfzeilen roh wie gelesen, Zugtext unverändert; fehlt bei harten
  Fehlern), die Seite (`club-import-review.component.ts`) schickt je 10 Partien genau deren Text mit Nummern 1…n —
  jede Portion wird sofort gespeichert. 10 statt 1, weil der globale Deckel 100 Anfragen/min je IP ist (Teilen-Link 60):
  500 Partien sind so 50 Anfragen. Eine abgerissene Portion wird zweimal wiederholt (429 wartet 20 s), eine Absage (400)
  nicht; war sie schon gespeichert und nur die Antwort verloren, zählt der Server sie als doppelt. Scheitert es endgültig,
  bleibt das Gespeicherte, und „Weiter importieren (n übrig)“ schickt nur den Rest. Ohne `pgn` (älterer Server) geht
  alles in einer Anfrage wie vorher.
* **Entwürfe** (0.595.0, Wunsch „wenn jemand eine neue Ligapartie einträgt — egal wie — soll sie gleich online abgelegt
  werden, damit ein Admin den Import fertigstellen kann“; `Services/League/LeagueClubDraftService.cs`, Tabelle
  `LeagueClubDrafts`): beim Lesen der Übersicht legt die Seite die Liste sofort als Entwurf ab (Datei, eingefügt,
  Lichess-Studie, Sprung aus RookHub), speichert den Stand der Übersicht gedrosselt (`ImportReview.snapshot`, für den
  Server opak) und nach jeder Portion die importierten Nummern. „Deine offenen Listen“ nimmt sie wieder auf
  (`ImportReview.restore` auf einer frischen Übersicht; schon importierte Partien sind dann Dubletten; seit 0.597.1 kommt aus
  dem Stand nur, was der Nutzer SELBST gesetzt hat — jede andere Seite nimmt den frischen Abgleich, „ersetzen" bleibt, solange
  die Seite gleich erkannt wird), Verwalter sehen
  unter „Offene Listen anderer“ ALLE (auch über Teilen-Links) und stellen fertig. **Mit `draftId` rechnen Übersicht und
  Import für den EINREICHER** (`ActingUserAsync`): seine Seite „du selbst“ wird ersetzt, die Partien tragen ihn als
  Hochladenden, ohne Konto eingereicht niemanden — nicht den Verwalter. Fertig importiert oder verworfen wird die
  Zeile GELÖSCHT (der Rohtext nennt die Spieler von Schwaz mit Namen), ebenso nach 30 Tagen ohne Bewegung und beim
  Kontolöschen. Deckel: 20 offene je Konto, 5 je IP ohne Konto (HMAC wie bei den Formularen); ohne Konto gehört der
  Entwurf dem Browser mit dem Schlüssel (`lh-anon-drafts`). „Schließen" in der Übersicht lässt den Entwurf liegen;
  endgültig verwerfen nur aus „Deine offenen Listen“/„Offene Listen anderer“ mit Rückfrage (bei fremder Liste mit dem
  Namen des Einreichers; Codereview F7-004).
* **ChessBase-Datenbanken** (0.598.0, Wunsch „ein Import für 2cbh und cbh zusätzlich zu PGN — bau es selbst in C#
  nach"; `Services/ChessBase/`): `POST …/club/games/chessbase` (angemeldet und über den Teilen-Link) nimmt die Dateien EINER
  Datenbank (einzeln, einzeln gepackt als `name.2cbg.gz` oder als ZIP) und antwortet mit einem PGN — gespeichert wird dabei
  nichts, danach ist es derselbe Weg wie eine PGN-Datei. Regeln, die dabei nicht kippen dürfen:
  - **Eigener Code nach Morphys reverse-engineerter Formatbeschreibung** (`format/v1`, `format/v2`; ChessBase dokumentiert
    keins der beiden). Morphy hat KEINE Lizenz — übernommen sind nur die Fakten (Offsets, Tabellen, Kodierung), kein Code,
    und seine Testdatenbanken gehören NICHT ins Repo. oschess (C) war bis v1.0.3 MIT (ab v1.0.4 AGPL) und diente nur zum
    Gegenlesen. Die Datenbanken des Nutzers auch nicht: sie tragen echte Namen — `ChessBaseFixtureTests` läuft nur lokal
    mit `CHESSBASE_FIXTURES=<Ordner>` (je Datenbank ihre Dateien + ein PGN-Export aus ChessBase) und vergleicht Kopfdaten
    und Hauptvariante als UCI. Stand 29.09.: `.2cbh` 78/78 gleich; `.cbh` gleich bis auf den auf 40 Zeichen gekürzten
    Turniernamen (steht so in der `.cbt`); Morphys WM-Sammlung in beiden Formaten (2 × 1025) mit denselben Zügen.
  - **Hauptvariante = alles vor dem ersten Linienende** — klassisch das erste `255` (Kodierung 0/4/5 über `CbhTables`,
    Chess960-Kodierungen 10/11 werden übersprungen), `.2cbg` die Wörter bis zum ersten `ffff`. Varianten und Anmerkungen
    (`.cba`/`.2cba`) liest niemand; dafür schickt die Seite nur `ChessBaseFiles.Upload` (SPIEGEL von
    `CHESSBASE_UPLOAD_EXTENSIONS` in `src-leaguehub/app/core/chessbase-upload.ts`, beide mit literalem Test).
  - **Das 2CBH-Zugwort wird aus Regeln aufgezählt** (`Cb2MoveTable.Build`, 0xb129 Einträge — König, Dame, Springer, Läufer,
    Turm je Farbe, dann Bauern, dann Rochaden); die Blockgrenzen stehen literal in `ChessBaseReaderTests`. Wer daran dreht,
    lässt den Fixture-Test laufen.
  - **Gespielt wird auf Gera.Chess direkt** (`MainlineBoard.Play`: `Move(new Move(von, nach))`, SAN aus `ExecutedMoves[^1]`)
    — die Kandidaten der Figur aufzuzählen und für jeden die SAN zu rechnen war sechsmal so langsam (3,5 ms statt ~1 ms je
    Partie). Nur Umwandlungen gehen über die Liste (direkt wählt Gera immer die Dame). Ein Zug, der nicht geht, macht die
    PARTIE zur übersprungenen (`ChessBaseGame.Error`, in der Antwort `skipped` mit Nummer), nie den ganzen Upload.
  - **Grenzen**: Rumpf 15 MB (`ChessBaseImportService.MaxBodyBytes` = die allgemeine `/api/`-Regel des Frontend-nginx —
    die Seite packt deshalb jede Datei per `CompressionStream('gzip')`, eine kommentierte `.cbg` auf ein Siebtel),
    ausgepackt 64 MB (`ChessBaseFiles.MaxTotalBytes`, beim LESEN gezählt — ZIP- und gzip-Bomben), 5000 Partien
    (`MaxGames`, `truncated`), höchstens 2 Umwandlungen gleichzeitig (sonst 429 `busy`). Das Aufteilen in Pakete ist
    derselbe Weg wie bei jeder Liste (siehe „Große Listen in Paketen").
* **Nur das JAHR** (`Date "2024.??.??"`), nur die Hauptvariante OHNE Kommentare, nur ab der Grundstellung
  (`fromPosition`). Dubletten: gleiche Züge (`MovesHash`) im gleichen Jahr; unter 20 Halbzügen zusätzlich gleiche Namen.
* **Spielerkarten** (`Services/League/LeagueProfileStore.cs`): `LeaguePlayerProfile.Pgn` hält NUR die fremden Partien
  (Lumbra, chess-results, Megabase); Karte, Partienzahl, PGN-Download und Eröffnungsbaum nehmen die Vereinspartien dazu
  (`WithClub`, Doppelte über Jahr + Hauptvariante — die fremde Fassung gewinnt). Nach jedem Upload/Löschen werden die
  betroffenen Karten neu gerechnet und die Partienzahl `g` in den fertigen Ansichten nachgezogen (`PatchViewCountsAsync`).
  Teilen-Links liefern die Vereinspartien im PGN mit („pgn sind nicht geschützt").
* **Partieformular**: dieselbe Einlesung wie in RookHub (`ScoresheetScan.Purpose = "league"`) und dieselbe Kostenbremse,
  aber eine EIGENE Tageszahl: **10 je Nutzer und 24 h** (`Scoresheet:LeagueDailyLimit`, RookHub bleibt bei 1); KEINE
  Partie in „Meine Partien", keine Glocke, nicht in `GET /api/scoresheets`. **Ohne Konto** (Teilen-Link):
  `ScoresheetScan.UserId` ist `null`, die Einlesung gehört dem Browser mit dem geheimen `AccessKey` (32 Hex, einmal beim
  Hochladen ausgegeben, LeagueHub merkt ihn im localStorage `lh-anon-scans`); **höchstens 10 je IP und 100 je Tag für
  alle zusammen** (`AnonPerIpDailyLimit`, `AnonDailyLimit`, Absage `dailyLimit` bzw. `anonDailyLimit`), drei offene je
  IP, Geld: das eigene Tagesbudget ohne Konto (`Scoresheet:AnonDailyUsd`, 3 $) UND das Gesamtbudget; Zählprüfung und
  Anlegen laufen nacheinander (Semaphor, parallele Uploads schlüpfen nicht durch). Gezählt wird über `AnonIpHash` =
  HMAC der IP (Schlüssel `Jwt:Key`; IPv4 bzw. IPv4-in-IPv6 als Adresse, echtes IPv6 je /64) — die Adresse selbst
  steht nirgends, der Vermerk wird nach zwei Tagen geleert. Übernehmen oder Verwerfen schließt die Einlesung
  (`CloseLeagueScanAsync` = `DetachWithoutLoading` + Dateiname „∅" + Schlüssel weg): Foto und Lesung gehen, die Zeile
  bleibt fürs Kontingent — und nichts verbindet sie mit der Partie.
* **Nie geprüfte Formulare** (0.581.0, Wunsch „damit die nicht im Limbo sind"): Verwalter (`league.manage`) sehen unter
  `GET /api/league/club/admin/scans` ALLE offenen Liga-Einlesungen (auch über Teilen-Links, `viaShareLink`, `mine`) und
  dürfen jede öffnen, übernehmen, verwerfen (`ScanActor.ManagerOf`, `LeagueOwned` ohne Eigentümer-Filter; der Controller
  nimmt ihn für jeden Verwalter). LeagueHub zeigt sie im Formular-Reiter unter „Offene Formulare anderer".
* **Nach dem Prüfen** (0.581.0): „PGN herunterladen" / „PGN kopieren" (`sheetPgn` in `core/club-format.ts`, Namen wie im
  Formular — die Datei bleibt beim Nutzer) und angemeldet „Zu meinen Partien hinzufügen" (`POST /api/games/import`,
  Link zurück nach RookHub über `rookHubUrlForLeagueHub`). Nach dem Übernehmen bleibt die Seite dafür offen.
* **Dauer** (0.581.0, Wortlaut seit 0.689.0): Hinweis „der Leser schaut alle 5 Minuten" bzw. „dauert meist ein paar Minuten"
  (seit 0.687.0 liest der Watcher auf dem Server; die alten „1–2 Sekunden pro Zug" galten fürs Modell) und eine Uhr ab dem Hochladen (`scoresheet-timing.ts`:
  `serverTime` liest Zeiten ohne Zone als UTC — aus der Datenbank kommen sie so —, `SecondsTicker` läuft nur, solange
  gelesen wird). Gemessen auf Prod: 43/59/104 Einträge in 22/31/73 s.
* **Löschen**: `league.manage` alles, sonst nur eigene Partien ohne „Schwaz". Konto löschen setzt `UploadedByUserId` null.
  Alles eines Teilen-Links auf einmal (samt Analysen, Karten neu gerechnet): `DeleteByShareAsync`.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/club/games?fide=&q=&page=` | view | Liste (50 je Seite, Jahr absteigend) mit `opening`, `canDelete`, `uci` und `pgn` — „Analyse" öffnet RookHubs Analysebrett mit dem GANZEN PGN (`/analysis?pgn=`, 0.592.0; über 6 000 Zeichen Adresse nur die Züge `?moves=`), `analysis` (Stand der Hintergrund-Analyse, 0.593.0), `whiteInRoster`/`blackInRoster` = Seite ohne FIDE-ID, deren Name in einer Meldeliste steht (0.594.0) — dort zeigt die Seite „(ohne FIDE-ID)“ statt des Zuordnen-Bleistifts, den behalten nur Namen, die niemand kennt |
| GET | `/api/league/club/games/{id}` | view | Eine Partie wie in der Liste, mit frischem `analysis` (0.593.0) |
| GET | `/api/league/club/games/{id}/evals` | view | Bewertungen aus der Hintergrund-Analyse (`GameEvalsDto`, ohne Buchzüge; Status `none` ohne Analyse, 404 unbekannt) |
| GET | `/api/league/club/games/pgn?fide=&q=` | view | Alle (gefilterten) als PGN |
| POST | `/api/league/club/games/preview` | contribute | `{ pgn }` → `{ games[{ index, year, result, event, plies, opening, error, duplicate, white/black{ raw, elo, match{ league, ambiguous, name, fide, club, candidates }, owner, replace } }], truncated }`; 400 `empty`/`tooLarge` (5 Mio. Zeichen), höchstens 500 Partien |
| POST | `/api/league/club/games/import` | contribute | `{ pgn, games?[{ index, white{ name, fide, replace }, black{…} }] }` (fehlt `games` = alle mit Vorgaben) → `{ added, duplicates, anonymized, truncated, ids, failed[{ index, white, black, reason }] }` |
| POST | `/api/league/club/games` | contribute | EINE Partie `{ moves[] (SAN), white, black, whiteFide, blackFide, whiteElo, blackElo, whiteReplace, blackReplace, result, event, year, scanId }` → `{ id, anonymized }`; 400 `reason` wie oben + `duplicate`, `illegal` (mit Meldung); schließt die Einlesung |
| DELETE | `/api/league/club/games/{id}` | contribute | 204 / 403 / 404 |
| POST | `/api/league/club/pairings` | contribute | Ligapaarungen für eine noch nicht gespeicherte Partie `{ white, whiteFide, black, blackFide, date, year }` → `[{ id, label, white, whiteFide, black, blackFide, result, whiteOwnClub, blackOwnClub, exact }]` (0.678.0; auch `POST /api/league/s/{token}/club/pairings`) |
| GET | `/api/league/club/games/{id}/pairings` | view | Ligapaarungen einer gespeicherten Partie (die aktuelle immer dabei); 404 ohne Bearbeiten-Recht (0.678.0) |
| PUT | `/api/league/club/games/{id}` | contribute | Namen/Ergebnis korrigieren `{ white?{ name, fide }, black?, result? }` (0.582.0; wer löschen darf; fehlende Seite = unverändert) → Partie; 400 `anonymous` („Schwaz" bleibt), `noLeaguePlayer`, `onlyOwnClub`, `invalidResult`; ein vorher unzugeordneter Name (ohne FIDE-ID) wird als Zuordnung gemerkt; ein Spieler von Schwaz (oder `replace`) wird zu „Schwaz" samt Wegfall von Veranstaltung und Hochladendem (0.583.0) — eine Korrektur anonymisiert nur, nie zurück |
| GET | `/api/league/club/players?q=&all=` | contribute | Ligaspieler-Vorschläge (jedes Wort irgendwo im Namen, Wortanfänge zuerst) samt `club`; `all=true` dazu das Megabase-Verzeichnis |
| POST | `/api/league/club/games/lichess` | contribute | `{ url }` einer öffentlichen Lichess-Studie → `{ pgn }` |
| POST | `/api/league/club/games/chessbase` | contribute | Multipart `files` (Dateien einer ChessBase-Datenbank, einzeln `.gz` oder ZIP) → `{ format, name, pgn, games, converted, deleted, truncated, skippedCount, skipped[{ id, white, black, reason }] }`; 400 `reason` ∈ `noFile`/`noDatabase`/`multipleDatabases`/`missingFile`/`tooLarge`/`invalidZip`/`invalidFile`/`unreadable`, 429 `busy` (0.598.0) |
| POST | `/api/league/club/match` | contribute | `{ white, black }` → je Seite `{ league, ambiguous, name, fide, club, candidates, lastNameOnly, mega }` |
| GET | `/api/league/club/scoresheet/status` | contribute | Tageszahl dieses Wegs (10) |
| GET | `/api/league/club/admin/scans` | manage | Alle offenen Liga-Einlesungen `[{ scan, viaShareLink, mine }]` (jüngste 50) |
| POST/GET | `/api/league/club/drafts` | contribute | Entwurf ablegen `{ pgn, source, label }` (400 `tooManyDrafts`) / die eigenen offenen (0.595.0) |
| GET/PUT/DELETE | `/api/league/club/drafts/{id}` | contribute | Entwurf öffnen (Rohtext, Stand, importierte) / Stand speichern `{ state?, imported? }` / löschen — Einreicher oder Verwalter |
| GET | `/api/league/club/admin/drafts` | manage | Alle offenen Entwürfe (jüngste 100) mit `owner`, `viaShareLink`, `mine` |
| DELETE | `/api/league/club/admin/shares/{token}/games?dryRun=` | manage | Alle Partien entfernen, die über diesen Teilen-Link kamen (auch nach seinem Ablauf, auch „Schwaz"-Partien) → `{ count, dryRun }`; `dryRun=true` zählt nur; 400 `invalidToken` (über 64 Zeichen) |
| GET/POST | `/api/league/club/scans` | contribute | offene Liga-Einlesungen / Foto hochladen (multipart wie `POST /api/scoresheets`) |
| GET | `/api/league/club/scans/{id}` (+`/photo`, `POST /resolve`, `DELETE`) | contribute | Stand / Foto / Rest neu aufbereiten / verwerfen |
| POST | `/api/league/s/{token}/club/games/preview`, `/games/import` | Teilen-Link | wie oben, ohne Konto; der Import speichert höchstens 50 je Aufruf und 200 je Link und Tag (Rest: `failed` mit `shareLimit`), jede Partie mit `UploadShareHash` |
| POST | `/api/league/s/{token}/club/games?scanKey=` | Teilen-Link | eine Partie, schließt die Einlesung mit diesem Schlüssel; zählt gegen denselben Deckel (400 `shareLimit`) |
| POST | `/api/league/s/{token}/club/games/lichess` | Teilen-Link | Lichess-Studie laden, wie oben |
| POST | `/api/league/s/{token}/club/games/chessbase` | Teilen-Link | ChessBase-Datenbank → PGN, wie oben |
| GET | `/api/league/s/{token}/club/players`, `POST …/match`, `GET …/scoresheet/status` | Teilen-Link | wie oben (Status: je IP) |
| POST | `/api/league/s/{token}/club/scans` | Teilen-Link | Foto hochladen → `{ key, scan }` |
| POST | `/api/league/s/{token}/club/scans/lookup` | Teilen-Link | `{ keys[] }` → die offenen Einlesungen dazu |
| POST | `/api/league/s/{token}/club/drafts` (+`/lookup`), GET/PUT/DELETE `…/drafts/{key}` | Teilen-Link | Entwürfe ohne Konto, über den Schlüssel (0.595.0) |
| GET | `/api/league/s/{token}/club/scans/{key}` (+`/photo`, `POST /resolve`, `DELETE`) | Teilen-Link | wie angemeldet, über den Schlüssel |

Der Teilen-Link muss gültig sein (`LeagueService.ShareValidAsync`, sonst 404); Rate-Limit `anonymous-tournament`. Lesen
der Vereinspartien gibt es über den Link NICHT.

**Megabase + andere Sammlungen** (0.574.0): `POST /api/league/admin/games?source=Mega` (`league.manage`, PGN, gern
gzip — entpackt höchstens 400 MB wie unkomprimiert, darüber 413, Codereview F7-021) spielt eine fremde Partiesammlung in die Karten ein — zugeordnet NUR über `WhiteFideId`/`BlackFideId` im Kopf,
zusammengeführt wie ein chess-results-Abruf, die Quelle steht als Kopfzeile `[LeagueSource "Mega"]` im gespeicherten PGN
(`LeagueProfileBuilder.StoredSource` liest sie VOR der FIDE-Regel, sonst sähe die Megabase wie Lumbra aus). Den Filter
auf die TMM-Spieler macht `mega_decide.py` im league-analyzer (Stand 28.09.: 11,5 Mio. Partien, 46 642 übernommen für
328 Spieler; ausgelassen 235 nur über den Namen, 262 mit Namen, der nicht zur Meldeliste passt — ChessBase hängt
modernen FIDE-IDs gelegentlich Partien von Namensvettern an —, 293 ohne Züge, 46 über 30 Jahre vor dem Median-Jahr des
Spielers). Die ganze Megabase liegt NICHT in RookHub.

**Spieler korrigieren über die Megabase** (0.575.0): beim Korrigieren eines Namens (Übersicht und Formular-Korrektur,
`features/club/player-search.component.ts`) sucht LeagueHub unter den Personen der Liga und — Häkchen, seit 0.576.0
standardmäßig an, auf der Seite des Partieformulars seit 0.672.4 aus (`searchMega`, Wunsch 2026-10-05 „nimm
standardmäßig nur Namen aus der Liga") — im Spielerverzeichnis der GANZEN Megabase (`LeagueMegaPlayers`, Tabelle `LeagueMegaPlayers`: Name, `NameKey` klein ohne
Akzente mit Index, FIDE-ID, Partien, jüngstes Jahr, höchste Elo). `GET …/club/players?q=&all=true` hängt die Treffer
(`source: "mega"`) an die Ligaspieler an; trägt ein Treffer die FIDE-ID eines Ligaspielers, gilt er als dieser. Gesucht
wird seit 0.576.0 wie mit `LIKE` („bert rud" findet „Bertl, Rudolf"): jedes getippte Wort (ab zwei Zeichen, höchstens
vier) muss IRGENDWO im Namen stehen — in der Megabase `NameKey LIKE '%wort%'` je Wort (bei 432 000 Zeilen Zehntelsekunden,
höchstens 300 nach Partienzahl), Wortanfänge zuerst, dann meistgespielte; die Liga ebenso. Die Suche startet gleich beim
Öffnen bzw. Hineinklicken ins Namensfeld (`autoSearch`, `onFocus`). Seit 0.577.1: jedes Wort in BEIDEN Umlaut-Schreibweisen
(`LeagueRosterIndex.Spellings`, „Höcher" → „hocher"/„hoecher" — das Megabase-Verzeichnis enthält kein einziges ä/ö/ü,
ChessBase schreibt „Hoecher, Michael"; der Rückweg „oe" → „o" ist bewusst weg, er machte „Michael" zu „Michal"), eine
reine Zahl (4–12 Ziffern) sucht die FIDE-ID, und Titel vor/hinter dem Namen fallen beim Abgleich und in der Suche weg
(`LeagueNames.StripTitles`: Schachtitel nur in Großschreibung — „Im, Seong" bleibt ein Name —, akademische vorn; NICHT in
`NameKey`, der Brettpaarung und Meldeliste beim Aktualisieren verknüpft). Eine
gewählte FIDE-ID ohne Ligaspieler bleibt an der Partie stehen (`Side.Fide`). Eingespielt über
`POST /api/league/admin/mega-players` (TSV, gern gzip, entpackt höchstens 400 MB, sonst 413; ersetzt alles) aus `scan_mega_players.py` im league-analyzer.

**Lichess-Studien** (0.575.0): `POST …/club/games/lichess { url }` (angemeldet und über den Teilen-Link) holt das PGN
einer ÖFFENTLICHEN Studie bzw. eines Kapitels (`LichessStudySource`, nur `lichess.org/study/{8}` bzw. `…/{8}/{8}` —
der Server ruft ausschließlich `/api/study/{id}[/{kapitel}].pgn` der festen Lichess-Basis `Lichess:SiteUrl` auf, kein
freier Abruf); danach wie ein Upload (Übersicht, Import). Absagen `invalidUrl`, `lichessNotFound` (privat/fehlt),
`lichessFailed`, `tooLarge`.

**Eröffnungsbaum** (0.574.0): `GET /api/league/player/{fide}/tree?color=w|s&line=e4 e5` (und `/api/league/s/{token}/player/{fide}/tree`
für Spieler der geteilten Meldeliste) → `{ total, ended, moves[{ san, n, score (Punkte aus SEINER Sicht, %), last (Jahr) }] }`
über alle seine Partien (fremde + Verein), höchstens 30 Halbzüge, Züge aus dem Partietext (ohne Brett, schnell genug für
2000 Partien je Klick). Oberfläche: Knopf „Eröffnungsbaum anzeigen" auf der Spielerkarte (`shared/opening-tree.component.ts`).
**Filter** (0.605.0, Wunsch 2026-09-30: „online ja/nein, wenn online: Zeitformat; nur Partien der letzten x Jahre"):
`source` = `board` (Vorgabe, wie vorher) / `both` / `online`, `speeds` = Komma-Liste aus `bullet,blitz,rapid,classical,correspondence`
(leer = alle, gilt nur für Online-Partien), `years` = 1–50 (Online-Partien ab heute − x Jahre; Brettpartien tragen oft nur das Jahr,
dort zählt jedes Jahr ab dem Jahr von heute − x). Online-Partien zählen nur von GESICHERTEN Konten — angemeldet nimmt `unsure=true`
die unsicheren dazu (0.612.0, Wunsch „unsichere standardmäßig nicht in den Entwicklungsbaum, über einen Schalter dazu"; bis 0.611.0
war es umgekehrt, `sure=true` schränkte ein), über den Teilen-Link nie. Die Karte trägt dafür `onlineUnsure` (Partien der unsicheren
Konten), der Baum zeigt den Schalter „auch unsichere Konten (n Partien)" nur, wenn es welche gibt (`TreeFilter.withUnsure`, gemerkt). Unbekannte Werte fallen still auf die Vorgabe (`LeagueProfileStore.TreeFilter.Parse`). Antwort zusätzlich
`board`/`online` = wie viele der Partien in der Stellung von wo kommen. Online-Partien fragt der Baum in SQL ab (`Line` =
Präfix-Treffer `Line == pre || Line.StartsWith(pre + " ")`, Tempo, `PlayedAt`, Konto-Zuordnung) — `QueryTranslationTests`
prüft die Übersetzung gegen MariaDB. Oberfläche: Filterleiste über dem Baum (`core/tree-filter.ts` = die reinen Regeln, gemerkt
im localStorage `lh-tree-filter`); Quellen-Wahl erst, wenn der Spieler Online-Partien hat, ohne Brettpartien gleich „online".
**Filter auch fürs Eröffnungsprofil der Karte** (0.617.0, Wunsch 2026-09-30 mit Screenshot: „auch an der Stelle will ich die vollen
Filtermöglichkeiten"): `GET /api/league/player/{fide}/profile?source=&speeds=&years=&unsure=` (und `/api/league/s/{token}/player/{fide}/profile`,
dort nur gesicherte Konten) → dieselben Abschnitte wie die Karte (`white`, `black_e4`, `black_d4`, `black_other` samt `first`/`lines`) plus
`n`/`board`/`online`/`years` über die GEFILTERTEN Partien (`LeagueProfileStore.ProfileAsync`, gleiche Regeln wie der Baum; die Abschnitte
rechnet `LeagueProfileBuilder.AddSections` — dieselbe Funktion wie für die gespeicherte Karte). Züge wie im Baum aus dem Partietext bzw.
der gespeicherten Online-Zeile (ohne Brett), Partien ab eigener Stellung zählen nicht. Die Filterleiste sitzt seither auf der KARTE
(`shared/tree-filter-bar.component.ts`), die Karte hält den Stand (`lh-tree-filter`) und gibt dem Baum den wirksamen Filter als Eingabe;
ohne Filter (nur Brett, alle Jahre) zeigt sie die gespeicherte Karte ohne weiteren Abruf.

**Online-Konten + Online-Partien** (0.605.0, Wunsch 2026-09-30: „für einen User kann es eine Liste von Onlinekonten geben — Name +
Seite, gesichert oder unsicher + Kommentare; im Hintergrund holst du die Spiele dieser User und legst sie in der DB ab"):
* **Konten** (`Services/League/LeagueOnlineAccounts.cs`): je FIDE-ID wie die Karte. `LeagueOnlineSites` kennt die Seiten (heute
  `lichess`, `chess.com`; eine weitere braucht dort Kürzel/Namensregel/Profiladresse und in `LeagueOnlineSync` einen Abruf) und liest
  Name ODER kopierte Profiladresse (die Adresse schlägt die gewählte Seite). `Confidence` bleibt wie im Import `sicher` /
  `wahrscheinlich` (Oberfläche: gesichert / unsicher), `Evidence` ist der Kommentar (≤ 1000). Von Hand gepflegt = `Manual`: solche
  Zeilen lässt der Bundle-Import stehen; ein Import-Konto, das es schon gibt, wird nur aktualisiert (sonst gingen seine Partien),
  nur verschwundene Import-Konten fallen samt Partien weg. Höchstens 20 je Spieler. Andere Seite oder anderer Name = anderes Konto:
  Partien weg, Abruf von vorn; nur andere Groß/Kleinschreibung behält sie. Nach jeder Änderung werden die Konten in den fertigen
  Ansichten nachgezogen (`PatchViewsAsync`, `roster[].acc`) und der Abruf geweckt.
* **Spieler ohne FIDE-ID** (0.730.0, Wunsch 2026-10-10: „die Verbindung von Onlinekonto zu Ligakonto soll nicht nur über FIDE
  gehen, um auch die ohne FIDE zu fangen"): sie bekommen den Schlüssel `LeagueNames.AccountKey(null, NameKey)` = „n-" + 14
  Hex-Zeichen SHA-256 des Namensschlüssels — passt in die 16 Zeichen aller `FideId`-Spalten (Konten, Online-Partien, Vorschläge,
  Scans), also KEINE Migration; Konten, Abruf, Baum und Profil laufen über denselben Weg wie mit FIDE-ID. Zurückgerechnet wird
  über `LeagueNoFidePlayers` (Namensschlüssel der Meldelisten ohne FIDE-ID durchsuchen): `LeagueKnowsAsync`,
  `LeagueAccountFinder.PlayerAsync`, `LeagueProfileStore.NameAsync`. Die Ansicht trägt `roster[].key` und `cand[].key`
  (nur ohne FIDE-ID), `acc` hängt am Schlüssel; `PatchViewsAsync` und die Meldeliste des Teilen-Links nehmen `key`, wo `fide`
  fehlt. `CardAsync` liefert für einen bekannten n-Schlüssel immer eine Karte (`fide: ""`, `key`, `name`), auch ohne Konto —
  sonst ließe sich nie eins eintragen. Frontend: `fixture-view` öffnet die Karte mit `fide || key`, `player-card.accountsKey`
  bindet die Konten an den n-Schlüssel (nie an die Id der Spielervorbereitung). Grenzen: gleicher Name = derselbe Schlüssel
  (wie `LeagueNames.Pid`); ein späterer Wechsel auf eine FIDE-ID lässt die Konten am alten Schlüssel liegen; Minderjährige sind
  ohne Scan-Zeile nicht verborgen (`LeagueHiddenAccounts`); die Aufstellungen je Runde (`round-lineups`) verlinken nur FIDE-Spieler.
* **Sichtbarkeit**: angemeldet trägt die Karte je Konto `id`, `comment`, `games`, `syncedAt`, `error` (`ToJson(full: true)`), dazu
  `online` = Summe der Partien; über einen Teilen-Link nur gesicherte Konten und nur `site/user/url/conf`. Pflegen nur
  `league.manage`, nie über einen Teilen-Link (`shared/online-accounts.component.ts`, in der Spielerkarte).
* **Abruf** (`Services/League/LeagueOnlineSync.cs`, HttpClient `LeagueOnline`, eigener User-Agent): Lichess über
  `/api/games/user/{name}?since=…&sort=dateAsc&max=500` (ndjson, höchstens 4 Seiten je Lauf), chess.com über die
  Monatsarchive (höchstens 12 je Lauf). Nur Standardschach ab der Grundstellung, höchstens `LeagueOnline:MaxYears` (5) zurück;
  gespeichert je Partie Tempo (ultraBullet → bullet, daily → correspondence), Farbe, Ergebnis aus SEINER Sicht, Gegner + Wertungen,
  alle Züge und die ersten 30 Halbzüge als `Line` (für den Baum) — beide in derselben SAN wie die Brettpartien, OHNE
  Schach-/Mattzeichen (`PgnParser.ExtractMainlineSans`; Lichess liefert „Bb4+“, sonst stand derselbe Zug zweimal im Baum,
  Codereview N4-001 — den Altbestand bereinigt die Migration `LeagueOnlineLinesWithoutCheckSigns`). Der Stand steht am Konto
  (`SyncCursor` in ms, `SyncMore` = es gibt noch Rückstand, `SyncedAt`, `GameCount`, `SyncError`); 404 = „Konto nicht gefunden", ein 429 beendet den ganzen Lauf.
* **Takt** (`LeagueOnlineSyncScheduler`): zwei Minuten nach dem Start, dann je Konto alle `LeagueOnline:IntervalHours` (12), sofort
  nach einem Weckruf; solange ein Konto Rückstand hat, eine Minute Pause zwischen den Läufen (je Lauf höchstens 10 min), sonst
  schaut er alle 30 min. `LeagueOnline:Enabled=false` schaltet ihn ab. Die Integrationstests nehmen alle Hosted Services heraus.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| POST | `/api/league/player/{fide}/accounts` | manage | Konto anlegen `{ site, user (Name oder Profiladresse), sure, comment }` → das Konto (volle Form); 400 `reason` ∈ `invalidSite`/`invalidUser`/`duplicate`/`tooMany`, 404 `unknownPlayer` |
| PUT | `/api/league/accounts/{id}` | manage | Ändern (fehlende Felder bleiben); 404 `notFound` |
| DELETE | `/api/league/accounts/{id}` | manage | Entfernen samt Partien → 204 |
| POST | `/api/league/accounts/{id}/sync` | manage | Nochmal holen (Fehler weg, Abruf geweckt) |

**Konto-Vorschläge** (0.607.0, Wunsch 2026-09-30 „zuerst die Konto-Vorschläge"; `Services/League/LeagueAccountFinder.cs`):
LeagueHub sucht selbst nach Konten und legt sie als VORSCHLAG ab (`LeagueAccountSuggestions`); ein Verwalter übernimmt
(unsicher/gesichert → normales Konto, Kommentar = die Hinweise) oder verwirft. Portiert aus `online_accounts.py`, aber lockerer
— dort zählten nur Konten mit Klarnamen (29 Konten bei 27 Spielern), hier entscheidet ein Mensch. Regeln:
* **Kandidaten**: Nutzernamen aus dem Namen (`Variants`: MaxMuster, Max_Muster, Max-Muster, MusterMax, Muster_Max, MMuster,
  MusterM; Umlaute als ae/oe/ue, Titel weg, ohne Komma „Nachname Vorname") auf BEIDEN Seiten; auf Lichess dazu die Suchvorschläge
  zum Nachnamen (`/api/player/autocomplete`, erst ab 5 Buchstaben, höchstens 12 Treffer). Lichess-Profile gesammelt über
  `POST /api/users`, chess.com einzeln `/pub/player/{name}` (300 ms Pause).
* **Urteil** (`Judge`, rein): raus bei gesperrtem/geschlossenem Konto, einem Profilnamen OHNE den Nachnamen (jemand anderes) und
  einem Land, das weder das Land seiner REGION (Tirol AT, Bayern DE — seit 0.712.0, siehe „Je Region" unter Team-Suche) noch die
  Föderation des Spielers (Meldeliste oder FIDE) ist. Hinweise: Klarname 3, nur Nachname 1,
  FIDE-Wertung im Profil ±250 zur Liste 2, Ort der Region in Ort/Bio 2 (Wortgrenzen — „Hallo" ist nicht Hall; Bayern auch „Gautinger"), Land 1. Ein Name
  aus dem Namen braucht ≥ 1 (plus 1 Punkt), einer aus der Suche ≥ 3.
* **Anderer Vorname = anderer Mensch** (0.611.0, `FirstNameMatch`, gesehen in der ersten vollen Suche: „Andreas Berchtold" für Axel,
  „Galin Georgiev" für Georgi): steht im Profil neben dem Nachnamen ein Vorname, muss es einer des Spielers sein (irgendeiner, auch
  der zweite); nur Initialen → eine muss passen; nur Nachname oder Titel („IM Muster") → schwacher Hinweis. Sonst fällt das Konto weg.
* **Online-Wertung gegen Elo** (0.609.0, Wunsch „ein Konto mit 500 auf einem 2000er ergibt keinen Sinn — nur niedriger ist ein
  Problem, alles droppen, was 400 niedriger ist"): die BESTE belastbare Wertung des Kontos (ab 10 Partien; seit 0.622.0 zählt auch „vorläufig", das Lichess nach langer Pause setzt —
  unbespielte Lichess-Kategorien stehen auf 1500 „prov"; Bullet zählt mit) darf höchstens `RatingBelow` 400 unter der Elo der
  Meldeliste liegen, sonst fällt das Konto weg; nach oben keine Grenze. Liegt sie 100 bis 300 DARÜBER (`FitMin`/`FitMax`), ist das
  der OPTIMALE Treffer (+2, `ScoreRatingFit`), jede andere Wertung darüber oder bis 400 darunter ein schwächerer (+1,
  `ScoreRatingWeak`) — `RatingEvidence`, Wunsch „normal ist Elo online ca. 200 höher, 100–300 wäre passend" und „das Band zeigt die
  optimalen Treffer, 400 unter FIDE schließt aus, alles andere ist halt Treffer, aber schwächer" (0.621.0; 0.619.0 gab außerhalb des
  Bands nichts, davor −250 bis +450 einen Punkt).
* **Gleicher Nutzername auf der anderen Seite** (0.621.0, Wunsch „wenn du einen Treffer hast, prüfe, ob der gleiche Username auf
  chess.com bzw. Lichess existiert und eventuell auch passt"; `LeagueAccountFinder.TwinAsync`): zu jedem Treffer holt die Suche das
  Profil desselben Namens auf der anderen Seite (`FetchProfileAsync`, nur wo dieser Name dort nicht ohnehin schon gefragt wurde) und
  urteilt nach denselben Regeln (`Judge` mit der Schwelle des abgeleiteten Namens, erster Hinweis „gleicher Nutzername wie das
  Lichess-Konto …", +`ScoreTwin` 1). Die Team-Suche tut dasselbe für jeden ihrer Vorschläge (Source `team`). Hakt die andere Seite,
  bleibt der Treffer selbst stehen; ein 429 beendet den Durchgang wie sonst. Lichess liefert die Wertungen in `POST /api/users` mit (`perfs`), chess.com nur über `/pub/player/{name}/stats` — ein Abruf
  mehr je gefundenem Konto (dort steht auch die selbst angegebene FIDE-Wertung).
* **Fassung** `LeagueAccountFinder.CurrentVersion` (8 seit 0.712.0 — Land/Orte nach der Region; 7 seit 0.622.0 — Lichess-„vorläufig" mit genug Partien zählt; 6 = Wertungsband
  optimal/schwächer, gleicher Name auf der anderen Seite) in `LeagueAccountScans.Version`: ältere Suchen sind sofort wieder
  fällig, und eine neue Suche entfernt OFFENE Vorschläge, die sie nicht mehr bestätigt (verworfene bleiben). Wer Kandidaten oder
  Urteil ändert, erhöht die Zahl.
* **Minderjährige: gesucht, aber VERBORGEN** (0.610.0, Wunsch „du linkst sie, aber zeigst niemandem den Namen/Account"; bis
  0.609.0 gar nicht gesucht): Jahrgang und Föderation über Lichess `/api/fide/player/{id}`, gemerkt in `LeagueAccountScans`. Unter
  18 (seit 0.616.0 NUR bei bekanntem Jahrgang — Wunsch „alle mit gesichertem Geburtsdatum unter 18 ausblenden, alle anderen
  anzeigen"; bis dahin galt auch „Jahrgang unbekannt" als verborgen) gilt `LeagueHiddenAccounts`: Seite, Nutzername, Adresse, Profilangaben und
  Kommentar verlassen den Server nie — Vorschläge (`hidden: true`, nur Hinweise + Spieler), Konto-JSON (`JsonAsync`/`ToJson(…,
  hidden)`), Karte (angemeldet nur DASS es ein Konto gibt, über einen Teilen-Link gar nichts), Meldeliste der Ansichten
  (`RebuildViewsAsync`, `PatchViewsAsync`: leer). Die Partien zählen nur im Eröffnungsbaum (Züge, keine Gegner/Links). Mit 18 wird
  das Konto von selbst sichtbar. Ohne Such-Eintrag (nie abgesucht) gilt ein Konto als sichtbar — die Suche erfasst jeden Spieler
  der laufenden Saison. In DB und Server-Log steht die Verknüpfung weiter (nur Betreiber). Testfall
  `Minors_AreSearched_ButNothingIdentifyingLeavesTheServer` prüft ALLE Ausgaben auf den Nutzernamen.
  **Ausnahme ADMINS** (0.625.0, Wunsch „Admins sollen auch bei Minderjährigen die Onlinekonten für die Prüfung auf
  sicher/unsicher/verwerfen sehen, sonst kann ich das nicht entscheiden"): Rolle `Admin` (`IsAdmin` im Controller → `reveal`)
  bekommt Karte, Vorschläge, Konto-Antworten und die Prüfung (i) VOLLSTÄNDIG, mit `minor: true` (Oberfläche: Etikett „minderjährig –
  nur für Admins sichtbar"). Wer nur `league.view`/`league.manage` hat, sieht weiter nichts; Teilen-Links (`onlySure`) und die
  Meldeliste der Ansichten zeigen es nie, auch nicht einem Admin.
* **Nicht wieder vorschlagen**: verworfene Vorschläge bleiben als `Rejected` stehen; ein ENTFERNTES Konto wird als verworfener
  Vorschlag gemerkt; ein angelegtes Konto erledigt den passenden Vorschlag (Vergleich ohne Groß/klein).
* **Takt**: im `LeagueOnlineSyncScheduler` nach jedem Abruf-Durchgang, je Runde höchstens 5 min (`SearchBudget`), 1 s Pause je
  Spieler; Spieler der laufenden Saison JE REGION (`LeagueOnlineRegions.CurrentSeasonTnrsAsync`, seit 0.712.0 — vorher die jüngste
  Saison überhaupt, mit Zugspitze 2026/27 vor Tirol 2025/26 fielen die Tiroler heraus) mit FIDE-ID, nie gesuchte zuerst, dann alle 90 Tage (`RescanDays`); ein Fehler versucht es
  am nächsten Tag. `LeagueOnline:Suggestions=false` schaltet die Suche ab. Ein 429 beendet den Durchgang.
* Oberfläche: auf der Spielerkarte unter den Konten „Vorschläge der Konto-Suche" mit „Jetzt suchen" (nur Verwalter), und der Reiter
  „Konto-Vorschläge" (`/konten`, `features/accounts/`) mit allen offenen Vorschlägen je Spieler und dem Stand der Suche.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/suggestions` | manage | Offene Vorschläge (stärkste zuerst, mit Name/Mannschaft) `{ items, scanned, total }` |
| GET | `/api/league/player/{fide}/suggestions` | manage | Offene Vorschläge eines Spielers `{ items }` |
| POST | `/api/league/player/{fide}/suggestions/scan` | manage | Jetzt suchen → `{ items, found, skipped }` (`skipped` seit 0.610.0 immer leer); 503 `rateLimited`/`unreachable` |
| POST | `/api/league/suggestions/{id}/accept` | manage | `{ sure }` → das Konto (wie Anlegen); 404 erledigt/unbekannt |
| POST | `/api/league/suggestions/{id}/reject` | manage | Verwerfen → 204 |

**Team-Suche** (0.612.0, Wunsch 2026-09-30 „auf Lichess gibt es Teams", „vor 5 Jahren gab es eine Online-TMM 2021", „schau, was
Schach Tirol sonst noch organisiert hat"; `Services/League/LeagueTeamScout.cs`, Tabelle `LeagueScoutAccounts`): die Namenssuche
findet nur Konten, deren Name aus dem Spielernamen kommt. Die Team-Suche nimmt die Konten aus dem Umfeld der Tiroler Vereine:
* **Bestand** (`RefreshPoolAsync`, höchstens alle `PoolEvery` 30 Tage — der Takt merkt es sich im Arbeitsspeicher): Lichess-Team-Suche
  nach Tiroler Orten (`DefaultPlaces`, abweichend `LeagueOnline:TeamPlaces`), nur Teams mit dem Ort als ganzem Wort im Namen
  (`ClubKeys`, beide Umlaut-Schreibweisen); deren Mitglieder (`Teams`) und alle, die in einem Team-Battle dieser Teams FÜR sie
  gespielt haben (`/api/team/{id}/arena` → Battles → `/api/tournament/{id}/results`, `PlayedFor`). Gemessen am 2026-09-30: die
  Online-TMM 2021 und die Quarantäne-Liga sind solche Battles.
* **Ein Team, das nichts hergibt, kostet nur sich selbst** (0.623.1, gemeldet 2026-09-30 als wiederholte
  „Team-Suche gescheitert"-Ausfälle im Log): Lichess antwortet **401**, wenn ein Team seine Mitgliederliste verborgen hat
  (`schachsport-union-innsbruck-team-2-mm-2021-osb-lv-tirol`). `SaveChangesAsync` steht erst am ENDE von
  `RefreshPoolAsync` — die Ausnahme flog also durch, kein Team NACH diesem einen wurde je gelesen, und
  `LeagueScoutAccounts` blieb seit der Einführung LEER (0 Zeilen auf Prod). Jetzt gehen die Abrufe je Team
  (`/users`, `/arena`) und je Battle (`/api/tournament/{id}`, `/results`) über `GetOpenAsync` (401/403 → `null` wie ein
  404) und stehen zusätzlich in einem `try/catch (HttpRequestException)` je Schleifendurchlauf, der nur diesen einen
  Eintrag verwirft und ihn als Warnung loggt; die Zahl steht als `{Skipped}` in der Abschlusszeile. **Ein 429 fliegt
  weiter** und beendet den Durchgang wie bisher — `LeagueOnlineSync.RateLimitedException` erbt von `Exception`, nicht
  von `HttpRequestException`, und genau das nagelt `Pool_ATooManyRequests_StillEndsTheRun` fest. Die Team-SUCHE selbst
  (`/api/team/search`) bleibt ungeduldet: antwortet die nicht, ist nichts zu holen, und ein Abbruch ist die richtige
  Meldung.
* **Ein 502 ist die Last, keine Auskunft — deshalb EIN Wiederholversuch** (0.624.2, am 01.10.2026 auf Prod gemessen):
  der erste Pool-Lauf mit 0.623.1 brachte 413 Konten (vorher 0) und meldete „30 Tiroler Teams, 155 Team-Battles,
  413 neue Konten, 37 uebersprungen" — die 37 waren KEIN 401/403, sondern **502 auf Team-Battles, alle binnen drei
  Sekunden**, und dieselben Turnier-Kennungen antworten einzeln abgefragt mit 200. Der Duldungs-`catch` schluckte sie
  endgültig, ihr `PlayedFor`/`Events` wäre erst beim nächsten Pool-Lauf (30 Tage) gekommen. `GetAsync` wiederholt
  deshalb EINMAL (`MaxAttempts` 2) nach `RetryPause` (2 s, im Test 0), aber NUR bei einem vorübergehenden Fehler —
  `IsTransient`: 5xx oder gar keine Verbindung (`HttpRequestException.StatusCode` ist dann `null`). Was eine ANTWORT
  ist, wird nie wiederholt: 404 (`null`), 401/403 (die Duldung oben) und 429 (`RateLimitedException`, beendet den
  Durchgang). Die Wiederholung sitzt in `GetAsync`, gilt also auch für die Team-Suche und den Partien-Abruf der
  Konto-Prüfung — dort kostete ein 502 bisher dem Konto einen Tag.
* **Der Bestands-Aufbau speichert ZWISCHEN und sitzt eine Drossel aus** (0.625.1, am 01.10.2026 auf Prod gemessen):
  mit 0.624.2 kam der Lauf zweimal (11:12, 11:43) überhaupt nicht mehr durch — `Lichess: zu viele Anfragen`, gemessen
  **54 Abrufe in 61 s** auf `/api/team/…`, beim zweiten Mal ohne jede Fremdlast (die (i)-Prüfungen des Nutzers endeten
  eine halbe Stunde davor). Weil `SaveChangesAsync` am ENDE stand, schrieb jeder abgebrochene Anlauf NICHTS und begann
  von vorn: der Takt klopfte alle 30 min vergeblich an, der Bestand blieb auf dem Stand des einen geglückten Laufs.
  Drei Änderungen, alle nur für den AUFBAU (`bulk: true` an seinen vier Abruf-Stellen; die Konto-Prüfung bleibt, wie sie
  war — dort beendet ein 429 den Durchgang weiter):
  - **`PoolPause`** (3 s statt `Pause` 1 s) — bei einer Sekunde Abstand trat die Drossel zu.
  - **Eine Drossel wird AUSGESESSEN**, nicht als Ende gelesen: `RateLimitWaits` (3) mal `RateLimitCooldown` (60 s,
    Lichess' Empfehlung), dann endet der Durchgang doch. Der Aufbau läuft nur alle `PoolEvery` (30 Tage) — die Minute
    Warten ist billig gegen einen verlorenen Lauf.
  - **Zwischengespeichert alle `SaveEvery` (25)** Teams bzw. Battles, UND im `finally` mit `CancellationToken.None` —
    dieselbe Lehre wie beim Rundenplan-Lauf des Turnierverzeichnisses: ein abgebrochener Token verhinderte genau das
    Speichern, das die Arbeit retten soll. Ein Fehler beim Speichern wird geloggt und verdeckt die ursprüngliche
    Ausnahme NICHT (der Takt muss eine Drossel als Drossel sehen). Die Abschlusszeile steht ebenfalls im `finally`,
    sagt also auch bei einem Abbruch, was erreicht wurde.
  **Was damit noch NICHT gelöst ist:** ein Anlauf beginnt immer bei Team 1 — die Abrufe der schon abgearbeiteten Teams
  laufen erneut (die DATEN bleiben, `Upsert` findet sie im Bestand). Erst wenn ein Lauf durchkommt, ist der Bestand
  vollständig; eine Fortschrittsmarke je Team gibt es nicht.
* **Prüfen** (`RunOnceAsync`, je Konto einmal, dann alle `RecheckDays` 90): Profile gesammelt über `POST /api/users`. (1) Steht ein
  Klarname im Profil, der zu einem Spieler der laufenden Saison passt (alle Nachnamen-Teile + Vorname voll, `FirstNameMatch`), gilt das
  Urteil der Namenssuche (`Judge` mit `lead` = die Team-Herkunft als erster Hinweis statt „Nutzername aus dem Namen"), +1 Punkt.
  (2) Sonst, wenn das Konto für einen Verein gespielt hat: seine letzten `MaxGames` 100 Lichess-Partien (mindestens `MinGames` 20
  ohne Bullet, `LeagueFingerprint.Usable`) gegen die Brettpartien JEDES Spielers dieses Vereins (alle Saisonen, Mannschaftsname enthält
  den Ort) — `LeagueFingerprint`: Stellungen nach den eigenen Zügen bis Halbzug 20, je Online-Partie die tiefste gemeinsame, gemittelt.
  Vorgeschlagen nur mit mindestens `ClubMargin` 1,3-fachem Abstand zum Zweiten (an der Meldeliste der Online-TMM 2021 geprüft: 11 von
  13 richtig, ab 2 alle 4) und plausibler Wertung (`RatingPlausible`); Punkte 4 ab Abstand 2, sonst 3. **Seit 0.614.0 zusätzlich**
  (die Kalibrierung stammt aus der Online-TMM 2021, wo JEDER Teilnehmer Ligaspieler war — in offenen Battles wie der Quarantäne-Liga
  sind es viele nicht, und „passt am besten unter den Vereinsspielern" traf schon mit dem ersten Zug allein): gemeinsame Stellungen im
  Schnitt mindestens bis Halbzug `MinDepth` 4 (falsche Treffer 1,6/2,4, bestätigte meist 4–11); kein Vorschlag, wenn der Nutzername
  einen anderen Vornamen aus den Meldelisten nennt (`OtherFirstName`, „Markus_Ragger" für Herbert — vermutlich der GM selbst); kein
  Vorschlag (auch über den Klarnamen) für ein Konto, das schon bei IRGENDEINEM Spieler eingetragen ist (ein selbst gemeldetes Konto
  kam sonst über die Stellungen beim Vereinskollegen noch einmal). Das Ergebnis je Konto steht in
  `LeagueScoutAccount.Result`. Ein Konto, dessen Partien nicht zu holen sind, kommt am nächsten Tag wieder; ein 429 beendet den Durchgang.
* **Eigene Vorschläge** (`LeagueAccountSuggestion.Source = "team"`; seit 0.716.0 auch `"report"` = aus einer Meldung — seit 0.726.0 nur noch, wenn das Konto bei einem anderen Spieler steht, siehe
  „Online-Schach Oberbayern"): die Namenssuche räumt beim erneuten Suchen nur Vorschläge OHNE
  Source weg — sonst verschwänden die der Team-Suche bei jedem Rescan, weil sie sie nie bestätigen kann.
* **Minderjährige**: vor jedem Vorschlag holt die Team-Suche den Such-Eintrag samt Jahrgang (`LeagueAccountFinder.ScanRowAsync`, legt
  ihn mit Fassung 0 an, die Namenssuche bleibt damit fällig) — ohne Eintrag gälte das Konto als sichtbar, und die Team-Suche erreicht
  auch Spieler, die die Namenssuche noch nicht (oder als Spieler früherer Saisonen nie) abgesucht hat.
* **Takt**: im `LeagueOnlineSyncScheduler` nach der Namenssuche, je Runde höchstens `SearchBudget`; `LeagueOnline:TeamScout=false`
  schaltet sie ab.
* **Je Region** (0.712.0, Wunsch 2026-10-07 „Online-Konten-Zuordnung für die Region Bayern (SK Weilheim, Schachkreis Zugspitze,
  Bezirk Oberbayern)"; `Services/League/LeagueOnlineRegions.cs`, `LeagueOnlineRegion`): Tirol und Bayern haben je Orte der
  Team-Suche (`Places`; Tirol wie bisher, Bayern = Weilheim, Starnberg, Gräfelfing, Germering, Gröbenzell, Gauting, Gilching, Ammersee,
  Windach, Fürstenfeldbruck, Tölz, Tegernsee, Miesbach, Penzberg, Geretsried, Wolfratshausen, Garching, Dachau, Augsburg, Haunstetten,
  Kriegshaber, Landsberg, Kaufbeuren, Rosenheim, Freising, Ingolstadt, „München|Münchner|Münchener" — „|" = weitere Schreibweisen,
  Schlüssel ist die erste —, Zugspitze, Oberbayern; „Bayern" allein NICHT, 110 Fremdtreffer), feste Lichess-Teams (`FixedTeams`,
  Bayern 39 aus der Recherche 07.10.2026: Kreis/Bezirk `schachkreis-zugspitze`, `schachbezirk-oberbayern`, `…-lounge`,
  `schachkreis-ingolstadt-freising`, `schachkreis-inn-chiemgau` und die Vereins-Teams, deren Ort nicht im Namen steht wie
  `grobes-schach` = SC Gröbenzell, `windacher-chess-academy`), Land (AT/DE), Orts-Regex fürs Profil (Tirol `TirolPlace`, Bayern aus
  den Orten, ä/ae/a), Beschriftungen („Tiroler/Bayerische Lichess-Teams", „Tiroler/Bayerischer Ort im Profil") und Online-Liga
  (Tirol „Online-TMM 2021", Bayern „Online-Liga Zugspitze/Oberbayern" = Serien `ZugLiga`/`ObbLiga`/`ZugspitzProbeLiga`).
  Konfig je Region `LeagueOnline:TeamPlaces:{region}` (Tirol weiter auch `LeagueOnline:TeamPlaces`) und `LeagueOnline:Teams:{region}`
  (Komma-Listen). **Vereins-Schlüssel** (`LeagueOnlineRegion.ClubKeys`): in Bayern zählt auch das Adjektiv („Gautinger SC" → gauting,
  „Tölzer Schachtiger" → toelz), Tirol bleibt beim Ortsnamen; **Kreis-/Bezirks-Teams** („Schachkreis …", „Schachbezirk …",
  `LeagueOnlineRegions.IsAreaTeam`) und die Such-Orte Zugspitze/Oberbayern (`AreaPlaces`) sind NIE „sein Verein" — wer für den Kreis
  spielte, steht im (i) mit „warn" bzw. „info". **Jugend**: Teams und Team-Battles mit „Jugend" im Namen/der Kennung kommen nicht in
  den Bestand (alle Regionen; Minderjährige). **Pool-Lauf** (`RefreshPoolAsync(ct, region?)`) je Region, aber nur für Regionen mit
  Ligen im Bestand (ganz ohne Ligen alle); feste Teams nur mit `GET /api/team/{id}` für den Namen, wenn die Suche sie nicht fand.
  **Prüfung** (`RunOnceAsync`): Klarnamen gegen die Spieler der laufenden Saison JEDER Region, das Urteil nach der Region des
  Spielers; die Stellungs-Prüfung vergleicht mit den Vereinsspielern derselben Region (Schlüssel einmal je Durchgang gerechnet).
  **`EventSeries`** kennt die Kreis-Schreibweise: „1-ZugLiga 1-21 7+3 Team Battle" → „ZugLiga", „KEM2022 7+3" → „KEM",
  „Kreis-Vergleichskampf-OBB 2-21" → „Kreis Vergleichskampf OBB" (Runde vorn, Bedenkzeit/Saison-Teil hinten, Bindestrich-Ketten ab
  drei Wörtern, angehängte Jahreszahl); Tests mit den Literalen in `LeagueAccountChecksTests.EventSeries_ZugspitzeAndOberbayern`.

**Konto-Prüfung (i)** (0.619.0, Wunsch 2026-09-30: „mach bei den Konten immer ein (i) und zeig an, was alles geprüft wurde:
Selbstmeldung, TMM 2021, Name, Land, % Übereinstimmung Repertoire, Elo passend"; `Services/League/LeagueAccountChecks.cs`): je
eingetragenem Konto und je Vorschlag eine Liste von Prüfungen `{ key, label, status, text }` — `ok` spricht dafür, `weak` spricht
schwächer dafür (0.621.0), `warn` macht stutzig, `fail` spricht dagegen, `none` = nichts zu prüfen, `info` = zur Kenntnis. Reihenfolge
wie im Wunsch:
* **Selbstmeldung** (`LeagueSelfReports`, eingespielt je QUELLE über `POST /api/league/admin/self-reports`, z. B. die Meldeliste der
  Online-TMM 2021): dieses Konto von ihm gemeldet → ok; von einem ANDEREN Spieler → fail (Name nur, wenn dessen Konten nicht verborgen
  sind); er hat ein anderes Konto derselben Seite gemeldet → warn.
* **Online-TMM 2021** (seit 0.712.0 die Online-Liga der REGION des Spielers: Bayern „Online-Liga Zugspitze/Oberbayern", Serien
  ZugLiga/ObbLiga; Schlüssel bleibt `tmm2021`): das Konto steht im Bestand der Team-Suche und `LeagueScoutAccount.Events` nennt die Serie (der Scout merkt sich
  seit 0.619.0 je Team-Battle die Serie ohne Runde, `EventSeries`: „Online TMM 2021 Runde 3 Team Battle" → „Online TMM 2021"); für
  seinen Verein (`ClubKeys` des Teams gegen die Orte seiner Mannschaften) → ok, sonst warn. Bestand von vor 0.619.0 hat noch keine
  Serien — bis zum nächsten Pool-Durchlauf steht dort „info".
* **Name im Profil** (`FirstNameMatch` wie die Suche; nur Initiale oder Nachname = weak), **Nutzername** (aus dem Namen gebildet /
  anderer Vorname aus den Meldelisten = fail / enthält den Nachnamen = weak), **Land** (Land der Region — Österreich bzw. seit 0.712.0
  Deutschland für Bayern — oder Föderation laut Meldeliste bzw. FIDE; ein deutsches Profil ist bei einem Weilheimer ok, bei einem Schwazer
  ohne GER-Föderation fail).
* **Übereinstimmung Repertoire** (`LeagueFingerprint.Coverage`): Anteil der letzten 100 Online-Partien (ohne Bullet, wenn genug
  andere), die mindestens `RepertoireOwnMoves` (3) EIGENE Züge weit einer Stellung aus seinen Brettpartien folgen (nach einem eigenen
  Zug ist fast jede Partie „im Repertoire"); ab 35 % ok, 10–35 % weak, unter 10 % warn — online spielt man oft anderes, deshalb nie fail. Ein
  eingetragenes Konto nimmt die gespeicherten Partien, ein Vorschlag holt sie (Lichess ein Abruf, chess.com die jüngsten drei
  Monatsarchive). Unter 5 Brettpartien bzw. 10 Online-Partien: nichts zu prüfen.
* **Online-Wertung je Kategorie** (`Profile.Ratings`, alle Kategorien mit mindestens einer Partie): nur belastbare zählen (≥ 10
  Partien, nicht vorläufig); 100–300 über der Elo ok (optimal), jede andere bis 400 darunter weak, mehr als 400 darunter fail.
* **Gleicher Name auf der anderen Seite** (0.621.0): schon als sein Konto eingetragen → ok; gibt es und passt nach den Regeln der Suche
  → ok (mit den Hinweisen); gibt es, passt aber nicht → info; gibt es nicht / gesperrt → none.
* Dazu FIDE-Wertung und Ort der Region im Profil (Schlüssel `place`, bis 0.711.0 `tirol`; „Tiroler/Bayerischer Ort im Profil"),
  Lichess-Teams der Region („Tiroler/Bayerische Lichess-Teams") und andere Team-Battles, zuletzt aktiv, gesperrt, bei einem anderen
  Spieler eingetragen, das Ergebnis der Team-Suche und (Vorschlag) ihre Hinweise.
Das Profil wird dafür FRISCH geholt (dieselben Abrufe wie die Suche); ist die Seite nicht erreichbar, stehen die Profil-Prüfungen auf
„nicht geprüft — …" und das Ergebnis wird nur eine Minute gemerkt (ebenso, wenn die Partien eines Vorschlags gerade nicht kamen),
sonst `CacheFor` 10 min (IMemoryCache). Verborgene Konten (Minderjährige) → 404, außer für Admins (0.625.0). **Partien eines
Lichess-Vorschlags NUR mit `Accept: application/x-ndjson`** — ohne den Kopf liefert Lichess PGN, das Lesen scheiterte, und bis 0.624
zeigte JEDER Lichess-Vorschlag „Partien gerade nicht abrufbar". Lichess gibt Partien nur einem Abruf je Adresse zugleich heraus;
läuft der Hintergrund-Abruf, kommt 429 → Text „Lichess bremst gerade …". Oberfläche: rundes (i) in der Konto- bzw. Vorschlags-Zeile (`shared/account-checks.component.ts`, reine Regeln
in `core/account-checks.ts`), Liste darunter mit Zeichen (✓ ! ✕ – i) + Wort für Vorleser + Satz, oben die Zusammenfassung.

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/accounts/{id}/checks` | view | Prüfung eines Kontos `{ site, user, url, player, elo, checkedAt, profileLoaded, items[] }`; 404 unbekannt/verborgen |
| POST | `/api/league/s/{token}/player/{fide}/accounts` | anonym (Teilen-Link) | **Online-Konto ohne Anmeldung eintragen** (0.630.0, Wunsch: „soll auch für nicht registrierte User möglich sein — direkt als sicher, beim Spieler vermerken, wer ihn hinzugefügt hat, in dem Fall dann anonym") `{ site, user, comment }` → sofort „gesichert", `AddedBy = "anonym"` + Hash des Links, Kommentar „Über einen Teilen-Link hinzugefügt (anonym)…"; nur Spieler der geteilten Begegnung (sonst 404), 400 wie beim Anlegen und `takenElsewhere` (Konto steht schon bei einem anderen Spieler). Ändern/Entfernen bleibt den Verwaltern. Angemeldet zeigt jedes Konto „hinzugefügt von …" (`addedBy` im Konto-JSON, nie über den Link), das (i) eine Zeile „Eingetragen"; Anlegen und Übernehmen eines Vorschlags vermerken den Nutzernamen |
| GET | `/api/league/suggestions/{id}/checks` | manage | Dasselbe für einen Vorschlag |
| POST | `/api/league/admin/online-reports/zugspitze?season=&dryRun=` | manage | **Online-Schach Oberbayern** (0.716.0, siehe unten) für EINE Saison (`20204`, `20211`, `20212`, `20213`, `20221`) → `{ season, source, dryRun, counts{ tournaments, skippedYouth, unreadable, rows, matched, ambiguous, notOnLichess, noRoster, rosterAmbiguous, otherClub, noFide, assigned, conflicting, reports }, reports{ added, updated, unchanged, removed, skipped, dryRun }, accounts{ created, upgraded, unchanged, accepted, takenElsewhere, rejected, failed }, suggestions, items? }` (seit 0.726.0 je Meldung direkt ein Konto „gesichert"; `suggestions` = neue Vorschläge, nur noch für `takenElsewhere`; `items[{ fide, player, user, team, pageName, tournaments[] }]` nur bei `dryRun`); 400 `invalidSeason`, 404 `notFound` (keine Turnierliste), 503 `rateLimited`/`unreachable` |
| POST | `/api/league/admin/self-reports?dryRun=` | manage | Selbstmeldungen einer Quelle einspielen `{ source, reporter?, items[{ fide, site, user (Name oder Profiladresse), team, note? }] }` — ERSETZT die Einträge dieser Quelle → `{ added, updated, unchanged, removed, skipped[{ index, reason }], dryRun }` (`reason` ∈ invalidFide/unknownPlayer/invalidUser/duplicate); 400 `noSource`/`tooMany` (über 5000)/`invalidReporter` (über 60). Mit `reporter` (0.629.0) sind es Meldungen DRITTER: im (i) je Meldendem eine Zeile „Gemeldet von …" (`reported:<Name>`, gleich nach der Selbstmeldung; ok = für ihn gemeldet, fail = für einen anderen Spieler, warn = für ihn ein anderes Konto derselben Seite, sonst „nicht in der Liste von …"), die Selbstmeldung zählt nur Zeilen ohne `reporter` |

**Online-Schach Oberbayern** (0.716.0, Wunsch 2026-10-07 „Online-Konten-Zuordnung für die Region Bayern";
`Services/League/ZugspitzeOnlineReports.cs`): der Schachkreis Zugspitze hat 2020/Q4–2022/Q1 rund 150 Turniere auf Lichess
ausgerichtet (ZugLiga/Kreisliga, ObbLiga/Bezirksliga, Online-KEM, Kreis-Vergleichskämpfe, Blitz/Rapid/Klassik-Serien); je Turnier
steht unter `https://schachkreis-zugspitze.de/onlineergebnis/?saison=…&serie=…&turnier={id}` eine Einzelwertung mit Rang,
„Nachname,Vorname", Verein und Lichess-Wertung — aber OHNE Nutzernamen. Der Importer (Endpunkt oben) liest je Saison
`/onlineturniere/?saison=` (nur Zeilen mit `onlineergebnis`-Link — 4er-MM laufen bei einem anderen Ausrichter; Art aus dem Lichess-Link,
sonst „N Runden" = Schweizer System, bei 404 die andere Art), je Turnier die Ergebnisseite (drei Tabellen nebeneinander: links
Rang/Name/Team/Rating, rechts Punkte + „Perf" bzw. „S-B"; bei Teamkämpfen ab „Einzelwertung") und `lichess.org/api/{swiss|tournament}/{id}/results`
(ndjson). **Zuordnung** über Wertung + Punkte, bei Gleichstand dazu Performance bzw. Sonneborn-Berger — NIE über den Rang (nicht
angemeldete Lichess-Spieler fehlen auf der Seite); mehrdeutig → weg, ein Konto für zwei Zeilen → beide weg. Dann Name + Verein gegen
die Meldelisten der Region Bayern (`LeagueRosterIndex`, voller Vorname Pflicht — die Index-Stufe „Nachname + Initiale" fiele sonst auf
den falschen; mehrere gleichnamige → der mit demselben Verein laut `LeagueOnlineRegion.ClubKeys`; Verein der Seite passt zu keinem
seiner Vereine → `otherClub`; Kreis-Team auf der Seite widerspricht nie; ohne FIDE-ID → `noFide`, Meldungen hängen an der FIDE-ID).
Ein Konto, das in zwei Turnieren zwei Spielern zugeordnet würde → `conflicting`, weg. Ergebnis über `LeagueSelfReportImport`
(ERSETZT die Quelle) als Meldungen Dritter: `Source` „Online-Schach Oberbayern {saison}", `Reporter` „Schachkreis Zugspitze",
`Team` = Verein laut Seite, `Note` = erstes Turnier mit Rang + Wertung („+ n weitere") → im (i) die Zeile „Gemeldet von Schachkreis
Zugspitze". **Konten direkt** (0.726.0, Wunsch 2026-10-08: „solche Konten sollten direkt als gesichert übernommen werden" — bis dahin
je Meldung nur ein Vorschlag, auf Prod lagen 33 offen): je Meldung legt `AccountsAsync` über `LeagueOnlineAccountService.CreateAsync`
(derselbe Weg wie ein Verwalter: Deckel 20 je Spieler, `PatchViewsAsync`, Abruf wecken) ein Lichess-Konto an — `Confidence` „sicher",
`Manual = true`, `AddedBy` „Schachkreis Zugspitze", Kommentar „Online-Schach Oberbayern {saison}: {Turnier} (Rang r, Lichess-Wertung w)
+ n weitere"; vorher der Jahrgang (`ScanRowAsync`; Konten Minderjähriger werden angelegt und bleiben über `LeagueHiddenAccounts`
verborgen). Regeln/Zähler `accounts{…}` (je Meldung genau einer): Konto steht schon bei DIESEM Spieler → „wahrscheinlich" wird „sicher"
(`upgraded`), „sicher" bleibt (`unchanged`); beide Male wird die Meldung an den Kommentar angehängt (` · `), wenn „{Quelle}:" darin
fehlt — nie überschrieben, `AddedBy` bleibt. Konto steht bei einem ANDEREN Spieler → kein Konto (`takenElsewhere`, Warnung im Log mit
beiden FIDE-IDs), stattdessen wie früher ein Vorschlag (`Source = "report"`, Punkte 5, Hinweis „Gemeldet von Schachkreis Zugspitze
(…): Name, Verein — Turniere", nur wenn es für ihn noch keinen gibt, auch keinen verworfenen) — `suggestions` zählt nur diese.
Verworfener Vorschlag (Spieler, Konto) → kein Konto (`rejected`). Offener Vorschlag für dasselbe Konto (gleich welcher Quelle) →
`CreateAsync` räumt ihn weg wie beim Übernehmen (`accepted` statt `created`). `failed` = `CreateAsync` lehnte ab (`tooMany`). `dryRun`
zählt nur. Ein zweiter Lauf derselben Saison ist idempotent (nur `unchanged`). **Bestand nachziehen**: die offenen `report`-Vorschläge
werden zu Konten, indem man die fünf Saisonen (20204, 20211, 20212, 20213, 20221) noch einmal über den Endpunkt laufen lässt — je
Saison erst `?dryRun=true` (erwartet `accepted` ≈ offene Vorschläge, `created` ≈ 0), dann ohne.
**Minderjährige**: Turniere/Serien mit „Jugend" werden nicht abgerufen. **Höflich**: 1 s Pause nach jedem Abruf (je Saison ~2 je
Turnier: 20204 39, 20211 28, 20212 13, 20213 1, 20221 11 Turniere ohne Jugend), Lichess 429 → Abbruch (503 `rateLimited`), geschrieben
wird erst am Ende. Am 07.10.2026 gegen zwei echte Seiten geprüft: Kreisliga-Teamkampf 21/21 zugeordnet, Blitz-Swiss 14/15 (einer
ohne passendes Konto). Reihenfolge: zuerst `?dryRun=true` je Saison ansehen, dann ohne.

**Lichess-Übertragungen** (0.608.0, Wunsch 2026-09-30; `Services/League/LeagueBroadcastImport.cs`, Tabelle `LeagueBroadcasts`):
Partien aus Lichess-Broadcasts von Turnieren am Brett kommen in die Spielerkarten (Quelle `Lichess-Übertragung`), zugeordnet über
`WhiteFideId`/`BlackFideId` wie die Megabase (`LeagueProfileStore.ImportGamesAsync`). Gemessen am 2026-09-30: in neun
Übertragungen 406 Partien mit Ligaspielern, davon fehlten 43 (21 aus der Bundesliga) — der Rest steht über chess-results schon
da; der Gewinn ist vor allem, dass die Partien schon WÄHREND des Turniers da sind. Regeln:
* **Finden**: Lichess-Suche (`/api/broadcast/search`) nach `LeagueBroadcasts:Queries` (Komma-Liste, Vorgabe Austria, Österreich,
  Tirol, Tyrol, Südtirol, Innsbruck, seit 0.712.0 auch Bavarian, Bayerische, Tegernsee, Munich, München — Bayerische
  Einzelmeisterschaften, Bavarian Open, Tegernsee Masters, Munich Chess Festival; „Bayern"/„Oberbayern"/„Landesliga Süd" bringen
  nichts, eine Lichess-Übertragung der Landesliga Süd/Oberliga/Zugspitzliga gibt es nicht; höchstens 10 Seiten je Wort), nur bis `LeagueBroadcasts:MaxYears` (5) zurück; am 30.09. 62
  Übertragungen. Der Takt sucht höchstens alle 20 h (merkt es sich selbst, ein Neustart sucht sofort). Turniere im Ausland per Link
  (`AddAsync`, Turnier- ODER Runden-Link — der Runden-Link wird über `/api/broadcast/-/-/{roundId}` aufgelöst).
* **Einspielen** (`CleanPgn`): nur fertige Partien (kein `*`), Standard ab der Grundstellung, mit mindestens einer FIDE-ID; Datum mit
  Punkten (ältere Übertragungen schreiben „2024-08-24" — sonst erkennt `Merge` die chess-results-Fassung nicht), fehlt es, gilt
  `UTCDate`; nur die Hauptvariante, ohne `[%eval]`/`[%clk]`/Anmerkungen; Kopfzeilen samt `GameURL`.
* **Dieselbe Partie mit ANDEREM Datum** (Kufsteiner Open 2026: Partien 30.05., Turnier 31.07.): `ImportGamesAsync(…, skipSameMoves:
  true)` verwirft sie über `LeagueProfileStore.SameGameKey` (beide Nachnamen + erste 20 Halbzüge, mindestens 10) gegen das, was die
  Karte schon hat (fremde + Vereinspartien).
* **Stand**: laufende alle 6 h neu, noch nicht begonnene warten; fertig = alle Runden `finished` (sonst 3 Tage nach dem letzten Tag)
  → nie wieder. **Falle**: `ImportGamesAsync` leert am Ende den ChangeTracker — den Stand der Übertragung schreibt `SaveAsync` deshalb
  ausdrücklich (`Update`). (Der Bündel-Import, der die fremden Partien der Karten ersetzte und deshalb alle Übertragungen
  zurücksetzte, ist seit 2026-10-08 entfernt.)
* **Takt**: im `LeagueOnlineSyncScheduler` nach Abruf und Konto-Suche, je Runde höchstens 5 min; `LeagueBroadcasts:Enabled=false`
  schaltet es ab. Oberfläche: Reiter „Übertragungen" (`/uebertragungen`, `features/broadcasts/`, nur Verwalter) mit Liste und
  „Hinzufügen und einspielen".

| Methode | Endpoint | Recht | Zweck |
|---------|----------|-------|-------|
| GET | `/api/league/admin/broadcasts` | manage | Alle vorgemerkten Übertragungen (jüngste zuerst) mit Stand |
| POST | `/api/league/admin/broadcasts` | manage | `{ url }` (Turnier- oder Runden-Link, auch die nackte Kennung) → hinzufügen + einspielen; 400 `invalidUrl`, 404 `notFound`, 503 `rateLimited`/`unreachable` |

**Letzte Partien nachspielen** (0.578.0): `GET /api/league/player/{fide}/recent` (und `/api/league/s/{token}/player/{fide}/recent`)
→ `{ fide, games[{ date, vs, color, pgn }] }` — dieselbe Auswahl und Reihenfolge wie `recent` der Karte
(`LeagueProfileBuilder.Recent`, `RecentCount` = 8), aus dem aktuellen Bestand gerechnet (fremde + Vereinspartien). Die Karte holt
sie erst beim ersten Klick und findet die Zeile über Datum + Gegner + Farbe wieder (die gespeicherte Karte kann älter sein);
nachgespielt wird in der Karte selbst (`shared/game-replay.component.ts`: Brett aus Sicht des Spielers, Züge deutsch, Knöpfe,
Pfeiltasten, Pos1/Ende). **Nach Farbe** (0.592.0): ist oben Weiß oder Schwarz gewählt (aus einer Brett-Zeile geöffnet von
selbst), holt die Karte `…/recent?color=w|s` — die letzten acht Partien DIESER Farbe samt PGN und denselben Angaben wie die
Karte (`LeagueProfileBuilder.RecentEntry`); acht gemischte zu filtern ließe oft nur drei übrig. „Beide" zeigt die Liste der Karte. Angemeldet stehen darüber „Zu meinen Partien“ und „Partie teilen“ (0.587.0,
`core/my-games.service.ts`): beide legen die Partie über `POST /api/games/import` in RookHubs „Meine Partien“ (eine schon
vorhandene wird nicht doppelt angelegt, die Id kommt trotzdem zurück); „Zu meinen Partien“ springt dann per Einmal-Code
nach RookHub auf `/games/{id}` (`HandoffService.jumpToRookHub`), „Partie teilen“ holt den Teilen-Token (`GET /api/games/{id}`)
und teilt RookHubs öffentlichen Link `/g/{token}` — am Handy über das Teilen-Blatt, sonst in die Zwischenablage. Ohne
Anmeldung (Teilen-Link) und ohne RookHub zur Adresse (localhost/IP) fehlen beide Knöpfe.

**Aus RookHub** (0.580.0): das ⋮-Menü der eigenen Partie (`/games/:id`) bietet mit `league.contribute` „In die
Vereins-Datenbank (LeagueHub)" — Sprung per Einmal-Code (`HandoffService.jumpToLeagueHub`, Adresse aus `leagueHubUrl` in
`core/partner-site.ts`: rookhub ↔ leaguehub, rookhub-dev ↔ leaguehub-dev, sonst kein Menüpunkt) auf
`/verein/neu?partie={id}`; LeagueHub holt das PGN über `GET /api/games/{id}` (dasselbe Konto) und öffnet gleich die Übersicht.

Oberfläche (LeagueHub): Reiter „Prognosen · Vereinspartien · Partien hinzufügen" (`/`, `/verein`, `/verein/neu`,
`/verein/formular/:id`); ohne Konto `/s/:token/hochladen` und `/s/:token/formular/:key` — der Teilen-Link zeigt die
Aufforderung dazu ganz oben. Die Formular-Korrektur benutzt DIESELBE Sitzung wie RookHubs Korrekturseite
(`features/games/sheet-edit-session.ts`, siehe `src/frontend/CLAUDE.md`).
