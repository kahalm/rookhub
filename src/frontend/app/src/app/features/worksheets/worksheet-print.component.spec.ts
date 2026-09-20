import { PrintTask, solutionLines, toSheets } from './worksheet-print.component';

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
