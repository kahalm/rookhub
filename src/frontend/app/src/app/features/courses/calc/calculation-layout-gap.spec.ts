import { CalculationComponent } from './calculation.component';

// u1-rk-calc-luecke: ab 900 px fiel der Restplatz eines breiten Fensters in die auto-Spalten (justify-content normal =
// stretch) — Brett links, Seitenspalte rechts, ≈ 1 070 px Loch dazwischen. Zentriert bleiben die drei Spalten beisammen.
describe('CalculationComponent Layout (u1-rk-calc-luecke)', () => {
  const css = ((CalculationComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ')
    .replace(/\[_ngcontent-%COMP%\]/g, '');

  it('ab 900 px steht Brett | Leiste | Seitenspalte zentriert als Block', () => {
    expect(css).toMatch(/@media \(min-width: 900px\) \{[^@]*\.calc-layout \{ ?grid-template-columns: auto auto minmax\(320px, 980px\); gap: 0\.6rem; justify-content: center; ?\}/);
  });

  it('zentriert ein Raster mit diesen Spalten tatsächlich, statt die auto-Spalten zu strecken', () => {
    const host = document.createElement('div');
    host.style.cssText = 'display:grid;width:3000px;grid-template-columns:auto auto minmax(320px, 980px);gap:10px;justify-content:center';
    host.innerHTML = '<div style="width:1000px"></div><div style="width:30px"></div><div></div>';
    document.body.appendChild(host);
    const [board, , side] = Array.from(host.children) as HTMLElement[];
    const gap = side.getBoundingClientRect().left - board.getBoundingClientRect().right;
    expect(gap).toBeLessThan(60);
    expect(board.getBoundingClientRect().left - host.getBoundingClientRect().left).toBeGreaterThan(400);
    host.remove();
  });
});
