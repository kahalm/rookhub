import { LibraryGame, MasterGameQuery } from '../guess/library.service';

/**
 * Eröffnungsgruppen der Meisterpartien-Suche als ECO-Bereiche. Der Bestand trägt keinen Eröffnungsnamen, nur den ECO-Code —
 * die Auswahl übersetzt „Sizilianisch“ in „B20-B99“ und schreibt das ins ECO-Feld (dort bleibt es sichtbar und änderbar).
 * Namen: `masterGames.openings.<key>`.
 */
export const OPENING_GROUPS: readonly { key: string; eco: string }[] = [
  { key: 'sicilian', eco: 'B20-B99' },
  { key: 'french', eco: 'C00-C19' },
  { key: 'caroKann', eco: 'B10-B19' },
  { key: 'pircModern', eco: 'B06-B09' },
  { key: 'alekhine', eco: 'B02-B05' },
  { key: 'scandinavian', eco: 'B01' },
  { key: 'kingsGambit', eco: 'C30-C39' },
  { key: 'petrov', eco: 'C42-C43' },
  { key: 'italian', eco: 'C50-C54' },
  { key: 'twoKnights', eco: 'C55-C59' },
  { key: 'ruyLopez', eco: 'C60-C99' },
  { key: 'slav', eco: 'D10-D19' },
  { key: 'qga', eco: 'D20-D29' },
  { key: 'qgd', eco: 'D30-D69' },
  { key: 'gruenfeld', eco: 'D70-D99' },
  { key: 'catalan', eco: 'E01-E09' },
  { key: 'queensIndian', eco: 'E12-E19' },
  { key: 'nimzoIndian', eco: 'E20-E59' },
  { key: 'kingsIndian', eco: 'E60-E99' },
  { key: 'english', eco: 'A10-A39' },
  { key: 'benoni', eco: 'A56-A79' },
  { key: 'dutch', eco: 'A80-A99' },
];

/** Die Gruppe, deren Bereich genau im ECO-Feld steht — sonst keine (eigener Code). */
export function openingGroupOf(eco: string | undefined): string {
  const v = (eco ?? '').replace(/\s+/g, '').toUpperCase();
  return OPENING_GROUPS.find(g => g.eco === v)?.key ?? '';
}

/** Gesetzte Filter als Adresszeile (`?player=Carlsen&yearFrom=2010`) — die Suche ist so verlinkbar und überlebt „Zurück“. */
export function queryToParams(q: MasterGameQuery): Record<string, string> {
  const out: Record<string, string> = {};
  for (const key of ['q', 'player', 'event', 'eco', 'result'] as const) {
    const v = (q[key] ?? '').trim();
    if (v) out[key] = v;
  }
  if (q.yearFrom != null) out['yearFrom'] = String(q.yearFrom);
  if (q.yearTo != null) out['yearTo'] = String(q.yearTo);
  if (q.sort && q.sort !== 'best') out['sort'] = q.sort;
  if (q.page && q.page > 1) out['page'] = String(q.page);
  return out;
}

const RESULTS = ['white', 'black', 'draw'] as const;
const SORTS = ['best', 'newest', 'oldest'] as const;

/** Rückweg von {@link queryToParams}; Unbekanntes fällt weg. */
export function paramsToQuery(p: Record<string, string | undefined>): MasterGameQuery {
  const year = (v?: string) => {
    const n = v ? Number.parseInt(v, 10) : NaN;
    return Number.isFinite(n) ? n : null;
  };
  const page = year(p['page']);
  return {
    q: p['q'] ?? '',
    player: p['player'] ?? '',
    event: p['event'] ?? '',
    eco: p['eco'] ?? '',
    yearFrom: year(p['yearFrom']),
    yearTo: year(p['yearTo']),
    result: (RESULTS as readonly string[]).includes(p['result'] ?? '') ? p['result'] as MasterGameQuery['result'] : '',
    sort: (SORTS as readonly string[]).includes(p['sort'] ?? '') ? p['sort'] as MasterGameQuery['sort'] : 'best',
    page: page && page > 1 ? page : 1,
  };
}

/** Das Jahr einer Zeile: eigene Spalte, sonst aus dem Datum. */
export function gameYear(g: LibraryGame): string {
  if (g.playedYear) return String(g.playedYear);
  return g.playedOn ? g.playedOn.slice(0, 4) : '';
}

/** Ein PGN-Platzhalter („?“) ist kein Name (DESIGN 2.7). */
export function cleanName(v: string | null | undefined): string {
  const t = (v ?? '').trim();
  return t === '?' || t === '-' ? '' : t;
}

/** „1-0“, „0-1“, „½-½“; offen/unbekannt = leer. */
export function resultLabel(r: string | null | undefined): string {
  if (r === '1/2-1/2') return '½-½';
  return r === '1-0' || r === '0-1' ? r : '';
}
