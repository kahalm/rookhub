import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks, tick } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';
import { ClubApiService } from '../../core/club-api.service';
import { ClubMatch, LeagueScanState } from '../../core/club.models';
import { ClubScanPageComponent } from './club-scan-page.component';

const PLY = (w: number, written: string, san: string, uci: string, extra = {}) =>
  ({ w, written, san, uci, match: 'exact', uncertain: false, ...extra });

const STATE: LeagueScanState = {
  scan: { id: 7, status: 'done', notationLanguage: 'de', createdAt: '2026-09-28T08:00:00Z', rounds: 1, moveCount: 4,
    uncertainCount: 1, unresolvedCount: 0 },
  notationLanguage: 'de',
  written: ['e4', 'e5', 'Sf3?', 'Sc6'],
  boxes: [null, null, null, null],
  plies: [
    PLY(0, 'e4', 'e4', 'e2e4'), PLY(1, 'e5', 'e5', 'e7e5'),
    PLY(2, 'Sf3?', 'Nf3', 'g1f3', { match: 'fuzzy', uncertain: true, options: [
      { san: 'Nf3', uci: 'g1f3', match: 'fuzzy', reach: 1, preview: ['Nc6'] },
      { san: 'Nh3', uci: 'g1h3', match: 'guess', reach: 1, preview: ['Nc6'] }] }),
    PLY(3, 'Sc6', 'Nc6', 'b8c6'),
  ],
  unresolved: [], unresolvedFrom: null,
  white: 'Oberschmid', black: 'Hengl', event: 'Simultan', date: '5.6.26', result: '0-1', ownerSide: 'white',
};
const MATCH = (blackLeague: boolean): ClubMatch => ({
  white: { league: true, ambiguous: false, name: 'Oberschmid, Patrik', fide: '900' },
  black: blackLeague ? { league: true, ambiguous: false, name: 'Hengl, Philip', fide: '222' } : { league: false, ambiguous: false, name: null, fide: null },
});

describe('ClubScanPageComponent', () => {
  let fixture: ComponentFixture<ClubScanPageComponent>;
  let api: jasmine.SpyObj<ClubApiService>;

  beforeEach(() => {
    api = jasmine.createSpyObj<ClubApiService>('ClubApiService', ['scan', 'photo', 'match', 'players', 'resolve', 'addGame', 'discard']);
    api.scan.and.resolveTo(structuredClone(STATE));
    api.photo.and.rejectWith(new Error('kein Foto'));
    api.match.and.resolveTo(MATCH(true));
    api.players.and.resolveTo([]);
  });

  function create(): HTMLElement {
    TestBed.configureTestingModule({
      imports: [ClubScanPageComponent],
      providers: [
        provideRouter([]),
        provideTranslateService({ fallbackLang: 'de' }),
        { provide: ClubApiService, useValue: api },
        { provide: ActivatedRoute, useValue: { snapshot: { paramMap: convertToParamMap({ id: '7' }) } } },
        { provide: AuthService, useValue: { has: () => true, currentUser: { username: 'patrik' } } },
      ],
    });
    fixture = TestBed.createComponent(ClubScanPageComponent);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('übernimmt Kopfdaten und springt auf die unsichere Stelle; die Vorschau zeigt „Schwaz" statt meines Namens', fakeAsync(() => {
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    const c = fixture.componentInstance;
    expect(c.s.cursor()).toBe(2);
    expect(c.year()).toBe(2026);
    expect(c.result()).toBe('0-1');
    expect(el.querySelector('.where')?.textContent).toContain('Sf3');       // deutsche Figuren
    expect(el.querySelector('.preview')?.textContent).toContain('2026 · Schwaz – Hengl, Philip · 0-1');
    expect(el.textContent).toContain('Ligaspieler: Hengl, Philip');
    expect(el.textContent).not.toContain('Kein Ligaspieler erkannt');
  }));

  it('eine andere Lesart wählen lässt den Rest neu lesen', fakeAsync(() => {
    create();
    flushMicrotasks();
    const c = fixture.componentInstance;
    api.resolve.and.returnValue(of({ plies: [PLY(3, 'Sc6', 'Nc6', 'b8c6')], unresolved: [] }));
    c.s.choose(STATE.plies[2].options![1]);
    expect(api.resolve).toHaveBeenCalledWith(7, ['e4', 'e5', 'Nh3'], 3);
    expect(c.s.plies().map(p => p.san)).toEqual(['e4', 'e5', 'Nh3', 'Nc6']);
  }));

  it('ohne Ligaspieler nach dem Ersetzen: Warnung', fakeAsync(() => {
    api.match.and.resolveTo(MATCH(false));
    const el = create();
    flushMicrotasks();
    fixture.detectChanges();
    expect(el.textContent).toContain('Kein Ligaspieler erkannt');
    fixture.componentInstance.anonymize.set(false);                  // dann bleibt ich selbst als Ligaspieler
    fixture.detectChanges();
    expect(el.textContent).not.toContain('Kein Ligaspieler erkannt');
  }));

  it('Übernehmen schickt Züge, Seite und Einlesung und geht zur Liste', fakeAsync(() => {
    create();
    flushMicrotasks();
    const router = TestBed.inject(Router);
    spyOn(router, 'navigate').and.resolveTo(true);
    api.addGame.and.resolveTo({ id: 11, anonymized: true });
    void fixture.componentInstance.save();
    flushMicrotasks();
    expect(api.addGame).toHaveBeenCalledWith(jasmine.objectContaining({
      moves: ['e4', 'e5', 'Nf3', 'Nc6'], white: 'Oberschmid', black: 'Hengl', year: 2026, result: '0-1',
      ownerSide: 'white', anonymize: true, scanId: 7,
    }));
    expect(router.navigate).toHaveBeenCalledWith(['/verein'], jasmine.anything());
    tick(1000);
  }));
});
