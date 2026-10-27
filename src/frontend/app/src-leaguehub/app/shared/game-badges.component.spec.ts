import { ComponentFixture, TestBed } from '@angular/core/testing';
import { GameBadgesComponent } from './game-badges.component';

describe('GameBadgesComponent', () => {
  let fixture: ComponentFixture<GameBadgesComponent>;

  function render(marks: number, brilliant: number): HTMLElement {
    fixture = TestBed.createComponent(GameBadgesComponent);
    fixture.componentRef.setInput('marks', marks);
    fixture.componentRef.setInput('brilliant', brilliant);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  // 0.852.0, Wunsch 2026-10-27: „finde es schaut noch nicht gut aus" → Abzeichen „+" (Gold) und „!!" (Türkis), Zahl erst ab 2,
  // Klick springt beim Nachspielen hin
  it('„+" und „!!" als Knöpfe, Zahl erst ab 2, ohne Wert kein Abzeichen', () => {
    let el = render(1, 3);
    expect(Array.from(el.querySelectorAll('button')).map(b => b.textContent?.trim())).toEqual(['+', '!!3']);
    expect(el.querySelector('.gb-mark')?.getAttribute('title')).toBe('1 interessante Stellung — anspringen');
    expect(el.querySelector('.gb-brilliant')?.getAttribute('aria-label')).toBe('3 brillante Züge, anspringen');
    el = render(0, 0);
    expect(el.querySelectorAll('button').length).toBe(0);
  });

  it('Klick meldet, welches Abzeichen', () => {
    const el = render(2, 1);
    const picked: string[] = [];
    fixture.componentInstance.pick.subscribe(k => picked.push(k));
    (el.querySelector('.gb-brilliant') as HTMLButtonElement).click();
    (el.querySelector('.gb-mark') as HTMLButtonElement).click();
    expect(picked).toEqual(['brilliant', 'mark']);
  });

  it('Schrift auf beiden Flächen mit mindestens 4,5:1 Kontrast', () => {
    const lum = (hex: string) => {
      const c = [1, 3, 5].map(i => parseInt(hex.slice(i, i + 2), 16) / 255).map(v => v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4);
      return 0.2126 * c[0] + 0.7152 * c[1] + 0.0722 * c[2];
    };
    const ratio = (a: string, b: string) => { const [x, y] = [lum(a), lum(b)].sort((p, q) => q - p); return (x + 0.05) / (y + 0.05); };
    const c = render(1, 1) && fixture.componentInstance;
    expect(ratio(c.markColor, c.markText)).toBeGreaterThanOrEqual(4.5);
    expect(ratio(c.brilliantColor, c.brilliantText)).toBeGreaterThanOrEqual(4.5);
  });
});
