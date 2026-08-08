import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Subject, of, throwError } from 'rxjs';
import { CalculationComponent } from './calculation.component';
import { CalcBook, CalcPosition, CalcPositionListItem, CalcReviewSaved } from './calculation.service';
import { CalcReviewPatch } from './calc-review.util';
import { findNode, lines } from './calc-tree.util';
import { VisibilityStopwatch } from '../../puzzles/visibility-stopwatch';

const START = 'r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4';

function position(overrides: Partial<CalcPosition> = {}): CalcPosition {
  return {
    id: 7, bookId: 1, round: '3', title: 'Aufgabe 3', chapter: null,
    fen: START, setupMoves: '', comment: 'Weiß am Zug — was rechnest du?',
    treeJson: null, treeUpdatedAt: null,
    ...overrides,
  };
}

/**
 * Server-Attrappe für den Review-Endpoint: übernimmt nur die GESETZTEN Felder und ADDIERT die
 * Zeit — genau das Verhalten, gegen das die Komponente gebaut ist.
 */
function fakeReviewServer() {
  const state = new Map<number, CalcReviewSaved>();
  return (id: number, patch: CalcReviewPatch): CalcReviewSaved => {
    const cur = state.get(id)
      ?? { bookPuzzleId: id, chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null };
    const next: CalcReviewSaved = {
      bookPuzzleId: id,
      chosenSan: 'chosenSan' in patch ? patch.chosenSan ?? null : cur.chosenSan,
      chosenUci: 'chosenUci' in patch ? patch.chosenUci ?? null : cur.chosenUci,
      secondsSpent: cur.secondsSpent + (patch.secondsDelta ?? 0),
      grade: 'grade' in patch ? patch.grade ?? null : cur.grade,
    };
    state.set(id, next);
    return next;
  };
}

/** Komponente ohne Template, mit Stub-Abhängigkeiten — für die reine Bedienlogik. */
function make(api: Partial<Record<'getBook' | 'getPosition' | 'saveTree' | 'deleteTree' | 'saveReview', unknown>> = {}) {
  const saved: { id: number; json: string }[] = [];
  const deleted: number[] = [];
  const warnings: string[] = [];
  const reviews: { id: number; patch: CalcReviewPatch }[] = [];
  const reviewServer = fakeReviewServer();
  const apiStub = {
    getBook: () => of({ bookId: 1, displayName: 'B', isCalculation: true, positions: [] }),
    getPosition: () => of(position()),
    saveTree: (id: number, json: string) => { saved.push({ id, json }); return of({ bookPuzzleId: id, updatedAt: '2026-07-28T10:00:00Z' }); },
    deleteTree: (id: number) => { deleted.push(id); return of(undefined); },
    saveReview: (id: number, patch: CalcReviewPatch) => { reviews.push({ id, patch }); return of(reviewServer(id, patch)); },
    ...api,
  };
  const component = new CalculationComponent(
    { snapshot: { paramMap: { get: () => '1' }, queryParamMap: { get: () => null } } } as never,
    { navigate: () => Promise.resolve(true) } as never,
    apiStub as never,
    { boardTheme: 'brown', pieceSet: 'cburnett' } as never,
    { warn: (m: string) => { warnings.push(m); } } as never,
    { instant: (k: string) => k } as never,
  );
  return { component, saved, deleted, warnings, reviews };
}

function listItem(pos: CalcPosition): CalcPositionListItem {
  return {
    id: pos.id, round: pos.round, title: pos.title, chapter: pos.chapter, hasTree: !!pos.treeJson,
    chosenSan: pos.chosenSan ?? null, chosenUci: pos.chosenUci ?? null,
    secondsSpent: pos.secondsSpent ?? 0, grade: pos.grade ?? null,
  };
}

/** Stellung laden, ohne den HTTP-Pfad zu bemühen (applyPosition ist der Kern davon). */
function load(component: CalculationComponent, pos: CalcPosition = position()): void {
  component.position = pos;
  // Die Sprunglisten-Zeile ist die Quelle für Wahl/Zeit/Stufe — sie muss VOR applyPosition stehen.
  component.positions = [listItem(pos)];
  component.index = 0;
  (component as unknown as { applyPosition: (p: CalcPosition) => void }).applyPosition(pos);
}

function item(id: number, overrides: Partial<CalcPositionListItem> = {}): CalcPositionListItem {
  return {
    id, round: String(id), title: null, chapter: null, hasTree: false,
    chosenSan: null, chosenUci: null, secondsSpent: 0, grade: null, ...overrides,
  };
}

/** Ganzes Buch über den echten Ladeweg holen (Sprungliste + Gruppen + Summen in einem Rutsch). */
function makeWithBook(book: Partial<CalcBook> & { positions: CalcPositionListItem[] }) {
  const full: CalcBook = { bookId: 1, displayName: 'B', isCalculation: true, ...book };
  const made = make({
    getBook: () => of(full),
    getPosition: (id: number) => {
      const row = full.positions.find(p => p.id === id) ?? full.positions[0];
      return of(position({ id: row.id, round: row.round, title: row.title, chapter: row.chapter }));
    },
  });
  (made.component as unknown as { loadBook(requested: number | null): void }).loadBook(null);
  return made;
}

describe('CalculationComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [CalculationComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CalculationComponent);
    expect(fixture.componentInstance).toBeTruthy();
  });
});

