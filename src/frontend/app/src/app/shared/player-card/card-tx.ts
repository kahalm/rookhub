import { inject } from '@angular/core';
import { TranslateService } from '@ngx-translate/core';

/** `tx(key, german, params)`: der übersetzte Text, sonst die deutsche Vorgabe. */
export type CardTx = (key: string, german: string, params?: Record<string, unknown>) => string;

/**
 * Sprache der geteilten Konto-Bausteine der Spielerkarte: RookHubs Spielervorbereitung folgt der Oberflächensprache
 * (e1-rk-prep-card-german), LeagueHub stellt `de` ein. Ohne Sprache (Specs) oder ohne Schlüssel steht der deutsche Text.
 * Codereview F3-011: vorher waren Knöpfe, Rückfragen und die (i)-Prüfung fest deutsch — auch mitten in einer englischen
 * bzw. kroatischen /prep-Seite. Im Injektionskontext aufrufen.
 */
export function injectCardTx(): CardTx {
  const translate = inject(TranslateService, { optional: true });
  return (key, german, params = {}) => {
    if (!translate?.getCurrentLang()) return german;
    const v = translate.instant(key, params);
    return v && v !== key ? v : german;
  };
}
