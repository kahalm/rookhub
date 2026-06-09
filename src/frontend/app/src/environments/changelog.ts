// Single Source of Truth fuer App-Version + Changelog.
// Wird von BEIDEN Environment-Dateien importiert (environment.ts = dev,
// environment.prod.ts = prod-Build via fileReplacements). Dadurch zeigt der
// Footer in JEDEM Build dieselbe Version/Changelog — ein Bump aendert nur hier.
export const APP_VERSION = '0.98.9';

export interface ChangelogEntry {
  version: string;
  date: string;
  changes: string[];
}

export const CHANGELOG: ChangelogEntry[] = [
  { version: '0.98.9', date: '2026-06-09', changes: [
    'Revert von 0.98.8 (Bereitschafts-Prüfung des Tag-Index): die Vollständigkeits-Abfrage wurde wieder auf die einfache Variante zurückgesetzt.',
  ]},
  { version: '0.98.7', date: '2026-06-09', changes: [
    'Tagespuzzle → Discord: Der Webhook an den Schach-Bot sendet jetzt die Lösungszeit pro Löser (timeSeconds) — der Bot zeigt sie in der „Gelöst von"-Zeile an (war bisher leer, weil die Zeit nicht übertragen wurde).',
  ]},
  { version: '0.98.6', date: '2026-06-09', changes: [
    'Admin: neuer „Puzzles"-Tab mit Button „Tag-Tabelle aufbauen" — stößt den einmaligen Backfill der Themen-Tag-Tabelle an (statt API-Aufruf von Hand).',
  ]},
  { version: '0.98.5', date: '2026-06-09', changes: [
    'Performance: Themen-Filter („schwächste Themen trainieren") nutzt jetzt eine normalisierte, indizierte Tag-Tabelle (Tags + PuzzleTags) statt LIKE-Full-Scan — Puzzle-Auswahl ist damit indexgestützt und schnell.',
    'Admin: einmaliger Backfill der Tag-Tabelle nach Deploy nötig (POST /api/admin/puzzles/backfill-tags); danach pflegt der Import die Tags automatisch.',
  ]},
  { version: '0.98.4', date: '2026-06-09', changes: [
    '„Schwächste Themen trainieren": Nach gelöstem Puzzle wird das passende Thema (das das gelöste Puzzle trägt) in der Filterleiste grün hervorgehoben — in Puzzle- und Endless-Modus.',
  ]},
  { version: '0.98.3', date: '2026-06-09', changes: [
    'Interne Doku: Hinweis zum lokalen dotnet-Pfad + InMemory-Test-Lücke in CLAUDE.md (keine App-Änderung).',
  ]},
  { version: '0.98.2', date: '2026-06-09', changes: [
    '„Schwächste Themen trainieren": Beim Aktivieren werden die 5 Themen jetzt sichtbar ins Themenfeld geschrieben (Endless), und der aktive Themenfilter wird während des Lösens/Runs als Chips angezeigt (beide Modi).',
    'Performance: Theme-gefilterte Puzzle-Auswahl massiv beschleunigt — statt mehrerer Full-Scans pro Aufruf (im Endless-Batch ×Fensteranzahl) jetzt ein einziger Scan über die Rating-Spanne (erstes Puzzle lädt nicht mehr ~30 s).',
  ]},
  { version: '0.98.1', date: '2026-06-09', changes: [
    'Fix: „5 schwächste Themen trainieren" lud kein Puzzle (Puzzle- + Endless-Modus). Ursache: der ODER-Themenfilter erzeugte ein von MySQL nicht übersetzbares LIKE-Prädikat (EF.Functions als Konstante statt statischem Property-Zugriff).',
  ]},
  { version: '0.98.0', date: '2026-06-09', changes: [
    'Neu: „5 schwächste Themen trainieren" — im Standard-Puzzle-Modus (Einstellungen) und im Endless-Modus (Konfiguration). Zieht gezielt Puzzles zu den Themen, die du am schlechtesten löst (niedrigste Lösungsquote).',
    'Backend: ODER-Themenfilter (themesAny) für die Puzzle-Auswahl + Endpoint /api/puzzles/stats/worst-themes.',
  ]},
  { version: '0.97.18', date: '2026-06-09', changes: [
    'Tests: 11 veraltete Unit-Tests an das aktuelle Verhalten angepasst (AnalysisComponent-Engine-Mock um engineFatalError$/destroy ergänzt; Aufgeben-Tests erwarten jetzt korrekt FAILED statt SOLVED; reviewLastPuzzle-from-Param). Gesamte Suite wieder grün (258/258).',
  ]},
  { version: '0.97.17', date: '2026-06-09', changes: [
    'Test-Infrastruktur: karma.conf.js findet automatisch die Puppeteer-Headless-Shell — ng test läuft jetzt ohne system-weites Chrome und ohne root.',
  ]},
  { version: '0.97.16', date: '2026-06-09', changes: [
    'Spielerstatistik: Elo-Verlaufskurve wird geglättet (Bézier statt zackiger Polyline) — gilt für Einzel- und Alle-Level-Ansicht.',
    'Spielerstatistik: Die Verlaufs-Ansicht startet standardmäßig auf der aktuell bei den Puzzles eingestellten Visualisierungsstufe.',
    'Spielerstatistik: „Nach Thema" wird absteigend nach Lösungs-% sortiert.',
    'Spielerstatistik: Behebt, dass die Balken bei „Nach Rating gelöst" alle gleich hoch waren (Balkenhöhe wird jetzt korrekt skaliert).',
  ]},
  { version: '0.97.15', date: '2026-06-09', changes: [
    'Logging: rohe Weiterleitungs-Kette (X-Forwarded-For + X-Real-IP) wird zusätzlich als Diagnosefeld erfasst, damit alle Proxy-Hops sichtbar sind.',
  ]},
  { version: '0.97.14', date: '2026-06-09', changes: [
    'Logging/Rate-Limiter erfassen jetzt die echte Client-IP statt der Docker-Gateway-IP (X-Forwarded-For über beide Proxy-Hops zurückgerollt).',
  ]},
  { version: '0.97.13', date: '2026-06-08', changes: [
    'Puzzle-Einstellungen: Schwierigkeitsstufen haben jetzt ein (i)-Info-Panel mit Beschreibung der Elo-Offsets.',
    'Puzzle: Bei Schwierigkeitsänderung wird automatisch zum nächsten Puzzle im neuen Bereich gesprungen.',
  ]},
  { version: '0.97.12', date: '2026-06-08', changes: [
    'Security: Swagger-UI nur noch im Development-Build verfügbar (nicht in Production).',
    'Security: RememberMe-Token-Laufzeit von 20 Jahren auf 1 Jahr reduziert.',
  ]},
  { version: '0.97.11', date: '2026-06-08', changes: [
    'StockfishService: toter Ternary bei Mate-Eval entfernt (beide Zweige waren identisch).',
    'EndlessProgressService: BulkImport überträgt jetzt Seed und ChainPuzzleIds aus dem DTO (wurden bisher stillschweigend verworfen).',
  ]},
  { version: '0.97.10', date: '2026-06-08', changes: [
    'Analyse: Engine-Crash-Erkennung verbessert — nach 3 fehlgeschlagenen Auto-Recovery-Versuchen wird ein Fehlerbanner mit Seite-neu-laden-Button angezeigt statt lautlos zu hängen.',
    'Analyse: crashStreak wird beim Verlassen der Seite zurückgesetzt, sodass ein erneuter Besuch einen sauberen Neustart garantiert.',
    'StockfishService: worker.onerror übergibt jetzt die Fehlermeldung an den Telemetrie-Hook.',
  ]},
  { version: '0.97.9', date: '2026-06-08', changes: [
    'RoundMonitorService: SaveChanges jetzt pro Iteration statt einmalig am Ende — bei Fehler in späteren Einträgen gehen frühere Updates nicht mehr verloren.',
  ]},
  { version: '0.97.8', date: '2026-06-08', changes: [
    'Puzzle: RecordAttemptAsync mit Idempotenz (30s-Fenster) und Elo-Guard — nur der erste Versuch pro Puzzle zählt für Elo, weitere Versuche werden gespeichert aber verändern die Wertung nicht.',
  ]},
  { version: '0.97.7', date: '2026-06-08', changes: [
    'Crawler: CrawlJob bleibt bei Queue-voll-Fehler nicht mehr dauerhaft auf "Queued" hängen — Job wird sofort auf "Failed" gesetzt.',
  ]},
  { version: '0.97.6', date: '2026-06-08', changes: [
    'Buchpuzzle: Ladefehler zeigt jetzt Fehlermeldung mit Retry-Button statt endlosem Spinner.',
  ]},
  { version: '0.97.5', date: '2026-06-08', changes: [
    'Analyse: Engine-Hang nach Puzzle→Analyse-Wechsel behoben — Worker wird beim Verlassen der Analyseseite sauber terminiert.',
  ]},
  { version: '0.97.4', date: '2026-06-08', changes: [
    'Endless: Nach "Analysieren" beim letzten verlorenen Herz landet man beim Zurückkehren korrekt im Zusammenfassungs-Screen statt auf der Startseite.',
  ]},
  { version: '0.97.3', date: '2026-06-08', changes: [
    'Endless: Nur der erste Fehlschlag pro Puzzle kostet ein Herz — weiteres Aufgeben oder Zurücksetzen beim selben Puzzle ist kostenlos.',
  ]},
  { version: '0.97.2', date: '2026-06-07', changes: [
    'Dark Mode: Alle hardkodierten Grautöne im Frontend auf color-mix(currentColor) umgestellt — Navbar, PGN-Viewer, Weekly-Liste, Share-Dialoge, Profil-Token-Box, App-Install-Dialog vollständig kompatibel.',
  ]},
  { version: '0.97.1', date: '2026-06-07', changes: [
    'Profil: Theme-Auswahl (System/Hell/Dunkel) als Button-Toggle direkt im Profil sichtbar.',
  ]},
  { version: '0.97.0', date: '2026-06-07', changes: [
    'Dark Mode: Neuer Theme-Toggle in der Navbar (Sonne/Mond/Auto-Icon). Default: System-Einstellung (prefers-color-scheme). Manuelle Wahl wird im Browser gespeichert.',
  ]},
  { version: '0.96.23', date: '2026-06-07', changes: [
    'Puzzle: Zugtext unter dem Brett jetzt größer und schwarz für besseren Kontrast.',
    'Puzzle: Reset- und Mausslip-Schaltfläche erscheinen ab dem ersten Zug, auch wenn man im richtigen Lösungszweig ist.',
    'Puzzle/Endless/Buch: Mausslip funktioniert jetzt auch auf dem Lösungspfad (nimmt den letzten korrekten Zug zurück).',
    'Fix: „Letztes Puzzle reviewen" gibt nach dem Review das aktuelle (neue) Puzzle zurück, nicht das reviewte.',
  ]},
  { version: '0.96.22', date: '2026-06-07', changes: [
    'Fix: Kibana-Dashboard-Link im Admin zeigt jetzt auf das korrekte Dashboard — Prod auf rookhub-prod-dashboard, Dev auf rookhub-dev-dashboard (statt der nicht existierenden ID rookhub-logging-dashboard).',
  ]},
  { version: '0.96.21', date: '2026-06-07', changes: [
    'Repertoires: Name und Kategorie bestehender Repertoires können jetzt über einen Bearbeiten-Button geändert werden.',
  ]},
  { version: '0.96.20', date: '2026-06-07', changes: [
    'API: JsonStringEnumConverter aktiviert – Extension-Token-Verbindung gibt kein 400 mehr bei POST analyze-game.',
  ]},
  { version: '0.96.19', date: '2026-06-07', changes: [
    'Endless: Game-Over-Liste zeigt jetzt auch die Puzzles, die auf einem anderen Tab gespielt wurden (Multi-Tab-Resume).',
  ]},
  { version: '0.96.18', date: '2026-06-07', changes: [
    'Infra: Kibana-Init erstellt jetzt zwei Dashboards (Prod + Dev) mit identischem Layout. ES-Index-Format konfigurierbar über Elasticsearch:IndexFormat.',
  ]},
  { version: '0.96.17', date: '2026-06-07', changes: [
    'Extension-API: neuer Endpoint POST /api/extension/analyze-game. Repcheck-Userscript/Extension (≥ v1.6.0) schickt jetzt die Zugliste der aktuellen Partie und der Server liefert ply-weise Annotationen zurück — das Repertoire-PGN verlässt den Server nicht mehr. Server-seitiger Positions-Set-Cache (15 min / 5 min sliding), invalidiert bei PGN-Upload/-Delete.',
  ]},
  { version: '0.96.16', date: '2026-06-07', changes: [
    'Admin: Tagespuzzle neu generieren schickt Webhook an den Bot — dieser postet das neue Puzzle in Discord und hinterlässt im alten Thread einen Hinweis.',
  ]},
  { version: '0.96.15', date: '2026-06-07', changes: [
    'Tagespuzzle/Claim-Session: War das Puzzle bereits eingeloggt gelöst, wird der anonyme Eintrag beim Login jetzt korrekt gelöscht statt als Duplikat stehen zu bleiben.',
  ]},
  { version: '0.96.14', date: '2026-06-07', changes: [
    'Tagespuzzle: Anonyme Lösungen werden beim nächsten Login automatisch dem Account zugeordnet.',
  ]},
  { version: '0.96.13', date: '2026-06-07', changes: [
    'Auth: Normaler Login 30 Tage gültig, „Eingeloggt bleiben" praktisch unbegrenzt (20 Jahre).',
  ]},
  { version: '0.96.12', date: '2026-06-07', changes: [
    'Auth: Session-Token-Gültigkeit von 1 Tag auf 48 Stunden erhöht.',
  ]},
  { version: '0.96.11', date: '2026-06-06', changes: [
    'Endless: Per-Puzzle-Timer (elapsedSeconds) wird jetzt an die Status-Card übergeben und nach dem Lösen angezeigt (wie Standard/Book).',
    'Standard: „Letztes Puzzle reviewen"-Button direkt nach der Status-Card (wie Endless), nicht mehr unter der Stats-Card.',
    'Standard + Endless: Viz-Countdown-Text nutzt i18n-Key statt hardcoded \"s…\" (wie Book).',
  ]},
  { version: '0.96.10', date: '2026-06-06', changes: [
    'Standard-Puzzle: Panel-Reihenfolge angepasst — Your Turn oben, dann Rating-Card, dann Stats (wie Endless).',
  ]},
  { version: '0.96.9', date: '2026-06-06', changes: [
    'Refactor: Einheitliche PuzzleStatusCardComponent für alle drei Puzzle-Modi (Standard/Endless/Book). Kapselt Zahnrad-Button + gesamten State-Switch (LOADING/SETUP/YourTurn/SOLVED/FAILED) — gleiche Optik und gleiche Features in allen Modi.',
    'Standard: Aufgeben (Give Up) führt jetzt wie in Endless/Book zum FAILED-Zustand (statt SOLVED). Retry, Analysieren und Nächstes-Puzzle-Button sind in FAILED sichtbar.',
    'Book: Analysieren-Button aus den unteren Aktionen in die Status-Card verschoben (SOLVED + FAILED). Nächstes-Puzzle-Button auch in FAILED (failedNext()-Methode).',
  ]},
  { version: '0.96.8', date: '2026-06-06', changes: [
    'Einstellungs-Dialog: (i)-Button neben „Visualisierung" klappt Beschreibungsliste aller 5 Modi auf; aktiv gewählter Modus ist hervorgehoben.',
  ]},
  { version: '0.96.7', date: '2026-06-06', changes: [
    'Puzzle-Einstellungen: Gear-Button öffnet jetzt einen Modal-Dialog (PuzzleSettingsDialogComponent) mit Figuren/Brett-Vorschau, Dropdown-Auswahl für Figuren-Set, Brett-Design, Modus, Visualisierung, Gegnerzug-Pfeil und modus-spezifischen Optionen (Schwierigkeit + Gelöste überspringen für Standard; Stockfish-Tiefe für Standard/Book). In allen drei Modi (Standard, Endless, Book) verfügbar.',
    'Refactor: Inline-Settings-Panels (filter-card, theme-card, config-card) aus allen drei Puzzle-Template entfernt; ThemePickerComponent aus den Einzel-Importen der drei Komponenten entfernt.',
  ]},
  { version: '0.96.6', date: '2026-06-06', changes: [
    'Endless: Settings-Gear-Button in die Status-Card verschoben (war vorher als freies Icon zwischen Status-Card und Rating-Card sichtbar).',
  ]},
  { version: '0.96.5', date: '2026-06-06', changes: [
    'Refactor: „Your turn!"-Panel als eigenständige PuzzleYourTurnComponent (Standard/Endless/Book-Modi nutzen dieselbe Komponente, i18n per Mode-Input).',
    'Refactor: Puzzle-Rating-Card als eigenständige PuzzleRatingCardComponent (Standard + Endless; zeigt Rating, Level-Range, Tags und Share-Button).',
    'Standard-Puzzle: Share-Button jetzt in der Rating-Card direkt über der Status-Card (war vorher getrennt darunter).',
  ]},
  { version: '0.96.4', date: '2026-06-06', changes: [
    'Puzzle-Modi: Tags/Themes sind in allen drei Modi (Standard/Endless/Book) standardmäßig ausgeblendet, neuer „Tags anzeigen"-Link klappt sie auf.',
    'Refactor: neue wiederverwendbare PuzzleTagsComponent (statt 3× kopiertes Tag-Toggle-Markup); CSS-Doubletten in den Puzzle-SCSS-Dateien entfernt.',
  ]},
  { version: '0.96.3', date: '2026-06-06', changes: [
    'Book-Puzzle: Leere graue Settings-Card unter "Share puzzle" ausgeblendet, solange das Einstellungs-Panel nicht offen ist (config-card jetzt komplett im @if).',
  ]},
  { version: '0.96.2', date: '2026-06-06', changes: [
    'Build: anyComponentStyle-Budget auf 8 kB Warning / 12 kB Error angehoben (endless-puzzle.scss lag 6 Bytes über dem alten 10-kB-Limit → CI-Build schlug fehl).',
  ]},
  { version: '0.96.1', date: '2026-06-06', changes: [
    'UI: Einheitliches Layout über alle Puzzle-Modi (Book/Weekly/Kurs/Standard/Endless) — Kontext-Card oben, Status-Card mittig, Share-Button unten, Viz-Text direkt unter dem Brett.',
    'Book-Puzzle: Separate Weekly-/Kurs-Card mit Puzzle-Info-Card zur einzigen Kontext-Card oben zusammengeführt.',
    'Viz-Text wird nicht mehr als Card in der rechten Spalte, sondern als plain Text direkt unter dem Schachbrett angezeigt.',
  ]},
  { version: '0.96.0', date: '2026-06-06', changes: [
    'Tagespuzzle: Lösungszeit je Solver im Discord-Embed (z. B. „@Anna (1:23)").',
  ]},
  { version: '0.95.14', date: '2026-06-06', changes: [
    'Weekly: Post-Titel im Header-Chip statt generischem "Wochenpost"-Label (spart eine Zeile).',
    'Viz-Card: Überarbeitet — Level-Badge statt Volltext-Titel, kein Monospace mehr, blauer Akzentbalken für Züge.',
    '"Aufgeben"-Button visuell abgedimmt (kein Warn-Rot, kleinere Schrift) — klare Hierarchie zu "Eval zeigen".',
  ]},
  { version: '0.95.13', date: '2026-06-06', changes: [
    'Profil: Passwort-Ändern-Funktion (aktuelles + neues Passwort mit Bestätigung).',
  ]},
  { version: '0.95.12', date: '2026-06-06', changes: [
    'Endless: Bugfix — nach Aufgeben + Analysieren landete man beim Fortsetzen wieder auf demselben aufgegebenen Puzzle und verlor ein zweites Leben dafür.',
  ]},
  { version: '0.95.11', date: '2026-06-06', changes: [
    'Endless: Kurven-Thresholds angepasst — T1 nach Puzzle 10 (war 5), T2 nach Puzzle 25 (war 20).',
  ]},
  { version: '0.95.10', date: '2026-06-06', changes: [
    'Statistiken: "Show Eval"-Klicks (EvalShown) und Viz-Show-Button-Klicks (VizShowCount) werden pro Puzzle-Versuch mitgespeichert.',
  ]},
  { version: '0.95.9', date: '2026-06-06', changes: [
    'Viz-Einstellungen: Checkbox "Gegnerzug-Pfeil" (nur bei Level 1–4 sichtbar, Einstellung wird lokal gespeichert); Pfeil-Timer von 3s auf 1s verkürzt.',
  ]},
  { version: '0.95.8', date: '2026-06-06', changes: [
    'Visualisierungs-Slider durch Radio-Chips ersetzt (Normal / Blindfold / Checker / Dark / Invisible) — in allen drei Puzzle-Modi.',
  ]},
  { version: '0.95.7', date: '2026-06-06', changes: [
    'Visualisierungsmodus: Gegnerzüge in der Zugliste fett dargestellt.',
  ]},
  { version: '0.95.6', date: '2026-06-06', changes: [
    'Visualisierungsmodus: Gegnerzug-Pfeil nur für Folgezüge (nicht den ersten Setup-Zug); Pfeil blendet sich nach 3 Sekunden automatisch aus.',
  ]},
  { version: '0.95.5', date: '2026-06-06', changes: [
    'Visualisierungsmodus 1–4: Letzter Gegnerzug wird als roter Pfeil auf dem eingefrorenen Brett angezeigt (via chessground setAutoShapes, unabhängig vom Auswahl-Ring).',
  ]},
  { version: '0.95.4', date: '2026-06-06', changes: [
    'Wochenposts sind vor ihrem Erscheinungstermin (scheduledAt) für Nicht-Admins nicht sichtbar — API gibt 404 zurück (Liste, Detail, Puzzles, Ergebnisse, Fortschritt, Versuch).',
  ]},
  { version: '0.95.3', date: '2026-06-06', changes: [
    'Doku: `CLAUDE.md` um eine Pflicht-Regel ergänzt — vor jedem Datei-Edit MUSS `git pull` laufen. Hintergrund: bei Parallel-Arbeit auf mehreren Klonen führt ein Edit gegen veralteten Stand zu Merge-Konflikten und verlorener Arbeit.',
  ]},
  { version: '0.95.2', date: '2026-06-06', changes: [
    'Doku: `CLAUDE.md` aufgeräumt — Endpoints/Schema auf aktuellen Stand gebracht.',
  ]},
  { version: '0.95.1', date: '2026-06-06', changes: [
    'Fix: Nach „Aufgeben → Analysieren → Zurück" wurde dasselbe Puzzle neu geladen statt zum nächsten zu springen. Der Analyse-„Zurück"-Link zeigt jetzt auf die allgemeine Puzzle-Seite statt auf die konkrete Puzzle-ID.',
  ]},
  { version: '0.95.0', date: '2026-06-05', changes: [
    'Wochenpost-Bestenliste: in der Übersicht lässt sich je Eintrag eine Bestenliste aufklappen — sortiert nach Genauigkeit (gelöst von allen Puzzles des Posts), bei Gleichstand zählt die schnellere Gesamtzeit. Pro Person: Platz, gelöst/gesamt + %, Zeit.',
  ]},
  { version: '0.94.1', date: '2026-06-05', changes: [
    'Intern: Wochenpost-Versuche werden jetzt strukturiert nach Kibana geloggt (Observability).',
  ]},
  { version: '0.94.0', date: '2026-06-05', changes: [
    'Wochenpost zeigt jetzt auch in RookHub die Gesamtzeit an: in der Übersicht hinter deinem Fortschritt (⏱) und beim Durchspielen über dem Brett — die Summe deiner Zeit über alle gespielten Puzzles des Posts.',
  ]},
  { version: '0.93.0', date: '2026-06-05', changes: [
    'Der Schach-Bot zeigt jetzt im Wochenpost-Thread auf Discord live den Fortschritt: wer den Post erledigt hat, gelöst/gesamt pro Person und die Gesamtzeit — aktualisiert sich automatisch, sobald jemand spielt (neuer Endpoint + Webhook an den Bot).',
  ]},
  { version: '0.92.0', date: '2026-06-05', changes: [
    'Wochenpost durchspielen: Wenn du schon Fortschritt hast, startet das Durchspielen jetzt direkt beim ersten Puzzle, das du noch nicht gespielt hast — und wenn du alle durch hast, wieder von vorne.',
  ]},
  { version: '0.91.1', date: '2026-06-05', changes: [
    'Wochenpost: Wenn du bei einem Puzzle „Aufgeben" drückst oder es nach einem Zug zurücksetzt, zählt es jetzt als ✗ (gespielt, nicht gelöst) — vorher wurde nur ein gelöstes oder per Fehlzug verlorenes Puzzle gewertet.',
  ]},
  { version: '0.91.0', date: '2026-06-05', changes: [
    'Wochenpost-Übersicht zeigt jetzt deinen Fortschritt: hinter jedem Eintrag steht gelöst/nicht-gelöst (✓/✗) und wie viel % du davon schon gespielt hast.',
  ]},
  { version: '0.90.1', date: '2026-06-05', changes: [
    'Wer einen Link auf eine Login-pflichtige Seite öffnet (z. B. einen Wochenpost) und nicht eingeloggt ist, sieht auf der Login-Seite jetzt einen Hinweis „Bitte logge dich ein oder registriere dich, um fortzufahren" — und landet nach dem Login direkt auf der gewünschten Seite.',
  ]},
  { version: '0.90.0', date: '2026-06-05', changes: [
    'Wochenpost zieht auf RookHub: der Wochenpost ist jetzt hier eingeloggt durchspielbar (vorher nur Admin), und dein Fortschritt wird gemerkt. Jedes gespielte Puzzle zählt — egal ob gelöst oder nicht; sind alle gespielt, gilt der Wochenpost als erledigt. Die Seite zeigt „X/Y gespielt · Z gelöst". (Der Schach-Bot kündigt neue Wochenposts künftig nur noch mit Link hier an, statt sie selbst zu verwalten.)',
  ]},
  { version: '0.89.0', date: '2026-06-05', changes: [
    'Admin: Tagespuzzle neu generieren. Im Admin-Bereich gibt es einen neuen Tab „Tagespuzzle" — Datum wählen, aktuelles Puzzle ansehen und per Knopf neu auswürfeln. Der Link/das Datum bleibt gleich, nur das Puzzle dahinter wechselt. Das bisher gezogene Puzzle wird ausgemustert und nie wieder als Tages-, Zufalls- oder Blindpuzzle gezogen.',
  ]},
  { version: '0.88.0', date: '2026-06-05', changes: [
    'Der Schach-Bot kann jetzt deinen Trainings-Fortschritt für eine tägliche, persönliche Motivations-DM lesen: neuer bot-interner Endpoint `GET /api/bot/player-progress/{discordId}` liefert für ein mit Discord verknüpftes Konto den heutigen Trainingsziele-Stand + die Puzzle-Statistik. Abgesichert über ein geteiltes HMAC-Secret (`SchachBot:StatsSecret`), keine Nutzerdaten ohne gültige Signatur. Nichts an der Web-Oberfläche ändert sich — die Funktion nutzt ausschließlich der Discord-Bot.',
  ]},
  { version: '0.87.0', date: '2026-06-05', changes: [
    'Die E-Mail-Adresse bei der Registrierung ist jetzt optional — wer keine angeben möchte, lässt das Feld einfach leer. Wird eine angegeben, muss sie weiterhin ein gültiges Format haben und eindeutig sein.',
  ]},
  { version: '0.86.1', date: '2026-06-04', changes: [
    'Android-TWA: Dev-Variante hinzugefügt. Neben dem Prod-Build gegen `rookhub.oberschmid.homes` lässt sich jetzt parallel ein Dev-Build gegen `rookhub-dev.oberschmid.homes` bauen — eigene Package-Id (`…rookhub.dev`), eigener App-Name („RookHub Dev"), parallel zur Prod-App auf demselben Telefon installierbar. CI-Workflow „Build Android TWA" fragt jetzt eine `variant`-Auswahl (prod/dev) ab und lädt das Artefakt entsprechend benannt hoch. `assetlinks.json` listet beide Package-Ids mit demselben Keystore-Fingerprint, sodass ohne Browser-Adressleiste auch im Dev-Build gestartet wird.',
  ]},
  { version: '0.86.0', date: '2026-06-04', changes: [
    'RookHub gibt es jetzt als Android-App: im Konto-Menü (Profilbild oben rechts) führt „App installieren (Android)" zu einer kurzen Anleitung mit Download-Link zur APK.',
    'Der Download-Link zeigt immer auf die neueste veröffentlichte Version.',
  ]},
  { version: '0.85.6', date: '2026-06-04', changes: [
    'Fix (Viz-Modus, Mobile): mehrere Versuche, die Auswahl per chessground-Brush bzw. „selected"-Highlight sichtbar zu kriegen, sind auf manchen Handys schlicht nicht angekommen — keinerlei Markierung beim Tap. Jetzt zeichnen wir den grünen Auswahl-Ring als eigenes DOM-Overlay direkt über dem Brett (absolute positioniert auf das jeweilige Feld, dicker grüner Border) — unabhängig von chessgrounds SVG-Drawable, damit auf wirklich jedem Gerät sichtbar wird, welches Feld angetippt wurde. chessgrounds Brush/Highlight bleiben zusätzlich als Bonus aktiv.',
  ]},
  { version: '0.85.5', date: '2026-06-04', changes: [
    'Fix (Lokalisierung): Beim Aufräumen des „Logs"-Tabs in 0.85.1 ist in den drei i18n-Dateien (en/de/hr) ein überflüssiges Komma stehen geblieben — die JSON-Dateien waren dadurch syntaktisch ungültig, ngx-translate konnte sie nicht parsen und zeigte nur noch die Schlüssel wie „dashboard.friends.manage" statt der Texte. JSON in allen 3 Sprachen repariert; die übrigen 22 Sprachen waren unverändert in Ordnung.',
  ]},
  { version: '0.85.4', date: '2026-06-04', changes: [
    'Fix (Viz-Modus, Mobile): in 0.85.3 wurde die gelbliche „selected"-Markierung beim Tap entfernt und nur noch ein dicker grüner Kreis gezeichnet — auf einigen Handys war dieser Kreis aber nicht mehr sichtbar, sodass beim Tap GAR keine Markierung mehr zu sehen war. Jetzt wieder: gelbliches Feld-Highlight UND grüner Kreis zusammen; der Kreis ist dabei nochmals dicker (Stroke 22 statt 18), damit er auf Mobile prominent steht und das Highlight als verlässliche Rückfallebene da bleibt.',
  ]},
  { version: '0.85.3', date: '2026-06-04', changes: [
    'Visualisierungs-Modus (Mobile): Ein Tap auf ein Feld zeichnet jetzt denselben prominenten grünen Kreis wie ein Rechtsklick auf Desktop — vorher war die Auswahl auf kleinen Brettern nur durch das dezente gelbliche „selected"-Highlight von chessground markiert, das unter dem Finger kaum zu sehen war. Der Kreis-Stroke ist gegenüber der Default-Brush verdickt (lineWidth 18 statt 10), damit er auch auf kleinen Handy-Brettern groß und gut sichtbar ist.',
  ]},
  { version: '0.85.2', date: '2026-06-04', changes: [
    'Tagespuzzle-Fairness: Wer beim Tagespuzzle einen falschen Zug macht und „Zurücksetzen" oder „Mausrutscher" drückt — oder aufgibt — gilt für diesen Tag als nicht-gelöst, selbst wenn er es anschließend doch noch löst. Vorher konnte ein fehlgeschlagener erster Versuch durch einen späteren Solve „überschrieben" werden, sodass derselbe Tag gleichzeitig als gelöst und nicht-gelöst zählte. Server entscheidet jetzt per erstem Versuch je User; Discord-Solver-Liste zeigt den ersten Versuch.',
  ]},
  { version: '0.85.1', date: '2026-06-04', changes: [
    'Fix (Admin): Der Logs-Tab im Admin-Screen warf in Prod „failed to load logs", weil die zugrundeliegende `RequestLogs`-Tabelle samt `/api/request-logs`-Endpoint längst entfernt war (Logs liegen in Elasticsearch/Kibana). Der verwaiste Tab inkl. Filter, Tabelle, State und i18n-Keys wurde entfernt — der Kibana-Dashboard-Link im Header bleibt die einzige Anlaufstelle für Request-Logs.',
    'Admin: „Kibana-Dashboard"-Link öffnet jetzt direkt das RookHub-Logging-Dashboard statt nur die Kibana-Wurzel (Deep-Link auf `app/dashboards#/view/rookhub-logging-dashboard`). Env-Variable `KIBANA_URL` bleibt der Root — die Deep-Link-Zusammensetzung passiert serverseitig in `/api/admin/config`.',
  ]},
  { version: '0.85.0', date: '2026-06-04', changes: [
    'Trainingsziel „Spielen" umgestellt: statt Spielzeit in Minuten/Tag zählt jetzt die Anzahl gespielter Rapid-/Classical-Partien pro Woche.',
    'Gezählt werden auf Lichess Rapid- und Classical-Partien, auf chess.com Rapid-Partien (chess.com hat keine eigene „Classical"-Kategorie). Bullet, Blitz und Korrespondenz/Daily zählen nicht.',
    'Das Spielen-Ziel ist damit ein Wochenziel und wird separat angezeigt („X / Y Partien diese Woche"); der Tages-Stern im Tracker richtet sich weiterhin nach Puzzles und Buch/Kurs.',
    'Hinweis: Bestehende „Spielen"-Ziele (in Minuten) wurden zurückgesetzt und müssen als Partien pro Woche neu gesetzt werden; die bisher erfasste Spielzeit wird neu als Partienzahl aufgebaut.',
  ]},
  { version: '0.84.2', date: '2026-06-04', changes: [
    'Übersetzungen vervollständigt: der Trainingsziele-Bereich (Tagesziele, Ziele-Tracker, Coach-Vorlage in der Gruppenverwaltung) ist jetzt auch in den 22 zusätzlichen Sprachen übersetzt — damit sind alle 25 Sprachen vollständig lokalisiert.',
  ]},
  { version: '0.84.1', date: '2026-06-04', changes: [
    'Lokalisierung vervollständigt: die zuletzt hinzugekommenen Oberflächen-Texte (Extension-Tokens, Repertoire-Kategorie, Tagespuzzle-Navigation, „Offline-Pool aufgebraucht") sind jetzt in allen 25 Sprachen übersetzt — vorher fielen sie in den 22 zusätzlichen Sprachen auf Englisch zurück.',
  ]},
  { version: '0.84.0', date: '2026-06-04', changes: [
    'Neuer Trainingsziele-Modus (Trainingsunterstützung für Schüler):',
    'Eigene Tagesziele je Kategorie festlegen — Puzzles, Buch-/Kursstudie und Spielen (in Minuten) — plus Wochenziel (an wie vielen Tagen pro Woche alle Ziele erreicht werden sollen).',
    'Coach/Admin kann pro Gruppe eine Ziel-Vorlage hinterlegen; Schüler übernehmen sie automatisch und dürfen sie für sich anpassen (oder auf die Vorlage zurücksetzen).',
    'Zweiter Aktivitäts-Tracker als Kalender-Heatmap: pro Tag ein Stern, wenn alle Ziele erreicht wurden, ein Teil-Symbol bei teilweiser Erfüllung — zusätzlich zur bestehenden Puzzle-Heatmap.',
    'Die Spielzeit auf Lichess/chess.com wird automatisch über die jeweilige öffentliche API erfasst (verknüpfter Benutzername vorausgesetzt) und in die Kategorie „Spielen" eingerechnet.',
  ]},
  { version: '0.83.0', date: '2026-06-04', changes: [
    'Google-Play-Vorbereitung & Mehrsprachigkeit (großes Update):',
    'Echte App-Icons (192/512 px + „maskable") + vervollständigtes Web-App-Manifest → RookHub ist eine sauber installierbare PWA.',
    'Konto selbst löschen (DSGVO): Profil → „Konto löschen" mit Passwort-Bestätigung; Identität und persönliche Daten werden entfernt, anonymisierte Statistiken bleiben erhalten. Neue öffentliche Seite /account-deletion.',
    'Neue öffentliche Rechtsseiten: Datenschutzerklärung (/privacy) und Impressum (/impressum), auf der Anmeldeseite verlinkt.',
    '22 neue Sprachen — die App ist jetzt in 25 Sprachen verfügbar (u. a. Spanisch, Chinesisch, Hindi, Arabisch, Portugiesisch, Französisch, Russisch, Japanisch, Koreanisch …), inklusive Rechts-nach-links-Darstellung für Arabisch und Persisch.',
    'Technische Grundlagen für die spätere Android-App im Google Play Store (Digital Asset Links, TWA-Build-Gerüst) und zentrale Konfiguration der Impressum-Betreiberdaten.',
  ]},
  { version: '0.82.4', date: '2026-06-04', changes: [
    'Android-TWA: Digital Asset Links unter `/.well-known/assetlinks.json` mit dem App-Signatur-Fingerprint ausgeliefert — verknüpft die installierte App mit der Domain, sodass die TWA ohne Browser-Adressleiste (full-screen) läuft.',
  ]},
  { version: '0.82.3', date: '2026-06-04', changes: [
    'Standard-Puzzle Offline: Wenn der vorab gespeicherte Puzzle-Pool aufgebraucht ist (alle gespielt), sieht man jetzt einen klaren Hinweis „Keine neuen Offline-Puzzles mehr" mit einem Button „Letztes Puzzle nochmal spielen" — statt dem unklaren „nichts gespeichert"-Bildschirm bzw. dem stillen Wiederholen desselben Puzzles. Sobald man wieder online ist, lädt die Seite automatisch neue Puzzles nach.',
  ]},
  { version: '0.82.2', date: '2026-06-04', changes: [
    'Fix (Mobile, Visualisierungs-Modus): Wenn man im Viz-Modus (≥ Stufe 1) auf ein Feld tippte, war die Auswahl auf dem Handy kaum erkennbar — nur ein dünner Kreis-Stroke, der unter dem Finger oft verschwand. Jetzt wird das angetippte Feld zusätzlich vollflächig hervorgehoben (chessground-„selected", gelblich) und der grüne Kreis bleibt als zweiter Akzent. Chessgrounds eigene Interaktion ist im Viz-Modus abgeschaltet, damit der Tap nicht gleichzeitig die Auswahl wieder löscht.',
  ]},
  { version: '0.82.1', date: '2026-06-04', changes: [
    'Endlos-Config-Screen: „Weiterspielen / Lauf archivieren + Neu starten / Start"-Block sitzt jetzt ganz oben — sofort sichtbar ohne erst durch Start-Rating, Schwellenwerte, Visualisierung etc. zu scrollen. Die Config-Felder bleiben darunter zum Feintunen vor dem nächsten Lauf.',
  ]},
  { version: '0.82.0', date: '2026-06-04', changes: [
    'Endlos: kompakte „Quick-Stats"-Pillen-Reihe direkt unter dem Brett — Puzzle-Rating, aktuelles Level und Herzen sind jetzt auch auf kleinen Handy-Screens ohne Scrollen sichtbar (vorher steckten diese Infos in der Stats-Karte unter Visualisierungs-/Status-Karte).',
  ]},
  { version: '0.81.1', date: '2026-06-04', changes: [
    'Fix (Mobile, Endlos): Nach dem Lösen sah die „Gelöst"-Karte unter dem Brett ungewohnt schmal aus, weil die Info-Sektion auf Mobile (flex-Spalte) ihre Breite vom Inhalt nahm statt 100 % zu spannen. Beide Sektionen (Brett + Status-Karte) werden jetzt im Mobile-Layout auf volle Breite gesetzt.',
  ]},
  { version: '0.81.0', date: '2026-06-03', changes: [
    'Tagespuzzle-Ansicht (`/puzzles/daily/:date`): keine Buch-Navigation („Nächstes/Zufällig aus Buch") mehr, sondern Datums-Navigation — „Voriger Tag" immer, „Nächster Tag" nur, wenn man in der Vergangenheit ist (keine Zukunft). Browser-Vor/Zurück funktionieren ebenfalls.',
    'Neuer Schnellzugriff „Tagespuzzle" auf der Dashboard-Puzzles-Karte (neben Lösen/Endlos) → öffnet das heutige Tagespuzzle.',
  ]},
  { version: '0.80.1', date: '2026-06-03', changes: [
    'Fix (kritisch): Die API startete nicht mehr — beim Auth-Setup kollidierte der JWT-Handler mit dem Policy-Scheme „Bearer" (beide unter dem Namen „Bearer" registriert → „Scheme already exists: Bearer", Startup-Crash). Der JWT-Handler läuft jetzt unter dem Namen „Jwt"; das Policy-Scheme „Bearer" bleibt der Default und leitet je nach Token (`rkh_…` → ApiToken, sonst → JWT) weiter. Behebt den API-Ausfall seit dem Auth-/API-Token-Feature.',
  ]},
  { version: '0.80.0', date: '2026-06-03', changes: [
    'Tagespuzzle ist jetzt über einen stabilen, datumsbasierten Link erreichbar: neue Route `/puzzles/daily/:date` (z.B. `/puzzles/daily/20260603`) lädt das Tagespuzzle des jeweiligen UTC-Datums. Der Schach-Bot verlinkt seine Tagespuzzle-Posts ab sofort dorthin (statt auf die wechselnde Puzzle-ID `/puzzles/book/{id}`) — derselbe Link zeigt dauerhaft auf das Tagespuzzle dieses Tages.',
  ]},
  { version: '0.79.1', date: '2026-06-03', changes: [
    'Extension-Endpoint (`/api/extension/*`): Scope-Guard — ein API-Token darf den Endpoint nur nutzen, wenn `scope=extension` (sichert frühe Trennung von Maschinen-Token-Berechtigungen für künftige Scopes). JWT-User dürfen weiter wie bisher. CORS-Policy `AllowCredentials` raus (Auth läuft strikt über Bearer-Token im Header, Cookies nicht gebraucht); CORS auf `GET` reduziert.',
  ]},
  { version: '0.79.0', date: '2026-06-03', changes: [
    'Extension-Tokens: Sektion „Extension-Tokens" im Profil zum Erstellen / Widerrufen persönlicher API-Tokens (rkh_…-Format, GitHub-PAT-Style). Beim Anlegen wird der Raw-Token einmalig im Modal mit Copy-Button und Warnhinweis gezeigt — danach nur noch der Prefix sichtbar. Optionaler Ablauf (30/90/365 Tage oder nie). Backend-Endpoints `GET/POST/DELETE /api/profile/tokens`. Vorbereitung für die chess.com-Tampermonkey-Erweiterung, die per Bearer-Token (Scope `extension`) auf `/api/extension/*` zugreifen wird.',
  ]},
  { version: '0.78.1', date: '2026-06-03', changes: [
    'Repertoire-Kategorie hinzu: Beim Anlegen wählst du jetzt eine Kategorie (Eröffnung / Mittelspiel / Endspiel / —). In der Liste wird die Kategorie als farbiger Chip neben dem Namen angezeigt. Backend-Feld `Repertoire.Kind` (Enum `None|Opening|Middlegame|Endgame`), Migration `AddRepertoireKind`. Vorbereitung für die chess.com-Extension, die per `GET /api/extension/repertoires?kind=opening` gezielt nur Eröffnungs-PGNs holen wird; der Endpoint liefert zusätzlich `totalSizeBytes` pro Repertoire als Hinweis für Client-side-Limits.',
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
