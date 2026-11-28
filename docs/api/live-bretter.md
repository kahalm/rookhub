# Live-Bretter der Turnierseite (1.014.0)

Wunsch 2026-11-25: bei Turnieren, denen man auf der Turnierseite folgt, die Bretter live einblenden, sobald es eine
**Lichess-Übertragung** (Broadcast) gibt — z. B. österreichische 1. Bundesliga 2026/27 (chess-results 1404438): Klick auf
eine Mannschaft zeigt deren sechs Bretter auf einer Seite.

## Endpunkte (`LiveBoardsController`, Route `api/live-boards` — bewusst NICHT unter `/api/tournaments`, siehe nginx)

| Methode | Endpoint | Auth | Zweck |
|---|---|---|---|
| GET | `/api/live-boards/{id}` | **AllowAnonymous** (`directory-read`) | Verknüpfung + Runden (`LiveStatus`); ohne Verknüpfung sucht der Server einmal automatisch. 404 = Turnier nicht geholt |
| GET | `/api/live-boards/{id}/rounds/{round}/stream?eval=1` | **AllowAnonymous** (`directory-read`) + höchstens 6 gleichzeitige Ströme je IP (`SseConnectionLimiter`, `LiveBoards:MaxStreamsPerIp`, gesamt `LiveBoards:MaxStreams` 2000) | Server-Sent Events der Runde. 404 `live_not_linked` ohne Verknüpfung, 429 `live_too_many_streams` |
| PUT | `/api/live-boards/{id}/link` `{ url }` | Konto (`user-crawl`) | Von Hand verknüpfen: Turnier-, Runden- oder Partie-Link von `lichess.org/broadcast/…` oder (1.040.0) ein chess.com-Event-Link (siehe unten). 400 `live_invalid_url`, 404 `live_broadcast_not_found` / `live_tournament_not_found`, 503 `live_chesscom_unavailable` |
| DELETE | `/api/live-boards/{id}/link` | `tournaments.manage` | Verknüpfung lösen (falsch zugeordnet); danach sucht das nächste Lesen wieder |
| GET | `/api/live-boards/pgn/{roundId}/{gameId}` | **AllowAnonymous** (`directory-read`) | PGN einer Live-Partie (Kopfzeilen, alle bisherigen Züge, `[%clk]`; `application/x-chess-pgn`) für „Analysieren“ (1.026.0). Aus dem Hub (offener Strom bzw. 30 min gemerkte fertige Runde), sonst nur für Runden, die Lichess kennt, aus dem 60 s gecachten Runden-PGN. 404 `live_game_not_found` |
| POST | `/api/live-boards/views` `{ tournamentId, view }` | **AllowAnonymous** (`live-view-write`, 30/min) | Geteilte Ansicht anlegen (1.029.0) → `{ token, tournamentId, view, canEdit, editKey }`; `editKey` nur hier. 404 `live_not_linked` |
| GET | `/api/live-boards/views/{token}` | **AllowAnonymous** (`directory-read`) | Ansicht lesen; `canEdit` für diesen Aufrufer (Konto des Erstellers oder Header `X-Live-View-Key`). 404 `live_view_not_found` |
| PUT | `/api/live-boards/views/{token}` `{ view }` | **AllowAnonymous** (`live-view-write`), nur Besitzer | Ansicht ändern; alle offenen Ströme folgen. 403 `live_view_forbidden` |
| GET | `/api/live-boards/views/{token}/stream?eval=` | **AllowAnonymous** (`directory-read`) + Stromgrenzen wie oben | SSE der Ansicht: `view`, dann wie der Runden-Strom; bei Änderung erneut `view` + `snapshot`, `view-gone` wenn gelöscht. `eval` = Balken DIESES Zuschauers (ohne Angabe aus; seit 1.039.0 nicht mehr Teil der Ansicht) |
| GET | `/api/live-boards/external?url=` | **AllowAnonymous** (`directory-read`) | „Brett hinzufügen“ aus einer FREMDEN Übertragung (1.025.0; chess.com seit 1.040.0): Partie-, Runden- oder Turnier-Link → `{ roundId, roundName, tourName, url, ongoing, finished, gameId, games[] }`. 400 `live_invalid_url`, 404 `live_broadcast_not_found` / `live_ext_game_not_found`, 503 `live_lichess_busy` / `live_chesscom_unavailable` |

`{id}` ist die chess-results-Nummer wie in `/tournaments/:id`. Gestreamt wird eine
Runde der in `TournamentBroadcastLinks` verknüpften Übertragung (fremde Runden nur als `extra=`, siehe unten) (chess-results-Runde → Lichess-Runde: Zahl im Rundennamen,
sonst Reihenfolge, `LichessBroadcasts.RoundFor` / `TournamentLiveService.RoundsOf`).

### SSE-Format

```
retry: 5000

event: snapshot
data: {"state":"live","round":1,"layout":{…},"games":[…]}

event: game
data: {"id":"1pPEJGno","boardTag":"1.1","white":{"name":…,"elo":2616,"clockSeconds":4896,…},"fen":…,"lastMove":"f8e8","result":"*","eval":null,…}

event: layout        (eine neue Partie hat die Zuordnung geändert)
event: state         {"state":"live|connecting|reconnecting|finished"}
: ping               (alle 20 s)
```

`layout` = `LiveRoundLayout`: Mannschaftsturnier `matches[]` (Begegnung aus der chess-results-Paarung, `boards[]` mit
`gameId` + `homeIsWhite`), Einzelturnier `boards[]` (chess-results-Brett mit `gameId`), `unassigned[]` = übertragene
Partien ohne Brett.

## Weitere Bretter (1.025.0, Wunsch 2026-11-26)

„bau ein, dass ich auch noch weitere Bretter hinzufügen kann“ — und „auch aus ANDEREN Übertragungen: Schwaz-Mannschaftskampf
ansehen und daneben die WM-Partie live“.

- **Adresse** (Teil des geteilten Links, auch für Gäste):
  - `add=` — Bretter DERSELBEN Runde, Schlüssel nach der chess-results-Paarung bzw. Brettposition: Mannschaftsturnier
    `Begegnung.Brett` (`2.3` = Begegnung 2, Brett 3), Einzelturnier `Brett` (`7`), übertragene Partie ohne Brett (`unassigned`) ihre
    Lichess-Kennung. **Warum nicht die Lichess-Kennung?** Beim Einzelturnier gibt es das Brett schon vor Rundenbeginn (die Partie
    erscheint, sobald sie übertragen wird), und setzt der Übertrager die Runde neu auf (neue Kennungen), stimmt der Link weiter.
    Höchstens 24 (`LIVE_MAX_ADDED`). Nur mit `team`/`player` sichtbar — ohne Filter sieht man ohnehin alle Bretter.
  - `ext=roundId.gameId,…` — Bretter aus fremden Übertragungen (dort ist die Kennung ab dem Einfügen bekannt, eine chess-results-
    Paarung gibt es nicht). Höchstens 12 Bretter aus 4 Runden (`ExternalRoundRegistry.MaxBoardsPerStream`/`MaxRoundsPerStream`).
