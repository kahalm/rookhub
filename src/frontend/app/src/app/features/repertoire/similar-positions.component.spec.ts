import { SimilarPositionsComponent } from './similar-positions.component';
import { SimilarPositionMatch } from '../../core/repertoire.service';

describe('SimilarPositionsComponent', () => {
  function match(over: Partial<SimilarPositionMatch> = {}): SimilarPositionMatch {
    return {
      repertoireId: 7, repertoireName: 'My Sicilian', chapter: 'Najdorf', lineName: 'Main line',
      gameIndex: 0, ply: 12, fen: '8/8/8/8/8/8/8/8 w - - 0 1',
      score: 83.4, positionScore: 83.4, mirrored: false,
      pawnScore: 91.2, materialScore: 100, pieceScore: 62.5, kingScore: 70,
      moveSan: '', moveFrom: '', moveTo: '', moveMatch: null,
      ...over,
    };
  }

  function make() {
    const c = new SimilarPositionsComponent();
    c.options = [{ id: 7, name: 'My Sicilian' }, { id: 9, name: 'Caro-Kann' }, { id: 11, name: 'Endings' }];
    c.selected = new Set([7, 9, 11]);
    c.matches = [match()];
    return c;
  }

  it('toggle() emits the FULL new selection without the toggled repertoire', () => {
    const c = make();
    const emitted = spyOn(c.selectionChange, 'emit');
    c.toggle(9);
    expect(emitted).toHaveBeenCalledWith([7, 11]);
  });

  it('toggle() adds a repertoire back and keeps the option order', () => {
    const c = make();
    c.selected = new Set([11]);
    const emitted = spyOn(c.selectionChange, 'emit');
    c.toggle(7);
    expect(emitted).toHaveBeenCalledWith([7, 11]);
  });

  it('selectAll()/selectNone() emit all ids resp. an empty selection', () => {
    const c = make();
    const emitted = spyOn(c.selectionChange, 'emit');
    c.selectAll();
    expect(emitted).toHaveBeenCalledWith([7, 9, 11]);
    c.selectNone();
    expect(emitted).toHaveBeenCalledWith([]);
  });

  it('selectedCount ignores ids that are no longer offered', () => {
    const c = make();
    c.selected = new Set([9, 99]);
    expect(c.selectedCount).toBe(1);
    expect(c.isSelected(9)).toBeTrue();
    expect(c.isSelected(7)).toBeFalse();
  });

  it('choosePreset() only emits on a real change', () => {
    const c = make();
    c.preset = 'ausgewogen';
    const emitted = spyOn(c.presetChange, 'emit');
    c.choosePreset('ausgewogen');
    expect(emitted).not.toHaveBeenCalled();
    c.choosePreset('stellungsbild');
    expect(emitted).toHaveBeenCalledWith('stellungsbild');
  });

  it('partValue maps each metric component to its own field', () => {
    const c = make();
    const m = match();
    expect(c.partValue(m, 'pawns')).toBe(91.2);
    expect(c.partValue(m, 'material')).toBe(100);
    expect(c.partValue(m, 'pieces')).toBe(62.5);
    expect(c.partValue(m, 'king')).toBe(70);
  });

  it('round()/pct() survive nonsense from the server', () => {
    const c = make();
    expect(c.round(83.4)).toBe(83);
    expect(c.round(NaN)).toBe(0);
    expect(c.pct(140)).toBe(100);
    expect(c.pct(-5)).toBe(0);
  });

  // ===== Zug-Treffer =====

  it('onMoveInput passes the raw text up (the anchor position lives in the panel)', () => {
    const c = make();
    const emitted = spyOn(c.moveTextChange, 'emit');
    c.onMoveInput({ target: { value: 'Nd5' } } as unknown as Event);
    expect(emitted).toHaveBeenCalledWith('Nd5');
    c.onMoveInput({ target: null } as unknown as Event);
    expect(emitted).toHaveBeenCalledWith('');
  });

  it('hitLabel names the move as it stands there — SAN exactly, long notation for the weaker level', () => {
    const c = make();
    expect(c.hitLabel(match({ moveMatch: 'exact', moveSan: 'Nd5', moveFrom: 'c3', moveTo: 'd5' }))).toBe('Nd5');
    // Schwächere Stufe: gerade das ABWEICHENDE Ausgangsfeld ist die Information.
    expect(c.hitLabel(match({ moveMatch: 'sameTarget', moveSan: 'Nd5', moveFrom: 'f3', moveTo: 'd5' }))).toBe('Nf3-d5');
    expect(c.hitLabel(match({ moveMatch: 'exact', moveSan: '', moveFrom: 'c3', moveTo: 'd5' }))).toBe('c3-d5');
  });

  it('hasBonus only claims two numbers where the bonus actually moved one', () => {
    const c = make();
    expect(c.hasBonus(match({ moveMatch: 'exact', score: 82, positionScore: 64 }))).toBeTrue();
    expect(c.hasBonus(match({ moveMatch: null, score: 64, positionScore: 64 }))).toBeFalse();
    // Trefferstufe, aber (gerundet) derselbe Wert — dann sagt die zweite Zahl nichts.
    expect(c.hasBonus(match({ moveMatch: 'exact', score: 100, positionScore: 100 }))).toBeFalse();
  });

  it('moveLabel turns half-moves into move numbers (white "n.", black "n…")', () => {
    const c = make();
    expect(c.moveLabel(match({ ply: 1 }))).toBe('1.');
    expect(c.moveLabel(match({ ply: 2 }))).toBe('1…');
    expect(c.moveLabel(match({ ply: 3 }))).toBe('2.');
    expect(c.moveLabel(match({ ply: 12 }))).toBe('6…');
    expect(c.moveLabel(match({ ply: 0 }))).toBe('1.'); // Template zeigt hier „Ausgangsstellung"
  });
});
