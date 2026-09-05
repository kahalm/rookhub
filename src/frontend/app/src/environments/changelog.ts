// Single Source of Truth fuer App-Version + Changelog-TYPEN.
// Wird von BEIDEN Environment-Dateien importiert (environment.ts = dev,
// environment.prod.ts = prod-Build via fileReplacements). Dadurch zeigt der
// Footer in JEDEM Build dieselbe Version — ein Bump aendert nur hier.
//
// Die Changelog-EINTRAEGE (CHANGELOG-Array) liegen in changelog-data.ts:
// ~0,9 MB Prosa, die das Overlay per dynamic import() erst beim Oeffnen laedt.
// Neue Eintraege gehoeren dort hinein; diese Datei bleibt bewusst winzig,
// damit sie ohne Bundle-Kosten eager importierbar ist (Version im Footer).
export const APP_VERSION = '0.395.1';
/** Bump this integer whenever a new APK must be installed by existing users. */
export const APK_VERSION = 2;

export interface ChangelogEntry {
  version: string;
  date: string;
  /** Zweisprachig: en (Default/Fallback) + de. Footer zeigt die aktive UI-Sprache. */
  changes: { en: string; de: string }[];
}
