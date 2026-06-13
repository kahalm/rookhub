import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface AdminUser {
  id: number;
  username: string;
  email: string;
  isAdmin: boolean;
  createdAt: string;
  groups: string[];
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface Book {
  id: number;
  fileName: string;
  displayName: string;
  difficulty: string | null;
  rating: number | null;
  minElo: number | null;
  maxElo: number | null;
  tags: string | null;
  description: string | null;
  forDaily: boolean;
  forRandom: boolean;
  forBlind: boolean;
  puzzleCount: number;
  /** Gruppen-Ids, die dieses Buch als Kurs sehen dürfen. */
  accessGroupIds: number[];
  createdAt: string;
  updatedAt: string;
}

export interface UpdateBook {
  displayName?: string;
  difficulty?: string | null;
  rating?: number | null;
  minElo?: number | null;
  maxElo?: number | null;
  tags?: string | null;
  description?: string | null;
  forDaily?: boolean;
  forRandom?: boolean;
  forBlind?: boolean;
}

export interface BookImportResult {
  books: { bookId: number; fileName: string; imported: number; skipped: number; invalid: number }[];
  totalImported: number;
  totalSkipped: number;
  totalInvalid: number;
}

export interface Group {
  id: number;
  name: string;
  description: string | null;
  memberCount: number;
  createdAt: string;
}

export interface GroupMember {
  userId: number;
  username: string;
}

export interface AdminConfig {
  kibanaUrl: string;
}

/** Tagespuzzle-Zuordnung (Auszug aus dem BookPuzzleDto), wie sie das Admin-UI anzeigt. */
export interface DailyPuzzleInfo {
  id: number;
  lineId: string;
  bookFileName: string;
  title: string | null;
  difficulty: string | null;
  bookRating: number | null;
}

/** Trainingsziel-Vorlage einer Gruppe (Puzzles/Buch = Min/Tag, Spielen = Partien/Woche + Wochenziel Tage). */
export interface GroupTrainingGoal {
  puzzleMinutes: number;
  bookMinutes: number;
  /** Wochenziel: Anzahl Rapid-/Classical-Partien pro ISO-Woche. */
  playGames: number;
  weeklyDaysTarget: number;
  source: 'none' | 'group' | 'personal';
  groupName: string | null;
}

/** Sichtbarkeitsstufe eines Menüeintrags (serialisiert wie das Server-Enum). */
export type MenuVisibilityLevel = 'All' | 'Registered' | 'Groups' | 'Admin';

export interface MenuItemConfig {
  key: string;
  level: MenuVisibilityLevel;
  groupIds: number[];
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  constructor(private http: HttpClient) {}

  /** Konfigurationswerte fürs Admin-UI (Kibana-Link aus dem Server-Env). */
  getConfig(): Observable<AdminConfig> {
    return this.http.get<AdminConfig>('/api/admin/config');
  }

  getUsers(search = '', page = 1, pageSize = 20): Observable<PagedResult<AdminUser>> {
    return this.http.get<PagedResult<AdminUser>>('/api/admin/users', {
      params: { search, page: page.toString(), pageSize: pageSize.toString() }
    });
  }

  deleteUser(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/users/${id}`);
  }

  toggleAdmin(id: number): Observable<AdminUser> {
    return this.http.post<AdminUser>(`/api/admin/users/${id}/toggle-admin`, {});
  }

  // --- Buch-Puzzles -----------------------------------------------------
  getBooks(): Observable<Book[]> {
    return this.http.get<Book[]>('/api/admin/books');
  }

  updateBook(id: number, dto: UpdateBook): Observable<Book> {
    return this.http.put<Book>(`/api/admin/books/${id}`, dto);
  }

  deleteBook(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/books/${id}`);
  }

  updateBookGroups(id: number, groupIds: number[]): Observable<number[]> {
    return this.http.put<number[]>(`/api/admin/books/${id}/groups`, { groupIds });
  }

  importBooks(files: FileList | File[]): Observable<BookImportResult> {
    const form = new FormData();
    for (const f of Array.from(files)) form.append('files', f, f.name);
    return this.http.post<BookImportResult>('/api/admin/books/import', form);
  }

  /** Stößt den einmaligen Backfill der normalisierten Puzzle-Tag-Tabelle an (Hintergrund-Job). */
  backfillPuzzleTags(): Observable<{ message: string }> {
    return this.http.post<{ message: string }>('/api/admin/puzzles/backfill-tags', {});
  }

  // --- Tagespuzzle ------------------------------------------------------
  /** Aktuell zugeordnetes Tagespuzzle eines UTC-Datums (yyyyMMdd oder 'today'). */
  getDailyPuzzle(date: string): Observable<DailyPuzzleInfo> {
    return this.http.get<DailyPuzzleInfo>(`/api/book-puzzles/daily/${date}`);
  }

  /** Generiert das Tagespuzzle eines Datums neu; mustert das bisherige aus. */
  regenerateDailyPuzzle(date: string): Observable<DailyPuzzleInfo> {
    return this.http.post<DailyPuzzleInfo>(`/api/admin/book-puzzles/daily/${date}/regenerate`, {});
  }

  // --- Menü-Sichtbarkeit ------------------------------------------------
  getMenuConfig(): Observable<MenuItemConfig[]> {
    return this.http.get<MenuItemConfig[]>('/api/admin/menu');
  }

  saveMenuConfig(items: MenuItemConfig[]): Observable<MenuItemConfig[]> {
    return this.http.put<MenuItemConfig[]>('/api/admin/menu', items);
  }

  // --- Gruppen ----------------------------------------------------------
  getGroups(): Observable<Group[]> {
    return this.http.get<Group[]>('/api/admin/groups');
  }

  createGroup(name: string, description: string | null): Observable<Group> {
    return this.http.post<Group>('/api/admin/groups', { name, description });
  }

  deleteGroup(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/groups/${id}`);
  }

  getGroupMembers(groupId: number): Observable<GroupMember[]> {
    return this.http.get<GroupMember[]>(`/api/admin/groups/${groupId}/members`);
  }

  addGroupMember(groupId: number, userId: number): Observable<void> {
    return this.http.post<void>(`/api/admin/groups/${groupId}/members/${userId}`, {});
  }

  removeGroupMember(groupId: number, userId: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/groups/${groupId}/members/${userId}`);
  }

  // --- Trainingsziel-Vorlage je Gruppe ----------------------------------
  getGroupTrainingGoal(groupId: number): Observable<GroupTrainingGoal> {
    return this.http.get<GroupTrainingGoal>(`/api/admin/groups/${groupId}/training-goal`);
  }

  setGroupTrainingGoal(groupId: number, goal: { puzzleMinutes: number; bookMinutes: number; playGames: number; weeklyDaysTarget: number }): Observable<GroupTrainingGoal> {
    return this.http.put<GroupTrainingGoal>(`/api/admin/groups/${groupId}/training-goal`, goal);
  }

  deleteGroupTrainingGoal(groupId: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/groups/${groupId}/training-goal`);
  }
}
