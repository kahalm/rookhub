import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { provideRouter } from '@angular/router';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { of, throwError } from 'rxjs';
import { CalculationComponent } from './calculation.component';
import { CalcPosition } from './calculation.service';
import { findNode, lines } from './calc-tree.util';

const START = 'r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4';

function position(overrides: Partial<CalcPosition> = {}): CalcPosition {
  return {
    id: 7, bookId: 1, round: '3', title: 'Aufgabe 3', chapter: null,
    fen: START, setupMoves: '', comment: 'Weiß am Zug — was rechnest du?',
    treeJson: null, treeUpdatedAt: null,
    ...overrides,
  };
}

/** Komponente ohne Template, mit Stub-Abhängigkeiten — für die reine Bedienlogik. */
function make(api: Partial<Record<'getBook' | 'getPosition' | 'saveTree' | 'deleteTree', unknown>> = {}) {
  const saved: { id: number; json: string }[] = [];
  const deleted: number[] = [];
  const warnings: string[] = [];
  const apiStub = {
    getBook: () => of({ bookId: 1, displayName: 'B', isCalculation: true, positions: [] }),
    getPosition: () => of(position()),
    saveTree: (id: number, json: string) => { saved.push({ id, json }); return of({ bookPuzzleId: id, updatedAt: '2026-07-28T10:00:00Z' }); },
    deleteTree: (id: number) => { deleted.push(id); return of(undefined); },
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
  return { component, saved, deleted, warnings };
}

/** Stellung laden, ohne den HTTP-Pfad zu bemühen (applyPosition ist der Kern davon). */
function load(component: CalculationComponent, pos: CalcPosition = position()): void {
  component.position = pos;
  (component as unknown as { applyPosition: (p: CalcPosition) => void }).applyPosition(pos);
  component.positions = [{ id: pos.id, round: pos.round, title: pos.title, chapter: pos.chapter, hasTree: !!pos.treeJson }];
  component.index = 0;
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
