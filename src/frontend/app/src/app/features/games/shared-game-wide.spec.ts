import { SharedGameComponent } from './shared-game.component';

// u1-rk-partie-brett: bei 3440 × 1440 blieb das Brett bei 760 px, seitlich je ≈ 900 px leer, die Auswertung gestapelt
// und scrollend. Ab 2400 px vier Spalten. Karma läuft 1 400 px breit — geprüft werden die Regeln selbst.
describe('SharedGameComponent Breitbild-Stufe (u1-rk-partie-brett)', () => {
  const css = ((SharedGameComponent as any).ɵcmp.styles as string[]).join('\n').replace(/\s+/g, ' ')
    .replace(/\[_ngcontent-%COMP%\]/g, '').replace(/--%NS%/g, '--');

  function block(query: string): string {
    const start = css.indexOf(query);
    if (start < 0) return '';
    let depth = 0;
    for (let i = css.indexOf('{', start); i < css.length; i++) {
      if (css[i] === '{') depth++;
      if (css[i] === '}' && --depth === 0) return css.slice(start, i + 1);
    }
    return '';
  }

  it('ab 2400 px: Seite bis 2 560 px, Brett bis 1 080 px in Fensterhöhe, Auswertung in zwei Spalten', () => {
    const wide = block('@media (min-width: 2400px)');
    expect(wide).withContext('Breitbild-Media-Query fehlt').not.toBe('');
    expect(wide).toContain('min(2560px, 96vw)');
    expect(wide).toMatch(/--board-size: ?clamp\(360px, min\((calc\()?100vh - 250px/);
    expect(wide).toMatch(/1080px\); ?\}/);
    expect(wide).toMatch(/\.side-section \{ ?display: grid; grid-template-columns: minmax\(0, ?1fr\) minmax\(0, ?1fr\)/);
    expect(wide).toMatch(/\.review-slot \{ ?grid-column: 1; grid-row: 1 \/ span 2/);
  });

  it('unter 2400 px bleibt der 760-px-Deckel und die einspaltige Auswertung', () => {
    const narrow = css.replace(block('@media (min-width: 2400px)'), '');
    expect(narrow).toContain('760px)');
    expect(narrow).not.toContain('1080px');
    expect(block('@media (min-width: 1280px)')).toContain('grid-template-columns: var(--board-size) var(--moves-width) var(--side-width)');
  });
});
