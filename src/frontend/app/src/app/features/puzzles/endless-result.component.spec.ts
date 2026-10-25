import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { EndlessResultComponent } from './endless-result.component';

describe('EndlessResultComponent', () => {
  function render(inputs: Record<string, unknown>) {
    TestBed.configureTestingModule({
      imports: [EndlessResultComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(EndlessResultComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return { fixture, el: fixture.nativeElement as HTMLElement, cmp: fixture.componentInstance };
  }
  const values = (el: HTMLElement) => Array.from(el.querySelectorAll('.go-value')).map(e => e.textContent!.trim());

  it('Sieg: Pokal, Leben statt Stufe, Nochmal/Zurück als Outputs', () => {
    const { el, cmp } = render({ kind: 'won', maxRating: 2100, solved: 30, lives: 1, level: 30, sessionSeconds: 65 });
    expect(el.querySelector('.stockfish-fish')!.textContent).toContain('🏆');
    expect(values(el)).toEqual(['2100', '30', '1', '1:05']);
    let again = 0, back = 0;
    cmp.playAgain.subscribe(() => again++);
    cmp.backToPuzzles.subscribe(() => back++);
    const [b1, b2] = Array.from(el.querySelectorAll('.gameover-actions button')) as HTMLButtonElement[];
    b1.click(); b2.click();
    expect([again, back]).toEqual([1, 1]);
  });

  it('Game Over: Stufe, Fehler-Ratings, Liste öffnet das Puzzle', () => {
    const { el, cmp } = render({
      kind: 'gameover', maxRating: 1650, solved: 4, level: 6, sessionSeconds: 45, mistakes: [1500, 1600],
      puzzles: [{ puzzleNumber: 1, puzzleId: 9, rating: 1500, solved: false }],
    });
    expect(values(el)).toEqual(['1650', '4', '6', '45s']);
    expect(el.querySelector('.mistake-ratings')).not.toBeNull();
    const opened: number[] = [];
    cmp.openPuzzle.subscribe(id => opened.push(id));
    (el.querySelector('.review-item.review-failed') as HTMLElement).click();
    expect(opened).toEqual([9]);
  });

  it('Archivieren folgt den Inputs (OnPush: Änderung der Lauf-Id kommt an)', () => {
    const { fixture, el, cmp } = render({ kind: 'gameover', isLoggedIn: true });
    expect(el.querySelector('.archive-btn')).toBeNull();
    fixture.componentRef.setInput('lastSessionId', 12);   // trifft asynchron nach dem Aufzeichnen ein
    fixture.detectChanges();
    let archived = 0;
    cmp.archive.subscribe(() => archived++);
    (el.querySelector('.archive-btn') as HTMLButtonElement).click();
    expect(archived).toBe(1);
    fixture.componentRef.setInput('lastSessionArchived', true);
    fixture.detectChanges();
    expect(el.querySelector('.archive-btn')).toBeNull();
    expect(el.querySelector('.archived-hint')).not.toBeNull();
  });

  it('History-Detail: nur „Zurück zur History“', () => {
    const { el, cmp } = render({ kind: 'gameover', historyView: true });
    let back = 0;
    cmp.backToHistory.subscribe(() => back++);
    const buttons = el.querySelectorAll('.gameover-actions button');
    expect(buttons.length).toBe(1);
    (buttons[0] as HTMLButtonElement).click();
    expect(back).toBe(1);
  });
});
