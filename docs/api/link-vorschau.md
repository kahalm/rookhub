# Link-Vorschau (Open Graph / Twitter Card) — Vorschaubilder je geteilter Seite (2026-11-09)

Wird ein RookHub-/LeagueHub-Link in Discord, WhatsApp, Signal, Teams … geteilt, holt der Messenger die Seite und liest
`og:title`, `og:description`, `og:image`. Eine SPA hat diese Tags nicht von selbst — deshalb schickt der Frontend-nginx die
teilbaren Routen an die API (`GET /api/og/render`), die die ECHTE `index.html` des jeweiligen Containers mit seitenbezogenen
Tags anreichert (Menschen bekommen die normale SPA). Das Bild liefert `GET /api/og/img/{kind}/{id}.png` (1200×630, SkiaSharp).

Wunsch des Nutzers (2026-11-09): „überleg dir für alles was öfters geteilt wird (partien, leaguehub, stellungen, puzzles)
passende info-vorschaubilder, glaub da fehlt noch einiges."

## Bausteine

| Teil | Datei | Aufgabe |
|---|---|---|
| OG-Weiche | `src/frontend/nginx.conf` (`location ~ ^/(g\|cg\|t\|puzzles\|tournaments/calendar\|l\|r\|w\|weekly\|s\|partie\|tl\|ga\|explorer/game/masters)(/\|$)\|^/analysis$`) | teilbare Pfade → `/api/og/render`, Header `X-Original-URI` + `X-Og-Site`; Fehler/429 → normale `index.html` |
| Kurs-Weiche | `nginx.conf`: `map $rookhub_og_bot` (Vorschau-Abrufer per User-Agent) + `map $rookhub_og_course` + `location = /__og_course` (intern) | Kurs-Kurz-URLs `/{slug}[/{kapitel}]` haben keinen Präfix: NUR Vorschau-Abrufer auf RookHub gehen an den Renderer, mit `X-Og-Route: course` (seit 2026-11-22) |
| Site-Schlüssel | `map $host $rookhub_og_site` | `""` RookHub · `turnier` Turnierseite · `leaguehub` LeagueHub (seit 2026-11-09) · `none` KidHub/ClubHub (eigene Shell, keine Vorschau, F8-012) |
| Shell | `Services/Og/OgIndexHtmlProvider.cs` | holt je Site die index.html des RICHTIGEN Containers (60 s Cache); LeagueHub: `Frontend:LeagueHubInternalUrl` → `http://leaguehub:8080` → `rookhub-leaguehub` → `rookhub-leaguehub-dev` |
| Pfad → (kind, id) | `OgMetaService.ParsePath(path, site)` | LeagueHub kennt nur `/s/…`, `/partie/…`; RookHub/Turnier nur die übrigen |
| Meta | `OgMetaService*.cs` (je Typgruppe eine Teildatei) | Titel, Beschreibung, Bild-Adresse, `og:site_name` |
| Bild | `OgImageService.RenderCard` (`OgImageService.Cards.cs`) | Karte mit Diagramm(en) + Text; alt: `RenderBoard` (textfrei, Turnier/Kalender, Trainingskarte) |
| Controller | `Controllers/Og/OgController.cs` | Anreichern (`Inject`, entfernt feste og-/twitter-Tags der Shell), Bild + `Cache-Control` je Typ |

Basis-URL für `og:url`/`og:image`: `App:BaseUrl`; Turnierseite `App:TurnierBaseUrl`; LeagueHub `App:LeagueHubBaseUrl`
(jeweils Rückfall auf `App:BaseUrl`) — nie aus dem Host-Header (Cache-Poisoning, siehe `OgController.ResolveBaseUrl`).

## Inventur: öffentliche bzw. per Token geteilte Routen

„vorher" = Stand 0.935.0, „nachher" = mit diesem Paket.

### RookHub

