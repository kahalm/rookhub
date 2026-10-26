import { of } from 'rxjs';
import { openCalcGradeDialog, openCalcTimerDialog } from './calc-dialogs';

const dialogReturning = (result: unknown) => ({ open: jasmine.createSpy('open').and.returnValue({ afterClosed: () => of(result) }) }) as any;

describe('Kalkulations-Dialoge', () => {
  it('Ergebnis: Zahl = Stufe, null = zurücknehmen, Fremdwert (leerer String) = nichts', () => {
    for (const [result, calls] of [[3, [3]], [null, [null]], ['', []], [undefined, []]] as const) {
      const onGrade = jasmine.createSpy('onGrade');
      openCalcGradeDialog(dialogReturning(result), { grade: 1, chosenSan: 'Nf3' }, onGrade);
      expect(onGrade.calls.allArgs().map(a => a[0])).toEqual([...calls]);
    }
  });

  it('Kapitelzeit: nur echte Zahlen, abgerundet und nie negativ', () => {
    const onSeconds = jasmine.createSpy('onSeconds');
    openCalcTimerDialog(dialogReturning(61.7), { seconds: 0, chapter: 'K' }, onSeconds);
    openCalcTimerDialog(dialogReturning(-5), { seconds: 0, chapter: 'K' }, onSeconds);
    openCalcTimerDialog(dialogReturning(''), { seconds: 0, chapter: 'K' }, onSeconds);
    expect(onSeconds.calls.allArgs()).toEqual([[61], [0]]);
  });
});
