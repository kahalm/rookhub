import { WritableSignal, signal } from '@angular/core';
import { ComponentFixture, TestBed, fakeAsync, tick } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { PrepApiService } from './prep-api.service';
import { PREP_SEARCH_DELAY_MS, PrepSearchComponent } from './prep-search.component';
import { PrepHit } from './prep.models';

const HITS: PrepHit[] = [
  { id: 31252, name: 'Carlsen, Magnus', fide: '1503014', games: 8530, firstYear: 2001, lastYear: 2026, maxElo: 2882 },
  { id: 9, name: 'Carlsen, Henrik', fide: null, games: 12, firstYear: 2004, lastYear: 2004, maxElo: null },
];

describe('PrepSearchComponent', () => {
  let fixture: ComponentFixture<PrepSearchComponent>;
  let api: { search: jasmine.Spy; lastQuery: WritableSignal<string> };
  const el = () => fixture.nativeElement as HTMLElement;

  async function create(url = '/prep'): Promise<void> {
    api = { search: jasmine.createSpy('search').and.resolveTo(HITS), lastQuery: signal('') };
    TestBed.configureTestingModule({
      imports: [PrepSearchComponent],
      providers: [provideRouter([{ path: 'prep', component: PrepSearchComponent }, { path: 'prep/:id', children: [] }]),
        provideTranslateService({ fallbackLang: 'en' }), provideNoopAnimations(), { provide: PrepApiService, useValue: api }],
    });
    await TestBed.inject(Router).navigateByUrl(url);
    fixture = TestBed.createComponent(PrepSearchComponent);
    fixture.detectChanges();
  }

  function type(value: string): void {
    const input = el().querySelector('input')!;
    input.value = value;
    input.dispatchEvent(new Event('input'));
  }

  it('sucht nach einer kurzen Pause, nicht bei jedem Tastendruck, und erst ab zwei Zeichen', fakeAsync(() => {
    void create();
    tick();
    type('C');
    tick(PREP_SEARCH_DELAY_MS);
    expect(api.search).not.toHaveBeenCalled();
    type('Ca');
    tick(100);
    type('Carlsen');
    tick(PREP_SEARCH_DELAY_MS);
    expect(api.search).toHaveBeenCalledOnceWith('Carlsen');
    tick();
    fixture.detectChanges();
    const links = Array.from(el().querySelectorAll<HTMLAnchorElement>('.hits a'));
    expect(links.length).toBe(2);
    expect(links[0].getAttribute('href')).toBe('/prep/31252');
    expect(links[0].textContent).toContain('Carlsen, Magnus');
    expect(links[0].textContent).toContain('FIDE\u00a01503014');                 // bricht nicht zwischen FIDE und Nummer
    expect(links[0].textContent).toContain('2001–2026');
    expect(links[1].textContent).not.toContain('FIDE 1');
    expect(links[1].textContent).toContain('prep.noFide');              // ohne FIDE-ID sagt es das
    expect(api.lastQuery()).toBe('Carlsen');
  }));

  it('die Suche steht in der Adresse und läuft beim Zurückkommen wieder', fakeAsync(() => {
    void create('/prep?q=Hoecher');
    tick();
    expect(api.search).toHaveBeenCalledOnceWith('Hoecher');
    tick();
    fixture.detectChanges();
    expect(el().querySelector('input')!.value).toBe('Hoecher');
  }));

  it('keine Treffer bzw. Fehler sagt es', fakeAsync(() => {
    void create();
    tick();
    api.search.and.resolveTo([]);
    type('Zzyzx');
    tick(PREP_SEARCH_DELAY_MS);
    tick();
    fixture.detectChanges();
    expect(el().textContent).toContain('prep.noHits');
    api.search.and.rejectWith(new Error('500'));
    type('Zzyzxx');
    tick(PREP_SEARCH_DELAY_MS);
    tick();
    fixture.detectChanges();
    expect(el().querySelector('.err')).not.toBeNull();
    expect(el().querySelector('.hits')).toBeNull();
  }));
});
