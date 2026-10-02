import { mountWithComponentStyles, parseColor, resolveColor } from './contrast';

/**
 * Der Mess-Helfer selbst (Nacharbeit UX-008): Angular 22 schreibt eigene Custom Properties der Komponentenstile als
 * `var(--%NS%name)`. Ersetzte der Helfer nur `%COMP%`, fiele jede `var()`-Deklaration weg und die Kontrast-Specs
 * maessen die Browser-Vorgabe — gruen, egal welcher Token-Wert im Stil steht.
 */
describe('mountWithComponentStyles', () => {
  let dispose: (() => void) | undefined;
  afterEach(() => { dispose?.(); dispose = undefined; });

  const fake = (css: string) => ({ ɵcmp: { styles: [css] } });

  it('loest den %NS%-Platzhalter auf: var(--%NS%…) wirkt wie im Renderer', () => {
    dispose = mountWithComponentStyles(
      fake('.probe[_ngcontent-%COMP%]{color:color-mix(in srgb, var(--%NS%rh-accent) 30%, transparent)}'),
      '<span class="probe">x</span>');
    const color = parseColor(getComputedStyle(document.querySelector('.probe')!).color);
    const accent = resolveColor('var(--rh-accent)');
    expect(color[3]).withContext('Deckkraft der 30-%-Mischung').toBeCloseTo(0.3, 2);
    expect(color.slice(0, 3).map(Math.round)).toEqual(accent.slice(0, 3).map(Math.round));
  });

  it('bricht ab, wenn ein Platzhalter stehen bliebe, statt still die Browser-Vorgabe zu messen', () => {
    expect(() => mountWithComponentStyles(fake('.probe[_ngcontent-%COMP%]{color:var(--%XY%rh-accent)}'), '<span></span>'))
      .toThrowError(/%XY%/);
    expect(Array.from(document.querySelectorAll('style')).some(s => s.textContent?.includes('%XY%'))).toBeFalse();
  });
});
