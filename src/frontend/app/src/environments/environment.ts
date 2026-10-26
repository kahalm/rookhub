import { APP_VERSION } from './changelog';

// KEIN CHANGELOG-Import hier: das Array (changelog-data.ts, ~2,2 MB) ist DATEN — der Build schreibt es nach
// /assets/changelog.json (scripts/changelog-json.mjs), das Changelog-Overlay laedt es per HttpClient erst beim
// Oeffnen (Codereview F8-008). Ein Import hier zoege es zurueck ins Initial-Bundle.
export const environment = {
  production: false,
  version: APP_VERSION,
};
