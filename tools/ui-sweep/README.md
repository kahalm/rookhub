# UI-Sweep — alle Seiten rendern, prüfen, vergleichen

Rendert jede Seite aller fünf Oberflächen (RookHub, Turnierseite, KidHub, LeagueHub, ClubHub) in einem echten
Chromium, prüft jede automatisch und vergleicht sie mit dem vorigen Lauf. Gedacht für „wir haben etwas umgebaut —
was sieht jetzt anders aus, und ist irgendwo etwas kaputt?". Kein Teil der CI und keines Images.

```bash
cd tools/ui-sweep
./sweep.sh                                    # Vorgabe: Dev, alle Oberflächen, Handy + breit, dunkel, ab- und angemeldet
./sweep.sh --app rookhub --route games        # nur Routen mit „games" im Pfad
./sweep.sh --viewport mobile,laptop,wide --theme dark,light
./sweep.sh --theme light --lang en --viewport tablet   # Zusatzachsen: Design, Sprache, Tablet-Breite
./sweep.sh --area clubhub --auth trainer,empty         # Sonderkonten: ClubHub-Trainer, leeres Konto
./sweep.sh --local --app rookhub              # den lokalen Build (dist/) gegen die Dev-API — vor dem Deploy ansehen
./sweep.sh --fast --viewport mobile,tablet,laptop,wide # jede Seite einmal laden, Breiten durch Umstellen (schneller, ohne Touch)
./sweep.sh --compare 20261010_2215_stufe1     # gegen einen bestimmten Lauf vergleichen (Vorgabe: der vorige)
./sweep.sh --list                             # Katalog + aufgelöste Adressen, kein Browser
./sweep.sh --help
```

Ergebnis in `runs/<Zeitpunkt>[_name]/`: **`index.html`** (Übersicht, Filter „nur Auffällige" / „nur Veränderte"),
`report.json`, `shots/*.png`, `diff/*.png`. `runs/` ist nicht im Repo.

## Tempo: Vorgaben je Umgebung und `--fast`

| Umgebung | Drossel der API je Adresse | `--rate` (Vorgabe) | `--concurrency` (Vorgabe) |
|----------|----------------------------|--------------------|---------------------------|
| `--env dev` | 500/min (`RateLimiting__PermitScale=5`) | 400/min | 4 |
| `--env prod` | 100/min | 70/min | 2 |

Ausdrückliche `--rate`/`--concurrency` gehen immer vor. Die Vorgabe bleibt mit Abstand unter der Drossel, weil die
Server-Minute auch Anfragen von vorher mitzählt (Poller, Nachzügler).

**`--fast`** (nicht Vorgabe): jede Seite (Route × Konto × Design × Sprache) wird nur EINMAL geladen; alle gewünschten
Breiten entstehen danach auf derselben Seite durch `page.setViewportSize` — kurz warten (400 ms), dann dieselbe
Netzruhe-Regel wie nach dem Laden (höchstens 15 s, keine neue Navigation), nach oben scrollen, die Prüfungen JE BREITE
neu rechnen. Bilder heißen genau wie ohne `--fast`, der Vergleich mit normalen Läufen greift also. Grenzen:

- **Handy-Ansicht nur über die Breite, ohne Touch.** `isMobile`/`hasTouch` lassen sich an einem offenen Kontext nicht
  umschalten; mit `--fast` wird der Kontext deshalb OHNE Handy-Emulation angelegt — kein `(pointer: coarse)`/`(hover: none)`,
  keine Touch-Ereignisse. Der Lauf trägt das im Kopf der Übersicht und im Ordnernamen (`_fast`).
