import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { HandoffService } from '@rh/core/handoff.service';
import { AuthService } from '@rh/core/auth.service';
import { LocaleService } from '@rh/core/locale.service';
import { TurnierNavbarComponent } from './turnier-navbar.component';

/**
 * Die Kopfzeile der Turnierseite am Handy. Gemeldet (360/375/414px): die Toolbar war rund 670px
 * breit — drei Textlinks plus RookHub, Design, Sprache und Anmelden in einer Zeile ohne Umbruch —
 * die GANZE Seite scrollte seitwaerts, Anmelden und Sprachwechsel lagen ausserhalb des Schirms.
 *
 * Der Bruch haengt an einer Media-Query, und die misst den VIEWPORT, nicht den Host. Karma
 * startet Chrome mit 800px; deshalb wird hier das Karma-iframe selbst schmal gestellt — jedes
 * Fenster hat seinen eigenen Viewport, und der des iframes ist dann 360px breit.
 */
describe('TurnierNavbarComponent', () => {
  const PARTNER = 'https://rookhub.test';

  /** loggedIn: die drei Wege gibt es nur angemeldet (UX-038); ausgeloggt steht „Anmelden“ als Textknopf in der Zeile. */
  function setup(partnerUrl: string | null, loggedIn = false): ComponentFixture<TurnierNavbarComponent> {
    localStorage.clear();
    TestBed.configureTestingModule({
      imports: [TurnierNavbarComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        // Auf localhost gibt es keine Schwesterseite — mit Partner ist die Zeile am laengsten.
        { provide: HandoffService, useValue: { partnerUrl, jump: () => Promise.resolve() } },
        ...(loggedIn ? [{ provide: AuthService, useValue:
          { isLoggedIn: true, isAdmin: false, currentUser: { username: 'tester' }, logout: () => {} } }] : []),
      ],
    });
    const fixture = TestBed.createComponent(TurnierNavbarComponent);
    fixture.detectChanges();
    return fixture;
  }

  /** Viewport des Test-Dokuments (das Karma-iframe) auf Handybreite stellen; Rueckgabe = zuruecksetzen. */
  async function narrowViewport(width: number): Promise<() => void> {
    const frame = window.frameElement as HTMLElement | null;
    // Ohne iframe (Karma-Debug-Seite im Hauptfenster) ist der Viewport nicht verstellbar —
    // dann ehrlich aussetzen statt gegen 800px zu messen.
    if (!frame) pending('Karma laeuft nicht im iframe — der Viewport laesst sich nicht verstellen');
    const prev = frame!.style.width;
    frame!.style.width = `${width}px`;
    // Ein Frame abwarten, damit der Eltern-Layout die neue Breite an das iframe durchreicht.
    await new Promise<void>(r => requestAnimationFrame(() => r()));
    expect(window.innerWidth).withContext('Viewport nicht verstellt').toBeLessThanOrEqual(width);
    return () => { frame!.style.width = prev; };
  }

  function overlayItems(): HTMLElement[] {
    return Array.from(document.querySelectorAll<HTMLElement>('.cdk-overlay-container [mat-menu-item]'));
  }

  it('passt bei 360px in die Zeile — nichts laeuft seitwaerts ueber', async () => {
    const restore = await narrowViewport(360);
    try {
      const fixture = setup(PARTNER);
      const host = fixture.nativeElement as HTMLElement;
      host.style.width = '360px';
      host.style.display = 'block';
      fixture.detectChanges();

      const toolbar = host.querySelector('mat-toolbar') as HTMLElement;
      expect(toolbar).withContext('Toolbar gerendert').toBeTruthy();
      // Gegen den Ist-Stand vor der Reparatur: 671 > 360.
      expect(toolbar.scrollWidth).toBeLessThanOrEqual(toolbar.clientWidth);

      // Das ☰ ist da, die Textlinks sind weg; Anmelden bleibt als die eine Aktion in der Zeile.
      // (Gaeste haben seit 0.643.0 den Kalender-Link — am Handy steht er wie alle Textlinks nur im ☰.)
      const menuBtn = host.querySelector('button[aria-label="nav.menu"]') as HTMLElement;
      expect(getComputedStyle(menuBtn).display).not.toBe('none');
      const links = host.querySelector('.links') as HTMLElement | null;
      expect(links === null || getComputedStyle(links).display === 'none').toBeTrue();
      const login = host.querySelector('a[href^="/login"]') as HTMLElement;   // mit returnUrl (UX-020)
      expect(getComputedStyle(login).display).not.toBe('none');
    } finally {
      restore();
    }
  });

  it('angemeldet bei 360px: die Textlinks sind in der Zeile ausgeblendet, die Zeile laeuft nicht ueber', async () => {
    const restore = await narrowViewport(360);
    try {
      const fixture = setup(PARTNER, true);
      const host = fixture.nativeElement as HTMLElement;
      host.style.width = '360px';
      host.style.display = 'block';
      fixture.detectChanges();
      const toolbar = host.querySelector('mat-toolbar') as HTMLElement;
      expect(toolbar.scrollWidth).toBeLessThanOrEqual(toolbar.clientWidth);
      expect(getComputedStyle(host.querySelector('.links') as HTMLElement).display).toBe('none');
    } finally {
      restore();
    }
  });

  it('das ☰-Menue traegt die drei Wege, den RookHub-Sprung, Design und Sprache', () => {
    const fixture = setup(PARTNER, true);
    const host = fixture.nativeElement as HTMLElement;
    const trigger = host.querySelector('button[aria-label="nav.menu"]') as HTMLButtonElement;
    expect(trigger).withContext('☰ fehlt').toBeTruthy();

    trigger.click();
    fixture.detectChanges();
    const items = overlayItems();
    const hrefs = items.map(i => i.getAttribute('href')).filter(Boolean);
    expect(hrefs).toEqual(['/tournaments/calendar', '/tournaments', '/tournaments/history']);
    const texts = items.map(i => i.textContent ?? '');
    // Uebersetzungen sind im Test nicht geladen — die Pipe liefert die Schluessel.
    expect(texts.some(t => t.includes('turnier.toRookHub'))).withContext('RookHub-Sprung').toBeTrue();
    expect(texts.some(t => t.includes('nav.language'))).withContext('Sprache als Untermenue').toBeTrue();
    // Design-Umschalter: das Symbol richtet sich nach der Vorgabe des ThemeService.
    expect(texts.some(t => /brightness_auto|light_mode|dark_mode/.test(t))).withContext('Design').toBeTrue();
    // Anmelden gehoert NICHT ins Menue — es bleibt als Knopf in der Zeile.
    expect(hrefs).not.toContain('/login');

    trigger.click();
    fixture.detectChanges();
  });

  /**
   * F6-019: Die Marke fuehrte auf /tournaments (die Merkliste), die Startseite ist aber der Kalender; die Merkliste
   * hiess „Turniere" und war neben „Meine Turniere" (dem Verlauf) nicht als „gemerkt" zu erkennen.
   */
  it('Marke fuehrt zur Startseite, die Merkliste heisst „Gemerkt", Kalender steht vorn (F6-019)', () => {
    const fixture = setup(null, true);
    const host = fixture.nativeElement as HTMLElement;
    expect(host.querySelector('a.brand')!.getAttribute('href')).toBe('/');

    const links = Array.from(host.querySelectorAll<HTMLAnchorElement>('nav.links a'));
    expect(links.map(a => a.getAttribute('href'))).toEqual(['/tournaments/calendar', '/tournaments', '/tournaments/history']);
    // Uebersetzungen sind nicht geladen — die Pipe liefert die Schluessel.
    expect(links.map(a => a.textContent?.trim())).toEqual(['nav.tournamentCalendar', 'nav.tournamentBookmarks', 'nav.tournamentHistory']);

    const trigger = host.querySelector('button[aria-label="nav.menu"]') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    const bookmarks = overlayItems().find(i => i.getAttribute('href') === '/tournaments')!;
    expect(bookmarks.textContent).toContain('nav.tournamentBookmarks');
    expect(bookmarks.querySelector('mat-icon')?.textContent?.trim()).toBe('bookmarks');
    trigger.click();
    fixture.detectChanges();
  });

  /**
   * F6-020/UX-073: Das Menue bot fest „EN/DE/HR" ohne Markierung. Die Startsprache kommt aber aus dem geteilten
   * Cookie, dem Geraet oder dem Browser und kann jede der 25 Sprachen sein — wer so auf Ungarisch kam, sah nicht,
   * was eingestellt ist, und fand nach einem Wechsel keinen Weg zurueck.
   */
  it('Sprachmenue: alle Sprachen mit Eigenbezeichnung wie RookHub, Haken an der aktuellen (F6-020, UX-073)', () => {
    const fixture = setup(null);
    const locale = TestBed.inject(LocaleService);
    locale.applyUnsaved('hu');   // z. B. ueber das geteilte Cookie aus RookHub
    try {
      fixture.detectChanges();
      const trigger = (fixture.nativeElement as HTMLElement)
        .querySelector('mat-toolbar button.wide[aria-label="nav.language"]') as HTMLButtonElement;
      trigger.click();
      fixture.detectChanges();

      const items = overlayItems();
      expect(items.length).withContext('alle Sprachen').toBe(locale.languages.length);
      const icon = (i: HTMLElement) => i.querySelector('mat-icon')?.textContent?.trim();
      const magyar = items.find(i => i.textContent?.includes('Magyar'));
      expect(magyar).withContext('Ungarisch waehlbar').toBeTruthy();
      expect(icon(magyar!)).toBe('check');
      expect(items.filter(i => icon(i) === 'check').length).withContext('genau ein Haken').toBe(1);
      expect(items.some(i => i.textContent?.trim() === 'EN')).withContext('keine nackten Kuerzel').toBeFalse();

      const use = spyOn(locale, 'use');
      items.find(i => i.textContent?.includes('Deutsch'))!.click();
      expect(use).toHaveBeenCalledWith('de');
    } finally {
      locale.applyUnsaved('en');
    }
  });

  /**
   * UX-038: Links, die fuer Gaeste nur auf die Anmeldemaske fuehren, wirken tot — „Gemerkt" und „Meine Turniere"
   * bleiben deshalb angemeldet. Der KALENDER ist seit 0.643.0 ohne Anmeldung offen und steht auch fuer Gaeste da.
   */
  it('Gaeste sehen den Kalender, aber keine Wege, die ein Konto brauchen — in der Zeile und im ☰ (UX-038)', () => {
    const fixture = setup(PARTNER);
    const host = fixture.nativeElement as HTMLElement;
    const lineLinks = Array.from(host.querySelectorAll('.links a')).map(a => a.getAttribute('href'));
    expect(lineLinks).withContext('Textlinks fuer Gaeste').toEqual(['/tournaments/calendar']);
    expect(host.querySelector('a.brand')!.getAttribute('href')).toBe('/');

    const trigger = host.querySelector('button[aria-label="nav.menu"]') as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    const hrefs = overlayItems().map(i => i.getAttribute('href')).filter(Boolean);
    expect(hrefs.filter(h => h!.startsWith('/tournaments'))).toEqual(['/tournaments/calendar']);
    // Design und Sprache bleiben auch fuer Gaeste erreichbar.
    expect(overlayItems().some(i => i.textContent?.includes('nav.language'))).toBeTrue();
    trigger.click();
    fixture.detectChanges();
  });

  it('ohne Schwesterseite (localhost) fehlt der RookHub-Sprung auch im Menue', () => {
    const fixture = setup(null);
    const host = fixture.nativeElement as HTMLElement;
    const trigger = host.querySelector('button[aria-label="nav.menu"]') as HTMLButtonElement;

    trigger.click();
    fixture.detectChanges();
    expect(overlayItems().some(i => i.textContent?.includes('turnier.toRookHub'))).toBeFalse();

    trigger.click();
    fixture.detectChanges();
  });

  it('am Schreibtisch (Karma-Fenster, breiter als 768px) bleibt alles wie bisher in der Zeile', () => {
    if (window.innerWidth <= 768) { pending('Fenster schmaler als der Bruch'); return; }
    const fixture = setup(PARTNER, true);
    const host = fixture.nativeElement as HTMLElement;

    expect(getComputedStyle(host.querySelector('.links') as HTMLElement).display).toBe('flex');
    expect(getComputedStyle(host.querySelector('button[aria-label="nav.menu"]') as HTMLElement).display).toBe('none');
    // Der RookHub-Knopf zeigt am Schreibtisch Symbol UND Text.
    const rookhub = host.querySelector('mat-toolbar button.wide') as HTMLElement;
    expect(rookhub.textContent).toContain('turnier.toRookHub');
    expect(getComputedStyle(rookhub).display).not.toBe('none');
  });
});

