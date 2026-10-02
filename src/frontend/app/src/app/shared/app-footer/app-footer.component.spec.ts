import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { AppFooterComponent } from './app-footer.component';
import { DISCORD_INVITE_URL } from '../../core/community';
import { environment } from '../../../environments/environment';
import { LEGAL_SITE } from '../../features/legal/legal-site';

/**
 * Die Fusszeile gehoert BEIDEN Oberflaechen (RookHub und Turnierseite). Geprueft wird vor allem
 * das Changelog-Overlay: seine ~0,9 MB Prosa duerfen nicht im Initial-Bundle liegen, kommen also
 * per dynamic import() erst beim Oeffnen.
 */
describe('AppFooterComponent', () => {
  function buildFixture(routes: { path: string }[] = [], legal?: object) {
    TestBed.configureTestingModule({
      providers: [provideRouter(routes.map(r => ({ path: r.path, children: [] }))),
                  provideTranslateService({ fallbackLang: 'en' }),
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

  it('laedt die Changelog-Eintraege beim Oeffnen des Overlays nach (dynamic import)', async () => {
    const footer = build();
    footer.openChangelog();
    expect(footer.showChangelog).toBeTrue();
    await footer.changelogLoad;
    expect(footer.changelog.length).toBeGreaterThan(0);
    expect(footer.changelog[0].version).toBeTruthy();
    expect(footer.changelog[0].changes[0].en).toBeTruthy();
    expect(footer.changelog[0].changes[0].de).toBeTruthy();
  });

  it('klappt das Overlay per Versionslink auf und zu (Laden nur beim Oeffnen)', async () => {
    const footer = build();
    footer.toggleChangelog();
    expect(footer.showChangelog).toBeTrue();
    await footer.changelogLoad;
    const loaded = footer.changelog;
    expect(loaded.length).toBeGreaterThan(0);

    footer.toggleChangelog();
    expect(footer.showChangelog).toBeFalse();

    footer.toggleChangelog();                 // erneutes Oeffnen laedt nicht neu
    await footer.changelogLoad;
    expect(footer.changelog).toBe(loaded);
  });

  it('schliesst das Overlay mit Escape', async () => {
    const footer = build();
    footer.openChangelog();
    await footer.changelogLoad;

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

    await fixture.componentInstance.changelogLoad;   // dynamic import nicht offen lassen
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
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
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
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(AppFooterComponent);
    fixture.detectChanges();
    (fixture.nativeElement.querySelector('.version-link') as HTMLElement).click();
    await fixture.componentInstance.changelogLoad;
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
