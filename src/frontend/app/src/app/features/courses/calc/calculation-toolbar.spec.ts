import { CalculationComponent } from './calculation.component';

/** UI-Sweep z1 (z1-rk-calc-werkzeugzeile): einspaltig keine eigene Werkzeugzeile unter dem Brett. */
describe('CalculationComponent Werkzeugzeile', () => {
  const css = ((CalculationComponent as unknown as { ɵcmp: { styles: string[] } }).ɵcmp.styles).join('\n').replace(/\s+/g, ' ');
  it('blendet die Leiste unter 900 px aus und zeigt Kopieren + Einstellungen in der Befehlsleiste', () => {
    const narrow = css.slice(css.lastIndexOf('@media (max-width: 899.98px)'));
    expect(narrow).toMatch(/\.calc-rail[^{]*\{ ?display: none/);
    expect(narrow).toMatch(/\.calc-settings-bar[^{]*\{ ?display: inline-flex/);
    expect(narrow).not.toContain('.calc-copy-bar { display: none');
  });
});
