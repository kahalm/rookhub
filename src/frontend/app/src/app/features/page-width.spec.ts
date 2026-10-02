import { Type } from '@angular/core';
import { CourseListComponent } from './courses/course-list.component';
import { CourseDetailComponent } from './courses/course-detail.component';
import { CourseBrowseComponent } from './courses/course-browse.component';
import { RepertoireListComponent } from './repertoire/repertoire-list.component';
import { RepertoireDetailComponent } from './repertoire/repertoire-detail.component';
import { RepertoireTrainerComponent } from './repertoire/repertoire-trainer.component';
import { WorksheetListComponent } from './worksheets/worksheet-list.component';

/**
 * Codereview W5 F3-017: „EINE Seitenbreite für alle Inhaltsseiten" (styles.scss, --page-max-width) — in Kursen,
 * Repertoires und Aufgabenblättern hielt sich nur worksheet-detail daran. Bei 1440 px sprang die Inhaltsbreite beim
 * Wechsel Kurse → Repertoires → Aufgabenblätter von 1100 auf 1200 auf 900 px. Bewusste Ausnahmen (mit Kommentar im
 * Stylesheet): Kalkulation (Brett + Baum + Liste) und die Karteikarten (Druckvorschau eines A4-Blatts).
 *
 * Gelesen werden die KOMPILIERTEN Komponenten-Styles — ohne die Seiten samt Diensten aufzubauen.
 */
const PAGES: [string, Type<unknown>, string][] = [
  ['Kursliste', CourseListComponent, 'courses-container'],
  ['Kursdetail', CourseDetailComponent, 'detail-page'],
  ['Durchsehen', CourseBrowseComponent, 'browse-container'],
  ['Repertoireliste', RepertoireListComponent, 'repertoire-container'],
  ['Repertoire-Detail', RepertoireDetailComponent, 'detail-container'],
  ['Repertoire-Trainer', RepertoireTrainerComponent, 'trainer'],
  ['Aufgabenblätter', WorksheetListComponent, 'wl-page'],
];

/** Der erste Regelblock, dessen Selektor genau mit `.<cls>` beginnt (gekapselt: `.cls[_ngcontent-%COMP%]`). */
function containerRule(cmp: Type<unknown>, cls: string): string | null {
  // Der Compiler setzt vor eigene Custom Properties den Platzhalter %NS% — fuer den Vergleich ohne ihn.
  const css = (((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/%NS%/g, '');
  const m = new RegExp(`(?:^|[}\\s])\\.${cls}(?:\\[[^\\]]*\\])?\\s*\\{([^}]*)\\}`).exec(css);
  return m ? m[1] : null;
}

describe('Seitenbreite der Inhaltsseiten (--page-max-width)', () => {
  for (const [name, cmp, cls] of PAGES) {
    it(`${name}: Container .${cls} nimmt die gemeinsame Seitenbreite`, () => {
      const rule = containerRule(cmp, cls);
      expect(rule).withContext(`Regel für .${cls} gefunden`).not.toBeNull();
      expect(rule!.replace(/\s/g, '')).toContain('max-width:min(var(--page-max-width),96vw)');
    });
  }

  it('Repertoireliste: derselbe 16-px-Rand wie die übrigen Listen (nicht 2rem)', () => {
    expect(containerRule(RepertoireListComponent, 'repertoire-container')!.replace(/\s/g, '')).toContain('padding:16px');
  });
});
