import { TestBed } from '@angular/core/testing';
import { Chess } from 'chess.js';
import { MoveListComponent } from './move-list.component';

function build(component: MoveListComponent, fen: string, sans: string[]) {
  const chess = new Chess(fen);
  sans.forEach(s => chess.move(s));
  component.moves = chess.history({ verbose: true });
  (component as unknown as { buildPairs: () => void }).buildPairs();
}

describe('MoveListComponent numbering', () => {
  let component: MoveListComponent;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [MoveListComponent] });
    component = TestBed.createComponent(MoveListComponent).componentInstance;
  });

  it('numbers a normal white-to-move game as N. white black', () => {
    build(component, 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1', ['e4', 'e5', 'Nf3']);
    expect(component.movePairs[0]).toEqual(jasmine.objectContaining({ number: 1, white: 'e4', black: 'e5' }));
    expect(component.movePairs[1]).toEqual(jasmine.objectContaining({ number: 2, white: 'Nf3' }));
    expect(component.movePairs[1].black).toBeUndefined();
  });

  it('numbers a black-to-move start as "N... black" (not white)', () => {
    // FEN: Schwarz am Zug, Vollzug 1
    build(component, 'rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq - 0 1', ['e5', 'Nf3']);
    expect(component.movePairs[0]).toEqual(jasmine.objectContaining({ number: 1, black: 'e5' }));
    expect(component.movePairs[0].white).toBeUndefined();
    expect(component.movePairs[1]).toEqual(jasmine.objectContaining({ number: 2, white: 'Nf3' }));
  });
});

describe('MoveListComponent Kommentare', () => {
  const START_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';

  function render(comments: { [i: number]: string }, segments: any = null) {
    TestBed.configureTestingModule({ imports: [MoveListComponent] });
    const fixture = TestBed.createComponent(MoveListComponent);
    const chess = new Chess(START_FEN);
    ['e4', 'e6', 'Nf3', 'd5'].forEach(s => chess.move(s));
    fixture.componentRef.setInput('moves', chess.history({ verbose: true }));
    fixture.componentRef.setInput('comments', comments);
    fixture.componentRef.setInput('commentSegments', segments);
    fixture.detectChanges();
    const el = fixture.nativeElement as HTMLElement;
    const rowText = (r: Element) => r.classList.contains('move-row')
      ? Array.from(r.children).map(c => c.textContent!.trim()).filter(t => t).join(' ')
      : r.textContent!.replace(/\s+/g, ' ').trim();
    return { fixture, el, rows: () => Array.from(el.querySelectorAll('.move-row, .comment-row')).map(rowText) };
  }

  it('zeigt die Einleitung vor dem ersten Zug', () => {
    const { rows } = render({ [-1]: 'Willkommen' });
    expect(rows()[0]).toBe('Willkommen');
  });

  it('zeigt beide Kommentare eines Zugpaars — Schwarz rückt in eine eigene Zeile', () => {
    const { rows } = render({ 0: 'Weiß', 1: 'Schwarz' });
    expect(rows()).toEqual(['1. e4', 'Weiß', '1. e6', 'Schwarz', '2. Nf3 d5']);
  });

  it('macht Züge anklickbar, wenn Stücke übergeben werden, und meldet den Klick', () => {
    const seg = { move: '2.d4', fen: 'x', from: 'd2', to: 'd4' };
    const { fixture, el } = render({ [-1]: 'dass er 2.d4 spielt' },
      { [-1]: [{ text: 'dass er ' }, seg, { text: ' spielt' }] });
    const clicked: unknown[] = [];
    fixture.componentInstance.commentMoveClicked.subscribe(s => clicked.push(s));

    const chip = el.querySelector('.comment-row .cmt-move') as HTMLButtonElement;
    expect(chip.textContent!.trim()).toBe('2.d4');
    chip.click();

    expect(clicked).toEqual([seg]);
  });
});
