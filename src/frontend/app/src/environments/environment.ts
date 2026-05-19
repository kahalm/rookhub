export const environment = {
  version: '0.5.2',
  changelog: [
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
