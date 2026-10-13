import { TestBed } from '@angular/core/testing';
import { FlashcardBoardComponent } from './flashcard-board.component';

/**
 * UI-Sweep a1 (a1-rk-flashcard-koordinaten-dunkel): die Linien-/Reihenbezeichner liegen neben dem Brett auf der Karte
 * und nehmen die Textfarbe der Umgebung — vorher fest #3a352c, im dunklen Design praktisch unsichtbar.
 */
describe('FlashcardBoardComponent Koordinaten', () => {
  function render(coordinates: boolean): HTMLElement {
    const fixture = TestBed.createComponent(FlashcardBoardComponent);
    fixture.componentRef.setInput('fen', '8/8/8/8/8/8/8/8 w - - 0 1');
    fixture.componentRef.setInput('coordinates', coordinates);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('16 Bezeichner in currentColor mit 70 % Deckkraft, kein fester Farbwert', () => {
    const texts = Array.from(render(true).querySelectorAll('text'));
    expect(texts.length).toBe(16);
    for (const t of texts) {
      expect(t.getAttribute('fill')).toBe('currentColor');
      expect(t.getAttribute('opacity')).toBe('0.7');
    }
  });

  it('folgt der Textfarbe der Umgebung (hell im dunklen Design)', () => {
    const el = render(true);
    el.style.color = 'rgb(227, 226, 230)';
    document.body.appendChild(el);
    try {
      expect(getComputedStyle(el.querySelector('text')!).fill).toBe('rgb(227, 226, 230)');
    } finally {
      el.remove();
    }
  });

  it('ohne coordinates keine Bezeichner', () => {
    expect(render(false).querySelectorAll('text').length).toBe(0);
  });
});
