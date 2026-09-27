import { InjectionToken } from '@angular/core';
import { OPERATOR } from '../../../environments/operator';

/**
 * Was die Rechtsseiten je Oberflaeche zeigen. RookHub und die Turnierseite nehmen die Vorgabe
 * (Impressum, Kontakt aus `OPERATOR`); KidHub setzt seine eigene (`kidhubConfig`): kein Impressum
 * (Wunsch des Nutzers, 2026-09-27) und eine eigene Adresse fuer Datenschutzfragen.
 */
export interface LegalSite {
  /** Kontakt fuer Datenschutzfragen (Datenschutzerklaerung). */
  contactEmail: string;
  /** Impressum verlinken? Ohne nennt die Datenschutzerklaerung den Verantwortlichen ueber die Kontaktadresse. */
  imprint: boolean;
}

/** Vorgabe fuer RookHub und die Turnierseite. */
export function defaultLegalSite(): LegalSite {
  return { contactEmail: OPERATOR.email, imprint: true };
}

export const LEGAL_SITE = new InjectionToken<LegalSite>('LEGAL_SITE', {
  providedIn: 'root',
  factory: defaultLegalSite,
});
