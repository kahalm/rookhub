import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Art eines Bruchstücks — dieselben Zahlen wie <c>ReconstructionPartKind</c> im Server. */
export enum PartKind { Moves = 0, Position = 1 }

/** Ein Bruchstück samt Auswertung der Kette (der Server rechnet sie bei jedem Lesen neu). */
export interface ReconstructionPart {
  id: number;
  ordinal: number;
  kind: PartKind;
  moves?: string | null;
  fen?: string | null;
  fromPly?: number | null;
  /** Schließt dieses Teil nahtlos an das vorige an? (Vorgabe: nein — dazwischen liegt eine Lücke.) */
  continuesPrevious: boolean;
  /** Bin ich mir bei diesem Bruchstück sicher? (Vorgabe ja — „nein" ist die Auskunft.) */
  certain: boolean;
  note?: string | null;
  /** Ist bekannt, welche Stellung VOR diesem Teil steht? */
  anchored: boolean;
  /** Nur wenn verankert: ließ es sich spielen bzw. laden? */
  valid: boolean;
  startFen?: string | null;
  endFen?: string | null;
  plyCount: number;
  /** Halbzug-Nummer, solange die Kette ab der Grundstellung durchgeht. */
  startPly?: number | null;
  firstBadMove?: string | null;
  /** Behauptet den Anschluss, passt aber nicht zur Stellung davor. */
  mismatch: boolean;
}

export interface ReconstructionListItem {
  id: number;
  title: string;
  white?: string | null;
  black?: string | null;
  event?: string | null;
  playedOn?: string | null;
  result?: string | null;
  partCount: number;
  knownPlies: number;
  gaps: number;
  updatedAt: string;
}

export interface Reconstruction extends ReconstructionListItem {
  note?: string | null;
  parts: ReconstructionPart[];
  /** Die bereits gesicherten Züge ab der Grundstellung. */
  prefixSan: string;
}

/** Kopfdaten (Titel ist Pflicht). */
export interface ReconstructionHead {
  title: string;
  white?: string | null;
  black?: string | null;
  event?: string | null;
  playedOn?: string | null;
  result?: string | null;
  note?: string | null;
}

/** Inhalt eines Teils; je nach `kind` zählt `moves` ODER `fen`. */
export interface PartInput {
  kind: PartKind;
  moves?: string | null;
  fen?: string | null;
  fromPly?: number | null;
  continuesPrevious?: boolean;
  certain?: boolean;
  note?: string | null;
}

/** Ein gefundener Weg durch eine Lücke (Zugfolge in SAN). */
export interface GapSolution {
  san: string;
  plies: number;
}

/**
 * Das Ergebnis der Lückensuche zu EINEM Teil. `reason` sagt, warum die Liste leer ist —
 * `budgetExhausted` unterscheidet dabei „nicht gefunden" von „gibt es nicht".
 */
export interface GapResult {
  partId: number;
  fromFen?: string | null;
  toFen?: string | null;
  maxPlies: number;
  nodes: number;
  budgetExhausted: boolean;
  reason?: string | null;
  solutions: GapSolution[];
}

/**
 * Zugriff auf „Partie rekonstruieren". Jede Änderung antwortet mit der GANZEN Rekonstruktion —
 * die Auswertung der Kette hängt an allen Teilen zusammen, ein einzelnes geändertes Teil wäre
 * danach an mehreren Stellen veraltet.
 */
@Injectable({ providedIn: 'root' })
export class ReconstructService {
  private http = inject(HttpClient);
  private base = '/api/reconstructions';

  list(): Observable<ReconstructionListItem[]> {
    return this.http.get<ReconstructionListItem[]>(this.base);
  }

  get(id: number): Observable<Reconstruction> {
    return this.http.get<Reconstruction>(`${this.base}/${id}`);
  }

  create(head: ReconstructionHead): Observable<Reconstruction> {
    return this.http.post<Reconstruction>(this.base, head);
  }

  updateHead(id: number, head: ReconstructionHead): Observable<Reconstruction> {
    return this.http.put<Reconstruction>(`${this.base}/${id}`, head);
  }

  remove(id: number): Observable<void> {
    return this.http.delete<void>(`${this.base}/${id}`);
  }

  addPart(id: number, part: PartInput): Observable<Reconstruction> {
    return this.http.post<Reconstruction>(`${this.base}/${id}/parts`, part);
  }

  updatePart(id: number, partId: number, part: PartInput): Observable<Reconstruction> {
    return this.http.put<Reconstruction>(`${this.base}/${id}/parts/${partId}`, part);
  }

  removePart(id: number, partId: number): Observable<Reconstruction> {
    return this.http.delete<Reconstruction>(`${this.base}/${id}/parts/${partId}`);
  }

  reorder(id: number, partIds: number[]): Observable<Reconstruction> {
    return this.http.put<Reconstruction>(`${this.base}/${id}/parts/order`, { partIds });
  }

  /** Sucht die Züge, die die Lücke VOR diesem Teil schließen. Antwortet auch ohne Treffer mit 200. */
  solveGap(id: number, partId: number, maxPlies?: number): Observable<GapResult> {
    return this.http.post<GapResult>(`${this.base}/${id}/parts/${partId}/gap`, { maxPlies: maxPlies ?? null });
  }

  /** Setzt einen gefundenen Weg als eigenes Teil VOR das Teil — beide hängen danach aneinander. */
  applyGap(id: number, partId: number, moves: string): Observable<Reconstruction> {
    return this.http.post<Reconstruction>(`${this.base}/${id}/parts/${partId}/gap/apply`, { moves });
  }
}
