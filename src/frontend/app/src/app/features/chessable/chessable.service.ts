import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface ChessableTestResult {
  uid: string;
  courseCount: number;
}

export interface ChessableCourse {
  bid: string;
  name: string;
  importedRepertoire?: boolean;
  importedBook?: boolean;
  /** Rohdaten in der DB gecacht → Import quasi sofort verfügbar. */
  cached?: boolean;
  /** Für diesen Kurs läuft bereits ein Import (eingereiht oder in Arbeit). */
  queued?: boolean;
}

/** Vorab-Schätzung der Kursgröße (Admin-Kursliste). */
export interface ChessableCourseInfo {
  bid: string;
  totalLines: number;
  cached: boolean;
}

export interface ChessableCoursesResult {
  courses: ChessableCourse[];
  cachedAt: string | null;
}

export type ChessableImportTarget = 'repertoire' | 'book';

export interface ChessableImport {
  id: number;
  bid: string;
  courseName: string;
  target: string;
  status: 'running' | 'completed' | 'failed' | 'cancelled' | 'paused';
  phase: string;
  error: string | null;
  resultId: number | null;
  imported: number;
  skipped: number;
  invalid: number;
  chaptersDone: number;
  chaptersTotal: number;
  linesDone: number;
  /** Gesamt-Linienzahl des Kurses (aus getCourse?includeVariations); 0 solange unbekannt. */
  linesTotal: number;
  queuedAhead: number;
  createdAt: string;
  /** Hol-Beginn (aus der Queue gezogen); null solange noch wartend. */
  startedAt: string | null;
  completedAt: string | null;
}

/** Import-Satz in der Admin-Ansicht: zusätzlich Besitzer. */
export interface ChessableAdminImport extends ChessableImport {
  userId: number;
  username: string;
}

/** ADMIN: ein User mit hinterlegtem Chessable-Bearer (Auswahl für „Kurse holen"). */
export interface ChessableCredentialedUser {
  userId: number;
  username: string;
  coursesCachedAt: string | null;
  /** Circuit-Breaker für den Bearer dieses Users offen (gesperrt bis Test bestätigt). */
  blocked?: boolean;
  blockedReason?: string | null;
}

/**
 * Was vom Chessable-Import über RookHub übrig ist: Seit /chessable nur noch auf die RepCheck-Erweiterung
 * verweist, nutzen den Dienst nur das Dashboard (aktive Importe, Abbrechen) und der Admin-Tab
 * „Chessable-Download". Die Nutzer-Wege (Disclaimer, Bearer, Kursliste, Import starten/pausieren …) sind
 * mit F5-024 aus dem Frontend entfernt; die API-Endpunkte bleiben davon unberührt.
 */
@Injectable({ providedIn: 'root' })
export class ChessableService {
  private readonly apiUrl = '/api/chessable';

  constructor(private http: HttpClient) {}

  /** ADMIN: Bearer eines Users testen (zugleich Circuit-Breaker-Reset dieses Users). */
  testUser(userId: number): Observable<ChessableTestResult> {
    return this.http.post<ChessableTestResult>(`${this.apiUrl}/admin/users/${userId}/test`, {});
  }

  /** Pollt den Status eines Imports. */
  getImport(id: number): Observable<ChessableImport> {
    return this.http.get<ChessableImport>(`${this.apiUrl}/imports/${id}`);
  }

  /** ADMIN: Nur aktive (laufende/pausierte) Importe aller User — fürs Dashboard. */
  getActiveImportsAdmin(): Observable<ChessableAdminImport[]> {
    return this.http.get<ChessableAdminImport[]>(`${this.apiUrl}/admin/active`);
  }

  /** ADMIN: Bricht einen wartenden/laufenden/pausierten Import eines beliebigen Users ab — auch mit
   *  `Chessable:Enabled=false` (der Nutzer-Weg `imports/{id}/cancel` antwortet dann 404). */
  cancelImportAdmin(id: number): Observable<ChessableAdminImport> {
    return this.http.post<ChessableAdminImport>(`${this.apiUrl}/admin/imports/${id}/cancel`, {});
  }

  /** ADMIN: User mit hinterlegtem Chessable-Bearer (für „Kurse von Usern holen"). */
  getCredentialedUsersAdmin(): Observable<ChessableCredentialedUser[]> {
    return this.http.get<ChessableCredentialedUser[]>(`${this.apiUrl}/admin/credentialed-users`);
  }

  /** ADMIN: Kursliste eines Users (mit dessen Bearer). */
  getUserCoursesAdmin(userId: number, refresh = false): Observable<ChessableCoursesResult> {
    return this.http.get<ChessableCoursesResult>(`${this.apiUrl}/admin/users/${userId}/courses`,
      refresh ? { params: { refresh: 'true' } } : {});
  }

  /** ADMIN: Lädt den Kurs {bid} eines Users ins eigene (Admin-)Konto — als Repertoire oder Buch. */
  importForUserAdmin(userId: number, bid: string, target: ChessableImportTarget, name: string): Observable<ChessableImport> {
    return this.http.post<ChessableImport>(
      `${this.apiUrl}/admin/users/${userId}/import/${encodeURIComponent(bid)}`, { target, name });
  }

  /** Vorab-Schätzung der Gesamt-Linienzahl eines Kurses (on-demand pro Zeile in der Admin-Kursliste). */
  estimateCourseForUser(userId: number, bid: string): Observable<ChessableCourseInfo> {
    return this.http.get<ChessableCourseInfo>(
      `${this.apiUrl}/admin/users/${userId}/courses/${encodeURIComponent(bid)}/estimate`);
  }
}
