import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LeagueApiService } from '@lh/core/league-api.service';
import { OpeningTree, TreeFilter } from '@lh/core/league.models';
import { OpeningTreeComponent, fenAfter, moveLabel } from './opening-tree.component';

const BOARD: TreeFilter = { source: 'board', speeds: [], years: null, withUnsure: false };

const TREE = (line: string, moves: OpeningTree['moves'], total = 10): OpeningTree =>
  ({ fide: '222', name: 'Hengl, Philip', color: 'w', line, total, ended: 0, moves });

describe('OpeningTreeComponent', () => {
  let fixture: ComponentFixture<OpeningTreeComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;


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

  it('fragt mit dem Filter der Karte und lädt bei neuem Filter in derselben Stellung neu (0.617.0)', fakeAsync(() => {
    api = jasmine.createSpyObj<LeagueApiService>('LeagueApiService', ['tree']);
    api.tree.and.resolveTo({ ...TREE('', [{ san: 'e4', n: 9, score: 50, last: '2026' }], 9), board: 4, online: 5 });
    const both: TreeFilter = { ...BOARD, source: 'both', speeds: ['blitz'] };
    const el = create({ fide: '222', filter: both });
    expect(api.tree).toHaveBeenCalledWith('222', 'w', [], null, both);
    expect(el.querySelector('[role=status]')?.textContent).toContain('davon 4 am Brett, 5 online');
    expect(el.querySelector('.tree-filter')).toBeNull();                              // die Leiste sitzt auf der Karte
    fixture.componentInstance.play('e4');
    flushMicrotasks();
    fixture.componentRef.setInput('filter', { ...both, years: 3 });
    fixture.detectChanges();
    flushMicrotasks();
    expect(api.tree).toHaveBeenCalledWith('222', 'w', ['e4'], null, { ...both, years: 3 });   // Stellung bleibt
  }));
});