- **Strom**: `…/stream?extra=roundId.gameId,…` — EINE EventSource je Seite. Der Server meldet sich für jede fremde Runde am selben
  `BroadcastLiveHub` an (ein Lichess-Strom je Runde für alle Zuschauer, Leerlauf-Schließen wie gehabt) und reicht ihre Ereignisse im
  selben SSE-Strom weiter: `ext-snapshot { round, tourName, roundName, url, state, games }`, `ext-game { round, game }`,
  `ext-state { round, state }`, `ext-error { round, code }` (`live_ext_not_found` | `live_ext_limit`). Das Abo trägt einen
  **Partiefilter** (`Subscribe(…, only)`): nur die gewählten Partien gehen hinaus, und NUR für sie wird bewertet (Cloud/Engine) — eine
  Runde mit 100 Brettern rechnet für ein hinzugefügtes Brett nicht alle.
- **Missbrauchsschutz** (`ExternalRoundRegistry`, Singleton, keine DB): Runde muss Lichess bekannt sein (`/api/broadcast/-/-/{roundId}`,
  Treffer 5 min, Fehltreffer 10 min gecacht); serverweit höchstens `LiveBoards:MaxExternalRounds` (Vorgabe 10) verschiedene fremde
  Runden gleichzeitig (Zählung je Runde, eine schon beobachtete kostet keinen Platz); dazu wie bisher `SseConnectionLimiter` und das
  Rate-Limit `directory-read`. Link-Prüfung `LichessBroadcasts.BoardLinkOf` (nur Wirt lichess.org, Kennungen `[A-Za-z0-9]{8}`), im
  Browser gespiegelt `parseBroadcastLink`.
- **Oberfläche**: Rahmenknopf „Brett hinzufügen“ (`add`) in der Werkzeugzeile → Dialog (`LiveAddBoardDialogComponent`, 560 px): oben
  „Aus dieser Runde“ mit Suche (nur mit Filter), darunter „Aus einer anderen Lichess-Übertragung“ mit Link-Feld + „Laden“; ein
  Partie-Link wählt seine Partie gleich aus. Zeilen schalten um (☑), „Fertig“ schreibt `add`/`ext`. Hinzugefügte Bretter stehen hinter
  den Mannschafts-/Spielerbrettern (Karte mit Begegnung als Zusatzzeile und ✕), fremde mit „Turnier · Runde“ als Zusatzzeile; der
  Link zu Lichess und „Warte auf n Partie(n) …“ stehen in der Kopfleiste über dem Raster. Seit 1.028.0 alles in EINEM Raster (siehe
  Oberfläche).
- **Analysieren** (1.026.0, Wunsch 2026-11-26): an jeder Karte (eigene, hinzugefügte, fremde) ein Symbolknopf `biotech` → neuer Tab
  `https://rookhub…/analysis?live=roundId.gameId` (Partnerseite aus `partnerSiteUrl`, Prod ↔ Prod, Dev ↔ Dev; ohne Partner kein
  Knopf). Das Analysebrett holt das PGN über `GamesService.livePgn` und lädt es wie `?pgn=` (alle Züge nachspielbar, Kopfzeilen,
  Uhren) — auch ohne Anmeldung (Menüeintrag `analysis` = `All`). Kein PGN in der Adresse: mit Uhren wären es schnell > 8 KB. Ziel-Fenster (1.036.0, `analysisLinkTarget`): in der installierten App (PWA `display-mode: standalone`, Android-TWA mit Referrer `android-app://`) `_self`, sonst `_blank`.
  Der Hub hält dafür je Partie das zuletzt übertragene Roh-PGN (`Feed._pgn`, `BroadcastLiveHub.PeekPgn`).
- **Elo-Schnitt** (1.025.0): `LiveMatchSlot.homeElo/awayElo` + `…EloBoards` — Elo der Spieler an den Brettern der Begegnung, aus dem
  Strom, sonst aus der Startliste; ohne Elo zählt nicht. Im Kopf der Begegnung „Ø 2315“ unter dem Namen, Tooltip „Schnitt über n Bretter“.

## chess.com-Übertragungen (1.040.0, Wunsch 2026-11-28)

„Kann ich bei den Livebrettern auch chess.com-Broadcast-Links einfügen?“ → „ja, bau chess.com ein“. Überall, wo ein fremder
Lichess-Link geht, geht jetzt auch ein chess.com-Event-Link: „Brett hinzufügen“ (Dialog, `?ext=`), Verknüpfen des eigenen
Turniers (`PUT …/link`), „Analysieren“ (`/analysis?live=`), geteilte Ansicht.

- **Linkformen** (`ChessComEvents.LinkOf`, im Browser `parseBroadcastLink`; nur `chess.com` / `*.chess.com`, Sprachpräfix `/de/…`
  erlaubt): `/events/{event}` (auch `/games`, `/results`, `/schedule` … dahinter) → laufende Runde (sonst die zuletzt
  begonnene/fertige); `/events/{event}/{runde}/{partie}` (auch `/events/{event}/dashboard/{runde}/{partie}`) → diese Partie vorgewählt;
  `/events/{event}/{runde}`; `/events/info/{gruppe}` (Event-Gruppe, z. B. London Chess Classic mit mehreren Räumen) → der zuletzt
  geänderte begonnene Raum. `/events`, `/events/current`, Partien außerhalb von Events (`/game/live/…`) → 400 `live_invalid_url`.
- **Kennungen** (ohne Punkt/Komma/Schrägstrich, damit `?ext=roundId.gameId,…` und `/pgn/{round}/{game}` gleich bleiben): Turnier
  `cc~{event-slug}`, Runde `cc~{event-slug}~{runden-slug}` (z. B. `cc~2026-fide-chess-world-championship~03`), Partie = chess.com-Slug
  (`Sindarov_Javokhir-Gukesh_D`). Lichess-Kennungen haben nie ein `~`; `ExternalRoundRegistry.IsRoundId`/`IsGameIdFor` und im Browser
  `isLiveRoundId`/`isLiveGameIdFor` prüfen je Quelle. `TournamentBroadcastLinks.TourId` dafür 12 → 120 Zeichen (Migration
  `WidenBroadcastTourId`).
