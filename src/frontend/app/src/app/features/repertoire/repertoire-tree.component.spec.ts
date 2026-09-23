import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { RepertoireTreeComponent } from './repertoire-tree.component';
import { MoveTreeService } from './move-tree.service';

describe('RepertoireTreeComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [RepertoireTreeComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(RepertoireTreeComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });

  async function render(popularity: any, note: string | null = null) {
    await TestBed.configureTestingModule({
      imports: [RepertoireTreeComponent],
      providers: [provideRouter([]), provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const tree = new MoveTreeService();
    tree.buildTree('[Event "a"]\n\n1. e4 e5 *\n\n[Event "b"]\n\n1. b4 d5 *\n');
    const fixture = TestBed.createComponent(RepertoireTreeComponent);
    fixture.componentRef.setInput('children', tree.children);
    fixture.componentRef.setInput('popularity', popularity);
    fixture.componentRef.setInput('popularityNote', note);
    fixture.detectChanges();
    return fixture;
  }

  const START_STATS = {
    status: 'ok', retryAfterSeconds: null, source: 'local', database: 'masters',
    total: 1000, white: 0, draws: 0, black: 0, opening: null, eco: null,
    moves: [{ uci: 'e2e4', san: 'e4', games: 440, white: 0, draws: 0, black: 0, averageRating: null, opening: null, eco: null }],
  };

  function itemFor(fixture: any, san: string): HTMLElement {
    return (Array.from(fixture.nativeElement.querySelectorAll('.child-item')) as HTMLElement[])
      .find(i => i.querySelector('.child-san')!.textContent!.trim() === san)!;
  }

  it('shows how often each move is played in this position — also for the own first move', async () => {
    const fixture = await render(START_STATS);
    expect(itemFor(fixture, 'e4').querySelector('.child-pop')!.textContent).toContain('44');
    // 1.b4 führt der Explorer hier nicht: kaum gespielt.
    expect(itemFor(fixture, 'b4').querySelector('.child-pop')!.textContent).toContain('—');
    expect(fixture.nativeElement.querySelector('.pop-source').textContent).toContain('repertoire.tree.popFrom');
  });

  it('without statistics no numbers; a note is shown instead of the source line', async () => {
    const fixture = await render(null, 'repertoire.tree.popToken');
    expect(fixture.nativeElement.querySelector('.child-pop')).toBeNull();
    expect(fixture.nativeElement.querySelector('.pop-source.note').textContent).toContain('repertoire.tree.popToken');
  });

  it('a position without games says so', async () => {
    const fixture = await render({ ...START_STATS, total: 0, moves: [] });
    expect(fixture.nativeElement.querySelector('.child-pop')).toBeNull();
    expect(fixture.nativeElement.querySelector('.pop-source').textContent).toContain('repertoire.tree.popNone');
  });
});
