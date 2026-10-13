import { Type } from '@angular/core';
import { CourseListComponent } from './courses/course-list.component';
import { CourseDetailComponent } from './courses/course-detail.component';
import { CourseBrowseComponent } from './courses/course-browse.component';
import { RepertoireListComponent } from './repertoire/repertoire-list.component';
import { RepertoireDetailComponent } from './repertoire/repertoire-detail.component';
import { RepertoireTrainerComponent } from './repertoire/repertoire-trainer.component';
import { WorksheetListComponent } from './worksheets/worksheet-list.component';
import { GuessListComponent } from './guess/guess-list.component';
import { AdminComponent } from './admin/admin.component';
import { DashboardComponent } from './dashboard/dashboard.component';
import { StatsComponent } from './stats/stats.component';

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

/**
 * UI-Sweep u1 (u1-x-breite-stufe): Breitbild-Stufe ab 2400 px. Raster-, Tabellen- und Brettseiten nehmen dort
 * var(--page-max-width-wide) — NUR innerhalb von @media (min-width: 2400px), damit Laptop/Handy/1920 pixelgleich
 * bleiben. Lese- und Formularseiten bleiben bei --page-max-width (Opt-in je Seite, DESIGN 4.1).
 */
const WIDE_PAGES: [string, Type<unknown>, string][] = [
  ['Kursliste', CourseListComponent, 'courses-container'],
  ['Repertoireliste', RepertoireListComponent, 'repertoire-container'],
  ['Aufgabenblätter', WorksheetListComponent, 'wl-page'],
  ['Punktepartie-Liste', GuessListComponent, 'gl-container'],
  ['Admin', AdminComponent, 'admin-container'],
  ['Dashboard', DashboardComponent, 'dashboard'],
  ['Statistik', StatsComponent, 'stats-container'],
];
/** Kompiliertes CSS ohne Leerraum und ohne %NS%-Platzhalter. */
function compactCss(cmp: Type<unknown>): string {
  return (((cmp as unknown as { ɵcmp: { styles?: string[] } }).ɵcmp.styles) ?? []).join('\n').replace(/%NS%/g, '').replace(/\s/g, '');
}

describe('Breitbild-Stufe ≥ 2400 px (--page-max-width-wide, u1-x-breite-stufe)', () => {
  for (const [name, cmp, cls] of WIDE_PAGES) {
    it(`${name}: .${cls} wird erst ab 2400 px breiter`, () => {
      const css = compactCss(cmp);
      const at = css.indexOf('@media(min-width:2400px)');
      expect(at).withContext('Media-Query ab 2400 px').toBeGreaterThan(-1);
      const re = new RegExp(`\\.${cls}(?:\\[[^\\]]*\\])?\\{[^}]*max-width:min\\(var\\(--page-max-width-wide,1880px\\),96vw\\)`);
      expect(re.test(css.slice(at))).withContext('breite Regel in der Media-Query').toBeTrue();
      // Außerhalb der Media-Query kommt das Token nicht vor — darunter ändert sich nichts.
      expect(css.slice(0, at)).not.toContain('--page-max-width-wide');
    });
  }

  it('styles.scss: Token = --page-max-width unter 2400 px, 1880 px ab 2400 px', () => {
    const root = getComputedStyle(document.documentElement);
    // Karma läuft 1400 px breit: das Token fällt auf die normale Seitenbreite zurück.
    expect(root.getPropertyValue('--page-max-width-wide').trim()).toBe(root.getPropertyValue('--page-max-width').trim());
    let wide: string | null = null;
    for (const sheet of Array.from(document.styleSheets)) {
      let rules: CSSRuleList;
      try { rules = sheet.cssRules; } catch { continue; }
      for (const r of Array.from(rules)) {
        if (!(r instanceof CSSMediaRule) || r.conditionText.replace(/\s/g, '') !== '(min-width:2400px)') continue;
        for (const inner of Array.from(r.cssRules)) {
          if (inner instanceof CSSStyleRule && inner.selectorText === ':root') {
            wide = inner.style.getPropertyValue('--page-max-width-wide').trim() || wide;
          }
        }
      }
    }
    expect(wide).toBe('1880px');
  });
});
