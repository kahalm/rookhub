import { ApplicationConfig, Component, Type } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Route, Router, RouterLink, Routes, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { authGuard } from '../core/auth.guard';
import { ForgotPasswordComponent } from '../features/auth/forgot-password.component';
import { LoginComponent } from '../features/auth/login.component';
import { RegisterComponent } from '../features/auth/register.component';
import { ResetPasswordComponent } from '../features/auth/reset-password.component';
import { AccountDeletionComponent } from '../features/legal/account-deletion.component';
import { ImpressumComponent } from '../features/legal/impressum.component';
import { LEGAL_SITE } from '../features/legal/legal-site';
import { PrivacyComponent } from '../features/legal/privacy.component';

/**
 * Prueft fuer eine App (RookHub, Turnierseite, KidHub, LeagueHub, ClubHub), ob jeder Link der geteilten
 * Anmelde- und Rechtsseiten dort einen eigenen Weg hat (Codereview UX-003).
 *
 * Hintergrund: die Seiten kommen ueber `@rh/*` aus RookHub, die Routen schreibt jede App selbst. Fehlte
 * ein Weg, fing ihn der Catch-all ('**' bzw. RookHubs ':slug') — auf der Turnierseite drehten sich
 * „Passwort vergessen?", Datenschutz und Impressum so im Kreis zurueck auf /login, auf KidHub fuehrte
 * der Link zur Konto-Loeschung still auf die Startseite.
 *
 * Nichts davon steht als Liste in den Specs: welche geteilten Seiten die App einbindet, ergibt sich
 * aus ihren Routen (lazy Komponenten werden geladen), welche Links es gibt, aus dem GERENDERTEN
 * Template — mit dem LEGAL_SITE der App, damit seitenabhaengige Links (KidHub: kein Impressum,
 * Ruecklink auf '/') so geprueft werden, wie sie dort erscheinen. Neue Links pruefen sich damit
 * von selbst mit.
 *
 * Ein Link ist in Ordnung, wenn der echte Router ihn auf die Route mit GENAU diesem Pfad fuehrt (nicht
 * auf einen Catch-all, Parameter-Weg oder ueber eine Umleitung woandershin) und die Route keine
 * Anmeldung verlangt — die Seiten werden abgemeldet besucht. Ausnahme: ein Link mit dem Attribut
 * `data-login-required` will bewusst ueber die Anmeldung (authGuard mit returnUrl) an sein Ziel, z. B.
 * „Konto jetzt loeschen" ins Profil (UX-023); auch er muss aber seine eigene Route treffen. Geprueft
 * wird gegen dieselbe Tabelle in derselben Reihenfolge, nur ohne Guards und Lazy-Chunks (wie in
 * src/app/app.routes.spec.ts).
 */

/** Die geteilten Anmelde- und Rechtsseiten. */
export const SHARED_AUTH_LEGAL_PAGES: readonly Type<unknown>[] = [
  LoginComponent, RegisterComponent, ForgotPasswordComponent, ResetPasswordComponent,
  PrivacyComponent, ImpressumComponent, AccountDeletionComponent,
];

/**
 * Zustaende, in denen eine Seite zusaetzliche Links zeigt. Getypt: wer das Feld umbenennt, muss hier
 * nachziehen, statt dass der Link still aus der Pruefung faellt.
 */
const EXTRA_STATES = new Map<Type<unknown>, (page: unknown) => void>([
  // Name/E-Mail vergeben → „Zur Anmeldung"
  inState(RegisterComponent, p => p.error.set('taken')),
  // Abgelaufener Link → „Neuen Link anfordern"
  inState(ResetPasswordComponent, p => { p.token = 'spec'; p.error.set('expired'); }),
]);

function inState<T>(page: Type<T>, apply: (p: T) => void): [Type<unknown>, (page: unknown) => void] {
  return [page, apply as (page: unknown) => void];
}

@Component({ standalone: true, template: '' })
class StubRouteComponent {}

