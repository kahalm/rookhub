// Single Source of Truth fuer App-Version + Changelog-TYPEN.
// Wird von BEIDEN Environment-Dateien importiert (environment.ts = dev,
// environment.prod.ts = prod-Build via fileReplacements). Dadurch zeigt der
// Footer in JEDEM Build dieselbe Version — ein Bump aendert nur hier.
//
// Die Changelog-EINTRAEGE (CHANGELOG-Array) liegen in changelog-data.ts:
// ~2,2 MB Prosa (Stand Oktober 2026). Der Build macht daraus /assets/changelog.json,
// das Overlay laedt es per HttpClient erst beim Oeffnen (Codereview F8-008).
// Neue Eintraege gehoeren dort hinein; diese Datei bleibt bewusst winzig,
// damit sie ohne Bundle-Kosten eager importierbar ist (Version im Footer).
export const APP_VERSION = '0.861.0';
/** Bump this integer whenever a new APK must be installed by existing users. */
export const APK_VERSION = 3;

export interface ChangelogEntry {
  version: string;
  date: string;
  /** Zweisprachig: en (Default/Fallback) + de. Footer zeigt die aktive UI-Sprache. */
  changes: { en: string; de: string }[];
}
