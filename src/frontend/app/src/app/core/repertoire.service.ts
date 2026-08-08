import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';
import { Repertoire } from './models';

/**
 * Kapselt die Repertoire-CRUD-HTTP-Calls (`/api/repertoires`), damit die Komponenten nicht direkt
 * den `HttpClient` ansprechen (Service-Layer, vgl. Audit-Fund „14 Komponenten rufen HttpClient direkt").
 */
/** Ergebnis eines Teilen-Vorgangs (Batch). */
export interface RepertoireShareResult {
  shared: number;
  skipped: { userId: number; reason: string }[];
}

/** Ein Nutzer, mit dem ein Repertoire aktuell geteilt ist. */
export interface RepertoireShareRecipient {
  userId: number;
  username: string;
  displayName: string | null;
  sharedAt: string;
}

/** Eine gefundene Repertoire-Linie, in der eine gesuchte Stellung vorkommt. */
export interface RepertoireLineMatch {
  chapter: string;
  lineName: string;
  gameIndex: number;
  /** Halbzüge bis zur Stellung auf der Hauptlinie (0 = Start); -1 = nur in einer Variante. */
  ply: number;
}

/** Ein Repertoire mit allen Linien, in denen die gesuchte Stellung vorkommt. */
export interface RepertoirePositionMatch {
  repertoireId: number;
  repertoireName: string;
  kind: string;
  /** true = mit dem Nutzer geteiltes (fremdes) Repertoire, nicht sein eigenes. */
  shared: boolean;
  lines: RepertoireLineMatch[];
}

export interface PositionLookupResult {
  repertoires: RepertoirePositionMatch[];
}

/** Ein Zug im Baummodus; `children` sind die im Repertoire folgenden Antworten. */
export interface PositionTreeNode {
  /** SAN ohne Schach-/Bewertungszeichen (der Server-PGN-Tokenizer strippt `+#!?`). */
  san: string;
  /** Wie viele Linien-Pfade durch diesen Zug laufen. */
  count: number;
  /** Hier endet mindestens eine Linie. */
  isEnd: boolean;
  /** Nur gesetzt, wenn ab hier GENAU EINE Linie durchläuft (dann sind Trainieren/Ansehen eindeutig). */
  chapter?: string | null;
  lineName?: string | null;
  gameIndex?: number | null;
  children: PositionTreeNode[];
}

/** Ein Repertoire mit dem ab der Stellung zusammengeführten Zugbaum. */
export interface RepertoirePositionTree {
  repertoireId: number;
  repertoireName: string;
  kind: string;
  shared: boolean;
  moves: PositionTreeNode[];
  /** Anzahl Linien-Vorkommen (Hauptlinie + Varianten) der Stellung. */
  occurrences: number;
  /** Baum wurde an der Knoten-Obergrenze gekappt. */
  truncated: boolean;
}

export interface PositionTreeResult {
  repertoires: RepertoirePositionTree[];
}

/**
 * Gewichtungs-Voreinstellung der Ähnlichkeitssuche. Die Werte sind BEWUSST die Wire-Werte des
 * Servers (nicht übersetzt/umbenannt) — Gewichte Bauern/Material/Figuren/König:
 * `struktur` 0.75/0.10/0.10/0.05 · `ausgewogen` 0.50/0.20/0.20/0.10 (Default) ·
 * `stellungsbild` 0.30/0.15/0.45/0.10.
 */
export type SimilarityPreset = 'struktur' | 'ausgewogen' | 'stellungsbild';

/** Umwandlungsfigur eines Zuges (chess.js-Schreibweise). */
export type SimilarMovePromotion = 'q' | 'r' | 'b' | 'n';

/**
 * Der Zug, den der Nutzer erwägt — BEWUSST als from→to (+ Umwandlungsfigur), nicht als SAN.
 * `Nbd2` und `Nd2` sind derselbe Zug, nur anders disambiguiert; ein Textvergleich verfehlt
 * solche Paare still. Die SAN-Eingabe wird im Frontend auf der Ankerstellung aufgelöst
 * (siehe `parseMoveInput`), über die Leitung gehen nur Felder.
 */
export interface SimilarMoveInput {
  from: string;
  to: string;
  promotion?: SimilarMovePromotion;
}

/**
 * Trefferstufe des mitgegebenen Zuges an der gefundenen Stellung:
 * - `exact` — dort geht die Linie tatsächlich mit genau diesem Zug weiter (Hauptzug ODER eine
 *   Variante an dieser Stelle); nicht bloße Legalität.
 * - `sameTarget` — dieselbe Figurenart zieht dort aufs gleiche Zielfeld, aber von woanders.
 */
