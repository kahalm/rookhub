import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { CalcLinesComponent } from './calc-lines.component';
import { addMove, createTree, setComment } from './calc-tree.util';

const START = 'r1bqkbnr/pppp1ppp/2n5/4p3/2B1P3/5N2/PPPP1PPP/RNBQK2R w KQkq - 4 4';

function mv(san: string, uci: string) {
  return { san, uci, fen: `fen-${uci}` };
}

describe('CalcLinesComponent', () => {
  it('creates (template AOT-compiles + DI resolves)', async () => {
    await TestBed.configureTestingModule({
      imports: [CalcLinesComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(CalcLinesComponent);
    fixture.componentInstance.tree = createTree(START);
    fixture.componentInstance.startFen = START;
    fixture.detectChanges();
    expect(fixture.componentInstance).toBeTruthy();
  });
});

describe('CalcLinesComponent Darstellung', () => {
  function make() {
    // Translate-Stub liefert den Schlüssel zurück — reicht für Darstellung + Symbol-Namen.
    const c = new CalcLinesComponent({ instant: (k: string) => k } as never);
    c.tree = createTree(START);
    c.startFen = START;
    return c;
  }

  it('numbers the first ply and hides the number for black mid-line', () => {
    const c = make();
    expect(c.prefixFor(0)).toBe('4.');
    expect(c.prefixFor(1)).toBe('');
    expect(c.prefixFor(2)).toBe('5.');
  });

  it('marks the line the cursor sits on as active', () => {
    const c = make();
    const a = addMove(c.tree, c.tree.rootId, mv('Nxe5', 'f3e5'));
    const b = addMove(c.tree, a.id, mv('Nxe5', 'c6e5'));
    const other = addMove(c.tree, c.tree.rootId, mv('d4', 'd2d4'));
    const [first, second] = c.allLines;

    c.cursorId = b.id;
    expect(c.isActiveLine(first)).toBeTrue();
    expect(c.isActiveLine(second)).toBeFalse();

    c.cursorId = other.id;
    expect(c.isActiveLine(second)).toBeTrue();
  });

  it('exposes the leaf comment as the line comment', () => {
    const c = make();
    const a = addMove(c.tree, c.tree.rootId, mv('Nxe5', 'f3e5'));
    setComment(c.tree, a.id, 'gewinnt einen Bauern');
    expect(c.leafComment(c.allLines[0])).toBe('gewinnt einen Bauern');
  });

  it('toggles the comment editor and emits the edited text', () => {
    const c = make();
    const a = addMove(c.tree, c.tree.rootId, mv('Nxe5', 'f3e5'));
    setComment(c.tree, a.id, 'alt');
    const emitted: { nodeId: number; text: string }[] = [];
    c.commentChanged.subscribe(e => emitted.push(e));

    c.toggleComment(a.id);
    expect(c.editingLeafId).toBe(a.id);
    expect(c.draftComment).toBe('alt');       // bestehender Kommentar wird vorbelegt

    c.draftComment = 'neu';
    c.commitComment(a.id);
    expect(emitted).toEqual([{ nodeId: a.id, text: 'neu' }]);
    expect(c.editingLeafId).toBeNull();

    c.toggleComment(a.id);
    c.toggleComment(a.id);                    // zweiter Klick schließt wieder
    expect(c.editingLeafId).toBeNull();
  });

  it('gibt den Symbolen in der Notation ihre Bedeutung als natives title', () => {
    // Bewusst `title` statt `matTooltip`: CDK-Overlays hängen am <body> und wären im
    // Brett-Vollbild unsichtbar.
    const c = make();
    expect(c.glyphName('!!')).toBe('calc.glyph.brilliant');
    expect(c.evalName('+−')).toBe('calc.eval.whiteWinning');
    expect(c.evalName('⨀')).toBe('calc.eval.zugzwang');
  });

});
