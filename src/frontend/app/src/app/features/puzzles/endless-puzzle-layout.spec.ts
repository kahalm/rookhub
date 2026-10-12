import { EndlessPuzzleComponent } from './endless-puzzle.component';
import { EndlessHistoryComponent } from './endless-history.component';

/** Abstände im Endlos-Bereich (UI-Sweep z2) — geprüft an den kompilierten Stilen, ohne die große Komponente zu rendern. */
describe('Endlos-Abstände (UI-Sweep z2)', () => {
  const css = (cmp: unknown) => (((cmp as any).ɵcmp.styles as string[]).join('\n')).replace(/\s+/g, ' ');

  it('der Schalter „5 schwächste Themen" hat Luft zum Themenfeld und zur Trennlinie (z2-rk-endless-schwaechste-eng)', () => {
    const s = css(EndlessPuzzleComponent);
    expect(s).toMatch(/\.worst-tags-row[^{]*\{[^}]*margin-top: 8px/);
    expect(s).toMatch(/\.adv-row[^{]*\{[^}]*margin: 16px 0/);
  });

  it('der Umschalter Alle/Aktiv/Archiviert klebt nicht am Titel (z2-rk-endless-verlauf-filter)', () => {
    expect(css(EndlessHistoryComponent)).toMatch(/\.toolbar[^{]*\{[^}]*margin: 12px 0 16px/);
  });
});
