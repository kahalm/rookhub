import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { CalcResultCardComponent } from './calc-result-card.component';

describe('CalcResultCardComponent', () => {
  async function render(inputs: Record<string, unknown>) {
    await TestBed.configureTestingModule({
      imports: [CalcResultCardComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const fixture = TestBed.createComponent(CalcResultCardComponent);
    for (const [k, v] of Object.entries(inputs)) fixture.componentRef.setInput(k, v);
    fixture.detectChanges();
    return { c: fixture.componentInstance, el: fixture.nativeElement as HTMLElement };
  }

  it('unbewertet: „Ergebnis" ohne Punkte und ohne Analyse-Knopf', async () => {
    const { el } = await render({ gradeLabel: 'Ergebnis', positionTime: '0:00' });
    expect(el.querySelector('.calc-result-label')!.textContent!.trim()).toBe('Ergebnis');
    expect(el.querySelector('.calc-review-score')).toBeNull();
    expect(el.querySelector('.calc-analyze-btn')).toBeNull();
    expect(el.querySelector('.calc-review-chip--set')).toBeNull();
  });

  it('bewertet: Stufe, Punkte, Analyse-Knopf; Klicks gehen als Ereignis raus', async () => {
    const { c, el } = await render({ chosenSan: 'Nxe5', graded: true, gradeLabel: 'Gelöst', score: '4 / 4', positionTime: '1:05' });
    expect(el.querySelector('.calc-review-score')!.textContent!.trim()).toBe('4 / 4');
    expect(el.querySelector('.calc-review-time')!.textContent!.trim()).toBe('1:05');
    expect(el.querySelector('.calc-review-chip--set')!.textContent).toContain('Nxe5');
    const log: string[] = [];
    c.openGrade.subscribe(() => log.push('grade'));
    c.analyze.subscribe(() => log.push('analyze'));
    (el.querySelector('.calc-result-btn') as HTMLButtonElement).click();
    (el.querySelector('.calc-analyze-btn') as HTMLButtonElement).click();
    expect(log).toEqual(['grade', 'analyze']);
  });
});