- **Endpunkte bei chess.com** (keine offizielle Broadcast-API; gemessen 28.11.2026 im Bündel der Events-Seite `events-view.js`,
  ohne Anmeldung, mit unserem User-Agent nicht von Cloudflare gesperrt — ein `curl`-User-Agent bekam 403):
  - `POST https://www.chess.com/events/v1/api/room/{event}` (leerer Rumpf) → Event, `rounds[]` (`slug`, `name`, `startAt`), ALLE
    `games[]` mit `result`, `whiteClock`/`blackClock` (ms), `currentFEN`, `lastMoveAt`, `board`, `revision`, Spieler mit `fideId`,
    `elo`, `title`, `fed`. Live aktuell (vier Abrufe im 13-s-Abstand: neue `revision` je Zug). WM 22 KB, Swiss mit 135 Partien 220 KB
    (gzip ~37 KB).
  - `POST …/game/{event}/{runde}/{partie}` → `moves[]` mit `cbn` = `"e2e4_e4"` (UCI_SAN) und `clock` (ms nach dem Zug); dazu
    `analysis[]` (darum ~360 KB, gzip ~66 KB bei 120 Halbzügen — wird nicht gelesen).
  - `POST …/index/{gruppe}` → Räume einer Event-Gruppe.
  - Nicht nutzbar: `/events/pgn/{eventId}/{roundId}` (verlangt Anmeldung), die Züge im Browser kommen sonst über Socket.IO
    (`nxt.chessbomb.com`). **Robustheit**: es sind interne Endpunkte der Seite — ändert chess.com sie, liefert der Abruf 404 oder
    unlesbares JSON (dann „nicht gefunden“ bzw. `live_chesscom_unavailable`); die gespeicherten echten Antworten in
    `tests/…/Fixtures/ChessCom/` und `ChessComLiveSmokeTests` (`ROOKHUB_LIVE_CHESSCOM=1`, optional `ROOKHUB_LIVE_CHESSCOM_URL`) zeigen
    schnell, was sich geändert hat.
- **Einbau** (`ChessComLiveSource`, `LiveSourceRouter`): die Quelle sieht nach außen aus wie Lichess — Runden-PGN, „Strom“ aus
  PGN-Stücken, Turnier-JSON in Lichess-Form (`ChessComEvents.ToTourJson` → `LichessBroadcasts.ParseTourDetail`). Der Router
  (registriert als `ILichessLiveSource`) gibt `cc~…` an chess.com, alles andere an Lichess. Hub, Zuordnung, Engine-Balken, „Nur ein
  Zug“, Vollbild-Zugliste, „Analysieren“ und geteilte Ansicht laufen dadurch unverändert. PGN je Partie (`ChessComEvents.ToPgn`):
  Kopfzeilen inkl. FIDE-ID (Zuordnung zur Startliste beim eigenen Turnier), `Round "r.Brett"`, `GameURL` = Partie bei chess.com
  (letzter Teil = Kennung), Züge mit `[%clk]`; als „Team“ zählt nur, was nicht die Föderation ist.
- **Takt und Drosselung**: EIN Raum-Abruf je Event und Takt für alle Runden und Zuschauer (`LiveBoards:ChessCom:PollSeconds`, Vorgabe
  8 s, 5–120; Raum im Speicher geteilt) — nur solange der Hub die Runde offen hält (letzter Zuschauer weg → 90 s später Schluss); eine
  Runde ohne begonnene Partie fragt alle 30 s. Züge nur für Partien mit neuer `revision`, höchstens
  `LiveBoards:ChessCom:GameFetchesPerTick` (6) je Runde und Takt, beim Vollstand 12; zuerst die ausdrücklich gewählten Bretter
  (`BroadcastLiveHub.WantedGames` = Partiefilter der `ext`-Abos), dann die am längsten nicht geholten. Bis dahin zeigt eine Partie die
  Stellung aus der Raum-Antwort (`[FEN]`, ohne Zugliste/Uhren) bzw. die zuletzt geholten Züge. Eine 404-Partie wird bis zur nächsten
  Revision nicht erneut gefragt. Ende des Stroms: alle Partien mit Ergebnis und ihre Züge geholt → `finished`.
  `HttpChessComEventsApi`: höchstens 2 Abrufe gleichzeitig, mindestens 250 ms Abstand, 15 s Zeitgrenze, gzip; nach 429/403 ruht die
  ganze Quelle 2 min (jeder Abruf wirft sofort `ChessComUnavailableException`); sonst verdoppelt der Strom seinen Takt bis 2 min und
  gibt nach drei Fehlern in Folge an den Hub zurück (der pausiert wie nach einem Lichess-429 90 s, Zustand `reconnecting`).
  `ChessComUnavailableException` erbt von `LichessRateLimitedException` (seit 1.040.0 nicht mehr `sealed`).
- **Auswahl im Dialog**: kommt direkt aus der Raum-Antwort (Namen, Titel, Elo, Ergebnis, nach Brett) — ohne die Züge jeder Partie zu
  holen. „Analysieren“ holt bei chess.com immer über die Quelle (Züge aus dem Speicher, sonst ein Abruf der Partie), weil das PGN im
  Hub zeitweise nur die Stellung trägt.
- **Oberfläche**: Platzhalter `lichess.org/broadcast/… · chess.com/events/…`, Texte „Übertragung (Lichess oder chess.com)“. Liste
  „Übertragungen“: eigene Zeile `{Turnier} · chess.com` bzw. `· lichess.org` (`broadcastHost`), fremde Runden „Turnier · Runde ·
  chess.com“ mit Link auf `chess.com/events/{event}/games` (`broadcastRoundUrl`; eine Runden-Seite gibt es dort nicht); Begegnungen
  eines chess.com-verknüpften Turniers verlinken ebenfalls die Event-Seite.
- **Engine-Balken**: Lichess-Cloud-Eval und Hintergrund-Engines arbeiten auf der Stellung (FEN) — für chess.com-Bretter genauso
  wie für Lichess-Bretter; `[%eval]` schickt chess.com nicht.
- **Nicht gemacht**: automatische Suche nach chess.com-Übertragungen (der `BroadcastFinder` sucht nur bei Lichess — verknüpfen geht
  von Hand); `?add=` für nicht zugeordnete Partien eines chess.com-verknüpften Turniers (Schlüssel bleibt die 8-stellige Lichess-Kennung,
  ohne Filter sieht man ohnehin alle).

## Geteilte, lebende Ansicht (1.029.0, Wunsch 2026-11-26)

„Wenn ich den Link share, nicht mit round=2&team=…&eval=1&ext=…, sondern einfach einen Hash — wenn ich Bretter ergänze oder
entferne oder etwas umstelle, sollen alle mit dem Link das auch sehen.“

- **Link** `/live/{token}` (Turnierseite, Route `live/:token`, dieselbe `LiveBoardsComponent`): Token 10 Zeichen base62
  (`RandomNumberGenerator`, ~59 bit). Die langen Adressen bleiben gültig.
