import { DEPTH_OPTIONS, isLinesCount } from './analysis-settings';
import { MAIA_ELO_OPTIONS } from './maia/maia-model';

/** Lesezugriff auf die Adress-Parameter (ParamMap oder Attrappe). */
export interface QueryParams { get(name: string): string | null; }

/** Was die Adresse des Analysebretts vorgibt (Zerlegung Z22) — alles schon geprüft, ungültige Werte fehlen. */
export interface AnalysisRouteParams {
  /** Ausgangsstellung (`?fen=`, nur wenn ladbar). */
  fen: string | null;
  orientation: 'white' | 'black' | null;
  /** Rücksprung (`?from=`), nur seiteneigene Pfade. */
  returnTo: string | null;
  /** Engine eines Auftrags (`?engine=`), gilt nur für diesen Aufruf. */
  engine: string | null;
  depth: number | null;
  lines: number | null;
  /** Zugfolge in UCI (`?moves=`, Leerzeichen/Komma getrennt). */
  moves: string[];
  /** Partie (`?pgn=`). */
  pgn: string | null;
  /** Verlauf-Eintrag (`?history=<id>`). */
  historyId: number | null;
  /** „Ohne Engine analysieren" (`?blind=1`, 0.855.0) — zusammen mit einer Partie (`?pgn=` bzw. Router-State). */
  blind: boolean;
  /** Partieseite der Partie (`?game=`, 0.900.0, nur seiteneigene Pfade): „Mit Engine“ oben führt dorthin zurück. */
  gamePage: string | null;
  /** „Gegen Maia weiterspielen" (`?maia=<Elo>`, 0.892.0, Abweichungs-Trainer): Maia-Karte offen, Stärke = nächste Maia-Stufe;
   *  `null` = nichts vorgegeben. Gilt nur für diesen Aufruf (nicht gespeichert). */
  maia: number | null;
}

/** Nächste Maia-Stufe zu einer Elo (600–2600 in 200er-Schritten; Gleichstand → die höhere). */
export function nearestMaiaElo(elo: number): number {
  let best: number = MAIA_ELO_OPTIONS[0];
  for (const o of MAIA_ELO_OPTIONS) if (Math.abs(o - elo) <= Math.abs(best - elo)) best = o;
  return best;
}

/** Nur seiteneigene Pfade — kein Sprung auf fremde Seiten. */
function isOwnPath(p: string): boolean { return p.startsWith('/') && !p.startsWith('//') && !p.includes('://') && !p.includes('\\'); }

/** Adress-Parameter lesen und prüfen; `isValidFen` entscheidet über `?fen=`. */
export function readAnalysisParams(params: QueryParams, isValidFen: (fen: string) => boolean): AnalysisRouteParams {
  const fen = params.get('fen');
  const orientation = params.get('orientation');
  // Herkunft für den Zurück-Button — Puzzle, Favoriten, Partienliste, Partie oder Kalkulation, daher heißt der Knopf
  // neutral „Zurück" (F4-012). Nur seiteneigene Pfade, kein Sprung auf fremde Seiten.
  const from = params.get('from');
  const engine = params.get('engine');
  const depth = parseInt(params.get('depth') || '', 10);
  const lines = parseInt(params.get('lines') || '', 10);
  const moves = params.get('moves');
  const pgn = params.get('pgn');
  const historyId = parseInt(params.get('history') || '', 10);
  const maia = parseInt(params.get('maia') || '', 10);
  const game = params.get('game');
  return {
    fen: fen && isValidFen(fen) ? fen : null,
    orientation: orientation === 'white' || orientation === 'black' ? orientation : null,
    returnTo: from && isOwnPath(from) ? from : null,
    engine: engine && /^[A-Za-z0-9_-]{1,64}$/.test(engine) ? engine : null,
    depth: DEPTH_OPTIONS.includes(depth) ? depth : null,
    lines: isLinesCount(lines) ? lines : null,
    moves: moves ? moves.split(/[ ,]+/).filter(Boolean) : [],
    pgn: pgn?.trim() ? pgn : null,
    historyId: historyId > 0 ? historyId : null,
    blind: params.get('blind') === '1',
    gamePage: game && isOwnPath(game) ? game : null,
    maia: maia > 0 && maia < 4000 ? nearestMaiaElo(maia) : null,
  };
}
