import { TestBed } from '@angular/core/testing';
import { provideNoopAnimations } from '@angular/platform-browser/animations';
import { HelpHintComponent } from './help-hint.component';

/** Hilfe-Icon (UI-Welle 2): Erklärtext liegt im Tooltip, Klick toggelt ihn (Touch ohne Hover). */
describe('HelpHintComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HelpHintComponent],
      providers: [provideNoopAnimations()],
    });
  });

  it('rendert das Icon und toggelt den Tooltip per Klick', () => {
    const fixture = TestBed.createComponent(HelpHintComponent);
    fixture.componentRef.setInput('text', 'Erklärung Satz 1.\n\nSatz 2.');
    fixture.detectChanges();
    const btn: HTMLButtonElement = fixture.nativeElement.querySelector('.hh-btn');
    expect(btn).toBeTruthy();
    expect(btn.getAttribute('aria-label')).toContain('Erklärung');

    btn.click();
    fixture.detectChanges();
    expect(document.querySelector('.hh-tooltip')).toBeTruthy();   // Tooltip offen

    btn.click();
    fixture.detectChanges();
  });
});
