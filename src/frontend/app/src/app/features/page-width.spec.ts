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
import { GameAnalysisDetailComponent } from './analysis/game-analysis-detail.component';
import { MoveComparisonComponent } from './analysis/move-comparison.component';
import { GamesListComponent } from './games/games-list.component';
import { GuessBoardComponent } from './guess/guess-board.component';
import { PrepPlayerComponent } from './prep/prep-player.component';
import { EndlessHistoryComponent } from './puzzles/endless-history.component';
import { WorksheetDetailComponent } from './worksheets/worksheet-detail.component';

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
  // a1-rk-games-ultrawide-schmal: vorher fest 1040 px.
  ['Gespeicherte Partien', GamesListComponent, 'games-page'],
  // a1-rk-endless-verlauf-ultrawide: vorher fest 900 px.
  ['Endlos-Verlauf', EndlessHistoryComponent, 'history-container'],
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
  // a1-rk-analyse-unterseiten-ultrawide: Brettseiten Partie-Analyse und „Züge vergleichen".
  ['Partie-Analyse', GameAnalysisDetailComponent, 'gad-container'],
  ['Züge vergleichen', MoveComparisonComponent, 'cmp-page'],
  // a1-rk-browse-breitstufe: Linien durchsehen (Gesamt, Kapitel, Studienbuch) ist eine Brettseite.
  ['Durchsehen', CourseBrowseComponent, 'browse-container'],
  // a1-rk-games-ultrawide-schmal / a1-rk-guess-brett-ultrawide / a1-rk-prep-ultrawide.
  ['Gespeicherte Partien', GamesListComponent, 'games-page'],
  ['Punktepartie-Brett', GuessBoardComponent, 'gb-container'],
  ['Spielervorbereitung', PrepPlayerComponent, 'prep'],
  // a1-rk-trainer-ultrawide-brett: Repertoire-Trainer ist eine Brettseite.
  ['Repertoire-Trainer', RepertoireTrainerComponent, 'trainer'],
  // a1-rk-endless-verlauf-ultrawide (Tabellenseite) / a1-rk-ws-detail-bretter-klein (Aufgabenblatt bearbeiten).
  ['Endlos-Verlauf', EndlessHistoryComponent, 'history-container'],
  ['Aufgabenblatt bearbeiten', WorksheetDetailComponent, 'wd-page'],
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

  it('Repertoireliste: ab 2400 px breitere Kartenspalten (alles-voll-2: 300 px ergaben sechs Spalten, Titel abgeschnitten)', () => {
    const css = compactCss(RepertoireListComponent);
    const at = css.indexOf('@media(min-width:2400px)');
    expect(css.slice(at)).toMatch(/\.repertoire-grid(?:\[[^\]]*\])?\{grid-template-columns:repeat\(auto-fill,minmax\(420px,1fr\)\)/);
    expect(css).toContain('repeat(auto-fill,minmax(300px,1fr))'); // darunter unverändert
  });

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

// a1-rk-analyse-unterseiten-ultrawide: bei 3440 × 1440 stand das Brett mit 520 bzw. 375 px mitten in 3440 px. Ab 2400 px
// wächst es — höchstens bis zur Fensterhöhe.
describe('Partie-Analyse / Züge vergleichen: Brett ab 2400 px bis Fensterhöhe', () => {
  function wide(cmp: Type<unknown>): string {
    const css = compactCss(cmp);
    const at = css.indexOf('@media(min-width:2400px)');
    expect(at).toBeGreaterThan(-1);
    return css.slice(at);
  }

  it('Partie-Analyse: Brettspalte min(1000px, 100vh - 220px), Zugliste 640 px', () => {
    const css = wide(GameAnalysisDetailComponent);
    expect(css).toMatch(/\.board-col(\[[^\]]*\])?\{max-width:min\(1000px,(calc\()?100vh-220px\)?\)/);
    expect(css).toMatch(/\.moves-col(\[[^\]]*\])?\{max-width:640px/);
  });

  it('Züge vergleichen: Brettspalte bis min(900px, 100vh - 260px)', () => {
    expect(wide(MoveComparisonComponent)).toMatch(/grid-template-columns:minmax\(380px,min\(900px,(calc\()?100vh-260px\)?\)\)1fr/);
  });
});
