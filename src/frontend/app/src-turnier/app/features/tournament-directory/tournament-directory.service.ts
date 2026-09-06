import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import {
  DirectoryCalendarDay, DirectoryCalendarResponse, DirectoryEntry, DirectoryFilter, DirectoryPage,
  GeoPlaceSuggestion,
} from './tournament-directory.model';

/**
 * HTTP-Zugriff aufs Turnierverzeichnis. Alle drei Ansichten (Liste, Karte, Kalender) teilen sich
 * denselben Filter — deshalb baut `toParams` die Query zentral: sonst driften die Ansichten
 * auseinander und zeigen bei identischer Filterleiste unterschiedliche Turniere.
 */
@Injectable({ providedIn: 'root' })
export class TournamentDirectoryService {
  constructor(private http: HttpClient) {}

  search(filter: DirectoryFilter, page = 1, pageSize = 50): Observable<DirectoryPage> {
    const params = this.toParams(filter).set('page', page).set('pageSize', pageSize);
    return this.http.get<DirectoryPage>('/api/tournament-directory', { params });
  }

  map(filter: DirectoryFilter, bbox: string, limit = 2000): Observable<DirectoryEntry[]> {
    // Der Umkreis geht NICHT mit: die Karte zeigt, was im sichtbaren Ausschnitt liegt.
    const { lat, lon, radiusKm, ...rest } = filter;
    const params = this.toParams(rest as DirectoryFilter).set('bbox', bbox).set('limit', limit);
    return this.http.get<DirectoryEntry[]>('/api/tournament-directory/map', { params });
  }

  calendar(filter: DirectoryFilter, year: number, month: number): Observable<DirectoryCalendarDay[]> {
    // Jahr und Monat bestimmen den Zeitraum — from/to aus der Filterleiste wären hier widersprüchlich.
    const { from, to, ...rest } = filter;
    const params = this.toParams(rest as DirectoryFilter).set('year', year).set('month', month);
    return this.http.get<DirectoryCalendarResponse>('/api/tournament-directory/calendar', { params })
      .pipe(map(res => expandCalendar(res)));
  }

  get(chessResultsId: string): Observable<DirectoryEntry> {
    return this.http.get<DirectoryEntry>(`/api/tournament-directory/${chessResultsId}`);
  }

  places(term: string): Observable<GeoPlaceSuggestion[]> {
    return this.http.get<GeoPlaceSuggestion[]>('/api/tournament-directory/places',
      { params: new HttpParams().set('q', term) });
  }

  private toParams(filter: Partial<DirectoryFilter>): HttpParams {
    let params = new HttpParams();
    if (filter.from) params = params.set('from', filter.from);
    if (filter.to) params = params.set('to', filter.to);
    if (filter.lat != null && filter.lon != null && filter.radiusKm) {
      params = params.set('lat', filter.lat).set('lon', filter.lon).set('radiusKm', filter.radiusKm);
    }
    if (filter.federation) params = params.set('fed', filter.federation);
    if (filter.speed) params = params.set('speed', filter.speed);
    if (filter.text) params = params.set('q', filter.text);
    if (filter.weekendOnly) params = params.set('weekendOnly', true);
    if (filter.minPlayers) params = params.set('minPlayers', filter.minPlayers);
    if (filter.profileId) params = params.set('profileId', filter.profileId);
    return params;
  }
}

/**
 * Setzt den Monat wieder zu Tagen mit Turnieren zusammen. Dasselbe Turnier steht an mehreren Tagen
 * als DASSELBE Objekt — die Ansicht vergleicht Eintraege ueber `chessResultsId`, aber Kopien waeren
 * genau die Verschwendung, die auf der Leitung gerade abgeschafft wurde. Eine Nummer ohne
 * Beschreibung wird uebergangen statt als Luecke gerendert.
 */
export function expandCalendar(res: DirectoryCalendarResponse): DirectoryCalendarDay[] {
  const byId = new Map((res.tournaments ?? []).map(t => [t.chessResultsId, t]));
  return (res.days ?? []).map(day => ({
    date: day.date,
    items: (day.ids ?? [])
      .map(id => byId.get(id))
      .filter((e): e is DirectoryEntry => e !== undefined),
  }));
}
