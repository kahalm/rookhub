import { TestBed } from '@angular/core/testing';
import { provideTranslateService, TranslateService } from '@ngx-translate/core';
import { cardPct } from '../player-card/card-lang';
import { PctPipe, formatPct, pctLocale } from './pct';

/** e1-rk-percent-space-en (DESIGN 5.13): Prozent je Sprache — en/hu ohne Leerzeichen, de/hr mit geschütztem. */
describe('formatPct / PctPipe', () => {
  it('schreibt Prozent in der Schreibweise der Sprache', () => {
    expect(formatPct(46, 'en')).toBe('46%');
    expect(formatPct(46, 'hu')).toBe('46%');
    expect(formatPct(46, 'de')).toBe('46 %');
    expect(formatPct(46, 'hr')).toBe('46 %');
    expect(formatPct(95.5, 'en', 1)).toBe('95.5%');
    expect(formatPct(95.5, 'de', 1, 1)).toBe('95,5 %');
    expect(formatPct(95, 'en', 1, 1)).toBe('95.0%');
  });

  it('nicht gepflegte Sprachen schreiben englisch', () => {
    expect(pctLocale('fr')).toBe('en');
    expect(pctLocale(null)).toBe('en');
    expect(formatPct(12, 'fr')).toBe('12%');
  });

  it('ohne Sprache gilt <html lang> (die Sprache der Oberfläche, nicht die des Browsers)', () => {
    const el = document.documentElement;
    const before = el.lang;
    try {
      el.lang = 'de';
      expect(formatPct(7)).toBe('7 %');
      el.lang = 'en';
      expect(formatPct(7)).toBe('7%');
    } finally { el.lang = before; }
  });

  it('cardPct der Spielerkarte nutzt denselben Helfer', () => {
    expect(cardPct(39, 'en')).toBe(formatPct(39, 'en'));
    expect(cardPct(39, 'de')).toBe('39 %');
  });

  it('Pipe: Sprache der Oberfläche, leer bei null', () => {
    TestBed.configureTestingModule({ providers: [provideTranslateService({ fallbackLang: 'en' }), PctPipe] });
    const pipe = TestBed.inject(PctPipe);
    const translate = TestBed.inject(TranslateService);
    translate.use('en');
    expect(pipe.transform(87.4)).toBe('87%');
    expect(pipe.transform(87.4, 1, 1)).toBe('87.4%');
    translate.use('de');
    expect(pipe.transform(87.4, 1, 1)).toBe('87,4 %');
    expect(pipe.transform(null)).toBe('');
    expect(pipe.transform(undefined)).toBe('');
  });
});