| Route | Was die Seite ohne Anmeldung zeigt | vorher | nachher (`kind`) |
|---|---|---|---|
| `/g/{token}` (+ `/summary`) | geteilte Partie | Titel/Beschreibung (Recap), Bild = Endstellung TEXTFREI (+ Kurve) | Karte: Endstellung mit Zugmarke, Spieler mit Titel (1.009.0, „IM Grötz, Harald“, auch im og:title) und Elo, Ergebnis, Ereignis/Datum, Kurve (`game`, `?c=2`) |
| `/g/{token}?ply=n` | Partie auf Halbzug n (geerntete Aufgabe) | wie `/g` (Endstellung) | Stellung nach n Halbzügen, „Position after 12. Nf5" (`gameply`) |
| `/g/{token}?train=white\|black` | Fehler-Training | Trainingskarte | unverändert (`train`) |
| `/g/{token}?roast=id` | Partie + freigegebener Roast | Roast als Beschreibung | unverändert, Bild = Partiekarte |
| `/cg/{token}` (+ `/summary`) | einzelne geteilte Vereinspartie (2026-11-09) | keine (Weiche kannte `cg` nicht) | seit 2026-11-22 Partiekarte wie `/g` (`cgame`); Name = Anonymisierungs-Name, ohne Event/Elo der ersetzten Seite |
| `/cg/{token}?train=white\|black` | Fehler-Training der Vereinspartie | keine | Trainingskarte wie `/g` (`cgtrain`, `{side}-{token}`) |
| `/s/{token}/club-games/{id}` (+ `/summary`) | Vereinspartie über einen Liga-Link | keine | seit 2026-11-22 Partiekarte wie `/cg/` (`scgame`, Kennung `{token}~{id}`), gelesen über `LeagueShareClubGames` (Link gültig + Partie an einem Brett SEINER Begegnung); anonymisiert nur der Anonymisierungs-Name |
| `/tl/{token}` (+ `/drill`) | Trainerstunden-Eintrag (läuft ab) | keine | seit 2026-11-22 Karte „Training session“/„Training drill“: erste besprochene Stellung, Datum, Umfang, ohne Namen; Beschreibung = Kurzfassung der Stunde (`lesson`/`ldrill`); nur GÜLTIGER Link |
| `/ga/{token}/summary` | anonyme Zusammenfassung einer eigenen Partie | keine | seit 2026-11-22 allgemeine Karte „Game summary“: Endstellung, Ergebnis, Zuglänge — KEIN Spieler, keine Elo, kein Ereignis/Datum (`ganon`, über `AnonGameShares.ReadAsync`) |
| `/explorer/game/masters/{id}` | Meisterpartie aus dem lokalen Explorer | keine | seit 2026-11-22 Partiekarte „Master game“ mit Namen/Elo/Ereignis (`master`; PGN über `LocalExplorerClient.CachedMasterPgnAsync`, derselbe Tages-Zwischenspeicher wie die Seite) |
| `/l/{token}` | Repertoire-Linie/Kapitel (läuft ab) | keine | Karte: Endstellung der Linie, Titel, Repertoire, Zuglänge (`line`) |
| `/r/{token}` | rekonstruierte Partie | keine | Karte: zuletzt bekannte Stellung, Spieler, „n plies known · g gaps" (`recon`) |
| `/w/{token}` | Aufgabenblatt zum Lösen (Link liefert auch Lösungen!) | keine | Raster aus ≤ 6 Mini-Diagrammen, Name, Anzahl — ohne Text/Lösung (`worksheet`) |
| `/puzzles/{id}` | Lichess-Puzzle (auch Puzzle-Challenges verlinken hierher) | Titel „Schachpuzzle #id", Bild = Stellung VOR dem Setup-Zug aus Sicht des GEGNERS | Löser-Sicht nach dem Setup-Zug mit Zugmarke, „White/Black to move", Rating, Themen (`puzzle`) |
| `/puzzles/book/{id}` (`?single=1`) | Buch-/Kurs-Puzzle | Buchtitel, Bild wie oben (falsch bei `startPly ≥ 0`) | Löser-Sicht (`startPly` wie `setupSolver`), Buchtitel, Buch-Rating (`book`) |
| `/puzzles/daily/{yyyyMMdd\|today}` | Tagespuzzle | Bild unter `daily/today.png` eine Woche „immutable" → am nächsten Tag falsch | Adresse trägt das Datum; `today` 1 h (`daily`) |
| `/puzzles/endless`, `/puzzles` | wechselnde Puzzles | keine | keine (keine feste Stellung) |
| `/weekly/{id}` | Wochenpost (öffentlich lesbar) | keine | Karte: erstes Puzzle (Löser-Sicht), Titel, Anzahl, Datum; nur veröffentlichte (`weekly`) |
| `/analysis?fen=…&orientation=black` | Analysebrett mit Stellung (⋮ → „Stellung teilen") | keine | Diagramm + Zugrecht (`position`, Kennung `w-`/`b-` + FEN base64url) |
| `/maxims` | Merksätze | keine | keine (offen: allgemeine Karte) |
| `/ebooks`, `/ebooks/{id}` | Bibliothek (Datei nur mit Freigabe/Ausweis) | keine | keine (offen: Titelbild öffentlicher Bücher) |
| `/{slug}`, `/{slug}/{kapitel}` | öffentlicher Kurs (Kurz-URL) | keine | seit 2026-11-22 Karte „Chess course“/„Course chapter“: erste Aufgabe in Löser-Sicht, Name, Kapitel, Anzahl Stellungen (`course`, Kennung `{slug}` bzw. `{slug}~{kapitel base64url}`). Nur `IsPublic` + `PublicSlug`; die Weiche schickt nur Vorschau-Abrufer (siehe „Kurs-Kurz-URLs“) |
| `/guess/{id}` | Punktepartie | keine | keine (offen) |
| Kurs-/Repertoire-Freigaben, Puzzle-Challenges | an Personen (angemeldet) | — | keine Vorschau nötig (Ziel = Seite mit Anmeldung bzw. `/puzzles/{id}`) |

### Turnierseite

| Route | vorher | nachher |
|---|---|---|
| `/t/{id}` | Name, Bild = Grundstellung textfrei | unverändert; feste og-Tags der Shell werden jetzt entfernt (vorher standen sie VOR den eingefügten) |
| `/tournaments/calendar/{id}` | Eckdaten, Grundstellung | unverändert (dito) |
| `/live/{token}` (geteilte, lebende Ansicht, 1.029.0) | keine | `liveview`: dieselbe Karte wie die lange Adresse, gebaut aus dem AKTUELLEN Stand der Ansicht (`SharedLiveViews`, `OgMetaService.LongPath`), ohne „zuletzt gesehen“ nachzuführen; unbekannte Kennung → keine Vorschau. Nur Turnierseite; Weiche `^/live/[A-Za-z0-9]+$` |
| `/tournaments/{id}/live?round=&team=&player=` | keine (allgemeine Karte „Turniere“) | seit 1.024.0 (`live`): Titel „Live: Heim – Gast · Übertragung, Runde n“ (mit `team`), „Live: Spieler · …“ (mit `player`), sonst „Live-Bretter · Übertragung, Runde n“; Beschreibung deutsch: Stand, Zahl der übertragenen Bretter, Begegnungen, „Runde läuft gerade“/„beendet“/„Beginn …“ (Wiener Zeit). Ohne `round` die laufende Runde. Bild = Turnier-Motiv wie `/t/` (eine Live-Stellung wäre im Messenger gleich veraltet). Gelesen nur verknüpfte Turniere (`TournamentLiveService.GetLinkedStatusAsync`, KEINE Lichess-Suche) und Partien nur, wenn der Server sie gerade im Speicher hat (`BroadcastLiveHub.Peek`: offener Feed oder gemerkte Endstellungen) — ein Vorschau-Abrufer öffnet nie einen Strom. Seit 1.025.0 nennt der Titel hinzugefügte Bretter („… + 3 weitere Bretter“ aus `add=`/`ext=`, nur gezählt, ohne Lichess-Abruf). Nur auf der Turnierseite (`X-Og-Site: turnier`); Weiche `^/tournaments/[A-Za-z0-9]+/live$` |

### LeagueHub (vorher: `X-Og-Site: none` → keinerlei Vorschau)

| Route | Was die Seite zeigt | nachher (`kind`) |
|---|---|---|
| `/s/{token}` (`?runde=N` → `{token}~{N}`) | geteilte Begegnung: Prognose der gegnerischen Aufstellung bzw. nach der Runde Aufstellung + Ergebnis | Karte: Liga · Runde, Heim – Gast (bzw. mit Ergebnis), Termin, je Brett der wahrscheinlichste Gegner mit Prozent bzw. der, der spielte (`league`) |
| `/s/{token}/hochladen`, `/s/{token}/formular/{key}` | Partien hochladen | keine (bewusst) |
| `/partie/{token}` | eine Ligapartie (0.935.0) | Karte: Endstellung, Spieler/Elo, Ergebnis, Liga · Runde · Brett, Heim – Gast (`lgame`) |
| `/verein/uebersicht` | angemeldet, kein öffentlicher Link | keine |

KidHub (`/stars/{n}/unlock` u. a.) und ClubHub (alles angemeldet) teilen nichts Öffentliches mit Inhalt — bleiben `none`.

## Roter Faden als Beschreibung (1.021.0)

Wunsch 2026-11-25: der rote Faden der Partie-Zusammenfassung „in der Vorschau vom Link“. Ausgezogen an EINER Stelle
(`SummaryRedThread.Extract`, `docs/api/partie-zusammenfassung.md` „Roter Faden ganz oben“), als Text ohne Markdown, auf übliche Länge
gekürzt (`SummaryRedThread.Plain`, ≤ 200 Zeichen am Wortende mit „…“; `OgMetaService.Summaries.cs`). Titel und Bild bleiben wie bisher.
Gelesen wird immer über denselben Dienst wie die Seite des Links — also mit dessen Filter. Ohne Zusammenfassung oder ohne roten Faden
bleibt die bisherige Beschreibung.

| Route | roter Faden als `og:description` | Grund |
|---|---|---|
| `/cg/{token}/summary`, `/cg/{token}` | ja (nicht bei `?train=`) | der Link liest den Text ohnehin; `ClubGameLinks.SummaryAsync` filtert anonymisierte Partien (interner Name/FIDE-ID irgendwo im Text → kein Text, „Runde/Brett N“ entfernt) |
| `/s/{token}/club-games/{id}/summary`, `/s/{token}/club-games/{id}` | ja | die Zusammenfassung hängt am selben Liga-Link (⋮ „Zusammenfassung“); bei einer anonymisierten Partie geht der Absatz zusätzlich durch `ClubGameLinks.SafeSummary` (`LeagueShareClubGames.RedThreadAsync`) — die Vorschau verrät nicht mehr als Karte und Titel |
| `/g/{token}/summary` | ja | `OwnGameSummaries.GetSharedAsync`, derselbe Text wie die Seite |
| `/g/{token}` | nur ohne „Kurz erzählt“ (die Nacherzählung bleibt vorn); `?roast=` und `?ply=` wie bisher | dito |
| `/ga/{token}/summary` | ja | `AnonGameShares.SummaryAsync`: Namen ersetzt bzw. Text gesperrt — kein Name in der Vorschau |
| `/club-games/{id}/summary`, `/games/{id}/summary` (angemeldet) | nein, keine Vorschau | nur mit Konto lesbar; die OG-Weiche kennt diese Pfade nicht, ein Abrufer bekommt die allgemeine Shell — nichts Privates nach außen |
| LeagueHub `/partie/{token}` (`lgame`) | nein | die Seite des Links zeigt keine Zusammenfassung; die Vorschau zeigt nur, was die Route ohne Anmeldung zeigt („Rechte“) — und bei anonymisierten Partien verbirgt der Link Brett und Mannschaften, ein freier Text könnte mehr sagen |
| LeagueHub `/s/{token}` (Begegnung) | nein | mehrere Partien, kein einzelner roter Faden |

Tests: `OgRedThreadTests` (je Route, Anonymisierung mit Klarname im Text, Kürzen, Trainingslink unverändert).

## Kurs-Kurz-URLs: warum nur Vorschau-Abrufer (2026-11-22)

`/{slug}` und `/{slug}/{kapitel}` sind die letzten Routen der SPA vor `**`: JEDES unbekannte Einzelsegment ist ein Kandidat
(`/mate1` genauso wie `/dashboard`). Alle Seitenaufrufe von Menschen über die API zu schicken, hieße einen zusätzlichen Sprung für jede
Seite der App — deshalb markiert der nginx nur Abrufe mit einem bekannten Vorschau-User-Agent (`map $http_user_agent $rookhub_og_bot`:
Discord, WhatsApp — auch Signal ruft so ab —, Telegram, Slack, X/Twitter, Facebook, LinkedIn, Teams/Skype, Mattermost, Mastodon,
Bluesky …) auf RookHub (`X-Og-Site` leer), deren Pfad wie ein Kurzname aussieht (`[a-z0-9-]{1,40}`, optional ein Kapitel ohne Punkt —
Dateien bleiben Dateien). `location /` schreibt sie auf die interne `location = /__og_course` um, die wie die OG-Weiche an
`/api/og/render` geht, zusätzlich mit `X-Og-Route: course`. Die API löst dann NUR `OgMetaService.ParseCoursePath` auf und schlägt den
Kurs nach (`IsPublic` + `PublicSlug`, Kapitel getrimmt ohne Groß/klein); unbekannt → die unveränderte Shell. Ein fehlender Abrufer
bekommt nur keine Kurs-Vorschau. Menschen kommen nie dort an.

## Bots werden nie umgeleitet

Die Umleitungen „Teilen-Link → volle Seite“ (`shareRedirectGuard`, LeagueHubs `partieRedirectGuard`) laufen im BROWSER als Angular-Guard
und nur für Angemeldete. Der Vorschau-Abrufer bekommt vom nginx bzw. `/api/og/render` immer 200 mit der Shell samt Tags — keine
3xx-Antwort auf diesem Weg (`DeploymentConfigTests.OgPreview_CourseSlugs_*` prüft die Kurs-Location darauf).

## Gestaltung der Karten (1200×630, lesbar bei 600 px Breite)

- **RookHub**: Hintergrund `#1f1d2b`, Rahmen `#3a3750`, Text weiß/`#c9c5e0`, Akzent = Primärfarbe des dunklen Themas
  `#abc7ff` (DESIGN 3.1). Wortmarke „RookHub" unten rechts. Brettfarben „brown" (`#f0d9b5`/`#b58863`), cburnett-Figuren,
  Zugmarke wie die App (gelbgrün halbtransparent).
- **LeagueHub**: Farben des dunklen LeagueHub-Themas (DESIGN 8.4: `--paper #0f141b`, `--surface #161d26`, `--ink #e6ebf1`,
  `--muted #97a5b5`, `--red #ff4d63`), roter Balken links wie `.cta`, Wortmarke „LeagueHub".
- Schrift: eingebettete DejaVu Sans (Bold/Regular) — Roboto/Barlow liegen nur als woff2 im Frontend; der Server braucht TTF.
- Aufbau (`OgCard`): Diagramm links (560 px), rechts Kicker (Art der Seite, Akzent, 28 px) → Titel (≤ 2 Zeilen, 50 px) →
  Spieler mit Farbscheibe (38 px) → Ergebnis (42 px, `½–½`) → „White/Black to move"-Pille → Zeilen (28 px) → Kurve.
  Kleinste Schrift 18 px (= 9 px bei halber Breite nur im Notfall beim Schrumpfen), sonst ≥ 24 px. Lange Texte schrumpfen,
  dann „…". Fehlt jedes Feld, entsteht trotzdem ein Bild (leere Markenfläche statt 500).
- Sprache: RookHub-Karten **englisch** (wie die Trainingskarte, Wunsch 2026-10-21 „mach das default englisch": die Vorschau
  sieht jeder, gleich welche Sprache); die neuen Titel/Beschreibungen ebenfalls. Bestehende deutsche Beschreibung der
  Partie (`… · Partie auf RookHub nachspielen`) unverändert. LeagueHub deutsch (Seite ist deutsch).

## Rechte

Nur Daten, die die Route OHNE Anmeldung ohnehin zeigt — gelesen über dieselben Dienste wie die Seite:
`SavedGameService.GetSharedAsync`, `SharedLineService.GetByTokenAsync` (abgelaufen → null), `GameReconstructionService.GetSharedAsync`,
`WorksheetService.GetSharedAsync` (Teilen aus → null), `WeeklyPostService` nur `ScheduledAt ≤ jetzt`,
`LeagueService.PublicShareAsync` (abgelaufen/widerrufen → null), `LeagueShareClubGames.ReadAsync` (`/s/…/club-games/{id}`: Link gültig und Partie an
einem Brett seiner Begegnung, sonst null; Projektion wie `/cg/`), `TrainingLessonShares.ResolveAsync` + `TrainingVideoService.GetForGuestAsync`
(`/tl/`: nur gültig — abgelaufen/widerrufen → null; ohne Partie- und Kontonamen auf der Karte), `AnonGameShares.ReadAsync` (`/ga/`: keine Namen), `ClubGameLinks.ReadAsync`/`EvalsAsync` (`/cg/`: widerrufen/archiviert → null;
anonymisiert: Anonymisierungs-Name, neu gebautes PGN ohne Event, ohne Elo der ersetzten Seite — `WhiteRealName`/`BlackRealName` nie), `LeagueGameShares.ReadAsync` (Marke kaputt/abgelaufen/Partie
umgehängt → null; anonymisierte Vereinspartie ohne Brett und Mannschaften, Name = Anonymisierungs-Name). Ungültiger Link →
keine Tags, Bild 404. Puzzles zeigen nie die Lösung; Aufgabenblätter nur die Stellungen (Überschrift, Begleittext und Lösung
liefert der Link mit, sie kommen trotzdem nicht aufs Bild).

## Caching

| Bild | `Cache-Control` | Grund |
|---|---|---|
| Partie, markierte Stellung, Puzzle, Buch-Puzzle, Tagespuzzle mit Datum, Stellung | `public, max-age=604800, immutable` | Inhalt fest je Adresse; neue Kurve → `?v=`, neue Gestaltung → `?c=` (`OgMetaService.CardVersion`) |
| Repertoire-Linie, Ligapartie, Vereinspartie `/cg/` (Partie- und Trainingskarte), anonyme Zusammenfassung `/ga/`, öffentlicher Kurs, Wochenpost, Tagespuzzle `today` | `public, max-age=3600` | Link läuft ab/wird widerrufen bzw. Inhalt wechselt |
| Rekonstruktion, Aufgabenblatt, Liga-Begegnung, Vereinspartie über den Liga-Link, Trainerstunde `/tl/` | `public, max-age=600` | bearbeitbar/neu gerechnet, widerrufbar |
| Meisterpartie | `public, max-age=604800, immutable` | veröffentlichte Partie, fest je Kennung |

Die angereicherte `index.html` selbst: `no-cache, must-revalidate` aus der nginx-map (Pfade `r|w|s|partie`, seit 2026-11-22 `cg`, `tl`, `ga`, `explorer` und `/__og_course` dort ergänzt).

## Deploy

- **Reihenfolge: zuerst API, dann Frontend-Images.** Die neue nginx-Weiche schickt `/l`, `/r`, `/w`, `/weekly`, `/analysis`,
  `/s`, `/partie` an `/api/og/render`; eine alte API kennt `X-Og-Site: leaguehub` nicht und lieferte dann RookHubs Shell auf
  LeagueHub (weiße Seite wie F8-012). Umgekehrt (neue API, alter nginx) passiert nichts: die neuen Routen kommen nur nicht an.
- LeagueHub-Container muss für die API unter `leaguehub`/`rookhub-leaguehub`(`-dev`) erreichbar sein (gleiches Compose-Netz)
  oder `Frontend__LeagueHubInternalUrl` setzen; `App__LeagueHubBaseUrl` (`LEAGUEHUB_BASE_URL`) steht schon in den Compose-Dateien.
- Prüfen: `curl -s -H 'User-Agent: Discordbot' https://leaguehub…/s/<token> | grep og:` bzw. den Link in einen Messenger.

## Tests

`tests/RookHub.Api.Tests/Og/`: `OgTests` (Bestand), `OgCardTests` (Rendering 1200×630, leere/kaputte Daten, Partie/Stellung
nach ply, Linie/Rekonstruktion inkl. abgelaufen/unbekannt, Cache-Header), `OgPuzzleCardTests` (Löser-Sicht, Themen,
`/analysis?fen=`, Wochenpost nur veröffentlicht), `OgWorksheetCardTests` (Raster, kein Text/Lösung, Widerruf),
`OgRedThreadTests` (roter Faden als Beschreibung), `OgClubGameTests` (`/cg/` Partie/Training, Anonymisierung: kein Klarname/FIDE/Event, Widerruf), `OgShareLinkTests` (`/tl/` + drill gültig/abgelaufen/widerrufen,
`/s/…/club-games/{id}` anonymisiert/abgelaufen/fremde Partie, `/ga/` ohne Namen + Widerruf, Meisterpartie, Kurs nur mit Markierung + nicht öffentlich), `OgLiveBoardsTests` (Live-Bretter: Titel je team/player/ohne, Stand nur aus dem Speicher, keine Suche/kein Strom, Escaping), `OgLeagueCardTests` (Pfade je Site, Begegnung offen/gespielt/leer, anonymisierte Partie, abgelaufene Links, Basis-URL,
Escaping, Entfernen der Shell-Tags). nginx-Map und -Location: `DeploymentConfigTests.OgPreview_*` (Kurs-Weiche: `OgPreview_CourseSlugs_OnlyForPreviewBotsOnRookHub`).
Bilder ansehen: `OG_DUMP_DIR=/tmp/og dotnet test --filter "FullyQualifiedName~Og"` schreibt einige PNGs.
