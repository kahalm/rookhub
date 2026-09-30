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
  /** Impressum verlinken? Ohne nennt die Datenschutzerklaerung den Verantwortlichen mit Name und Anschrift
   *  (aus `OPERATOR`) und der Kontaktadresse — Pflichtangabe nach Art. 13 Abs. 1 lit. a DSGVO. */
  imprint: boolean;
  /** Welche Oberflaeche: waehlt in der Datenschutzerklaerung Einleitung und Zusatzabschnitte. Fehlt = RookHub
   *  (auch die Turnierseite). KidHub bekommt eine Fassung in einfacher Sprache mit Elternhinweis (Codereview F7-003). */
  kind?: LegalSiteKind;
  /** Ziel des Ruecklinks am Ende der Rechtsseiten; fehlt = `/login`. KidHub: `/` — ein Kind soll zurueck zum
   *  Spiel, nicht auf die Anmeldemaske. */
  back?: string;
}

export type LegalSiteKind = 'rookhub' | 'kidhub';

/** Vorgabe fuer RookHub und die Turnierseite. */
export function defaultLegalSite(): LegalSite {
  return { contactEmail: OPERATOR.email, imprint: true };
}

/** Ruecklink der Rechtsseiten: `/login` behaelt den Text der Seite („Zurueck zur Anmeldung"), jedes andere Ziel
 *  heisst „Zurueck zur Startseite". */
export function legalBack(site: LegalSite, loginLabel: string): { link: string; label: string } {
  const link = site.back ?? '/login';
  return { link, label: link === '/login' ? loginLabel : 'legal.backHome' };
}

export const LEGAL_SITE = new InjectionToken<LegalSite>('LEGAL_SITE', {
  providedIn: 'root',
  factory: defaultLegalSite,
});