export interface SharedPageLinkReport {
  /** Pfade, unter denen die App eine geteilte Seite einbindet. */
  mounted: string[];
  /** Jeder gefundene Link als „Seite → /ziel". */
  links: string[];
  /** Was nicht stimmt — leer, wenn alles passt. */
  problems: string[];
}

function isSharedPage(c: unknown): c is Type<unknown> {
  return SHARED_AUTH_LEGAL_PAGES.includes(c as Type<unknown>);
}

/** Die Komponente einer Route — lazy geladen, falls noetig. Nur statische Pfade: die geteilten Seiten haben keine Parameter. */
async function componentOf(route: Route): Promise<unknown> {
  if (route.component) return route.component;
  if (!route.loadComponent) return undefined;
  const loaded = await (route.loadComponent() as Promise<unknown>);
  return (loaded as { default?: unknown })?.default ?? loaded;
}

export async function checkSharedPageLinks(routes: Routes, config?: ApplicationConfig): Promise<SharedPageLinkReport> {
  const mounted: { path: string; page: Type<unknown> }[] = [];
  for (const r of routes) {
    if (typeof r.path !== 'string' || r.path.includes(':') || r.path === '**' || r.redirectTo !== undefined) continue;
    const c = await componentOf(r);
    if (isSharedPage(c)) mounted.push({ path: r.path, page: c });
  }

  // Dieselbe Tabelle in derselben Reihenfolge, ohne Guards und Lazy-Chunks.
  const stubRoutes = routes.map(r => (r.redirectTo !== undefined
    ? { path: r.path, pathMatch: r.pathMatch, redirectTo: r.redirectTo }
    : { path: r.path, pathMatch: r.pathMatch, component: StubRouteComponent }) as Route);

  const legalSite = (config?.providers ?? []).filter(p => (p as { provide?: unknown })?.provide === LEGAL_SITE);
  TestBed.configureTestingModule({
    providers: [
      provideRouter(stubRoutes), provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
      provideTranslateService({ fallbackLang: 'en' }), ...legalSite,
    ],
  });
  const router = TestBed.inject(Router);

  const links: { from: string; path: string; loginRequired: boolean }[] = [];
  for (const { path: from, page } of mounted) {
    const fixture = TestBed.createComponent(page);
    const collect = () => {
      fixture.detectChanges();
      for (const de of fixture.debugElement.queryAll(By.directive(RouterLink))) {
        const tree = de.injector.get(RouterLink).urlTree;
        if (!tree) continue;
        const path = (tree.root.children['primary']?.segments ?? []).map(s => s.path).join('/');
        const loginRequired = (de.nativeElement as Element).hasAttribute('data-login-required');
        if (!links.some(l => l.from === from && l.path === path)) links.push({ from, path, loginRequired });
      }
    };
    collect();
    const extra = EXTRA_STATES.get(page);
    if (extra) { extra(fixture.componentInstance); collect(); }
    fixture.destroy();
  }

  const problems: string[] = [];
  if (mounted.length === 0) problems.push('die App bindet keine der geteilten Anmelde- und Rechtsseiten ein');
  for (const { from, path, loginRequired } of links) {
    await router.navigateByUrl('/' + path);
    // Der Router kopiert die Routen beim Einlesen; die Stelle in router.config fuehrt zur echten Route zurueck.
    const hit = router.routerState.snapshot.root.firstChild?.routeConfig;
    const target = hit ? routes[router.config.indexOf(hit)] : undefined;
    if (!target || target.path !== path) {
      problems.push(`/${from} verlinkt /${path}, der Router landet aber auf '${target?.path ?? '—'}' (Weg fehlt)`);
    } else if (target.canActivate?.includes(authGuard) && !loginRequired) {
      problems.push(`/${from} verlinkt /${path}, der Weg verlangt aber eine Anmeldung`);
    }
  }

  return {
    mounted: mounted.map(m => m.path),
    links: links.map(l => `/${l.from} → /${l.path}`),
    problems,
  };
}
