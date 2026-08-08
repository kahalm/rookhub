import { TestBed } from '@angular/core/testing';
import { MAT_DIALOG_DATA, MatDialogRef } from '@angular/material/dialog';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { CalcGradeDialogComponent, CalcGradeDialogData, CalcGradeDialogResult } from './calc-grade-dialog.component';

/** Dialog mit den übergebenen Daten bauen; `closed` sammelt, womit er geschlossen wurde. */
async function open(data: CalcGradeDialogData) {
  const closed: (CalcGradeDialogResult | undefined)[] = [];
  await TestBed.configureTestingModule({
    imports: [CalcGradeDialogComponent],
    providers: [
      provideNoopAnimations(),
      provideTranslateService({ fallbackLang: 'en' }),
      { provide: MAT_DIALOG_DATA, useValue: data },
      { provide: MatDialogRef, useValue: { close: (v: CalcGradeDialogResult) => closed.push(v) } },
    ],
  }).compileComponents();
  const fixture = TestBed.createComponent(CalcGradeDialogComponent);
  fixture.detectChanges();
  return { fixture, closed, el: fixture.nativeElement as HTMLElement };
}

describe('CalcGradeDialogComponent', () => {
  afterEach(() => TestBed.resetTestingModule());

  it('bietet die fünf Stufen mit ihrer Bedeutung an und schließt mit der gewählten', async () => {
    const { el, closed } = await open({ grade: null, chosenSan: null });

    const options = el.querySelectorAll<HTMLButtonElement>('.cg-option');
    expect(options.length).toBe(5);
    expect(options[2].textContent).toContain('calc.review.grade.moveNoMainLine');

    options[2].click();
    expect(closed).toEqual([2]);
  });

  it('zeigt die FESTLEGUNG mit an, wenn es eine gibt', async () => {
    const { el } = await open({ grade: 3, chosenSan: 'Nd5' });
    const choice = el.querySelector('.cg-choice')!;
    expect(choice.classList).not.toContain('cg-choice--none');
    expect(choice.textContent).toContain('calc.review.dialog.choice');
    // Die bisherige Stufe ist als gewählt markiert.
    expect(el.querySelectorAll('.cg-option--on').length).toBe(1);
  });

  it('sagt es auch, wenn keine Festlegung getroffen wurde', async () => {
    const { el } = await open({ grade: null, chosenSan: null });
    expect(el.querySelector('.cg-choice--none')!.textContent).toContain('calc.review.dialog.noChoice');
    expect(el.querySelectorAll('.cg-option--on').length).toBe(0);
  });

  it('schließt beim ABBRECHEN mit `undefined` — nie mit einem leeren Wert', async () => {
    // Der Knopf-Pfad, nicht ESC/Backdrop: `mat-dialog-close` als STATISCHES Attribut setzt den
    // Input `dialogResult` auf den leeren STRING. Der Aufrufer unterscheidet aber „weggeklickt"
    // (nichts ändern) von `null` („Bewertung entfernen") — ein leerer Wert käme dort als
    // Löschbefehl an und kostete eine bestehende Bewertung.
    const { el, closed } = await open({ grade: 3, chosenSan: 'Nd5' });

    const actions = el.querySelectorAll<HTMLButtonElement>('mat-dialog-actions button');
    actions[actions.length - 1].click();          // der letzte Knopf ist „Abbrechen"

    expect(closed.length).toBe(1);
    expect(closed[0]).toBeUndefined();
    expect(closed[0] as unknown).not.toBe('');
  });

  it('bietet das Zurücknehmen nur an, wenn überhaupt bewertet wurde', async () => {
    const unrated = await open({ grade: null, chosenSan: null });
    expect(unrated.el.querySelector('.cg-clear')).toBeNull();
    TestBed.resetTestingModule();

    const { el, closed } = await open({ grade: 1, chosenSan: null });
    el.querySelector<HTMLButtonElement>('.cg-clear')!.click();
    // `null` = „noch nicht bewertet" und ausdrücklich NICHT Stufe 0 („nicht gelöst").
    expect(closed).toEqual([null]);
  });
});
