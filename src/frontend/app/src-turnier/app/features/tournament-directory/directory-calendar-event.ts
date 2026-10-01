import { TranslateService } from '@ngx-translate/core';
import { CalendarEvent } from '@rh/core/ics';
import { DirectoryEntry } from './tournament-directory.model';

/** Die chess-results-Seite eines Turniers (englische Fassung). */
export function directoryChessResultsUrl(chessResultsId: string): string {
  return `https://chess-results.com/tnr${chessResultsId}.aspx?lan=1`;
}

/**
 * Turnier → Kalendertermin, EINE Abbildung fuer Karte (Liste/Karte/Kalender) und Detailseite —
 * `null` ohne Startdatum (dann fehlt der Knopf).
 *
 * <p>Die Kennung ist die IDENTITAET des Turniers (`directory-<id>`), nicht die chess-results-Nummer:
 * dieselbe Kennung aktualisiert den Termin im Kalender des Nutzers statt ihn zu verdoppeln — auch
 * wenn er einmal von der Karte und einmal von der Detailseite kommt (vorher zwei Kennungen,
 * Codereview F6-009) —, und ein FIDE-Turnier (`f<Nummer>`) hat gar keine chess-results-Nummer.</p>
 *
 * <p>Beschreibung: nur, was auch stimmt — chess-results liefert Rundenzahl und Gemeldete nicht
 * immer. Der Ort steht im LOCATION-Feld, nicht noch einmal in der Beschreibung.</p>
 */
export function directoryCalendarEvent(
  entry: DirectoryEntry, translate: Pick<TranslateService, 'instant'>,
): CalendarEvent | null {
  if (!entry.startDate) return null;

  const url = entry.chessResultsId === null ? null : directoryChessResultsUrl(entry.chessResultsId);
  const facts = [
    entry.timeControl,
    entry.rounds ? translate.instant('tournamentDirectory.detail.rounds') + ': ' + entry.rounds : null,
    entry.playerCount ? translate.instant('tournamentDirectory.players', { count: entry.playerCount }) : null,
    entry.organizer ? translate.instant('tournamentDirectory.detail.organizer') + ': ' + entry.organizer : null,
    url,
  ].filter((l): l is string => !!l);

  return {
    uid: `directory-${entry.id}@rookhub`,
    title: entry.name,
    start: entry.startDate,
    end: entry.endDate,
    location: entry.location,
    description: facts.join('\n'),
    url,
  };
}
