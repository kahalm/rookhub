export const environment = {
  version: '0.4.0',
  changelog: [
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
