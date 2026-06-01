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
