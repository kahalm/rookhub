import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { PuzzleSettingsDialogComponent, PuzzleSettingsDialogData } from './puzzle-settings-dialog.component';
import { mountWithComponentStyles, setDarkTheme, textContrast } from '../../testing/contrast';

describe('PuzzleSettingsDialogComponent', () => {
  function create(overrides: Partial<PuzzleSettingsDialogData> = {}): PuzzleSettingsDialogComponent {
    const data: PuzzleSettingsDialogData = {
      mode: 'standard',
      boardTheme: 'brown',
      pieceSet: 'cburnett',
      themeMode: 'fixed',
      visualizationMode: 0,
      vizArrowEnabled: true,
      ...overrides,
    };
    const dialogRef = {} as MatDialogRef<PuzzleSettingsDialogComponent>;
    return new PuzzleSettingsDialogComponent(dialogRef, data);
  }

  it('vizLevelOptions liefert i18n-Keys statt hartcodierter Texte', () => {
    const opts = create().vizLevelOptions;
    expect(opts.length).toBe(5);
    opts.forEach((opt, i) => {
      expect(opt.value).toBe(i);
      expect(opt.label).toBe(`puzzles.viz.level${i}Name`);
      expect(opt.description).toBe(`puzzles.viz.level${i}Desc`);
      // darf keinen rohen Anzeigetext mehr enthalten (regressionsschutz gegen de/en-Mischstrings)
      expect(opt.description).not.toContain(' ');
    });
  });

  it('difficultyInfoOptions liefert i18n-Keys für Label und Beschreibung', () => {
    const opts = create().difficultyInfoOptions;
    expect(opts.length).toBe(5);
    opts.forEach(opt => {
      expect(opt.label.startsWith('puzzles.difficulty.')).toBeTrue();
      expect(opt.description.startsWith('puzzles.difficulty.')).toBeTrue();
      expect(opt.description.endsWith('Desc')).toBeTrue();
      expect(opt.description).not.toContain(' ');
    });
  });
  describe('DOM', () => {
    function render(overrides: Partial<PuzzleSettingsDialogData> = {}) {
      TestBed.configureTestingModule({
        imports: [PuzzleSettingsDialogComponent],
        providers: [
          provideNoopAnimations(),
          provideTranslateService({ fallbackLang: 'en' }),
          { provide: MatDialogRef, useValue: { close: () => {} } },
          { provide: MAT_DIALOG_DATA, useValue: {
            mode: 'standard', boardTheme: 'brown', pieceSet: 'cburnett', themeMode: 'fixed',
            visualizationMode: 0, vizArrowEnabled: true, ...overrides } },
        ],
      });
      const fixture = TestBed.createComponent(PuzzleSettingsDialogComponent);
      fixture.detectChanges();
      return { fixture, el: fixture.nativeElement as HTMLElement };
    }
    afterEach(() => TestBed.resetTestingModule());

    it('Titel ist ein schlichtes h2 mat-dialog-title ohne ✕ — unten steht „Abbrechen“ (d7-rk-dialog-titel-uneinheitlich)', () => {
      const { el } = render();
      const title = el.querySelector('[mat-dialog-title]')!;
      expect(title.tagName).toBe('H2');
      expect(title.querySelector('button')).toBeNull();
      expect(el.querySelector('button[mat-dialog-close] mat-icon')).toBeNull();
      expect(el.querySelectorAll('mat-dialog-actions button').length).toBe(2);
    });

    it('Erklär-Knöpfe zeigen das Standard-? statt ⓘ, mit Namen; die Rechentiefe erklärt sich in einer Unterzeile (d9-rk-einstellungen-info-statt-hilfesymbol)', () => {
      const { fixture, el } = render();
      const icons = Array.from(el.querySelectorAll('mat-icon')).map(i => i.textContent!.trim());
      expect(icons).not.toContain('info_outline');
      const btns = Array.from(el.querySelectorAll<HTMLButtonElement>('button.psd-info-btn'));
      expect(btns.length).toBe(2);   // Visualisierung + Schwierigkeit (Standard-Modus)
      for (const b of btns) {
        expect(b.querySelector('mat-icon')!.textContent!.trim()).toBe('help_outline');
        expect(b.getAttribute('aria-label')).toBe('puzzles.settings.showExplanation');
        expect(b.getAttribute('aria-expanded')).toBe('false');
      }
      btns[0].click();
      fixture.detectChanges();
      expect(btns[0].getAttribute('aria-expanded')).toBe('true');
      const depthRow = Array.from(el.querySelectorAll('.psd-label')).find(l => l.textContent!.includes('puzzles.settings.stockfishDepth'))!;
      expect(depthRow.querySelector('.psd-sublabel')!.textContent).toContain('puzzles.settings.stockfishDepthHint');
    });

    it('Inhalt trägt den Weiterscroll-Schatten (d7-rk-einstellungen-dialog-scroll-ohne-hinweis)', () => {
      const { el } = render();
      const content = el.querySelector<HTMLElement>('mat-dialog-content')!;
      expect(content.classList).toContain('rh-scroll-hint');   // globale Klasse (styles.scss, DESIGN 5.6)
      const bg = getComputedStyle(content).backgroundImage;
      expect(bg).toContain('linear-gradient');
      expect(bg).toContain('radial-gradient');
      expect(getComputedStyle(content).backgroundAttachment).toContain('local');
    });

    it('Figuren und Brett-Designs zeigen übersetzte Namen (d7-rk-einstellungen-englische-optionswerte)', async () => {
      const { fixture, el } = render();
      const translate = TestBed.inject(TranslateService);
      translate.setTranslation('de', { puzzles: { theme: { board: { brown: 'Braun' }, piece: { cburnett: 'Klassisch' } } } });
      translate.use('de');
      fixture.detectChanges();
      await fixture.whenStable();
      fixture.detectChanges();
      const values = Array.from(el.querySelectorAll('mat-select')).map(s => s.textContent!.trim());
      expect(values[0]).toBe('Klassisch');
      expect(values[1]).toBe('Braun');
    });
  });

  // UI-Sweep 2026-11-10 (d10-rk-puzzle-einstellungen-zahlenfeld-klein, d10-rk-puzzle-einstellungen-unterzeile-blass)
  describe('Zahlenfelder und Unterzeilen (UI-Sweep 2026-11-10)', () => {
    const DIALOG = 'var(--mat-sys-surface-container-high)';
    const HTML = '<div class="psd-row"><span class="psd-label">Rechentiefe'
      + '<span class="psd-sublabel">Höher = stärkere Antworten, dauert aber länger.</span></span>'
      + '<input class="psd-number-input" type="number" value="16"></div>'
      + '<div class="psd-viz-info"><div class="psd-viz-info-row"><span class="psd-viz-name">Stufe</span>'
      + '<span class="psd-viz-desc">Beschreibung der Stufe</span></div></div>';
    let wasDark: boolean;
    let dispose: (() => void) | undefined;
    beforeEach(() => { wasDark = document.documentElement.classList.contains('dark-theme'); });
    afterEach(() => { dispose?.(); dispose = undefined; setDarkTheme(wasDark); });

    it('Zahlenfelder sind mindestens 44 px hoch, Schrift 16 px', () => {
      dispose = mountWithComponentStyles(PuzzleSettingsDialogComponent, HTML, DIALOG);
      const input = document.querySelector<HTMLInputElement>('.psd-number-input')!;
      expect(input.getBoundingClientRect().height).toBeGreaterThanOrEqual(44);
      expect(getComputedStyle(input).fontSize).toBe('16px');
    });

    for (const dark of [false, true]) {
      it(`Unterzeile und Stufenbeschreibung ohne Deckkraft, mindestens 4,5:1 (${dark ? 'dunkel' : 'hell'})`, () => {
        setDarkTheme(dark);
        dispose = mountWithComponentStyles(PuzzleSettingsDialogComponent, HTML, DIALOG);
        for (const sel of ['.psd-sublabel', '.psd-viz-desc']) {
          const el = document.querySelector(sel)!;
          expect(getComputedStyle(el).opacity).withContext(sel).toBe('1');
          expect(textContrast(el)).withContext(sel).toBeGreaterThanOrEqual(4.5);
        }
      });
    }
  });
});
