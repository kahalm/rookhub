import { InjectionToken } from '@angular/core';

/**
 * App-eigene Zeilen der Anmelde- und Registriermaske (Codereview UX-027), als i18n-Schlüssel — nach dem Muster von
 * LEGAL_SITE. Auf der Turnierseite ist die Maske die Startseite jedes neuen Besuchers (alles außer /t/:id braucht ein
 * Konto); ohne diese Zeile erfuhr er nichts über das Angebot und nicht, dass sein RookHub-Konto hier gilt.
 * RookHub selbst setzt nichts (offene Bereiche wie Puzzles und Analyse erklären sich selbst).
 */
export interface AuthIntro {
  /** Über dem Anmeldeformular: was die Seite bietet, welches Konto gilt. */
  login?: string;
  /** Über dem Registrierformular: wofür das neue Konto noch gilt. */
  register?: string;
}

export const AUTH_INTRO = new InjectionToken<AuthIntro>('AUTH_INTRO', {
  providedIn: 'root',
  factory: () => ({}),
});
