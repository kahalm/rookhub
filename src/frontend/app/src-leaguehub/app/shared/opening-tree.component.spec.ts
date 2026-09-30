import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LeagueApiService } from '../core/league-api.service';
import { OpeningTree, TreeFilter } from '../core/league.models';
import { OpeningTreeComponent, fenAfter, moveLabel } from './opening-tree.component';
import { TREE_FILTER_KEY } from '../core/tree-filter';

const BOARD: TreeFilter = { source: 'board', speeds: [], years: null, onlySure: false };

const TREE = (line: string, moves: OpeningTree['moves'], total = 10): OpeningTree =>
  ({ fide: '222', name: 'Hengl, Philip', color: 'w', line, total, ended: 0, moves });

describe('OpeningTreeComponent', () => {
  let fixture: ComponentFixture<OpeningTreeComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;

  beforeEach(() => localStorage.removeItem(TREE_FILTER_KEY));
  afterEach(() => localStorage.removeItem(TREE_FILTER_KEY));

  function create(inputs: Record<string, unknown>): HTMLElement {
    TestBed.configureTestingModule({
      imports: [OpeningTreeComponent],
      providers: [provideTranslateService({ fallbackLang: 'de' }), { provide: LeagueApiService, useValue: api }],
    });
    fixture = TestBed.createComponent(OpeningTreeComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    flushMicrotasks();
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('Zuglabel und Stellung', () => {
    expect(moveLabel(0, 'Nf3')).toBe('1.Sf3');
    expect(moveLabel(3, 'Nc6')).toBe('2…Sc6');
    expect(fenAfter(['e4', 'e5']).fen).toContain('4p3/4P3');
    expect(fenAfter(['e4', 'Ke3']).last).toEqual(['e2', 'e4']);           // ein Zug, der nicht geht, bricht ab
  });

  it('lädt, geht tiefer und über die Zugleiste zurück; Farbe umschaltbar', fakeAsync(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['tree']);
    api.tree.and.callFake(async (_f, color, line) => line.length === 0
      ? TREE('', [{ san: 'e4', n: 7, score: 64, last: '2025' }, { san: 'd4', n: 3, score: null, last: null }])
      : TREE(line.join(' '), [{ san: 'c5', n: 4, score: 50, last: '2024' }], 7));
    TestBed.configureTestingModule({
      imports: [OpeningTreeComponent],
      providers: [provideTranslateService({ fallbackLang: 'de' }), { provide: LeagueApiService, useValue: api }],
    });
    fixture = TestBed.createComponent(OpeningTreeComponent);
    fixture.componentRef.setInput('fide', '222');
    fixture.componentRef.setInput('token', 'TOK');
    fixture.componentRef.setInput('startColor', 'w');
    fixture.detectChanges();
    flushMicrotasks();
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    expect(api.tree).toHaveBeenCalledWith('222', 'w', [], 'TOK', BOARD);
    const rows = el.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('1.e4');
    expect(rows[0].textContent).toContain('7 (70 %)');
    expect(rows[0].textContent).toContain('64 %');
    (rows[0].querySelector('button.pl') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.tree).toHaveBeenCalledWith('222', 'w', ['e4'], 'TOK', BOARD);
    expect(el.querySelector('tbody tr')?.textContent).toContain('1…c5');
    (el.querySelector('.crumbs button') as HTMLButtonElement).click();          // „Start"
    flushMicrotasks();
    expect(fixture.componentInstance.line()).toEqual([]);
    fixture.componentInstance.setColor('s');
    flushMicrotasks();
    expect(api.tree).toHaveBeenCalledWith('222', 's', [], 'TOK', BOARD);
  }));

  it('ohne Online-Partien keine Quellen-Wahl, nur der Zeitraum', fakeAsync(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['tree']);
    api.tree.and.resolveTo(TREE('', []));
    const el = create({ fide: '222', boardGames: 12, onlineGames: 0 });
    expect(el.querySelector('.tree-filter .seg')).toBeNull();
    expect(el.querySelector('.tree-filter select')).not.toBeNull();
    fixture.componentInstance.setYears('3');
    flushMicrotasks();
    expect(api.tree).toHaveBeenCalledWith('222', 'w', [], null, { ...BOARD, years: 3 });
  }));

  it('Online dazu: Zeitformat wählbar, Zahlen je Quelle, Auswahl gemerkt', fakeAsync(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['tree']);
    api.tree.and.resolveTo({ ...TREE('', [{ san: 'e4', n: 9, score: 50, last: '2026' }], 9), board: 4, online: 5 });
    const el = create({ fide: '222', boardGames: 4, onlineGames: 30 });
    const seg = Array.from(el.querySelectorAll<HTMLButtonElement>('.tree-filter .seg button'));
    expect(seg.map(b => b.textContent?.trim())).toEqual(['Brett', 'Brett + online', 'Online']);
    expect(el.querySelector('.chips')).toBeNull();                                   // nur Brett: kein Zeitformat
    seg[1].click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.tree).toHaveBeenCalledWith('222', 'w', [], null, { ...BOARD, source: 'both' });
    expect(el.querySelector('[role=status]')?.textContent).toContain('davon 4 am Brett, 5 online');
    const blitz = Array.from(el.querySelectorAll<HTMLButtonElement>('.chips button')).find(b => b.textContent?.trim() === 'Blitz')!;
    blitz.click();
    flushMicrotasks();
    expect(api.tree).toHaveBeenCalledWith('222', 'w', [], null, { ...BOARD, source: 'both', speeds: ['blitz'] });
    expect(JSON.parse(localStorage.getItem(TREE_FILTER_KEY)!)).toEqual({ ...BOARD, source: 'both', speeds: ['blitz'] });
    // Nächste Karte: die gemerkte Auswahl gilt.
    fixture.componentInstance.fide = '333';
    fixture.componentInstance.ngOnChanges({ fide: {} as never });
    flushMicrotasks();
    expect(api.tree).toHaveBeenCalledWith('333', 'w', [], null, { ...BOARD, source: 'both', speeds: ['blitz'] });
  }));

  it('nur Online-Konten, keine Brettpartien: gleich online, „Brett" gesperrt', fakeAsync(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['tree']);
    api.tree.and.resolveTo(TREE('', []));
    const el = create({ fide: '222', boardGames: 0, onlineGames: 8 });
    expect(api.tree).toHaveBeenCalledWith('222', 'w', [], null, { ...BOARD, source: 'online' });
    const board = el.querySelector<HTMLButtonElement>('.tree-filter .seg button')!;
    expect(board.disabled).toBeTrue();
    expect(el.querySelector('.check')).not.toBeNull();                                // angemeldet: „nur gesicherte Konten"
  }));

  it('über einen Teilen-Link keine Wahl „nur gesicherte" (der Server liefert ohnehin nur diese)', fakeAsync(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['tree']);
    api.tree.and.resolveTo(TREE('', []));
    const el = create({ fide: '222', token: 'TOK', boardGames: 0, onlineGames: 8 });
    expect(el.querySelector('.chips')).not.toBeNull();
    expect(el.querySelector('.check')).toBeNull();
  }));
});
