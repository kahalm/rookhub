/**
 * Zeilen der Fristen-Tabelle in der Datenschutzerklärung (Codereview R-2 a / O-7, Stand 2026-10-26). Quelle der Fristen:
 * `docs/api/speicherdauer.md` — neue Frist dort UND hier eintragen (Texte unter `legal.privacy.periods.<id>.data|period`
 * in de/en/hr/hu). Reine Daten, ohne Angular.
 */
export interface PrivacyPeriodGroup {
  /** i18n-Schlüssel des Zwischentitels. */
  title: string;
  /** Kennungen der Zeilen (`legal.privacy.periods.<id>.*`). */
  rows: readonly string[];
}

export const PRIVACY_PERIODS: readonly PrivacyPeriodGroup[] = [
  { title: 'legal.privacy.periodsGroupAccount',
    rows: ['account', 'notifications', 'messages', 'scoresheetFailed', 'scoresheetSaved', 'scoresheetArchive', 'sharedLogin', 'linkCodes'] },
  { title: 'legal.privacy.periodsGroupAnon',
    rows: ['endless', 'attempts', 'extension', 'leagueDrafts', 'scoresheetAnon', 'ipHash', 'ebooks'] },
  { title: 'legal.privacy.periodsGroupOther', rows: ['logs', 'backups', 'botDm'] },
];

/** i18n-Schlüssel einer Zelle. */
export function privacyPeriodKey(row: string, col: 'data' | 'period'): string {
  return 'legal.privacy.periods.' + row + '.' + col;
}
