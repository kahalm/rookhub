import { of } from 'rxjs';
import { LocalCalculationBackend } from './calc-local.backend';
import { CalcPublicBook, CalculationService } from './calculation.service';
import { clearCalcLocal, writeCalcLocalReview, writeCalcLocalTree } from './calc-local.util';

const BOOK = 4343;
const FEN = 'r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4';

function publicBook(): CalcPublicBook {
  return {
    bookId: BOOK, displayName: 'Öffentlicher Kalkulationskurs', isCalculation: true,
    positions: [
      { id: 11, round: '1', title: 'Eins', chapter: 'KW46', fen: FEN, setupMoves: 'g1f3', comment: 'Rechne!' },
      { id: 12, round: '2', title: null, chapter: 'KW47', fen: FEN, setupMoves: '', comment: null },
    ],
  };
}

function backend(book = publicBook()) {
  const calls: number[] = [];
  const api = { getPublicBook: (id: number) => { calls.push(id); return of(book); } } as unknown as CalculationService;
  return { backend: new LocalCalculationBackend(api, BOOK), calls };
}

describe('LocalCalculationBackend', () => {
  beforeEach(() => clearCalcLocal(BOOK));
  afterEach(() => clearCalcLocal(BOOK));

  it('holt die Stellungen NUR vom öffentlichen Endpoint', () => {
    const { backend: b, calls } = backend();
    let got: unknown;
    b.getBook(BOOK).subscribe(x => (got = x));
    expect(calls).toEqual([BOOK]);
    expect((got as { positions: unknown[] }).positions.length).toBe(2);
  });

  it('macht daraus die Sprungliste — ohne Baum ist nichts bearbeitet', () => {
    const { backend: b } = backend();
    b.getBook(BOOK).subscribe(book => {
      expect(book.positions.map(p => p.id)).toEqual([11, 12]);
      expect(book.positions.every(p => !p.hasTree)).toBeTrue();
      expect(book.positions[0].chapter).toBe('KW46');
      // Summen kommen bewusst NICHT mit: die rechnet die Ansicht selbst (kein Server, der sie führt).
      expect(book.chapters).toBeUndefined();
      expect(book.points).toBeUndefined();
    });
  });

  it('spiegelt den lokalen Stand in die Sprungliste', () => {
    writeCalcLocalTree(BOOK, 11, '{"a":1}');
    writeCalcLocalReview(BOOK, 11, { chosenSan: 'Sf3', chosenUci: 'g1f3', grade: 2, secondsDelta: 45 });
    const { backend: b } = backend();
    b.getBook(BOOK).subscribe(book => {
      expect(book.positions[0]).toEqual(jasmine.objectContaining({
        id: 11, hasTree: true, chosenSan: 'Sf3', chosenUci: 'g1f3', secondsSpent: 45, grade: 2,
      }));
      expect(book.positions[1].hasTree).toBeFalse();
    });
  });

  it('liefert die Stellung samt Vorlauf, aber ohne jede Lösung', () => {
    const { backend: b } = backend();
    b.getBook(BOOK).subscribe();
    b.getPosition(11).subscribe(pos => {
      expect(pos.fen).toBe(FEN);
      expect(pos.setupMoves).toBe('g1f3');
      expect(pos.comment).toBe('Rechne!');
      // Es gibt kein Lösungs-Feld — der Modus kennt die Lösung nicht, auch nicht anonym.
      expect((pos as unknown as Record<string, unknown>)['moves']).toBeUndefined();
    });
  });

  it('reicht den lokal gespeicherten Baum an die Stellung durch', () => {
    const at = writeCalcLocalTree(BOOK, 12, '{"b":2}');
    const { backend: b } = backend();
    b.getBook(BOOK).subscribe();
    b.getPosition(12).subscribe(pos => {
      expect(pos.treeJson).toBe('{"b":2}');
      expect(pos.treeUpdatedAt).toBe(at!);
    });
  });

  it('meldet eine Stellung, die nicht im öffentlichen Buch steht, als Fehler', () => {
    const { backend: b } = backend();
    b.getBook(BOOK).subscribe();
    let failed = false;
    b.getPosition(999).subscribe({ error: () => (failed = true) });
    expect(failed).toBeTrue();
  });

  it('schreibt Baum und Bewertung in den localStorage', () => {
    const { backend: b } = backend();
    b.getBook(BOOK).subscribe();
    let savedAt = '';
    b.saveTree(11, '{"a":1}').subscribe(res => (savedAt = res.updatedAt));
    b.saveReview(11, { grade: 4, secondsDelta: 10 }).subscribe(res => {
      expect(res).toEqual({ bookPuzzleId: 11, chosenSan: null, chosenUci: null, secondsSpent: 10, grade: 4 });
    });
    expect(savedAt).toBeTruthy();

    b.getPosition(11).subscribe(pos => {
      expect(pos.treeJson).toBe('{"a":1}');
      expect(pos.grade).toBe(4);
      expect(pos.secondsSpent).toBe(10);
    });
  });

  it('meldet einen Fehler, wenn der Speicher nicht mitspielt (statt „gespeichert" zu behaupten)', () => {
    const { backend: b } = backend();
    b.getBook(BOOK).subscribe();
    spyOn(Storage.prototype, 'setItem').and.throwError('QuotaExceededError');
    let failed = false;
    b.saveTree(11, '{"a":1}').subscribe({ error: () => (failed = true) });
    expect(failed).toBeTrue();
  });

  it('meldet auch bei Festlegung/Zeit/Bewertung den Fehler, wenn nichts geschrieben wurde', () => {
    // Derselbe Vertrag wie beim Baum: die Komponente darf einen Fehlschlag nicht als Erfolg sehen,
    // sonst stehen Wahl, Zeit und Bewertung als gespeichert da und sind nach dem Neuladen weg.
    const { backend: b } = backend();
    b.getBook(BOOK).subscribe();
    spyOn(Storage.prototype, 'setItem').and.throwError('QuotaExceededError');
    let failed = false;
    let succeeded = false;
    b.saveReview(11, { grade: 4, secondsDelta: 30 })
      .subscribe({ next: () => (succeeded = true), error: () => (failed = true) });
    expect(failed).toBeTrue();
    expect(succeeded).toBeFalse();
  });

  it('verwirft den Baum, lässt die Trainings-Werte aber stehen', () => {
    const { backend: b } = backend();
    b.getBook(BOOK).subscribe();
    b.saveTree(11, '{"a":1}').subscribe();
    b.saveReview(11, { grade: 1 }).subscribe();
    b.deleteTree(11).subscribe();
    b.getPosition(11).subscribe(pos => {
      expect(pos.treeJson).toBeNull();
      expect(pos.grade).toBe(1);
    });
  });
});
