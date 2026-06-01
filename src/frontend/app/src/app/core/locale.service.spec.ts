import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LocaleService } from './locale.service';

describe('LocaleService', () => {
  let svc: LocaleService;

  beforeEach(() => {
    localStorage.clear();
    TestBed.configureTestingModule({
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    svc = TestBed.inject(LocaleService);
  });

  afterEach(() => localStorage.clear());

  it('offers en, de and hr', () => {
    expect(svc.languages.map(l => l.code)).toEqual(['en', 'de', 'hr']);
  });

  it('use() switches the language and persists it', () => {
    svc.use('de');
    expect(svc.current).toBe('de');
    expect(localStorage.getItem('rookhub_lang')).toBe('de');
  });

  it('init() applies the stored language', () => {
    localStorage.setItem('rookhub_lang', 'hr');
    svc.init();
    expect(svc.current).toBe('hr');
  });

  it('init() falls back to a supported language for an unknown stored value', () => {
    localStorage.setItem('rookhub_lang', 'xx');
    svc.init();
    expect(['en', 'de', 'hr']).toContain(svc.current);
  });
});
