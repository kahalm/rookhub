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

// a1-rk-analyse-tablet-brett: zwischen 769 und ~840 px passten Brett und Seitenspalte nicht nebeneinander, das Brett klebte
// trotzdem (ab 769 px) mit ~539 px über Engine und Zügen und stand linksbündig. Jetzt klebt es erst ab 900 px (dort hält
// die Brettspalte der Seitenspalte 300 px frei), darunter steht es mittig. Karma läuft 1 400 px breit → Regeln als Text.
describe('AnalysisComponent Tablet-Brett (a1-rk-analyse-tablet-brett)', () => {
  const css = ((AnalysisComponent as any).ɵcmp.styles as string[]).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');

  function block(query: string): string {
    const start = css.indexOf(query);
    expect(start).withContext(`${query} fehlt`).toBeGreaterThan(-1);
    let depth = 0;
    for (let i = css.indexOf('{', start); i < css.length; i++) {
      if (css[i] === '{') depth++;
      if (css[i] === '}' && --depth === 0) return css.slice(start, i + 1);
    }
    return '';
  }

  it('klebt erst ab 900 px und hält dort der Seitenspalte Platz frei', () => {
    expect(css).not.toContain('@media(min-width:769px){');
    const wide = block('@media(min-width:900px)');
    expect(wide).toContain('position:sticky');
    expect(wide).toMatch(/(calc\()?100%-300px\)?\)/);
  });

  it('769–899.98 px: Brett mittig in voller Breite, nicht klebend', () => {
    const tab = block('@media(min-width:769px)and(max-width:899.98px)');
    expect(tab).toContain('margin-inline:auto');
    expect(tab).toMatch(/width:min\(100%,(calc\()?100vh-170px\)?\)/);
    expect(tab).not.toContain('sticky');
  });
});

// a1-rk-evalbar-hell: im hellen Design verschwand der weiße Teil der Bewertungsleiste auf der hellen Seite.
describe('AnalysisComponent Bewertungsleiste (a1-rk-evalbar-hell)', () => {
  const css = ((AnalysisComponent as any).ɵcmp.styles as string[]).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');

  it('hat einen Rahmen aus dem Umriss-Token (trägt hell und dunkel)', () => {
    const m = /\.eval-bar(?:\[[^\]]*\])?\{([^}]*)\}/.exec(css);
    expect(m).not.toBeNull();
    expect(m![1]).toContain('border:1pxsolidvar(--mat-sys-outline)');
    expect(m![1]).toContain('box-sizing:border-box');
  });
});

// b1-w-analyse-verlauf-klebt: der Abstand zwischen Seitenkopf und Brett hing am h1 (margin-bottom 8 px), wirkte bei
// zentrierter Ausrichtung nur halb — ohne Vollbild-Knopf (Safari) klebte „Verlauf" 4 px über dem Brett. Jetzt trägt
// der Kopf 12 px selbst, das h1 hat keinen Rand.
describe('AnalysisComponent Seitenkopf (b1-w-analyse-verlauf-klebt)', () => {
  const css = ((AnalysisComponent as any).ɵcmp.styles as string[]).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');

  it('der Kopf trägt den Abstand zum Brett, nicht das h1', () => {
    expect(css).toMatch(/\.header(\[[^\]]*\])?\{[^}]*margin-bottom:12px/);
    expect(css).toMatch(/\.header(\[[^\]]*\])?h1(\[[^\]]*\])?\{margin:0;?\}/);
  });
});
