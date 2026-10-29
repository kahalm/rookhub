import { Type } from '@angular/core';
import { EbookListComponent } from './ebooks/ebook-list.component';
import { EbookReaderComponent } from './ebooks/ebook-reader.component';

/** Nachprüfung der c1-Umsetzung (Lauf c1-nachpruef, 0.775.2): Rest- und Folgefehler. */
function cmpDef(cmp: Type<unknown>) {
  return (cmp as unknown as { ɵcmp: { styles?: string[]; template?: unknown } }).ɵcmp;
}
function css(cmp: Type<unknown>): string {
  return (cmpDef(cmp).styles ?? []).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
}

describe('Nachprüfung c1 (0.775.3)', () => {
  it('E-Books: „Filter ▾“ steht auf jeder Breite, Sortierung und „Meine Uploads“ seit 0.877.2 ebenfalls dahinter', () => {
    const c = css(EbookListComponent);
    expect(c).not.toMatch(/\.eb-filter-toggle(\[[^\]]*\])?\{display:none/);
    expect(c).not.toMatch(/\.eb-active-mine(\[[^\]]*\])?\{display:none/);
    expect(c).toMatch(/\.eb-filter-more(\[[^\]]*\])?\{display:none/);
  });

  it('Leser am Handy: ⋮ steht in der Titelzeile, damit der Diagramm-Knopf es nicht in eine dritte Zeile schiebt', () => {
    expect(css(EbookReaderComponent)).toMatch(/max-width:599\.98px\)\{[^@]*\.rd-more(\[[^\]]*\])?\{order:-1/);
  });

});
