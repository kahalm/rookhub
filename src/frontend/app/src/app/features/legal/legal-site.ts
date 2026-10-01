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
  /** Impressum verlinken? Ohne nennt die Datenschutzerklaerung beim Verantwortlichen direkt die Kontaktadresse
   *  dieser Oberflaeche (kein Name/keine Anschrift — Entscheidung des Betreibers, 2026-09-30). */
  imprint: boolean;
  /** Welche Oberflaeche: waehlt in der Datenschutzerklaerung Einleitung und Zusatzabschnitte. Fehlt = RookHub
   *  (auch die Turnierseite). KidHub bekommt eine Fassung in einfacher Sprache mit Elternhinweis (Codereview F7-003),
   *  LeagueHub den Abschnitt ueber die Daten der Ligaspieler ohne Konto (Art. 14 DSGVO, Codereview F7-006). */
  kind?: LegalSiteKind;
  /** Ziel des Ruecklinks am Ende der Rechtsseiten; fehlt = `/login`. KidHub: `/` — ein Kind soll zurueck zum
   *  Spiel, nicht auf die Anmeldemaske. */
  back?: string;
  /** Wo das Konto gefuehrt wird, wenn nicht hier. KidHub, LeagueHub, ClubHub und die Turnierseite haben keine Karte
   *  „Konto loeschen" — es ist dasselbe RookHub-Konto, geloescht wird es in RookHubs Profil. Die Loeschseite sagt das
   *  und verlinkt dorthin (Codereview UX-023). Fehlt = hier: RookHub selbst, Knopf auf {@link ACCOUNT_DELETE_ROUTE}. */
  accountHome?: 'rookhub';
}

/** Wo RookHub die Karte „Konto loeschen" hat: im Profil, per `?section=delete` aufgeklappt und angesprungen
 *  (ProfileComponent). Der Knopf auf /account-deletion fuehrt dorthin — Abgemeldete ueber die Anmeldung (authGuard
 *  mit returnUrl), von den anderen Oberflaechen aus ueber RookHubs Adresse (Codereview UX-023). */
export const ACCOUNT_DELETE_ROUTE = '/profile';
export const PROFILE_SECTION_PARAM = 'section';
export const ACCOUNT_DELETE_SECTION = 'delete';
export const ACCOUNT_DELETE_QUERY: Readonly<Record<string, string>> = { [PROFILE_SECTION_PARAM]: ACCOUNT_DELETE_SECTION };

export type LegalSiteKind = 'rookhub' | 'kidhub' | 'leaguehub';

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
