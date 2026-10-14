import { TranslateService } from '@ngx-translate/core';

/**
 * Anzeigename der Quelle einer gespeicherten Partie — geteilt von Partieliste (Tooltip am Quellsymbol) und
 * Partiekopf (b1-rk-partie-quelle-pgn: dort stand der Rohschlüssel „pgn“). Eigene Uploads werden übersetzt,
 * Plattformen tragen ihren Markennamen. Feste Schlüssel, damit die i18n-Prüfung sie findet.
 */
export function gameSourceLabel(source: string | null | undefined, translate: TranslateService): string {
  switch (source) {
    case 'scoresheet': return translate.instant('games.source.scoresheet');
    case 'pgn': return translate.instant('games.source.pgn');
    case 'lichess': return 'Lichess';
    case 'chess.com': case 'chesscom': return 'Chess.com';
    default: return source ?? '';
  }
}

export function gameSourceIcon(source: string | null | undefined): string {
  return source === 'lichess' ? 'public' : source === 'scoresheet' ? 'photo_camera' : source === 'pgn' ? 'description' : 'sports_esports';
}
