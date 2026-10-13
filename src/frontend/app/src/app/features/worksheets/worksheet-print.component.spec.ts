import { PrintTask, WorksheetPrintComponent, solutionLines, toSheets } from './worksheet-print.component';

const task = (no: number): PrintTask =>
  ({ no, fen: '8/8/8/8/8/8/8/8 w - - 0 1', orientation: 'white', whiteToMove: true, heading: '', text: '' });

describe('worksheet-print', () => {
  describe('toSheets', () => {
    it('füllt Seiten der gewählten Dichte, die letzte bleibt angebrochen', () => {
      const sheets = toSheets([1, 2, 3, 4, 5, 6, 7].map(task), 6);
      expect(sheets.length).toBe(2);
      expect(sheets[0].length).toBe(6);
      expect(sheets[1].map(t => t.no)).toEqual([7]);
    });

    it('zwei je Seite ergibt drei Seiten aus fünf Aufgaben', () => {
      expect(toSheets([1, 2, 3, 4, 5].map(task), 2).map(s => s.length)).toEqual([2, 2, 1]);
    });

    it('keine Aufgaben = keine Seite', () => {
      expect(toSheets([], 4)).toEqual([]);
    });

    it('unsinnige Dichte fällt auf sechs zurück statt endlos zu teilen', () => {
      expect(toSheets([1, 2].map(task), 0).length).toBe(1);
    });
  });

  describe('solutionLines', () => {
    it('je weniger Diagramme je Seite, desto mehr Platz für die Lösung', () => {
      expect(solutionLines(6).length).toBe(2);
      expect(solutionLines(4).length).toBe(3);
      expect(solutionLines(2).length).toBe(5);
    });
  });
});

/** Kompilierte Komponenten-Styles, Leerraum vereinheitlicht. */
function compiledCss(cmp: unknown): string {
  return ((cmp as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/%NS%/g, '').replace(/\s+/g, ' ');
}

/** UI-Sweep a1 (a1-rk-ws-druck-titel-umbruch): Überschrift bricht nicht mitten im Wort, Zugangabe am Handy darunter. */
describe('worksheet-print Diagrammkopf', () => {
  const css = compiledCss(WorksheetPrintComponent);
  it('Überschrift mit break-word statt anywhere', () => {
    expect(css).toMatch(/\.ws-heading[^{]*\{[^}]*overflow-wrap: break-word/);
    expect(css).not.toMatch(/\.ws-heading[^{]*\{[^}]*overflow-wrap: anywhere/);
  });
  it('≤ 700 px: Kopf bricht um, Zugangabe als eigene Zeile, Raster oben ausgerichtet', () => {
    const phone = css.slice(css.indexOf('@media (max-width: 700px)'), css.indexOf('@media print'));
    expect(phone).toMatch(/\.ws-task-head[^{]*\{ ?flex-wrap: wrap/);
    expect(phone).toMatch(/\.ws-turn[^{]*\{ ?flex-basis: 100%; text-align: center/);
    expect(phone).toMatch(/\.ws-grid[^{]*\{ ?align-items: start/);
  });
});
