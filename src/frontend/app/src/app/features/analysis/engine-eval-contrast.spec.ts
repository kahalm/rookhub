import { AnalysisComponent } from './analysis.component';
import { AnalysisJobsComponent } from './analysis-jobs.component';
import { AnalysisJobViewDialogComponent } from './analysis-job-view-dialog.component';
import { mountWithComponentStyles, setDarkTheme, textContrast } from '../../testing/contrast';

const CARD = 'var(--mat-sys-surface-container-low)';
const DIALOG = 'var(--mat-sys-surface-container-high)';

/**
 * Kontrast der Engine-Bewertungen (Codereview 2026-09-29, UX-015). `.line-eval` trug die Material-900-Toene
 * #1b5e20 / #b71c1c fest — fuer helle Flaechen gemacht, auf der dunklen Karte 2,2:1 bzw. 2,6:1, und der Dunkelmodus
 * ist die Vorgabe. Dieselbe Form (#2e7d32 / #c62828) stand in der Auftragsliste und im Auftrags-Dialog. Gemessen
 * werden die gekapselten Stile der Komponenten auf ihrer Flaeche, hell und dunkel.
 */
describe('Engine-Bewertungen in Theme-Farben: Analysebrett, Auftragsliste, Auftrags-Dialog (UX-015)', () => {
  let wasDark: boolean;
  let dispose: (() => void) | undefined;
  beforeEach(() => { wasDark = document.documentElement.classList.contains('dark-theme'); });
  afterEach(() => { dispose?.(); dispose = undefined; setDarkTheme(wasDark); });

  const LINES = '<div class="lines"><div class="line-row"><span class="line-eval">+0.33</span>'
    + '<span class="line-san">e4 e5</span></div><div class="line-row"><span class="line-eval neg">-1.20</span>'
    + '<span class="line-san">d4</span></div></div>';
  const cases: { name: string; cmp: unknown; surface: string; html: string }[] = [
    { name: 'Analysebrett', cmp: AnalysisComponent, surface: CARD, html: LINES },
    { name: 'Auftragsliste', cmp: AnalysisJobsComponent, surface: CARD,
      html: LINES + '<div class="job-meta"><span class="eval">+0.33</span> <span class="eval neg">-1.20</span></div>' },
    { name: 'Auftrags-Dialog', cmp: AnalysisJobViewDialogComponent, surface: DIALOG, html: LINES },
  ];

  for (const dark of [false, true]) {
    for (const c of cases) {
      it(`${c.name}: Plus- und Minus-Bewertung erreichen 4,5:1 (${dark ? 'dunkel' : 'hell'})`, () => {
        setDarkTheme(dark);
        dispose = mountWithComponentStyles(c.cmp, c.html, c.surface);
        const evals = Array.from(document.querySelectorAll('.line-eval, .eval'));
        expect(evals.length).toBeGreaterThanOrEqual(2);
        for (const el of evals) {
          const ratio = textContrast(el);
          expect(ratio).withContext(`${el.className} „${el.textContent}": ${ratio.toFixed(2)}:1`)
            .toBeGreaterThanOrEqual(4.5);
        }
      });
    }
  }
});