export type SimilarMoveMatch = 'exact' | 'sameTarget';

/** Lücken-Schluss-Bonus je Trefferstufe (siehe `applyMoveBonus`). */
export const SIMILAR_MOVE_BONUS: Record<SimilarMoveMatch, number> = { exact: 0.5, sameTarget: 0.25 };

/**
 * Verrechnet den Zug-Treffer als LÜCKEN-SCHLUSS, nicht als fünfte gewichtete Komponente:
 * `score' = score + bonus * (100 - score)`. Das ordnet richtig (ein Zug-Treffer hebt jeden
 * Stellungswert an, ohne die Reihenfolge innerhalb einer Stufe zu drehen) und kann per
 * Konstruktion nie über 100 laufen. Ohne Trefferstufe bleibt der Wert unverändert.
 */
export function applyMoveBonus(score: number, level: SimilarMoveMatch | null): number {
  if (!level || !Number.isFinite(score)) return Number.isFinite(score) ? score : 0;
  const clamped = Math.max(0, Math.min(100, score));
  return clamped + SIMILAR_MOVE_BONUS[level] * (100 - clamped);
}

/** Anfrage an `POST /api/repertoires/similar-positions`. */
export interface SimilarPositionsRequest {
  fen: string;
  /** Leere Liste = alle (der Server filtert dann nicht). */
  repertoireIds: number[];
  preset: SimilarityPreset;
  /** Auch farbgetauscht+gespiegelt vergleichen und den besseren Wert werten. */
  includeMirrored: boolean;
  /** Nur Stellungen mit derselben Seite am Zug (Server-Default: aus). */
  sameSideToMove: boolean;
  /**
   * Optionaler Zug, den der Nutzer erwägt („wo geht 12.Nd5 noch?"). Fehlt er, ist die Suche
   * exakt die bisherige.
   */
  move?: SimilarMoveInput;
  /** Nur Treffer, an denen dieser Zug tatsächlich der Repertoirezug ist. Nur mit `move` sinnvoll. */
  onlyWithMove?: boolean;
  limit: number;
  // KEIN minScore: die Schwelle gehört dem Server. Schickte das Frontend eine eigene mit, würden
  // die beiden Defaults auseinanderdriften (genau das war hier der Fall: 55 hier vs. 60 dort).
}

/**
 * Eine ähnliche Stellung in einer Repertoire-Linie. `score` ist der ENDWERT 0–100 (Stellung plus
 * Zug-Bonus), `positionScore` der reine Stellungswert davor — beide bleiben sichtbar, sonst wäre
 * nicht erkennbar, warum ein Treffer oben steht. Die vier Teilwerte sind die Komponenten derselben
 * Skala (Bauerngerüst, Material, Figurenplatzierung, König); sie werden in der Trefferliste offen
 * ausgewiesen, nicht hinter einem Tooltip versteckt.
 */
export interface SimilarPositionMatch {
  repertoireId: number;
  repertoireName: string;
  chapter: string;
  lineName: string;
  gameIndex: number;
  /** Halbzüge vom Linienanfang bis zur Stellung. */
  ply: number;
  fen: string;
  /** Endwert: Stellungswert nach Verrechnung des Zug-Treffers (ohne Zug identisch zu `positionScore`). */
  score: number;
  /** Reiner Stellungswert vor dem Zug-Bonus. */
  positionScore: number;
  /** Treffer entstand über den Farbtausch (Brett gespiegelt, Farben getauscht). */
  mirrored: boolean;
  pawnScore: number;
  materialScore: number;
  pieceScore: number;
  kingScore: number;
  /** SAN des Zuges, mit dem die Linie an dieser Stellung weitergeht ('' = keiner gemeldet). */
  moveSan: string;
  /** Dessen Ausgangsfeld ('' = keines gemeldet). */
  moveFrom: string;
  /** Dessen Zielfeld ('' = keines gemeldet). */
  moveTo: string;
  /** Trefferstufe des mitgegebenen Zuges; `null` = kein Zug mitgegeben oder kein Treffer. */
  moveMatch: SimilarMoveMatch | null;
}

export interface SimilarPositionsResult {
  matches: SimilarPositionMatch[];
}