describe('CalculationComponent Zug-Eingabe (Brett eingefroren)', () => {
  it('records a clicked move without ever moving the board', () => {
    const { component } = make();
    load(component);

    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });

    // Das Brett zeigt weiter die Ausgangsstellung — nur der Baum wächst.
    expect(component.startFen).toBe(START);
    expect(component.lineCount).toBe(1);
    expect(findNode(component.tree, component.cursorId)!.san).toBe('Nxe5');
    // Die Cursor-FEN (nur intern, für die Legalitätsprüfung) ist weitergezogen.
    expect(component.cursorFen).not.toBe(START);
  });

  it('records moves for BOTH sides in one line', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });   // Weiß
    component.onMove({ orig: 'c6' as never, dest: 'e5' as never });   // Schwarz
    expect(lines(component.tree)[0].moves.map(m => m.san)).toEqual(['Nxe5', 'Nxe5']);
  });

  it('ignores an illegal click', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'a1' as never, dest: 'a8' as never });
    expect(component.lineCount).toBe(0);
    expect(component.atStart).toBeTrue();
  });

  it('blocks input on an illegal diagram position', () => {
    const { component } = make();
    load(component, position({ fen: '8/8/8/3p4/8/8/8/8 w - - 0 1' }));   // ohne Könige → chess.js lehnt ab
    expect(component.illegalPosition).toBeTrue();
    component.onMove({ orig: 'd5' as never, dest: 'd4' as never });
    expect(component.lineCount).toBe(0);
  });

  it('orients the board toward the side to move', () => {
    const { component } = make();
    load(component);
    expect(component.orientation).toBe('white');
    load(component, position({ fen: '8/8/8/4k3/8/8/4K3/8 b - - 0 7' }));
    expect(component.orientation).toBe('black');
    component.flipBoard();
    expect(component.orientation).toBe('white');
  });

  it('replays the (non-solution) setup moves into the start position', () => {
    const { component } = make();
    load(component, position({
      fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
      setupMoves: 'e2e4 e7e5',
    }));
    expect(component.startFen).toContain('4p3');       // schwarzer Bauer steht auf e5
    expect(component.orientation).toBe('white');
    expect(component.illegalPosition).toBeFalse();
  });
});

describe('CalculationComponent Linien & Abzweigungen', () => {
  it('(+) starts a new line from the start position', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    expect(component.atStart).toBeFalse();

    component.startNewLine();
    expect(component.atStart).toBeTrue();

    component.onMove({ orig: 'd2' as never, dest: 'd4' as never });
    expect(component.lineCount).toBe(2);
  });

  it('branches off in the middle of a line', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    const branchPoint = component.cursorId;
    component.onMove({ orig: 'c6' as never, dest: 'e5' as never });
    expect(component.lineCount).toBe(1);

    component.setCursor(branchPoint);              // mitten in die Linie zurück
    component.onMove({ orig: 'd7' as never, dest: 'd6' as never });

    const all = lines(component.tree);
    expect(all.length).toBe(2);
    expect(all[1].sharedPrefix).toBe(1);           // 1. Nxe5 wird geteilt
    expect(all[1].moves.map(m => m.san)).toEqual(['Nxe5', 'd6']);
  });

  it('does not duplicate a line when the same move is replayed', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    component.startNewLine();
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });   // derselbe Zug erneut
    expect(component.lineCount).toBe(1);
  });

  it('navigates within the line and switches lines', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    const first = component.cursorId;
    component.onMove({ orig: 'c6' as never, dest: 'e5' as never });
    const second = component.cursorId;

    component.goBack();
    expect(component.cursorId).toBe(first);
    component.goForward();
    expect(component.cursorId).toBe(second);

    component.startNewLine();
    component.onMove({ orig: 'd2' as never, dest: 'd4' as never });
    const otherLineLeaf = component.cursorId;

    component.switchLine(-1);
    expect(component.cursorId).toBe(second);       // zurück auf die erste Linie
    component.switchLine(1);
    expect(component.cursorId).toBe(otherLineLeaf);
  });

  it('goBack at the start position does nothing', () => {
    const { component } = make();
    load(component);
    component.goBack();
    expect(component.atStart).toBeTrue();
  });

  it('take-back removes the selected move and its continuation', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    const first = component.cursorId;
    component.onMove({ orig: 'c6' as never, dest: 'e5' as never });

    component.deleteFromCursor();
    expect(component.cursorId).toBe(first);
    expect(lines(component.tree)[0].moves.length).toBe(1);

    component.deleteFromCursor();
    expect(component.lineCount).toBe(0);
    expect(component.atStart).toBeTrue();
  });

  it('deleting a line keeps the shared moves of the other line', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    const branchPoint = component.cursorId;
    component.onMove({ orig: 'c6' as never, dest: 'e5' as never });
    component.setCursor(branchPoint);
    component.onMove({ orig: 'd7' as never, dest: 'd6' as never });

    component.deleteLine(lines(component.tree)[1].leafId);

    const all = lines(component.tree);
    expect(all.length).toBe(1);
    expect(all[0].moves.map(m => m.san)).toEqual(['Nxe5', 'Nxe5']);
  });
});