- **Anlegen**: „Link teilen“ auf der langen Adresse → `POST …/views` mit der aktuellen Ansicht (Runde immer ausdrücklich, wie bisher
  im langen Link) → Bearbeitungsschlüssel (24 Zeichen) in `localStorage rookhub_live_view_keys` (`device`, höchstens 50, älteste raus;
  so kann auch ein Gast seine Ansicht weiter ändern) → Seite wechselt mit `replaceUrl` auf `/live/{token}` → kurzer Link wird geteilt.
  Scheitert das Anlegen (z. B. 429), wird die lange Adresse geteilt. In der Tabelle steht nur der SHA-256 des Schlüssels; angemeldet
  zählt zusätzlich das Konto (`OwnerUserId`). Nur für verknüpfte Turniere.
- **Was geteilt ist** (1.039.0, Wunsch 2026-11-28: „beim shared link sollen nur die bretter bei allen gleich sein (sprich wenn ich
  eins ergänze sollen es alle haben). engine + bester zug soll jeder selbst ein/ausschalten können, standard mit engine + pfeil“):
  NUR die Brettauswahl — `LiveViewState` = `round`, `team`, `player`, `add`, `ext` (Reihenfolge der Bretter folgt daraus).
  Engine-Balken und Pfeil „Bester Zug“ schaltet jeder selbst (Besitzer wie Zuschauer), gemerkt im Browser (`rookhub_live_eval`,
  `rookhub_live_best`), Standard beide AN. Vor 1.039.0 trug die Ansicht `eval`: alte Zeilen behalten es im JSON, `SharedLiveViews.Parse`
  übergeht es (keine Migration), beim nächsten Speichern fällt es weg; ein `eval` im `PUT`/`POST` alter Browser wird ebenso übergangen.
- **Besitzer** (`canEdit`): Runde, Mannschaft/Spieler, Bretter hinzufügen/entfernen wirken sofort lokal und gehen
  entprellt (`LIVE_VIEW_SAVE_MS` 400 ms, mehrere Klicks = ein PUT) als `PUT …/views/{token}` mit Header `X-Live-View-Key`. Bis dahin
  überschreibt ein `view`-Ereignis die eigene Änderung nicht. Balken/Pfeil des Besitzers: kein `PUT`, nur sein Strom. Hinweiszeile
  „Geteilte Ansicht — die Bretter sind für alle mit dem Link gleich, deine Änderungen daran sehen alle. Engine und besten Zug schaltet
  jeder selbst.“
- **Zuschauer** (schlank entschieden): sie folgen der Brettauswahl. Balken und Pfeil sind ihre eigenen (Strom mit `eval=`, das
  `view`-Ereignis fasst sie nicht an). Jede Änderung an den Brettern (Runde, Mannschaft, Brett) macht daraus ihre eigene Ansicht:
  Sprung auf die lange Adresse mit der Änderung (ohne `eval`) — die geteilte bleibt unberührt. Hinweiszeile sagt das.
- **Strom** `…/views/{token}/stream`: EINE Verbindung je Zuschauer. Der Server liest die Ansicht, schickt `view` und streamt die Runde
  wie `…/rounds/{n}/stream` (Filter, `add`, `ext` aus der Ansicht). Ändert der Besitzer sie, weckt `LiveViewChanges` (Singleton, im
  Speicher — die API läuft als eine Instanz) alle offenen Ströme dieser Kennung: sie beenden die Abos am Hub, lesen neu und hängen sich
  um (neuer `view` + `snapshot`, ggf. `ext-snapshot`). KEIN zusätzlicher Lichess-Strom: der `BroadcastLiveHub` hält je Runde einen,
  ein Rundenwechsel nutzt dessen 90-s-Nachlauf. Runde ohne Übertragung: `state offline`, dann Warten auf die nächste Änderung mit
  Lebenszeichen. Gelöscht/abgelaufen: `view-gone` → Hinweis „Diese geteilte Ansicht gibt es nicht mehr.“
- **Speicherdauer**: 30 Tage ohne Aufruf (`Retention:LiveViewDays`), Deckel 50 000 (`Retention:LiveViewCap`), Aufräumlauf
  `AnonymousDataRetentionService`; „zuletzt gesehen“ wird höchstens stündlich nachgeführt (`SharedLiveViews.TouchEvery`). Konto
  gelöscht → seine Ansichten auch (`TournamentDataEraser`). Vorschau: `docs/api/link-vorschau.md`.

## Ablauf im Server (`Services/Tournaments/Live/`)

- **Finden** (`BroadcastFinder`, Singleton): `/api/broadcast/top` (10 min gecacht) — steht in `info.standings`/`website`
  `tnr{id}`, ist es diese (die Bundesliga trägt `tnr1404438`). Sonst nur für ein gerade laufendes Turnier (Termin um heute,
  ohne Termin zuletzt vor ≤ 3 Tagen geholt) die Lichess-Suche mit Wörtern des Namens (AUT → Austria, Jahreszahlen weg; die
  Suche verlangt ALLE Wörter) und bei höchstens drei Kandidaten der FIDE-Abgleich der zuletzt begonnenen Runde (≥ 4 IDs der
  Startliste, ≥ Hälfte der Runde). Kein Treffer → 6 h Ruhe je Turnier.
- **Strom** (`BroadcastLiveHub`, Singleton): je Lichess-Runde mit Zuschauern GENAU EINE Verbindung zu
  `GET /api/stream/broadcast/round/{id}.pgn`. Erster Zuschauer → einmal `/api/broadcast/round/{id}.pgn` (Vollstand), dann
  der Strom; Lichess schickt nach dem Verbinden alle Partien noch einmal — Unverändertes (gleicher Hash) geht nicht hinaus.
  Abriss → neu verbinden, Pause 2 s … 60 s, nach 429 90 s für alle Abrufe. Letzter Zuschauer weg → Strom nach 90 s zu (ein
  Neuladen reißt ihn nicht ab). Schließen und Entfernen des Feeds geschehen atomar unter dem Hub-Lock, Anmelden
  ebenso (Sperrreihenfolge Hub → Feed) — ein Zuschauer gerät nie an einen sterbenden Feed (1.014.1). Fertige Runden (Lichess `finished` oder alle Partien mit Ergebnis): kein Strom, die
  Endstellungen bleiben 30 min im Speicher.
