export const environment = {
  production: false,
  version: '0.13.0',
  changelog: [
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
  ]
};
