import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { AnalysisEngineCardComponent, arrowBrush } from './analysis-engine-card.component';

describe('AnalysisEngineCardComponent', () => {
  // ngModel schreibt den Wert erst asynchron in Schalter und Auswahl — deshalb whenStable vor dem Prüfen.
  async function render(set: Partial<AnalysisEngineCardComponent>) {
    TestBed.configureTestingModule({
      imports: [AnalysisEngineCardComponent],
      providers: [provideNoopAnimations(), provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(AnalysisEngineCardComponent);
    Object.assign(fixture.componentInstance, set);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
    return fixture;
  }

  it('zeigt die Linien mit dem Farbpunkt ihres Pfeils und die Vergleichs-Engine', async () => {
    const fixture = await render({
      displayLines: [{ evalText: '+0.33', san: 'e4 e5', positive: true }, { evalText: '-1.20', san: 'd4', positive: false }],
      compareOn: true, compareRunning: true, compareEngineName: 'RookHub PC', mainEngineName: 'Browser',
      compareLines: [{ evalText: '+0.10', san: 'c4', positive: true }],
    });
    const el = fixture.nativeElement as HTMLElement;
    const marks = el.querySelectorAll<HTMLElement>('.line-mark');
    expect(marks.length).toBe(2);                                   // nur die Haupt-Engine hat Pfeile
    expect(marks[0].style.background).not.toBe('');
    // d5-rk-analyse-pfeile-nur-farbe: die Marke trägt die Liniennummer (wie die Beschriftung des Pfeils).
    expect(Array.from(marks).map(m => m.textContent!.trim())).toEqual(['1', '2']);
    expect(getComputedStyle(marks[0]).width).toBe('16px');
    expect(el.querySelector('.line-eval.neg')?.textContent).toBe('-1.20');
    expect(el.querySelector('.cmp-block')?.textContent).toContain('RookHub PC');
    expect(fixture.componentInstance.lineColor(4)).toMatch(/^rgba\(/);   // 'purple' deckt nur teilweise
    expect(arrowBrush(9)).toBe('blue');
  });

  it('Partie-Ende statt Linien; im Sparring ist der Schalter gesperrt', async () => {
    const fixture = await render({ terminal: 'stalemate', terminalText: 'Patt', sparring: true });
    const el = fixture.nativeElement as HTMLElement;
    expect(el.querySelector('.terminal-state')?.textContent).toContain('Patt');
    expect(el.querySelector('.lines')).toBeNull();
    expect(el.querySelector('mat-slide-toggle button')?.hasAttribute('disabled')
      || el.querySelector('mat-slide-toggle')?.classList.contains('mat-mdc-slide-toggle-disabled')).toBeTrue();
  });

  it('meldet Schalter und Vergleichsknopf als Ereignis', async () => {
    const fixture = await render({ engines: [{ id: 'x', name: 'PC' } as never] });
    const c = fixture.componentInstance;
    const toggled: boolean[] = [];
    let compare = 0;
    c.engineToggle.subscribe(v => toggled.push(v));
    c.compareToggle.subscribe(() => compare++);
    const el = fixture.nativeElement as HTMLElement;
    (el.querySelector('mat-slide-toggle button') as HTMLButtonElement).click();
    (el.querySelector('.cmp-btn') as HTMLButtonElement).click();
    expect(toggled).toEqual([false]);
    expect(compare).toBe(1);
  });
});
