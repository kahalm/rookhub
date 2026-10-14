import { Type } from '@angular/core';
import { GuessBoardComponent } from './guess-board.component';
import { GuessListComponent } from './guess-list.component';

function css(cmp: Type<unknown>): string {
  return (((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n')
    .replace(/\s+/g, ' ').replace(/\[_ngcontent-%COMP%\]/g, '');
}

function block(all: string, query: string): string {
  const start = all.indexOf(query);
  if (start < 0) return '';
  let depth = 0;
  for (let i = all.indexOf('{', start); i < all.length; i++) {
    if (all[i] === '{') depth++;
    if (all[i] === '}' && --depth === 0) return all.slice(start, i + 1);
  }
  return '';
}

describe('UI-Sweep b1 — Punktepartie', () => {
  it('b1-rk-guess-brett-tablet: 600–899 px einspaltig, Brett mittig bis Fensterhöhe, Seitenspalte volle Breite', () => {
    const tab = block(css(GuessBoardComponent), '@media (min-width: 600px) and (max-width: 899.98px)');
    expect(tab).withContext('Tablet-Media-Query').not.toBe('');
    expect(tab).toMatch(/\.body \{ ?flex-direction: column; align-items: center/);
    expect(tab).toMatch(/\.board-col \{ ?flex: none; width: min\(100%, max\(320px, (calc\()?100vh - 300px\)?\)\)/);
    expect(tab).toMatch(/\.side-col \{ ?flex: none; width: 100%; max-width: none/);
  });

  it('b1-rk-breitstufe-zeilen-auseinander: Partienliste ab 2400 px zweispaltig, darunter einspaltig', () => {
    const all = css(GuessListComponent);
    const wide = block(all, '@media (min-width: 2400px)');
    expect(wide).toMatch(/\.g-list \{ ?display: grid; grid-template-columns: repeat\(2, minmax\(0, 1fr\)\); column-gap: 40px/);
    expect(all.replace(wide, '')).not.toContain('.g-list {');
  });
});
