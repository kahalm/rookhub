import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideTranslateService } from '@ngx-translate/core';
import { SolveModeDialogComponent, SolveModeDialogData } from './solve-mode-dialog.component';
import { mountWithComponentStyles, setDarkTheme, textContrast } from '../../testing/contrast';

// UX-004: „für alle Puzzle-Bereiche übernehmen“ — sonst kam dieselbe Frage in jedem Bereich einzeln.
describe('SolveModeDialogComponent', () => {
  function render(data: SolveModeDialogData): { el: HTMLElement; close: jasmine.Spy } {
    const close = jasmine.createSpy('close');
    TestBed.configureTestingModule({
      imports: [SolveModeDialogComponent],
      providers: [
        provideTranslateService({ fallbackLang: 'en' }),
        { provide: MatDialogRef, useValue: { close } },
        { provide: MAT_DIALOG_DATA, useValue: data },
      ],
    });
    const fixture = TestBed.createComponent(SolveModeDialogComponent);
    fixture.detectChanges();
    return { el: fixture.nativeElement as HTMLElement, close };
  }
  const choices = (el: HTMLElement) => el.querySelectorAll<HTMLButtonElement>('.sm-choice');

  it('bietet „für alle“ an (voreingestellt an) und meldet es mit der Wahl', () => {
    const { el, close } = render({ offerApplyAll: true });
    const box = el.querySelector<HTMLInputElement>('.sm-all input[type=checkbox]')!;
    expect(box.checked).toBeTrue();
    choices(el)[1].click();
    expect(close).toHaveBeenCalledWith({ mode: 'easy', applyAll: true });
  });

  it('Häkchen weg → gilt nur für diesen Bereich', () => {
    const { el, close } = render({ offerApplyAll: true });
    el.querySelector<HTMLInputElement>('.sm-all input[type=checkbox]')!.click();
    choices(el)[0].click();
    expect(close).toHaveBeenCalledWith({ mode: 'training', applyAll: false });
  });

  it('ohne Angebot (Kurs) kein Häkchen und kein „für alle“', () => {
    const { el, close } = render({});
    expect(el.querySelector('.sm-all')).toBeNull();
    choices(el)[0].click();
    expect(close).toHaveBeenCalledWith({ mode: 'training', applyAll: false });
  });

  it('neues Design (UI-Sweep 10.10.): Titel ohne Leerraum davor, zwei Kacheln mit Symbol, Texte unverändert', () => {
    const { el } = render({});
    expect(el.querySelector('h2')!.textContent).toBe('solveMode.title');
    expect(el.querySelectorAll('.sm-choices > .sm-choice').length).toBe(2);
    choices(el).forEach(c => {
      expect(c.querySelector('.sm-icon mat-icon')).not.toBeNull();
      expect(c.querySelector('.sm-name')).not.toBeNull();
      expect(c.querySelector('.sm-desc')).not.toBeNull();
    });
  });

  // UI-Sweep 2026-10-11 (r2-rk-weekly-mode-dialog): der Wochenpost bringt Titel und Hinweis mit, die Kacheln bleiben gleich.
  it('nimmt eigenen Titel und Hinweis, Kachel-Texte bleiben die gemeinsamen', () => {
    const { el } = render({ title: 'Wochenpost-Titel', intro: 'Gilt für diesen Wochenpost.', hint: 'Ab dem nächsten Puzzle.' });
    expect(el.querySelector('h2')!.textContent).toBe('Wochenpost-Titel');
    expect(el.querySelector('.sm-intro')!.textContent).toBe('Gilt für diesen Wochenpost.');
    expect(el.querySelector('.sm-hint')!.textContent).toBe('Ab dem nächsten Puzzle.');
    expect(choices(el)[0].querySelector('.sm-desc')!.textContent).toBe('solveMode.trainingDesc');
    expect(choices(el)[1].querySelector('.sm-desc')!.textContent).toBe('solveMode.easyDesc');
  });
});

/**
 * UI-Sweep a1 (a1-rk-spielweise-dialog-hell): Untertitel, Einleitung, Kachelbeschreibungen und Hinweis lagen im hellen
 * Design bei ≈ 3,4:1 — currentColor ist im Dialog schon der gedimmte Dialogtext, 60–75 % davon reichten nicht. Gemessen
 * wie im Dialog: Fläche surface-container-high, Textfarbe on-surface-variant, Kachel mit ihrer Tönung.
 */
describe('SolveModeDialogComponent Kontrast', () => {
  let wasDark: boolean;
  let dispose: (() => void) | undefined;
  beforeEach(() => { wasDark = document.documentElement.classList.contains('dark-theme'); });
  afterEach(() => { dispose?.(); dispose = undefined; setDarkTheme(wasDark); });

  const HTML = '<div style="color: var(--mat-sys-on-surface-variant)">'
    + '<p class="sm-subtitle">Buch</p><p class="sm-intro">Einmalig</p>'
    + '<div class="sm-choices"><button type="button" class="sm-choice"><span class="sm-name">Training</span>'
    + '<span class="sm-desc">Die Figuren bleiben stehen.</span></button></div>'
    + '<p class="sm-hint">Umschalten über ⋮</p></div>';

  for (const dark of [false, true]) {
    for (const target of ['.sm-subtitle', '.sm-intro', '.sm-name', '.sm-desc', '.sm-hint']) {
      it(`${target} erreicht 4,5:1 (${dark ? 'dunkel' : 'hell'})`, () => {
        setDarkTheme(dark);
        dispose = mountWithComponentStyles(SolveModeDialogComponent, HTML, 'var(--mat-sys-surface-container-high)');
        const ratio = textContrast(document.querySelector(target)!);
        expect(ratio).withContext(`${target}: ${ratio.toFixed(2)}:1`).toBeGreaterThanOrEqual(4.5);
      });
    }
  }
});
