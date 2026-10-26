import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ChangelogEntry } from '../../../environments/changelog';
import { provideTranslateService } from '@ngx-translate/core';
import { AppFooterComponent } from './app-footer.component';
import { DISCORD_INVITE_URL } from '../../core/community';
import { environment } from '../../../environments/environment';
import { LEGAL_SITE } from '../../core/legal-site';
import { setDarkTheme, textContrast } from '../../testing/contrast';
import { FooterPresenceService } from './footer-presence';

/** Ein Changelog wie /assets/changelog.json — neueste Version zuerst. */
function fakeChangelog(n: number): ChangelogEntry[] {
  return Array.from({ length: n }, (_, i) => ({
    version: `0.${n - i}.0`, date: '2026-10-26',
    changes: [{ en: `change ${n - i}`, de: `Änderung ${n - i}` }],
  }));
}

/** Beantwortet die offene Changelog-Anfrage und wartet, bis die Komponente sie uebernommen hat. */
async function answerChangelog(footer: AppFooterComponent, entries = fakeChangelog(3)): Promise<void> {
  const req = TestBed.inject(HttpTestingController).expectOne(AppFooterComponent.ChangelogUrl);
  expect(req.request.method).toBe('GET');
  req.flush(entries);
  await footer.changelogLoad;
}

/** Fuer alle Fusszeilen-Specs: Router, Uebersetzung und ein Fake-HttpClient (das Changelog kommt per HTTP). */
const FOOTER_PROVIDERS = [provideHttpClient(), provideHttpClientTesting()];

/**
 * Die Fusszeile gehoert BEIDEN Oberflaechen (RookHub und Turnierseite). Geprueft wird vor allem
 * das Changelog-Overlay: seine ~2,2 MB Prosa liegen weder im Initial-Bundle noch in einem Chunk, sondern
 * kommen als Daten (/assets/changelog.json, Codereview F8-008) erst beim Oeffnen.
 */
