import { HttpParams } from '@angular/common/http';
import { TreeFilter } from '@lh/core/league.models';
import { de } from '@lh/core/league-format';

/**
 * Trainingslinien gegen einen Gegner (2026-10-07): Antwort von `GET /api/prep/player/{id}/training-lines` bzw.
 * `GET /api/league/player/{fide}/training-lines` (ein Dienst am Server, `TrainingLinesService`). Die Linien sind die des
 * eigenen Repertoires („Für Extension und Vorbereitung verwenden"), wahrscheinlichste zuerst.
 */
export interface TrainingLine {
  /** Linien-Schlüssel wie im Trainer (`?line=`). */
  key: string;
  /** Endstellung (erste drei FEN-Felder). */
  end: string;
  /** Eigene Startstellung, `null` = Grundstellung. */
  start: string | null;
  chapter: string;
  /** Züge in englischer SAN. */
  moves: string[];
  /** 0…1 — Produkt der Anteile der Gegnerzüge in seinen Partien. */
  probability: number;
  /** Partien, die die Stellung nach dem letzten Gegnerzug der Linie erreicht haben. */
  reached: number;
  lastYear: number | null;
  neverReached: boolean;
}

export interface TrainingLines {
  repertoires: { id: number; name: string }[];
  repertoire: number | null;
  color: 'w' | 'b' | null;
  colors: ('w' | 'b')[];
  /** Gezählte Partien (der Gegner mit der anderen Farbe). */
  games: number;
  total: number;
  lines: TrainingLine[];
  /** So viele Linien mehr gibt es (ohne `take` höchstens 50 in der Antwort). */
  more: number;
}

export interface TrainingLinesQuery {
  repertoire: number | null;
  color: 'w' | 'b' | null;
  /** Eigene Farb-Festlegungen je Kapitel (Trainer) — sonst rechnet der Server die Auto-Erkennung. */
  chapterColors?: Record<string, string> | null;
  take?: number;
  /** Der Filter der Karte (Brett/online, Tempo, Jahre, unsichere Konten); `null` = Vorgabe des Servers (Brett + online). */
  filter?: TreeFilter | null;
}

/** So viele Linien holt „Alle in dieser Reihenfolge trainieren" im Trainer (die Obergrenze des Servers). */
export const TRAINING_LINES_ALL = 5000;

/** Gemerkte Auswahl (Repertoire, Farbe) — je Gerät, für alle Gegner. */
export const TRAINING_LINES_KEY = 'lh-training-lines';

/** Der Filter als Adress-Parameter — anders als beim Baum steht `source` IMMER dabei: ohne Angabe nimmt der Server Brett + online. */
export function trainingFilterParams(filter: TreeFilter | null | undefined): Record<string, string> {
  if (!filter) return {};
  const p: Record<string, string> = { source: filter.source };
  if (filter.source !== 'board') {
    if (filter.speeds.length) p['speeds'] = filter.speeds.join(',');
    if (filter.withUnsure) p['unsure'] = 'true';
  }
  if (filter.years) p['years'] = String(filter.years);
  return p;
}

export function trainingLinesParams(q: TrainingLinesQuery, params = new HttpParams()): HttpParams {
  if (q.repertoire !== null) params = params.set('repertoire', q.repertoire);
  if (q.color) params = params.set('color', q.color);
  if (q.chapterColors && Object.keys(q.chapterColors).length) params = params.set('chapterColors', JSON.stringify(q.chapterColors));
  if (q.take) params = params.set('take', q.take);
  for (const [k, v] of Object.entries(trainingFilterParams(q.filter))) params = params.set(k, v);
  return params;
}

/** Der Gegner im Trainer (`?opponent=`): `prep:<Id>` oder `league:<FIDE>` → Adresse der Trainingslinien; sonst `null`. */
export function trainingLinesUrl(opponent: string | null | undefined): string | null {
  const m = /^(prep|league):([\w-]{1,32})$/.exec(opponent ?? '');
  if (!m) return null;
  return m[1] === 'prep' ? `/api/prep/player/${m[2]}/training-lines` : `/api/league/player/${encodeURIComponent(m[2])}/training-lines`;
}

/** Was der Trainer an die Trainingslinien weiterreicht (Filter der Karte und — Spielervorbereitung — `all`/`twin`). */
export const TRAINER_PASS_THROUGH = ['source', 'speeds', 'years', 'unsure', 'all', 'twin'] as const;

/** Wahrscheinlichkeit in deutscher Schreibweise: „42 %", „3,5 %", „<0,1 %". */
export function percent(p: number): string {
  if (p <= 0) return '0 %';
  const v = p * 100;
  if (v < 0.1) return '<0,1 %';
  return (v >= 10 ? Math.round(v).toString() : v.toFixed(1).replace('.', ',')) + ' %';
}

/** Züge mit Zugnummern in deutscher Notation („1.e4 c5 2.Sf3"), ab einer eigenen Startstellung mit deren Zugnummer. */
export function lineText(moves: string[], start: string | null): string {
  const parts = (start ?? '').split(' ');
  let black = parts[1] === 'b';
  let no = Number(parts[5]) > 0 ? Number(parts[5]) : 1;
  const out: string[] = [];
  moves.forEach((m, i) => {
    if (!black) out.push(`${no}.${de(m)}`);
    else { out.push(i === 0 ? `${no}…${de(m)}` : de(m)); no++; }
    black = !black;
  });
  return out.join(' ');
}
