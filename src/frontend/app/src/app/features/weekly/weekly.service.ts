import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface WeeklyPost {
  id: number;
  title: string;
  fileName: string;
  fileSize: number;
  scheduledAt: string;
  createdAt: string;
  updatedAt: string;
}

export interface WeeklyPostDetail extends WeeklyPost {
  pgnContent: string;
}

@Injectable({ providedIn: 'root' })
export class WeeklyService {
  constructor(private http: HttpClient) {}

  getAll(): Observable<WeeklyPost[]> {
    return this.http.get<WeeklyPost[]>('/api/weekly-posts');
  }

  getById(id: number): Observable<WeeklyPostDetail> {
    return this.http.get<WeeklyPostDetail>(`/api/weekly-posts/${id}`);
  }

  /** scheduledAt als lokaler Wall-Clock-String "YYYY-MM-DDTHH:mm:ss" (ohne Zeitzone). */
  create(file: File, scheduledAt: string, title?: string): Observable<WeeklyPost> {
    const form = new FormData();
    form.append('file', file, file.name);
    form.append('scheduledAt', scheduledAt);
    if (title) form.append('title', title);
    return this.http.post<WeeklyPost>('/api/admin/weekly-posts', form);
  }

  update(id: number, dto: { title?: string; scheduledAt?: string }): Observable<WeeklyPost> {
    return this.http.put<WeeklyPost>(`/api/admin/weekly-posts/${id}`, dto);
  }

  delete(id: number): Observable<void> {
    return this.http.delete<void>(`/api/admin/weekly-posts/${id}`);
  }
}