describe('CalculationComponent Bewertungen & Kommentare', () => {
  it('applies and toggles glyph and evaluation on the selected move', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });

    component.applyGlyph('??');
    component.applyEval('⩲');
    expect(component.cursorNode!.glyph).toBe('??');
    expect(component.cursorNode!.evaluation).toBe('⩲');

    component.applyGlyph('??');                      // gleiches Symbol → aus
    expect(component.cursorNode!.glyph).toBeUndefined();

    component.applyEval('±');
    component.clearAnnotations();
    expect(component.cursorNode!.evaluation).toBeUndefined();
  });

  it('never annotates the start position', () => {
    const { component } = make();
    load(component);
    component.applyGlyph('!!');
    component.applyEval('∞');
    const root = findNode(component.tree, component.tree.rootId)!;
    expect(root.glyph).toBeUndefined();
    expect(root.evaluation).toBeUndefined();
  });

  it('stores the comment of the selected move and of a line', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });

    component.cursorComment = 'gewinnt einen Bauern';
    component.saveCursorComment();
    expect(component.cursorNode!.comment).toBe('gewinnt einen Bauern');

    component.onLineComment({ nodeId: component.cursorId, text: 'doch nur Ausgleich' });
    expect(component.cursorNode!.comment).toBe('doch nur Ausgleich');
    expect(component.cursorComment).toBe('doch nur Ausgleich');
  });
});

