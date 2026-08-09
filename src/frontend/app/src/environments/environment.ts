import { APP_VERSION } from './changelog';

// KEIN CHANGELOG-Import hier: das Array (changelog-data.ts, ~0,9 MB) wird vom
// Changelog-Overlay per dynamic import() erst beim Oeffnen geladen — ein
// statischer Import hier zoege es zurueck ins Initial-Bundle.
export const environment = {
  production: false,
  version: APP_VERSION,
};
