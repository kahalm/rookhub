import { Injectable } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { Observable, map } from 'rxjs';
import {
  DirectoryCalendarDay, DirectoryCalendarResponse, DirectoryEntry, DirectoryFilter, DirectoryPage,
  DirectoryReport, GeoPlaceSuggestion,
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

  /**
   * Der naechstgelegene Ort zu Koordinaten — die Umkehrung der Ortssuche. Gebraucht fuer den
   * Browser-Standort: der liefert Koordinaten, im Ortsfeld soll aber ein NAME stehen. `null`
   * heisst „kein Ort im Lexikon in Reichweite" (Server: 204) — die Koordinaten gelten trotzdem.
   */
  nearestPlace(lat: number, lon: number): Observable<GeoPlaceSuggestion | null> {
    return this.http.get<GeoPlaceSuggestion>('/api/tournament-directory/places/nearest',
      { params: new HttpParams().set('lat', lat).set('lon', lon), observe: 'response' })
      .pipe(map(res => res.body ?? null));
  }

  /** „Falsches Event melden" — alle Felder freiwillig, auch der Text. */
  report(chessResultsId: string, report: DirectoryReport): Observable<void> {
    return this.http.post<void>(`/api/tournament-directory/${chessResultsId}/report`, report);
  }

  /** „Mein Turnier fehlt" — der Link ist Pflicht, er ist der verwertbare Teil. */
  suggestSource(link: string, message: string | null): Observable<void> {
    return this.http.post<void>('/api/tournament-directory/suggest-source', { link, message });
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
    // Kommagetrennt, wie der Server sie erwartet — leere Listen gar nicht senden, sonst
    // beantwortet ein `kinds=` die Frage „welche Arten" mit „keine".
    if (filter.kinds?.length) params = params.set('kinds', filter.kinds.join(','));
    if (filter.ageGroups?.length) params = params.set('ageGroups', filter.ageGroups.join(','));
    if (filter.genders?.length) params = params.set('genders', filter.genders.join(','));
    if (filter.adultsOnly) params = params.set('adultsOnly', true);
    if (filter.hideLeagues) params = params.set('hideLeagues', true);
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