describe('CalculationComponent Speichern', () => {
  it('saves the tree and marks the position as worked on', () => {
    const { component, saved } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });

    component.flushSave();

    expect(saved.length).toBe(1);
    expect(saved[0].id).toBe(7);
    expect(JSON.parse(saved[0].json).nodes.length).toBe(2);
    expect(component.positions[0].hasTree).toBeTrue();
    expect(component.savedAt).not.toBeNull();
  });

  it('does not save when nothing changed', () => {
    const { component, saved } = make();
    load(component);
    component.flushSave();
    expect(saved.length).toBe(0);
  });

  it('discards a stored tree when the last move is taken back', () => {
    const { component, saved, deleted } = make();
    load(component, position({
      treeJson: JSON.stringify({
        version: 1, startFen: START, rootId: 0, nextId: 2,
        nodes: [
          { id: 0, parentId: null, san: '', uci: '', fen: START, childIds: [1] },
          { id: 1, parentId: 0, san: 'Nxe5', uci: 'f3e5', fen: 'x', childIds: [] },
        ],
      }),
      treeUpdatedAt: '2026-07-27T10:00:00Z',
    }));
    expect(component.lineCount).toBe(1);            // gespeicherter Baum wurde geladen

    component.setCursor(1);
    component.deleteFromCursor();
    component.flushSave();

    expect(saved.length).toBe(0);
    expect(deleted).toEqual([7]);
    expect(component.positions[0].hasTree).toBeFalse();
    expect(component.savedAt).toBeNull();
  });

  it('keeps the change pending and warns when saving fails', () => {
    const { component, warnings } = make({ saveTree: () => throwError(() => new Error('offline')) });
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });

    component.flushSave();
    expect(warnings).toEqual(['calc.saveFailed']);
    expect(component.saving).toBeFalse();

    // Erneuter Versuch möglich, weil die Änderung als „noch offen" markiert bleibt.
    const { component: c2, saved } = make();
    load(c2);
    c2.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    c2.flushSave();
    expect(saved.length).toBe(1);
  });

  it('rettet den Baum der alten Stellung, wenn der Save beim Stellungswechsel scheitert', () => {
    const saved: { id: number; json: string }[] = [];
    let fail = true;
    const { component } = make({
      saveTree: (id: number, json: string) => {
        if (fail) return throwError(() => new Error('offline'));
        saved.push({ id, json });
        return of({ bookPuzzleId: id, updatedAt: '2026-07-28T10:00:00Z' });
      },
    });
    load(component, position({ id: 7 }));
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    component.flushSave();                       // scheitert → Snapshot bleibt an Stellung 7 hängen
    expect(saved.length).toBe(0);

    fail = false;
    load(component, position({ id: 8 }));        // Wechsel: `tree` ist jetzt der Baum von Stellung 8
    component.flushSave();

    expect(saved.length).toBe(1);
    expect(saved[0].id).toBe(7);                 // der gerettete Baum, nicht die neue Stellung
    expect(JSON.parse(saved[0].json).nodes.length).toBe(2);
  });

  it('schickt einen bewusst geleerten Baum nicht doch noch aus der Outbox hoch', () => {
    // Regression: ein zuvor GESCHEITERTER Save liegt in der Outbox. Nimmt der Nutzer den Zug
    // danach zurück (Baum leer, nie gespeichert), wurde der alte Stand trotzdem gesendet.
    const saved: { id: number; json: string }[] = [];
    let fail = true;
    const { component } = make({
      saveTree: (id: number, json: string) => {
        if (fail) return throwError(() => new Error('offline'));
        saved.push({ id, json });
        return of({ bookPuzzleId: id, updatedAt: '2026-07-28T10:00:00Z' });
      },
    });
    load(component, position({ id: 7 }));
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    component.flushSave();                       // scheitert → Stand liegt in der Outbox
    expect(saved.length).toBe(0);

    fail = false;
    component.setCursor(1);
    component.deleteFromCursor();                // Zug zurücknehmen → Baum ist wieder leer
    component.flushSave();

    expect(saved.length).toBe(0);                // nichts hochgeschrieben
    expect(component.positions[0].hasTree).toBeFalse();
  });

  it('rollt einen erfolgreich gespeicherten Stand nicht auf einen älteren zurück', () => {
    // Regression: Save v1 scheitert SPÄT, während v2 schon in der Outbox liegt — das Requeue
    // von v1 hätte den Server beim nächsten Flush auf den alten Stand zurückgesetzt.
    const first = new Subject<{ bookPuzzleId: number; updatedAt: string }>();
    const saved: { id: number; json: string }[] = [];
    let call = 0;
    const { component } = make({
      saveTree: (id: number, json: string) => {
        call++;
        if (call === 1) return first;            // v1 bleibt offen
        saved.push({ id, json });
        return of({ bookPuzzleId: id, updatedAt: '2026-07-28T10:00:00Z' });
      },
    });
    load(component, position({ id: 7 }));
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    component.flushSave();                       // v1 unterwegs
    component.startNewLine();
    component.onMove({ orig: 'd2' as never, dest: 'd4' as never });   // zweite Variante
    component.flushSave();                       // v2 wartet (v1 noch unterwegs)

    first.error(new Error('offline'));           // v1 scheitert nachträglich
    component.flushSave();                       // schickt v2 — NICHT v1

    expect(saved.length).toBe(1);
    expect(lines(JSON.parse(saved[0].json)).length).toBe(2);   // v2 = zwei Varianten
  });

  it('schickt zwei Stände derselben Stellung nie parallel (Reihenfolge am Server)', () => {
    // Regression (Codereview 2026-08-07): der Server-Upsert ist ein reines „last write wins" ohne
    // Versions-Token. Gingen v1 und v2 derselben Stellung gleichzeitig raus, konnte v1 NACH v2
    // ankommen → der ältere Baum blieb gespeichert. Erwartung: v2 wartet, bis v1 beantwortet ist.
    const first = new Subject<{ bookPuzzleId: number; updatedAt: string }>();
    const sent: string[] = [];
    let call = 0;
    const { component } = make({
      saveTree: (id: number, json: string) => {
        call++; sent.push(json);
        if (call === 1) return first;            // v1 bleibt offen
        return of({ bookPuzzleId: id, updatedAt: '2026-07-28T10:00:00Z' });
      },
    });
    load(component, position({ id: 7 }));
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    component.flushSave();                       // v1 unterwegs
    component.startNewLine();
    component.onMove({ orig: 'd2' as never, dest: 'd4' as never });
    component.flushSave();                       // v2 darf NICHT parallel losgehen

    expect(sent.length).toBe(1);

    first.next({ bookPuzzleId: 7, updatedAt: '2026-07-28T10:00:00Z' });
    first.complete();                            // v1 beantwortet → v2 geht automatisch raus

    expect(sent.length).toBe(2);
    expect(lines(JSON.parse(sent[1])).length).toBe(2);
  });

  it('serialisiert auch Löschen gegen einen danach neu angelegten Baum', () => {
    // Sonst könnte das DELETE nach dem PUT beim Server ankommen und den neuen Baum wegräumen.
    const del = new Subject<void>();
    const saved: { id: number; json: string }[] = [];
    const { component } = make({
      deleteTree: () => del,
      saveTree: (id: number, json: string) => {
        saved.push({ id, json });
        return of({ bookPuzzleId: id, updatedAt: '2026-07-28T10:00:00Z' });
      },
    });
    load(component, position({
      treeJson: JSON.stringify({
        version: 1, startFen: START, rootId: 0, nextId: 2,
        nodes: [
          { id: 0, parentId: null, san: '', uci: '', fen: START, childIds: [1] },
          { id: 1, parentId: 0, san: 'Nxe5', uci: 'f3e5', fen: 'x', childIds: [] },
        ],
      }),
      treeUpdatedAt: '2026-07-27T10:00:00Z',
    }));
    component.setCursor(1);
    component.deleteFromCursor();
    component.flushSave();                       // DELETE unterwegs

    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    component.flushSave();                       // neuer Baum — darf nicht überholen
    expect(saved.length).toBe(0);

    del.next();
    del.complete();                              // DELETE beantwortet → jetzt erst speichern
    expect(saved.length).toBe(1);
  });

  it('schreibt eine späte Antwort der alten Stellung nicht in die neue Ansicht', () => {
    const answer = new Subject<{ bookPuzzleId: number; updatedAt: string }>();
    const { component } = make({ saveTree: () => answer });
    load(component, position({ id: 7 }));
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    component.flushSave();
    expect(component.saving).toBeTrue();

    load(component, position({ id: 8 }));        // Wechsel, während der Save noch unterwegs ist
    expect(component.saving).toBeFalse();
    answer.next({ bookPuzzleId: 7, updatedAt: '2026-07-28T10:00:00Z' });
    answer.complete();

    expect(component.saving).toBeFalse();
    expect(component.savedAt).toBeNull();        // „gespeichert um …" gehörte zu Stellung 7
  });
});

describe('CalculationComponent Laden', () => {
  it('ignoriert die überholte Antwort eines schnell weggeklickten Ladevorgangs', () => {
    const first = new Subject<CalcPosition>();
    const second = new Subject<CalcPosition>();
    const queue = [first, second];
    const { component } = make({ getPosition: () => queue.shift()! });
    component.bookId = 1;
    const loadPosition = (component as unknown as { loadPosition: (id: number) => void }).loadPosition.bind(component);

    loadPosition(7);
    loadPosition(8);                                        // schnell weitergeklickt

    second.next(position({ id: 8, fen: '8/8/8/4k3/8/8/4K3/8 b - - 0 7' }));
    second.complete();
    first.next(position({ id: 7 }));                        // ältere Antwort trifft ZULETZT ein
    first.complete();

    expect(component.position!.id).toBe(8);
    expect(component.startFen).toBe('8/8/8/4k3/8/8/4K3/8 b - - 0 7');
    expect(component.loading).toBeFalse();
  });
});

