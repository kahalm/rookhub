import { InjectionToken, inject } from '@angular/core';
import { Location } from '@angular/common';
import { Router } from '@angular/router';
import { OPERATOR } from '../../../environments/operator';
import { AuthService } from '../../core/auth.service';
import { GUEST_START_URL } from '../../core/handoff.service';

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
  /** Ziel des Ruecklinks am Ende der Rechtsseiten, wenn es keinen Schritt zurueck gibt; fehlt = die Startseite
   *  (angemeldet `/`, abgemeldet GUEST_START_URL — z1-rk-legal-back-anon; abgemeldet in LeagueHub/ClubHub `/login`,
   *  dort die einzige offene Seite). KidHub: `/` — ein Kind soll zurueck zum Spiel, nicht auf die Anmeldemaske;
   *  Turnierseite: der Kalender (ihre Startseite, auch abgemeldet offen). */
  back?: string;
  /** Wo das Konto gefuehrt wird, wenn nicht hier. KidHub, LeagueHub, ClubHub und die Turnierseite haben keine Karte
   *  „Konto loeschen" — es ist dasselbe RookHub-Konto, geloescht wird es in RookHubs Profil. Die Loeschseite sagt das
   *  und verlinkt dorthin (Codereview UX-023). Fehlt = hier: RookHub selbst, Knopf auf {@link ACCOUNT_DELETE_ROUTE}. */
  accountHome?: 'rookhub';
  /** Nur LeagueHub (0.698.0): der Verein, für den das Konto LeagueHub gerade nutzt — die Datenschutzerklärung nennt ihn;
   *  `null` = unbekannt (abgemeldet, kein Verein). */
  leagueClub?: () => string | null;
}

/** Wo RookHub die Karte „Konto loeschen" hat: im Profil, per `?section=delete` aufgeklappt und angesprungen
 *  (ProfileComponent). Der Knopf auf /account-deletion fuehrt dorthin — Abgemeldete ueber die Anmeldung (authGuard
 *  mit returnUrl), von den anderen Oberflaechen aus ueber RookHubs Adresse (Codereview UX-023). */
export const ACCOUNT_DELETE_ROUTE = '/profile';
export const PROFILE_SECTION_PARAM = 'section';
export const ACCOUNT_DELETE_SECTION = 'delete';
export const ACCOUNT_DELETE_QUERY: Readonly<Record<string, string>> = { [PROFILE_SECTION_PARAM]: ACCOUNT_DELETE_SECTION };

/** `turnier` (UI-Sweep 2026-10-11, r2-t-privacy-intro): eigene Einleitung, der Abschnitt „Turnierdaten" steht oben. */
export type LegalSiteKind = 'rookhub' | 'kidhub' | 'leaguehub' | 'clubhub' | 'turnier';

/** Vorgabe fuer RookHub und die Turnierseite. */
export function defaultLegalSite(): LegalSite {
  return { contactEmail: OPERATOR.email, imprint: true };
}

/** Oberflaechen, die abgemeldet NUR die Anmeldung zeigen (jede andere Seite verlangt ein Konto): dort ist /login die
 *  Startseite eines Gasts. */
const LOGIN_ONLY_KINDS: readonly LegalSiteKind[] = ['leaguehub', 'clubhub'];

/** Ersatz-Ruecklink der Rechtsseiten ohne Verlauf (Lesezeichen, Link aus einer Mail): die Startseite der Oberflaeche —
 *  ihr eigenes Ziel `back`, sonst angemeldet `/`, abgemeldet RookHubs Gast-Startseite (GUEST_START_URL, wohin auch die
 *  Route `''` Gaeste schickt) — mit „Zurueck zur Startseite". Abgemeldet fuehrt er nur in LeagueHub/ClubHub zur Anmeldung (dort ist sie die einzige
 *  offene Seite, Text der Seite „Zurueck zur Anmeldung"); in RookHub stand das vorher auch ohne Verlauf da, obwohl man
 *  nie auf /login war (UI-Sweep z1-rk-legal-back-anon, nach x-back-login). */
export function legalBack(site: LegalSite, loginLabel: string, loggedIn = false): { link: string; label: string } {
  const login = !loggedIn && !!site.kind && LOGIN_ONLY_KINDS.includes(site.kind);
  // Abgemeldet direkt die Gast-Startseite statt `/` (die Route '' leitet nur weiter) — der Link trifft seinen Weg (UX-003).
  const link = site.back ?? (login ? '/login' : loggedIn ? '/' : GUEST_START_URL);
  return { link, label: link === '/login' ? loginLabel : 'legal.backHome' };
}

/** Ruecklink am Ende einer Rechtsseite, wie ihn {@link legalBackLink} liefert. */
export interface LegalBackLink {
  /** Ersatzziel ohne Verlauf (`/` bzw. das `back` der Oberflaeche) — als routerLink, damit die Routen-Specs es pruefen. */
  link: string;
  /** Schon fertig gewaehlter i18n-Schluessel: „Zurueck" mit Verlauf (von /login aus „Zurueck zur Anmeldung"),
   *  sonst der Text des Ersatzziels. */
  label: string;
  /** Kam man innerhalb der App hierher? Dann fuehrt der Link zurueck, wo man war. */
  history: boolean;
  /** Adresse des Ersatzziels — fuer Strg-/Mittelklick (neuer Tab), wenn der Link zurueck fuehrt. */
  href: string;
  /** Klick-Behandlung: mit Verlauf ein Schritt zurueck (Location.back()), sonst nichts (der routerLink gilt). */
  go(event: MouseEvent): void;
}

/**
 * Ruecklink der Rechtsseiten (Codereview UX-017): Wer aus der App kam — Fusszeile, ☰-Menue, Anmeldemaske, eingeloggt
 * vom Dashboard —, kommt mit „Zurueck" genau dorthin zurueck, statt immer auf /login zu landen (eingeloggt leitete das
 * ohnehin aufs Dashboard um). Direkt aufgerufen (Lesezeichen, Mail, Play Store, neuer Tab) gibt es keinen Schritt
 * zurueck in der App: dann das Ziel aus {@link legalBack}. „Zur Anmeldung" heisst er nur, wenn man von /login kam
 * (UI-Sweep 2026-10-10, x-back-login). Nur im Injektionskontext aufrufen (Feld-Initialisierer).
 */
export function legalBackLink(loginLabel: string): LegalBackLink {
  const router = inject(Router);
  const location = inject(Location);
  const { link, label } = legalBack(inject(LEGAL_SITE), loginLabel, inject(AuthService).isLoggedIn);
  // Waehrend die Seite entsteht, laeuft ihre eigene Navigation noch — die letzte ERFOLGREICHE ist die Seite davor.
  const previous = router.lastSuccessfulNavigation();
  const history = previous !== null;
  const fromLogin = !!previous && router.serializeUrl(previous.finalUrl ?? previous.extractedUrl).split(/[?#]/)[0] === '/login';
  return {
    link,
    label: history ? (fromLogin ? loginLabel : 'common.back') : label,
    history,
    href: router.serializeUrl(router.parseUrl(link)),
    go: (e: MouseEvent) => {
      if (!history || e.button !== 0 || e.ctrlKey || e.metaKey || e.shiftKey || e.altKey) return;
      e.preventDefault();
      location.back();
    },
  };
}

export const LEGAL_SITE = new InjectionToken<LegalSite>('LEGAL_SITE', {
  providedIn: 'root',
  factory: defaultLegalSite,
});
