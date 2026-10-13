import { AnalysisComponent } from './analysis.component';

// u1-rk-analyse-3spalten: bei 3440 × 1440 hielt der 820-px-Deckel das Brett klein, links und rechts je ≈ 880 px leer, der
// Explorer zugeklappt. Ab 2400 px drei Spalten (Brett | Engine + Züge | Explorer offen + FEN/PGN). Karma läuft 1 400 px
// breit — geprüft werden daher die Regeln selbst; unter 2400 px sind die Hüllen `display: contents` (Layout unverändert).
describe('AnalysisComponent Breitbild-Stufe (u1-rk-analyse-3spalten)', () => {
  const css = ((AnalysisComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ');

  function wideBlock(): string {
    const start = css.indexOf('@media (min-width: 2400px)');
    expect(start).withContext('Breitbild-Media-Query fehlt').toBeGreaterThan(-1);
    let depth = 0;
    for (let i = css.indexOf('{', start); i < css.length; i++) {
      if (css[i] === '{') depth++;
      if (css[i] === '}' && --depth === 0) return css.slice(start, i + 1);
    }
    return '';
  }

  it('unter 2400 px sind die Unterspalten nur Hüllen', () => {
    expect(css).toMatch(/\.side-a\[[^\]]*\], ?\.side-b\[[^\]]*\] ?\{ ?display: ?contents|\.side-a, ?\.side-b ?\{ ?display: ?contents/);
  });

  it('ab 2400 px: Seite bis 2 560 px, Brett bis 1 100 px in Fensterhöhe, zwei Unterspalten ≥ 440 px', () => {
    const block = wideBlock();
    expect(block).toContain('min(2560px, 96vw)');
    // Sass kürzt calc() innerhalb von min() — beide Schreibweisen gelten.
    expect(block).toMatch(/min\(64vw, (calc\()?100vh - 170px\)?, 1100px, (calc\()?100% - 960px\)?\)/);
    expect(block).toMatch(/flex-direction: ?row/);
    expect(block).toMatch(/min-width: ?440px/);
  });
});
