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
| OG-Weiche | `src/frontend/nginx.conf` (`location ~ ^/(g\|t\|puzzles\|tournaments/calendar\|l\|r\|w\|weekly\|s\|partie)(/\|$)\|^/analysis$`) | teilbare Pfade → `/api/og/render`, Header `X-Original-URI` + `X-Og-Site`; Fehler/429 → normale `index.html` |
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
| `/g/{token}` (+ `/summary`) | geteilte Partie | Titel/Beschreibung (Recap), Bild = Endstellung TEXTFREI (+ Kurve) | Karte: Endstellung mit Zugmarke, Spieler mit Elo, Ergebnis, Ereignis/Datum, Kurve (`game`, `?c=2`) |
| `/g/{token}?ply=n` | Partie auf Halbzug n (geerntete Aufgabe) | wie `/g` (Endstellung) | Stellung nach n Halbzügen, „Position after 12. Nf5" (`gameply`) |
| `/g/{token}?train=white\|black` | Fehler-Training | Trainingskarte | unverändert (`train`) |
| `/g/{token}?roast=id` | Partie + freigegebener Roast | Roast als Beschreibung | unverändert, Bild = Partiekarte |
| `/s/{token}/club-games/{id}` | Vereinspartie über einen Liga-Link | keine | keine (offen, siehe unten) |
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
| `/{slug}`, `/{slug}/{kapitel}` | öffentlicher Kurs (Kurz-URL) | keine | keine (offen: beliebiger Pfad — die Weiche müsste jedes Einzelsegment an die API schicken) |
| `/guess/{id}` | Punktepartie | keine | keine (offen) |
| Kurs-/Repertoire-Freigaben, Puzzle-Challenges | an Personen (angemeldet) | — | keine Vorschau nötig (Ziel = Seite mit Anmeldung bzw. `/puzzles/{id}`) |

### Turnierseite

| Route | vorher | nachher |
|---|---|---|
| `/t/{id}` | Name, Bild = Grundstellung textfrei | unverändert; feste og-Tags der Shell werden jetzt entfernt (vorher standen sie VOR den eingefügten) |
| `/tournaments/calendar/{id}` | Eckdaten, Grundstellung | unverändert (dito) |

### LeagueHub (vorher: `X-Og-Site: none` → keinerlei Vorschau)

| Route | Was die Seite zeigt | nachher (`kind`) |
|---|---|---|
| `/s/{token}` (`?runde=N` → `{token}~{N}`) | geteilte Begegnung: Prognose der gegnerischen Aufstellung bzw. nach der Runde Aufstellung + Ergebnis | Karte: Liga · Runde, Heim – Gast (bzw. mit Ergebnis), Termin, je Brett der wahrscheinlichste Gegner mit Prozent bzw. der, der spielte (`league`) |
| `/s/{token}/hochladen`, `/s/{token}/formular/{key}` | Partien hochladen | keine (bewusst) |
| `/partie/{token}` | eine Ligapartie (0.935.0) | Karte: Endstellung, Spieler/Elo, Ergebnis, Liga · Runde · Brett, Heim – Gast (`lgame`) |
| `/verein/uebersicht` | angemeldet, kein öffentlicher Link | keine |

KidHub (`/stars/{n}/unlock` u. a.) und ClubHub (alles angemeldet) teilen nichts Öffentliches mit Inhalt — bleiben `none`.

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
`LeagueService.PublicShareAsync` (abgelaufen/widerrufen → null), `LeagueGameShares.ReadAsync` (Marke kaputt/abgelaufen/Partie
umgehängt → null; anonymisierte Vereinspartie ohne Brett und Mannschaften, Name = Anonymisierungs-Name). Ungültiger Link →
keine Tags, Bild 404. Puzzles zeigen nie die Lösung; Aufgabenblätter nur die Stellungen (Überschrift, Begleittext und Lösung
liefert der Link mit, sie kommen trotzdem nicht aufs Bild).

## Caching

| Bild | `Cache-Control` | Grund |
|---|---|---|
| Partie, markierte Stellung, Puzzle, Buch-Puzzle, Tagespuzzle mit Datum, Stellung | `public, max-age=604800, immutable` | Inhalt fest je Adresse; neue Kurve → `?v=`, neue Gestaltung → `?c=` (`OgMetaService.CardVersion`) |
| Repertoire-Linie, Ligapartie, Wochenpost, Tagespuzzle `today` | `public, max-age=3600` | Link läuft ab/wird widerrufen bzw. Inhalt wechselt |
| Rekonstruktion, Aufgabenblatt, Liga-Begegnung | `public, max-age=600` | bearbeitbar/neu gerechnet, widerrufbar |

Die angereicherte `index.html` selbst: `no-cache, must-revalidate` aus der nginx-map (Pfade `r|w|s|partie` dort ergänzt).

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
`OgLeagueCardTests` (Pfade je Site, Begegnung offen/gespielt/leer, anonymisierte Partie, abgelaufene Links, Basis-URL,
Escaping, Entfernen der Shell-Tags). nginx-Map und -Location: `DeploymentConfigTests.OgPreview_*`.
Bilder ansehen: `OG_DUMP_DIR=/tmp/og dotnet test --filter "FullyQualifiedName~Og"` schreibt einige PNGs.