describe('CalculationComponent Tastatur', () => {
  it('maps the arrow keys to tree navigation', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    const first = component.cursorId;
    component.onMove({ orig: 'c6' as never, dest: 'e5' as never });

    const press = (key: string, target: Partial<HTMLElement> = { tagName: 'DIV' }) => {
      let prevented = false;
      component.onKeydown({ key, target, preventDefault: () => { prevented = true; } } as never);
      return prevented;
    };

    expect(press('ArrowLeft')).toBeTrue();
    expect(component.cursorId).toBe(first);
    expect(press('ArrowRight')).toBeTrue();
    expect(component.cursorId).not.toBe(first);
    expect(press('a')).toBeFalse();                 // unbeteiligte Taste bleibt unangetastet
  });

  it('keeps its hands off keys typed into inputs', () => {
    const { component } = make();
    load(component);
    component.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    const cursor = component.cursorId;
    component.onKeydown({ key: 'ArrowLeft', target: { tagName: 'INPUT' }, preventDefault: () => { /* egal */ } } as never);
    expect(component.cursorId).toBe(cursor);
  });
});

describe('CalculationComponent Symbol-Erklärungen', () => {
  it('erklärt jedes Symbol im Mouseover — Bedeutung plus Bedienhinweis', () => {
    const { component } = make();          // Translate-Stub liefert den Schlüssel zurück
    expect(component.evalTooltip('+−')).toBe('calc.eval.whiteWinning (calc.symbolToggleHint)');
    expect(component.evalTooltip('−+')).toBe('calc.eval.blackWinning (calc.symbolToggleHint)');
    expect(component.glyphTooltip('??')).toBe('calc.glyph.blunder (calc.symbolToggleHint)');
  });

  it('lässt keines der angebotenen Symbole ohne Erklärung', () => {
    const { component } = make();
    for (const g of component.glyphs) expect(component.glyphTooltip(g)).toContain('calc.glyph.');
    for (const e of component.evals) expect(component.evalTooltip(e)).toContain('calc.eval.');
  });
});

describe('CalculationComponent Kapitel-Timer', () => {
  beforeEach(() => {
    localStorage.clear();
    jasmine.clock().install();
  });
  afterEach(() => {
    jasmine.clock().uninstall();
    localStorage.clear();
  });

  function makeWithBook() {
    const { component } = make();
    component.bookId = 1;             // sonst hieße der Speicher-Schlüssel „…_undefined"
    return component;
  }

  it('zählt nur, solange er läuft, und persistiert je Sekunde', () => {
    const c = makeWithBook();
    load(c, position({ chapter: 'A' }));

    c.startTimer();
    jasmine.clock().tick(3000);
    expect(c.timerSeconds).toBe(3);
    expect(c.timerRunning).toBeTrue();

    c.pauseTimer();
    jasmine.clock().tick(5000);
    expect(c.timerSeconds).toBe(3);   // pausiert = eingefroren
    expect(JSON.parse(localStorage.getItem('rookhub_calc_timer_1')!)).toEqual({ A: 3 });
  });

  it('kumuliert innerhalb des Kapitels über Stellungswechsel hinweg', () => {
    const c = makeWithBook();
    load(c, position({ id: 7, chapter: 'A' }));
    c.startTimer();
    jasmine.clock().tick(4000);

    load(c, position({ id: 8, chapter: 'A' }));   // nächste Stellung, GLEICHES Kapitel
    jasmine.clock().tick(2000);

    expect(c.timerSeconds).toBe(6);   // 4 + 2, ein Topf
  });

  it('führt je Kapitel einen eigenen Topf und lädt ihn beim Wechsel zurück', () => {
    const c = makeWithBook();
    load(c, position({ id: 7, chapter: 'A' }));
    c.startTimer();
    jasmine.clock().tick(4000);

    load(c, position({ id: 9, chapter: 'B' }));   // Kapitelwechsel: A wird gesichert, B startet leer
    expect(c.timerSeconds).toBe(0);
    jasmine.clock().tick(2000);
    expect(c.timerSeconds).toBe(2);

    load(c, position({ id: 7, chapter: 'A' }));   // zurück nach A: Topf kommt wieder
    expect(c.timerSeconds).toBe(4);
    expect(JSON.parse(localStorage.getItem('rookhub_calc_timer_1')!)).toEqual({ A: 4, B: 2 });
  });

  it('formatiert die Anzeige als m:ss bzw. h:mm:ss', () => {
    const c = makeWithBook();
    load(c, position({ chapter: null }));
    c.timerSeconds = 65;
    expect(c.timerDisplay).toBe('1:05');
    c.timerSeconds = 3723;
    expect(c.timerDisplay).toBe('1:02:03');
  });

  it('stoppt beim Zerstören und sichert den Stand', () => {
    const c = makeWithBook();
    load(c, position({ chapter: 'A' }));
    c.startTimer();
    jasmine.clock().tick(2000);

    c.ngOnDestroy();

    expect(c.timerRunning).toBeFalse();
    jasmine.clock().tick(5000);
    expect(c.timerSeconds).toBe(2);
    expect(JSON.parse(localStorage.getItem('rookhub_calc_timer_1')!)).toEqual({ A: 2 });
  });
});

