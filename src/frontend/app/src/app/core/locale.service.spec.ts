import { TestBed } from '@angular/core/testing';
import { provideTranslateService } from '@ngx-translate/core';
import { LocaleService, resolveStartupLocale, FORMAT_LOCALES } from './locale.service';
import { sharedCookieDomain } from './partner-site';

const LANG_COOKIE = 'rookhub_lang';

function clearSharedLang(): void {
  document.cookie = `${LANG_COOKIE}=; path=/; max-age=0`;
  const domain = sharedCookieDomain();
  if (domain) document.cookie = `${LANG_COOKIE}=; domain=${domain}; path=/; max-age=0`;
}

describe('LocaleService', () => {
  let svc: LocaleService;

  beforeEach(() => {
    localStorage.clear();
    clearSharedLang();
    TestBed.configureTestingModule({
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    svc = TestBed.inject(LocaleService);
  });

  afterEach(() => {
    localStorage.clear();
    clearSharedLang();
  });

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
  afterEach(() => {
    localStorage.clear();
    clearSharedLang();
  });

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
  /**
   * Dieselbe Sprache auf BEIDEN Oberflaechen. RookHub und die Turnierseite sind zwei Origins und
   * teilen den localStorage nicht — wer hier Deutsch waehlte, sass dort weiter in Englisch.
   */
  it('legt die Sprachwahl als geteiltes Cookie ab', () => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const svc = TestBed.inject(LocaleService);

    svc.use('de');

    // Nur wo es eine gemeinsame Elterndomaene GIBT (in Karma laeuft es auf localhost, dort
    // bewusst nicht) — geprueft wird deshalb die Entscheidung, nicht das Cookie selbst.
    if (sharedCookieDomain()) {
      expect(document.cookie).toContain(`${LANG_COOKIE}=de`);
    } else {
      expect(document.cookie).not.toContain(`${LANG_COOKIE}=de`);
    }
    // Der geraetelokale Wert traegt in JEDEM Fall — sonst ueberlebt die Wahl ohne gemeinsame
    // Domaene keinen Seitenaufruf.
    expect(localStorage.getItem('rookhub_lang')).toBe('de');
  });

  /**
   * Die geteilte Wahl hat Vorrang vor dem geraetelokalen Wert: sie ist die, die der Nutzer
   * zuletzt auf EINER der beiden Seiten getroffen hat.
   */
  it('nimmt beim Start die geteilte Wahl vor dem lokalen Wert', () => {
    localStorage.setItem('rookhub_lang', 'en');
    document.cookie = `${LANG_COOKIE}=hr; path=/`;

    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [provideTranslateService({ fallbackLang: 'en' })],
    });
    const fresh = TestBed.inject(LocaleService);
    fresh.init();

    expect(fresh.current).toBe('hr');
    // Und dieselbe Reihenfolge fuer die Zahlen-/Datumsformate beim Bootstrap.
    document.cookie = `${LANG_COOKIE}=de; path=/`;
    expect(resolveStartupLocale()).toBe('de');
  });
});
