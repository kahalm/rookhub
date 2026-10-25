/**
 * Weiterleitung: `LEGAL_SITE` und seine Helfer liegen seit Codereview F8-013 in `core/legal-site.ts` (die geteilte
 * Fußzeile in `shared/` darf nicht aus `features/` importieren). Nur noch für die Turnierseite (`src-turnier`, die in
 * der laufenden Zerlegungs-Welle eine andere Sitzung umbaut) — danach dort auf `@rh/core/legal-site` umstellen und
 * diese Datei löschen.
 */
export * from '../../core/legal-site';