- **Reihenfolge: schmalste Breite zuerst.** Geladen wird in der Handy-Breite, so wie ein Telefon die Seite aufbaut, danach
  wird nur breiter. Gemessen am Bereich `kurse` (mobile + wide gegen einen normalen Lauf): schmal zuerst → alle Handy-Bilder
  pixelgleich, 3 breite Bilder anders; breit zuerst → 6 Bilder anders, und die Handy-Bilder bekamen falsche Funde
  („Seite zu breit", „ragt raus") aus Breiten, die die Desktop-Ansicht beim Laden gesetzt hatte. Fehlalarme auf dem Handy
  wiegen schwerer als ein abweichendes Breitbild.
- **Was beim Laden aufgeht, behält seine Handy-Form**: ein Dialog/Bottom-Sheet, das die Seite beim Öffnen zeigt (z. B.
  „Wie möchtest du Kurse spielen?" im Kurs-Solver), bleibt auch in der breiten Aufnahme ein Bottom-Sheet. Solche Seiten
  sind im Vergleich mit einem normalen Lauf „verändert", ohne dass sich etwas geändert hat.
- Konsole/Anfragen einer Aufnahme enthalten alles seit dem Laden der Seite (auch das der schmaleren Breiten davor).

Voller Lauf `--fast` gegen den normalen (568 Bilder): 514 pixelgleich, 0 Funde nur in einem der beiden; die 54 übrigen
sind Zufallsinhalt (KidHub-Endlos/Sternenjagd, Analyse) und die Puzzle-/Kurs-Löser (Modus-Dialog beim Laden, Handy-Bilder
wenige Pixel kürzer ohne Touch-Emulation).

`--fast` ist für den schnellen Überblick gedacht; für die Abnahme eines Umbaus (Touch, Handy-Emulation) ohne `--fast`.

## Vorgabe-Lauf und Vollständiger Lauf

Die **Vorgabe** (`./sweep.sh` ohne Achsen-Optionen) ist der tägliche Blick: Handy + breit, dunkel, Deutsch, abgemeldet +
normales Konto. Gemessen 11.10.2026 auf Dev (400/min, 4 gleichzeitig): `--auth anon,user,admin` = 568 Aufnahmen in
10½ min (Bildteil 631 s, 0 Pausen wegen 429; 33 Bilder von der eigenen Bremse bis 43 s gehalten — die 400/min sind die Grenze),
mit `--fast` 284 Seitenladungen in 6¼ min (371 s). Vorher (80/min, 2 gleichzeitig) ~35 min für ≈ 300 Aufnahmen. Alles Weitere sind Zusatzachsen, die nur auf Zuruf mitlaufen:

| Achse | Werte | Vorgabe |
|-------|-------|---------|
| `--viewport` | `mobile` (390), `tablet` (820, Touch), `laptop` (1366), `wide` (1920) | `mobile,wide` |
| `--theme` | `dark`, `light` — RookHub/Turnier/LeagueHub/ClubHub über `rookhub_app_theme` + Cookie `rookhub_theme`; KidHub hat nur sein helles Design | `dark` |
| `--lang` | `de`, `en`, `hr`, `hu` … — Cookie + localStorage `rookhub_lang` (so wählt `LocaleService`), Browser-Sprache passend. LeagueHub und ClubHub sind fest deutsch, KidHub zeigt nur seine Kindersprachen | `de` |
| `--auth` | `anon`, `user`, `admin`, `empty`, `trainer` | `anon,user` |

Bilder mit `--lang` ≠ `de` tragen die Sprache im Dateinamen (`…__light__en.png`); deutsche heißen wie vor der
Sprach-Achse, damit der Vergleich mit älteren Läufen weiter greift.

**Vollständiger Lauf** (vor einem großen Umbau-Tag, ≈ 1 700 Aufnahmen, mehrere Stunden — am besten in Teilen nach
`--app` oder `--area`, sonst bremst die Drossel alles):

```bash
./sweep.sh --auth anon,user,admin,empty,trainer --viewport mobile,tablet,laptop,wide --name voll-dunkel
./sweep.sh --auth anon,user --viewport mobile,wide --theme light --name voll-hell
./sweep.sh --auth anon,user --viewport mobile --lang en --name voll-en
```

## Was geprüft wird (`checks.mjs`)

- **Seite zu breit** — das Dokument scrollt waagerecht.
- **ragt raus** — sichtbare Elemente reichen über den Fensterrand, ohne dass ein Vorfahre sie einfängt.
- **Text abgeschnitten** — ein Element schneidet ab (`overflow: hidden`), sein Text ist breiter, keine Auslassungspunkte.
- **Schlüssel statt Text** — sichtbarer Text wie `games.review.title` (Namensräume aus `public/i18n/en.json`).
- **lädt noch** — nach dem Laden steht noch ein Ladekreis.
- **Konsole / Anfrage** — Konsolenfehler, Ausnahmen, API-Antworten ab 400, abgebrochene Anfragen.
- **weitergeleitet** — die Seite landete woanders (z. B. auf der Anmeldung).

Die Prüfungen ersetzen das Hinsehen nicht — Gestaltung („wirkt das gut?") sieht nur ein Mensch bzw. Claude.

## Routen und Testdaten

- **`routes.mjs`** ist der Katalog. Neue Seite in einer App → dort eintragen (`auth`: `any` = ab- und angemeldet,
  `user` = nur angemeldet, `anon` = nur abgemeldet). Platzhalter `{name}` kommen aus `seed.mjs`.
- **`seed.mjs`** sucht die Parameter über die API des Kontos `claude-dev` und legt fehlende an (eine Partie, ein
  Aufgabenblatt mit Teilen-Link, eine Rekonstruktion, eine geteilte Repertoire-Linie, für das normale
  Konto einen eigenen Kalkulationskurs „Kalkulation (ui-sweep)", eine offene LeagueHub-Einlesung — alle mit „ui-sweep" im Namen, beim nächsten Lauf
  wiedergefunden; einmalig wird die Partie zur Analyse eingeworfen). Gefundene Werte merkt sich `runs/params-dev.json`;
  das Zufallspuzzle und der LeagueHub-Teilen-Link werden von dort weiterbenutzt. Fehlt ein Parameter (kein Freund,
  keine Kinderkurse auf Dev), wird die Route übersprungen und in der Übersicht unter „nicht auflösbar" genannt.
- **Rollen** (`--auth`): `admin` = das Admin-Konto `claude-dev` (`ROOKHUB_DEV_USER`/`_PASSWORD`),
  `user` = ein normales Konto ohne Sonderrechte (`ROOKHUB_DEV_PLAIN_USER`/`_PASSWORD`), `empty` = ein frisch registriertes
  Konto ganz ohne Daten für die Leerzustände (`ROOKHUB_DEV_EMPTY_USER`, `claude-dev-empty`), `trainer` = ClubHub-Trainer
  (`ROOKHUB_DEV_TRAINER_USER`, `claude-dev-trainer`: Rolle „ClubHub-Trainer (ui-sweep)" mit `club.trainer` über die
  gleichnamige Gruppe, als Konto mit Zugriff der ersten ClubHub-Gruppe zugeteilt — `ensureTrainer` in `seed.mjs`, bei
  jedem Lauf mit `trainer` nachgezogen). Die drei Konten werden beim ersten Gebrauch auf Dev registriert und an
  `~/.config/rookhub/dev-claude.env` angehängt (`ACCOUNTS` in `sweep.mjs`). Konto-eigene Parameter (Partie, Repertoire,
  Kurs, Einlesung … — `OWNED_PARAMS`) kommen für `user` aus den eigenen Daten (`runs/params-dev-user.json`); `empty` und
  `trainer` haben keine, ihre Routen damit entfallen still („ohne eigene Daten"); der Trainer bekommt Kind und Gruppe aus
  SEINER Sicht (`TRAINER_PARAMS`). `anon` überspringt Routen, die ein Konto voraussetzen (`ANON_SKIP`).
- **Engine-abhängige Parameter** (`analysisId`, `analyzedGameId`, `analyzedShareToken`, `comparisonId`): der Seed stößt
  „Partie analysieren" an der Sweep-Partie bzw. einen Zugvergleich an, sobald Dev eine Engine hat
  (`/api/game-analyses/guess/status`), und nimmt nur FERTIGE — fertig ist eine Analyse erst nach Minuten, also beim
  nächsten Lauf. Ohne Engine (Stand 11.10.: keine Registrierung auf Dev) sagt `--list` das als Grund an.
- **LeagueHub-Einlesungen**: Dev liest Formulare nicht selbst (`Scoresheet:Reader=external`, der Watcher schaut nur auf
  Prod) — ein hochgeladenes Testbild (`ui-sweep-formular.png`, ein gezeichnetes Blatt) bleibt „wartet aufs Einlesen"
  stehen, ohne Schlüssel. Je Konto eine (`scanId`, wird wiedergefunden), dazu eine anonyme über den Teilen-Link
  (`shareScanKey`, gemerkt in `runs/params-dev.json`). Die Seite zeigt eine laufende Uhr — sie ist bei jedem Vergleich „verändert".
  `--env prod` rendert nur abgemeldet und legt nichts an.
- **Bestand des normalen Kontos**: `node seed-user.mjs` gibt ihm, was ein Nutzer auf Prod typischerweise hat — Kurse über
  eine Gruppe (drei angepinnt, drei angefangen), vier Repertoires, eigene Partien, ein Aufgabenblatt, Trainingsziel,
  Puzzle-Versuche, einen Endlos-Lauf, eine Freundschaft; LeagueHub als Mitglied Schwaz (Rolle „Verein Schwaz"),
  KidHub-Fortschritt und ein verknüpftes ClubHub-Karteiblatt. Wiederholbar, nur Dev.
- **Browser**: das von Playwright gecachte Chromium (`~/.cache/ms-playwright/chromium-*`) oder `CHROME_BIN`.

## Vergleich

Je Bild ein Pixel-Vergleich (`pixelmatch`) mit dem vorigen Lauf derselben Art (dev/prod, lokal/deployt).
Unter 0,05 % abweichende Pixel gilt als gleich. Seiten mit Zufallsinhalt (Endlos-Puzzle, Kalender „ab heute",
Benachrichtigungen) ändern sich auch ohne Umbau — das ist kein Fehler.

## Fallen

- Jede Aufnahme (mit `--fast`: jede Seite) bekommt einen frischen Browser-Kontext: angemeldet = `rookhub_user` im localStorage
  (die Antwort von `POST /api/auth/login`), Sprache `de`, Design über `rookhub_app_theme` + Cookie, Service Worker aus,
  Animationen aus.
- **Drossel**: die API begrenzt je Adresse (Prod 100 Anfragen/min global, anonyme Wege teils 60/min; Dev das Fünffache), und alle Aufnahmen
  kommen von einer Adresse. Ohne Bremse lief der erste Lauf in hunderte 429 und fotografierte Fehlerzustände (und
  füllt nebenbei das Log). Deshalb laufen alle API-Aufrufe durch ein gemeinsames Fenster von `--rate` je Minute
  (Vorgabe dev 400, prod 70 — Dev läuft mit 500/min, siehe „Tempo"); `POST /api/client-log` wird gar nicht erst
  geschickt. Antwortet der Server trotzdem 429, pausieren alle eine Minute und die Seite wird neu aufgenommen; die Zahl
  der Pausen steht am Ende der Ausgabe und im Kopf der Übersicht.
- Gewartet wird, bis 0,8 s keine Anfrage mehr läuft, höchstens 60 s (Poller halten das Netz nie ganz still).
- Ganzseitige Bilder sind auf 4000 px Höhe begrenzt (`truncated` im Bericht).
