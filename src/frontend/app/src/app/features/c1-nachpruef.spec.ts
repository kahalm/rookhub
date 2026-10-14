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
  it('E-Books: „Filter ▾“ ist ab 600 px ausgeblendet — die Regel schlägt .eb-chip (gleich stark und davor stand sie)', () => {
    const c = css(EbookListComponent);
    expect(c).toMatch(/\.eb-chip(\[[^\]]*\])?\.eb-filter-toggle(\[[^\]]*\])?\{display:none/);
    // Am Handy wieder sichtbar, ebenso stark.
    expect(c).toMatch(/max-width:599\.98px\)\{[^@]*\.eb-chip(\[[^\]]*\])?\.eb-filter-toggle(\[[^\]]*\])?\{display:inline-flex/);
  });

  it('Leser am Handy: ⋮ steht in der Titelzeile, damit der Diagramm-Knopf es nicht in eine dritte Zeile schiebt', () => {
    expect(css(EbookReaderComponent)).toMatch(/max-width:599\.98px\)\{[^@]*\.rd-more(\[[^\]]*\])?\{order:-1/);
  });

});