/** Rohform eines Treffers, wie er über die Leitung kommt (siehe `normalizeSimilarMatch`). */
type SimilarPositionWireMatch = Partial<Omit<SimilarPositionMatch, 'moveMatch'>> & {
  breakdown?: { pawns?: number; material?: number; pieces?: number; king?: number };
  moveMatch?: string | null;
  /** Verschachtelte Alternativform der Fortsetzung. */
  move?: { san?: string; from?: string; to?: string; match?: string | null } | null;
};

function num(v: unknown): number { return typeof v === 'number' && Number.isFinite(v) ? v : 0; }
function str(v: unknown): string { return typeof v === 'string' ? v : ''; }

/** Trefferstufe aus der Leitung lesen; alles Unbekannte wird `null` (= kein Zug-Treffer). */
function moveMatchOf(v: unknown): SimilarMoveMatch | null {
  const s = str(v).trim().toLowerCase().replace(/[_-]/g, '');
  if (s === 'exact') return 'exact';
  if (s === 'sametarget') return 'sameTarget';
  return null;
}

/**
 * Bringt einen Treffer auf die flache Vertragsform. Der Vertrag nennt die vier Teilwerte flach
 * (`pawnScore`/`materialScore`/`pieceScore`/`kingScore`); liefert der Server sie stattdessen
 * verschachtelt (`breakdown: { pawns, material, pieces, king }`), wird das hier — an EINER Stelle,
 * nicht in der Ansicht — übersetzt. Dasselbe für die Fortsetzung (`moveSan`/`moveFrom`/`moveTo`/
 * `moveMatch` bzw. `move: { san, from, to, match }`). Fehlende Werte werden 0 bzw. '', damit die
 * Aufschlüsselung nie `undefined` in die Balken schreibt.
 *
 * Zu den beiden Zahlen: nennt der Server `positionScore`, hat er den Lücken-Schluss selbst
 * gerechnet und `score` IST der Endwert. Fehlt `positionScore`, ist `score` der reine
 * Stellungswert und der Endwert wird hier aus der Trefferstufe abgeleitet — so wird der Bonus
 * nie doppelt verrechnet und ein Server ohne Zug-Kenntnis (Trefferstufe `null`) liefert
 * unverändert Endwert = Stellungswert.
 */
export function normalizeSimilarMatch(m: SimilarPositionWireMatch): SimilarPositionMatch {
  const b = m.breakdown;
  const mv = m.move ?? undefined;
  const moveMatch = moveMatchOf(m.moveMatch ?? mv?.match);
  const serverClosedGap = typeof m.positionScore === 'number' && Number.isFinite(m.positionScore);
  const positionScore = serverClosedGap ? num(m.positionScore) : num(m.score);
  const score = serverClosedGap ? num(m.score) : applyMoveBonus(positionScore, moveMatch);
  return {
    repertoireId: num(m.repertoireId),
    repertoireName: m.repertoireName ?? '',
    chapter: m.chapter ?? '',
    lineName: m.lineName ?? '',
    gameIndex: num(m.gameIndex),
    ply: num(m.ply),
    fen: m.fen ?? '',
    score,
    positionScore,
    mirrored: m.mirrored === true,
    pawnScore: num(m.pawnScore ?? b?.pawns),
    materialScore: num(m.materialScore ?? b?.material),
    pieceScore: num(m.pieceScore ?? b?.pieces),
    kingScore: num(m.kingScore ?? b?.king),
    moveSan: str(m.moveSan ?? mv?.san),
    moveFrom: str(m.moveFrom ?? mv?.from),
    moveTo: str(m.moveTo ?? mv?.to),
    moveMatch,
  };
}

/** Öffentliche Sicht einer geteilten Einzel-Linie (Nur-Ansehen-Link `/l/{token}`). */
export interface SharedLine {
  shareToken: string;
  title: string | null;
  repertoireName: string | null;
  pgn: string;
  createdAt: string;
}

@Injectable({ providedIn: 'root' })
export class RepertoireService {
  private readonly apiUrl = '/api/repertoires';

  constructor(private http: HttpClient) {}

  /** Teilt ein eigenes Repertoire mit ausgewählten (befreundeten) Nutzern. */
  share(id: number, recipientUserIds: number[]): Observable<RepertoireShareResult> {
    return this.http.post<RepertoireShareResult>(`${this.apiUrl}/${id}/share`, { recipientUserIds });
  }

  /** Mit welchen Nutzern ist dieses eigene Repertoire aktuell geteilt? */
  getShareRecipients(id: number): Observable<RepertoireShareRecipient[]> {
    return this.http.get<RepertoireShareRecipient[]>(`${this.apiUrl}/${id}/shares`);
  }

