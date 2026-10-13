import { Pipe, PipeTransform, inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';
import { FORMAT_LOCALES } from '../../core/locale.service';

/**
 * Prozent in der Schreibweise der Sprache (DESIGN 5.13, e1-rk-percent-space-en): en/hu „46%“, de/hr „46 %“ mit
 * geschütztem Leerzeichen — über `Intl.NumberFormat(style: 'percent')`, nie ein festes „ %“ anhängen.
 *
 * `value` in Prozentpunkten (46 → „46%“). `maxDigits`/`minDigits` = Nachkommastellen (Vorgabe: ganze Zahl). Sprachen
 * ohne registrierte Locale-Daten (alles außer `FORMAT_LOCALES`) schreiben englisch; ohne `lang` gilt die Sprache der
 * Oberfläche (`<html lang>`, gesetzt von `LocaleService`).
 */
export function formatPct(value: number, lang?: string | null, maxDigits = 0, minDigits = 0): string {
  return new Intl.NumberFormat(pctLocale(lang ?? documentLang()),
    { style: 'percent', minimumFractionDigits: minDigits, maximumFractionDigits: Math.max(minDigits, maxDigits) })
    .format(value / 100);
}

/** Die Locale, in der Prozent geschrieben werden: nur die gepflegten Sprachen, sonst englisch. */
export function pctLocale(lang: string | null | undefined): string {
  return lang && FORMAT_LOCALES.includes(lang) ? lang : 'en';
}

function documentLang(): string {
  return (typeof document !== 'undefined' && document.documentElement?.lang) || 'en';
}

/**
 * `{{ x | pct }}`, `{{ x | pct:1 }}` (höchstens 1 Stelle), `{{ x | pct:1:1 }}` (genau 1 Stelle). `null`/`undefined` →
 * leer. Unrein wie `SanPipe`, weil die Sprache zur Laufzeit wechselt.
 */
@Pipe({ name: 'pct', standalone: true, pure: false })
export class PctPipe implements PipeTransform {
  private readonly translate = inject(TranslateService, { optional: true });

  transform(value: number | null | undefined, maxDigits = 0, minDigits = 0): string {
    if (value == null || !Number.isFinite(value)) return '';
    return formatPct(value, this.translate?.currentLang() || null, maxDigits, minDigits);
  }
}
