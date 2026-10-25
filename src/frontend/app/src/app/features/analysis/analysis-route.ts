import { DEPTH_OPTIONS, isLinesCount } from './analysis-settings';

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
}

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
  return {
    fen: fen && isValidFen(fen) ? fen : null,
    orientation: orientation === 'white' || orientation === 'black' ? orientation : null,
    returnTo: from && from.startsWith('/') && !from.startsWith('//') && !from.includes('://') ? from : null,
    engine: engine && /^[A-Za-z0-9_-]{1,64}$/.test(engine) ? engine : null,
    depth: DEPTH_OPTIONS.includes(depth) ? depth : null,
    lines: isLinesCount(lines) ? lines : null,
    moves: moves ? moves.split(/[ ,]+/).filter(Boolean) : [],
    pgn: pgn?.trim() ? pgn : null,
    historyId: historyId > 0 ? historyId : null,
  };
}