  /** Nimmt die Freigabe des eigenen Repertoires für einen Empfänger zurück. */
  unshare(id: number, recipientId: number): Observable<void> {
    return this.http.delete<void>(`${this.apiUrl}/${id}/share/${recipientId}`);
  }

  /** Erzeugt einen öffentlichen Nur-Ansehen-Link für eine einzelne Linie (liefert das Token). */
  shareLine(id: number, body: { pgn: string; title?: string }): Observable<{ shareToken: string }> {
    return this.http.post<{ shareToken: string }>(`${this.apiUrl}/${id}/share-line`, body);
  }

  /** Öffentliche Sicht einer geteilten Linie über ihr Token (kein Login). */
  getSharedLine(token: string): Observable<SharedLine> {
    return this.http.get<SharedLine>(`${this.apiUrl}/shared-line/${token}`);
  }

  list(): Observable<Repertoire[]> {
    return this.http.get<Repertoire[]>(this.apiUrl);
  }

  create(dto: unknown): Observable<unknown> {
    return this.http.post(this.apiUrl, dto);
  }

  update(id: number, dto: unknown): Observable<unknown> {
    return this.http.put(`${this.apiUrl}/${id}`, dto);
  }

  remove(id: number): Observable<unknown> {
    return this.http.delete(`${this.apiUrl}/${id}`);
  }

  /** Kombinierter PGN-Download (Blob). */
  downloadPgn(id: number): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/${id}/pgn`, { responseType: 'blob' });
  }

  /** Wandelt ein Repertoire in einen persönlichen Kurs um (nur Puzzle-PGN im Chessable-Stil). */
  convertToCourse(id: number): Observable<{ bookId: number; displayName: string }> {
    return this.http.post<{ bookId: number; displayName: string }>(`${this.apiUrl}/${id}/convert-to-course`, {});
  }

  /** Repertoire-Detail (inkl. Dateien); Form feature-lokal → generisch. */
  getDetail<T>(id: number): Observable<T> {
    return this.http.get<T>(`${this.apiUrl}/${id}`);
  }

  /** Kombinierter PGN-Text (zum Anzeigen, nicht als Blob-Download). */
  getPgnText(id: number): Observable<string> {
    return this.http.get(`${this.apiUrl}/${id}/pgn`, { responseType: 'text' });
  }

  /** „In welchen eigenen Repertoire-Linien kommt diese Stellung vor?" (Repertoire → Kapitel → Linie). */
  lookupPosition(fen: string): Observable<PositionLookupResult> {
    return this.http.post<PositionLookupResult>(`${this.apiUrl}/position-lookup`, { fen });
  }

  /** Baummodus derselben Suche: „wie geht mein Repertoire ab dieser Stellung weiter?"
   * Der Baum kommt bewusst vom Server — er führt auch VARIANTEN zusammen, die der
   * Client-PGN-Parser (`parsePgnText`) wegwirft. `maxDepth` = Halbzüge (0 = Server-Default). */
  lookupPositionTree(fen: string, maxDepth = 0): Observable<PositionTreeResult> {
    return this.http.post<PositionTreeResult>(`${this.apiUrl}/position-tree`, { fen, maxDepth });
  }

  /** „Wo in meinen Repertoires steht etwas ÄHNLICHES?" — dieselbe Frage wie `lookupPosition`,
   * nur unscharf: der Server verdichtet jede Stellung zu Bitmasken und gewichtet Bauerngerüst,
   * Material, Figurenplatzierung und Königsstellung (Voreinstellung `preset`). Sortiert nach Score. */
  findSimilarPositions(req: SimilarPositionsRequest): Observable<SimilarPositionsResult> {
    return this.http.post<{ matches?: SimilarPositionWireMatch[] }>(`${this.apiUrl}/similar-positions`, req).pipe(
      map(res => ({ matches: (res?.matches ?? []).map(normalizeSimilarMatch) })),
    );
  }

  /** PGN-Datei hochladen (multipart). */
  uploadFile(id: number, form: FormData): Observable<unknown> {
    return this.http.post(`${this.apiUrl}/${id}/files`, form);
  }

  /** Einzelne PGN-Datei herunterladen (Blob). */
  downloadFile(id: number, fileId: number): Observable<Blob> {
    return this.http.get(`${this.apiUrl}/${id}/files/${fileId}`, { responseType: 'blob' });
  }

  /** Einzelne PGN-Datei löschen. */
  deleteFile(id: number, fileId: number): Observable<unknown> {
    return this.http.delete(`${this.apiUrl}/${id}/files/${fileId}`);
  }
}