describe('CalculationComponent App-Vollbild-Layout', () => {
  it('führt die Zugzeile doppelt: unter dem Brett UND (fürs App-Vollbild) in der Seitenspalte', async () => {
    // Im App-Vollbild blendet CSS die Brett-Variante aus und die Seiten-Variante ein — beide
    // müssen dafür im DOM stehen, an den richtigen Stellen.
    await TestBed.configureTestingModule({
      imports: [CalculationComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CalculationComponent);
    fixture.detectChanges();                       // ngOnInit (HTTP bleibt offen/pending)
    const c = fixture.componentInstance;
    c.bookId = 1;
    load(c, position());
    c.loading = false;
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('.calc-board-col .calc-where--board')).not.toBeNull();
    expect(el.querySelector('.calc-side-col .calc-where--side')).not.toBeNull();
    // Auch die Brett-Kopfzeile (Eingefroren/Timer/Drehen) steht doppelt da: im Vollbild zeigt
    // CSS die Seiten-Variante, damit über dem Brett nichts Höhe stiehlt.
    expect(el.querySelector('.calc-board-col .calc-board-head--board')).not.toBeNull();
    expect(el.querySelector('.calc-side-col .calc-board-head--side')).not.toBeNull();
    // Drei Timer-Anzeigen im DOM: Brett-Kopfzeile, Seiten-Kopie (App-Vollbild) und das
    // data-fs-only-Overlay fürs BRETT-Vollbild — CSS zeigt je Modus genau eine.
    expect(el.querySelectorAll('.calc-timer-time').length).toBe(3);
  });
});

describe('CalculationComponent Selbstbewertung (Stufen)', () => {
  it('setzt die Stufe und schickt sie — nicht die Punktzahl', () => {
    const { component: c, reviews } = make();
    load(c);

    c.setGrade(3);

    expect(c.review.grade).toBe(3);
    expect(c.positionPoints).toBe(3);
    expect(reviews).toEqual([{ id: 7, patch: { grade: 3 } }]);
    // Die Sprunglisten-Zeile zieht sofort mit (ohne auf den Server zu warten).
    expect(c.positions[0].grade).toBe(3);
  });

  it('nimmt die Bewertung zurück, wenn dieselbe Stufe erneut geklickt wird', () => {
    const { component: c, reviews } = make();
    load(c);

    c.setGrade(0);
    expect(c.review.grade).toBe(0);      // Stufe 0 IST eine Bewertung
    c.setGrade(0);

    // „noch nicht bewertet" ist etwas anderes als „nicht gelöst" — und geht als Löschwunsch raus.
    expect(c.review.grade).toBeNull();
    expect(c.isGrade(0)).toBeFalse();
    expect(reviews.map(r => r.patch)).toEqual([{ grade: 0 }, { grade: null }]);
  });

  it('verschiebt die Bewertung auf eine andere Stufe', () => {
    const { component: c } = make();
    load(c);

    c.setGrade(1);
    c.setGrade(4);

    expect(c.review.grade).toBe(4);
    expect(c.positionPoints).toBe(4);
  });

  it('übernimmt die gespeicherte Stufe beim Laden der Stellung', () => {
    const { component: c } = make();
    load(c, position({ grade: 2, secondsSpent: 90, chosenSan: 'Nd5', chosenUci: 'c3d5' }));

    expect(c.review.grade).toBe(2);
    expect(c.isGrade(2)).toBeTrue();
    expect(c.review.chosenSan).toBe('Nd5');
    expect(c.positionTimeDisplay).toBe('1:30');
  });

  it('bietet die fünf Stufen in der Reihenfolge schlecht → gut an', () => {
    const { component: c } = make();
    expect(c.gradeOptions.map(o => o.key)).toEqual([
      'notSolved', 'someIdeas', 'moveNoMainLine', 'moveNoSideLines', 'solved',
    ]);
    expect(c.maxPointsPerPosition).toBe(4);
  });
});

describe('CalculationComponent Summen mit Maximum', () => {
  it('nennt Kapitel- und Kurssumme immer mit dem Maximum (4 je Stellung)', () => {
    const { component: c } = makeWithBook({
      positions: [
        item(1, { chapter: 'Turmendspiele', grade: 4 }),
        item(2, { chapter: 'Turmendspiele', grade: 2 }),
        item(3, { chapter: 'Turmendspiele', grade: null }),
        item(4, { chapter: 'Bauernendspiele', grade: 1 }),
      ],
    });

    expect(c.groups.length).toBe(2);
    expect(c.scoreDisplay(c.groups[0].points, c.groups[0].maxPoints)).toBe('6 / 12');
    expect(c.scoreDisplay(c.groups[1].points, c.groups[1].maxPoints)).toBe('1 / 4');
    expect(c.scoreDisplay(c.totalPoints, c.totalMaxPoints)).toBe('7 / 16');
    expect(c.chapterLabel(c.groups[0])).toBe('Turmendspiele · calc.review.chapterScore');
  });

  it('übernimmt die Kapitelzeit des Servers (Feldname `secondsSum`)', () => {
    const { component: c } = makeWithBook({
      positions: [item(1, { chapter: 'K1' }), item(2, { chapter: 'K1' })],
      // Der Server nennt die Summe `secondsSum` — hieß das Feld hier anders, stünde die Kapitel-
      // und Kurszeit dauerhaft auf 0, ohne dass irgendetwas fehlschlüge.
      chapters: [{ chapter: 'K1', points: 0, maxPoints: 8, secondsSum: 750 }],
      points: 0, maxPoints: 8, secondsSum: 750,
    });

    expect(c.groups[0].seconds).toBe(750);
    expect(c.chapterLabel(c.groups[0])).toBe('K1 · calc.review.chapterSummary');
  });

  it('nimmt die Summen des Servers — bis der Nutzer selbst bewertet', () => {
    const { component: c } = makeWithBook({
      positions: [item(1, { chapter: 'K1', grade: 1 }), item(2, { chapter: 'K1', grade: 1 })],
      chapters: [{ chapter: 'K1', points: 7, maxPoints: 8, secondsSum: 60 }],
      points: 7, maxPoints: 8,
    });
    // Der Server zählt hier bewusst anders als die zwei geladenen Zeilen (7 statt 2) — solange
    // der Nutzer nichts ändert, gilt SEIN Stand.
    expect(c.totalPoints).toBe(7);
    expect(c.groups[0].points).toBe(7);
    expect(c.groups[0].seconds).toBe(60);

    c.setGrade(4);                            // eigene Änderung ⇒ Server-Summen sind überholt

    expect(c.totalPoints).toBe(5);            // jetzt aus den Zeilen: 4 + 1
    expect(c.totalMaxPoints).toBe(8);
    expect(c.groups[0].points).toBe(5);
  });

  it('kommt ohne Server-Summen aus (Maximum ergibt sich aus den Stellungen)', () => {
    const { component: c } = makeWithBook({ positions: [item(1), item(2), item(3)] });
    expect(c.totalPoints).toBe(0);
    expect(c.totalMaxPoints).toBe(12);
  });
});

describe('CalculationComponent Festlegung auf einen ersten Zug', () => {
  /** Zwei verschiedene erste Züge anlegen und ihre Knoten-Ids liefern. */
  function twoFirstMoves(c: CalculationComponent): number[] {
    c.onMove({ orig: 'f3' as never, dest: 'e5' as never });    // Nxe5
    c.startNewLine();
    c.onMove({ orig: 'd2' as never, dest: 'd4' as never });    // d4
    return findNode(c.tree, c.tree.rootId)!.childIds;
  }

  it('merkt sich genau EINE Wahl je Stellung — ein anderer Zug verschiebt sie', () => {
    const { component: c, reviews } = make();
    load(c);
    const [first, second] = twoFirstMoves(c);

    c.chooseMove(first);
    expect(c.review.chosenSan).toBe('Nxe5');
    expect(c.review.chosenUci).toBe('f3e5');

    c.chooseMove(second);
    expect(c.review.chosenSan).toBe('d4');
    expect(reviews.map(r => r.patch.chosenUci)).toEqual(['f3e5', 'd2d4']);
  });

  it('nimmt die Festlegung zurück, wenn derselbe Zug erneut geklickt wird', () => {
    const { component: c } = make();
    load(c);
    const [first] = twoFirstMoves(c);

    c.chooseMove(first);
    c.chooseMove(first);

    expect(c.review.chosenSan).toBeNull();
    expect(c.review.chosenUci).toBeNull();
    expect(c.positions[0].chosenSan).toBeNull();
  });

  it('lässt sich nur auf ERSTE Züge festlegen (Kinder der Ausgangsstellung)', () => {
    const { component: c, reviews } = make();
    load(c);
    c.onMove({ orig: 'f3' as never, dest: 'e5' as never });
    c.onMove({ orig: 'c6' as never, dest: 'e5' as never });    // zweiter Halbzug
    const deep = c.cursorId;

    c.chooseMove(deep);

    expect(c.review.chosenSan).toBeNull();
    expect(reviews.length).toBe(0);
  });

  it('räumt die Festlegung weg, wenn der gewählte Zug aus dem Baum verschwindet', () => {
    const { component: c } = make();
    load(c);
    const [first] = twoFirstMoves(c);
    c.chooseMove(first);
    expect(c.review.chosenUci).toBe('f3e5');

    c.setCursor(first);
    c.deleteFromCursor();

    // Eine Wahl, die in keiner Linie mehr vorkommt, wäre eine Behauptung ohne Grundlage.
    expect(c.review.chosenUci).toBeNull();
  });
});

describe('CalculationComponent Rechenzeit', () => {
  it('misst mit der Stoppuhr aus dem Puzzle-Modus (zählt nicht bei verstecktem Tab)', () => {
    const { component: c } = make();
    expect((c as unknown as { watch: unknown }).watch instanceof VisibilityStopwatch).toBeTrue();
  });

  it('schickt die gemessene Zeit als DELTA und schreibt sie der Stellung sofort gut', () => {
    const reviews: { id: number; patch: CalcReviewPatch }[] = [];
    // Server-Attrappe mit den schon gespeicherten 60 s: sie ADDIERT das Delta und antwortet mit
    // der Gesamtzeit — die der Client nicht selbst kennen kann (anderes Gerät, anderer Tab).
    const { component: c } = make({
      saveReview: (id: number, patch: CalcReviewPatch) => {
        reviews.push({ id, patch });
        return of({
          bookPuzzleId: id, chosenSan: null, chosenUci: null,
          secondsSpent: 60 + (patch.secondsDelta ?? 0), grade: null,
        });
      },
    });
    load(c, position({ secondsSpent: 60 }));
    const inner = c as unknown as { watch: { stop(): number }; harvestWatch(): void };
    spyOn(inner.watch, 'stop').and.returnValue(42);

    inner.harvestWatch();

    expect(reviews.length).toBe(1);
    expect(reviews[0].id).toBe(7);
    expect(reviews[0].patch.secondsDelta).toBe(42);
    expect(reviews[0].patch.secondsToken).toBeTruthy();     // Zeit geht nie ohne Marke raus
    expect(c.review.secondsSpent).toBe(102);
    expect(c.positions[0].secondsSpent).toBe(102);
  });

  it('wiederholt ein gescheitertes Zeit-Delta mit DERSELBEN Marke', () => {
    // Der Kern der At-least-once-Falle: der Fehler kann ein Timeout sein — die Anfrage KAM AN,
    // nur die Antwort ging verloren. Eine frische Marke beim Wiederholen würde die Zeit auf dem
    // Server ein zweites Mal addieren, still und unkorrigierbar.
    const sent: CalcReviewPatch[] = [];
    let failNext = true;
    const { component: c } = make({
      saveReview: (id: number, patch: CalcReviewPatch) => {
        sent.push({ ...patch });
        if (failNext) { failNext = false; return throwError(() => new Error('timeout')); }
        return of({ bookPuzzleId: id, chosenSan: null, chosenUci: null, secondsSpent: 42, grade: null });
      },
    });
    load(c);
    const inner = c as unknown as {
      watch: { stop(): number }; harvestWatch(): void; sendReviews(): void;
    };
    spyOn(inner.watch, 'stop').and.returnValue(42);

    inner.harvestWatch();        // 1. Anlauf scheitert → landet wieder in der Warteschlange
    inner.sendReviews();         // 2. Anlauf

    expect(sent.length).toBe(2);
    expect(sent[0].secondsToken).toBeTruthy();
    expect(sent[1].secondsToken).toBe(sent[0].secondsToken);
    expect(sent[1].secondsDelta).toBe(42);
  });

  it('gibt jeder NEUEN Messung eine eigene Marke (sonst zählte die zweite nie)', () => {
    const { component: c, reviews } = make();
    load(c);
    const inner = c as unknown as {
      watch: { stop(): number }; harvestWatch(): void; beginWatch(id: number): void;
    };
    spyOn(inner.watch, 'stop').and.returnValues(30, 12);

    inner.harvestWatch();
    inner.beginWatch(7);
    inner.harvestWatch();

    expect(reviews.length).toBe(2);
    expect(reviews[0].patch.secondsToken).not.toBe(reviews[1].patch.secondsToken);
  });

  it('lässt die Zeit der Zeile stehen, solange ein neueres Delta wartet', () => {
    // Die Server-Antwort kennt das gerade eingereihte Delta noch nicht — die Zeile würde sonst
    // kurz zu klein anzeigen und erst nach der nächsten Antwort zurückspringen.
    const answer = new Subject<CalcReviewSaved>();
    const { component: c } = make({ saveReview: () => answer });
    load(c, position({ secondsSpent: 60 }));
    const inner = c as unknown as {
      watch: { stop(): number }; harvestWatch(): void; beginWatch(id: number): void;
    };
    spyOn(inner.watch, 'stop').and.returnValues(20, 15);

    inner.harvestWatch();                       // 20 s unterwegs (Antwort steht aus)
    inner.beginWatch(7);
    inner.harvestWatch();                       // 15 s warten in der Schlange
    answer.next({ bookPuzzleId: 7, chosenSan: null, chosenUci: null, secondsSpent: 80, grade: null });

    expect(c.positions[0].secondsSpent).toBe(95);   // nicht 80
  });

  it('schöpft die Zeit ab, ohne etwas zu schicken, wenn nichts gemessen wurde', () => {
    const { component: c, reviews } = make();
    load(c);
    const inner = c as unknown as { watch: { stop(): number }; harvestWatch(): void };
    spyOn(inner.watch, 'stop').and.returnValue(0);

    inner.harvestWatch();

    expect(reviews.length).toBe(0);
  });
});

describe('CalculationComponent Bewertungs-Auswahl (Darstellung)', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('zeigt fünf benannte Stufen statt eines Zahlenfelds — Klick auf die gewählte nimmt zurück', async () => {
    await TestBed.configureTestingModule({
      imports: [CalculationComponent],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        provideNoopAnimations(),
        provideTranslateService({ fallbackLang: 'en' }),
      ],
    }).compileComponents();
    const fixture = TestBed.createComponent(CalculationComponent);
    fixture.detectChanges();                       // ngOnInit (HTTP bleibt offen/pending)
    const c = fixture.componentInstance;
    c.bookId = 1;
    load(c, position());
    c.loading = false;
    fixture.detectChanges();

    const el: HTMLElement = fixture.nativeElement;
    const buttons = el.querySelectorAll<HTMLButtonElement>('.calc-grade');
    expect(buttons.length).toBe(5);
    // Kein Zahlenfeld mehr: die Bedeutung ist die Eingabe.
    expect(el.querySelector('.calc-review input[type="number"]')).toBeNull();
    expect(buttons[2].textContent).toContain('calc.review.grade.moveNoMainLine');

    buttons[2].click();
    fixture.detectChanges();
    expect(c.review.grade).toBe(2);
    expect(el.querySelectorAll('.calc-grade--on').length).toBe(1);

    buttons[2].click();
    fixture.detectChanges();
    expect(c.review.grade).toBeNull();
    expect(el.querySelectorAll('.calc-grade--on').length).toBe(0);
  });
});
