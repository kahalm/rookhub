import { HttpParams } from '@angular/common/http';

/**
 * `?lang=` nur setzen, wenn es eine Sprache gibt — ohne `lang` liefert der Server exakt das Original.
 * Die EINE Fassung dieser Regel für die Kurs- und Kalkulations-Endpunkte (Codereview F3-020); `base` trägt
 * die übrigen Parameter, `lang` kommt stets zuletzt dazu.
 */
export function courseLangParams(lang?: string | null, base: HttpParams = new HttpParams()): HttpParams {
  return lang ? base.set('lang', lang) : base;
}
