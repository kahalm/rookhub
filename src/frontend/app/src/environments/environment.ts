export const environment = {
  production: false,
  version: '0.8.3',
  changelog: [
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