- **Bestand = Runden-Export** (1.035.1). Der Strom meldet nie, dass eine Partie verschwunden ist; der Hub sammelte deshalb
  jede je gesehene Game-Id. Prod 27.11.2026 (1. BL, Runde 3, Lichess-Runde `AE2OT3gp`): der Veranstalter legte die Runde
  zweimal neu an — vorbelegte Partien mit den Paarungen aus Runde 1 ohne Zug (z. B. `5mv2r87i`), dann eine zweite (`WmuQuGE5`,
  `wMAduO8I` …) und eine dritte Generation (`fGU4RMj9`, `W0ALPVpb` …). Der Hub hielt 108 statt 36 Partien, die Begegnung
  zeigte je Brett zwei. Seither: jeder Vollabzug ERSETZT den Bestand (`Feed.Replace`) — was der Export nicht führt, fliegt
  raus (samt PGN für „Analysieren“); was der Strom NACH dem Start des Abrufs brachte (`LiveGame.Seen` > Marke), bleibt; ein
  leerer Export entfernt nichts. Während der Strom läuft, gleicht der Hub alle `ResyncEvery` (5 min) und nach jeder
  Neuverbindung (frühestens `ResyncMinGap` = 1 min nach dem letzten Abgleich) ab: EIN Abruf je Runde über die gedrosselte
  Quelle, nach 429 fällt er aus. Fiel etwas weg, bekommen alle Zuschauer ein neues `snapshot` (samt `layout`); die Seite
  ersetzt damit ihren ganzen Stand (`live-boards.component`, Spec „neuer Vollstand mitten im Strom“).
- **Zerlegen** (`BroadcastPgnSplitter`, `LiveGameParser`): Partien aus beliebigen Stücken, fertig beim Ergebnis-Token; je
  Partie FEN (Gera.Chess), letzter Zug (UCI), Uhren aus `[%clk]` (letzter Wert je Seite), `[%eval]`, Namen/Elo/Titel/FIDE-ID/
  Mannschaft aus den Kopfzeilen.
- **Zuordnung** (`LiveBoardAssembler`): FIDE-ID (Startliste ↔ `WhiteFideId`/`BlackFideId`), Rückfall Name (Wörter sortiert,
  ohne Akzente). Einzel: Paarung der Runde (Namen → Startliste). Mannschaft: der Crawler kennt je Runde nur die Begegnungen —
  die Partie gehört zur Begegnung der beiden Mannschaften der Spieler (Startliste, sonst `WhiteTeam`/`BlackTeam`). Innerhalb
  der Begegnung ordnet der Lichess-Tag `Round "1.n"`: die Übertragung nummeriert blockweise je Begegnung in Brettreihenfolge
  (1. BL R1: „1.1"–„1.6" Pinggau–Schwaz, „1.7"–„1.12" nächste Begegnung …, Farben abwechselnd); nur ohne Tag die
  Meldelisten-Nummer (1.014.1). Brettnummer = Position in dieser Ordnung. Einzelturnier: Brettnummer = chess-results.
  Sicherheitsnetz (1.035.1, `LiveBoardAssembler.Current`): mehrere Partien auf DEMSELBEN Tag „r.n“ → jede ältere (kleineres
  `Seen`, die Laufnummer des Feeds beim letzten Empfang) fällt weg, wenn sie dieselben Spieler hat (neu angelegt) oder keinen
  Zug (vorbelegt). Zwei verschiedene Partien mit Zügen auf einem Tag bleiben beide; Tags ohne Punkt („3“) greifen nicht.
