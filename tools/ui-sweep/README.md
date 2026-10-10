# UI-Sweep — alle Seiten rendern, prüfen, vergleichen

Rendert jede Seite aller fünf Oberflächen (RookHub, Turnierseite, KidHub, LeagueHub, ClubHub) in einem echten
Chromium, prüft jede automatisch und vergleicht sie mit dem vorigen Lauf. Gedacht für „wir haben etwas umgebaut —
was sieht jetzt anders aus, und ist irgendwo etwas kaputt?". Kein Teil der CI und keines Images.

```bash
cd tools/ui-sweep
./sweep.sh                                    # Vorgabe: Dev, alle Oberflächen, Handy + breit, dunkel, ab- und angemeldet
./sweep.sh --app rookhub --route games        # nur Routen mit „games" im Pfad
./sweep.sh --viewport mobile,laptop,wide --theme dark,light
./sweep.sh --local --app rookhub              # den lokalen Build (dist/) gegen die Dev-API — vor dem Deploy ansehen
./sweep.sh --compare 20261010_2215_stufe1     # gegen einen bestimmten Lauf vergleichen (Vorgabe: der vorige)
./sweep.sh --list                             # Katalog + aufgelöste Adressen, kein Browser
./sweep.sh --help
```

Ergebnis in `runs/<Zeitpunkt>[_name]/`: **`index.html`** (Übersicht, Filter „nur Auffällige" / „nur Veränderte"),
`report.json`, `shots/*.png`, `diff/*.png`. `runs/` ist nicht im Repo.

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
  Aufgabenblatt mit Teilen-Link, eine Rekonstruktion — alle mit „ui-sweep" im Namen, beim nächsten Lauf
  wiedergefunden; einmalig wird die Partie zur Analyse eingeworfen). Gefundene Werte merkt sich `runs/params-dev.json`;
  das Zufallspuzzle und der LeagueHub-Teilen-Link werden von dort weiterbenutzt. Fehlt ein Parameter (kein Freund,
  keine Kinderkurse auf Dev), wird die Route übersprungen und in der Übersicht unter „nicht auflösbar" genannt.
- **Zugang**: `~/.config/rookhub/dev-claude.env` (`ROOKHUB_DEV_USER`, `ROOKHUB_DEV_PASSWORD`) oder
  `UI_SWEEP_USER`/`UI_SWEEP_PASSWORD`. `--env prod` rendert nur abgemeldet und legt nichts an.
- **Browser**: das von Playwright gecachte Chromium (`~/.cache/ms-playwright/chromium-*`) oder `CHROME_BIN`.

## Vergleich

Je Bild ein Pixel-Vergleich (`pixelmatch`) mit dem vorigen Lauf derselben Art (dev/prod, lokal/deployt).
Unter 0,05 % abweichende Pixel gilt als gleich. Seiten mit Zufallsinhalt (Endlos-Puzzle, Kalender „ab heute",
Benachrichtigungen) ändern sich auch ohne Umbau — das ist kein Fehler.

## Fallen

- Jede Aufnahme bekommt einen frischen Browser-Kontext: angemeldet = `rookhub_user` im localStorage
  (die Antwort von `POST /api/auth/login`), Sprache `de`, Design über `rookhub_app_theme` + Cookie, Service Worker aus,
  Animationen aus.
- **Drossel**: die API begrenzt je Adresse (100 Anfragen/min global, anonyme Wege teils 60/min), und alle Aufnahmen
  kommen von einer Adresse. Ohne Bremse lief der erste Lauf in hunderte 429 und fotografierte Fehlerzustände (und
  füllt nebenbei das Log). Deshalb laufen alle API-Aufrufe durch ein gemeinsames Fenster von `--rate` (70) je Minute;
  `POST /api/client-log` wird gar nicht erst geschickt. Ein voller Lauf (≈ 290 Aufnahmen) dauert damit ~30–40 min.
- Gewartet wird, bis 0,8 s keine Anfrage mehr läuft, höchstens 45 s (Poller halten das Netz nie ganz still).
- Ganzseitige Bilder sind auf 4000 px Höhe begrenzt (`truncated` im Bericht).
