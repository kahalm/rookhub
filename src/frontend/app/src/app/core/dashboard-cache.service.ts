import { Injectable } from '@angular/core';
import { AuthService } from './auth.service';
import { DashboardCourse } from './dashboard.service';
import { Subscription } from './models';

/**
 * Zwischenstand des Dashboards: wird nach jedem erfolgreichen Datenladen in localStorage
 * gespeichert und beim nächsten Aufruf SYNCHRON gelesen, damit die Kacheln nicht erst
 * eine halbe Sekunde leer bleiben. Frische Werte überschreiben den Cache dann normal.
 *
 * Bewusst pro User gekeyed (`rookhub_dashboard_cache_v1_u{userId}`) — beim Login-/Konto-Wechsel
 * werden fremde Snapshots nicht angezeigt. `v1` erlaubt Bump ohne Schema-Migration.
 */
export interface DashboardSnapshot {
  repertoireCount: number;
  courseCount: number;
  pinnedCourses: DashboardCourse[];
  subscriptionCount: number;
  subscriptions: Subscription[];
  friendCount: number;
  favoriteCount: number;
  puzzleSolved: number;
  puzzleAccuracy: number;
  puzzleElo: number;
}

const KEY_PREFIX = 'rookhub_dashboard_cache_v1_u';

@Injectable({ providedIn: 'root' })
export class DashboardCacheService {
  constructor(private auth: AuthService) {}

  private keyForCurrentUser(): string | null {
    const uid = this.auth.currentUser?.userId;
    return uid ? `${KEY_PREFIX}${uid}` : null;
  }

  /** Zuletzt gespeicherten Snapshot lesen; null wenn nichts da / kaputt / nicht eingeloggt. */
  load(): DashboardSnapshot | null {
    const key = this.keyForCurrentUser();
    if (!key) return null;
    try {
      const raw = localStorage.getItem(key);
      if (!raw) return null;
      const parsed = JSON.parse(raw) as DashboardSnapshot;
      if (!parsed || typeof parsed !== 'object') return null;
      return parsed;
    } catch {
      return null;
    }
  }

  save(snap: DashboardSnapshot): void {
    const key = this.keyForCurrentUser();
    if (!key) return;
    try {
      localStorage.setItem(key, JSON.stringify(snap));
    } catch {
      /* Quota / privater Modus — Cache ist rein optional, still ignorieren. */
    }
  }
}
