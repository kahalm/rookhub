# Zwei Oberflächen: RookHub + Turnierseite

> Ausgelagert aus `CLAUDE.md` am 2026-10-11 (Abschnitt „Zwei Oberflächen: RookHub + Turnierseite“). Neue Doku zu diesem Bereich gehört HIERHER, nicht in die CLAUDE.md.


Turniere laufen seit v0.409.0 als **eigene Seite** unter `turnier.oberschmid.homes`
(Dev: `turnier-dev.oberschmid.homes`) — eigene PWA, eigenes App-Symbol, eigenes Image
`ghcr.io/kahalm/rookhub-turnier:{dev,latest}`. Geteilt wird alles darunter: **eine** API,
**eine** Datenbank, **ein** Konto.

- **Ein Quellbaum, zwei Angular-Projekte** (`app` und `turnier` in derselben `angular.json`).
  Die Turnierseite importiert Auth, i18n und die kleinen Bausteine per Alias `@rh/*` aus
  `src/app/` — keine Kopie, eine Änderung wirkt auf beiden Seiten.
- **Ein Dockerfile, zwei Images**: `APP_PROJECT` entscheidet, welches Bundle in den nginx kommt.
  Die CI baut beide aus demselben Kontext (`build-frontend`, `build-turnier`).
- **Anmeldung**: das JWT liegt in `localStorage` und ist damit an die Herkunft gebunden — eine
  Domain kann die andere nicht mitlesen. Zwei Wege überbrücken das, beide in
  `core/handoff.service.ts`:
  1. **Sprung über das Menü** — nimmt einen Einmal-Code mit (`POST /api/auth/handoff` →
     `?h=<code>` → `POST /api/auth/rh-session/handoff` (der alte Pfad `handoff/exchange` ist seit 0.828.3 entfernt),
     60 s gültig, genau einmal einlösbar). Bei eingerichteter Elterndomäne nur mit dem geteilten Cookie DESSELBEN
     Kontos (`POST /api/auth/handoff` legt es an) — sonst Login-CSRF über fremde Codes (Codereview F1-008).
  2. **Geteilte Anmeldung** (seit v0.413.0, `Services/Accounts/SharedSessionService.cs`) — für den
     Normalfall „ich rufe die andere Seite direkt auf". Beim Anmelden legt die API ein Cookie auf
     der gemeinsamen Elterndomäne ab (`Auth:SharedSessionDomain`, z. B. `.oberschmid.homes`;
     Name `Auth:SharedSessionCookie`, auf Dev ein ANDERER als in Prod — sonst überschreiben sich
     die beiden Umgebungen gegenseitig auf derselben Domäne). Beide Oberflächen tauschen es beim
     Start über `POST /api/auth/rh-session` gegen ihre eigene Anmeldung; `logout()` beendet es über
     `POST /api/auth/rh-session/end`. Das Cookie trägt ein Token mit EIGENEM Adressaten
     (`rookhub-shared-session`) — der JWT-Handler der API weist es ab, es öffnet also nur diesen
     einen Endpunkt; dazu `HttpOnly`, `SameSite=Lax`, `Path=/api/auth/rh-session` (bis N6-001
     `/api/auth` — das bedienen auch RCT, Lernkompass, Cal.com und die Dev-Stacks unter derselben
     Elterndomäne; die alten Routen `session`/`session/end` sind seit 0.828.1 entfernt). **Leere Domäne = aus**
     (localhost/IP haben keine gemeinsame Domäne). Eine schon offene Seite der Gegenrichtung
     merkt eine Abmeldung erst beim nächsten Laden — sie hält ihr eigenes JWT.
- **Link-Vorschau**: `/t/{id}` lebt jetzt auf der Turnierseite. Der nginx sagt der API über
  `X-Og-Site` (aus `$host` abgeleitet), welche SPA-Shell sie anreichern soll und welche Domain in
  `og:url` gehört (`App:TurnierBaseUrl`) — sonst bekäme der Besucher die RookHub-Shell serviert
  und landete auf dem Dashboard. KidHub-, LeagueHub- und ClubHub-Hosts setzen `X-Og-Site` auf `none`: der nginx
  liefert dort die EIGENE `index.html` (keine Vorschau); OG-Antworten tragen nur noch das Cache-Control der nginx-map,
  das der API wird verborgen (Codereview F8-012).