describe('AppFooterComponent', () => {
  function buildFixture(routes: { path: string }[] = [], legal?: object) {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes.map(r => ({ path: r.path, children: [] }))),
                  provideTranslateService({ fallbackLang: 'en' }), ...FOOTER_PROVIDERS,
                  ...(legal ? [{ provide: LEGAL_SITE, useValue: legal }] : [])],
    });
    return TestBed.createComponent(AppFooterComponent);
  }

  function build(routes: { path: string }[] = []) {
    return buildFixture(routes).componentInstance;
  }

  it('nennt den Discord-Einladungslink und die laufende Version', () => {
    const footer = build();
    expect(footer.discordUrl).toBe(DISCORD_INVITE_URL);
    expect(footer.version).toBe(environment.version);
  });

  it('haelt die Changelog-Eintraege aus dem eager geladenen Environment heraus', () => {
    expect((environment as Record<string, unknown>)['changelog']).toBeUndefined();
    expect(build().changelog.length).toBe(0);
  });

  it('laedt die Changelog-Eintraege erst beim Oeffnen des Overlays per HTTP (/assets/changelog.json)', async () => {
    const footer = build();
    const http = TestBed.inject(HttpTestingController);
    http.expectNone(AppFooterComponent.ChangelogUrl);   // vor dem Oeffnen: keine Anfrage
    footer.openChangelog();
    expect(footer.showChangelog).toBeTrue();
    await answerChangelog(footer, fakeChangelog(2));
    expect(footer.changelog.map(e => e.version)).toEqual(['0.2.0', '0.1.0']);
    expect(footer.changelog[0].changes[0].de).toBe('Änderung 2');
    http.verify();
  });

  it('klappt das Overlay per Versionslink auf und zu (Laden nur beim Oeffnen)', async () => {
    const footer = build();
    footer.toggleChangelog();
    expect(footer.showChangelog).toBeTrue();
    await answerChangelog(footer);
    const loaded = footer.changelog;
    expect(loaded.length).toBe(3);

    footer.toggleChangelog();
    expect(footer.showChangelog).toBeFalse();

    footer.toggleChangelog();                 // erneutes Oeffnen laedt nicht neu
    await footer.changelogLoad;
    TestBed.inject(HttpTestingController).verify();
    expect(footer.changelog).toBe(loaded);
  });

  it('bleibt nach einem Ladefehler leer und versucht es beim naechsten Oeffnen erneut', async () => {
    const footer = build();
    const http = TestBed.inject(HttpTestingController);
    footer.openChangelog();
    http.expectOne(AppFooterComponent.ChangelogUrl).flush('weg', { status: 404, statusText: 'Not Found' });
    await footer.changelogLoad;
    expect(footer.changelog).toEqual([]);

    footer.onEscape();
    footer.openChangelog();
    await answerChangelog(footer);
    expect(footer.changelog.length).toBe(3);
  });

  it('schliesst das Overlay mit Escape', async () => {
    const footer = build();
    footer.openChangelog();
    await answerChangelog(footer);

    footer.onEscape();

    expect(footer.showChangelog).toBeFalse();
  });

  /**
   * Das Overlay lag frueher ungestylt im Seitenfluss UNTER der Fusszeile: die gleichnamigen
   * Regeln in RookHubs AppComponent sind dort gekapselt und erreichen diese Ansicht nicht.
   * Karma wendet Komponenten-Styles an — position: fixed ist der Beleg, dass die Regeln HIER liegen.
   */
  it('stylt das Changelog-Overlay selbst (fixed, nicht im Seitenfluss)', async () => {
    const fixture = buildFixture();
    fixture.detectChanges();
    // Ueber den KLICK oeffnen, nicht ueber die Methode: Angular 22 prueft eine View nur, wenn
    // etwas sie als geaendert markiert hat — ein DOM-Ereignis tut das, ein direkter Aufruf
    // nicht (dieselbe Falle wie bei HTTP-Antworten, siehe render-after-http.interceptor).
    (fixture.nativeElement.querySelector('.version-link') as HTMLElement).click();
    fixture.detectChanges();

    const overlay = fixture.nativeElement.querySelector('.changelog-overlay') as HTMLElement | null;
    expect(overlay).withContext('Overlay nicht gerendert').not.toBeNull();
    expect(getComputedStyle(overlay!).position).toBe('fixed');

    await answerChangelog(fixture.componentInstance);   // Anfrage nicht offen lassen
  });

  // ----- Kontrast der Markenlinks (UX-055) ---------------------------------------------------
  // Discord-Blau #5865F2 hatte auf der dunklen Seite nur 4,07:1 (axe rechnete gegen Weiss und sah 4,61:1), Ko-fi
  // #ff5e5b im hellen Modus 3,0:1. Gemessen wird gegen die Flaeche, auf der die Fusszeile wirklich steht (body).
  describe('Kontrast der Discord- und Ko-fi-Links (UX-055)', () => {
    let wasDark: boolean;
    beforeEach(() => { wasDark = document.documentElement.classList.contains('dark-theme'); });
    afterEach(() => setDarkTheme(wasDark));

    for (const dark of [false, true]) {
      it(`erreichen 4,5:1 auf der Seite (${dark ? 'dunkel' : 'hell'})`, () => {
        setDarkTheme(dark);
        const fixture = buildFixture();
        fixture.detectChanges();
        for (const sel of ['.discord-link', '.kofi-link']) {
          const link = (fixture.nativeElement as HTMLElement).querySelector(sel)!;
          const ratio = textContrast(link);
          expect(ratio).withContext(`${sel} (${dark ? 'dunkel' : 'hell'}): ${ratio.toFixed(2)}:1`)
            .toBeGreaterThanOrEqual(4.5);
        }
      });
    }
  });

  // a1-t-fuss-kontrast: die gedaempfte Fusszeile (Version, Hilfe, Feedback, Rechtslinks) lag hell bei 47 % mit 3,6:1.
  describe('Kontrast der Fusszeilen-Links (a1-t-fuss-kontrast)', () => {
    let wasDark: boolean;
    beforeEach(() => { wasDark = document.documentElement.classList.contains('dark-theme'); });
    afterEach(() => setDarkTheme(wasDark));

    for (const dark of [false, true]) {
      it(`Version, Feedback und Rechtslinks erreichen 4,5:1 (${dark ? 'dunkel' : 'hell'})`, () => {
        setDarkTheme(dark);
        const fixture = buildFixture([{ path: 'privacy' }, { path: 'impressum' }]);
        fixture.detectChanges();
        const root = fixture.nativeElement as HTMLElement;
        for (const sel of ['.version-link', '.feedback-link', 'a.legal-link']) {
          const el = root.querySelector(sel)!;
          const ratio = textContrast(el);
          expect(ratio).withContext(`${sel} (${dark ? 'dunkel' : 'hell'}): ${ratio.toFixed(2)}:1`)
            .toBeGreaterThanOrEqual(4.5);
        }
      });
    }

    it('wird beim Hover nicht blasser (kein 65-%-Mischwert auf dem schon gedaempften Ton)', () => {
      const css = ((AppFooterComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n');
      expect(css).toMatch(/\.app-footer[^{]*\{[^}]*currentColor 60%/);
      expect(css).not.toMatch(/:hover[^{]*\{[^}]*currentColor 65%/);
    });
  });

  // ----- Ausblenden am Handy: Vorgabe ja, per Input abschaltbar (Turnierseite) --------------

  it('traegt hide-on-mobile nur, wenn hideOnMobile gesetzt ist', () => {
    const fixture = buildFixture();
    fixture.detectChanges();
    const footerEl = fixture.nativeElement.querySelector('.app-footer') as HTMLElement;

    expect(fixture.componentInstance.hideOnMobile).withContext('Vorgabe fuer RookHub').toBeTrue();
    expect(footerEl.classList.contains('hide-on-mobile')).toBeTrue();

    fixture.componentRef.setInput('hideOnMobile', false);
    fixture.detectChanges();
    expect(footerEl.classList.contains('hide-on-mobile')).toBeFalse();
  });

  // ----- Der eine Teil, der sich zwischen den Oberflaechen unterscheidet ----

  it('verlinkt die eigene Hilfeseite, wenn es sie in dieser App gibt', () => {
    const footer = build([{ path: 'help' }]);
    expect(footer.helpRoute).toBeTrue();
    expect(footer.helpHref).toBeNull();
  });

  it('laesst den Hilfe-Link weg, wenn es weder eine eigene Seite noch eine Schwesterseite gibt', () => {
    // Karma laeuft auf localhost — dort gibt es keine Schwesterseite, ein Link waere ins Leere.
    const footer = build();
    expect(footer.helpRoute).toBeFalse();
    expect(footer.helpHref).toBeFalsy();
  });

  // Impressum und Datenschutz standen nur unter der Anmeldekarte — die sehen Eingeloggte nie (guestGuard), am Handy
  // fehlte ohnehin jeder Weg (UX-017). Jetzt in der gemeinsamen Fusszeile, also auch auf der Turnierseite.
  describe('Rechtslinks (UX-017)', () => {
    const legalHrefs = (fixture: ReturnType<typeof buildFixture>) => {
      fixture.detectChanges();
      return [...(fixture.nativeElement as HTMLElement).querySelectorAll('a.legal-link')].map(a => a.getAttribute('href'));
    };

    it('zeigt Impressum und Datenschutz, wo die App beide Wege hat', () => {
      expect(legalHrefs(buildFixture([{ path: 'privacy' }, { path: 'impressum' }]))).toEqual(['/impressum', '/privacy']);
    });

    it('ohne Impressum laut LEGAL_SITE nur den Datenschutz', () => {
      const fixture = buildFixture([{ path: 'privacy' }, { path: 'impressum' }], { contactEmail: 'x@y.z', imprint: false });
      expect(legalHrefs(fixture)).toEqual(['/privacy']);
    });

    it('kein Link auf einen Weg, den die App nicht hat', () => {
      expect(legalHrefs(buildFixture())).toEqual([]);
    });

    // UI-Review login-legal: die Fusszeile meldet, wo sie die Rechtslinks zeigt — die Anmeldemaske laesst ihre Zeile dort weg.
    it('meldet ihre Rechtslinks: RookHub nur breit, Turnierseite immer, ohne Datenschutz-Route keine, weg = keine', () => {
      let fixture = buildFixture([{ path: 'privacy' }]);
      const presence = TestBed.inject(FooterPresenceService).presence;
      fixture.detectChanges();
      expect(presence()).toBe('wide');
      fixture.destroy();
      expect(presence()).toBe('none');

      TestBed.resetTestingModule();
      fixture = buildFixture([{ path: 'privacy' }]);
      fixture.componentRef.setInput('hideOnMobile', false);
      fixture.detectChanges();
      expect(TestBed.inject(FooterPresenceService).presence()).toBe('always');

      TestBed.resetTestingModule();
      fixture = buildFixture();
      fixture.detectChanges();
      expect(TestBed.inject(FooterPresenceService).presence()).toBe('none');
    });
  });
});

/**
 * Codereview W5 UX-072: am Handy (nur die Turnierseite zeigt die Fusszeile dort) waren die Links ~34 px hoch, der
 * Trennpunkt blieb am Zeilenende haengen, das Discord-Symbol sass hoeher als sein Text, und der Versionslink oeffnete
 * RookHubs ganzes Changelog. Die Handy-Regeln stehen in einer Media-Query — Karma laeuft breiter als 768 px, deshalb
 * wird die Regel im CSSOM gelesen (die Komponentenstyles haengen nach dem Erzeugen im Dokument).
 */
describe('AppFooterComponent am Handy (UX-072)', () => {
  function render(changelogLink?: boolean) {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), ...FOOTER_PROVIDERS],
    });
    const fixture = TestBed.createComponent(AppFooterComponent);
    fixture.componentRef.setInput('hideOnMobile', false);
    if (changelogLink !== undefined) fixture.componentRef.setInput('changelogLink', changelogLink);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  afterEach(() => TestBed.resetTestingModule());

  /** Alle Regeln aller `@media (max-width: 768px)`-Bloecke im Dokument, deren Selektor `part` enthaelt. */
  function mobileRules(part: string): CSSStyleRule[] {
    const out: CSSStyleRule[] = [];
    for (const sheet of Array.from(document.styleSheets)) {
      let rules: CSSRuleList;
      try { rules = sheet.cssRules; } catch { continue; }
      for (const r of Array.from(rules)) {
        if (r instanceof CSSMediaRule && r.conditionText.replace(/\s/g, '') === '(max-width:768px)') {
          for (const inner of Array.from(r.cssRules)) {
            if (inner instanceof CSSStyleRule && inner.selectorText.includes(part)) out.push(inner);
          }
        }
      }
    }
    return out;
  }

  it('gibt am Handy JEDEM Link ein 44-px-Ziel, mittig ausgerichtet (auch Discord und Ko-fi)', () => {
    render();
    // Gekapselte Selektoren tragen Attribute (`.app-footer[_ngcontent-…] a[_ngcontent-…]`) — gesucht wird die Regel,
    // die Links UND die Versionsnummer gemeinsam auf 44 px setzt (die Rechtslinks hatten das schon allein).
    const linkRule = mobileRules('.version-link').find(r => r.style.minHeight === '44px');
    expect(linkRule).withContext('44-px-Regel fuer alle Links').toBeDefined();
    expect(linkRule!.selectorText).toMatch(/\.app-footer\S*\s+a\b/);
    expect(linkRule!.selectorText).toContain('.version-text');
    expect(linkRule!.style.display).toBe('inline-flex');
    expect(linkRule!.style.alignItems).toBe('center');
  });

  it('bricht am Handy nur zwischen den Eintraegen um — ohne haengenden Trennpunkt', () => {
    render();
    expect(mobileRules('.footer-sep').some(r => r.style.display === 'none')).toBeTrue();
    expect(mobileRules('.app-footer').some(r => r.style.flexWrap === 'wrap')).toBeTrue();
  });

  it('haelt Impressum und Datenschutz am Handy als eigene, gemeinsame Zeile zusammen (a1-t-fuss-rechtslinks-en)', () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([{ path: 'privacy', children: [] }, { path: 'impressum', children: [] }]),
                  provideTranslateService({ fallbackLang: 'en' }), ...FOOTER_PROVIDERS],
    });
    const fixture = TestBed.createComponent(AppFooterComponent);
    fixture.componentRef.setInput('hideOnMobile', false);
    fixture.detectChanges();
    const group = (fixture.nativeElement as HTMLElement).querySelector('.legal-group');
    expect(group).withContext('Huelle um die Rechtslinks').not.toBeNull();
    expect(group!.querySelectorAll('a.legal-link').length).toBe(2);
    // Breit (Karma 1400 px) wirkt die Huelle nicht: die Trenner bleiben wie bisher.
    expect(getComputedStyle(group!).display).toBe('contents');
    const rule = mobileRules('.legal-group').find(r => r.style.flexBasis === '100%');
    expect(rule).withContext('Handy: eigene Zeile').toBeDefined();
    expect(rule!.style.flexWrap).toBe('nowrap');
    expect(rule!.style.display).toBe('inline-flex');
  });

  it('zeigt die Version ohne Changelog-Knopf, wenn changelogLink aus ist (Turnierseite)', () => {
    const el = render(false);
    expect(el.querySelector('.version-link')).toBeNull();
    expect(el.querySelector('.version-text')?.textContent).toContain('v');
  });

  it('behaelt in RookHub den Versionslink zum Changelog', () => {
    const el = render();
    expect(el.querySelector('.version-link[role="button"]')).not.toBeNull();
  });
});

