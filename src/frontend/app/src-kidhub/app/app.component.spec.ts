import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter, Router } from '@angular/router';
import { Component } from '@angular/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { SwUpdate } from '@angular/service-worker';
import { Subject } from 'rxjs';
import { isHomeUrl, KidHubAppComponent, KIDS_LANGUAGES } from './app.component';
import { AuthResponse, AuthService } from '@rh/core/auth.service';
import { FORMAT_LOCALES, LocaleService } from '@rh/core/locale.service';
import { FooterPresenceService } from '@rh/shared/app-footer/footer-presence';

@Component({ standalone: true, template: '' })
class BlankComponent {}

/** Ein Token ohne Ablauf — die Anmeldung prueft nur `exp`. */
const USER: AuthResponse = { token: 'e30.e30.x', username: 'lena', userId: 7, isAdmin: false };

describe('KidHubAppComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [KidHubAppComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideRouter([{ path: '', component: BlankComponent }, { path: 'levels', component: BlankComponent }]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        {
          provide: SwUpdate,
          useValue: {
            isEnabled: false, versionUpdates: new Subject<unknown>(),
            unrecoverable: new Subject<unknown>(), checkForUpdate: () => Promise.resolve(false),
          },
        },
      ],
    });
  });

  // Auch der Merker eines Abmeldens ohne Antwort (F1-004): sonst holte der naechste Test beim Start das
  // Ende der geteilten Anmeldung nach, statt sie zu uebernehmen.
  afterEach(() => {
    localStorage.removeItem('rookhub_lang'); localStorage.removeItem('rookhub_user');
    localStorage.removeItem(AuthService.SessionEndPendingKey);
  });

  it('meldet die Fusszeile als immer sichtbar — die Anmeldemaske zeigt „Datenschutz" nicht doppelt (x-login-legal)', () => {
    TestBed.createComponent(KidHubAppComponent);
    expect(TestBed.inject(FooterPresenceService).presence()).toBe('always');
  });

  function selected(f: { nativeElement: HTMLElement }): string {
    return (f.nativeElement.querySelector('footer select') as HTMLSelectElement).value;
  }

  /**
   * Gemeldet 2026-09-27: unten stand „Deutsch", die Seite sprach Englisch — die Liste zeigte ihren
   * ERSTEN Eintrag statt der aktiven Sprache, und ein Klick auf Deutsch aenderte nichts.
   */
  it('die Auswahl zeigt die Sprache, in der die Seite wirklich steht', async () => {
    localStorage.setItem('rookhub_lang', 'en');
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    expect(TestBed.inject(TranslateService).currentLang()).toBe('en');
    expect(selected(f)).toBe('en');
  });

  it('eine Wahl gilt sofort und wird gespeichert', async () => {
    localStorage.setItem('rookhub_lang', 'en');
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    f.componentInstance.setLang('de');
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    expect(TestBed.inject(TranslateService).currentLang()).toBe('de');
    expect(localStorage.getItem('rookhub_lang')).toBe('de');
    expect(selected(f)).toBe('de');
  });

  /** Seite mit einer Sprache ohne Kindertexte starten und die Frage nach dem IP-Land beantworten. */
  async function startWithForeignLanguage(answer: { country: string | null; language: string | null } | 'error') {
    localStorage.setItem('rookhub_lang', 'fr');
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    // NICHT vorher auf whenStable warten: das wartet auf genau diese offene Anfrage, bis der
    // 4-s-Timeout der Seite sie abbricht.
    const req = TestBed.inject(HttpTestingController).expectOne('/api/kids/language-hint');
    if (answer === 'error') req.flush('weg', { status: 503, statusText: 'Service Unavailable' });
    else req.flush(answer);
    f.detectChanges();
    await f.whenStable();
    f.detectChanges();
    return f;
  }

  it('Sprache ohne Kindertexte: erst das Land der IP (Ungarn → Ungarisch)', async () => {
    const f = await startWithForeignLanguage({ country: 'HU', language: 'hu' });
    expect(TestBed.inject(TranslateService).currentLang()).toBe('hu');
    expect(selected(f)).toBe('hu');
    expect(localStorage.getItem('rookhub_lang')).toBe('fr');   // die Wahl bleibt unangetastet
  });

  it('Frankreich → Englisch (der Server sagt es so)', async () => {
    await startWithForeignLanguage({ country: 'FR', language: 'en' });
    expect(TestBed.inject(TranslateService).currentLang()).toBe('en');
  });

  it('Land unbekannt (LAN) → Deutsch', async () => {
    await startWithForeignLanguage({ country: null, language: null });
    expect(TestBed.inject(TranslateService).currentLang()).toBe('de');
    expect(TestBed.inject(LocaleService).current).toBe('de');
  });

  it('Hinweis nicht erreichbar → Deutsch', async () => {
    await startWithForeignLanguage('error');
    expect(TestBed.inject(TranslateService).currentLang()).toBe('de');
  });

  it('eine Kindersprache fragt gar nicht erst nach dem Land', async () => {
    localStorage.setItem('rookhub_lang', 'en');
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    await f.whenStable();
    TestBed.inject(HttpTestingController).expectNone('/api/kids/language-hint');
    expect(TestBed.inject(TranslateService).currentLang()).toBe('en');
  });

  it('bietet nur die vollstaendig uebersetzten Sprachen an', () => {
    expect(KIDS_LANGUAGES.map(l => l.code as string).sort()).toEqual([...FORMAT_LOCALES].sort());
  });

  it('verlinkt den Datenschutz, aber kein Impressum (Wunsch 2026-09-27)', () => {
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    const hrefs = Array.from(f.nativeElement.querySelectorAll('footer a') as NodeListOf<HTMLAnchorElement>)
      .map(a => a.getAttribute('href'));
    expect(hrefs).toContain('/privacy');
    expect(hrefs).not.toContain('/impressum');
  });

  function account(f: { nativeElement: HTMLElement }): HTMLElement | null {
    return f.nativeElement.querySelector('header .account');
  }

  it('Startseite ohne Anmeldung: rechts oben Anmelden und Registrieren', async () => {
    const f = TestBed.createComponent(KidHubAppComponent);
    await TestBed.inject(Router).navigateByUrl('/');
    f.detectChanges();
    const links = Array.from(account(f)!.querySelectorAll('a') as NodeListOf<HTMLAnchorElement>)
      .map(a => a.getAttribute('href'));
    expect(links).toEqual(['/login?returnUrl=%2F', '/register?returnUrl=%2F']);
  });

  it('schon angemeldet (geteiltes Cookie): Name statt der Knoepfe', async () => {
    const f = TestBed.createComponent(KidHubAppComponent);
    f.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/auth/rh-session').flush(USER);
    await f.whenStable();
    f.detectChanges();
    expect(account(f)!.textContent).toContain('lena');
    expect(account(f)!.querySelector('a')).toBeNull();
    expect(account(f)!.querySelector('button')).not.toBeNull();
  });

  it('Abmelden fuehrt zurueck zur Startseite und zeigt wieder die Knoepfe', async () => {
    TestBed.inject(AuthService).adoptSession(USER);
    const f = TestBed.createComponent(KidHubAppComponent);
    await TestBed.inject(Router).navigateByUrl('/');
    f.detectChanges();
    (account(f)!.querySelector('button') as HTMLButtonElement).click();
    await f.whenStable();
    f.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/');
    expect(account(f)!.querySelectorAll('a').length).toBe(2);
  });

  it('in einer Stufe gibt es keinen Konto-Knopf', async () => {
    const f = TestBed.createComponent(KidHubAppComponent);
    await TestBed.inject(Router).navigateByUrl('/levels');
    f.detectChanges();
    expect(account(f)).toBeNull();
  });

  it('isHomeUrl: Abfrage und Anker zaehlen nicht', () => {
    expect(isHomeUrl('/')).toBeTrue();
    expect(isHomeUrl('/?quickstart=1')).toBeTrue();
    expect(isHomeUrl('/#oben')).toBeTrue();
    expect(isHomeUrl('/levels')).toBeFalse();
    expect(isHomeUrl('/login?returnUrl=%2F')).toBeFalse();
  });
});
