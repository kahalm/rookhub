import { Component, EventEmitter, Input, Output } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AuthService } from '@rh/core/auth.service';
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
 * UI-Sweep b1: ausgeklapptes Filterfeld des Turnierkalenders. Eigene Datei, weil tournament-directory.component.spec.ts
 * schon über 1 000 Zeilen hat.
 */
describe('TournamentDirectoryComponent Filterfeld (UI-Sweep b1)', () => {
  let fixture: ComponentFixture<TournamentDirectoryComponent>;
  let component: TournamentDirectoryComponent;

  beforeEach(async () => {
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
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    http.expectOne(r => r.url === '/api/tournament-directory').flush({ items: [], total: 0, truncated: false });
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  afterEach(() => {
    for (const k of Object.keys(localStorage))
      if (k.startsWith(TournamentDirectoryComponent.ViewKey)) localStorage.removeItem(k);
  });

  const el = () => fixture.nativeElement as HTMLElement;
  const css = () => ((TournamentDirectoryComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles)
    .join('\n').replace(/%NS%/g, '').replace(/\s+/g, ' ');
  const mobileCss = () => { const all = css(); return all.slice(all.indexOf('@media (max-width: 768px)')); };

  it('b1-t-filter-chips-doppelt-offen: Wertechips nur bei zugeklapptem Filter', () => {
    expect(el().querySelectorAll('.bar-chips .value-chip').length).toBe(2);
    (el().querySelector('.bar-chips .range-chip') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(component.filtersOpen).toBeTrue();
    expect(el().querySelector('.bar-chips')).toBeNull();
    expect(el().querySelectorAll('.filter-extra .extra-narrow').length).toBe(2);
    component.filtersOpen = false;
    fixture.detectChanges();
    expect(el().querySelector('.bar-chips')).not.toBeNull();
  });

  it('b1-t-filter-feldbreiten: Filterfeld in Zeilen nach Sinn — Suche allein, Chips, Schalter, Rest', () => {
    component.filtersOpen = true;
    fixture.detectChanges();
    const rows = Array.from(el().querySelectorAll('.filter-extra > .extra-row')).map(r => r.classList[1]);
    expect(rows).toEqual(['period-row', 'search-row', 'chip-row', 'toggle-row', 'end-row']);
    const search = el().querySelector('.search-row')!;
    expect(search.children.length).toBe(1);
    expect(search.querySelector('.search-field')).not.toBeNull();
    expect(el().querySelectorAll('.chip-row mat-chip-listbox').length).toBe(3);
    expect(el().querySelectorAll('.toggle-row mat-slide-toggle').length).toBeGreaterThanOrEqual(3);
    const end = el().querySelector('.end-row')!;
    expect(end.querySelector('.players-field')).not.toBeNull();
    expect(end.lastElementChild!.textContent).toContain('tournamentDirectory.reset');
  });

  it('b1-t-filter-feldbreiten: Feldbreiten nach Inhalt, Zeitraum/Umkreis am Handy nur nebeneinander, wenn beide passen', () => {
    const all = css();
    expect(all).toMatch(/\.players-field[^{]*\{ ?width: 14rem;? ?\}/);
    expect(all).not.toContain('width: 150px');
    expect(all).toMatch(/\.search-field[^{]*\{ ?flex: 1 1 18rem; ?min-width: 0/);
    expect(all).toMatch(/@media \(min-width: 769px\) ?\{ ?\.period-row[^{]*:not\(:has\(\.date-field/);
    const mobile = mobileCss();
    expect(mobile).toMatch(/\.period-row[^{]*> ?\.range-field[^{]*\{ ?flex: 1 1 13rem/);
    expect(mobile).toMatch(/\.period-row[^{]*> ?\.radius-field[^{]*\{ ?flex: 1 1 8rem/);
    expect(mobile).not.toContain('calc(50% - 0.375rem)');
  });

  it('b1-t-seitenpolster-handy-8px: 16 px Seitenpolster am Handy', () => {
    expect(mobileCss()).toMatch(/\.page[^{]*\{ ?padding: 0\.75rem 1rem;? ?\}/);
    expect(css()).not.toContain('padding: 0.75rem 0.5rem');
  });
});