/** Codereview W5 F8-008 (Teil): das Overlay renderte alle ~1500 Versionen auf einmal — jetzt die neuesten 30. */
describe('AppFooterComponent Changelog seitenweise (F8-008)', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('zeigt die neuesten 30 Versionen und auf Wunsch die naechsten 30', async () => {
    TestBed.configureTestingModule({
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' }), ...FOOTER_PROVIDERS],
    });
    const fixture = TestBed.createComponent(AppFooterComponent);
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.version-link') as HTMLElement).click();
    await answerChangelog(fixture.componentInstance, fakeChangelog(75));
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const total = fixture.componentInstance.changelog.length;
    expect(total).toBeGreaterThan(60);   // Prüfung trägt nur mit genug Einträgen

    expect(el.querySelectorAll('.changelog-entry').length).toBe(AppFooterComponent.ChangelogPage);
    expect(el.querySelector('.changelog-entry strong')?.textContent).toContain(fixture.componentInstance.changelog[0].version);

    (el.querySelector('.changelog-more') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(el.querySelectorAll('.changelog-entry').length).toBe(2 * AppFooterComponent.ChangelogPage);

    // Schliessen und wieder oeffnen beginnt wieder bei den neuesten 30 (ueber Klicks — ein direkter Methodenaufruf
    // markiert die Ansicht in Angular 22 nicht als geaendert).
    (el.querySelector('.changelog-header button') as HTMLButtonElement).click();
    fixture.detectChanges();
    (el.querySelector('.version-link') as HTMLElement).click();
    await fixture.componentInstance.changelogLoad;
    fixture.detectChanges();
    expect(el.querySelectorAll('.changelog-entry').length).toBe(AppFooterComponent.ChangelogPage);
  });
});
