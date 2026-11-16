import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, shareReplay } from 'rxjs';

/** Eindeutige Benutzer eines Landes (ISO 3166, zwei Großbuchstaben); `hits` bekommt nur der Admin. */
export interface VisitorCountry {
  country: string;
  users: number;
  hits: number | null;
}

/** Antwort von `GET /api/visitor-origins` (`VisitorOriginsDto`). */
export interface VisitorOrigins {
  /** Darf dieses Konto Zeitraum und Oberfläche wählen? Der Server verwirft die Parameter sonst. */
  admin: boolean;
  days: number | null;
  surface: string | null;
  uniqueUsers: number;
  countries: number;
  unknownUsers: number;
  hits: number | null;
  /** Erster erfasster Tag (YYYY-MM-DD); `null`, solange nichts gezählt ist. */
  since: string | null;
  byCountry: VisitorCountry[];
}

/** Kartenausschnitte: die Welt und die sechs Kontinente (Kürzel wie im Karten-Asset). */
export const VISITOR_VIEWS = ['world', 'eu', 'na', 'sa', 'af', 'as', 'oc'] as const;
export type VisitorView = typeof VISITOR_VIEWS[number];

/** Oberflächen, wie der Server sie zählt — Spiegel von `VisitorOriginRecorder.Surfaces` (Literaltest auf beiden Seiten). */
export const VISITOR_SURFACES = ['rookhub', 'leaguehub', 'kidhub', 'clubhub', 'turnier'] as const;
/**
 * „Land“ der Besucher über ein bekanntes VPN-/Rechenzentrumsnetz — Spiegel von `VisitorNetworkClassifier.VpnCountry`
 * (Literaltest auf beiden Seiten). Steht in der Rangliste als „VPN“, färbt auf der Karte nichts ein und zählt nicht
 * als Land.
 */
export const VISITOR_VPN = 'XV';
/** Wählbare Zeiträume in Tagen — Spiegel von `VisitorOriginService.AllowedDays`. */
export const VISITOR_DAYS = [7, 30, 90, 365] as const;

/** Ein Land der Karte: ISO-Kürzel, Kontinent, SVG-Pfad. */
export interface MapCountry {
  id: string;
  c: Exclude<VisitorView, 'world'>;
  d: string;
}

/** Das Karten-Asset `/assets/geo/world-countries.json` (erzeugt von `tools/visitor-map/build_map.py`). */
export interface WorldMap {
  w: number;
  h: number;
  /** viewBox je Ausschnitt: x, y, Breite, Höhe. */
  views: Record<VisitorView, [number, number, number, number]>;
  countries: MapCountry[];
}

/** Zahl der Farbstufen der Karte (ohne „kein Wert“). */
export const VISITOR_STEPS = 5;

/**
 * Farbstufe 1…5 eines Werts (0 = kein Wert), logarithmisch gegen den größten Wert des Ausschnitts — so bleiben
 * kleine Länder neben einem großen unterscheidbar.
 */
export function visitorStep(value: number, max: number): number {
  if (!(value > 0) || !(max > 0)) return 0;
  const t = Math.log(value + 1) / Math.log(max + 1);
  return 1 + Math.min(VISITOR_STEPS - 1, Math.floor(t * VISITOR_STEPS));
}

@Injectable({ providedIn: 'root' })
export class VisitorOriginsService {
  private readonly http = inject(HttpClient);
  private map$?: Observable<WorldMap>;

  /** Länderzahlen. `days`/`surface` wirken nur für Admins (der Server verwirft sie sonst). */
  get(days: number | null = null, surface: string | null = null): Observable<VisitorOrigins> {
    const params: Record<string, string> = {};
    if (days != null) params['days'] = String(days);
    if (surface) params['surface'] = surface;
    return this.http.get<VisitorOrigins>('/api/visitor-origins', { params });
  }

  /** Die Kartengeometrie — einmal je Sitzung geladen; ein Fehlschlag wird nicht gemerkt. */
  map(): Observable<WorldMap> {
    return this.map$ ??= this.http.get<WorldMap>('/assets/geo/world-countries.json').pipe(
      shareReplay({ bufferSize: 1, refCount: false }),
    );
  }

  /** Nach einem Fehlschlag: beim nächsten Aufruf neu laden. */
  resetMap(): void {
    this.map$ = undefined;
  }
}
