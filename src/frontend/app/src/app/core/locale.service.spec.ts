import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LocaleService, resolveStartupLocale, FORMAT_LOCALES } from './locale.service';

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

  it('offers en, de and hr first, plus the worldwide languages', () => {
    const codes = svc.languages.map(l => l.code);
    expect(codes.slice(0, 3)).toEqual(['en', 'de', 'hr']);   // primäre Sprachen zuerst
    expect(codes).toContain('es');                            // + weltweite Sprachen ergänzt (seit 0.79.0)
    expect(codes.length).toBeGreaterThanOrEqual(3);
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

describe('resolveStartupLocale (LOCALE_ID factory)', () => {
  afterEach(() => localStorage.clear());

  it('returns a stored, format-supported language', () => {
    localStorage.setItem('rookhub_lang', 'de');
    expect(resolveStartupLocale()).toBe('de');
  });

  it('falls back to en for a stored language without registered locale data', () => {
    // 'fr' is a supported UI language but not in FORMAT_LOCALES → must not be used as
    // LOCALE_ID (would crash DatePipe with "Missing locale data").
    localStorage.setItem('rookhub_lang', 'fr');
    expect(FORMAT_LOCALES).not.toContain('fr');
    expect(resolveStartupLocale()).toBe('en');
  });

  it('falls back to en when nothing is stored and the browser lang is unsupported', () => {
    localStorage.clear();
    expect(FORMAT_LOCALES.includes(resolveStartupLocale())).toBeTrue();
  });
});
