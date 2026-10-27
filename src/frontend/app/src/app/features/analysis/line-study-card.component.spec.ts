import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LineStudyCardComponent } from './line-study-card.component';

// 0.859.0, Wunsch 2026-10-27: „es soll einen Knopf geben, wo ich die Engine wieder wegschalte"
describe('LineStudyCardComponent', () => {
  function make(revealed: boolean, hidden: boolean) {
    TestBed.configureTestingModule({ imports: [LineStudyCardComponent], providers: [provideTranslateService({ fallbackLang: 'de' })] });
    const f = TestBed.createComponent(LineStudyCardComponent);
    f.componentRef.setInput('revealed', revealed);
    f.componentRef.setInput('hidden', hidden);
    f.detectChanges();
    return f;
  }

  it('vor dem Aufdecken nur „Engine einschalten"', () => {
    const el = make(false, false).nativeElement as HTMLElement;
    expect(el.querySelector('.reveal')?.textContent).toContain('analysis.lineStudy.reveal');
    expect(el.querySelector('.hide')).toBeNull();
  });

  it('aufgedeckt „Engine ausschalten", weggeschaltet wieder „Engine einschalten"', () => {
    const f = make(true, false);
    const hide = jasmine.createSpy('hide');
    f.componentInstance.hide.subscribe(hide);
    const btn = (f.nativeElement as HTMLElement).querySelector('.hide') as HTMLButtonElement;
    expect(btn.textContent).toContain('analysis.lineStudy.hide');
    btn.click();
    expect(hide).toHaveBeenCalled();
    f.componentRef.setInput('hidden', true);
    f.detectChanges();
    const el = f.nativeElement as HTMLElement;
    expect(el.querySelector('.hide')).toBeNull();
    expect(el.querySelector('.reveal')?.textContent).toContain('analysis.lineStudy.reveal');
    expect(el.querySelector('.label')?.textContent).toContain('analysis.lineStudy.title');
  });
});
