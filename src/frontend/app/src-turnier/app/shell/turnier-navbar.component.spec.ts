import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { HandoffService } from '@rh/core/handoff.service';
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

  function setup(partnerUrl: string | null): ComponentFixture<TurnierNavbarComponent> {
    localStorage.clear();   // ausgeloggt: die breitere Fassung mit „Anmelden“ als Textknopf
    TestBed.configureTestingModule({
      imports: [TurnierNavbarComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        // Auf localhost gibt es keine Schwesterseite — mit Partner ist die Zeile am laengsten.
        { provide: HandoffService, useValue: { partnerUrl, jump: () => Promise.resolve() } },
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
      const menuBtn = host.querySelector('button[aria-label="nav.menu"]') as HTMLElement;
      expect(getComputedStyle(menuBtn).display).not.toBe('none');
      expect(getComputedStyle(host.querySelector('.links') as HTMLElement).display).toBe('none');
      const login = host.querySelector('a[href="/login"]') as HTMLElement;
      expect(getComputedStyle(login).display).not.toBe('none');
    } finally {
      restore();
    }
  });

  it('das ☰-Menue traegt die drei Wege, den RookHub-Sprung, Design und Sprache', () => {
    const fixture = setup(PARTNER);
    const host = fixture.nativeElement as HTMLElement;
    const trigger = host.querySelector('button[aria-label="nav.menu"]') as HTMLButtonElement;
    expect(trigger).withContext('☰ fehlt').toBeTruthy();

    trigger.click();
    fixture.detectChanges();
    const items = overlayItems();
    const hrefs = items.map(i => i.getAttribute('href')).filter(Boolean);
    expect(hrefs).toEqual(['/tournaments', '/tournaments/calendar', '/tournaments/history']);
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
    const fixture = setup(PARTNER);
    const host = fixture.nativeElement as HTMLElement;

    expect(getComputedStyle(host.querySelector('.links') as HTMLElement).display).toBe('flex');
    expect(getComputedStyle(host.querySelector('button[aria-label="nav.menu"]') as HTMLElement).display).toBe('none');
    // Der RookHub-Knopf zeigt am Schreibtisch Symbol UND Text.
    const rookhub = host.querySelector('mat-toolbar button.wide') as HTMLElement;
    expect(rookhub.textContent).toContain('turnier.toRookHub');
    expect(getComputedStyle(rookhub).display).not.toBe('none');
  });
});
