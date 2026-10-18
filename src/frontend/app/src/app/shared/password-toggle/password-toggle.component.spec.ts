import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { PasswordToggleComponent } from './password-toggle.component';

describe('PasswordToggleComponent', () => {
  it('schaltet zwischen password und text, mit aria-pressed und passender Beschriftung', () => {
    TestBed.configureTestingModule({
      imports: [PasswordToggleComponent],
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const fixture = TestBed.createComponent(PasswordToggleComponent);
    fixture.detectChanges();
    const c = fixture.componentInstance;
    const btn = (fixture.nativeElement as HTMLElement).querySelector('button') as HTMLButtonElement;
    expect(c.type()).toBe('password');
    expect(btn.type).toBe('button');
    expect(btn.getAttribute('aria-label')).toBe('auth.passwordToggle.show');
    btn.click();
    fixture.detectChanges();
    expect(c.type()).toBe('text');
    expect(btn.getAttribute('aria-pressed')).toBe('true');
    expect(btn.getAttribute('aria-label')).toBe('auth.passwordToggle.hide');
    expect(btn.textContent).toContain('visibility_off');
  });
});