- **Engine-Balken** — Rückfallkette je Stellung, nur solange ein Zuschauer der Runde ihn an hat (`?eval=1`):
  1. `[%eval]` aus dem Strom (die Bundesliga schickt KEINS, gemessen 25.11.2026, nur `[%clk]`);
  2. Lichess Cloud-Eval (`LichessCloudEval`: je Stellung gecacht, Treffer 6 h, Fehltreffer 15 min, ein Abruf je Sekunde,
     nach 429 Pause) — kennt Mittelspielstellungen meist nicht;
  3. **Hintergrund-Engines** (seit 1.027.0; vorher 1.015.0 eine einzelne „Live-Engine“ mit 1,5 s). Befund 26.11.: auf Prod
     rechnete die Live-Engine NIE — die Auswahl nahm ohne Einstellung das erste Admin-Konto (Id 2, keine Engines), alle Engines
     gehören Id 5; im Strom kamen nur Cloud-Werte. Wünsche des Users: „nur die Bretter rechnen, die jemand ansieht“, „kannst du
     die nicht einfach in die Hintergrundengines werfen?“, „Stellungen nur bis Tiefe 30 rechnen“.
     - **Nur Sichtbares**: der Strom bekommt den Filter der Ansicht (`team`, `player`, `add`; `LiveView.Visible`), das Abo
       (`RoundSubscription.SetVisible`) bewertet nur diese Bretter, mit Filter erst ab der Zuordnung (vorher nichts). Ohne
       Filter alle Bretter der Runde, fremde Bretter (`ext`) ohnehin nur die gewählten. Bedarf = Vereinigung über alle
       Zuschauer mit Balken; Cloud-Eval genauso nur für Angezeigte.
     - **Cloud genügt ab Tiefe 30**: eine Cloud-Bewertung wird sofort gezeigt; ist sie ≥ `Depth` (30), rechnet keine Engine.
     - **Wo**: `LiveEngineQueue` (Singleton, über ALLE Runden) verteilt auf die Hintergrund-Engines des Kontos der
       Analyseaufträge (`RegistryLiveEnginePoolSource`: `OwnerUserName`, sonst die Haus-Engine `EngineOwnerResolver.HouseOwnerAsync`,
       sonst das erste Admin-Konto MIT Hintergrund-Engines; nur online stehende `rhe_`-Engines der Hintergrund-Liste, ohne
       `AnalysisJobs:ExplicitOnlyEngineIds`; Liste 30 s gecacht). PARALLEL: je freie Engine eine Stellung, reihum über die
       Bretter, gedeckelt durch `MaxParallel` (0 = alle).
     - **Neben den Analyseaufträgen** (`Services/Engines/LiveEngineLane.cs`): eine Engine ist frei, wenn dort KEIN Auftrag
       rechnet und niemand am Analysebrett live darauf rechnet — ein laufender Auftrag wird nie abgewürgt. Warten Live-Stellungen,
       beansprucht die Schlange die Engines (`Claim`): der `AnalysisJobWorker` startet dort keinen NEUEN Auftrag (Vorrang vor
       allen Stufen von `JobPriority`), und endet einer, weckt er die Schlange (`JobEnded`). Die Live-Rechnung meldet sich beim
       `EngineActivityTracker` an (Admin-Seite „Engines“ zeigt sie als Live) und ab OHNE Ruhephase (`End(…, grace: false)`) —
       ist die Stellung fertig, darf der Worker sofort wieder. Zum Datenbank-Auftrag (`AnalysisJobs`) wurde sie bewusst nicht:
       je Zug eine Zeile anlegen/löschen, Takt des Workers als Verzögerung, kein Abbruch beim nächsten Zug und keine
       Zwischenstände ohne eigenen Weg.
     - **Je Brett nur die neueste Stellung**: ein neuer Zug ersetzt den wartenden Wunsch an seinem Platz und bricht die laufende
       Rechnung des alten ab; sieht niemand das Brett mehr mit Balken (Filter, Balken aus, Seite zu), bricht sie beim nächsten
       Zwischenstand ab.
     - **Zwischenstände**: `BrokerLiveEngineEvaluator` rechnet bis `depth` 30 (2 Linien seit 1.033.0, vorher 1; Threads/Hash wie die Hintergrund-Aufträge
       = was die Engine anbietet, Sitzung `rh-live-boards`, harte Grenze `MaxSeconds` 180 s — das Erreichte gilt); jede tiefere
       Zeile geht als Bewertung `source: "engine", depth: n` an den Balken (höchstens alle 0,7 s je Brett), tiefer ersetzt flacher.
       Ergebnis je Stellung 6 h gecacht (`IsSettled`), auch für andere Bretter/Runden mit derselben Stellung.
     - **Wie viele parallel**: so viele, wie Hintergrund-Engines frei sind (Prod: alle online stehenden Engines der Hintergrund-
       Liste von kahalm, ohne Lc0). Rechnen dort gerade Partieanalysen, kommen die Live-Bretter erst dran, wenn ein Auftrag endet
       — dann aber vor jedem neuen Auftrag; bei vielen sichtbaren Brettern (z. B. ganze Runde ohne Filter) läuft die
       Partieanalyse so lange langsamer. Liefert eine Engine nichts (Provider weg), wird sie 60 s ausgelassen.
  Quelle im Eval: `source` = `broadcast` | `cloud` | `engine`, dazu `depth`; die Karte zeigt „+0.4 · Tiefe 18“ und die
  Quelle als Tooltip.
  **Bester Zug** (1.030.0, Wunsch 2026-11-26): `best` = erster Zug der Hauptvariante als UCI (`LiveEval.FirstMove`: Cloud
  `pvs[0].moves` als Text, Broker `pvs[0].moves[0]` als Liste; `[%eval]` hat keinen → `null`). Immer mitgeschickt (ein paar Byte,
  kein eigener Abruf). Schalter „Besten Zug zeigen“ in der Werkzeugzeile: Gerätevorliebe `localStorage rookhub_live_best`
  (`device`), bewusst NICHT in der Adresse und nicht in der geteilten Ansicht — jeder Zuschauer selbst; gesperrt ohne Balken (ohne
  Bewertung kein Zug). An: blauer Pfeil auf dem Brett (`app-chess-board` `arrows`) und das SAN (Figurenbuchstaben der Sprache) fett
  hinter „+0.4 · Tiefe 22“. Nur zur Bewertung der AKTUELLEN Stellung (`game.eval`): hält die Karte nach einem Zug den alten Wert,
  gibt es keinen Pfeil; ist der Zug in der Stellung nicht legal (`liveBestMove`), auch nicht. Rochade als `e1h1` wird zu `e1g1`.

  **Nur ein Zug** (1.033.0, Wunsch 2026-11-26: „markieren, wenn es nur einen Zug gibt, der gut ist, und alle anderen z. B.
  1 Punkt schlechter“; Präzisierung „wenn der 2.-beste Zug noch +3 ist, dann ist alles wurst“):
  - **Zwei Linien**: die Engines rechnen MultiPV 2 (`BrokerLiveEngineEvaluator.LiveMultiPv`, gleiche Zieltiefe 30, kostet je
    Stellung etwa das 1,5-Fache; der Broker meldet nur volle Sätze, `depth` = Minimum beider Linien, Zwischenstände tragen beide).
    Cloud-Eval mit `&multiPv=2` (gemessen 26.11.: nach 1.e4 `e7e5 cp 22`, `c7c6 cp 27` — auch die zweite Linie Weiß-Sicht).
    Kennt die Cloud nur eine Linie → keine Markierung (die Engine rechnet ohnehin, wenn Tiefe < 30). `[%eval]` → nie.
    Nur ein legaler Zug → keine zweite Linie → keine Markierung (erzwungen, nichts zu finden).
  - **Regel** (`LiveOnlyMove`, rein, einmal im Server je Bewertung in `BroadcastLiveHub.ApplyEval` — auch für Cache-Treffer): beide
    Linien in die Sicht der Seite am Zug drehen (Broker/Cloud liefern Weiß-Sicht), Matt für den Ziehenden = +∞ (früher besser),
    gegen ihn = −∞. Zweitbester ≥ `LiveBoards:OnlyMoveIrrelevantPawns` (3,0) oder selbst Matt für den Ziehenden → egal, nichts.
    Bester ≤ −`LiveBoards:OnlyMoveLostPawns` (5,0) oder Matt gegen ihn → verloren, nichts. Sonst Abstand ≥
    `LiveBoards:OnlyMoveGapPawns` (1,0 Bauern) → „Nur ein Zug“. Erst ab Tiefe `MinShowDepth` (20), sonst flackert es.
    Beispiele: +2,0 / +0,8 → ja; +4,5 / +3,1 → nein; −3,8 / −4,9 → ja (Rettungszug); Matt in 3 / +0,5 → ja; Matt / Matt später → nein.
  - **Warum Bauern statt Gewinnprozent**: der User denkt in „1 Punkt“; die Grenzen +3/−5 nehmen genau die Stellungen heraus, in denen
    ein Bauernabstand nichts mehr bedeutet — der Hauptvorteil einer Gewinnprozent-Kurve. Zwischen −5 und +3 ist ein Bauer fast
    überall eine echte Entscheidung.
  - **Daten**: `eval.secondCp`/`secondMate` (Weiß-Sicht) und `eval.onlyMove: true` — alle drei fehlen im JSON, wenn unbekannt bzw.
    falsch (`WhenWritingNull`). Der Browser rechnet nichts nach.
  - **Oberfläche**: Chip „Nur ein Zug“ vor der Uhr der Seite am Zug, Tooltip „bester +0.3, nächstbester −1.2“; nur mit Balken, nur
    zur aktuellen Stellung, verrät den Zug nicht (Pfeil weiter nur mit „Besten Zug zeigen“).

  **Einstellungen** (`LiveBoards:Engine:*`, `LiveEngineOptions`):

  | Schlüssel | Vorgabe | Bedeutung |
  |---|---|---|
  | `Enabled` | `true` | aus = nur Strom + Cloud |
  | `OwnerUserName` | Haus-Engine, sonst erstes Admin-Konto mit Hintergrund-Engines | wessen Engines |
  | `EngineIds` (Liste, auch `EngineId`) | leer | nur diese Engines (online, `rhe_`); leer = die Hintergrund-Liste ohne Nur-auf-Anforderung |
  | `Depth` | 30 | Zieltiefe (10–60); Cloud mindestens so tief → keine Engine |
  | `MinShowDepth` | 20 | Zwischenstände erst ab dieser Tiefe an den Balken (vorher schlägt er nach einem Zug kurz aus); bis dahin hält die Karte den letzten Wert (1.027.1) |
  | `MaxSeconds` | 180 | harte Grenze je Stellung (10–1800) |
  | `MaxParallel` | 0 | höchstens so viele Bretter gleichzeitig (0 = alle freien Engines) |
  | `Threads` / `HashMb` | 0 / 0 | 0 = was die Engine anbietet (wie die Hintergrund-Aufträge) |
