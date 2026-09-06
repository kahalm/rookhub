import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { SearchProfile, SearchProfileInput } from './tournament-directory.model';

/** Gespeicherte Umkreise („Zuhause 100 km"). Sie steuern Ansicht UND naechtliche Meldung. */
@Injectable({ providedIn: 'root' })
export class SearchProfileService {
  constructor(private http: HttpClient) {}

  list(): Observable<SearchProfile[]> {
    return this.http.get<SearchProfile[]>('/api/tournament-search-profiles');
  }

  create(input: SearchProfileInput): Observable<SearchProfile> {
    return this.http.post<SearchProfile>('/api/tournament-search-profiles', input);
  }

  update(id: number, input: SearchProfileInput): Observable<SearchProfile> {
    return this.http.put<SearchProfile>(`/api/tournament-search-profiles/${id}`, input);
  }

  remove(id: number): Observable<unknown> {
    return this.http.delete(`/api/tournament-search-profiles/${id}`);
  }
}
