import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { BehaviorSubject, Observable, of } from 'rxjs';
import { catchError, map, switchMap } from 'rxjs/operators';
import { AuthService } from './auth.service';

/**
 * Effektive Menü-Sichtbarkeit (admin-konfiguriert, serverseitig je Benutzer aufgelöst).
 * `visible$` füttert die Navbar (synchron via Snapshot), `check()` ist die frische
 * Abfrage für den Route-Guard. Wird bei jedem Login/Logout neu geladen.
 */
/** localStorage-Schlüssel für die zuletzt erfolgreich geladene Menü-Sichtbarkeit (Offline-Fallback). */
const MENU_CACHE_KEY = 'rookhub_menu_keys';

@Injectable({ providedIn: 'root' })
export class MenuService {
  // Mit dem zuletzt gecachten Stand starten, damit ein Offline-Kaltstart (Flugmodus) sofort
  // ein vollständiges Menü hat, statt auf die — offline scheiternde — /api/menu-Antwort zu warten.
  private visibleSubject = new BehaviorSubject<Set<string>>(this.loadCache());
  /** Aktuell sichtbare Menü-Keys (für Template-Bindings). */
  visible$ = this.visibleSubject.asObservable();

  constructor(private http: HttpClient, private auth: AuthService) {
    // Bei jedem Auth-Wechsel (inkl. Start: currentUser$ ist BehaviorSubject) neu laden.
    this.auth.currentUser$.pipe(switchMap(() => this.fetch())).subscribe(set => this.visibleSubject.next(set));
  }

  private fetch(): Observable<Set<string>> {
    return this.http.get<string[]>('/api/menu').pipe(
      map(keys => {
        this.saveCache(keys);   // letzten guten Stand für Offline merken
        return new Set(keys);
      }),
      // Offline / Serverfehler: nicht das Menü leeren, sondern den gecachten Stand behalten.
      catchError(() => of(this.loadCache())),
    );
  }

  /** Zuletzt gecachte Menü-Keys (leeres Set, wenn nie geladen / kaputt). */
  private loadCache(): Set<string> {
    try {
      const raw = localStorage.getItem(MENU_CACHE_KEY);
      const arr = raw ? JSON.parse(raw) : [];
      return Array.isArray(arr) ? new Set<string>(arr) : new Set<string>();
    } catch { return new Set<string>(); }
  }

  private saveCache(keys: string[]): void {
    try { localStorage.setItem(MENU_CACHE_KEY, JSON.stringify(keys)); } catch { /* Quota/Privatmodus */ }
  }

  /** Synchroner Snapshot — true, wenn der Eintrag aktuell sichtbar ist. */
  isVisible(key: string): boolean {
    return this.visibleSubject.value.has(key);
  }

  /** Neu laden (z. B. nachdem ein Admin die Konfiguration geändert hat). */
  refresh(): void {
    this.fetch().subscribe(set => this.visibleSubject.next(set));
  }

  /** Frische, autoritative Sichtbarkeitsprüfung für den Route-Guard. */
  check(key: string): Observable<boolean> {
    return this.fetch().pipe(map(set => set.has(key)));
  }
}
