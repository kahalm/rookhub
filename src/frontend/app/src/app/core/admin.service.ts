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
  statusCode: number;
  durationMs: number;
  userName: string | null;
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
}
