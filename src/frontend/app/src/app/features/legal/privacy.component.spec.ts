import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { PrivacyComponent } from './privacy.component';

describe('PrivacyComponent', () => {
  it('renders (template compiles)', async () => {
    await TestBed.configureTestingModule({
      imports: [PrivacyComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const f = TestBed.createComponent(PrivacyComponent);
    f.detectChanges();
    expect(f.componentInstance).toBeTruthy();
  });
});
