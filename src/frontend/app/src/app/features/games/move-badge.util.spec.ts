import { MOVE_CLASSES, MOVE_CLASS_COLORS } from './game-review.util';
import { MOVE_CLASS_SYMBOLS, moveBadgeSvg } from './move-badge.util';

describe('moveBadgeSvg', () => {
  it('jede Klasse bekommt einen Kreis in ihrer Farbe', () => {
    for (const c of MOVE_CLASSES) expect(moveBadgeSvg(c)).toContain(`fill="${MOVE_CLASS_COLORS[c]}"`);
  });

  it('Satzzeichen-Klassen stehen als Text, die übrigen als gezeichnete Glyphe — kein Emoji auf dem Brett', () => {
    expect(moveBadgeSvg('blunder')).toContain('>??</text>');
    expect(moveBadgeSvg('inaccuracy')).toContain('>?!</text>');
    expect(moveBadgeSvg('brilliant')).toContain('>!!</text>');
    for (const c of ['best', 'excellent', 'good', 'book', 'miss'] as const) {
      const svg = moveBadgeSvg(c);
      expect(svg).not.toContain('<text');
      expect(svg).not.toContain(MOVE_CLASS_SYMBOLS[c]);
    }
  });

  it('ist gültiges SVG', () => {
    for (const c of MOVE_CLASSES) {
      const doc = new DOMParser().parseFromString(`<svg xmlns="http://www.w3.org/2000/svg">${moveBadgeSvg(c)}</svg>`, 'image/svg+xml');
      expect(doc.querySelector('parsererror')).withContext(c).toBeNull();
    }
  });
});
