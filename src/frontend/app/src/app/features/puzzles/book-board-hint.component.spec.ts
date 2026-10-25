import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { BookBoardHintComponent } from './book-board-hint.component';

describe('BookBoardHintComponent (Zerlegung Z18)', () => {
  function make(inputs: Record<string, unknown>) {
    TestBed.configureTestingModule({
      imports: [BookBoardHintComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(BookBoardHintComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return { el: fixture.nativeElement as HTMLElement, c: fixture.componentInstance };
  }

  it('zeigt das Zugrecht (schwarzer Punkt) ohne Vorspielzüge, sonst die Züge', () => {
    const { el } = make({ turnColor: 'black' });
    expect(el.querySelector('.tomove-dot.black')).not.toBeNull();
    expect(el.querySelector('.board-hint-tomove')?.textContent).toContain('common.blackToMove');
    TestBed.resetTestingModule();
    const moves = make({ moveHtml: '<b>1.e4</b>' });
    expect(moves.el.querySelector('.board-hint-tomove')).toBeNull();
    expect(moves.el.querySelector('b')?.textContent).toBe('1.e4');
  });

  it('Tipp-Knopf zählt die nächste Stufe und meldet den Klick; „Tipps melden“ gesperrt beim Speichern', () => {
    const { el, c } = make({ showTip: true, hintLevel: 1, totalHints: 3, canFlag: true, flagSaving: true });
    let tips = 0;
    c.tipClicked.subscribe(() => tips++);
    const tip = el.querySelector('.board-hint-tip') as HTMLButtonElement;
    expect(tip.textContent).toContain('(2/3)');
    tip.click();
    expect(tips).toBe(1);
    expect((el.querySelector('.board-hint-flag') as HTMLButtonElement).disabled).toBeTrue();
  });
});
