export const environment = {
  production: false,
  version: '0.6.4',
  changelog: [
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
