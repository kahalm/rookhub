// Single Source of Truth fuer App-Version + Changelog.
// Wird von BEIDEN Environment-Dateien importiert (environment.ts = dev,
// environment.prod.ts = prod-Build via fileReplacements). Dadurch zeigt der
// Footer in JEDEM Build dieselbe Version/Changelog — ein Bump aendert nur hier.
export const APP_VERSION = '0.78.1';

export interface ChangelogEntry {
  version: string;
  date: string;
  changes: string[];
}

export const CHANGELOG: ChangelogEntry[] = [
  { version: '0.78.1', date: '2026-06-03', changes: [
    'App-Vorbereitung: echte App-Icons (192/512 px + „maskable" für runde/adaptive Android-Icons) statt nur des kleinen Favicons; das Web-App-Manifest wurde vervollständigt (id/scope/lang/categories + apple-touch-icon). Damit ist RookHub eine sauber installierbare PWA — Grundlage für eine spätere Veröffentlichung im Google Play Store. Keine Änderung an Funktionen.',
  ]},
  { version: '0.78.0', date: '2026-06-03', changes: [
    'Endlos-Gauntlet: jeder Lauf bekommt jetzt einen eindeutigen Seed; Seed + die geordneten Ketten-Puzzle-IDs werden beim Lauf-Ende am Server gespeichert (neue Spalten auf der Session). Damit ist die exakte Kette eines Laufs dauerhaft hinterlegt — Grundlage für ein späteres Replay.',
  ]},
  { version: '0.77.0', date: '2026-06-03', changes: [
    'Endlos ist jetzt ein „Gauntlet": Beim Start wird die komplette Puzzle-Kette generiert und auf deinem Gerät abgelegt. Ein Seiten-Refresh oder „Fortsetzen" zeigt deshalb immer exakt dasselbe Puzzle — die Reihenfolge steht von Anfang an fest.',
    'Die Schwierigkeit folgt einer annähernd logarithmischen Kurve: schnell hoch bis zum 1. Schwellenwert (Ø erster Fehler, ~5 Puzzles), dann gemächlicher bis zum 2. Schwellenwert (Ø Maximum deiner letzten 5 Läufe, ~20 Puzzles), danach nur noch leicht ansteigend.',
    'Jedes Puzzle führt weiter — egal ob gelöst oder nicht: Ein Fehler kostet ein Leben UND rückt zum nächsten (höheren) Puzzle. Bei 0 Leben ist Schluss.',
    'Online wird am Kettenende automatisch nachgeneriert; offline am Ende der Kette erscheint „You win" 🏆.',
    'Einstellungen: beide Schwellenwerte bleiben editierbar; die Vorschau zeigt jetzt den Rating-Verlauf der Kette (Puzzle 1 / 6 / 21 / 30).',
    'Das Offline-Vorabladen des Endlos-Pools nutzt jetzt dieselbe Ketten-Kurve wie der Live-Gauntlet (vorher die alte Lauf-Logik).',
  ]},
  { version: '0.76.0', date: '2026-06-03', changes: [
    'Tagespuzzle wird persistiert: neuer Endpoint `GET /api/book-puzzles/daily/{yyyyMMdd|today}` (anonym) liefert das Tagespuzzle für ein UTC-Datum. Pro Tag eine Zeile in der neuen Tabelle `DailyPuzzles` (PK=Date, FK→BookPuzzle); ein Background-Service `DailyPuzzleScheduler` legt die heutige Zuordnung um 00:00 UTC an + holt verpasste Tage beim Start nach. On-Demand-Fallback wenn Scheduler offline war. `GetRandomAsync(pool="daily")` routet jetzt durch die persistierte Zuordnung — historische Tagespuzzles bleiben damit stabil, egal wie der `forDaily`-Pool sich später ändert. Migration `AddDailyPuzzleTable`, Book-Löschung räumt Historie ab.',
  ]},
  { version: '0.75.0', date: '2026-06-03', changes: [
    'Tagespuzzle-Solver-Updates per Webhook: nach jedem Buch-/Tagespuzzle-Versuch feuert die API einen HMAC-signierten POST an den Schach-Bot — der aktualisiert den Discord-Post sofort statt erst nach ≤5 Minuten (vorheriges Polling-Modell). Neuer Service `SchachBotWebhookService`, fire-and-forget via vorhandene `BackgroundTaskQueue`. Konfig: `SchachBot__WebhookUrl` (z.B. http://schach-bot:9000/webhook/puzzle-attempt) + `SchachBot__WebhookSecret` (muss identisch zum Bot-`WEBHOOK_SECRET` sein). Beide leer = Webhook deaktiviert. Compose-Files (dev / dev.vpn / vpn) reichen `SCHACH_BOT_WEBHOOK_URL` und `SCHACH_BOT_WEBHOOK_SECRET` durch.',
  ]},
  { version: '0.74.0', date: '2026-06-03', changes: [
    'Admin: Kibana-Dashboard-Link im Admin-Header (en/de/hr lokalisiert). Wird nur angezeigt, wenn `KIBANA_URL` im Server-Env gesetzt ist — gelesen via neuem Endpoint `GET /api/admin/config` (Admin-only). Compose-Files reichen `KIBANA_URL` an die API als `Kibana__Url`.',
  ]},
  { version: '0.73.2', date: '2026-06-03', changes: [
    'Fix (Endlos): Nach „Nochmal spielen" konnte ein bereits beendeter Run fälschlich erneut fortgesetzt werden (Resume → Aufgeben → Nochmal → wieder fortsetzbar). „Nochmal spielen" verwirft den beendeten Run jetzt vollständig.',
  ]},
  { version: '0.73.1', date: '2026-06-03', changes: [
    'Fix: Das Dashboard zeigt jetzt dein tatsächliches Puzzle-Elo — nämlich das deines meistgespielten Levels — statt immer das Elo des Normal-Levels (das bei reinem Visualisierungs-/Blindfold-Spiel auf dem Standardwert 1500 stehen bleibt). Die Statistik-Übersicht nutzt dieselbe Logik.',
  ]},
  { version: '0.73.0', date: '2026-06-03', changes: [
    'Neu: „Eingeloggt bleiben"-Option beim Anmelden — hält dich 30 Tage angemeldet (sonst läuft die Anmeldung nach 1 Tag ab).',
    'Passwort-Anforderung vereinfacht: ein Passwort braucht jetzt nur noch mindestens 4 Zeichen (vorher: 8 Zeichen mit Groß-/Kleinbuchstabe + Ziffer).',
  ]},
  { version: '0.72.0', date: '2026-06-03', changes: [
    'Die drei Puzzle-Modi (Standard, Buch/Tagespuzzle, Endlos) verhalten sich jetzt einheitlich:',
    'Aufgeben spielt in allen Modi die Lösung von vorne durch (vorher sprang der Endlosmodus nur ans Ende).',
    'Nach dem Lösen springt jeder Modus per kurzem, sichtbarem Countdown automatisch zum nächsten Puzzle — jederzeit per „Weiter" überspringbar (vorher: Standard 3s, Endlos sofort, Buch gar nicht).',
    'Endlos: Nach „Analysieren" → „Zurück" geht es direkt beim nächsten Puzzle des laufenden Runs weiter statt in der Übersicht zu landen.',
    'Endlos: Nach einem Fehlversuch gibt es jetzt „Wiederholen" (kostet kein weiteres Leben); Einstellungen (Brett-/Figurenthema + Visualisierung) sind auch während des Spiels erreichbar.',
    'Buch-Puzzle hat jetzt — wie die anderen Modi — den „Bewertung anzeigen"-Knopf (Stockfish-Einschätzung).',
    'Der „Mausrutscher"-Knopf und der gemerkte Offen-Zustand der Einstellungen verhalten sich nun in allen Modi gleich.',
    'Unter der Haube: die gemeinsame Lösungs-/Review-/Countdown-Logik liegt jetzt zentral (weniger Duplikat), keine Funktionsänderung darüber hinaus.',
  ]},
  { version: '0.71.1', date: '2026-06-03', changes: [
    'Fix (Betrieb): Der Docker-Healthcheck des Frontend-Containers prüft jetzt 127.0.0.1 statt localhost — sonst wurde der Container fälschlich als „unhealthy" gemeldet (busybox-wget wählte für „localhost" IPv6 ::1, wo nginx nicht lauscht). Reiner Healthcheck-Fix, keine Funktionsänderung an der App.',
  ]},
  { version: '0.71.0', date: '2026-06-03', changes: [
    'Internes Code-Review/Refactoring (keine bewusste Funktionsänderung): die großen Puzzle-/Turnier-/Admin-Komponenten wurden in Templates/Styles + wiederverwendbare Bausteine aufgeteilt, die Hinweis-Meldungen (Snackbars) zentralisiert und die Buch-/Kurs-/Admin-Logik in eigene Services ausgelagert (schlankere, testbarere Controller). Verbessert Wartbarkeit; 223 Frontend- + 482 Backend-Tests grün.',
    'Fix: Das Löschen eines Buchs entfernt jetzt auch die aufgezeichneten Tagespuzzle-/Buch-Versuche korrekt (vorher konnte es an einer Datenbank-Beziehung scheitern).',
    'Fix: Anonymer Endless-Fortschritt wird je Sitzung eindeutig gespeichert (Unique-Index + Bereinigung etwaiger Alt-Duplikate).',
    'Kleinigkeit: Die Brett-/Figuren-Auswahl im Einstellungs-Zahnrad sieht jetzt in allen Puzzle-Modi einheitlich aus (ein versehentlicher grauer Chip-Hintergrund im Standard-/Endlosmodus ist weg).',
  ]},
  { version: '0.70.0', date: '2026-06-03', changes: [
    'Puzzle-Buttons konsistent (wie im Endlosmodus): „Aufgeben" ist jetzt von Anfang an verfügbar — auch beim Buch-Puzzle schon vor dem ersten Zug. „Zurücksetzen" und „Mausrutscher" erscheinen erst, nachdem ein Zug gemacht wurde (vorher waren sie beim Standard-Puzzle schon am Anfang da, obwohl es nichts zurückzusetzen gab).',
    'Die Puzzle-Einstellungen/Filter (Zahnrad) bleiben jetzt offen, wenn du zum nächsten Puzzle wechselst (der Offen-Zustand wird gemerkt) — vorher klappten sie zu.',
  ]},
  { version: '0.69.0', date: '2026-06-03', changes: [
    'Auch ohne Login zählen gelöste Buch-/Tagespuzzles jetzt mit: Ein anonym (nicht eingeloggt) gelöstes Buch-Puzzle wird über eine anonyme Sitzungs-ID erfasst (neuer Endpoint POST `/api/book-puzzles/{id}/attempt/anonymous`). In der Tagespuzzle-Anzeige auf Discord erscheinen eingeloggte Löser namentlich, anonyme als Anzahl („+N anonym"). Eingeloggte Solves bleiben wie bisher (namentlich, via `/attempt`).',
  ]},
  { version: '0.68.1', date: '2026-06-03', changes: [
    'Fix: Client-Diagnose-Endpoint loggt Routine-Heartbeats (z.B. Bot-Lebenszeichen) jetzt auf Info statt Warning — sonst lösten die regelmäßigen Heartbeats im Log-Watcher einen „warn_spike"-Fehlalarm aus. Echte Engine-Crash-/Hänger-Meldungen bleiben auf Warning.',
  ]},
  { version: '0.68.0', date: '2026-06-03', changes: [
    'Überwachung/Betrieb: Die API sendet jetzt alle 60 s ein „Heartbeat"-Lebenszeichen nach Elasticsearch (inkl. kurzem DB-Selbst-Check, Status healthy/degraded). Damit erkennt der Log-Watcher einen toten/hängenden Dienst an AUSBLEIBENDEN Heartbeats — statt Stille nicht von „gerade ruhig" unterscheiden zu können. Zusätzlich Docker-Healthchecks für API (/health), Frontend und Crawler (Container-Neustart bei Fehler). (Intervall via `Heartbeat:IntervalSeconds` konfigurierbar.)',
  ]},
  { version: '0.67.0', date: '2026-06-03', changes: [
    'Kibana-Dashboard stark erweitert (in Sektionen): Puzzle (Genauigkeit, Rating-Verteilung, Ø Zeit/Puzzle, Attempts je Viz-Level, Top-User nach Elo), Endless (Runs/Tag, Ø gelöste Puzzles, Max-Rating-Verteilung, Leaderboard), Kurse (Solves/Tag, je Buch, je User) und Betrieb (Feature-Nutzung nach Bereich, HTTP-Fehler über Zeit, langsamste Endpoints).',
    'Neu „Unique Visits" statt der Login-Panels: eindeutige Besucher (eingeloggt via Username, anonym via Session-Id) als Metrik + Verlauf je Tag. „Recent Logs" zeigt jetzt Zeitstempel / Level / RequestPath / Message / Username.',
    'API: neues Event EndlessSessionCompleted (Endless-Session-Zusammenfassung: gelöste Puzzles, Max-Rating, Dauer) und eine VisitorId auf jedem Request (Username wenn eingeloggt, sonst anonyme Session-Id via neuem X-Visitor-Id-Header). Die Kurs-Kennzahlen bauen auf dem bestehenden CoursePuzzleAttempt-Log (0.63.0) auf.',
    'Dashboard-Deploy automatisiert: kibana-init ist jetzt ein gebautes Image (init-kibana.sh eingebacken) — Dashboard-Updates wandern beim `docker compose up -d --build` automatisch mit, ohne manuelles git pull / Container-Recreate.',
  ]},
  { version: '0.66.1', date: '2026-06-02', changes: [
    'Sicherheit/Robustheit (Code-Review): Öffentliche Profile (per Username) geben keine sensiblen Daten mehr preis — nur noch Anzeigename + öffentliche Schach-IDs, KEINE Klarnamen/ChessResults-ID/Discord-Verknüpfung. Kurs-Lösungen können bei parallelem Speichern nicht mehr verloren gehen. Discord-Verknüpfung meldet eine Kollision jetzt sauber (statt Serverfehler). Analyse-Engine erkennt einen ausbleibenden Engine-Start jetzt auch in der Initialisierungsphase und startet automatisch neu (kein „Berechne…"-Festhängen mehr). Offline gelöste Endless-Puzzles werden nicht mehr verloren (lokal vorgemerkt + bei Reconnect synchronisiert). Offline-Fallback wählt ein Puzzle mit passendem Rating statt irgendeinem. Mehrere kleinere Härtungen (Rate-Limiting hinter Proxy, Log-Hygiene, defensivere Server-Aufrufe).',
  ]},
  { version: '0.66.0', date: '2026-06-02', changes: [
    'Schach-Engine (Stockfish) funktioniert jetzt auch offline: Die Engine-Dateien werden vom Service Worker vorab gecacht, sodass Analyse, Bewertungsanzeige und Eval-Bar ohne Internet laufen. (Der frühere „Berechne…"-Hänger lag nicht am Offline-Caching, sondern an der Befehlsreihenfolge zur Engine und ist seit 0.64.2 behoben; greift dennoch etwas daneben, springt die Selbstheilung + Hänger-Watchdog ein.)',
  ]},
  { version: '0.65.0', date: '2026-06-02', changes: [
    'Engine-Diagnostik: Browser-Stockfish-Crashes und -Hänger werden jetzt erkannt und an den Server gemeldet (neuer Endpoint POST /api/client-log → strukturiert in Elasticsearch/Kibana). Erfasst werden Worker-Absturz, fehlgeschlagene Engine-Initialisierung, abgebrochene Suchen (Timeout) und – im Analyse-Modus – ein „Hänger"-Watchdog (keine Berechnungs-Ergebnisse nach Start) inkl. automatischem Neustart. So sieht man in Kibana, wie oft die Engine bei echten Nutzern Probleme macht. (Meldungen pro Art gedrosselt.)',
  ]},
  { version: '0.64.2', date: '2026-06-02', changes: [
    'Analyse hängt nicht mehr bei „Berechne …": Der Stockfish-WASM wird nicht mehr über den Service-Worker-Cache geladen (sondern direkt) — ein aus dem Cache serviertes WASM ließ den Engine-Worker oft nicht starten. Außerdem sauberes UCI-Sequencing: eine neue Stellung wird erst analysiert, nachdem die Engine den vorherigen Lauf bestätigt gestoppt hat (verhindert verschluckte Suchen). Hinweis: Engine-Analyse/Eval brauchen jetzt eine Verbindung (Puzzle-/Endless-Lösen offline bleibt unberührt).',
  ]},
  { version: '0.64.1', date: '2026-06-02', changes: [
    'Stockfish im Browser stabiler: Stürzt der WASM-Worker ab (z.B. Speicher), wird er jetzt automatisch neu gestartet, statt die Engine für die restliche Sitzung lahmzulegen. Im Analyse-Modus wird die aktuelle Stellung nach einem Absturz nahtlos wieder aufgenommen (mit Schutz gegen Endlos-Neustarts); die Hash-Größe ist begrenzt, um Speicher-Crashes bei langen Analysen vorzubeugen. Eine fehlgeschlagene Engine-Initialisierung wird beim nächsten Versuch erneut probiert.',
  ]},
  { version: '0.64.0', date: '2026-06-02', changes: [
    'Offline-Pools werden jetzt schon beim App-Start vorab geladen (sobald online) – nicht erst beim ersten Öffnen des Modus. Standard-Puzzle (passend zu Elo + Schwierigkeit) und Endless-Run werden im Hintergrund gecacht, sodass beide Modi direkt offline gestartet werden können. Wird auch bei Reconnect nachgezogen; füllt nur, was noch fehlt.',
  ]},
  { version: '0.63.0', date: '2026-06-02', changes: [
    'Strukturiertes Logging pro Puzzle (für Elasticsearch/Kibana): jedes gelöste/aufgegebene Puzzle wird mit Start- und Lösungszeit geloggt — über alle Modi (Standard-Puzzle, Tagespuzzle/Buch, Kurs, Endless). Im Endless-Modus werden die einzelnen Puzzles einer Session mit ihren Zeiten beim Session-Ende an den Server gemeldet (nur Logging, nicht persistiert); Kurs-Lösungen senden jetzt zusätzlich die benötigte Zeit.',
  ]},
  { version: '0.62.0', date: '2026-06-02', changes: [
    'Endless: Nach dem Lösen eines Puzzles lässt es sich jetzt analysieren — der Button „Letztes Puzzle analysieren" bleibt sichtbar (auch nachdem automatisch das nächste Puzzle geladen wurde) und öffnet das gerade gelöste Puzzle im Analysemodus. Beim Aufgeben gibt es im Lösungs-Screen zusätzlich „Analysieren" für das aktuelle Puzzle. Zurück führt jeweils in den Endless-Modus (laufender Run lässt sich fortsetzen).',
  ]},
  { version: '0.61.0', date: '2026-06-02', changes: [
    'Echter Offline-Betrieb: Die App hat jetzt einen Service Worker und cacht App-Shell, alle Lazy-Module (Puzzle, Endless …) und die Übersetzungen. Dadurch lassen sich Puzzle- und Endless-Modus auch ohne Verbindung öffnen und starten (sofern vorher einmal online geladen) — nicht mehr nur weiterspielen. Bei einer neuen Version erscheint ein „Neu laden"-Hinweis.',
    'Offline gelöste Puzzles gehen nicht mehr verloren: Lösungen/Versuche (Standard-Puzzle, Tagespuzzle, Kurs-Puzzle und Endless-Sessions) werden offline lokal vorgemerkt und automatisch hochgeladen, sobald wieder eine Verbindung besteht. Im Profil zeigt „Offline" die Anzahl noch wartender Lösungen.',
    'Endless lädt schon beim Öffnen der Konfiguration einen Run vorab (online), damit ein Offline-Start Daten hat; ohne Cache gibt es jetzt einen klaren Hinweis statt „Run beendet". Auch der Standard-Puzzle-Modus zeigt offline ohne Cache einen verständlichen Hinweis.',
  ]},
  { version: '0.60.0', date: '2026-06-02', changes: [
    'Bücher offline: In der Kurs-Liste lässt sich jedes Buch per Wolken-Button offline speichern (alle Puzzles lokal gecacht) bzw. wieder entfernen. Ist ein Buch offline gespeichert, funktionieren ohne Internet der Direktaufruf eines Buch-Puzzles sowie „Nächstes im Buch"/„Zufällig aus Buch" aus dem Cache. Größe + Anzahl gecachter Bücher stehen im Profil unter „Offline" (mit „Cache leeren"). Neuer Endpoint GET `/api/courses/{bookId}/puzzles`.',
  ]},
  { version: '0.59.0', date: '2026-06-02', changes: [
    'Offline-Einstellungen im Profil (Abschnitt „Offline", pro Gerät): wie viele Standard-Puzzles (auf der aktuellen Schwierigkeit) und wie viele Endless-Runs offline vorgehalten werden (Standard 10 / 2), plus Anzeige der Cache-Größe und „Cache leeren"-Button.',
    'Standard-Puzzle-Modus offline: bei fehlender Verbindung werden vorab geladene Puzzles aus dem lokalen Pool gespielt (wird online aufgefüllt; bei Schwierigkeitswechsel neu).',
    'Endless: es werden jetzt mehrere Runs (Standard 2, einstellbar) vorab geladen statt nur einer.',
  ]},
  { version: '0.58.0', date: '2026-06-02', changes: [
    'Grundlage Tagespuzzle-Anzeige: Lösungsversuche an Standalone-Buch-Puzzles werden für eingeloggte User erfasst (neue Endpoints POST `/api/book-puzzles/{id}/attempt` + GET `/api/book-puzzles/{id}/results` mit Solver-Liste inkl. Discord-Verknüpfung). Der Schach-Bot kann damit anzeigen, wer das Tagespuzzle gelöst hat.',
  ]},
  { version: '0.57.2', date: '2026-06-02', changes: [
    'Fix (Endless): „Unfinished run" wurde mit `0 lives` angezeigt — der Run war faktisch vorbei, aber der Active-State landete kurz vor `endGame()` mit lives=0 auf dem Server und wurde dort als „resumebar" gemerkt, wenn der User die Seite verlies. `loseLife()` und `resetPuzzle()` schreiben jetzt bei 0 Lives null statt einen Zombie-State; beim Laden werden Legacy-Zombies (lives ≤ 0) lokal und am Server aufgeräumt; das Banner ist zusätzlich gegen lives ≤ 0 abgesichert.',
  ]},
  { version: '0.57.1', date: '2026-06-02', changes: [
    'Fix: Die EF-Core-Warnung „FirstOrDefault ohne OrderBy" wird jetzt an der Quelle behoben — die zufällige Puzzle-Auswahl ermittelt den ID-Bereich über deterministische Min/Max-Aggregate statt über GroupBy+FirstOrDefault. Die frühere Log-Unterdrückung dafür wurde wieder entfernt.',
  ]},
  { version: '0.57.0', date: '2026-06-02', changes: [
    'Buch-Puzzle (Standalone, `/puzzles/book/:id`): zwei neue Buttons „Nächstes im Buch" (nächstes Puzzle in Buchreihenfolge, am Ende wieder vorne) und „Zufällig aus Buch". Neue Endpoints GET `/api/book-puzzles/{id}/next` + `/api/book-puzzles/{id}/random`. In Kurs-/Wochenpost-Ansicht weiterhin deren eigene Navigation.',
  ]},
  { version: '0.56.0', date: '2026-06-02', changes: [
    'Statistik „Alle": Die Elo-Kurven aller Visualisierungs-Modi werden jetzt in EINER Grafik überlagert (gemeinsame Skala, je Modus farbkodiert) mit kleiner Legende — statt getrennter Mini-Charts.',
  ]},
  { version: '0.55.2', date: '2026-06-02', changes: [
    'Feature: Buch-Import-Antwort meldet jetzt drei Zahlen statt zwei — Importiert / Duplikate / Ungültig. Bisher zählte „Skipped" nur Duplikate; Spiele, die der Parser wegen fehlender Round/FEN/Mainline oder Grundstellung-ohne-[%tqu]-Marker verwirft, fielen heimlich raus. Snackbar zeigt nur die nicht-null Teile an.',
    'API: BookImportItemDto + BookImportResultDto bekommen ein neues Feld `Invalid` / `TotalInvalid`. `PgnImportService.ParsePgn` liefert jetzt einen `ParseResult`-Record (Puzzles + Invalid) statt einer flachen Liste.',
  ]},
  { version: '0.55.1', date: '2026-06-02', changes: [
    'Log-Rauschen reduziert: erwartete DataProtection-Startup-Warnungen (Key-Ring im gemounteten /keys-Volume, ohne XML-Encryptor) werden nicht mehr bei jedem Neustart als Warning geloggt; die EF-Core-„FirstOrDefault ohne OrderBy"-Meldung der zufälligen Puzzle-Auswahl (deterministische Aggregat-/ID-Range-Query) ist auf Debug herabgestuft. Echte Fehler bleiben sichtbar.',
  ]},
  { version: '0.55.0', date: '2026-06-02', changes: [
    'Kibana-Logging-Dashboard erweitert: neue Panels „Logins per Day" (erfolgreiche Logins pro Tag) und „Unique Logins" (eindeutige User pro Zeitraum) neben den bestehenden Puzzle-Kennzahlen (Puzzles Solved, Puzzles per User).',
    'API: Erfolgreiche Logins erzeugen jetzt einen strukturierten Log-Event („UserLogin" mit UserId/UserName) — Grundlage für die Login- und Unique-Login-Auswertung in Kibana.',
  ]},
  { version: '0.54.0', date: '2026-06-02', changes: [
    'Endless offline: Beim Start eines Runs werden jetzt im Hintergrund passende Puzzles für einen ganzen Run vorab geladen und lokal gespeichert — so kann man auch ohne Internet weiterpuzzeln. Run-Größe = Maximum der gelösten Puzzles der letzten 5 Runs + 10 (ohne Historie: 30). Neuer Endpoint POST `/api/puzzles/random-batch` (ein Puzzle je Rating-Fenster, eindeutig).',
  ]},
  { version: '0.53.0', date: '2026-06-02', changes: [
    'Analysebrett: Pfeile/Kreise (Rechtsklick-Ziehen) funktionieren jetzt zuverlässig — Engine-Updates verwerfen die Zeichnung nicht mehr (Brett wird nur bei echten Stellungsänderungen neu gesetzt).',
    'Pfeile/Kreise jetzt auch auf den Puzzle-Brettern (Standard, Buch, Kurs, Wochenpost, Endless, Blind) per Rechtsklick-Ziehen.',
    'Analysemodus: Such-Tiefe manuell einstellbar (12–30, Standard 22, wird gespeichert); Anzeige „Tiefe erreicht/Max". Das „Linien"-Feld hat jetzt genug Platz.',
    'Analysemodus: „Zurück zum Puzzle"-Button, wenn man über „Analysieren"/„Letztes Puzzle ansehen" hergekommen ist.',
  ]},
  { version: '0.52.2', date: '2026-06-02', changes: [
    '„Letztes Puzzle ansehen" öffnet jetzt direkt den Analysemodus mit dem zuletzt gelösten Puzzle (Stellung + Zugfolge + Orientierung), statt das Puzzle erneut zum Lösen zu laden.',
  ]},
  { version: '0.52.1', date: '2026-06-02', changes: [
    'Menüpunkte „Repertoires" und „Wochenpost" sind vorerst nur für Admins sichtbar/erreichbar (Navigation ausgeblendet, Routen per adminGuard geschützt, Dashboard-Repertoire-Kachel nur für Admins). Die Lese-API der Wochenposts bleibt unverändert.',
  ]},
  { version: '0.52.0', date: '2026-06-02', changes: [
    'Statistik: Bei Level „Alle" wird die Elo-Kurve jetzt pro Visualisierungs-Modus getrennt als eigener Graph angezeigt (ein Graph je Modus, sofern dort mind. 2 Einträge vorliegen) — statt einer modus-übergreifenden Mischkurve. Einzelne Level zeigen weiterhin ihre eigene Kurve.',
  ]},
  { version: '0.51.0', date: '2026-06-02', changes: [
    'Neu: „Analysieren"-Button bei Puzzles (Standard- und Buch-/Kurs-/Wochenpost-Puzzles, im gelösten/aufgegebenen Zustand) — öffnet den Analysemodus mit der aktuellen Stellung und der kompletten Zugfolge (über `/analysis?fen=…&moves=…&orientation=…`). Dort dann Engine-Lines, eigenes Weiterziehen und Pfeile/Kreise (Rechtsklick-Ziehen). Der Analysemodus lädt eine übergebene Zugfolge ab der Startstellung und springt an die aktuelle Stellung.',
  ]},
  { version: '0.50.1', date: '2026-06-02', changes: [
    'Fix (Puzzle): „Aufgeben" wechselt jetzt auch im normalen Puzzle-Modus auf die Anfangsstellung und spielt die Lösung automatisch Zug für Zug durch (vorher verhielt es sich wie „Zurücksetzen"). Manuelles Vor-/Zurückklicken stoppt die Wiedergabe; „Nochmal"/„Nächstes" beendet sie. Analog zum bereits bestehenden Verhalten bei Buch-/Kurs-/Wochenpost-Puzzles.',
  ]},
  { version: '0.50.0', date: '2026-06-02', changes: [
    'Neu: RookHub-Konto mit Discord verknüpfen. Im Profil unter „Discord" wird ein verknüpftes Konto angezeigt + „Verknüpfung trennen". Verknüpft wird über einen vom Schach-Bot signierten Link (`/link`-Befehl bzw. Begrüßungs-DM): eingeloggt sofort, anonym wird der Link vorgemerkt und nach Login/Registrierung automatisch eingelöst. Neue Endpoints POST `/api/profile/discord/link` + DELETE `/api/profile/discord`; Discord-ID ist eindeutig (≤ 1 RookHub-User). Secret via `Discord__LinkSecret` (= Bot `ROOKHUB_LINK_SECRET`), leer → Feature inaktiv.',
  ]},
  { version: '0.49.0', date: '2026-06-02', changes: [
    'Statistik erweitert: Aufschlüsselung nach Thema (Trefferquote je Taktik-Thema), Rating-Verteilung der gelösten Puzzles (Balken je 200er-Rating-Band, Genauigkeit beim Überfahren) und eine Aktivitäts-Heatmap (Versuche pro Tag, letzte ~6 Monate). Neuer Endpoint GET `/api/puzzles/stats/breakdown`.',
  ]},
  { version: '0.48.0', date: '2026-06-02', changes: [
    'Neu (Statistik): Persönliche Statistikseite (Menü „Statistik", eingeloggt) — Puzzle-Elo-Verlauf als Kurve (pro Visualisierungs-Level filterbar), Kennzahlen (Elo, gelöst, Versuche, Genauigkeit, aktuelle/beste Serie), Elo je Level und eine Liste der zuletzt gespielten Puzzles (mit Δ-Elo, Zeit, Link zum Puzzle). Neuer Endpoint GET `/api/puzzles/elo-history`.',
  ]},
  { version: '0.47.1', date: '2026-06-02', changes: [
    'Footer: „Feedback / Bug melden"-Link zum GitHub-Issue-Tracker (github.com/kahalm/rookhub/issues).',
  ]},
  { version: '0.47.0', date: '2026-06-02', changes: [
    'Neu (Analyse): Eigenständiger Analysemodus (Menü „Analyse", öffentlich) à la Lichess — Brett mit freiem Ziehen beider Seiten, lokale Stockfish-Engine mit konfigurierbarer Anzahl Top-Lines (1–5, Standard 3) inkl. Eval und SAN-Zugfolge, Eval-Bar, beste Züge als Pfeile, eigene Pfeile/Kreise per Rechtsklick-Ziehen (chessground). Zugliste mit Durchklicken + Tastatur (←/→/Pos1/Ende), Brett drehen, FEN laden/kopieren, PGN laden. (Phase 1; Varianten-Baum & „Analysieren"-Button folgen.)',
  ]},
  { version: '0.46.2', date: '2026-06-02', changes: [
    'Buch-/Kurs-/Wochenpost-Puzzles: „Aufgeben" wechselt jetzt auf die Anfangsstellung und spielt die Lösung automatisch Zug für Zug durch (statt sie selbst spielen zu lassen). Manuelles Vor-/Zurückklicken stoppt die Wiedergabe; „Nochmal" startet das Puzzle neu.',
  ]},
  { version: '0.46.1', date: '2026-06-02', changes: [
    'Admin/Userliste: Neue Spalte „Gruppen" zeigt pro User die Gruppen-Mitgliedschaften (als Chips). `GET /api/admin/users` liefert die Gruppennamen mit.',
  ]},
  { version: '0.46.0', date: '2026-06-01', changes: [
    'Lokalisierung (i18n): Die gesamte Oberfläche ist jetzt mehrsprachig — Englisch (Standard), Deutsch und Kroatisch. Sprachumschalter (Globus-Icon) in der Navigationsleiste, Auswahl wird gespeichert (localStorage). Umgesetzt mit ngx-translate; alle Texte aller Bereiche (Navigation, Auth, Dashboard, Profil, Freunde, Repertoires, Turniere, Puzzles, Endlosmodus, Bücher/Kurse, Wochenpost, Admin, PGN-Viewer) laufen über Übersetzungs-Keys (`public/i18n/{en,de,hr}.json`). Fehlende Keys fallen auf Englisch zurück. (Backend-/API-Meldungen bleiben vorerst Englisch.)',
  ]},
  { version: '0.45.0', date: '2026-06-01', changes: [
    'Endlosmodus vereinfacht: Fasttrack ist jetzt der Standard (kein Toggle mehr) — die Schwierigkeit steigt immer entlang der Phasen. Die beiden Schwellen heißen jetzt „1st Threshold" / „2nd Threshold" (vorher „1st/2nd Mistake Rating"). Die „Step Size"-Einstellung ist entfallen; die Breite des Rating-Fensters bei der Puzzleauswahl ist intern fix 40. DB: Spalten Step + Fasttrack aus EndlessProgresses entfernt (Migration); FasttrackThreshold1/2 bleiben. Alte gespeicherte Sessions/Configs laden weiterhin problemlos.',
  ]},
  { version: '0.44.3', date: '2026-06-01', changes: [
    'Tests/Refactor: Test-Audit der neuen Features. Neue Tests für `courseAccessGuard` (Zugriffs-Gating: Login/Admin/Gruppe/Fehler → 5 Fälle) und die Wochenpost-Termin-Logik (letzter + 7 Tage / gleiche Uhrzeit / Default 19:00, inkl. Monatsübergang) — dafür wurde die Termin-Berechnung in reine, testbare Funktionen (`nextWeeklySlot`, `weeklyDatePart`, `weeklyTimePart`) ausgelagert. Auto-Subscription-Test um FideId-Änderung erweitert. Frontend 115, Backend 424 Tests grün.',
  ]},
  { version: '0.44.2', date: '2026-06-01', changes: [
    'Fix (Auto-Subscription / Crawler-Last): Ein Profil-Update stößt den Turnier-Crawler (`/api/players/tournaments`) jetzt nur noch an, wenn sich die Schach-Identität (ChessResultsId/LastName/FirstName/FideId) tatsächlich ändert. Vorher löste JEDER `PUT /api/profile` den Crawler-Lauf aus — also auch reine Einstellungs-Saves (Brett-Theme, Figuren, Stockfish-Tiefe, Schwierigkeit) aus dem PreferencesService, was zu sehr häufigen `gluetun:8080/api/players/tournaments`-Calls führte.',
  ]},
  { version: '0.44.1', date: '2026-06-01', changes: [
    'Härtung (Logging): Alle Log-Events innerhalb eines Requests tragen jetzt — sofern vorhanden — UserId, UserName und IpAddress (per LogContext-Enrichment in einer Middleware), nicht mehr nur die Request-Summary. Anonyme Requests bleiben ohne UserId/UserName („wenn vorhanden"). Feldnamen unverändert (UserId/UserName/IpAddress) — Kibana-Dashboards bleiben kompatibel.',
  ]},
  { version: '0.44.0', date: '2026-06-01', changes: [
    'Neu (Wochenpost durchspielen): „Durchspielen" öffnet jetzt eine Spiel-Seite (`/weekly/:id`) statt eines PGN-Dialogs — man löst die Puzzles des Wochenposts der Reihe nach (wie im Endlosmodus, aber ohne Leben und mit beliebig vielen Retrys). Das hochgeladene PGN wird serverseitig on-the-fly in Puzzles geparst (gleiche Logik wie Bücher); neuer öffentlicher Endpoint GET `/api/weekly-posts/{id}/puzzles`. Fortschritt (X/Y) + „Nächstes Puzzle" / „Überspringen" / „Zur Übersicht".',
  ]},
  { version: '0.43.0', date: '2026-06-01', changes: [
    'Neu (Wochenpost): Neuer öffentlicher Menüpunkt „Wochenpost" — bildet die wöchentlichen Schach-Posts auf RookHub ab. Admins laden ein PGN mit Termin (Datum + Uhrzeit) hoch; das Datum wird automatisch auf „letzter Eintrag + 7 Tage" vorbelegt, die Uhrzeit bleibt gleich (Standard 19:00). Jeder Post ist über den PGN-Viewer interaktiv durchklickbar; Liste/Anzeige sind öffentlich (auch ohne Login), Hochladen/Bearbeiten/Löschen nur für Admins. Daten liegen in der DB (neue Tabelle WeeklyPosts). Neue API: GET `/api/weekly-posts`(+`/{id}`), POST/PUT/DELETE `/api/admin/weekly-posts`.',
  ]},
  { version: '0.42.0', date: '2026-06-01', changes: [
    'Neu (Kurse · Gruppen-Berechtigung): Im Admin-Bereich lässt sich pro Buch festlegen, welche Gruppen es als Kurs sehen dürfen (Spalte „Sichtbar für (Kurse)" in der Bücher-Liste). Das „Kurse"-Menü und die Kurs-Übersicht sind jetzt nicht mehr admin-only: Mitglieder einer freigegebenen Gruppe sehen den Menüpunkt und genau die für sie freigegebenen Bücher (Admins weiterhin alle). Zugriff wird server- und routenseitig erzwungen. Freigaben liegen in der DB (neue Tabelle BookGroupAccess) und werden beim Löschen von Buch oder Gruppe mit aufgeräumt. Neue API: GET/PUT `/api/admin/books/{id}/groups`, GET `/api/courses/access`.',
  ]},
  { version: '0.41.0', date: '2026-06-01', changes: [
    'Neu (Kurse, admin-only): Neuer Menüpunkt „Kurse" zeigt alle als Bücher importierten Sammlungen als Übersicht mit Fortschrittsbalken (gelöste Puzzles / gesamt). Jedes Buch lässt sich in zwei Modi durcharbeiten — sequenziell (Buchreihenfolge, „Überspringen" springt weiter) oder zufällig. Der Fortschritt ist user-bezogen und wird komplett in der DB gespeichert (neue Tabellen CourseProgress + CoursePuzzleResult); ein Buch hat einen geteilten Fortschritt über beide Modi. Reset pro Kurs möglich. Neue API: GET `/api/courses`, GET `/api/courses/{bookId}/next`, POST `/api/courses/{bookId}/results`, POST `/api/courses/{bookId}/reset`.',
  ]},
  { version: '0.40.41', date: '2026-06-01', changes: [
    'Härtung (Crawler-Proxy): (1) `/api/tournaments/crawl` reicht nicht mehr den Roh-Body durch, sondern baut einen validierten Body (chessResultsId + jobType gegen Whitelist) — keine Injektion beliebiger/zukünftiger Felder. (2) Alle Proxy-Aufrufe reichen `HttpContext.RequestAborted` durch, sodass abgebrochene Client-Requests nicht am Crawler weiterlaufen. (3) `GET /api/book-puzzles/random` liefert bei einem Pool-Schrumpf zwischen Count und Skip jetzt 404 statt eines unbehandelten 500. (Code-Audit Findings, Projektübergreifend.)',
  ]},
  { version: '0.40.40', date: '2026-06-01', changes: [
    'Härtung (Freundschaften): Neuer richtungsunabhängiger Unique-Index auf dem ungeordneten Nutzerpaar (STORED computed columns LEAST/GREATEST). Gleichzeitige A→B- und B→A-Anfragen können jetzt keine zwei Zeilen mehr erzeugen — die Migration dedupliziert vorhandene Duplikate sicher vor dem Index-Aufbau. (Code-Audit Finding, DB-Migration.)',
  ]},
  { version: '0.40.39', date: '2026-06-01', changes: [
    'Fix (Random-Puzzle): Bei gesetzten Filtern (Rating/Themen/„gelöste ausblenden") wird die ID-Range jetzt über die *gefilterte* Treffermenge bestimmt statt global — vorher landete der Zufallspunkt fast immer außerhalb der Treffer, sodass der Fallback stets dasselbe Puzzle lieferte. Zusätzlich Wrap-around-Suche (vorwärts/rückwärts) statt „immer das erste". (Code-Audit Finding.)',
  ]},
  { version: '0.40.38', date: '2026-06-01', changes: [
    'Fix (Profil-Spielersuche): Bei je genau einem ChessResults- und FIDE-Treffer überschreibt der FIDE-Treffer nicht mehr die zum CR-Spieler gehörende FIDE-Id (Auto-Fill); manuelles Anklicken bleibt unverändert.',
    'Fix (Endless-Sync): Das „bereits migriert"-Flag ist jetzt pro Identität (User-Id bzw. anonyme Session) statt global — ein zweiter Account/Anon im selben Browser migriert seine lokalen Daten wieder; der alte globale Flag wird sicher übernommen (keine Doppel-Migration).',
    'Fix (Turnier-Detail): `reloadAll` leert jetzt auch die Anzeige-Arrays und lädt den aktiven Tab neu — die Teams/Paarungen-Tabelle zeigt nach einem Refresh keine veralteten Zeilen mehr, die dem Zähler widersprechen.',
    'Fix (Puzzle-Brett): Die Premove-Ausführung (setTimeout) wird bei Component-Destroy abgebrochen und emittiert nicht mehr auf eine zerstörte/zurückgesetzte Stellung.',
    '(Code-Audit Findings.)',
  ]},
  { version: '0.40.37', date: '2026-06-01', changes: [
    'Härtung (PGN-Upload): Die Inhaltsvalidierung verlangt jetzt ein echtes Tag-Pair (`[Event "…"]`) oder einen echten ersten Zug (`1. e4`/`1. Nf3`/`1. O-O`) statt nur die Teilstrings `[Event`/`1.` irgendwo — reiner Fließtext wie „Chapter 1. Intro" wird abgelehnt (ReDoS-sicheres Regex mit Timeout). (Code-Audit Finding.)',
  ]},
  { version: '0.40.36', date: '2026-06-01', changes: [
    'Fix (PGN-Parser): `stripVariations` erzeugt jetzt brace-balancierte Ausgabe (keine Streu-`}`, eine unterminierte `{`-Kommentarklammer wird am Ende geschlossen) — ein Spiel mit unbalancierten Klammern wird so geparst statt still verworfen. Der vormals leere `catch{}`-Block loggt übersprungene Spiele jetzt per `console.warn` (Diagnose). (Code-Audit Findings.)',
  ]},
  { version: '0.40.35', date: '2026-06-01', changes: [
    'Härtung/Doku: Admin-`DeleteUser` fängt eine DbUpdateException (verbliebene FK-Referenzen) ab und liefert 409 statt eines unbehandelten 500. CORS-Doku in der CLAUDE.md an den tatsächlichen Code angeglichen (ExtensionPolicy erlaubt nur chess.com, nicht `chrome-extension://*`/localhost). (Code-Audit Findings.)',
  ]},
  { version: '0.40.34', date: '2026-06-01', changes: [
    'Härtung (Turnier-Monitor): Aktivierung wird abgelehnt (502), wenn der Crawler keine DB-Id liefert — sonst pollte der Hintergrund-Monitor dauerhaft `/api/tournaments/0/rounds/check`. Der Monitor-Loop überspringt zudem defensiv Datensätze mit DbId ≤ 0 und reicht den CancellationToken an alle Crawler-Aufrufe weiter (sauberer Shutdown, kein Sprengen des 30s-Intervalls). (Code-Audit Findings.)',
  ]},
  { version: '0.40.33', date: '2026-06-01', changes: [
    'Fix (Puzzle-Timer): `abortSolver` räumt jetzt auch den Visualisierungs-Show-Timer auf (sonst feuerte sein Callback nach Teardown/Puzzlewechsel); `continueAfterSolve` (Endless) verwirft einen noch laufenden Auto-Advance-Timer, bevor es fortfährt (kein Doppel-Advance). (Code-Audit Findings.)',
  ]},
  { version: '0.40.32', date: '2026-06-01', changes: [
    'Hardening (API): (1) Gruppen-Anlage fängt jetzt eine DbUpdateException (paralleler Create mit gleichem Namen) ab und liefert 400 statt 500. (2) Passwort-Hashing nutzt einen expliziten BCrypt-Workfactor (12) statt des Library-Defaults (10) — für Registrierung, Admin-Seeder und den Timing-Dummy-Hash. (Code-Audit Findings.)',
  ]},
  { version: '0.40.31', date: '2026-06-01', changes: [
    'Repertoire-Upload (MEDIUM+LOW-Bündel): (1) Client-seitige Validierung — nur .pgn-Dateien bis 10 MB werden hochgeladen (Drag&Drop umging bisher den accept-Filter). (2) Datei-Input wird nach Auswahl zurückgesetzt, damit dieselbe Datei erneut wählbar ist. (3) Download zeigt bei Fehler eine Snackbar statt still zu scheitern. (Code-Audit Findings, repertoire-edit.component.spec.ts.)',
  ]},
  { version: '0.40.30', date: '2026-06-01', changes: [
    'Fix (PGN-Zugliste): Die Zugnummerierung nahm fix Weiß-zuerst an — Partien/Linien, die laut FEN mit Schwarz am Zug beginnen, wurden falsch nummeriert/gefärbt. Nummer und Seite werden jetzt pro Zug aus der FEN (`move.before`) abgeleitet; Schwarz-Start wird als „N… Zug" dargestellt. (Code-Audit Finding, move-list.component.spec.ts.)',
  ]},
  { version: '0.40.29', date: '2026-06-01', changes: [
    'Performance (PGN-Import): Beim Buch-Import wurden zur Duplikat-Erkennung ALLE LineIds aller Bücher in den Speicher geladen. Jetzt nur noch die Linien des aktuellen Buchs/der Datei (LineIds sind dateiprefix-eindeutig). Verhaltensgleich, deutlich weniger Speicher/DB-Last bei großem Bestand. (Code-Audit Finding.)',
  ]},
  { version: '0.40.28', date: '2026-06-01', changes: [
    'Robustness (Crawler, chessresults_crawler-Repo): VPN-Rotation reicht jetzt den CancellationToken durch (Shutdown kann abbrechen) und setzt den Rate-Limiter-Zeitstempel zurück, damit die erste Anfrage über die neue Verbindung den vollen Mindestabstand abwartet. (Code-Audit Finding.)',
  ]},
  { version: '0.40.27', date: '2026-06-01', changes: [
    'Security (Crawler, chessresults_crawler-Repo): Restliche SSRF-Host-Prüfungen von `EndsWith("chess-results.com")` (matchte auch „evilchess-results.com") auf exakten Host-Vergleich umgestellt; zusätzlich globales 5s-Regex-Timeout als ReDoS-Schutz für den untrusted HTML-Body. (Code-Audit Findings.)',
  ]},
  { version: '0.40.26', date: '2026-06-01', changes: [
    'Fix (Crawler, chessresults_crawler-Repo): Mannschaftspaarungen bekamen eine fortlaufende Zähler-Nummer statt der echten „Nr." aus der Tabelle — bei übersprungenen/sortierten Zeilen wich die MatchNumber ab. Nutzt jetzt die geparste echte Nummer. (Code-Audit Finding, HtmlParserServiceTests.)',
  ]},
  { version: '0.40.25', date: '2026-06-01', changes: [
    'Security (Crawler, chessresults_crawler-Repo): API-Key-Middleware prüfte offene Pfade per `StartsWith` — `/api/healthXYZ` o.ä. umging dadurch den API-Key. Jetzt exakter/segment-genauer Match (`/api/health`, `/api/health/ip`, `/swagger`, `/swagger/*`). Zusätzlich wird der Key-Vergleich über SHA-256 längensicher gemacht (keine Key-Längen-Leak über die Vergleichszeit). (Code-Audit Findings, ApiKeyMiddlewareTests.)',
  ]},
  { version: '0.40.24', date: '2026-06-01', changes: [
    'Hardening (API, MEDIUM-Bündel): (1) PGN-Upload hat jetzt ein RequestSizeLimit (~11 MB) → übergroße Bodies werden abgewiesen, bevor sie gepuffert werden. (2) Endless-Bulk-Import-Endpunkte (auth + anonym) mit RequestSizeLimit (2 MB) → großer Payload wird nicht erst komplett deserialisiert. (3) Registrierung gibt bei Username- UND E-Mail-Kollision dieselbe generische Meldung „Username or email already in use." zurück → kein Enumeration-Oracle mehr. (Code-Audit MEDIUM-Findings, AuthServiceTests.)',
  ]},
  { version: '0.40.23', date: '2026-06-01', changes: [
    'Fix (Repertoire-Upload): Beim Hochladen mehrerer PGN-Dateien wurde nach JEDEM einzelnen Erfolg ein voller Repertoire- + Kombi-PGN-Reload ausgelöst (N Reloads). Die Uploads werden jetzt gebündelt (forkJoin) und der Reload wird genau einmal nach Abschluss ausgelöst; Teilfehler verwerfen die erfolgreichen Uploads nicht mehr. (Code-Audit Finding, repertoire-edit.component.spec.ts.)',
  ]},
  { version: '0.40.22', date: '2026-06-01', changes: [
    'Fix (Crawler, chessresults_crawler-Repo): Der Duplikat-Crawl-Schutz war eine TOCTOU-Race (AnyAsync-Check + Insert nicht atomar). Neue EF-Migration: STORED Computed Column „ActiveKey" (= ChessResultsId nur für aktive Jobs, sonst NULL) mit Unique-Index erzwingt DB-seitig höchstens EINEN aktiven Crawl-Job pro Turnier; die Race wird als 409 abgefangen. (Code-Audit Finding.)',
  ]},
  { version: '0.40.21', date: '2026-06-01', changes: [
    'Security (Crawler, chessresults_crawler-Repo): Die Turnier-Fetches folgten Redirects automatisch ohne den finalen Host zu prüfen (SSRF — nur die erste Anfrage war abgesichert). Jeder Fetch validiert jetzt den End-Host exakt gegen chess-results.com (schließt auch „evilchess-results.com"/„…com.attacker.tld" aus). (Code-Audit Finding, CrawlerServiceSsrfTests.)',
  ]},
  { version: '0.40.20', date: '2026-06-01', changes: [
    'Fix (Endless-Highscore): Der Highscore-Sync war blindes Last-Writer-Wins — ein lokal niedrigerer Wert konnte einen auf einem anderen Gerät/Tab erreichten höheren Highscore überschreiben. Der Client merkt sich jetzt den höchsten bekannten Highscore (aus loadFromServer + Saves) und sendet nie einen niedrigeren. (Code-Audit Finding, endless-storage.service.spec.ts.)',
  ]},
  { version: '0.40.19', date: '2026-06-01', changes: [
    'Fix (Puzzle/Mouseslip): Im Stockfish-Fehlerpfad ist der Zustand zwar PLAYING, es wurde aber kein Gegnerzug gespielt — Mouseslip nahm trotzdem 2 Halbzüge zurück und löschte so einen gültigen Lösungszug mit. Es wird jetzt nur zurückgenommen, was wirklich gespielt wurde. (Code-Audit Finding, base-puzzle-solver.spec.ts.)',
  ]},
  { version: '0.40.18', date: '2026-06-01', changes: [
    'Security/UX (Auth): Das JWT-Ablaufdatum wurde nur einmal beim App-Start geprüft — eine abgelaufene Session galt clientseitig bis zum nächsten 401 als eingeloggt. Die Gültigkeit wird jetzt bei jedem Zugriff (isLoggedIn/token/currentUser/isAdmin) erneut geprüft und bei abgelaufenem Token automatisch ausgeloggt. (Code-Audit Finding, auth.service.spec.ts. Hinweis: JWT bleibt in localStorage — Umstieg auf HttpOnly-Cookie ist als größerer Umbau separat offen.)',
  ]},
  { version: '0.40.17', date: '2026-06-01', changes: [
    'Fix (Anti-DoS): Anonyme Puzzle-Versuche wurden pro Session unbegrenzt gespeichert (Tabellen-Bloat). Pro anonymer Session werden jetzt nur die neuesten 200 Versuche behalten (ältere werden beim Aufzeichnen getrimmt); die anonymen Endpunkte sind zusätzlich rate-limitiert. (Code-Audit Finding, AnonymousPuzzleTests.)',
  ]},
  { version: '0.40.16', date: '2026-06-01', changes: [
    'Fix (Stockfish): Der gemeinsame (root-weite) Stockfish-Service wurde beim Verlassen jedes Puzzle-Modus terminiert und riss laufende Suchen ab bzw. konnte einen TypeError im Timer auslösen (Zugriff auf den bereits beendeten Worker). Der Worker wird jetzt App-weit wiederverwendet (kein destroy() beim Komponenten-Teardown) und die Suche hält den Worker lokal fest, sodass ein paralleles Beenden keinen Fehler mehr wirft. (Code-Audit Findings.)',
  ]},
  { version: '0.40.15', date: '2026-06-01', changes: [
    'Fix (PGN-Viewer): Der PGN-Parser lief synchron ohne Größenlimit — ein sehr großes/kombiniertes PGN konnte den Browser-Tab einfrieren. Eingabe ist jetzt gedeckelt (≤2 MB), Anzahl Partien (≤500) und pathologisch große Einzelpartien werden übersprungen. (Code-Audit Finding, pgn-parser.spec.ts.)',
  ]},
  { version: '0.40.14', date: '2026-06-01', changes: [
    'Fix (HTTP-Retry): Der retry-Interceptor wiederholte fehlgeschlagene Requests (Status 0/502/503) für ALLE Methoden — ein automatischer Retry von POST/PUT/DELETE konnte doppelte Seiteneffekte erzeugen (z.B. doppelte Puzzle-Attempts/Anfragen). Es werden jetzt nur noch idempotente Methoden (GET/HEAD) erneut versucht. (Code-Audit Finding, retry.interceptor.spec.ts.)',
  ]},
  { version: '0.40.13', date: '2026-06-01', changes: [
    'Fix (Spielersuche): Ein einzelner ChessResults-Treffer ohne `name`-Feld ließ `GetProperty("name")` werfen, wodurch im catch die GESAMTE Trefferliste verworfen wurde (leere Suche). Solche Einträge werden jetzt übersprungen (TryGetProperty), der Rest bleibt erhalten. (Code-Audit Finding, PlayerSearchServiceTests.)',
  ]},
  { version: '0.40.12', date: '2026-06-01', changes: [
    'Fix (Endless-Sync): Zwei gleichzeitige Progress-Speicherungen (auth oder anonym) führten zu einer Unique-Constraint-Verletzung beim Insert → HTTP 500 und verlorenem Update. Bei einem solchen Insert-Race wird jetzt die parallel angelegte Zeile nachgeladen und das Update darauf angewendet. (Code-Audit Finding.)',
  ]},
  { version: '0.40.11', date: '2026-06-01', changes: [
    'Fix (Auto-Favoriten): Der Spielerabgleich verglich Nachnamen per Substring (`name.Contains`) und favorisierte dadurch falsche Spieler (z.B. „Ott" → „Ottenweller", „Scott"). Jetzt exakter Token-Vergleich auf das „Nachname, Vorname"-Format (Vorname per erstem Token); reiner Nachnamen-Match nur ab Länge ≥3. (Code-Audit Finding, AutoSubscriptionServiceTests.)',
  ]},
  { version: '0.40.10', date: '2026-06-01', changes: [
    'Security (Auth): Login führt jetzt immer einen BCrypt-Verify gegen einen Dummy-Hash aus, auch wenn der Username nicht existiert — verhindert Username-Enumeration über den Timing-Seitenkanal. Username-Vergleich bei Login & Registrierung ist jetzt case-insensitiv (passend zur DB-Collation); kollidierende Registrierungen liefern sauber 409 (inkl. DbUpdateException-Absicherung gegen Races) statt 500. (Code-Audit Findings, AuthServiceTests.)',
  ]},
  { version: '0.40.9', date: '2026-06-01', changes: [
    'Fix (Friends): Accept/Decline/Remove gaben bei fehlender Berechtigung `Forbid(message)` zurück — die Meldung wurde als Auth-Scheme interpretiert und führte zu HTTP 500 statt 403. Liefert jetzt sauber 403 mit Fehlermeldung. (Code-Audit Finding, FriendControllerTests.)',
  ]},
  { version: '0.40.8', date: '2026-06-01', changes: [
    'Security (Turnier-Proxy): Die anonym erreichbaren Turnier-GETs (öffentliche Turnierseite/Teilen) bleiben ohne Login nutzbar, sind jetzt aber per dedizierter Rate-Limit-Policy (60/min) gedrosselt — vorher konnten sie unauthentifiziert den dahinterliegenden Crawler (chess-results.com) ungebremst belasten (DoS). (Code-Audit Finding #5, TournamentProxyControllerTests.)',
  ]},
  { version: '0.40.7', date: '2026-06-01', changes: [
    'Security (Admin-Seeder): Der Seeder hat das Admin-Passwort bei JEDEM Start auf den ADMIN_PASSWORD-Wert zurückgesetzt und das Konto re-promotet — wer die Env-Config kannte, konnte so ein bestehendes Konto übernehmen/aussperren, und ein selbst geändertes Admin-Passwort wurde bei jedem Neustart überschrieben. Der Seeder legt den Admin jetzt nur noch an, wenn er fehlt, fasst bestehende Konten nicht mehr an und verweigert den Platzhalter „change_me". (Code-Audit Finding #4, AdminSeedTests.)',
  ]},
  { version: '0.40.6', date: '2026-06-01', changes: [
    'UX: Nach Give Up wird das Puzzle wieder von vorne aufgebaut, damit der Spieler die richtige Lösung selbst durchspielen kann (statt nur im Review-Modus durchklicken). Fehlversuch wird vorher als verloren registriert; keine Doppel-Aufzeichnung.',
    'UX: Brett-Beschriftung — Linien (a–h) jetzt rechtsbündig in der unteren rechten Ecke jeder Spalte (analog zu den Ziffern oben links), nicht mehr zentriert unter dem Brett. Override mit !important gegen die chessground-Defaults.',
  ]},
  { version: '0.40.5', date: '2026-06-01', changes: [
    'Fix: Share-Puzzle-Button war als absolut positionierter Icon-Button in der Mat-Card praktisch unsichtbar — jetzt ein klar erkennbarer "Puzzle teilen"-Block-Button am Ende der Puzzle-Info in allen 3 Modi.',
    'Fix: Brett-Beschriftung — chessground rendert Default-Coords mit top: -20px (Ränge zu hoch) und left: 24px (Linien zu weit rechts). Custom-Override zieht die Beschriftungen wieder mittig an den Rand der Eck-Squares.',
  ]},
  { version: '0.40.4', date: '2026-06-01', changes: [
    'Fix (Viz-Modus): Promotion-Dialog fehlte beim Bauern-Umwandlungszug — der Promotion-Check schaute auf das eingefrorene Brett, das nichts vom Bauer auf der 7. Reihe wusste. Erkennung läuft jetzt über die tatsächliche chess.js-Stellung (neuer actualFen-Input).',
    'Fix (Viz-Modus): Illegaler 2. Klick (z.B. a1 → c3 ohne legalen Zug) verwarf die Auswahl wirkungslos — jetzt wird das geklickte Feld zum neuen Ausgangsfeld, der Spieler verliert die Orientierung nicht mehr.',
    'Fix: Share-Puzzle-Button war versteckt (CSS top/right -8px wurde durch Material-Card-Clipping abgeschnitten) und im Endless-Modus durch die Stats-Card nach unten verdrängt. Position auf 0/0 korrigiert, im Endless-Modus über die Stats-Card gezogen.',
    'Feature: Share-Puzzle jetzt auch im Buch-Puzzle-Modus (war bisher nicht vorhanden).',
  ]},
  { version: '0.40.3', date: '2026-06-01', changes: [
    'Fix: Nach Mouseslip wurde der korrekte Lösungszug nicht mehr erkannt — onSolutionPath blieb auf false und der State auf PLAYING, dadurch landete jeder folgende User-Zug im off-path-Handler. Mouseslip stellt jetzt den Lösungspfad sauber wieder her (state=AWAITING_USER_MOVE, Fehlzug aus dem Move-Log entfernt).',
    'Fix: Race-Condition bei Reset/Mouseslip während Stockfish noch denkt — ein verspätet ankommender Solver-Zug konnte das frisch zurückgesetzte Brett verschmutzen. Eine Solver-Epoch invalidiert jetzt laufende Stockfish-Aufrufe, der späte Zug wird verworfen.',
  ]},
  { version: '0.40.2', date: '2026-06-01', changes: [
    'Fix: Mehrzügige Puzzles beendeten nach korrekter Lösung nicht mehr — der zweite/letzte Lösungszug wurde fälschlich als off-path-Zug behandelt, weil der Solver nach der Stockfish-Antwort auf PLAYING statt AWAITING_USER_MOVE wechselte (Regression aus dem BasePuzzleSolver-Refactor v0.38.2).',
  ]},
  { version: '0.40.1', date: '2026-06-01', changes: [
    'Fix (Infra): nginx-Frontend stuerzte beim Start ab ("host not found in upstream api"), wenn der api-Container nach einem Reboot noch nicht im DNS war → 502 auf der ganzen Seite. Upstream wird jetzt zur Laufzeit ueber Dockers DNS aufgeloest (resolver + Variable im proxy_pass), nginx startet dadurch zuverlaessig und faengt api-Neustarts (neue IP) automatisch ab.',
  ]},
  { version: '0.40.0', date: '2026-05-31', changes: [
    'Feature: Per-Visualisierungslevel Elo-Ratings — jede Stufe (0-4) hat eigenes Puzzle-Elo. Default: 1500/1400/1300/1200/1100. Level-Wechsel im Slider laedt Stats fuer das gewaehlte Level.',
  ]},
  { version: '0.39.2', date: '2026-05-31', changes: [
    'UI: Countdown 3s statt 5s bei Level 2-4. Show-Button zeigt Figuren fuer 3 Sekunden (Klick statt Halten).',
  ]},
  { version: '0.39.1', date: '2026-05-31', changes: [
    'UI: Viz-Card im Desktop-Modus rechts neben dem Brett (info-section), auf Mobile darunter (einspaltiges Layout).',
  ]},
  { version: '0.39.0', date: '2026-05-31', changes: [
    'Feature: Visualisierung 5-Stufen-Slider (Level 0-4) — Normal, Blindfold, Checker (farbige Spielsteine), Dark Checker (schwarze Steine), Invisible (komplett unsichtbar). Bei Level 2-4 verschwinden Figuren nach 5s Countdown; Show-Button blendet sie temporaer ein. Nach Puzzle-Ende werden Figuren normal angezeigt.',
    'UI: Viz-Card (Zugliste + Show-Button + Countdown) direkt unter dem Brett fuer bessere Mobile-Ansicht.',
  ]},
  { version: '0.38.3', date: '2026-05-31', changes: [
    'Fix: Mouseslip im Visualisierungs-Modus — zurückgenommene Züge werden jetzt auch aus der SAN-Zugliste entfernt (vorher schien Mouseslip wirkungslos, da das Brett eingefroren bleibt).',
  ]},
  { version: '0.38.2', date: '2026-05-31', changes: [
    'Refactor: Gemeinsamer Lös-Automat der 3 Puzzle-Modi (Setup, Zug-Handling, Stockfish-Übernahme, Mouseslip, Brett-Präsentation, Visualisierung) in BasePuzzleSolver zusammengefasst — vorher ~3x dupliziert (~750 Zeilen weniger in den Komponenten). Modus-Unterschiede laufen über Hooks. Verhaltensgleich, leichter wartbar.',
  ]},
  { version: '0.38.1', date: '2026-05-31', changes: [
    'Refactor: Gemeinsame Schach-/Brett-Helfer (parseUci, applyUci, tryFreeMove, calcDests, SAN-Zugliste) aus den 3 Puzzle-Modi in puzzle-move.util.ts ausgelagert (vorher byte-gleich 3x dupliziert) — leichter wartbar.',
  ]},
  { version: '0.38.0', date: '2026-05-31', changes: [
    'Feature: Visualisierungs-Modus (Blindfold) in allen 3 Puzzle-Modi (Normal, Endless, Buch) — als Schalter in den Einstellungen. Das Brett bleibt auf der Startstellung; eigene Züge werden per Klick (Von-Feld → Ziel-Feld) eingegeben, aber nicht angezeigt; Gegner-/Lösungszug erscheint als SAN-Text. Trainiert das Rechnen/Visualisieren im Kopf.',
  ]},
  { version: '0.37.2', date: '2026-05-31', changes: [
    'Feature (Schach-Bot): /puzzle zeigt standardmaessig nur Link (kein Brettbild/Loesung). Jeder User kann mit /puzzle option:showBoard bzw. hideBoard umschalten.',
  ]},
  { version: '0.37.1', date: '2026-05-31', changes: [
    'Feature: Loesungs-Review sofort nach Puzzle-Ende — Pfeiltasten (links/rechts) und GUI-Buttons zum Durchklicken der Loesung ohne vorheriges "Show Solution".',
    'Feature: "Ganze Partie ansehen" erst sichtbar wenn Puzzle fertig (SOLVED/FAILED).',
  ]},
  { version: '0.37.0', date: '2026-05-31', changes: [
    'Feature: Theme-Modus — Normal (wie bisher), Random (zufaelliges Brett-Theme + Figurensatz pro Puzzle), Crazy (jedes Feld eine andere Farbe, jede Figurenart ein anderer Satz).',
    'Feature: Modus-Auswahl als Chips in den Einstellungen aller 3 Puzzle-Modi (Normal, Endless, Buch). Board-/Figuren-Picker nur bei Normal sichtbar.',
    'Refactor: BOARD_THEMES und PIECE_SETS in gemeinsame board-theme.util.ts ausgelagert (vorher 3x dupliziert).',
  ]},
  { version: '0.36.1', date: '2026-05-31', changes: [
    'Fix: Bauernumwandlungs-Auswahl zeigt jetzt die korrekten Figuren — cburnett-SVGs komplett lokal gevendort (vorher externer GitHub-Request, der fehlschlug und alle 4 Optionen leer/gleich aussehen liess).',
  ]},
  { version: '0.36.0', date: '2026-05-31', changes: [
    'Feature: Loesungs-Review nach Puzzle-Ende — "Show Solution" oeffnet Step-Through statt Auto-Play in allen 3 Puzzle-Modi (Normal, Endless, Buch).',
    'Feature: Pfeil-Buttons und Pfeiltasten (links/rechts) zum manuellen Durchsteppen der Loesung.',
    'Feature: Buch-Puzzle unterscheidet "Show Solution" (nur Loesungszuege ab Trainingsstart) und "Ganze Partie ansehen" (alle Zuege).',
  ]},
  { version: '0.35.3', date: '2026-05-31', changes: [
    'Fix: Buch-Puzzles mit FEN = Puzzle-Stellung (z.B. „1001 Chess Exercises") starten jetzt korrekt beim ersten Lösungszug (StartPly=-1) statt den ersten Zug als „Setup" wegzuspielen.',
    'Fix: Ganze-Partie-Einträge ohne Trainingsmarker (FEN = Grundstellung, kein [%tqu]) werden beim Import übersprungen (kein scheinbares Puzzle ab Eröffnung).',
  ]},
  { version: '0.35.2', date: '2026-05-31', changes: [
    'Fix: Nach Retry/Reset/Mouseslip wurde faelschlicherweise "Alternative Loesung" angezeigt — alternativeSolve wird jetzt in setupPuzzle zurueckgesetzt (alle 3 Puzzle-Modi).',
  ]},
  { version: '0.35.1', date: '2026-05-31', changes: [
    'Fix: Buch-Puzzles starten am Trainingskommentar — ChessBase-[%tqu]-Marker wird beim Import erkannt (neues Feld StartPly); das Brett spult bis zur Trainingsstellung vor und löst ab dem markierten Zug (vorher wurde die ganze Eröffnung ab Grundstellung „gelöst").',
    'Feature: „Ganze Partie ansehen" im Buch-Puzzle — komplette Partie per ◀/▶ durchklickbar; Vorgeschichte bleibt erhalten.',
  ]},
  { version: '0.35.0', date: '2026-05-31', changes: [
    'Feature: User-Preferences Server-Sync — Board Theme, Piece Set, Stockfish Depth, Schwierigkeit und Book Stockfish Depth werden serverseitig gespeichert und geraeteuebergreifend synchronisiert.',
    'Feature: Neuer PreferencesService — zentraler Service fuer alle Puzzle-Einstellungen mit localStorage + Server-Persistierung.',
    'API: GET/PUT /api/profile liefert/akzeptiert jetzt 5 neue Preference-Felder (boardTheme, pieceSet, stockfishDepth, puzzleDifficulty, bookStockfishDepth).',
    'Sync: Nach Login werden Server-Preferences automatisch geladen und ueberschreiben lokale Werte.',
    'Fix: cburnett-Figurenvorschau lokal gevendort — kein externer GitHub-Request mehr.',
  ]},
  { version: '0.34.1', date: '2026-05-31', changes: [
    'Fix: Version + Changelog aus gemeinsamer Quelle (changelog.ts) — der Production-Build zeigt jetzt die korrekte Version/Changelog (environment.prod.ts war seit v0.26.0 auf 0.26.1 eingefroren).',
  ]},
  { version: '0.34.0', date: '2026-05-31', changes: [
    'Feature: Gruppensystem — Admin kann Gruppen anlegen/löschen und User zuordnen (Admin-Tab „Gruppen"). Basis für künftige gruppenabhängige Anzeige (GET /api/my-groups).',
  ]},
  { version: '0.33.0', date: '2026-05-31', changes: [
    'Feature: Admin-Bücher — pro Buch eine empfohlene Elo-Spanne (von/bis) angeben (Book.MinElo/MaxElo, Migration, Eingabe im Bücher-Tab).',
  ]},
  { version: '0.32.1', date: '2026-05-31', changes: [
    'UI: Dashboard — Puzzles-Karte zuerst (mit zusätzlichem Endless-Link), dann Tournaments, Friends, Repertoires.',
  ]},
  { version: '0.32.0', date: '2026-05-31', changes: [
    'UI: Einstellungs-Zahnrad sitzt in der Status-Box (oben rechts); beim Öffnen scrollt die Seite (mobil) zum Einstellungsblock.',
  ]},
  { version: '0.31.2', date: '2026-05-31', changes: [
    'UI: Einstellungen-Zahnrad als kompaktes Icon oben rechts im Info-Panel (statt Button-Zeile).',
  ]},
  { version: '0.31.1', date: '2026-05-31', changes: [
    'Fix: Filter-Felder Abstand — Hint-Text ueberlappt nicht mehr mit dem naechsten Feld',
  ]},
  { version: '0.31.0', date: '2026-05-31', changes: [
    'UI: Brett-/Figuren-Auswahl-Chips zeigen die echte Textur bzw. Figur als Vorschau.',
    'UI: Filter- und Darstellungs-Einstellungen (Schwierigkeit, Tiefe, Brett, Figuren) hinter ein Zahnrad/„Einstellungen"-Toggle gelegt (Default eingeklappt) — in Normal-, Endless- und Buch-Puzzle.',
  ]},
  { version: '0.30.0', date: '2026-05-31', changes: [
    'Feature: Figuren-Auswahl mit Mini-Vorschau (Springer) im Chip; zusätzliche Sets Celtic, Chessnut, RhosGFX (MIT/Apache/CC0).',
    'Feature: Zusätzliche Brett-Texturen Leder und Ahorn.',
  ]},
  { version: '0.29.0', date: '2026-05-31', changes: [
    'Feature: Figuren-Sets wählbar — Classic (cburnett), Merida, Fantasy, Spatial (lokal gevendort, MIT/GPL).',
    'Feature: Neue Brett-Texturen — Holz, Wasser, Marmor, Metall (echte Lichess-Texturen).',
    'Auswahl für Figuren + Brett in allen Puzzle-Modi (Normal, Endless, Buch), in localStorage gemerkt.',
  ]},
  { version: '0.28.2', date: '2026-05-31', changes: [
    'Fix: Buch-Puzzle-Import (Legacy-JSON) kürzt BookFileName auf 200 Zeichen und überspringt leere Dateinamen — verhindert DB-Fehler + Geister-Bücher (Code-Review).',
    'Fix: Schwierigkeits-Dropdown — erstes Puzzle erst nach Laden der Elo (nicht mehr Default 1500); Rating-Fenster wird auf den echten DB-Rating-Bereich geklemmt (keine leeren Ergebnisse/Retry-Schleife); ungültiger localStorage-Wert wird ignoriert (Code-Review).',
  ]},
  { version: '0.28.1', date: '2026-05-31', changes: [
    'Fix: Board Themes — Schachbrett korrekt als 8x8 statt 4 Quadranten gerendert (background-size: 25%)',
    'Fix: Board Theme Kontrast erhoeht — Blue, Green, Gray, Wood mit deutlich unterscheidbaren Feldfarben',
  ]},
  { version: '0.28.0', date: '2026-05-31', changes: [
    'Feature: Schwierigkeits-Dropdown bei den Puzzles — liefert Puzzles rund um die eigene Elo: Normal (±100), Leicht (−300), Sehr leicht (−600), Schwer (+300), Sehr schwer (+600).',
    'Feature: Rating-Fenster wird aus eigener Puzzle-Elo + Schwierigkeits-Offset berechnet (ersetzt die manuellen Min/Max-Rating-Felder); Auswahl wird in localStorage gemerkt.',
  ]},
  { version: '0.27.1', date: '2026-05-31', changes: [
    'Fix: Legacy-Buch-Puzzle-Import (POST /api/admin/book-puzzles/import) legt jetzt pro Datei ein Book an und setzt BookId — so erscheinen auch per Skript importierte Puzzles in den Pools (random/daily/blind) und im Admin-Bücher-Tab (Code-Review).',
  ]},
  { version: '0.27.0', date: '2026-05-31', changes: [
    'Feature: Puzzle Elo Rating System — dynamische Elo-Zahl basierend auf Puzzle-Schwierigkeit',
    'Feature: K-Faktor 40 fuer erste 30 Versuche (provisorisch), danach K=20',
    'Feature: Elo-Aenderung (+N gruen / -N rot) nach jedem Puzzle angezeigt',
    'Feature: Elo in Stats-Grid und Dashboard-Card sichtbar',
    'API: PuzzleElo auf AppUser, EloAfter/EloChange auf PuzzleAttempt',
    'Tests: 10 neue Tests fuer Elo-Berechnung, K-Faktor, Floor, Stats, History',
  ]},
  { version: '0.26.2', date: '2026-05-31', changes: [
    'Fix: Swagger-UI immer aktiv (nicht mehr nur in Development) — die API ist nur im internen Netz erreichbar.',
  ]},
  { version: '0.26.1', date: '2026-05-31', changes: [
    'Feature: Bauernumwandlungs-Dialog — bei Promotion erscheint ein Overlay mit 4 Figuren (Dame, Turm, Laeufer, Springer) statt automatischer Dame-Wahl',
    'Feature: Board Themes — 5 Brett-Farbschemata: Brown (Standard), Blue, Green, Gray, Wood',
    'Feature: Theme-Auswahl in allen Puzzle-Modi (Normal, Endless, Book) mit Farbvorschau-Chips',
    'Feature: Board-Theme wird in localStorage gespeichert und bleibt nach Refresh erhalten',
    'Fix: Promotion-Zuege werden korrekt mit UCI-Notation verglichen (inkl. Promotion-Buchstabe)',
  ]},
  { version: '0.26.0', date: '2026-05-30', changes: [
    'Feature: Buch-Puzzles als RookHub-Feature — Admin lädt PGN-Dateien als "Bücher" hoch (serverseitiges Parsing, SAN→UCI via Gera.Chess).',
    'Feature: Admin-Tab "Bücher" — Liste aller importierten Bücher mit Puzzle-Count und Pool-Schaltern Daily/Random/Blind pro Buch.',
    'API: GET /api/book-puzzles/random?pool=daily|random|blind&exclude=… — zufälliges Buch-Puzzle aus dem Pool; daily ist deterministisch pro UTC-Tag (gemeinsames Tagespuzzle).',
    'API: POST /api/admin/books/import (PGN-Upload), GET/PUT/DELETE /api/admin/books — Buch-Verwaltung inkl. Pool-Flags.',
    'DB: Neue Book-Entität (Flags ForDaily/ForRandom/ForBlind + Metadaten), BookPuzzle.BookId; Migration mit Backfill für Altbestand.',
    'Tests: 27 neue Tests (PGN-Parser inkl. Promotion/Rochade/Varianten/Skip-Regeln, random-Pool-Auswahl, Buch-Verwaltung).',
  ]},
  { version: '0.25.5', date: '2026-05-30', changes: [
    'UI: Mobile-Optimierung — Padding, Wrapping und Layout fuer alle Views angepasst',
    'UI: Endless History — Card-Layout statt Tabelle auf Mobilgeraeten',
    'UI: Friends — Suchfeld und Button stacken korrekt auf Mobile',
    'UI: Profile — Formularfelder stacken auf Mobile, Suchbutton volle Breite',
    'UI: Endless Puzzle — Config/GameOver/Help-Overlay mobile-optimiert',
    'Logging: Bildschirmaufloesung (ScreenWidth/ScreenHeight) wird bei Puzzle-Attempts nach Elasticsearch geloggt',
  ]},
  { version: '0.25.4', date: '2026-05-30', changes: [
    'Feature: Endless Run Resume — bei Seiten-Refresh wird ein laufender Run mit Continue-Button angeboten',
    'Feature: Archive & New Game — laufenden Run archivieren und neu starten',
    'Feature: Archive-Button auf Game-Over/Exhausted-Screen fuer sofortige Archivierung',
    'API: recordSessionToServer gibt jetzt Session-ID zurueck',
  ]},
  { version: '0.25.3', date: '2026-05-30', changes: [
    'Fix: Data Protection persistiert Keys auf gemountetes Volume (/keys, eigenes dataprotection-keys-Volume) statt In-Memory — keine "No XML encryptor / ephemeral key"-Warnings mehr beim API-Start.',
    'Fix: compose.dev.vpn.yml — API wartet auf gluetun (service_healthy); ES/Kibana Host-Port-Defaults 9201/5602 statt 9200/5601 (keine Kollision mit Prod-Stack auf demselben Host).',
    'Fix: .env.dev.vpn.example neu + .gitignore — dev-eigene Ports und getrennte Bind-Pfade (verhindert geteiltes ES-Datenverzeichnis/node.lock).',
    'Fix: Crawler-Dockerfile — redundantes ASPNETCORE_URLS entfernt (Bindung via Base-Image HTTP_PORTS=8080); entfernt "Overriding HTTP_PORTS"-Warning.',
  ]},
  { version: '0.25.2', date: '2026-05-30', changes: [
    'UI: Puzzle History im User-Menue (Profilbild oben rechts) verlinkt',
  ]},
  { version: '0.25.1', date: '2026-05-30', changes: [
    'Feature: Endless Session Archivierung — Sessions koennen archiviert/unarchiviert werden',
    'Feature: Archivierte Sessions fliessen nicht mehr in Sync-Response (Fasttrack/Session-Count)',
    'Feature: History-Filter: Alle / Aktive / Archivierte Sessions',
    'Feature: Mehrfachauswahl mit Checkboxen in der History-Tabelle',
    'API: Neuer Endpoint POST /api/endless/archive mit Bulk-Archivierung',
    'API: GET /api/endless/history unterstuetzt neuen Query-Parameter archived (bool)',
    'Tests: 5 neue Tests fuer Archive/Unarchive/Filter/Sync-Exclusion',
  ]},
  { version: '0.25.0', date: '2026-05-30', changes: [
    'Feature: Endless Puzzle History — paginierte Uebersicht aller vergangenen Sessions',
    'Feature: Authentifizierte User haben kein 50-Session-Limit mehr (unbegrenzte History)',
    'API: Neuer Endpoint GET /api/endless/history mit Pagination (page, pageSize)',
    'Tests: 5 neue Tests fuer History-Pagination und Trim-Verhalten',
  ]},
  { version: '0.24.1', date: '2026-05-30', changes: [
    'Observability: Puzzle-Attempts als strukturierte Serilog-Events nach Elasticsearch',
    'Kibana: 2 neue Dashboard-Panels (Puzzles Solved 24h, Puzzles per User)',
  ]},
  { version: '0.24.0', date: '2026-05-30', changes: [
    'Feature: Puzzle MoveLog — gespielte Zuege, erwartete Zuege und Denkzeit pro Zug werden getrackt',
    'Feature: Endless Mode sendet jetzt korrekte Puzzle-Dauer (timeSpentSeconds) statt 0',
    'API: MoveLog-Feld (JSON) auf PuzzleAttempt, in RecordAttempt und History-Endpoints',
  ]},
  { version: '0.23.3', date: '2026-05-30', changes: [
    'Fix: init-kibana.sh legt Data Views mit allowNoIndex:true an — Erstellung jetzt timing-unabhaengig (funktioniert auch, wenn der Log-Index beim init-Lauf noch nicht existiert). Behebt leeres Kibana (keine Data Views/Dashboard) bei frischem Stack-Start.',
    'Fix: kibana-init als Idle-Sidecar (restart: unless-stopped, idlet nach dem Init) statt One-Shot — Stack zeigt in Arcane/Portainer nicht mehr "partially running". Init laeuft idempotent bei jedem Stack-Start.',
  ]},
  { version: '0.23.2', date: '2026-05-29', changes: [
    'Fix: nginx PID-Pfad auf /tmp/nginx.pid geaendert (non-root Container Fix)',
  ]},
  { version: '0.23.1', date: '2026-05-29', changes: [
    'Fix: FriendController Search-Minimum von 3 auf 2 Zeichen (Frontend/API-Konsistenz)',
    'Fix: Email wird bei Registrierung normalisiert (lowercase, getrimmt)',
    'Fix: LIKE-Wildcards (%, _) in Friend-Suche bereinigt',
    'Fix: JWT base64url-Dekodierung in AuthService korrigiert',
    'Perf: RepertoireService.UpdateAsync — unnoetige Include(Files) durch CountAsync ersetzt',
  ]},
  { version: '0.23.0', date: '2026-05-29', changes: [
    'Security: SessionId-Validierung per Regex in EndlessController + DTOs (wie PuzzleController)',
    'Security: Repertoire-Limits — max 50 pro User, max 100 Files pro Repertoire',
    'Security: nginx laeuft als non-root User (Port 8080 intern)',
    'Security: init-db.sh — GRANT ALL durch spezifische Privileges ersetzt',
  ]},
  { version: '0.22.9', date: '2026-05-29', changes: [
    'Perf: TournamentDetail — Template-Getter durch gecachte Properties ersetzt (kein Array-Rebuild pro Change Detection)',
  ]},
  { version: '0.22.8', date: '2026-05-29', changes: [
    'Fix: Fehlende Error-Handler auf HTTP-Calls in RepertoireList, TournamentDetail — SnackBar-Feedback bei Fehlern',
  ]},
  { version: '0.22.7', date: '2026-05-29', changes: [
    'Fix: Puzzle-Laden zeigt ERROR-State mit Retry-Button statt endlosem Spinner',
  ]},
  { version: '0.22.6', date: '2026-05-29', changes: [
    'Security: GetCrawlerIp erfordert jetzt Authentifizierung (kein AllowAnonymous mehr)',
    'Security: ES+Kibana Ports in Production-Compose nicht mehr exponiert',
    'Cleanup: Irrelevanter CORS-Kommentar zu chrome-extension entfernt',
  ]},
  { version: '0.22.5', date: '2026-05-29', changes: [
    'Fix: CrawlerExceptionFilter faengt TaskCanceledException — Timeout ergibt 504 statt 500',
    'Fix: CrawlerProxyService — leere Response-Body sicher behandelt, CancellationToken durchgereicht',
  ]},
  { version: '0.22.4', date: '2026-05-29', changes: [
    'Security: ActiveGameState MaxLength 1MB im DTO (verhindert unbegrenztes Schreiben)',
    'Security: BookPuzzle Import — RequestSizeLimit 50MB + max 10000 Puzzles pro Import',
  ]},
  { version: '0.22.3', date: '2026-05-29', changes: [
    'Fix: FriendService Race Condition — Single SaveChanges + DbUpdateException Handling bei Re-Request nach Decline',
  ]},
  { version: '0.22.2', date: '2026-05-29', changes: [
    'Security: compose.dev.vpn.yml — Required-Variable-Checks fuer kritische Secrets',
  ]},
  { version: '0.22.1', date: '2026-05-29', changes: [
    'Security: Open Redirect in Login/Register behoben — returnUrl wird gegen Whitelist validiert',
  ]},
  { version: '0.22.0', date: '2026-05-29', changes: [
    'Endless Mode: Give Up zeigt "Gave Up" mit Flag-Icon statt "Wrong" (orange statt rot)',
    'Endless Mode: Show Eval und Give Up vor dem ersten Zug verfuegbar',
    'Infra: Kibana Logging-Dashboard wird automatisch erstellt (6 Panels: Volume, Levels, Apps, Errors, Timeline, Log-Tabelle)',
    'Fix: compose.dev.vpn.yml — kibana-init + Healthcheck + Service-Name korrigiert',
    'Fix: CRAWLER_API_KEY in .env.dev ergaenzt',
  ]},
  { version: '0.21.2', date: '2026-05-29', changes: [
    'Infra: Kibana Data Views werden beim Start automatisch erstellt (rookhub-logs, crawler-logs, alle-logs)',
    'Infra: kibana-init One-Shot-Container in beiden Docker-Compose-Files',
  ]},
  { version: '0.21.1', date: '2026-05-29', changes: [
    'Fix: Migration DropRequestLogs bereinigt — duplizierte Schema-Operationen entfernt die API-Start verhinderten',
  ]},
  { version: '0.21.0', date: '2026-05-29', changes: [
    'Logging: Elasticsearch + Kibana fuer zentrales Log-Management (ersetzt DB-Logging)',
    'Logging: Serilog mit Elasticsearch-Sink fuer strukturiertes Logging in beiden Projekten',
    'Cleanup: RequestLog DB-Tabellen, Middleware und Controller entfernt (RookHub + Crawler)',
    'Cleanup: LogRetentionService und CrawlRequestLog im Crawler entfernt',
    'Infra: ES 8.17 + Kibana 8.17 in allen Docker-Compose-Files',
  ]},
  { version: '0.20.1', date: '2026-05-28', changes: [
    'Fix: Stockfish-Timeout bei hoher Suchtiefe zeigt nicht mehr sofort "Incorrect" — User kann weiterspielen',
    'E2E Test: Neuer Testfall fuer Stockfish-Timeout-Szenario mit komplexer Stellung',
    'E2E: squareCenter/makeMove/dragMove unterstuetzen jetzt Black-Orientation',
  ]},
  { version: '0.20.0', date: '2026-05-28', changes: [
    'Feature: Server-Side Endless Puzzle Progress Sync — Config, Highscore und Active Game werden serverseitig gespeichert',
    'Feature: Endless Session History wird auf dem Server persistiert (max 50 Sessions)',
    'Feature: Anonyme und eingeloggte User koennen nahtlos auf anderen Geraeten weiterspielen',
    'Feature: Claim-Session uebertraegt anonyme Endless-Daten bei Login auf den Account',
    'Feature: Bulk-Import fuer localStorage-Migration (einmalig beim ersten Server-Sync)',
    'API: Neue Endpoints GET/PUT /api/endless/progress, POST /api/endless/sessions, POST /api/endless/claim-session',
    'API: Anonyme Varianten mit Rate-Limiting (anonymous-puzzle Policy)',
    'Tests: 19 neue Tests fuer EndlessProgressService (Progress, Sessions, Claim, BulkImport)',
  ]},
  { version: '0.19.4', date: '2026-05-28', changes: [
    'Refactor: UnitTest1.cs aufgeteilt in AuthServiceTests, ProfileServiceTests, FriendServiceTests, RepertoireServiceTests',
    'Refactor: TeamPlayersDialogComponent in eigene Datei extrahiert',
    'Refactor: Endless-Puzzle localStorage-Logik in EndlessStorageService ausgelagert',
    'Feature: Retry-Interceptor fuer 502/503/0 Fehler (1x Retry nach 1s)',
    'Feature: environment.prod.ts + Angular fileReplacements (kein sed im Dockerfile mehr)',
    'Crawler: RoundDetectionService mit IMemoryCache (60s pro Tournament)',
    'Crawler: CancellationToken durch alle Crawl-Methoden durchgereicht',
    'CI: Neuer Test-Workflow (.github/workflows/test.yml) fuer dotnet test + ng build',
  ]},
  { version: '0.19.3', date: '2026-05-28', changes: [
    'Code-Review: Crawler GetAllTournaments mit Pagination (page/pageSize) statt unbegrenzter Liste',
    'Code-Review: LogRetentionService fuer RookHub API und Crawler — loescht Request-Logs aelter als 30 Tage',
    'Code-Review: Frontend Tournament-Liste nutzt paginierte API-Response',
  ]},
  { version: '0.19.2', date: '2026-05-28', changes: [
    'Fix: E2E Test puzzle-moves.spec.ts erwartete deutschen Text statt englischem UI-Text',
  ]},
  { version: '0.19.1', date: '2026-05-28', changes: [
    'Crawler: Outgoing Request Logging — alle HTTP-Calls zu chess-results.com werden in DB persistiert (CrawlRequestLog)',
    'Crawler: Neuer Endpoint GET /api/crawl-request-logs mit Filter (URL, Status, Zeitraum, Success) und Pagination',
    'Crawler: Response-Body optional abrufbar (includeBody Parameter)',
  ]},
  { version: '0.19.0', date: '2026-05-28', changes: [
    'Anonyme Puzzle-Stats: Puzzle-Attempts werden auch ohne Login getrackt (localStorage SessionId)',
    'Anonyme Stats: Accuracy, Streak und Best Streak fuer nicht eingeloggte User',
    'Session-Claim: Bei Login/Register werden anonyme Puzzle-Daten automatisch auf den Account uebertragen',
    'Rate-Limiting: Eigene Policy fuer anonyme Puzzle-Endpoints (30 Requests/Minute)',
    'API: Neue Endpoints POST /api/puzzles/{id}/attempt/anonymous, GET /api/puzzles/stats/anonymous, POST /api/puzzles/claim-session',
  ]},
  { version: '0.18.5', date: '2026-05-28', changes: [
    'Code-Review: FriendService Suche mit determinisitscher Sortierung (OrderBy)',
    'Code-Review: AdminController ClearPuzzles in Transaktion gewrappt',
    'Code-Review: MaxFileSize Konstante dedupliziert (RepertoireService als Single Source)',
    'Code-Review: BackgroundTaskQueue DropOldest statt Wait (verhindert Request-Blockierung)',
    'Code-Review: Health-Endpoint prueft DB-Konnektivitaet (503 bei Fehler)',
    'Code-Review: PuzzleService Min/Max ID-Range mit MemoryCache (5min TTL)',
    'Code-Review: DB-Index auf TournamentSubscription.CrawlerTournamentId',
    'Code-Review: Dashboard takeUntilDestroyed fuer Subscription-Cleanup',
    'Crawler: ApiKey-Vergleich timing-safe (CryptographicOperations.FixedTimeEquals)',
    'Crawler: CrawlJob ErrorMessage Null-Check',
  ]},
  { version: '0.18.4', date: '2026-05-28', changes: [
    'Fix: Endless Mode letztes Puzzle erscheint jetzt in der Zusammenfassung (Reset auf letztem Leben)',
  ]},
  { version: '0.18.3', date: '2026-05-28', changes: [
    'Puzzle Mode: Einheitliche Controls in allen Zustaenden (kein UI-Hinweis ob am Loesungsweg)',
    'Puzzle Mode: Auto-Advance nach 3s bei geloestem Puzzle, Show Solution Button',
    'Puzzle Mode: Review-Button fuer letztes geloestes Puzzle',
  ]},
  { version: '0.18.2', date: '2026-05-27', changes: [
    'Endless Mode: Bei falschem Puzzle kein Auto-Advance, stattdessen Show Solution + Continue Buttons',
  ]},
  { version: '0.18.1', date: '2026-05-27', changes: [
    'Endless Mode: Stockfish Depth waehrend laufender Serie per Slider anpassbar',
  ]},
  { version: '0.18.0', date: '2026-05-27', changes: [
    'Security: CrawlerProxyService Error-Details durchreichen (CrawlerRequestException + ExceptionFilter)',
    'Security: JWT Key Minimum 32 Bytes fuer HMAC-SHA256',
    'Security: HSTS Header in nginx',
    'Security: Puzzle-Theme SQL-Wildcard Sanitization',
    'Security: Passwort-Validierung im Register-Formular (8+ Zeichen, Gross/Klein/Zahl)',
    'Performance: PuzzleService GetStats via DB-Level Aggregation statt ToListAsync',
    'Performance: Puzzle CSV Import mit File-Size-Limit (500 MB) und CancellationToken',
    'Fix: AdminSeeder kein Re-Hash bei unveraendertem Passwort',
    'Fix: AutoSubscriptionService Race-Condition bei SaveChanges (DbUpdateException Handling)',
    'Fix: TournamentMonitor per-User Scoping (UserId + FK + Migration)',
    'Fix: TournamentProxyController try/catch durch CrawlerExceptionFilter ersetzt',
    'Fix: AuthService localStorage Error Handling komplett in try/catch',
    'Fix: Dashboard typisierte HTTP-Calls (Repertoire[], Friend[], PuzzleStatsDto)',
    'Infra: compose.vpn.yml API depends_on Gluetun',
    'Crawler: BackgroundTaskQueue statt fire-and-forget Task.Run, 429 bei Queue-Full',
    'Crawler: RequestLoggingMiddleware ohne MemoryStream, Background-Queue statt Task.Run',
    'Crawler: VPN-Rotation Thread-Safety (Semaphore waehrend Rotation gehalten)',
    'Crawler: Separater HttpClient fuer Gluetun API (5s Timeout)',
    'Crawler: ResolveTournamentAsync auch fuer nicht-numerische IDs',
    'Tests: 16 neue Tests (CrawlerProxyService, BackgroundTaskQueue, AdminSeeder, PuzzleService, TournamentMonitor, AutoSubscription)',
  ]},
  { version: '0.17.0', date: '2026-05-27', changes: [
    'Book-Puzzles: Import von Puzzle-Buechern aus schach-bot (12 Buecher, 5000+ Puzzles)',
    'Book-Puzzles: Eigene Route /puzzles/book/:id mit Board, Zugloesung und Buch-Metadaten',
    'Book-Puzzles: Lookup-API GET /api/book-puzzles/by-line-id fuer schach-bot Integration',
    'Book-Puzzles: Admin-Import-Endpoint POST /api/admin/book-puzzles/import',
    'Book-Puzzles: Buch-Liste mit Puzzle-Counts GET /api/book-puzzles/books',
    'Book-Puzzles: Python-Import-Script scripts/import_books.py',
  ]},
  { version: '0.16.2', date: '2026-05-27', changes: [
    'Crawler: Migration-Upgrade-Doku fuer bestehende DBs (EnsureCreated → Migrate)',
    'Deploy: Crawler-Fix fuer leere __EFMigrationsHistory bei Bestandsdatenbanken',
  ]},
  { version: '0.16.1', date: '2026-05-27', changes: [
    'Security: Tournament-ID-Validierung gegen SSRF in Proxy- und Monitor-Controllern',
    'Security: .gitignore fuer Test-Artifacts (auth-state, playwright-report)',
    'Refactor: Fire-and-forget Task.Run ersetzt durch Channel+BackgroundService',
    'Refactor: TypeScript-Interfaces statt any fuer alle API-Responses',
    'Crawler: EnsureCreated durch EF Core Migrations ersetzt',
  ]},
  { version: '0.16.0', date: '2026-05-27', changes: [
    'Endless Mode: Puzzle-Review auf Game-Over-Screen zeigt alle gespielten Puzzles',
    'Endless Mode: Klick auf Puzzle im Review navigiert zu /puzzles/:id',
    'Endless Mode: Fehlgeschlagene Puzzles rot hervorgehoben',
  ]},
  { version: '0.15.9', date: '2026-05-27', changes: [
    'E2E Teststack: Isolierter Docker-Stack (compose.e2e.yml) mit eigenem DB, API, Frontend',
    'E2E Teststack: scripts/e2e.sh startet Stack, seedet Puzzles, fuehrt Tests aus, raeumt auf',
    'E2E: API_URL konfigurierbar in global-setup.ts und auth.fixture.ts',
  ]},
  { version: '0.15.8', date: '2026-05-27', changes: [
    'Fix: User-Seite bleibt fest — movable.color immer auf orientation statt turnColor',
    'Fix: Dests nur wenn User am Zug (verhindert Stockfish-Figuren ziehen nach Premove+Wrong Move)',
  ]},
  { version: '0.15.7', date: '2026-05-27', changes: [
    'Share-Puzzle: QR-Code + Link zum Teilen von Puzzles (Normal + Endless Mode)',
    'Direkt-Link /puzzles/:id laedt ein bestimmtes Puzzle',
  ]},
  { version: '0.15.6', date: '2026-05-27', changes: [
    'Fix: Premove-Orientierungsbug — illegale Premoves werden ignoriert statt Seitenwechsel auszulösen',
  ]},
  { version: '0.15.5', date: '2026-05-27', changes: [
    'E2E Tests: Falscher Zug + Premove in Puzzle Mode und Endless Mode',
  ]},
  { version: '0.15.4', date: '2026-05-27', changes: [
    'Puzzle Mode: Show Eval und Reset Buttons nach falschem Zug',
    'Fix: Endless Mode kein sofortiges Wrong bei Stockfish-Fehler',
  ]},
  { version: '0.15.3', date: '2026-05-27', changes: [
    'E2E Tests mit Playwright (Auth, Dashboard, Puzzles)',
  ]},
  { version: '0.15.2', date: '2026-05-27', changes: [
    'Tests: 110 neue Tests (122 → 232), alle Controller vollständig getestet',
    'Tests: TournamentProxyController, PuzzleController, AuthController, ProfileController, FriendController, RepertoireController, ExtensionController, RequestLogController, RoundMonitorService',
  ]},
  { version: '0.15.1', date: '2026-05-27', changes: [
    'Fix: Premoves im normalen Puzzle Mode funktionieren jetzt',
  ]},
  { version: '0.15.0', date: '2026-05-27', changes: [
    'Puzzle Mode: Stockfish übernimmt bei falschem Zug (wie im Endless Mode)',
    'Puzzle Mode: Schachmatt gegen Stockfish zählt als alternative Lösung',
    'Puzzle Mode: Mouseslip-Button (einmal pro Puzzle)',
    'Puzzle Mode: Stockfish Depth Einstellung (1–24, persistiert)',
    'Puzzle Mode: Premoves während Stockfish denkt',
  ]},
  { version: '0.14.5', date: '2026-05-27', changes: [
    'Fix: Premove funktioniert jetzt (Figuren während Gegnerzug anklickbar)',
  ]},
  { version: '0.14.4', date: '2026-05-27', changes: [
    'Fix: Premove-System komplett überarbeitet (Zug wird nach Gegnerzug automatisch ausgeführt)',
    'Fix: Hilfe-Overlay mit korrekten Umlauten',
    'Fix: Config-Grid Abstand zwischen Feldern korrigiert',
    'Hilfe: Stockfish Depth Einstellung dokumentiert',
  ]},
  { version: '0.14.3', date: '2026-05-27', changes: [
    'Endless Mode: Stockfish-Tiefe konfigurierbar (1–24, Standard 16)',
  ]},
  { version: '0.14.2', date: '2026-05-27', changes: [
    'Endless Mode: Premoves waehrend Gegner denkt (Zug vorausplanen wie auf Lichess/Chess.com)',
  ]},
  { version: '0.14.1', date: '2026-05-27', changes: [
    'Endless Mode: Hilfe-Overlay erklaert Spielablauf, Stockfish, Buttons und Einstellungen',
  ]},
  { version: '0.14.0', date: '2026-05-27', changes: [
    'Endless Mode: Mouseslip-Button macht letzten Zug gratis rueckgaengig (einmal pro Puzzle)',
  ]},
  { version: '0.13.9', date: '2026-05-27', changes: [
    'Endless Mode: Fasttrack standardmaessig aktiviert',
  ]},
  { version: '0.13.8', date: '2026-05-27', changes: [
    'Fix: Fehlende EF-Migration fuer Puzzles-Tabelle hinzugefuegt (Prod-Deployment)',
  ]},
  { version: '0.13.7', date: '2026-05-27', changes: [
    'Stockfish Suchtiefe von 12 auf 16 erhoeht (~2400 Elo)',
  ]},
  { version: '0.13.6', date: '2026-05-27', changes: [
    'Endless Mode: Puzzle Reset kostet ein Leben',
  ]},
  { version: '0.13.5', date: '2026-05-27', changes: [
    'Endless Mode: Alternative Loesung pausiert, Continue/Show Solution Buttons',
    'Endless Mode: Show Solution spielt die beabsichtigte Zugfolge animiert ab',
  ]},
  { version: '0.13.4', date: '2026-05-27', changes: [
    'Fasttrack: Auto-Thresholds mindestens startElo+400/+800 (keine sinnlosen niedrigen Werte mehr)',
    'Fasttrack: Step Size auf 10–200 geclampt',
  ]},
  { version: '0.13.3', date: '2026-05-27', changes: [
    'Endless Mode: Rating-Range aus DB geladen, alle Inputs validiert gegen tatsaechliche Puzzle-Range',
    'Endless Mode: Leere Rating-Bereiche werden automatisch uebersprungen',
    'Endless Mode: "Ausgespielt" Screen mit Stockfisch-Frage wenn Max-Rating ueberschritten',
    'Endless Mode: Step-Size Migration von altem Default 20 auf 40',
    'Endless Mode: Stale Fasttrack-Thresholds werden beim Laden bereinigt',
    'API: Neuer Endpoint GET /api/puzzles/rating-range',
  ]},
  { version: '0.13.2', date: '2026-05-27', changes: [
    'Fasttrack: Immer verfuegbar, Fallback startElo+400/+800 wenn keine History',
    'Fasttrack: Thresholds editierbar mit Auto-Wert-Anzeige und Klick-Reset',
    'Fasttrack: Manuelle Overrides werden persistiert',
    'Endless Mode: Range Width entfernt, ergibt sich aus Step Size',
    'Endless Mode: Puzzle-Tags standardmaessig ausgeblendet (Show/Hide toggle)',
  ]},
  { version: '0.13.0', date: '2026-05-27', changes: [
    'Endless Mode: Session-History trackt alle Spieldurchlaeufe (max 50, localStorage)',
    'Endless Mode: Fasttrack-Option ueberspringt leichte Puzzles basierend auf vergangenen Sessions',
    'Endless Mode: 3-Phasen Fasttrack-Algorithmus (Phase 1: bis 1. Fehler-Avg, Phase 2: bis 2. Fehler-Avg, Phase 3: Step 20)',
    'Endless Mode: Dynamisches Rating-System statt fester Level-Berechnung',
    'Endless Mode: Fehler-Ratings im Game-Over-Screen angezeigt',
    'Endless Mode: Phase-Indikator waehrend Fasttrack-Spiel',
  ]},
  { version: '0.12.4', date: '2026-05-27', changes: [
    'Endless Mode: Einheitliches UI nach erstem Zug (kein Unterschied zwischen richtig/falsch sichtbar)',
    'Endless Mode: Correct nur bei komplett geloestem Puzzle, sonst immer Reset/Give Up/Eval Buttons',
    'Endless Mode: Eval-Vergleich Start vs. Aktuell beim Klick auf Show Eval',
  ]},
  { version: '0.12.3', date: '2026-05-27', changes: [
    'Endless Mode: Stockfish spielt nach falschem Zug endlos weiter (kein Limit)',
    'Endless Mode: Matt gegen Stockfish zaehlt als alternative Loesung',
    'Endless Mode: Show Eval, Reset (kein Lebensverlust), Give Up Buttons',
    'Endless Mode: Eval-Anzeige aus Weiss-Perspektive',
    'Fix: CSP + WASM MIME-Type fuer Stockfish Web Worker',
  ]},
  { version: '0.12.2', date: '2026-05-27', changes: [
    'Endless Mode: Stockfish-Refutation bei falschem Zug',
    'Stockfish 18 Lite (WASM) als Web Worker integriert',
  ]},
  { version: '0.12.1', date: '2026-05-27', changes: [
    'Endless Mode: Standard Step-Size und Range auf 40 erhoeht',
  ]},
  { version: '0.12.0', date: '2026-05-27', changes: [
    'Endless Puzzle Mode: Progressive Schwierigkeit mit konfigurierbarem Start-Rating, Step-Size und Range',
    'Endless Mode: 3 Leben, bei 0 Game Over mit Highscore-Tracking',
    'Endless Mode: Prefetch fuer schnelles Laden, Session-Timer, Level-Anzeige',
    'Endless Mode: Config wird in localStorage gespeichert, Highscore persistent',
  ]},
  { version: '0.11.3', date: '2026-05-26', changes: [
    'Performance: Puzzle-Loading von 10+ Sekunden auf unter 1 Sekunde optimiert',
    'Backend: ID-Range-Ansatz statt COUNT+SKIP auf 5.3M Rows',
    'Frontend: Naechstes Puzzle wird im Hintergrund vorgeladen',
  ]},
  { version: '0.11.2', date: '2026-05-26', changes: [
    'Fix: Puzzle-Board rendert korrekt (Critical CSS + inlineCritical deaktiviert)',
    'Fix: Puzzle-Zuege funktionieren (Chessground viewOnly-Bug umgangen)',
    'Fix: State-Reihenfolge in Puzzle-Setup korrigiert (dests vor Board-Update)',
  ]},
  { version: '0.11.1', date: '2026-05-26', changes: [
    'Puzzles ohne Anmeldung spielbar (Route + API offen)',
    'Stats und Attempts nur fuer eingeloggte User',
    'Puzzles-Link in Navbar auch fuer nicht eingeloggte User sichtbar',
  ]},
  { version: '0.11.0', date: '2026-05-26', changes: [
    'Puzzle-Feature: Lichess-Puzzles interaktiv loesen mit Chessground-Board',
    'Puzzle-Filter: Rating-Range und Skip-Solved',
    'Puzzle-Statistiken: Accuracy, Streak, History',
    'Admin: CSV-Import fuer Lichess-Puzzle-Datenbank',
    'Dashboard: Puzzle-Stats-Card',
  ]},
  { version: '0.10.0', date: '2026-05-26', changes: [
    'Backend: Automatisches Detail-Crawling (art=9) fuer favorisierte Spieler bei neuer Runde',
    'Crawler: Neuer Endpoint POST /api/crawl/player-details fuer Spieler-Einzelergebnisse',
    'Crawler: Neuer Endpoint GET /api/tournaments/{id}/players/{snr}/results',
    'PlayerResult-Model erweitert: OpponentSnr, OpponentName, OpponentElo, Points',
    'Claude-Settings fuer beide Projekte vereinheitlicht',
  ]},
  { version: '0.9.9', date: '2026-05-21', changes: [
    'Spieler und Freunde werden bei Turnier-Abos automatisch als Favoriten markiert',
    'Matching ueber FIDE-ID und Name',
  ]},
  { version: '0.9.8', date: '2026-05-21', changes: [
    'Fix: Dashboard-Turnier-Links nutzen jetzt korrekte ID (nicht mehr ChessResults-ID)',
    'Crawler akzeptiert sowohl interne DB-ID als auch ChessResults-ID in Turnier-Routen',
  ]},
  { version: '0.9.7', date: '2026-05-21', changes: [
    'Info-Button neben Login/Register zeigt Quickstart-Guide für nicht eingeloggte User',
    'Quickstart aus dem User-Menü entfernt',
  ]},
  { version: '0.9.6', date: '2026-05-21', changes: [
    'Fix: Quickstart-Icons für Monitor und ChessResults-ID korrigiert',
    'Fix: Umlaute im Quickstart-Guide',
  ]},
  { version: '0.9.5', date: '2026-05-21', changes: [
    'Quickstart-Guide im User-Menü erklärt Subscribe, Monitor, Favoriten und ChessResults-ID',
    'Quickstart wird automatisch nach Registrierung angezeigt',
  ]},
  { version: '0.9.4', date: '2026-05-21', changes: [
    'Dashboard: Abonnierte Turniere sind jetzt klickbar und fuehren zur Turnierseite',
  ]},
  { version: '0.9.3', date: '2026-05-20', changes: [
    'Anstehende Turniere werden automatisch abonniert wenn ChessResults-ID hinterlegt',
    'Taegliche automatische Suche nach neuen Turnier-Anmeldungen',
  ]},
  { version: '0.9.2', date: '2026-05-20', changes: [
    'Revert: Spielersuche wieder rein per Vor-/Nachname (ChessResults-ID-Suche entfernt)',
  ]},
  { version: '0.9.1', date: '2026-05-20', changes: [
    'Fix: Spielersuche zeigt bei exaktem Namens-Treffer nur diesen an (statt alle mit gleichem Vornamen)',
  ]},
  { version: '0.9.0', date: '2026-05-20', changes: [
    'Spielersuche im Profil: Vorname/Nachname eingeben und auf ChessResults + FIDE suchen',
    'Suchergebnisse zeigen Name, Titel, Elo, Land und IDs an',
    'Klick auf Ergebnis uebernimmt ChessResults-ID und/oder FIDE-ID automatisch',
    'Bei genau einem Treffer pro Quelle wird die ID automatisch ausgefuellt',
    'Neue Profil-Felder: Vorname und Nachname',
  ]},
  { version: '0.8.7', date: '2026-05-19', changes: [
    'Freunde-Suche durchsucht jetzt auch Chess.com, Lichess, FIDE-ID und ChessResults-ID',
    'Suchergebnisse zeigen vorhandene Schach-Identitaeten an',
  ]},
  { version: '0.8.6', date: '2026-05-19', changes: [
    'Fix: Schachbrett-Rendering komplett ueberarbeitet (JS-basierte Dimensionen statt CSS-Tricks)',
    'Chessground bekommt jetzt explizite Pixel-Groesse via JavaScript',
    'ResizeObserver haelt das Brett bei Fensteraenderungen quadratisch',
  ]},
  { version: '0.8.5', date: '2026-05-19', changes: [
    'Fix: Chessground-Board rendert jetzt korrekt (padding-bottom Trick statt aspect-ratio)',
    'Fix: Board-Wrapper mit zwei DIVs damit Chessground konkrete Dimensionen bekommt',
  ]},
  { version: '0.8.4', date: '2026-05-19', changes: [
    'Fix: Schachbrett rendert jetzt korrekt neben der Zugliste (explizite 400x400px Dimensionen fuer Chessground)',
  ]},
  { version: '0.8.3', date: '2026-05-19', changes: [
    'Fix: Schachbrett-Layout im Repertoire-Detail korrigiert (feste Breite 400px, responsive Breakpoint)',
  ]},
  { version: '0.8.2', date: '2026-05-19', changes: [
    'PGN-Kommentare werden in der Zugliste angezeigt (kursiv, unter dem Zug)',
    'Chessbase-Annotationen ([%csl], [%cal], [%tqu]) werden aus Kommentaren entfernt',
    'PGN-Viewer Dialog zeigt ebenfalls Kommentare an',
  ]},
  { version: '0.8.1', date: '2026-05-19', changes: [
    'Fix: PGN-Parser unterstuetzt Chessbase-Annotationen (RAV-Varianten, Labels, NAGs)',
    'Fix: Header-Erkennung ignoriert jetzt ] in Kommentaren wie {[%tqu ...]}',
  ]},
  { version: '0.8.0', date: '2026-05-19', changes: [
    'Repertoire: Neues Lines/Tree/Edit-Layout mit Inline-Schachbrett',
    'Repertoire: Lines-Ansicht zeigt alle Partien mit Zugliste und Navigation',
    'Repertoire: Tree-Ansicht mit Zugbaum, Haeufigkeiten und Breadcrumb-Navigation',
    'Repertoire: Edit-Ansicht fuer Datei-Upload und -Verwaltung',
    'Repertoire: Keyboard-Navigation (Pfeiltasten) in der Lines-Ansicht',
    'Refactor: PGN-Parser als geteiltes Modul extrahiert',
  ]},
  { version: '0.7.0', date: '2026-05-19', changes: [
    'PGN-Viewer: Interaktives Schachbrett mit Zugnavigation (chess.js + chessground)',
    'PGN-Viewer: Zugliste mit Klick-Navigation und Keyboard-Support (Pfeiltasten, Home/End)',
    'PGN-Viewer: Multi-Game-Support fuer PGN-Dateien mit mehreren Partien',
  ]},
  { version: '0.6.8', date: '2026-05-19', changes: [
    'Admin: Log-Bereich mit Filtern (Pfad, Methode, Status, Benutzer)',
    'Admin: Farbige Status-Codes, Methoden-Badges, IP-Spalte',
    'Admin: Langsame Requests (>1s) hervorgehoben',
  ]},
  { version: '0.6.7', date: '2026-05-19', changes: [
    'Checkliste: Nach jedem Commit wird die aktuelle Version mitgeteilt',
  ]},
  { version: '0.6.6', date: '2026-05-19', changes: [
    'Fix: Copy-Button im Share-Dialog funktioniert jetzt auch ohne HTTPS (Fallback)',
  ]},
  { version: '0.6.5', date: '2026-05-19', changes: [
    'Fix: Typo im Repository-Namen korrigiert (chessreslults → chessresults)',
  ]},
  { version: '0.6.4', date: '2026-05-19', changes: [
    'Security: Input-Validierung und Laengenbegrenzungen auf allen Suchfeldern',
    'Security: Passwort-Policy auf 8 Zeichen + Komplexitaet erhoeht',
    'Security: Crawler-Body-Validierung im Proxy-Controller',
    'Fix: HealthController gibt 503 statt falsches 200 bei IP-Fehler',
    'Fix: Leere Catch-Bloecke durch Logging ersetzt',
    'Performance: Response Compression aktiviert',
  ]},
  { version: '0.6.3', date: '2026-05-19', changes: [
    'Security: API-Key-Authentifizierung fuer Crawler-Endpoints',
    'Security: TournamentMonitorController erfordert jetzt Authentifizierung',
    'Security: .env.dev aus Git-Tracking entfernt',
  ]},
  { version: '0.6.2', date: '2026-05-19', changes: [
    'Dev-Badge im Footer: zeigt "dev" neben der Version im Dev-Build',
    'CI/CD: :dev und :latest Tag-Schema fuer Docker-Images',
  ]},
  { version: '0.6.1', date: '2026-05-19', changes: [
    'CI/CD: GitHub Actions bauen Docker-Images bei Push (latest) und Tag (versioniert)',
    'Prod-Compose nutzt IMAGE_TAG fuer getaggte Versionen',
  ]},
  { version: '0.6.0', date: '2026-05-19', changes: [
    'Admin-System: Benutzerverwaltung mit Admin-Rolle (IsAdmin)',
    'Admin-Seed beim Start ueber ADMIN_USERNAME/ADMIN_PASSWORD Env-Variablen',
    'Admin-Panel mit User-Verwaltung und Request-Log-Ansicht',
    'RequestLogController jetzt nur fuer Admins zugaenglich',
  ]},
  { version: '0.5.2', date: '2026-05-19', changes: [
    'Commit-Checkliste in beiden CLAUDE.md verankert (Version, Changelog, Tests)',
  ]},
  { version: '0.5.1', date: '2026-05-19', changes: [
    '73 neue Unit-Tests fuer beide Projekte (Crawler + RookHub)',
    'Test-Pflicht in CLAUDE.md verankert: jedes Feature/Endpoint/Bugfix braucht Tests',
  ]},
  { version: '0.5.0', date: '2026-05-19', changes: [
    'Oeffentliche Turnierseite /t/:id (ohne Login einsehbar)',
    'Share-Button mit QR-Code und kopierbarem Link',
    'localStorage-Favoriten fuer oeffentliche Ansicht',
  ]},
  { version: '0.4.0', date: '2026-05-18', changes: [
    'Turnier-Datum und Ort von chess-results.com extrahieren',
    'VPN IP-Rotation alle 20 Requests (via Gluetun API)',
    'HTTP-Retry bei transienten Netzwerkfehlern',
  ]},
  { version: '0.3.0', date: '2026-05-18', changes: [
    'Browser-Benachrichtigungen bei neuer Runde (Round Monitor)',
  ]},
  { version: '0.2.0', date: '2026-05-18', changes: [
    'Changelog hinzugefuegt (Klick auf Version im Footer)',
    'Crawler IP-Endpoint (VPN-Verifizierung)',
    'Tournament Round Monitor (1h Auto-Check)',
    'Request Logging Middleware',
    'Keine Default-Werte in Compose-Example-Dateien',
  ]},
  { version: '0.1.0', date: '2026-05-18', changes: [
    'Versionsnummer im Footer (Desktop)',
  ]},
];
