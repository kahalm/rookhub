import { ChangeDetectionStrategy, Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { Router, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { firstValueFrom } from 'rxjs';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { renderAfterHttpInterceptor } from '../../core/render-after-http.interceptor';
import { setEinkActive } from '../../core/eink';
import { VisitorOriginsComponent } from './visitor-origins.component';
import { VisitorOrigins, WorldMap } from './visitor-origins.service';

const MAP: WorldMap = {
  w: 960, h: 400,
  views: {
    world: [0, 0, 960, 400], eu: [413.3, 31.2, 200, 107.8], na: [26.7, 0, 320, 218.4], sa: [256, 195.7, 138.7, 201.4],
    af: [426.7, 124.8, 194.7, 212.8], as: [546.7, 76.6, 333.3, 192.9], oc: [773.3, 229.8, 186.7, 144.7],
  },
  countries: [
    { id: 'AT', c: 'eu', d: 'M505,100l10,0 0,5 -10,0z' },
    { id: 'DE', c: 'eu', d: 'M500,85l12,0 0,12 -12,0z' },
    { id: 'CH', c: 'eu', d: 'M496,101l8,0 0,4 -8,0z' },
    { id: 'US', c: 'na', d: 'M150,90l160,0 0,60 -160,0z' },
    { id: 'BR', c: 'sa', d: 'M290,230l80,0 0,80 -80,0z' },
    { id: 'JP', c: 'as', d: 'M830,120l10,0 0,20 -10,0z' },
  ],
};

function origins(over: Partial<VisitorOrigins> = {}): VisitorOrigins {
  return {
    admin: false, days: null, surface: null, uniqueUsers: 20, countries: 4, unknownUsers: 2, hits: null, since: null,
    byCountry: [
      { country: 'AT', users: 12, hits: null },
      { country: 'DE', users: 5, hits: null },
      { country: 'US', users: 3, hits: null },
      { country: 'BR', users: 1, hits: null },
    ],
    ...over,
  };
}

/** Viele Länder (für „N weitere Länder“): ISO-Kürzel, die `Intl.DisplayNames` kennt. */
const MANY = ['AT', 'DE', 'CH', 'IT', 'FR', 'ES', 'PL', 'HU', 'HR', 'SI', 'NL', 'BE', 'SE'];

describe('VisitorOriginsComponent (/visitors, „RookHub weltweit“)', () => {
  let http: HttpTestingController;
  let harness: RouterTestingHarness;
  let el: HTMLElement;

  async function open(url: string, data: VisitorOrigins | null = origins(), map: WorldMap | null = MAP): Promise<VisitorOriginsComponent> {
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideRouter([{ path: 'visitors', component: VisitorOriginsComponent }]),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    const component = await harness.navigateByUrl(url, VisitorOriginsComponent);
    el = harness.routeNativeElement!;
    if (map) http.expectOne('/assets/geo/world-countries.json').flush(map);
    if (data) http.expectOne(r => r.url === '/api/visitor-origins').flush(data);
    harness.detectChanges();
    return component;
  }

  const kpi = (name: string) => el.querySelector(`.vo-kpi-${name} dd`)?.textContent?.trim();
  const rowNames = () => Array.from(el.querySelectorAll('.vo-table tbody tr th')).map(th => th.textContent!.trim());
  const chips = (index: number) => Array.from(el.querySelectorAll('mat-chip-listbox')[index]?.querySelectorAll('mat-chip-option') ?? [])
    .map(c => c.textContent!.trim());
  const path = (id: string) => el.querySelector(`svg path[data-id="${id}"]`) as SVGPathElement;

  afterEach(() => { setEinkActive(false); http.verify(); });

  // ── Normales Konto ─────────────────────────────────────────────────────────────────────────────

  it('normales Konto: Karte + Rangliste, „Eindeutige Benutzer“ ohne Zeitraum, KEINE Filterleiste, keine Aufrufe', async () => {
    await open('/visitors');

    expect(el.querySelector('.vo-filters')).toBeNull();
    // einzige Chip-Leiste ist die Kontinent-Wahl
    expect(el.querySelectorAll('mat-chip-listbox').length).toBe(1);
    expect(chips(0)).toEqual(['world', 'eu', 'na', 'sa', 'af', 'as', 'oc'].map(v => 'visitors.continent.' + v));

    expect(kpi('users')).toBe('20');
    expect(el.querySelector('.vo-kpi-users dt')!.textContent!.trim()).toBe('visitors.kpi.users');
    expect(kpi('countries')).toBe('4');
    expect(el.querySelector('.vo-kpi-hits')).toBeNull();
    expect(el.textContent).not.toContain('visitors.kpi.hits');
    expect(el.textContent).not.toContain('visitors.filter');

    // Rangliste als Datentabelle: Kopfzeile + Zeilenköpfe, absteigend, „Unbekannt“ am Ende
    expect(Array.from(el.querySelectorAll('.vo-table thead th')).map(th => th.getAttribute('scope'))).toEqual(['col', 'col', 'col']);
    expect(rowNames()).toEqual(['Austria', 'Germany', 'United States', 'Brazil', 'visitors.unknown']);
    expect(el.querySelector('.vo-table')!.getAttribute('aria-labelledby')).toBe('vo-rank-title');

    // Karte: beschriftet, ein Pfad je Land, Stufe nach Wert (Farbe nie allein — die Zahl steht in der Tabelle)
    const svg = el.querySelector('.vo-map svg')!;
    expect(svg.getAttribute('role')).toBe('img');
    expect(svg.getAttribute('aria-label')).toBe('visitors.mapLabel');
    expect(svg.getAttribute('viewBox')).toBe('0 0 960 400');
    expect(el.querySelectorAll('.vo-map svg path').length).toBe(6);
    expect(path('AT').classList).toContain('vo-s5');
    expect(path('JP').classList).toContain('vo-s0');
    expect(path('BR').classList.contains('vo-s0')).toBeFalse();

    // Namensnennung + Datenschutz-Hinweis auf der Seite
    const link = el.querySelector('.vo-foot a') as HTMLAnchorElement;
    expect(link.textContent).toBe('IP Geolocation by DB-IP');
    expect(link.getAttribute('href')).toBe('https://db-ip.com');
    expect(el.querySelector('.vo-foot')!.textContent).toContain('visitors.privacyNote');
    // nichts Zeitbezogenes für normale Konten
    expect(el.textContent).not.toContain('visitors.since');
  });


  it('„VPN“ (XV): steht als „VPN“ in der Rangliste, zählt nicht als Land, färbt nichts und verschiebt die Farbskala nicht', async () => {
    await open('/visitors', origins({
      uniqueUsers: 50, unknownUsers: 0,
      byCountry: [
        { country: 'XV', users: 30, hits: null },
        { country: 'AT', users: 12, hits: null },
        { country: 'DE', users: 5, hits: null },
      ],
    }));
    expect(rowNames()).toEqual(['visitors.vpn', 'Austria', 'Germany']);
    expect(kpi('countries')).toBe('2');
    expect(el.querySelector('svg path[data-id="XV"]')).toBeNull();
    expect(path('AT').classList).toContain('vo-s5');      // Maximum der Karte ist AT, nicht die 30 VPN-Benutzer
    harness.detectChanges();
    await harness.navigateByUrl('/visitors?continent=eu', VisitorOriginsComponent);
    harness.detectChanges();
    expect(rowNames()).toEqual(['Austria', 'Germany']);  // VPN gehört zu keinem Erdteil
  });
  it('normales Konto: fragt den Server ohne Parameter — auch wenn die URL days/surface trägt, bleibt die Leiste weg', async () => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting(), provideNoopAnimations(),
        provideRouter([{ path: 'visitors', component: VisitorOriginsComponent }]), provideTranslateService({ fallbackLang: 'en' })],
    });
    http = TestBed.inject(HttpTestingController);
    harness = await RouterTestingHarness.create();
    await harness.navigateByUrl('/visitors', VisitorOriginsComponent);
    el = harness.routeNativeElement!;
    http.expectOne('/assets/geo/world-countries.json').flush(MAP);
    const req = http.expectOne(r => r.url === '/api/visitor-origins');
    expect(req.request.params.keys()).toEqual([]);
    // Der Server antwortet einem Nicht-Admin immer ungefiltert (admin: false) — danach richtet sich die Seite.
    req.flush(origins());
    harness.detectChanges();
    expect(el.querySelector('.vo-filters')).toBeNull();
  });

  it('Welt: zehn Länder, der Rest als „N weitere Länder“; „Alle zeigen“ klappt auf und wieder zu', async () => {
    const byCountry = MANY.map((country, i) => ({ country, users: 100 - i, hits: null }));
    await open('/visitors', origins({ byCountry, countries: MANY.length, uniqueUsers: 900, unknownUsers: 0 }));

    expect(el.querySelectorAll('.vo-table tbody tr').length).toBe(11);
    expect(el.querySelector('.vo-rest th')!.textContent!.trim()).toBe('visitors.table.more');
    expect(el.querySelector('.vo-rest .vo-num')!.textContent!.trim()).toBe(String(90 + 89 + 88));

    (el.querySelector('.vo-more') as HTMLButtonElement).click();
    harness.detectChanges();
    expect(el.querySelectorAll('.vo-table tbody tr').length).toBe(13);
    expect(el.querySelector('.vo-rest')).toBeNull();
    (el.querySelector('.vo-more') as HTMLButtonElement).click();
    harness.detectChanges();
    expect(el.querySelectorAll('.vo-table tbody tr').length).toBe(11);
  });

  // ── Kontinent-Ansicht ──────────────────────────────────────────────────────────────────────────

  it('?continent=eu: Ausschnitt, Kennzahlen und Rangliste nur Europas, Skala neu, Weg zurück zur Welt', async () => {
    await open('/visitors?continent=eu');

    const svg = el.querySelector('.vo-map svg')!;
    expect(svg.getAttribute('viewBox')).toBe('413.3 31.2 200 107.8');
    expect(svg.getAttribute('aria-label')).toBe('visitors.mapLabelContinent');
    expect(kpi('users')).toBe('17');          // AT 12 + DE 5 — Summe der Länder des Kontinents
    expect(kpi('countries')).toBe('2');
    expect(rowNames()).toEqual(['Austria', 'Germany']);   // ohne „Unbekannt“, ohne USA/Brasilien

    // Länder anderer Kontinente blass und ohne Stufe; Europa gegen sein eigenes Maximum
    expect(path('US').classList).toContain('vo-dim');
    expect(path('US').classList).toContain('vo-s0');
    expect(path('AT').classList).toContain('vo-s5');
    expect(path('AT').classList.contains('vo-dim')).toBeFalse();
    expect(path('CH').classList).toContain('vo-s0');

    const back = el.querySelector('.vo-back') as HTMLButtonElement;
    expect(back.textContent).toContain('visitors.backToWorld');
    back.click();
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/visitors');
    expect(el.querySelector('.vo-map svg')!.getAttribute('viewBox')).toBe('0 0 960 400');
    expect(el.querySelector('.vo-back')).toBeNull();
    expect(kpi('users')).toBe('20');
  });

  it('Kontinent ohne Besucher: Karte bleibt, statt der Tabelle ein Satz', async () => {
    await open('/visitors?continent=oc');
    expect(kpi('users')).toBe('0');
    expect(el.querySelector('.vo-table')).toBeNull();
    expect(el.querySelector('.vo-none')!.textContent).toContain('visitors.emptyContinent');
    expect(el.querySelector('.vo-map svg')).not.toBeNull();
  });

  it('unbekannter Kontinent in der URL fällt auf die Welt zurück', async () => {
    const c = await open('/visitors?continent=mars');
    expect(c.view()).toBe('world');
  });

  it('Klick auf ein Land der Weltkarte öffnet dessen Kontinent (Zustand in der URL) und zeigt seine Zahl', async () => {
    const c = await open('/visitors');
    path('US').dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await harness.fixture.whenStable();
    harness.detectChanges();

    expect(TestBed.inject(Router).url).toBe('/visitors?continent=na');
    expect(c.view()).toBe('na');
    expect(c.selected()).toBe('US');
    expect(el.querySelector('.vo-status strong')!.textContent).toBe('visitors.countryValue');
    expect(el.querySelector('.vo-status')!.getAttribute('aria-live')).toBe('polite');
    expect(path('US').classList).toContain('vo-sel');
    expect(el.querySelector('.vo-row-sel th')!.textContent).toContain('United States');

    // im Kontinent: Tap auf dasselbe Land hebt die Wahl auf, ein blasses Nachbarland wechselt den Kontinent
    path('US').dispatchEvent(new MouseEvent('click', { bubbles: true }));
    harness.detectChanges();
    expect(c.selected()).toBeNull();
    expect(el.querySelector('.vo-status')!.textContent).toContain('visitors.mapHintContinent');
    path('BR').dispatchEvent(new MouseEvent('click', { bubbles: true }));
    await harness.fixture.whenStable();
    expect(c.view()).toBe('sa');
  });

  it('Kontinent-Chips: Tastatur-/Klick-Wahl schreibt ?continent=…, „Welt“ entfernt den Parameter', async () => {
    const c = await open('/visitors');
    const options = el.querySelectorAll('.vo-views mat-chip-option button');
    (options[5] as HTMLElement).click();                       // Asien
    await harness.fixture.whenStable();
    harness.detectChanges();
    expect(TestBed.inject(Router).url).toBe('/visitors?continent=as');
    expect(c.view()).toBe('as');
    // die Chips sind echte Optionen einer Listbox (Pfeiltasten/Leertaste von Material), die gewählte ist markiert
    expect(el.querySelector('.vo-views')!.getAttribute('role')).toBe('listbox');
    expect(el.querySelectorAll('.vo-views mat-chip-option')[5].classList).toContain('mat-mdc-chip-selected');

    (el.querySelectorAll('.vo-views mat-chip-option button')[0] as HTMLElement).click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/visitors');
  });

  it('Mauszeiger über einem Land: Name + Zahl am Zeiger, beim Verlassen weg', async () => {
    const c = await open('/visitors');
    path('AT').dispatchEvent(new MouseEvent('mousemove', { bubbles: true, clientX: 10, clientY: 10 }));
    harness.detectChanges();
    expect(c.tip()?.text).toBe('visitors.countryValue');
    expect(el.querySelector('.vo-tip')).not.toBeNull();
    el.querySelector('.vo-map svg')!.dispatchEvent(new MouseEvent('mouseleave'));
    harness.detectChanges();
    expect(el.querySelector('.vo-tip')).toBeNull();
  });

  // ── Admin ──────────────────────────────────────────────────────────────────────────────────────

  it('Admin: Filterleiste (Seite + Zeitraum), Aufrufe als Kennzahl und Spalte; die Wahl lädt mit Parametern neu', async () => {
    const admin = origins({ admin: true, hits: 480, since: '2026-11-15', byCountry: origins().byCountry.map(c => ({ ...c, hits: c.users * 10 })) });
    await open('/visitors', admin);

    expect(el.querySelector('.vo-filters')).not.toBeNull();
    expect(chips(0)).toEqual(['visitors.filter.allSurfaces', 'visitors.surface.rookhub', 'visitors.surface.leaguehub',
      'visitors.surface.kidhub', 'visitors.surface.clubhub', 'visitors.surface.turnier']);
    expect(chips(1).length).toBe(5);
    expect(kpi('users')).toBe('20');
    expect(el.querySelector('.vo-kpi-users dt')!.textContent!.trim()).toBe('visitors.kpi.users');
    expect(kpi('hits')).toBe('480');
    expect(el.querySelector('.vo-foot')!.textContent).toContain('visitors.since');
    expect(el.querySelectorAll('.vo-table thead th').length).toBe(4);
    expect(el.querySelector('.vo-table tbody tr')!.querySelectorAll('td.vo-num')[2].textContent!.trim()).toBe('120');

    // Zeitraum 30 Tage → URL + neuer Abruf mit days=30
    (el.querySelectorAll('.vo-filters mat-chip-listbox')[1].querySelectorAll('mat-chip-option button')[1] as HTMLElement).click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/visitors?days=30');
    harness.detectChanges();
    const byDays = http.expectOne(r => r.url === '/api/visitor-origins');
    expect(byDays.request.params.get('days')).toBe('30');
    byDays.flush({ ...admin, days: 30, uniqueUsers: 7 });
    harness.detectChanges();
    expect(kpi('users')).toBe('7');

    // Oberfläche LeagueHub dazu
    (el.querySelectorAll('.vo-filters mat-chip-listbox')[0].querySelectorAll('mat-chip-option button')[2] as HTMLElement).click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/visitors?days=30&surface=leaguehub');
    harness.detectChanges();
    const bySurface = http.expectOne(r => r.url === '/api/visitor-origins');
    expect(bySurface.request.params.get('surface')).toBe('leaguehub');
    expect(bySurface.request.params.get('days')).toBe('30');
    bySurface.flush({ ...admin, days: 30, surface: 'leaguehub' });
  });

  it('Admin: der Kontinent-Wechsel lädt nichts nach', async () => {
    await open('/visitors?days=7', origins({ admin: true, days: 7, hits: 10 }));
    (el.querySelectorAll('.vo-views mat-chip-option button')[1] as HTMLElement).click();
    await harness.fixture.whenStable();
    expect(TestBed.inject(Router).url).toBe('/visitors?days=7&continent=eu');
    http.expectNone(r => r.url === '/api/visitor-origins');
    harness.detectChanges();
    // Aufrufe als Kennzahl nur für die Welt (je Kontinent stehen sie in der Spalte)
    expect(el.querySelector('.vo-kpi-hits')).toBeNull();
  });

  // ── Leer, Fehler ───────────────────────────────────────────────────────────────────────────────

  it('leer nach dem Deploy: eine Leerkarte mit einem Satz, keine Karte und keine Tabelle', async () => {
    await open('/visitors', origins({ uniqueUsers: 0, countries: 0, unknownUsers: 0, since: null, byCountry: [] }));
    expect(el.querySelector('.vo-empty .empty-state p')!.textContent).toContain('visitors.emptyNew');
    expect(el.querySelector('.vo-map')).toBeNull();
    expect(el.querySelector('.vo-table')).toBeNull();
    expect(el.querySelector('app-loading-spinner')).toBeNull();
    expect(el.querySelector('.vo-foot a')).not.toBeNull();
  });

  it('leer für die Auswahl des Admins: nennt, seit wann gezählt wird', async () => {
    await open('/visitors?days=7', origins({ admin: true, days: 7, uniqueUsers: 0, countries: 0, unknownUsers: 0, since: '2026-11-15', byCountry: [] }));
    expect(el.querySelector('.vo-empty .empty-state p')!.textContent).toContain('visitors.emptyFiltered');
    expect(el.querySelector('.vo-filters')).not.toBeNull();
  });

  it('Ladefehler ist kein Leerzustand: app-load-error mit Wiederholen', async () => {
    await open('/visitors', null);
    http.expectOne(r => r.url === '/api/visitor-origins').flush('nope', { status: 500, statusText: 'Server Error' });
    harness.detectChanges();
    expect(el.querySelector('app-load-error')).not.toBeNull();
    expect(el.querySelector('.vo-empty')).toBeNull();

    (el.querySelector('app-load-error button') as HTMLButtonElement).click();
    http.expectOne(r => r.url === '/api/visitor-origins').flush(origins());
    harness.detectChanges();
    expect(kpi('users')).toBe('20');
  });

  it('Karte nicht ladbar: Kennzahlen und Rangliste bleiben, die Karte meldet den Fehler', async () => {
    await open('/visitors', null, null);
    http.expectOne('/assets/geo/world-countries.json').flush('nope', { status: 404, statusText: 'Not Found' });
    http.expectOne(r => r.url === '/api/visitor-origins').flush(origins());
    harness.detectChanges();
    expect(kpi('users')).toBe('20');
    expect(rowNames().length).toBe(5);
    expect(el.querySelector('.vo-map-col app-load-error')).not.toBeNull();
  });

  // ── Sprache, Zahlen ────────────────────────────────────────────────────────────────────────────

  it('Ländernamen und Zahlen in der Oberflächensprache (Intl), unbekanntes Kürzel bleibt Kürzel', async () => {
    const c = await open('/visitors', origins({ uniqueUsers: 12345, byCountry: [{ country: 'AT', users: 12000, hits: null }, { country: 'ZZ', users: 1, hits: null }] }));
    expect(kpi('users')).toBe('12,345');
    expect(c.countryName('AT')).toBe('Austria');

    await firstValueFrom(TestBed.inject(TranslateService).use('de'));
    harness.detectChanges();
    expect(kpi('users')).toBe('12.345');
    expect(rowNames()[0]).toBe('Österreich');
    expect(c.countryName('DE')).toBe('Deutschland');
    expect(c.countryName('ZZ').length).toBeGreaterThan(0);

    await firstValueFrom(TestBed.inject(TranslateService).use('hu'));
    harness.detectChanges();
    expect(rowNames()[0]).toBe('Ausztria');
  });

  // ── Layout ─────────────────────────────────────────────────────────────────────────────────────

  it('Handybreite 390 px: nichts ragt waagerecht hinaus (DESIGN 4.4)', async () => {
    const byCountry = MANY.map((country, i) => ({ country, users: 1000 - i, hits: 123456 }));
    await open('/visitors', origins({ admin: true, hits: 9999999, byCountry, countries: MANY.length }));
    el.style.cssText = 'display:block;width:390px';
    harness.detectChanges();
    const page = el.querySelector('.vo-page') as HTMLElement;
    expect(page.clientWidth).toBeLessThanOrEqual(390);
    expect(page.scrollWidth).toBeLessThanOrEqual(page.clientWidth);
    for (const sel of ['.vo-table', '.vo-map', '.vo-filters', '.vo-viewbar', '.vo-kpis']) {
      const box = (el.querySelector(sel) as HTMLElement).getBoundingClientRect();
      expect(box.right).withContext(sel).toBeLessThanOrEqual(page.getBoundingClientRect().right + 0.5);
    }
    el.style.cssText = '';
  });

  it('E-Ink: Stufen als Helligkeitstreppe aus den Flächen-Tokens, Grenzen als Linien (DESIGN 3.6)', async () => {
    await open('/visitors');
    const css = Array.from(document.querySelectorAll('style')).map(s => s.textContent ?? '').join('\n');
    expect(css).toMatch(/html\.eink[^{]*\.vo-s5[^{]*\{[^}]*--vo-fill: ?var\(--mat-sys-on-surface\)/);
    expect(css).toMatch(/html\.eink[^{]*\.vo-s0[^{]*\{[^}]*--vo-fill: ?var\(--mat-sys-surface\)/);
    expect(css).toMatch(/html\.eink[^{]*\.vo-c[^{]*\{[^}]*stroke: ?var\(--mat-sys-on-surface\)/);
    // gemessen: mit der Klasse eink ist die höchste Stufe so dunkel wie der Text, „kein Wert“ so hell wie die Fläche
    setEinkActive(true);
    const fill = (id: string) => getComputedStyle(path(id)).fill;
    expect(fill('AT')).not.toBe(fill('JP'));
    expect(fill('AT')).not.toBe(fill('BR'));
  });
});

/*
 * Angular-22-Falle (DESIGN 5.15): unter einer OnPush-Elternansicht zeichnet nach der Antwort nur neu, was in Signalen
 * liegt. Host OnPush, gezeichnet über autoDetectChanges + whenStable — kein detectChanges nach der Antwort.
 */
@Component({ selector: 'app-h-visitors', standalone: true, imports: [VisitorOriginsComponent],
  changeDetection: ChangeDetectionStrategy.OnPush, template: '<app-visitor-origins />' })
class VisitorsHost {}

describe('VisitorOriginsComponent unter OnPush-Elternansicht', () => {
  it('zeigt Zahlen und Karte nach den Antworten ohne weiteren Anstoß', async () => {
    await TestBed.configureTestingModule({
      imports: [VisitorsHost],
      providers: [provideHttpClient(withInterceptors([renderAfterHttpInterceptor])), provideHttpClientTesting(),
        provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const http = TestBed.inject(HttpTestingController);
    const fixture: ComponentFixture<VisitorsHost> = TestBed.createComponent(VisitorsHost);
    fixture.autoDetectChanges();
    await fixture.whenStable();
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('app-loading-spinner')).withContext('vor der Antwort').not.toBeNull();

    http.expectOne(r => r.url === '/api/visitor-origins').flush(origins());
    http.expectOne('/assets/geo/world-countries.json').flush(MAP);
    await fixture.whenStable();

    expect(el.querySelector('app-loading-spinner')).toBeNull();
    expect(el.querySelector('.vo-kpi-users dd')!.textContent!.trim()).toBe('20');
    expect(el.querySelectorAll('.vo-map svg path').length).toBe(6);
    http.verify();
  });
});
