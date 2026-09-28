import { ComponentFixture, TestBed, fakeAsync, flushMicrotasks } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LeagueApiService } from '../core/league-api.service';
import { OpeningTree } from '../core/league.models';
import { OpeningTreeComponent, fenAfter, moveLabel } from './opening-tree.component';

const TREE = (line: string, moves: OpeningTree['moves'], total = 10): OpeningTree =>
  ({ fide: '222', name: 'Hengl, Philip', color: 'w', line, total, ended: 0, moves });

describe('OpeningTreeComponent', () => {
  let fixture: ComponentFixture<OpeningTreeComponent>;
  let api: jasmine.SpyObj<LeagueApiService>;

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
    expect(api.tree).toHaveBeenCalledWith('222', 'w', [], 'TOK');
    const rows = el.querySelectorAll('tbody tr');
    expect(rows[0].textContent).toContain('1.e4');
    expect(rows[0].textContent).toContain('7 (70 %)');
    expect(rows[0].textContent).toContain('64 %');
    (rows[0].querySelector('button.pl') as HTMLButtonElement).click();
    flushMicrotasks();
    fixture.detectChanges();
    expect(api.tree).toHaveBeenCalledWith('222', 'w', ['e4'], 'TOK');
    expect(el.querySelector('tbody tr')?.textContent).toContain('1…c5');
    (el.querySelector('.crumbs button') as HTMLButtonElement).click();          // „Start"
    flushMicrotasks();
    expect(fixture.componentInstance.line()).toEqual([]);
    fixture.componentInstance.setColor('s');
    flushMicrotasks();
    expect(api.tree).toHaveBeenCalledWith('222', 's', [], 'TOK');
  }));
});