- Lichess-Abrufe mit dem User-Agent des übrigen Lichess-Codes (`HttpClientsSetup`, Client `lichess-live`, ohne
  Gesamt-Timeout für den Strom), ohne persönliche Daten.

Fertige Partie mit Signal-Königszug des Siegers am Ende (DGT-Brett, 1.022.0): `LiveGameParser.Parse` lässt ihn weg, das Brett
zeigt die Stellung davor — Regel in `Services/Games/SignalKingMove.cs` (siehe `docs/api/leaguehub-vereins-datenbank.md`).

## Vorausanalyse der eigenen Mannschaft (1.034.0)

Nicht Teil des Hubs: die laufenden Partien der eigenen Mannschaft (Schwaz in der Bundesliga) rechnet
`Services/League/ClubBroadcastPreAnalysis.cs` schon während der Runde mit Tiefe 30 und fünf Linien auf den
Hintergrund-Engines vor — gespeist vom 10-Minuten-Takt der Liga-Übertragungen, NICHT vom Strom dieser Seite (läuft also
auch ohne Zuschauer, ohne zusätzlichen Lichess-Abruf). Die Live-Schlange (`LiveEngineLane`) geht dieser Arbeit wie jeder
Stufe vor. Einzelheiten: `docs/api/leaguehub-vereins-datenbank.md` und `docs/api/hintergrund-analyseauftraege.md`.

## Proxy-Kette

- Turnier-nginx (`src/frontend/nginx.conf`): eigene `location ^~ /api/live-boards/` mit `proxy_buffering off`,
  `proxy_cache off`, 3600 s Zeitschranken und `proxy_pass_header X-Accel-Buffering` (sonst verschluckt nginx den Kopf und der
  Nginx Proxy Manager davor puffert). `DeploymentConfigTests.LiveBoardsLocation_…` hält das fest.
- API: `text/event-stream` steht nicht in den Typen der Response-Compression, der Controller setzt
  `Cache-Control: no-cache, no-transform`, `X-Accel-Buffering: no` und schaltet das Puffern ab (`DisableBuffering`).

## Oberfläche (Turnierseite, `features/tournaments/live/`)

- Seite `/tournaments/:id/live?round=&team=&player=` (`LiveBoardsComponent`), auch ohne Konto. Detailseite: Knopf
  „Live-Bretter“ in der Aktionsleiste, sobald verknüpft; sonst ⋮ → „Übertragung verknüpfen“. Paarungen: „Live-Bretter dieser
  Runde“ neben der Rundenwahl. Mannschaftsfenster: „Live-Bretter“ (Mannschaft + ggf. Runde), sonst leiser Hinweis.
  Begegnungen eines Spielers (Einzelturnier): „Live-Bretter“ mit `?player=`.
- Mannschaft gewählt: nur ihre Begegnung, Bretter aus ihrer Sicht (sie steht unten), Stand oben (chess-results, solange
  leer: Summe der Lichess-Ergebnisse). Ohne Wahl alle Begegnungen aus Sicht der Heimmannschaft.
- **Ein Raster** (1.028.0, Wunsch 2026-11-26 „ich hätt die Bretter gern in einer Wurscht, damit ich 8 auf einmal gut sehe“):
  `LiveBoardsComponent.allBoards` reiht Begegnung(en)/Runde, hinzugefügte (`add`), fremde (`ext`) und weitere übertragene Partien
  in EINE Folge, gezeichnet in einem `.board-grid` (`auto-fill, minmax(min(100%, 240px), 1fr)`, Seite bis 1880 px): 1920 px → 7
  Spalten, 1280 px → 4–5, 390 px → 1. Herkunft je Karte als Zusatzzeile; Stand/Elo-Schnitt je Begegnung und Links der fremden
  Runden als kompakte Kopfleiste darüber.
- **Übertragungen** (1.032.0, Wunsch 2026-11-26 „sammel alle Links … und schreib die untereinander“): Liste über dem Raster, je
  Zeile eine Quelle — eigenes Turnier (`status.url`), je Begegnung Stand + Ø-Elo mit Link `lichess.org/broadcast/-/-/<eigene
  Lichess-Runde>`, je fremde Runde „Turnier · Runde“ mit ihrem Lichess-Link (sonst `…/broadcast/-/-/<roundId>`).
- **Brett drehen** (1.031.0, Wunsch 2026-11-26): Symbolknopf `swap_vert` im Kartenkopf (vor Analyse/✕, 40 px, `aria-pressed`)
  kehrt die Vorgabe (gewählte Mannschaft bzw. Spieler unten, sonst Heim/Weiß) für DIESE Partie um — Spieler, Uhren, Balken-Richtung
  und Pfeil drehen mit (alles hängt am `flipped` der Karte). Nur dieser Zuschauer, diese Sitzung (`sessionStorage
  rookhub_live_turned`, Partie-Kennungen, höchstens 100), nicht in Adresse oder geteilter Ansicht — auch nicht beim Besitzer
  (schlank entschieden: Ausrichtung ist Sache jedes Zuschauers).
- **Zugnummer** (1.028.0, Wunsch „zeig auch irgendwo die aktuelle Zuganzahl an“): im Kartenkopf „Zug n“ aus `plies`
  (`liveMoveNumber`: laufend `floor(plies/2)+1` = der Zug, der gerade gespielt wird, fertig `ceil(plies/2)`, vor dem ersten Zug
  nichts), Tooltip wer am Zug ist.
