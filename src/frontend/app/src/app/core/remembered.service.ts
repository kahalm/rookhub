import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

/** Eine über die RepCheck-Extension „gemerkte" Stellung (chessable.com „Remember line"). */
export interface RememberedPosition {
  id: number;
  fen: string;
  courseId: string | null;
  courseName: string | null;
  sourceUrl: string | null;
  createdAt: string;
}

/** Lesen/Löschen der gemerkten Stellungen (Endpoints unter `/api/extension`, JWT genügt). */
@Injectable({ providedIn: 'root' })
export class RememberedService {
  constructor(private http: HttpClient) {}

  list(take = 200): Observable<RememberedPosition[]> {
    return this.http.get<RememberedPosition[]>(`/api/extension/remembered-lines?take=${take}`);
  }

  remove(id: number): Observable<void> {
    return this.http.delete<void>(`/api/extension/remembered-lines/${id}`);
  }
}
