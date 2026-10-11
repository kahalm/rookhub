import { EnvironmentInjector, runInInjectionContext } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { SanPipe } from './san.pipe';

describe('SanPipe', () => {
  const make = () => runInInjectionContext(TestBed.inject(EnvironmentInjector), () => new SanPipe());

  it('without TranslateService the move stays English', () => {
    TestBed.configureTestingModule({});
    const p = make();
    expect(p.transform('Nf3')).toBe('Nf3');
    expect(p.transform('1. e4 c5 2. Nf3', 'text')).toBe('1. e4 c5 2. Nf3');
    expect(p.transform(null)).toBe('');
  });

  it('German: single moves and whole lines in German piece letters', () => {
    TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'en' })] });
    TestBed.inject(TranslateService).use('de');
    const p = make();
    expect(p.transform('Nf3')).toBe('Sf3');
    expect(p.transform('e8=Q+')).toBe('e8=D+');
    expect(p.transform('1. e4 c5 2. Nf3 e6 3. Bb5', 'text')).toBe('1. e4 c5 2. Sf3 e6 3. Lb5');
    expect(p.transform('12...Nc6', 'text')).toBe('12...Sc6');
  });

  it('English UI keeps English letters', () => {
    TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'en' })] });
    TestBed.inject(TranslateService).use('en');
    expect(make().transform('Nf3')).toBe('Nf3');
  });
});
