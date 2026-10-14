import { PrepPlayerComponent } from './prep-player.component';

// b1-rk-breitstufe-zeilen-auseinander: „Letzte Partien" — ab 2400 px Datum und Gegner so schmal wie ihr Inhalt.
describe('UI-Sweep b1 — Spielervorbereitung „Letzte Partien" ab 2400 px', () => {
  it('Datum- und Gegnerspalte nur in der Breitbild-Stufe auf Inhaltsbreite', () => {
    const css = (((PrepPlayerComponent as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/\s/g, '');
    const at = css.indexOf('@media(min-width:2400px)');
    expect(at).toBeGreaterThan(-1);
    expect(css.slice(at)).toMatch(/table\.recenttd:nth-child\(1\),[^{]*table\.recenttd:nth-child\(2\)\{width:1%;white-space:nowrap/);
    expect(css.slice(0, at)).not.toContain('width:1%');
  });
});
