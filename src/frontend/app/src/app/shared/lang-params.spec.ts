import { HttpParams } from '@angular/common/http';
import { langParams } from './lang-params';

describe('langParams', () => {
  it('setzt lang nur mit Sprache', () => {
    expect(langParams('de').toString()).toBe('lang=de');
    expect(langParams(null).keys()).toEqual([]);
    expect(langParams(undefined).keys()).toEqual([]);
    expect(langParams('').keys()).toEqual([]);
  });

  it('hängt lang an die übrigen Parameter an, ohne Sprache bleiben sie unverändert', () => {
    const base = new HttpParams().set('chapter', '3');
    expect(langParams('hr', base).toString()).toBe('chapter=3&lang=hr');
    expect(langParams(null, base)).toBe(base);
  });
});
