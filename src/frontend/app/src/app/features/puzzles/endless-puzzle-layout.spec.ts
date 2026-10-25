import { EndlessPuzzleComponent } from './endless-puzzle.component';
import { EndlessConfigComponent } from './endless-config.component';
import { EndlessHistoryComponent } from './endless-history.component';

/** Abstände im Endlos-Bereich (UI-Sweep z2) — geprüft an den kompilierten Stilen, ohne die große Komponente zu rendern.
 *  Die Regeln des Start-Bildschirms liegen seit der Zerlegung Z19 in EndlessConfigComponent. */
describe('Endlos-Abstände (UI-Sweep z2)', () => {
  const css = (cmp: unknown) => (((cmp as any).ɵcmp.styles as string[]).join('\n')).replace(/\s+/g, ' ');

  it('der Schalter „5 schwächste Themen" hat Luft zum Themenfeld und zur Trennlinie (z2-rk-endless-schwaechste-eng)', () => {
    const s = css(EndlessConfigComponent);
    expect(s).toMatch(/\.worst-tags-row[^{]*\{[^}]*margin-top: 8px/);
    expect(s).toMatch(/\.adv-row[^{]*\{[^}]*margin: 16px 0/);
  });

  it('Themen-Schnellwahl-Chips erben die App-Schrift (a1-rk-endless-chips-schrift)', () => {
    expect(css(EndlessConfigComponent)).toMatch(/\.theme-preset-chip[^{:]*\{[^}]*font-family: inherit/);
  });

  it('Sitzungszähler lesbar, Verlauf-Link mit voller Tippfläche (a1-rk-endless-verlauf-link-klein)', () => {
    const s = css(EndlessConfigComponent);
    expect(s).toMatch(/\.session-count[^{]*\{[^}]*color: color-mix\(in srgb, currentColor 65%, transparent\)/);
    const link = /\.history-link[^{ ]*\s?\{([^}]*)\}/.exec(s)![1];
    expect(link).toContain('font-size: 0.9rem');
    expect(link).not.toContain('min-height');
    expect(link).not.toContain('line-height');
  });

  it('der Umschalter Alle/Aktiv/Archiviert klebt nicht am Titel (z2-rk-endless-verlauf-filter)', () => {
    expect(css(EndlessHistoryComponent)).toMatch(/\.toolbar[^{]*\{[^}]*margin: 12px 0 16px/);
  });

  it('d4-rk-endless-brett-breiter-als-puzzle: am Handy kein eigener 0,5-rem-Seitenrand (1 rem wie Standard-/Buch-Löser)', () => {
    const s = css(EndlessPuzzleComponent);
    const at = s.indexOf('@media (max-width: 768px)');
    expect(at).toBeGreaterThan(-1);
    const block = s.slice(at, s.indexOf('}', s.indexOf('{', at) + 1) + 1);
    expect(block).not.toContain('.endless-page');
    expect(s).toMatch(/\.endless-page[^{]*\{ padding: 1rem;/);
  });
});
