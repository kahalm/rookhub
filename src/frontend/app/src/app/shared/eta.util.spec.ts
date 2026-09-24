import { TestBed } from '@angular/core/testing';
import { TranslateService, provideTranslateService } from '@ngx-translate/core';
import { formatEta } from './eta.util';

describe('formatEta', () => {
  let translate: TranslateService;

  beforeEach(() => {
    TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'en' })] });
    translate = TestBed.inject(TranslateService);
  });

  it('unter einer Stunde nur Minuten, nie weniger als eine', () => {
    expect(formatEta(11, translate)).toBe('11 min');
    expect(formatEta(0.2, translate)).toBe('1 min');
  });

  it('ab einer Stunde Stunden und Minuten', () => {
    expect(formatEta(90, translate)).toBe('1 h 30 min');
    expect(formatEta(47 * 60 + 59, translate)).toBe('47 h 59 min');
  });

  it('ab zwei Tagen Tage und Stunden, ohne Minuten', () => {
    expect(formatEta(6 * 24 * 60 + 12 * 60 + 7, translate)).toBe('6 common.days 12 h');
  });
});
