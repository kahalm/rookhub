import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface AdminUser {
  id: number;
  username: string;
  email: string;
  isAdmin: boolean;
  createdAt: string;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface RequestLog {
  id: number;
  timestamp: string;
  method: string;
  path: string;
  queryString: string | null;
  statusCode: number;
  durationMs: number;
  userName: string | null;
  ipAddress: string | null;
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
  books: { bookId: number; fileName: string; imported: number; skipped: number }[];
  totalImported: number;
  totalSkipped: number;
}

@Injectable({ providedIn: 'root' })
export class AdminService {
  constructor(private http: HttpClient) {}

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

  getRequestLogs(params: Record<string, string> = {}): Observable<PagedResult<RequestLog>> {
    return this.http.get<PagedResult<RequestLog>>('/api/request-logs', { params });
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

  importBooks(files: FileList | File[]): Observable<BookImportResult> {
    const form = new FormData();
    for (const f of Array.from(files)) form.append('files', f, f.name);
    return this.http.post<BookImportResult>('/api/admin/books/import', form);
  }
}
