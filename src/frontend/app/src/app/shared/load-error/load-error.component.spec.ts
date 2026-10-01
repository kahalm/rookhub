import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LoadErrorComponent } from './load-error.component';

describe('LoadErrorComponent', () => {
  function render(messageKey?: string) {
    TestBed.configureTestingModule({
      imports: [LoadErrorComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(LoadErrorComponent);
    if (messageKey) fixture.componentRef.setInput('messageKey', messageKey);
    fixture.detectChanges();
    return fixture;
  }

  it('zeigt die Standardmeldung und meldet den Erneut-Klick', () => {
    const fixture = render();
    const el: HTMLElement = fixture.nativeElement;
    expect(el.querySelector('[role="alert"]')?.textContent).toContain('common.loadFailed');
    let retried = 0;
    fixture.componentInstance.retry.subscribe(() => retried++);
    (el.querySelector('button') as HTMLButtonElement).click();
    expect(retried).toBe(1);
  });

  it('nimmt einen eigenen Meldungsschluessel', () => {
    const el: HTMLElement = render('x.custom').nativeElement;
    expect(el.textContent).toContain('x.custom');
  });
});
