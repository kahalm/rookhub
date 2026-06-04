// Betreiber-/Impressum-Daten — sprachneutral, NICHT in den i18n-Dateien.
// Einzige Stelle zum Pflegen; vor dem Go-live mit echten Daten ausfüllen.
export const OPERATOR = {
  /** Name oder Firma des Diensteanbieters. */
  name: '[NAME / FIRMA — bitte eintragen]',
  /** Vollständige Anschrift (Straße, PLZ, Ort, Land). */
  address: '[Anschrift — Straße, PLZ, Ort, Land]',
  /** UID/USt-IdNr., falls Unternehmen — leer lassen, wenn nicht zutreffend. */
  vatId: '',
  /** Kontakt-E-Mail für Datenschutz, Impressum und Konto-Löschung. */
  email: 'p.oberschmid@cp-solutions.at',
} as const;
