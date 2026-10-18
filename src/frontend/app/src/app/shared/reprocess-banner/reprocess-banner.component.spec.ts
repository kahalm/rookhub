import { TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { ReprocessBannerComponent } from './reprocess-banner.component';
import { SnackbarService } from '../../core/snackbar.service';

/**
 * Sichert die Banner-Sichtbarkeit/Zählung ab: der Knopf darf nur Datensätze zählen, die er
 * tatsächlich aktualisieren kann (lokal aufbereitbar + von Chessable nachladbar); rein manuell
 * zu re-importierende Kurse werden getrennt ausgewiesen, nicht mitgezählt (Regression v0.157.2 —
 * „Aktualisieren (12)" blieb hängen, weil manuell hochgeladene Kurse mitgezählt, aber übersprungen wurden).
 */
describe('ReprocessBannerComponent', () => {
  let snackbar: jasmine.SpyObj<SnackbarService>;

  function createComponent(): ReprocessBannerComponent {
    const fixture = TestBed.createComponent(ReprocessBannerComponent);
    fixture.componentInstance.section = 'courses';
    return fixture.componentInstance;
  }

  beforeEach(() => {
    snackbar = jasmine.createSpyObj<SnackbarService>('SnackbarService', ['info']);
    TestBed.configureTestingModule({
      imports: [ReprocessBannerComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' }), 
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: SnackbarService, useValue: snackbar },
      ],
    });
  });

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('allCount = reprocessableLocally + refetchable (manuelle Re-Imports zählen NICHT mit)', () => {
    const c = createComponent();
    c.status = { currentVersion: 2, total: 50, stale: 48, reprocessableLocally: 5, refetchable: 31, needsReimport: 12 };
    expect(c.allCount).toBe(36);
    expect(c.actionableCount).toBe(36);
  });

  // UI-Sweep 2026-10-11 (r2-rg-rep-reprocess): Einzahl/Mehrzahl und Admin-Umfang im Bannertext.
  it('wählt den Bannertext nach Anzahl und Umfang (eigene vs. alle Nutzer)', () => {
    const c = createComponent();
    const base = { currentVersion: 2, total: 13, stale: 13, refetchable: 0, needsReimport: 0 };
    c.status = { ...base, reprocessableLocally: 1 };
    expect(c.textKey).toBe('reprocess.availableOne.courses');
    c.status = { ...base, reprocessableLocally: 13 };
    expect(c.textKey).toBe('reprocess.available.courses');
    c.status = { ...base, reprocessableLocally: 13, allUsers: true, ownActionable: 1 };
    expect(c.textKey).toBe('reprocess.availableAll.courses');
    c.status = { ...base, reprocessableLocally: 1, allUsers: true, ownActionable: 0 };
    expect(c.textKey).toBe('reprocess.availableAllOne.courses');
  });

  // UI-Sweep z2 (z2-rk-courses-vorlauf, z2-rk-rep-liste-vorlauf-admin): sichtbar nur der Kurzsatz, der lange Text
  // (samt „aller Nutzer, davon eigene") im ⓘ; der Knopf ist ein Textknopf in derselben Zeile.
  it('zeigt den Kurzsatz, legt den langen Text ins ⓘ und nutzt einen Textknopf', () => {
    const fixture = TestBed.createComponent(ReprocessBannerComponent);
    fixture.componentInstance.section = 'repertoires';
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/repertoires/reprocess/status').flush(
      { currentVersion: 2, total: 13, stale: 13, reprocessableLocally: 13, refetchable: 0, needsReimport: 0,
        allUsers: true, ownActionable: 1 });
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const c = fixture.componentInstance;
    expect(c.shortKey).toBe('reprocess.short.repertoires');
    expect(el.querySelector('.rb-lead')!.textContent).toContain('reprocess.short.repertoires');
    expect(el.querySelector('.rb-lead app-help-hint')).not.toBeNull();
    const btn = el.querySelector('.reprocess-banner button.rb-btn') as HTMLElement;
    expect(btn.classList).toContain('mat-mdc-button');
    expect(btn.classList).not.toContain('mat-mdc-outlined-button');
    expect(getComputedStyle(el.querySelector('.reprocess-banner')!).flexWrap).toBe('nowrap');
    c.status = { ...c.status!, reprocessableLocally: 1 };
    expect(c.shortKey).toBe('reprocess.shortOne.repertoires');
  });

  // d3-rk-update-banner-rot-hell: ein Angebot zum Aktualisieren tönt mit --rh-info, nicht mit --rh-warn (hell fast rot).
  it('tönt Symbol und Rahmen mit --rh-info statt --rh-warn', () => {
    const root = document.documentElement.style;
    root.setProperty('--rh-info', 'rgb(1, 2, 3)');
    root.setProperty('--rh-warn', 'rgb(200, 0, 0)');
    try {
      const fixture = TestBed.createComponent(ReprocessBannerComponent);
      fixture.componentInstance.section = 'repertoires';
      fixture.detectChanges();
      TestBed.inject(HttpTestingController).expectOne('/api/repertoires/reprocess/status').flush(
        { currentVersion: 2, total: 3, stale: 3, reprocessableLocally: 3, refetchable: 0, needsReimport: 0 });
      fixture.detectChanges();
      const el = fixture.nativeElement as HTMLElement;
      expect(getComputedStyle(el.querySelector('.rb-icon')!).color).toBe('rgb(1, 2, 3)');
      const banner = getComputedStyle(el.querySelector('.reprocess-banner')!);
      expect(banner.borderTopColor).not.toContain('200, 0, 0');
      expect(banner.backgroundColor).not.toContain('200, 0, 0');
    } finally {
      root.removeProperty('--rh-info');
      root.removeProperty('--rh-warn');
    }
  });

  it('Zählungen = 0 ohne Status', () => {
    const c = createComponent();
    expect(c.allCount).toBe(0);
  });

  it('kein Banner, wenn es nur nicht-aktualisierbare Einträge gibt (die tragen ihr eigenes (!))', () => {
    // Genau der gemeldete Zustand (2026-09-20): das Banner versprach eine Aktualisierung, die der Lauf
    // dann überspringen musste. Solche Einträge stehen jetzt als (!) an der Karte, nicht mehr hier.
    const fixture = TestBed.createComponent(ReprocessBannerComponent);
    fixture.componentInstance.section = 'courses';
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/courses/reprocess/status')
      .flush({ currentVersion: 2, total: 50, stale: 12, reprocessableLocally: 0, refetchable: 0, needsReimport: 12 });
    fixture.detectChanges();

    expect(fixture.componentInstance.actionableCount).toBe(0);
    expect((fixture.nativeElement as HTMLElement).querySelector('.reprocess-banner')).toBeNull();
  });

  it('run() postet ohne localOnly und lädt den Status verzögert neu (Hintergrundlauf)', fakeAsync(() => {
    const c = createComponent();
    const http = TestBed.inject(HttpTestingController);
    c.run();
    const req = http.expectOne('/api/courses/reprocess');
    expect(req.request.method).toBe('POST');
    req.flush({ started: true }, { status: 202, statusText: 'Accepted' });   // Server startet im Hintergrund
    expect(c.working).toBeFalse();
    expect(snackbar.info).toHaveBeenCalled();
    // Der Status wird erst nach kurzer Verzögerung neu geholt (der Lauf ist asynchron).
    tick(2500);
    http.expectOne('/api/courses/reprocess/status').flush(
      { currentVersion: 2, total: 50, stale: 12, reprocessableLocally: 0, refetchable: 0, needsReimport: 12 });
    expect(c.allCount).toBe(0);   // nur noch manuell zu re-importierende → Knopf verschwindet
    expect(c.status!.needsReimport).toBe(12);
  }));

  it('blockt den zweiten Klick, solange ein Lauf aktiv ist', () => {
    const c = createComponent();
    const http = TestBed.inject(HttpTestingController);
    c.run();
    http.expectOne('/api/courses/reprocess');   // erster Lauf offen
    c.run();                                      // zweiter Klick wird ignoriert (working === true)
    // Kein zweiter Request: verify() in afterEach würde sonst fehlschlagen.
    expect(c.working).toBeTrue();
  });

  it('ngOnInit übersteht gesperrten localStorage (Elternseite rendert weiter)', () => {
    // FALLE: `ngOnInit` las den Wegklick-Marker ungeschützt. In Browsern, die Site-Daten blockieren
    // (Safari „Alle Cookies blockieren", Enterprise-Policy), wirft schon der ZUGRIFF — und zwar
    // mitten in der Change Detection der Elternseite: die Kursliste rendert dann nicht, obwohl ihre
    // Daten längst da sind (dieselbe Fehlerklasse wie der Template-Getter-Fund in 0.317.2).
    spyOn(Storage.prototype, 'getItem').and.throwError('SecurityError');
    const fixture = TestBed.createComponent(ReprocessBannerComponent);
    fixture.componentInstance.section = 'courses';

    expect(() => fixture.detectChanges()).not.toThrow();
    TestBed.inject(HttpTestingController).expectOne('/api/courses/reprocess/status')
      .flush({ currentVersion: 2, total: 1, stale: 0, reprocessableLocally: 0, refetchable: 0, needsReimport: 0 });
  });

});

/** UI-Sweep z3 (z3-rk-courses-vorlauf): am Handy bekommt der Kurzsatz die volle Breite, der Knopf rückt in eine flache
 *  zweite Zeile rechts (statt als feste Spalte neben dem Text). */
describe('ReprocessBannerComponent Handy-Layout', () => {
  const css = ((ReprocessBannerComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');
  it('bricht unter 600 px um und stellt den Knopf rechts in die zweite Zeile', () => {
    const phone = css.slice(css.indexOf('@media (max-width: 599.98px)'));
    expect(phone).toMatch(/\.reprocess-banner[^{]*\{ ?flex-wrap: wrap/);
    expect(phone).toMatch(/\.rb-text[^{]*\{ ?display: contents/);
    expect(phone).toMatch(/\.rb-lead[^{]*\{ ?flex: 1 1 calc\(100% - 40px\)/);
    expect(phone).toMatch(/\.rb-btn[^{]*\{ ?order: 3; margin-left: auto/);
  });
});