- **Vollbild** (1.036.0, Wunsch 2026-11-27 „beim Vollbildbutton mach das Livebrett groß, blende nebenbei die Zugfolge ein, Engine
  mit Lines (lokale) und mach die Option, dass man selber Züge macht“): der Vollbild-Knopf über dem Brett (`app-chess-board`
  `fullscreenMode="custom"`, vorher: nur das Brett mit schwarzen Balken ins Vollbild) schickt die GANZE Karte ins Element-Vollbild
  — ohne Element-Vollbild (iOS) legt sie sich `position: fixed` übers Fenster, der Knopf erscheint dort jetzt auch. Hochformat:
  Brett volle Breite, darunter das Seitenteil; quer ab 600 px (`lb-wide`): Brett `min(100dvh − 150 px, 100vw − 420 px)`, rechts
  das Seitenteil mit eigener Scrollfläche. Seitenteil (`live-board-study.component.ts`): ⏮ ◀ ▶ ⏭, „Live“ bzw. „Zur Live-Stellung“,
  „Engine“ (Browser-Stockfish über `LiveEngineSession` + `app-live-engine-panel` der Partieseite, Linien 1–5 per natives `<select>`,
  gemerkt je Gerät `localStorage rookhub_live_fs` `{engine, lines}`, Vorgabe an/3), „Selbst ziehen“, die Zugliste (`app-move-list`
  samt `[%clk]`). Zustand in `live-study.ts` (`LiveStudy`): Züge einmal per `GET /api/live-boards/pgn/{round}/{game}`, danach hängt
  jedes Live-Ereignis seinen `lastMove` an, wenn er genau auf die letzte Stellung passt — sonst (`stale`, mehrere Züge verpasst) holt
  die Karte das PGN gebündelt nach 1,5 s neu; das Brett zeigt beim Folgen ohnehin die Live-FEN. `cursor` null = folgt (am letzten
  Zug, neue Züge rücken nach), sonst bleibt die Ansicht stehen. Der erste eigene Zug heftet die Ansicht an den Zug, an dem man steht;
  Live-Züge laufen in der Zugliste weiter, die eigene Variante bleibt, bis „Zur Live-Stellung“, ein Zug-Klick oder „Selbst ziehen“ aus
  sie verwirft. Balken/Pfeil im Vollbild: Engine an → Wert und Pfeil der Browser-Engine; aus → wie bisher, aber nur an der
  Live-Stellung (abseits davon leerer Balken, kein Pfeil, kein „Nur ein Zug“). Tasten ←/→/Pos1/Ende, Esc schließt. Kein CDK-Overlay
  im Seitenteil (die Turnierseite hängt Overlays nicht ins Vollbild-Element). Gäste und `/live/<token>` genauso (PGN ist anonym).
  Die Turnierseite liefert dafür `assets/stockfish/` aus (angular.json, ngsw-Gruppe `engine` lazy).
- Brett = geteiltes `app-chess-board` (nur Anzeige; ein Klick tut vorerst nichts), letzter Zug markiert, Uhren (die Seite am
  Zug zählt sekündlich herunter — Näherung ab dem Eintreffen; im E-Ink-Modus steht sie), Ergebnis als Chip.
- Engine-Balken: Schalter in der Kopfzeile, gemerkt je Gerät (`localStorage rookhub_live_eval`, `device`), **Standard an**
  (1.039.0; nur `0`/`false` heißt aus, gesperrter Speicher → an). „Besten Zug zeigen“ ebenso Standard an. Die lange Adresse
  nimmt `?eval=1|0` weiter an (alte geteilte Links): ausdrücklich gesetzt gilt es vor der Gerätevorliebe, ohne sie zu überschreiben;
  der Schalter schreibt seit 1.039.0 NICHT mehr in die Adresse, sondern merkt die Wahl und nimmt ein vorhandenes `eval` heraus
  (`replaceUrl`), sonst überstimmte es die Wahl beim Neuladen. Der kurze Link `/live/{token}` kennt kein `eval`. Gäste dürfen den
  Balken nutzen (der Strom ist `AllowAnonymous`, auch mit `eval`).
- **Vibrieren** (1.035.0, Wunsch 2026-11-27): Schalter „Vibrieren“ (Standard aus, `localStorage rookhub_live_vibrate`, `device`;
  nicht in Adresse/geteilter Ansicht). Fehlt, wo `navigator.vibrate` fehlt (iOS Safari, Desktop-Firefox). Erkennung rein in
  `live-vibrate.ts` (`LiveAlertTracker`): **Ergebnis** — eine Partie, die live lief, bekommt 1-0/0-1/½-½ → `[400,150,400]`;
  **Bewertungssprung** — Bewertung aus Weiß-Sicht in Bauern, Matt/große Werte auf ±10 gekappt, gegen Min/Max der Bewertungen
  der letzten 8 Halbzüge (4 Züge): Ausschlag ≥ 1,0 → `[120,80,120,80,120]`, danach beginnt das Fenster neu bei der aktuellen
  Bewertung. Eine tiefere Bewertung derselben Stellung ersetzt die alte. `snapshot`/`ext-snapshot` (Laden, Wiederverbinden,
  Rundenwechsel) setzen nur den Anfangsstand, nie Vibration. Nur gezeigte Bretter; Sprung nur mit Balken (sonst kommt keine
  Bewertung); ein Sprung frühestens 2 s nach der letzten Vibration, ein Ergebnis immer. Das Einschalten vibriert kurz (`[60]`) —
  der Klick ist die Nutzerinteraktion, die Chrome vor `vibrate` verlangt. Läuft nur, solange die Seite offen und sichtbar ist
  (kein Push im Hintergrund).
- **Link teilen** (1.024.0, Wunsch 2026-11-26: „einen speziellen Livestream … auch für anonyme User sharen“): Knopf im Kopf,
  Link = `/tournaments/{id}/live?round=n[&team=…|&player=…]` (Rückfall, wenn das Anlegen der geteilten Ansicht scheitert; bis
  1.038 mit `&eval=1`, seit 1.039.0 ohne — jeder Zuschauer schaltet selbst) — die Runde immer ausdrücklich (ohne sie zeigte der Link
  am nächsten Spieltag die nächste Runde). Am Handy Teilen-Blatt, sonst Zwischenablage. Gast-Prüfung 26.11. im echten
  Headless-Browser gegen Prod: keine Umleitung zur Anmeldung, keine 401, Mannschaft aus `?team=` und Runde aus `?round=`
  greifen; einziger Fehler war `eval=1` → 400 (behoben 1.023.1). Vorschau im Messenger: `docs/api/link-vorschau.md`.
- Strom im Browser: `EventSource` (Chrome 111 kann es), `LiveBoardsService.stream` — gibt der Browser die Verbindung auf
  (404/429), neuer Versuch nach 3 s / 10 s / 30 s. Seit 1.025.0 mit `ngsw-bypass=1`: im Prod-Build lief der Strom sonst durch den
  Service Worker (ngsw reicht jede nicht gecachte Anfrage per `respondWith` durch), Chrome beendet einen untätigen Worker nach ~30 s —
  im Prod-Log endeten die Browser-Ströme nach 29–60 s, während der Server-Strom > 90 s hielt. Ein Abriss bei schon geladenen
  Brettern wird erst nach 10 s angezeigt (`LIVE_LOST_GRACE_MS`): bis dahin bleiben „Live“ und die Bretter mit dem letzten Stand stehen.

## Tests

`tests/RookHub.Api.Tests/Tournaments/Live/` (`ChessComLiveTests`: Linkformen, echte Antworten aus `Fixtures/ChessCom`, PGN, Takt/Deckel/
Vorrang, Backoff, Router, Auswahl, Verknüpfen; Zerleger in Stücken, Parser %clk/%eval, Zuordnung, Hub-Lebenszyklus, Finder,
Controller-Zugriff), `LichessLiveSmokeTests` liest mit `ROOKHUB_LIVE_LICHESS=1` den echten Bundesliga-Strom ein paar
Sekunden; Frontend `live-boards.*.spec.ts` (EventSource gemockt).
