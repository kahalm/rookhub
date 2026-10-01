import { BookPuzzleComponent } from './book-puzzle.component';
import { EndlessPuzzleComponent } from './endless-puzzle.component';
import { PuzzleComponent } from './puzzle.component';
import { PuzzleTagsComponent } from './puzzle-tags.component';
import { mountWithComponentStyles, parseColor, setDarkTheme, textContrast } from '../../testing/contrast';

const CARD = 'var(--mat-sys-surface-container-low)';

/**
 * Kontrast auf den Puzzle-Seiten (Codereview 2026-09-29, UX-008): feste Hellthema-Farben und Deckkraft-Mischungen
 * statt Theme-Tokens. „Tags anzeigen" und „Auto: …" trugen #1976d2 (im Standard-Dunkelmodus ≈ 3,7:1), der
 * Buchname war 45 % der Textfarbe. Der Tipp-Knopf erbte die 60-%-Farbe der Hinweiszeile — gegen die echte Flaeche
 * gerechnet reichte das knapp (≈ 5,5/6,5:1; axe meldete ihn nur wegen der fehlenden Seitenflaeche, UX-055), aber
 * eine Kernbedienung in der gedimmten Farbe sieht aus wie deaktiviert.
 */
describe('Puzzle-Seiten: Kontrast von Tipp-Knopf, „Tags anzeigen", „Auto: …" und Buchname (UX-008)', () => {
  let wasDark: boolean;
  let dispose: (() => void) | undefined;
  beforeEach(() => { wasDark = document.documentElement.classList.contains('dark-theme'); });
  afterEach(() => { dispose?.(); dispose = undefined; setDarkTheme(wasDark); });

  const TIP = '<div class="board-hint"><button class="board-hint-tip"><span>Tipp (0/3)</span></button></div>';
  const cases: { name: string; cmp: unknown; html: string; target: string; card?: boolean }[] = [
    { name: 'Standard-Puzzle: Tipp-Knopf', cmp: PuzzleComponent, html: TIP, target: '.board-hint-tip' },
    { name: 'Buch/Tages/Wochenpost: Tipp-Knopf', cmp: BookPuzzleComponent, html: TIP, target: '.board-hint-tip' },
    { name: 'Endless: Tipp-Knopf', cmp: EndlessPuzzleComponent, html: TIP, target: '.board-hint-tip' },
    { name: 'Buch: Buchname', cmp: BookPuzzleComponent, card: true, target: '.book-name',
      html: '<div class="ctx-text"><p class="meta-chapter">Kapitel 1</p><span class="book-name">Buch</span></div>' },
    { name: 'Endless: „Auto: …"', cmp: EndlessPuzzleComponent, card: true, target: '.auto-hint',
      html: '<button type="button" class="auto-hint">Auto: 1000</button>' },
    { name: '„Tags anzeigen"', cmp: PuzzleTagsComponent, card: true, target: '.puzzle-tags-toggle',
      html: '<span class="puzzle-tags-toggle" role="button" tabindex="0">Show tags</span>' },
  ];

  for (const cmp of [PuzzleComponent, BookPuzzleComponent, EndlessPuzzleComponent]) {
    it(`${(cmp as { name: string }).name}: der Tipp-Knopf erbt nicht die gedimmte Farbe der Hinweiszeile`, () => {
      dispose = mountWithComponentStyles(cmp, TIP);
      expect(parseColor(getComputedStyle(document.querySelector('.board-hint-tip')!).color)[3]).toBe(1);
    });
  }

  for (const dark of [false, true]) {
    for (const c of cases) {
      it(`${c.name} erreicht 4,5:1 (${dark ? 'dunkel' : 'hell'})`, () => {
        setDarkTheme(dark);
        dispose = mountWithComponentStyles(c.cmp, c.html, c.card ? CARD : undefined);
        const ratio = textContrast(document.querySelector(c.target)!);
        expect(ratio).withContext(`${c.target}: ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(4.5);
      });
    }
  }
});
