import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { provideTranslateService } from '@ngx-translate/core';
import { ImpressumComponent } from './impressum.component';

describe('ImpressumComponent', () => {
  it('renders (template compiles)', async () => {
    await TestBed.configureTestingModule({
      imports: [ImpressumComponent],
      providers: [provideRouter([]), provideTranslateService({ fallbackLang: 'en' })],
    }).compileComponents();
    const f = TestBed.createComponent(ImpressumComponent);
    f.detectChanges();
    expect(f.componentInstance).toBeTruthy();
  });
});