/** UX-020: „Anmelden“ führt zurück, z. B. auf das geteilte Turnier — bisher über den Rückfall /dashboard in den Kalender. */
describe('TurnierNavbarComponent „Anmelden“ behält das Ziel (UX-020)', () => {
  @Component({ standalone: true, template: '' })
  class StubPageComponent {}

  async function loginHrefAt(url: string): Promise<string | null> {
    localStorage.clear();   // ausgeloggt
    TestBed.configureTestingModule({
      imports: [TurnierNavbarComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(),
        provideRouter([{ path: '**', component: StubPageComponent }]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: HandoffService, useValue: { partnerUrl: null, jump: () => Promise.resolve() } },
      ],
    });
    await TestBed.inject(Router).navigateByUrl(url);
    const fixture = TestBed.createComponent(TurnierNavbarComponent);
    fixture.detectChanges();
    return (fixture.nativeElement as HTMLElement).querySelector('mat-toolbar a[href^="/login"]')!.getAttribute('href');
  }

  it('auf dem geteilten Turnier /t/42: zurück dorthin', async () => {
    expect(await loginHrefAt('/t/42')).toBe('/login?returnUrl=%2Ft%2F42');
  });

  it('auf der Registrierung mit Ziel: das Ziel, nicht die Registrierung; ohne Ziel „/“ (kein /dashboard hier)', async () => {
    expect(await loginHrefAt('/register?returnUrl=%2Ft%2F42')).toBe('/login?returnUrl=%2Ft%2F42');
    TestBed.resetTestingModule();
    expect(await loginHrefAt('/register')).toBe('/login?returnUrl=%2F');
  });
});
