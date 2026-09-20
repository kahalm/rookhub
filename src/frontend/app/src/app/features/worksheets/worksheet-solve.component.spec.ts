import { Chess } from 'chess.js';
import { SharedWorksheetItem } from './worksheet.service';
import { WorksheetTask } from './worksheet-solve.component';

/** Aufgabe: Weiß zieht 1.e4, Schwarz antwortet 1…e5, dann 2.Sf3 als zweiter Lösungszug. */
const item = (over: Partial<SharedWorksheetItem> = {}): SharedWorksheetItem => ({
  fen: new Chess().fen(),
  orientation: 'white',
  heading: '',
  text: '',
  solutionMoves: 'e2e4 e7e5 g1f3',
  ...over,
});

describe('WorksheetTask', () => {
  it('der richtige Zug zählt und die Gegnerantwort kommt sofort mit', () => {
    const task = new WorksheetTask(item());

    expect(task.play('e2', 'e4')).toBeTrue();

    const expected = new Chess();
    expected.move('e4'); expected.move('e5');
    expect(task.fen).toBe(expected.fen());   // Antwort 1…e5 steht schon auf dem Brett
    expect(task.state).toBe('solving');      // 2.Sf3 fehlt noch
  });

  it('der letzte Lösungszug schließt die Aufgabe ab', () => {
    const task = new WorksheetTask(item());
    task.play('e2', 'e4');

    expect(task.play('g1', 'f3')).toBeTrue();

    expect(task.state).toBe('solved');
    expect(task.finished).toBeTrue();
    expect(task.dests.size).toBe(0);   // fertig = kein Weiterziehen
  });

  it('ein falscher Zug lässt die Stellung stehen, die Aufgabe bleibt offen', () => {
    const task = new WorksheetTask(item());
    const before = task.fen;

    expect(task.play('d2', 'd4')).toBeFalse();

    expect(task.state).toBe('wrong');
    expect(task.fen).toBe(before);   // es wurde nie gezogen, nichts zurückzunehmen
    expect(task.finished).toBeFalse();
  });

  it('Umwandlung: Start und Ziel entscheiden, die Figur nimmt die Lösung', () => {
    const task = new WorksheetTask(item({
      fen: '8/4P3/8/8/8/8/8/4K1k1 w - - 0 1',
      solutionMoves: 'e7e8q',
    }));

    expect(task.play('e7', 'e8', 'n')).toBeTrue();   // Dialog-Wahl weicht ab …

    expect(task.fen.startsWith('4Q3/')).toBeTrue();  // … die Lösung setzt die Dame
    expect(task.state).toBe('solved');
  });

  it('Lösung zeigen spielt den Rest vor', () => {
    const task = new WorksheetTask(item());

    task.giveUp();

    const expected = new Chess();
    expected.move('e4'); expected.move('e5'); expected.move('Nf3');
    expect(task.fen).toBe(expected.fen());
    expect(task.state).toBe('given-up');
  });

  it('ohne Lösung ist die Aufgabe ein Rechenbrett: jeder legale Zug geht, nichts wird bewertet', () => {
    const task = new WorksheetTask(item({ solutionMoves: '' }));

    expect(task.state).toBe('free');
    expect(task.play('d2', 'd4')).toBeTrue();
    expect(task.state).toBe('free');
    expect(task.fen).not.toBe(new Chess().fen());
  });

  it('zurücksetzen holt die Ausgangsstellung zurück', () => {
    const task = new WorksheetTask(item());
    const start = task.fen;
    task.play('e2', 'e4');

    task.reset();

    expect(task.fen).toBe(start);
    expect(task.index).toBe(0);
    expect(task.state).toBe('solving');
  });

  it('eine kaputte FEN macht die Aufgabe nicht kaputt', () => {
    const task = new WorksheetTask(item({ fen: 'unsinn', solutionMoves: '' }));
    expect(task.fen).toBe(new Chess().fen());   // Ersatzbrett statt Absturz
  });
});
