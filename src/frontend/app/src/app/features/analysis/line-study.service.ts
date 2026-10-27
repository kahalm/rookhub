import { Injectable, inject } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable } from 'rxjs';
import { AnalysisTreeDto } from './analysis-tree';

/** „Ohne Engine analysieren" (0.855.0): der eigene Zugbaum zu einer Partie — `GET/PUT /api/line-studies`. */
export interface LineStudy {
  id: number;
  gameKey: string;
  startFen: string;
  title: string | null;
  tree: AnalysisTreeDto;
  current: number;
  /** Engine eingeschaltet — erst dann liefert `evals` Zahlen. */
  revealed: boolean;
  analyzed: number;
  total: number;
}

/** Bewertung der Stellung NACH Knoten `index` des gespeicherten Baums (-1 = Ausgangsstellung), Weiß-Sicht. */
export interface LineStudyNodeEval {
  index: number;
  cp?: number | null;
  mate?: number | null;
  bestUci?: string | null;
  depth: number;
}

export interface LineStudyEvals {
  revealed: boolean;
  analyzed: number;
  total: number;
  nodes: LineStudyNodeEval[];
}

/**
 * Kennung einer Partie für den Baum: Ausgangsstellung + Züge der Partie, gehasht (FNV-1a, 2 × 32 Bit). Dieselbe Partie aus
 * „Meine Partien", der Vereinsliste oder einem Teilen-Link landet so im selben Baum — und eine andere nie.
 */
export function lineStudyKey(startFen: string, ucis: readonly string[]): string {
  const text = startFen.trim().split(/\s+/).slice(0, 4).join(' ') + '|' + ucis.join(' ');
  let h1 = 0x811c9dc5, h2 = 0x01000193 ^ text.length;
  for (let i = 0; i < text.length; i++) {
    const c = text.charCodeAt(i);
    h1 = Math.imul(h1 ^ c, 0x01000193) >>> 0;
    h2 = Math.imul(h2 ^ c, 0x5bd1e995) >>> 0;
  }
  return 'g:' + h1.toString(16).padStart(8, '0') + h2.toString(16).padStart(8, '0') + ':' + ucis.length;
}

@Injectable({ providedIn: 'root' })
export class LineStudyService {
  private readonly http = inject(HttpClient);

  get(gameKey: string): Observable<LineStudy> {
    return this.http.get<LineStudy>('/api/line-studies', { params: new HttpParams().set('key', gameKey) });
  }

  save(req: { gameKey: string; startFen: string; title: string | null; tree: AnalysisTreeDto; current: number }): Observable<LineStudy> {
    return this.http.put<LineStudy>('/api/line-studies', req);
  }

  reveal(id: number): Observable<LineStudy> {
    return this.http.post<LineStudy>(`/api/line-studies/${id}/reveal`, {});
  }

  evals(id: number): Observable<LineStudyEvals> {
    return this.http.get<LineStudyEvals>(`/api/line-studies/${id}/evals`);
  }
}
