import { formatDate } from '@angular/common';
import { TranslateService } from '@ngx-translate/core';
import { FORMAT_LOCALES } from '@rh/core/locale.service';
import { formatPct } from '@rh/shared/format/pct';
import { localizeSan, localizeSanText } from '@rh/shared/chess/keyboard-move.util';

/**
 * Sprache der Spielerkarte (e1-rk-prep-card-german): in RookHub (Spielervorbereitung) die der Oberfläche, in LeagueHub
 * deutsch — LeagueHub stellt beim Start `de` ein (`LocaleService.applyUnsaved('de')`), die Karte folgt damit derselben
 * Quelle. Ohne gesetzte Sprache (Specs ohne Übersetzung) bleibt sie deutsch wie vor der Umstellung.
 */
export function cardLang(translate: TranslateService | null | undefined): string {
  return translate?.getCurrentLang() || 'de';
}

/** Sprache für Zahlen und Daten: nur die registrierten Locale-Daten (en/de/hr/hu), sonst englisch. */
export function cardFormatLocale(lang: string): string {
  return FORMAT_LOCALES.includes(lang) ? lang : 'en';
}

/** Zugfolge („1.e4 c5 2.Nf3") in den Figurenbuchstaben der Sprache — „1.e4 c5 2.Sf3" auf Deutsch/Kroatisch. */
export function cardSanText(text: string | null | undefined, lang: string): string {
  return localizeSanText(String(text ?? ''), lang);
}

/** Ein einzelner Zug in den Figurenbuchstaben der Sprache. */
export function cardSan(san: string, lang: string): string {
  return localizeSan(san, lang);
}

/**
 * Prozent in der Schreibweise der Sprache: en/hu „39%", de/hr „39 %" (geschütztes Leerzeichen). `digits` = höchstens so
 * viele Nachkommastellen (0 → ganze Zahl). Gemeinsamer Helfer: `formatPct` (`shared/format/pct.ts`).
 */
export function cardPct(value: number, lang: string, digits = 0): string {
  return formatPct(value, cardFormatLocale(lang), digits);
}

/**
 * PGN-Datum („2024.05.01") als Datum der Sprache (`mediumDate`: de „01.05.2024", en „May 1, 2024"). Unvollständige
 * Angaben („2025.??.??") → nur das Jahr; Unlesbares bleibt, wie es kam (ohne „??").
 */
export function cardDate(pgn: string | null | undefined, lang: string): string {
  const s = String(pgn ?? '');
  const full = /^(\d{4})\.(\d{2})\.(\d{2})$/.exec(s);
  if (full) {
    const d = new Date(Number(full[1]), Number(full[2]) - 1, Number(full[3]));
    // ohne registrierte Locale-Daten (Missing locale data) die frühere Schreibweise TT.MM.JJJJ
    try { return formatDate(d, 'mediumDate', cardFormatLocale(lang)); } catch { return `${full[3]}.${full[2]}.${full[1]}`; }
  }
  const year = /^(\d{4})(?:\.|$)/.exec(s);
  return year ? year[1] : s.replace(/\.?\?\?/g, '');
}