- **Ohne Konto benutzbar** (0.643.0, Wunsch „vor allem Turniersuche und Turnier-Detailseiten; Speichern von Filtern und
  Turnieren braucht natuerlich einen Account"): `tournaments/calendar`, `tournaments/calendar/:id` und `tournaments/:id`
  tragen keinen `authGuard` mehr; `/tournaments` (Gemerkt), `/tournaments/history`, `/profile` und `/admin` schon. Jede
  speichernde Aktion (Suchprofil, Merken, Beobachten, Ausblenden, Melden, Vereine nachtragen) geht durch
  `src-turnier/app/core/require-account.ts` (`requireAccount`: angemeldet → weiter, sonst Anmeldung mit `returnUrl`);
  „Ausblenden" und der Schalter „ausgeblendete zeigen" erscheinen fuer Gaeste gar nicht. Gaeste behalten Filter im
  localStorage `rh.turnier.directoryView.guest` (kein Server-Abgleich) und Sterne/„Nur Favoriten" unter denselben
  Schluesseln wie die Teilen-Ansicht `/t/:id` (`public_fav_players_{id}`, `public_fav_teams_{id}`,
  `public_fav_filter_{id}`). Weil `HandoffService.consumeIncoming()` eine geteilte Anmeldung ERST NACH dem Start
  uebernimmt, laedt die Seite danach neu (`AppComponent.afterAdoption`) — sonst stuende sie als Gast da.
  Specs der betroffenen Komponenten stellen `isLoggedIn` ausdruecklich per `spyOnProperty` (Getter) ein.
- **Netz**: der Turnier-Container muss im selben Compose-Netz liegen wie die API, weil sein nginx
  `/api/` an den Servicenamen `api` weiterreicht.
- **Kachel-Proxy `/tiles/`** (Codereview 2026-09-29, F8-002): nur auf der Turnierseite (rookhub*/kidhub*/leaguehub*-Hosts
  → 404; unbekannte Hosts wie IP/localhost/E2E bleiben offen), Zoom 0–18, x/y höchstens sechsstellig; gedrosselt
  je Betrachter (20 r/s, burst 200, Schlüssel `X-Real-IP`) und je Container (50 r/s), Überlauf 429. Verlässt sich
  darauf, dass der NPM `X-Real-IP` setzt (Standard-`proxy.conf`); `DeploymentConfigTests` hält es fest.
- **Eigene Symbole** (`public-turnier/`, seit 0.433.0): Vorlagen in `design/Designer{,2,3}.png`,
  alles darunter ist ABGELEITET (Rezept in `public-turnier/ASSETS.md`). Die Rollenteilung ist
  keine Geschmacksfrage: `Designer.png` fuellt seinen Rahmen (aeusserste Ecke bei 94 % des
  Radius) und taugt fuer `any`, `Designer2.png` haelt das Motiv bei 70 % und taugt damit fuer
  `maskable` (Android darf ab 80 % beschneiden), `Designer3.png` ist das 1200×630-Vorschaubild.
  **Die Falle, die hier lange unbemerkt lief**: `public-turnier/` wird UEBER `public/` gelegt —
  eine dort FEHLENDE Datei faellt still auf RookHubs Fassung zurueck, ohne 404 und ohne
  Warnung. Das Manifest verwies auf Symbole, die es nie gab, und die Turnierseite trug deshalb
  RookHubs Logo. `TurnierAssetTests` haelt jeden Verweis gegen die Dateien auf der Platte und
  prueft zusaetzlich, dass kein `icon.svg` verwiesen wird (es gibt keine Vektorfassung).
- **Turnierverlauf** (`src-turnier/app/features/tournament-history/`, Route
  `/tournaments/history`, Navbar): gespielte und kommende Turniere mit Platz, Punkten,
  Performance-Rating und Elo-Aenderung, umschaltbar auf Freunde (alle oder einzeln).
  **Ausgewertet wird JE BEDENKZEIT-KLASSE** (Turnier/Schnell/Blitz/ohne Angabe) und **je JAHR**,
  mit Turnier- UND Partienzahl: eine Gesamtpunktzahl addierte Blitz zu Turnierschach und
  Fuenfrundige zu Elfrundigen — sie wuchs nur mit der Zeit. Eine Performance ist ausserdem NUR
  getrennt lesbar; 1900 im Blitz ist nicht 1900 im Turnierschach. **`unknown` ist eine eigene
  Gruppe** und faellt nicht heraus: die Bedenkzeit steht auf einer eigenen Seite, die erst der
  naechtliche Durchgang holt — ohne diese Gruppe war die Uebersicht direkt nach dem Deploy leer.
  **Ein REITER je Konto UND je verfolgtem Spieler** (ich zuerst, dann die Freunde, dann die
  Verfolgten) statt einer Auswahlliste; jeder Reiter
  laedt genau EIN Konto (alle auf einmal hiesse: eine chess-results-Abfrage je Freund, auch fuer
  die, die niemand ansieht), einmal geladene bleiben im Zwischenspeicher. Freunde ohne Namen im
  Profil behalten ihren Reiter — gesperrt und mit Grund, statt kommentarlos zu fehlen. Der EIGENE
  Verlauf bleibt bei jeder Auswahl dabei — „nur Freunde" waere eine Ansicht, in der man sich
  selbst sucht, und der Vergleich ist der Zweck. Zwei Dinge, die dabei nicht kippen duerfen:
  (1) Die Seite fragt NACH, solange `pending > 0` (Server holt die Ergebnisse einzeln im
  Hintergrund) — mit Deckel (`MaxPolls` 15), sonst laeuft sie endlos; ohne das Nachfragen saehe
  man eine halbe Tabelle und hielte sie fuer endgueltig. (2) Ist „alle Freunde" die GEMERKTE
  Auswahl, laedt der Verlauf erst NACH der Freundesliste — er muss wissen, wen er meint, sonst
  ist die gemerkte Auswahl beim Wiederkommen wirkungslos (genau so aufgefallen).
  **Ein Klick fuehrt auf das TURNIER, nicht ins Verzeichnis** (0.430.2). Der Kalendereintrag war
  fuer die Mehrheit der Verlaufs-Eintraege eine Sackgasse („steht (noch) nicht im Verzeichnis"),
  und das heilt nicht: der Sweep liest nur `[heute − 30 Tage, heute + 18 Monate]` — ein 2024
  gespieltes Turnier steht dort NIE. Ist es geholt → `/tournaments/{id}`; ist es das nicht →
  Holen-Auftrag einreihen und nachfragen (`MaxImportPolls` 30 × 4 s ≈ zwei Minuten, danach eine
  Meldung statt endlosen Wartens).
  **Das „+" verfolgt beliebige Personen** (0.463.0, `track-player-dialog.component.ts`): Vor- und
  Nachname eingeben, suchen (`GET /api/profile/player-search`), einen Treffer waehlen — er wird
  zum eigenen Reiter. Drei Dinge dabei: (1) Der Reiter-SCHLUESSEL ist zusammengesetzt (`u:12`
  fuer ein Konto, `t:3` fuer einen Verfolgt-Eintrag) — die beiden Kennungen kommen aus
  verschiedenen Toepfen und kollidieren zwangslaeufig; mit der Zahl allein zeigten zwei Reiter auf
  denselben Zwischenspeicher. Ein alter gemerkter Reiter (`{ userId }` im localStorage) wird
  weiter gelesen. (2) Der gemerkte Reiter wird erst verworfen, wenn BEIDE Listen (Freunde und
  Verfolgte) da sind — die Antworten kommen in beliebiger Reihenfolge, und wer nach der ersten
  urteilt, wirft einen Reiter weg, den die zweite gerade mitbringt. (3) Angelegt wird IM Dialog,
  nicht beim Aufrufer: scheitert es (Deckel erreicht, Netz weg), bleibt er offen und sagt es —
  statt dass sich ein Fenster schliesst und danach sichtbar nichts passiert. „Nicht mehr
  verfolgen" hat ein Rueckgaengig, deshalb traegt `TrackedPlayerDto` die Suchfelder mit.
- **Geteilte Anzeige-Einstellungen** (`core/shared-preference.ts`): Design-Modus UND **Sprache**
  liegen in einem Cookie auf der gemeinsamen Elterndomaene — zwei Origins teilen den
  `localStorage` nicht, eine Sprachwahl auf der einen Seite liess die andere sonst in Englisch
  sitzen. Der Mechanismus (lesen/schreiben/`visibilitychange`) steht dort EINMAL; vorher trug der
  Design-Modus seine eigene Kopie. Ohne gemeinsame Elterndomaene (localhost, IP) passiert nichts —
  der geraetelokale Wert traegt dann weiter.
- **Das Identitaets-Formular ist GETEILT** (`shared/profile-identity-form/`): Name, Anzeigename,
  E-Mail und die zwei Spielerkennungen samt SPIELERSUCHE, benutzt von RookHubs Profilseite und der
  der Turnierseite. Vorher zweimal getippt — und darum fehlte der Turnierseite die Suche, obwohl
  dort alles an den Kennungen haengt. Die Komponente aendert das uebergebene Objekt direkt und
  speichert NICHT: was gespeichert wird, entscheidet die Seite (RookHub schickt chess.com/Lichess
  mit, die Turnierseite bewusst nicht). `ngModelOptions: standalone`, damit sie sich nicht im
  `<form>` der Elternkomponente registriert.
- **Profilseite**: die Turnierseite hat ihre EIGENE (`src-turnier/app/features/profile/`, Route
  `/profile`, im Konto-Menue) — Vor-/Nachname, Anzeigename, E-Mail und die beiden
  Spielerkennungen, ueber denselben `PUT /api/profile`. Bewusst nicht RookHubs Profilseite
  wiederverwendet: die ist eine Sammlung aus Chessable-Zugang, Engine-Token, API-Tokens,
  Brett-Einstellungen und Offline-Speicher. Und bewusst nicht weggelassen: der **Nachname** ist
  die Voraussetzung fuer den Turnierverlauf (die chess-results-Spielersuche sucht ueber den
  Namen), die Kennungen entscheiden bei Namensgleichheit. Gespeichert wird nur dieser Teil —
  ein vollstaendiges Profil-Objekt zurueckzuschicken ueberschriebe die RookHub-Einstellungen mit
  dem Stand einer Seite, die sie nicht kennt.

## Dritte Oberfläche: KidHub, die Kinderseite (0.554.0)

`kidhub.oberschmid.homes` (Dev: `kidhub-dev.oberschmid.homes`) — eigenes Angular-Projekt `kidhub`
(`src-kidhub/`, `public-kidhub/`, `tsconfig.kidhub.json`, `ngsw-config.kidhub.json`), eigenes Image
`ghcr.io/kahalm/rookhub-kidhub:{dev,latest}` aus demselben Dockerfile (`APP_PROJECT=kidhub`), Host-Port Prod
**8096** / Dev **8097** (`KIDHUB_PORT`). Name vom Nutzer („KidHub", 2026-09-27). API und Endpunkte: „KidHub —
Kinderseite" unter REST API.

- **Spielen ohne Konto**: kein `visitorInterceptor`; Fortschritt nur im
  localStorage (`rh-kids-progress-v1`, `KidsProgressStore`): Sterne je Stufe (0–1 Fehler = 3, 2–4 = 2, sonst 1 —
  Tipps zählen als Fehler), der laufende Durchgang (Aufgabe + Fehler), gelöste Kurs-Linien. Stufe n ist offen,
  sobald n−1 geschafft ist.
- **Anmelden/Registrieren** (0.561.0, Wunsch 2026-09-27 „auf der ersten Seite rechts oben"): NUR auf der Startseite
  rechts oben (`isHomeUrl`, mitten in einer Stufe lenkt ein Konto-Knopf ab) — abgemeldet „Anmelden" + „Registrieren",
  angemeldet der Name + „Abmelden" (führt zurück auf `/`, nicht auf die Maske). Die Masken sind RookHubs eigene
  (`/login`, `/register`, `/forgot-password`, `/reset-password` über `@rh/features/auth`, `guestGuard`), mit
  `authInterceptor`; beim Start tauscht `HandoffService.consumeIncoming()` das geteilte Cookie gegen eine eigene
  Anmeldung — wer in RookHub oder auf der Turnierseite angemeldet ist, ist es hier auch. Seit 0.563.0 liegt der
  Fortschritt angemeldet im Konto (Abschnitt „Fortschritt im Konto" unter REST API); die Startseite sagt, wo er liegt.
- **Kein Impressum** (0.563.0, Wunsch 2026-09-27): keine Route, kein Link im Fuß. `LEGAL_SITE`
  (`src/app/core/legal-site.ts`) sagt den geteilten Rechtsseiten je Oberfläche, ob es ein Impressum gibt und
  welche Adresse für Datenschutzfragen gilt — KidHub setzt `{ contactEmail: 'kidhub@oberschm.id', imprint: false }` in
  `kidhubConfig`; die Datenschutzerklärung nennt den Verantwortlichen dann über diese Adresse statt übers Impressum,
  die Anmeldemaske lässt den Impressum-Link weg, und auch die Seite zur Konto-Löschung (`/account-deletion`, von der
  Datenschutzerklärung verlinkt, deshalb auch in KidHub eine Route) nennt diese Adresse. RookHub und die Turnierseite
  nehmen die Vorgabe (`OPERATOR`). `LEGAL_SITE` trägt seit dem Codereview (F7-003) auch `kind` (`kidhub`: Fassung in
  einfacher Sprache mit Elternhinweis; `leaguehub`: Abschnitt über Ligaspieler ohne Konto) und `back` (Rücklink,
  KidHub `/` statt `/login`). `accountHome: 'rookhub'` (UX-023; KidHub, LeagueHub, ClubHub, Turnierseite): dort gibt es
  keine Karte „Konto löschen" — `/account-deletion` sagt „dein Konto hier ist ein RookHub-Konto" und verlinkt RookHubs
  `/profile?section=delete` (`accountHomeUrl()` in `partner-site.ts`, angemeldet per Einmal-Code
  `HandoffService.jumpToAccountHome`). Ohne `accountHome` (RookHub) führt der Knopf „Konto jetzt löschen" direkt dorthin;
  `?section=delete` klappt im Profil die Karte auf und scrollt zu ihr. Die Liste „Was entfernt wird" (`removedKeys` in
  `account-deletion.component.ts`) und `profile.delete.warn` geben wieder, was `ProfileService.DeleteAccountAsync`
  löscht (UX-021: eigene Kurse samt Freigaben und fremdem Fortschritt, Partien mit Formular-Fotos, Aufgabenblätter und
  Teilen-Links, KidHub-Fortschritt, Verbindungen) — wer dort etwas ergänzt oder herausnimmt, zieht die Texte nach
  (en/de/hr/hu). LeagueHub (`kind`) nennt zusätzlich Entwürfe, bleibende Vereinspartien und Teilen-Links. Wege zu den
  Rechtsseiten (UX-017): die gemeinsame Fußzeile (`app-footer`, RookHub und Turnierseite) zeigt „Impressum · Datenschutz",
  wo die App die Route hat (Impressum nur mit `imprint`), das ☰-Menü (Gast und „Konto") dieselben zwei Einträge — am
  Handy ist RookHubs Fußzeile aus. Der Rücklink (`legalBackLink`) heißt „Zurück" und geht einen Schritt zurück, wenn man
  aus der App kam (`Router.lastSuccessfulNavigation()`), sonst zum Ersatzziel `back` (Vorgabe `/login`). Ohne Impressum nennt die Datenschutzerklärung beim Verantwortlichen NUR die
  Kontaktadresse der Oberfläche. **Rechtsseiten allgemein** (Betreiber-Entscheidung 2026-09-30, UX-001):
  `environments/operator.ts` (`OPERATOR`) enthält nur noch die Kontaktadresse `rookhub@oberschm.id` — kein
  Diensteanbieter-Block mit Name/Anschrift im Impressum, keine Platzhalter.
- **Geteilt über `@rh/*`**: HTTP-Kette (connectivity, retry, renderAfterHttp), Sprachdateien (Namespace `kids.*`,
  gepflegt in en/de/hr/hu — nur diese vier bietet die Seite an), `PuzzleBoardComponent` (neues Input `autoQueen`:
  Umwandlung ohne Auswahl zur Dame), Datenschutz als eigene Route.
- **Endlos-Modus** (0.566.0, Wunsch 2026-09-27 „analog wie bei RookHub, nur mit sehr flacher Kurve … adaptiv"):
  Route `/endless`, Kachel auf der Startseite. Die KURVE rechnet der Browser (`core/kids-endless.ts`, rein): Start 700,
  +20 je Puzzle (5 Puzzles je 100 Elo), Anker wie RookHub — T1 nach 10 Puzzles = Ø Rating des ersten Fehlers der
  letzten 10 Läufe, T2 nach 25 = Ø Höchst-Rating (sauber gelöst) der letzten 5, danach wieder +20; NIE flacher als die
  Grundkurve (700 → 900 → 1200), keine steile Erst-Lauf-Kurve. Wer weit kommt, bekommt also steilere Läufe. Drei Herzen;
  ein Fehler kostet eins, ein Tipp erst ab dem ZWEITEN in derselben Aufgabe (`ENDLESS_FREE_HINTS` = 1, Wunsch 2026-09-27 —
  der erste lässt nur die Figur leuchten), höchstens eins je Aufgabe, und das Kind löst die Aufgabe trotzdem zu Ende
  (dafür meldet `KidsPuzzleComponent` jeden falschen Zug sofort per `mistake` und jeden Tipp per `hinted` mit seiner Zahl
  in der Aufgabe; für die Sterne der Stufen zählt weiter jeder Tipp; den Preis des nächsten Tipps bekommt sie als Eingabe
  `hintCost` vom Modus — Endlos `free`/`heart`, Stufen `star`, Kurse keiner, Codereview UX-061). Das letzte Herz beendet den Lauf nach
  `WRONG_HOLD_MS`. Läufe + Rekord liegen NUR im Browser (`KidsEndlessStore`, `rh-kids-endless-v1`, letzte 20) — nicht im
  Konto. Die PUZZLES holt `KidsEndlessService` in Blöcken zu 20 (Nachladen bei < 5): je Fenster Zufalls-Sprung in den
  Id-Raum, die nächsten 12 des Fensters, das erste kindgerechte (≤ 3 eigene Züge, Qualitätsgrenzen der Leiter, ohne
  Rochade/en passant/Unterverwandlung; Figurenzahl frei), drei Sprünge, dann zufällig unter den ersten 200 des Fensters.
  Auf Dev gemessen (5,36 Mio. Puzzles): ~10 ms je Sprung (Plan: Primärschlüssel-Scan), kindgerecht sind je 200er-Band
  zwischen 30 % (2400) und 70 % (1000–1400).
- **Sternenjagd** (0.731.0, Wunsch 2026-10-10 „x Punkte auf dem Brett, von einer Figur in x Zügen zu fressen, jeden Zug
  einen, nur eindeutige Lösungen"): Routen `/stars` (Stufenkarte) und `/stars/:stage`, Kachel auf der Startseite, ohne
  Server. Regeln und Generator rein in `core/kids-stars.ts`: EINE weiße Figur (R/B/Q/N, seit 0.731.1 kein König) ohne König auf dem Brett (FEN
  nur mit ihr — das Brett prüft keine Schachregeln, `dests` rechnet die Seite), Sterne als `customSvg`-Markierung
  (`STAR_SVG`). Sterne BLOCKIEREN Turm/Läufer/Dame (auf ihn ja, darüber nicht), den Springer nicht. Gestellt wird nur,
  was GENAU EINE Reihenfolge hat (`solveStars` zählt bis 2): zufälliger Weg der Figur, dann zählen, bis 600 Versuche;
  bevorzugt mit mehr als einem erreichbaren Stern am Anfang; seit 0.732.0 nur, wenn die Lösung nach JEDEM Stern die
  Richtung wechselt (`turnsEveryMove`: Linie bei Turm/Läufer/Dame, Sprung beim Springer — zurück zählt als Wechsel). Deshalb ist jeder andere Stern eine Sackgasse und wird
  SOFORT als falsch gezeigt (`deadEnd`), ein Zug auf ein leeres Feld ebenso (`empty`) — beides bleibt `WRONG_HOLD_MS`
  stehen. `STAR_STAGES` = 20 Stufen, Figur reihum R/B/N/Q, je sechs frisch gewürfelte Aufgaben mit wachsender Sternzahl
  (`counts` = n, n, n+1, n+1, n+2, n+2; n = 2 + ⌊(Stufe−1)/4⌋, bis 8 Sterne; Wunsch 0.732.0 „steiler"); Tipps
  kostenlos (Stern leuchtet, dann Pfeil). Fortschritt nur im Browser (`KidsStarsStore`, `rh-kids-stars-v1`). Freies Spiel (0.733.0) `/stars/free` (Route-`data.free`, dieselbe
  `StarsPlayComponent`): Figur + Sternzahl 2–8 frei, Aufgaben ohne Ende, zählt nicht für Stufen, Wahl in `rh-kids-stars-free`.
  Seit 0.734.0 Zahlenfeld hinter der 8 bis `MAX_STARS` 63 (`maxStars`: Läufer 31). Über `RANDOM_WALK_MAX` (8) erst
  `generateChain` (RÜCKWÄRTS gelegt: von jedem Vorgängerfeld aus ist genau der nächste Stern sichtbar, sonst nur eine
  FALLE ohne Fortsetzung → eindeutig ohne Suche; trägt bis ~12, Springer ~20), sonst `generateOpenPath` (rückwärts mit
  Zurücksetzen + Warnsdorff, `unique: false`, mehrere Wege, seit 0.734.2 OHNE Abbiege-Regel — die gilt nur für eindeutige). Kette nur bis `chainMax` (R 14, B 12, N 25, Q 10), darüber gleich die offene Aufgabe; deren Anlauf bricht nach
  `OPEN_STEPS_PER_TRY` (20 000) Schritten ab und beginnt auf einem anderen Feld (0.734.1: vorher bohrte EIN festgefahrener
  Anlauf bis zum Zeitende — Springer 63 scheiterte). Je Aufgabe höchstens `GENERATE_BUDGET_MS` 600 ms; alle Figuren bis zum Maximum.
  `solveStars` hat einen Knotendeckel (`null` = keine Aussage). Bei `unique: false` prüft die Seite einen anderen Stern
  mit `solveStars` (Deckel 60 000, ohne Aussage = gilt) und führt die Restfolge (`route`). Die Figur ist immer
  ausgewählt: `PuzzleBoardComponent.autoSelect` wählt nach jeder Änderung und jedem Tippen wieder aus. Dafür
  zeichnet `PuzzleBoardComponent` `reviewShapes` jetzt auch beim Aufbau (vorher erst bei der nächsten Änderung).
  Verschickbarer Link `/stars/:stage/unlock` (0.758.0): Weiterleitung als `redirectTo`-Funktion in `app.routes.ts`,
  `KidsStarsStore.unlock` merkt die Stufe in `unlocked` (selber Schlüssel) — offen ohne die davor; geschafft öffnet sie
  wie gewohnt die nächste. Unbekannte Stufe → `/stars`.
- **Löser** `src-kidhub/app/core/kids-solver.ts` (rein, ohne Angular): EINE Form für Lichess-Puzzles
  (`startPly` 0) und Kurs-Linien (eigener `StartPly`, `-1` = kein Stellungszug; alles davor stumm vorgespult);
  im LETZTEN Zug zählt jedes Matt; Kurs-`AltMoves` sind „auch gut, aber gesucht ist ein anderer" (kein Fehler);
  falscher Zug → Fehlerpunkt, der Zug bleibt `WRONG_HOLD_MS` (2 s) mit markiertem Zielfeld stehen, dann Stellung
  zurück (0.562.0, Wunsch des Nutzers; die Stellung danach wird per `KidsSolver.fenAfter` AUSDRÜCKLICH gesetzt —
  jede geänderte Brett-Eingabe setzt sonst sofort die alte `fen`). Am PC ist Leertaste/Enter „Weiter" bzw. nach
  einer geschafften Stufe „Nächste Stufe" (`isAdvanceKey`: nicht in Feldern, auf Knöpfen/Links, bei gehaltener
  Taste; wer die Taste verbraucht, ruft `preventDefault`, sonst spränge dieselbe Taste über die Sterne hinweg).
  **PC-Aufbau** (0.562.0): Brett links, so groß wie die Fensterhöhe erlaubt (`--kid-board` in der App-Hülle, auch
  die Titelzeile richtet sich danach), rechts Aufgabentext (`[kidTask]`-Slot), Eule, Knopf; Punkte der Stufe in der
  Titelzeile; Brett blau (`boardTheme="blue"`). Bis 760px alles untereinander, Aufgabe über dem Brett. `KidsPuzzleComponent` hält ALLES in Signalen (Stellungszug und
  Gegnerantwort kommen per Timer — Angular 22 zeichnet unmarkierte Ansichten danach nicht neu) und gibt dem Brett
  nach jedem Zug ein NEUES `dests`-Objekt: nur eine geänderte Eingabe lässt das Brett einen falschen Zug optisch
  zurücknehmen.
- **Routen** `/`, `/levels`, `/levels/:level`, `/courses`, `/courses/:bookId`, `/login`, `/register`,
  `/forgot-password`, `/reset-password`, `/privacy`, `/account-deletion` — bewusst
  keine mit `/g`, `/t`, `/puzzles`: diese Präfixe schickt der gemeinsame nginx an die Link-Vorschau der API.
- **Symbole** (0.560.0, Vorlagen 2+3 seit 0.560.2): gezeichnete Vorlagen `design/kidhub/KidHub{,2,3}.png` (Bild-KI),
  alles in `public-kidhub/` leitet `design/kidhub/derive.py` ab — es stellt bei `KidHub.png` die WEISSEN Ecken frei
  (die Vorlage hat keinen Alphakanal) und färbt ein rotlila Stück Umriss um, schneidet `KidHub2.png` (maskable) um
  das Motiv zu (dort nur ~23 % des Radius, danach 32 % — innerhalb Androids sicherer 40 %) und schreibt in die leere
  rechte Hälfte von `KidHub3.png` Name und Satz (Vorschaubild).
  Rezept in `public-kidhub/ASSETS.md`; `KidHubAssetTests` hält Manifest, `index.html` und Dateien gegeneinander
  (dieselbe Falle wie bei der Turnierseite: was in `public-kidhub/` fehlt, kommt still aus `public/`).
- **Sprache aus dem IP-Land** (0.560.0): nur wenn die ermittelte Sprache keine Kindersprache ist, fragt KidHub
  `GET /api/kids/language-hint` (Land der Besucher-IP → de/en/hr/hu, `KidsLanguageHint`; seit 0.560.1 bekommt JEDES
  andere bekannte Land Englisch — „Frankreich → Englisch"), erst ohne Land (LAN, Ausfall) Deutsch
  (Warten höchstens `HINT_TIMEOUT_MS` 4 s). Nachgeschlagen wird LOKAL (`IpCountryService`): die Länderliste von DB-IP
  („IP to Country Lite", CC BY 4.0 — daher „IP Geolocation by DB-IP" im Fuß und in der Datenschutzerklärung) lädt der
  Server beim ersten Bedarf selbst (4,5 MB, ~717 000 Bereiche, 2,2 s Einlesen, ~19 MB Speicher), cacht sie 35 Tage im
  Temp-Ordner und fragt nach einem Fehlschlag erst nach einer Stunde wieder — keine Besucher-IP geht an einen fremden
  Dienst. Bewusst NICHT beim Start: die Integrationstests starten die API. LAN-Adressen haben kein Land.
- **Sprache** (0.558.3): Startsprache wie überall (Cookie `rookhub_lang` auf `.oberschmid.homes` → localStorage →
  Browser → en). KidHub steht in `partner-site.ts` als `KIDHUB_LABELS`: KEINE Partnerseite (kein Sprung), darf
  aber das geteilte Cookie SCHREIBEN — vorher las es das Cookie nur, und eine in KidHub getroffene Wahl verlor beim
  nächsten Laden gegen RookHubs. Die Auswahl im Fuß bindet `[selected]` je Option an `translate.currentLang()`
  (mit `[value]` am `<select>` zeigte sie immer „Deutsch“, den ersten Eintrag, während die Seite Englisch sprach).
  Jede Sprache außer de/en/hr/hu wird über `LocaleService.applyUnsaved('de')` als Deutsch ANGEZEIGT, ohne die Wahl
  zu überschreiben.
- **Drossel und Netzausfall** (Codereview 2026-09-29, A10-003 — Server-Teil unter „Rate-Limit nach Zweck"):
  `KidsApiService` hält Stufen-Leiter und Kursliste (je Sprache) `CATALOG_TTL_MS` (5 min, = `max-age` des Servers) im
  Speicher — Startseite, Stufenkarte und jeder Stufenstart teilen sich EINE Abfrage; eine leere Leiter und Fehler bleiben
  nicht liegen. Ein 429 auf `levels`, `levels/{n}`, `courses` und `courses/{id}/puzzles` holt er EINMAL nach
  (`retryOnceAfter429`: `Retry-After`, sonst ein ganzes Minutenfenster — die Limiter der API schicken keins; ein zweites
  429 zeigt das Fehlerbild). Bewusst dort und nicht im geteilten `retryInterceptor`, der 429 absichtlich nicht wiederholt.
  Der Service Worker (`ngsw-config.kidhub.json`, dataGroup `kids-catalog`, freshness, 5 s) liefert Stufen und Kurse,
  die schon einmal geladen waren, auch ohne Netz; `progress`, `language-hint` und `endless/batch` bleiben draußen.
- **Emojis** (Eule, Themenbilder, Sterne) brauchen eine Emoji-Schrift auf dem Gerät — Handys/Tablets haben sie,
  der Headless-Chromium auf dem Server nicht (Screenshots zeigen dort Kästchen).

## Als ein Nutzer einsteigen — auf BEIDEN Oberflaechen (0.429.0)

Der Einstieg selbst ist unveraendert (`POST /api/admin/users/{id}/impersonate`, das Token traegt
die Rollen des ZIELS). Neu ist, dass die Turnierseite ihn anbietet: `/admin` dort ist
`src-turnier/app/features/admin/turnier-admin.component.ts` — Kontenliste plus Einstieg, und sonst
nichts. **Bewusst nicht RookHubs Admin-Panel eingebunden**: dessen zehn Laschen (Buecher,
Tagespuzzle, Puzzle-Themen, Chessable, Menue-Sichtbarkeit, CI, Rollen) fuehren zu Bereichen, die es
auf der Turnierseite nicht gibt — sie waeren mehrheitlich Wege ins Leere. Der rote Streifen ist
dagegen GETEILT (`shared/impersonation-banner/`, in beiden Huellen eingehaengt): ein Einstieg ohne
sichtbaren Hinweis ist die gefaehrliche Variante, und das darf auf keiner der beiden Seiten anders
sein. Der Ausgang fuehrt auf `/admin` (beide Seiten haben eine solche Seite) und frischt das Menue
auf.

## Turnierkalender-Filterleiste (Stand 0.421.0)

Die Leiste ist um „was ist in meiner Naehe, und wann" gebaut: **Ort** (Autocomplete gegen den
Gazetteer + `my_location`-Knopf), **Umkreis** (gestufte Werte, kein Schieber) und **Zeitraum**.
Die Textsuche (Name/Ort/Veranstalter) steht in den ausklappbaren Zusatzfiltern — wer die Seite
oeffnet, sucht meist kein Turnier, dessen Namen er schon kennt. Suchprofile leben im ⋮-Menue;
ist eines gewaehlt, ZEIGT das Ortsfeld dessen Ort, und beim Anfassen von Ort oder Radius wird
daraus ein selbst gefuehrter Mittelpunkt (`profileId` faellt weg, `lat`/`lon`/`radiusKm` gehen
mit) — sonst behauptet die Leiste eines und der Server rechnet ein anderes.

**Der Browser-Standort laeuft ueber zwei Schritte** (`src-turnier/app/core/geolocation.service.ts`
+ `GET /api/tournament-directory/places/nearest`): die Ortung liefert Koordinaten, ins Feld gehoert
ein NAME. Aufgeloest wird gegen den LOKALEN Gazetteer — die Koordinaten eines Nutzers sind das
Letzte, was diese Anwendung nach draussen geben sollte. Findet sich kein Ort, gelten die
Koordinaten trotzdem (dann stehen sie selbst im Feld). Der Standort wird NIE von selbst abgefragt:
eine unaufgeforderte Abfrage beim Seitenaufruf loest eine Berechtigungsfrage ohne erkennbaren
Anlass aus, die im Zweifel abgelehnt wird — und danach ist der Knopf wirkungslos.

**Alles, was aus einer HTTP-Antwort kommt, liegt in Signalen** (`entries`, `total`, `pins`,
`calendarDays`, `loading`, …). Das ist die Behebung des gemeldeten Fehlers „ich sehe das Ergebnis
erst, wenn ich von Karte auf Liste wechsle": `provideHttpClient()` laeuft ueber `fetch`, zone.js
traegt die Angular-Zone NICHT durch den Antwort-Strom, und eine Feldzuweisung im Abonnenten loest
darum keine Aenderungserkennung aus. Der FILTER bleibt bewusst ein einfaches Objekt — er wird nur
durch Eingaben geaendert, und die laufen in der Zone. Siehe TODO.md fuer die uebrigen Seiten.

**Die Karten-Pins** sind eine eigene Canvas-Marke (`features/tournament-directory/map-pin-marker.ts`,
erbt von `L.CircleMarker`): Leaflet kennt im Canvas nur Kreise, und `divIcon`-Marker bringen bei
ein paar tausend Marken den Browser zum Kriechen. Ueberschrieben sind DREI Dinge, und sie muessen
zusammenpassen: die gezeichnete Form (`_updatePath`), der Trefferbereich (`_containsPoint` — Kopf
UEBER dem Anker plus zulaufender Schwanz; der geerbte Kreis um den Anker laege zur Haelfte unter
dem Pin im Leeren) und die Ausdehnung fuers Neuzeichnen (`_updateBounds` — sonst schneidet der
Renderer die oberen zwei Drittel weg). Popup- und Tooltip-Offsets kommen aus
`MapPinMarker.heightAbove`.

**Karte gekappt?** (Codereview 2026-09-29, F6-006): `/api/tournament-directory/map` antwortet `{ items, truncated }`;
bei `truncated` steht unter der Karte ein Hinweis (`tournamentDirectory.mapTruncated`). Der Service liest die alte
Listenform tolerant mit.

**EINE Kurzansicht fuer alle drei Ansichten** (`tournament-card.component.ts`, Stand 0.425.0).
Liste, Karte und Kalender zeigten dasselbe Turnier dreimal verschieden: die Liste als Karte mit
Lesezeichen, die Karte als von Hand gebauten DOM-Baum ohne Aktionen, der Kalender als nackten
Knopf mit dem Namen. Wer auf der Karte ein Turnier fand, musste erst auf die Detailseite, nur um
es zu merken. Jetzt ist es eine Komponente mit vier Aktionen (merken, in den Kalender
uebertragen, ausblenden, melden), und **die Aktionen macht die Komponente SELBST** — alle vier
betreffen genau dieses Turnier, sie in drei Eltern je viermal zu verdrahten waere derselbe Code
dreimal. Nach draussen geht nur, was den Eltern gehoert: `selected` (die Liste merkt vorher ihren
Filterzustand) und `ignoredChanged` (die Liste laesst die Zeile fallen, die Karte holt ihren
Ausschnitt neu, der Kalender laedt neu — dort steht ein Turnier an mehreren Tagen).

Drei Dinge, die dabei nicht kippen duerfen: (1) Im **Karten-Popup** wird die Komponente
DYNAMISCH erzeugt (`ViewContainerRef.createComponent` + `changeDetectorRef.detectChanges()`,
Element aus `ref.location.nativeElement`), weil Leaflet den Popup-Inhalt in einem eigenen
Container ausserhalb des Templates haelt; sie wird beim naechsten Popup, beim `clearLayers` und in
`ngOnDestroy` abgeraeumt, sonst haengt je geoeffnetem Punkt eine Komponente samt Abonnements im
Speicher. (2) Der eigene Zustand (`busy`/`subscribed`/`ignored`) liegt in **Signalen** und wird
aus dem Eintrag nur VORBELEGT — den Eintrag zu mutieren erreichte die Elternanzeige nicht
(OnPush), und die HTTP-Antworten kommen ohnehin ausserhalb der Zone an. (3) Die Komponente
importiert `MatDialogModule` selbst: sie oeffnet den Melde-Dialog und steht in drei verschiedenen
Eltern, auf deren Importe darf sie sich nicht verlassen.

**Der Merken-Knopf ist ein UMSCHALTER, und die KARTE zeigt den Zustand mit** (0.430.0). Zwei
Fehler, die zusammengehoerten: ein zweiter Klick tat gar nichts (`if (subscribed) return`), und
der Pin unterschied gemerkt/nicht gemerkt ueberhaupt nicht — die Auskunft stand nur in der
Kurzansicht, also erst nach dem Klick auf den richtigen Punkt, den man ohne die Auskunft nicht
kennt. `pinStyle()` in `map-pin-groups.ts` (bis zur Zerlegung Z27 in `tournament-map.component.ts`) faerbt gemerkte Punkte in einem eigenen
Farbton UND mit dickerem Ring (**zwei Kanaele** — Farbe allein trennt nicht fuer jeden), die
Abschwaechung fuer „nur ungefaehr verortet" bleibt darunter erhalten. Gemerkte werden ZULETZT
gezeichnet und liegen damit oben. Nach einem Klick faerbt `applySubscribed()` nur die Punkte
DIESES Turniers um — die Ausschnitts-Daten neu zu laden wuerde das offene Popup zuschlagen.

Im **Kalender** oeffnet ein Klick die Kurzansicht als kleines Fenster
(`tournament-card-dialog.component.ts`) statt direkt auf die Detailseite zu fuehren: im
Monatsraster ist ein Tag ein paar Zeilen hoch, und wer vergleicht, verliert beim Wegnavigieren den
Monat. Erst der Klick auf den Namen fuehrt weiter.

**Rueckmeldungen** (beide gehen in den Admin-Nachrichtenkanal, siehe API-Abschnitt):
`report-entry-dialog.component.ts` auf der Detailseite („falsches Event melden", mit dem
IST-Stand daneben und der Lern-Frage nach regionalen Turniernamen) und
`missing-tournament-dialog.component.ts` unter allen drei Ansichten („dein Turnier fehlt?",
Pflicht-Link).
