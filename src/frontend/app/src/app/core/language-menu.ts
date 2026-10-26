import { I18N_COVERAGE } from './i18n-coverage';
import { FORMAT_LOCALES, LangOption } from './locale.service';

/** Eine Teilsprache im Menü: Eigenbezeichnung plus Anteil der übersetzten Texte. */
export interface PartialLangOption extends LangOption {
  /** Prozent der en-Schlüssel (gerundet, aus `I18N_COVERAGE`). */
  coverage: number;
}

export interface LanguageMenu {
  /** Vollständig gepflegt (`FORMAT_LOCALES`: en, de, hr, hu) — oben, wie bisher. */
  full: LangOption[];
  /** Teilübersetzungen (Rest fällt auf Englisch zurück) — unter „Teilweise übersetzt“ mit Prozent. */
  partial: PartialLangOption[];
}

/**
 * Sprachmenü in zwei Gruppen (Codereview F8-009, Entscheidung 2026-10-25): 21 der 25 Sprachen waren zu ≈ 17 %
 * übersetzt und standen gleichrangig neben den gepflegten. Reihenfolge innerhalb der Gruppen wie in `languages`.
 * Eine Sprache ohne Eintrag in der Abdeckung zählt als 0 %.
 */
export function languageMenu(languages: readonly LangOption[], coverage: Readonly<Record<string, number>> = I18N_COVERAGE): LanguageMenu {
  const full = languages.filter(l => FORMAT_LOCALES.includes(l.code));
  const partial = languages.filter(l => !FORMAT_LOCALES.includes(l.code)).map(l => ({ ...l, coverage: coverage[l.code] ?? 0 }));
  return { full, partial };
}
