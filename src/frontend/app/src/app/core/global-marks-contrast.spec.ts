import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { BookBoardHintComponent } from '../features/puzzles/book-board-hint.component';
import { EndlessPuzzleComponent } from '../features/puzzles/endless-puzzle.component';
import { PuzzleComponent } from '../features/puzzles/puzzle.component';
import { contrast, effectiveBackground, mountWithComponentStyles, parseColor, setDarkTheme } from '../testing/contrast';

/**
 * Globale Regeln aus styles.scss (UI-Sweep a1), gemessen an berechneten Farben in hell UND dunkel.
 */
describe('styles.scss: Fortschrittsspur und „am Zug“-Punkt (UI-Sweep a1)', () => {
  let wasDark: boolean;
  let dispose: (() => void) | undefined;
  beforeEach(() => { wasDark = document.documentElement.classList.contains('dark-theme'); });
  afterEach(() => { dispose?.(); dispose = undefined; setDarkTheme(wasDark); });

  @Component({
    standalone: true,
    imports: [MatProgressBarModule],
    template: `<div style="background: var(--mat-sys-surface-container-low); padding: 8px">
      <mat-progress-bar mode="determinate" [value]="0" /></div>`,
  })
  class ProgressHost {}

  // a1-rk-fortschritt-spur-hell: die leere Spur lag im hellen Design bei 1,17:1 auf der Karte — bei 0 % unsichtbar.
  for (const dark of [false, true]) {
    it(`leere Fortschrittsspur hebt sich von der Karte ab (${dark ? 'dunkel' : 'hell'})`, () => {
      setDarkTheme(dark);
      const fixture = TestBed.configureTestingModule({ imports: [ProgressHost] }).createComponent(ProgressHost);
      document.body.appendChild(fixture.nativeElement);
      fixture.detectChanges();
      dispose = () => { fixture.nativeElement.remove(); TestBed.resetTestingModule(); };
      const bar = (fixture.nativeElement as HTMLElement).querySelector('mat-progress-bar')!;
      const track = bar.querySelector('.mdc-linear-progress__buffer-bar')!;
      const card = effectiveBackground(bar);
      const ratio = contrast(parseColor(getComputedStyle(track).backgroundColor), card);
      expect(ratio).withContext(`Spur ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(1.4);
    });
  }

  // a1-rk-am-zug-punkt-dunkel: #2b2b2b mit schwachem Rand ≈ 1,4:1 im dunklen Design. Jetzt schwarz mit Ring aus der
  // Textfarbe — die globale Regel muss die gleichnamigen gekapselten Regeln der drei Puzzle-Seiten schlagen.
  const DOT = '<span class="board-hint-tomove"><span class="tomove-dot black"></span>Schwarz am Zug</span>';
  // Buch-Löser: der Punkt sitzt seit Z18 in der Kindkomponente BookBoardHintComponent.
  for (const cmp of [PuzzleComponent, BookBoardHintComponent, EndlessPuzzleComponent]) {
    for (const dark of [false, true]) {
      it(`${(cmp as { name: string }).name}: schwarzer Punkt mit sichtbarem Ring (${dark ? 'dunkel' : 'hell'})`, () => {
        setDarkTheme(dark);
        dispose = mountWithComponentStyles(cmp, DOT, 'var(--mat-sys-surface)');
        const dot = document.querySelector('.tomove-dot.black')!;
        const style = getComputedStyle(dot);
        expect(parseColor(style.backgroundColor).slice(0, 3)).toEqual([0, 0, 0]);
        expect(style.borderTopWidth).toBe('2px'); // 1,5 px rundet Chrome bei DPR 1 auf 1 px ab
        const ground = effectiveBackground(dot.parentElement!);
        // Dunkel trägt der Ring den Punkt (≥ 3:1 gegen die Seite), hell der schwarze Punkt selbst.
        const ring = contrast(parseColor(style.borderTopColor), ground);
        const fill = contrast([0, 0, 0, 1], ground);
        expect(Math.max(ring, fill)).withContext(`Ring ${ring.toFixed(2)}:1, Punkt ${fill.toFixed(2)}:1`).toBeGreaterThanOrEqual(3);
        if (dark) expect(ring).withContext(`Ring ${ring.toFixed(2)}:1`).toBeGreaterThanOrEqual(3);
      });
    }
  }
});
