import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
import { WIDE_SCREEN_QUERY } from '../../core/wide-screen';
import { TournamentDirectoryComponent } from './tournament-directory.component';
import { TournamentMapComponent } from './tournament-map.component';

@Component({ selector: 'app-tournament-map', template: '<div class="map-host"></div>' })
class MapStubComponent {
  @Input() entries: unknown;
  @Input() centre: unknown;
  @Input() colourBy: unknown;
  @Input() initialView: unknown;
  @Output() colourByChange = new EventEmitter<unknown>();
  @Output() viewChanged = new EventEmitter<unknown>();
  @Output() boundsChanged = new EventEmitter<string>();
  @Output() tilesFailed = new EventEmitter<void>();
  @Output() entryIgnored = new EventEmitter<void>();
  @Output() entrySelected = new EventEmitter<unknown>();
}

/**
 * UI-Sweep u1 (u1-t-kalender-karte): ab 2400 px stehen Liste und Karte nebeneinander unter EINEM Reiter, darunter
 * bleiben es drei Reiter. Eigene Datei, weil tournament-directory.component.spec.ts schon über 1 000 Zeilen hat.
 */
describe('TournamentDirectoryComponent Liste + Karte nebeneinander ab 2400 px', () => {
  let fixture: ComponentFixture<TournamentDirectoryComponent>;
  let component: TournamentDirectoryComponent;
  let http: HttpTestingController;

  async function setup(wide: boolean) {
    spyOn(window, 'matchMedia').and.callFake((q: string) => ({
      matches: q === WIDE_SCREEN_QUERY ? wide : false, media: q,
      addEventListener: () => {}, removeEventListener: () => {}, addListener: () => {}, removeListener: () => {},
    }) as unknown as MediaQueryList);
    TestBed.overrideComponent(TournamentDirectoryComponent, {
      remove: { imports: [TournamentMapComponent] },
      add: { imports: [MapStubComponent] },
    });
    await TestBed.configureTestingModule({
      imports: [TournamentDirectoryComponent],
      providers: [
        provideHttpClient(), provideHttpClientTesting(), provideRouter([]),
        provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' }),
        { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap({}) } } },
      ],
    }).compileComponents();
    const auth = TestBed.inject(AuthService);
    spyOnProperty(auth, 'currentUser', 'get').and.returnValue(null);
    spyOnProperty(auth, 'isLoggedIn', 'get').and.returnValue(false);
    spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);
    fixture = TestBed.createComponent(TournamentDirectoryComponent);
    component = fixture.componentInstance;
    http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/tournament-directory').flush({ items: [], total: 0, truncated: false });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  }

  afterEach(() => {
    for (const k of Object.keys(localStorage))
      if (k.startsWith(TournamentDirectoryComponent.ViewKey)) localStorage.removeItem(k);
  });

  const el = () => fixture.nativeElement as HTMLElement;
  const tabLabels = () => Array.from(el().querySelectorAll('.mdc-tab__text-label')).map(t => t.textContent!.trim());

  it('breit: zwei Reiter, Liste und Karte im ersten nebeneinander; die Karte lädt ihren Ausschnitt mit', async () => {
    await setup(true);
    expect(component.split()).toBeTrue();
    expect(tabLabels()).toEqual(['tournamentDirectory.tabs.listAndMap', 'tournamentDirectory.tabs.calendar']);
    const split = el().querySelector('.split-view')!;
    expect(split.querySelector('.split-list .empty-state')).not.toBeNull();
    expect(split.querySelector('.split-map app-tournament-map')).not.toBeNull();

    component.onBoundsChanged('46.0,5.0,55.0,17.0');
    http.expectOne(r => r.url === '/api/tournament-directory/map').flush({ items: [], truncated: false });

    // Neu laden (Filter) holt beides: Liste UND Kartenpunkte
    component.reload();
    http.expectOne(r => r.url === '/api/tournament-directory').flush({ items: [], total: 0, truncated: false });
    http.expectOne(r => r.url === '/api/tournament-directory/map').flush({ items: [], truncated: false });

    // Zweiter Reiter = Kalender (der Karten-Reiter fällt weg)
    component.onTabChange(1);
    expect(component.tab).toBe('calendar');
    http.expectOne(r => r.url === '/api/tournament-directory/calendar').flush({ days: [] });
    http.verify();
  });

  it('breit mit gemerktem Karten-Reiter: geteilte Ansicht, die Liste lädt mit', async () => {
    await setup(true);
    component.tab = 'map';
    component.reload();
    http.expectOne(r => r.url === '/api/tournament-directory').flush({ items: [], total: 0, truncated: false });
    fixture.detectChanges();
    expect(el().querySelector('.split-view')).not.toBeNull();
  });

  it('schmal: drei Reiter wie bisher, keine geteilte Ansicht', async () => {
    await setup(false);
    expect(component.split()).toBeFalse();
    expect(tabLabels()).toEqual(['tournamentDirectory.tabs.list', 'tournamentDirectory.tabs.map', 'tournamentDirectory.tabs.calendar']);
    expect(el().querySelector('.split-view')).toBeNull();
    expect(el().querySelector('app-tournament-map')).toBeNull();
    component.onTabChange(1);
    expect(component.tab).toBe('map');
  });

  it('CSS: Breitbild-Regeln nur in @media (min-width: 2400px), darunter unverändert', () => {
    const css = ((TournamentDirectoryComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n')
      .replace(/%NS%/g, '').replace(/\s+/g, '');
    const at = css.indexOf('@media(min-width:2400px)');
    expect(at).toBeGreaterThan(-1);
    const wide = css.slice(at);
    expect(wide).toMatch(/\.page[^{]*\{max-width:var\(--page-max-width-wide,1880px\)/);
    expect(wide).toMatch(/\.split-view[^{]*\{display:grid;/);
    expect(wide).toMatch(/\.split-map[^{]*\{position:sticky;/);
    expect(css.slice(0, at)).not.toContain('page-max-width-wide');
    expect(css.slice(0, at)).not.toContain('split-');
  });
});

/** alles-voll-2: Die Seite war ab 2400 px 4 540 px breit (inaktiver Reiter ragte heraus), die Karte endete unter dem
 *  Fenster. Karma läuft schmal — geprüft wird der CSS-Text der Breitbild-Stufe. */
describe('TournamentDirectoryComponent Breitbild-CSS', () => {
  const css = ((TournamentDirectoryComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');
  const wide = css.slice(css.indexOf('@media (min-width: 2400px)'));
  it('schneidet die Reiter-Hülle waagrecht ab (clip statt visible), damit kein Reiter die Seite verbreitert', () => {
    expect(wide).toMatch(/mat-mdc-tab-body-wrapper[^{]*\{ ?overflow-x: clip; ?overflow-y: visible/);
    expect(wide).not.toMatch(/:is\([^)]*mat-mdc-tab-body-wrapper/);
  });
  it('baut die Kartenspalte als Flex-Säule fester Höhe: Karte füllt, Trefferzeile bleibt im Fenster (b1)', () => {
    // b1-t-karte-trefferzeile-abgeschnitten: vorher Karte fest 100vh - 16rem, die Trefferzeile darunter ragte hinaus.
    expect(wide).toMatch(/\.split-map[^{]*\{ ?position: sticky; ?top: 1rem; ?display: flex; ?flex-direction: column; ?height: calc\(100vh - 17rem\)/);
    expect(wide).toMatch(/\.split-map[^{,]*> ?\*[^{]*\{ ?flex: none/);
    expect(wide).toMatch(/\.map-host[^{]*\{ ?flex: 1 1 auto; ?min-height: 0; ?height: auto !important/);
    expect(wide).not.toContain('calc(100vh - 16rem)');
    // Unter 2400 px bleibt die Karte ohne Säule (Template ohne .split-view, CSS ohne split-Regeln).
    expect(css.slice(0, css.indexOf('@media (min-width: 2400px)'))).not.toContain('split-map');
  });
});
