import { Injectable, computed, inject, signal } from '@angular/core';
import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { Observable, catchError, map, of, shareReplay, switchMap } from 'rxjs';
import { AuthService } from '@rh/core/auth.service';

/**
 * LeagueHub für mehrere Vereine (0.698.0): der Verein, in dem das Konto gerade arbeitet. Der Server kennt ihn über `?club=`
 * (Haupt-CLAUDE.md „LeagueHub — Vereine als Mandanten"); diese Seite holt die Vereine des Kontos EINMAL (`GET /api/league/me`),
 * merkt die Wahl im localStorage (`lh-club`) und hängt sie per {@link leagueClubInterceptor} an jeden LeagueHub-Aufruf.
 * Auf den Teilen-Seiten ohne Anmeldung gilt der Verein des Links (`shareClub`, aus der Antwort von `GET /api/league/s/{token}`).
 */

export interface LeagueClubInfo {
  id: number;
  name: string;
  /** Unter diesem Namen erscheint ein anonymisierter Spieler des Vereins („Schwaz", „Weilheim"). */
  anonName: string;
  /** Wie die Mannschaften des Vereins beginnen („Schwaz", „SK Weilheim"). */
  teamPrefix?: string;
  /** `tirol` = chess-results, `bayern` = Ligamanager + Schachkreis Zugspitze (seit 0.704.0; vorher `source`). */
  region?: string | null;
}

/** `GET /api/league/me`. */
export interface LeagueMe {
  clubs: LeagueClubInfo[];
  current: number | null;
}

export const CLUB_STORAGE_KEY = 'lh-club';

/** Welcher Verein gilt: der gemerkte (wenn das Konto noch dazugehört), sonst der des Servers, sonst der erste. */
export function pickClub(me: LeagueMe, remembered: number | null): LeagueClubInfo | null {
  return me.clubs.find(c => c.id === remembered) ?? me.clubs.find(c => c.id === me.current) ?? me.clubs[0] ?? null;
}

/** SPIEGEL von `LeagueClub.OwnsTeam`: Mannschaft = Anfang oder beginnt mit Anfang + Leerzeichen/„/" (ohne Groß/klein). */
export function ownsTeam(prefix: string | null | undefined, team: string | null | undefined): boolean {
  const clean = (s: string) => s.replace(/\s+/g, ' ').trim();
  const p = clean(prefix ?? '').toLowerCase();
  const t = clean(team ?? '').replace(/[/\- ]+$/, '').toLowerCase();
  return p.length > 0 && (t === p || t.startsWith(p + ' ') || t.startsWith(p + '/'));
}

/** Braucht dieser Aufruf den Verein? Alle LeagueHub-Endpunkte außer `/me` und den Teilen-Links (`/s/…`, Verein des Links). */
export function needsClub(url: string): boolean {
  const path = url.split('?')[0].replace(/^https?:\/\/[^/]+/, '');
  return path.startsWith('/api/league/') && !path.startsWith('/api/league/s/') && path !== '/api/league/me';
}

function readRemembered(): number | null {
  try {
    const v = Number(localStorage.getItem(CLUB_STORAGE_KEY));
    return Number.isInteger(v) && v > 0 ? v : null;
  } catch { return null; }
}

function remember(id: number): void {
  try { localStorage.setItem(CLUB_STORAGE_KEY, String(id)); } catch { /* ohne Speicher: nur für diese Seite */ }
}

@Injectable({ providedIn: 'root' })
export class ClubContextService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);

  /** Die Vereine des angemeldeten Kontos (Admin: alle). */
  readonly clubs = signal<LeagueClubInfo[]>([]);
  /** Der gewählte Verein des angemeldeten Kontos. */
  readonly current = signal<LeagueClubInfo | null>(null);
  /** Der Verein eines Teilen-Links (Seiten unter `/s/:token`) — schlägt den angemeldeten. */
  readonly shareClub = signal<LeagueClubInfo | null>(null);
  /** `/api/league/me` ist beantwortet (auch: ohne Verein). */
  readonly loaded = signal(false);

  readonly club = computed(() => this.shareClub() ?? this.current());
  /** Der Ersatzname anonymisierter Spieler — bis der Verein da ist, neutral. */
  readonly anonName = computed(() => this.club()?.anonName ?? 'Verein');
  readonly clubName = computed(() => this.club()?.name ?? 'dem Verein');

  private pending: Observable<number | null> | null = null;
  private pendingFor: number | null = null;

  /** Die Vereine des Kontos laden (einmal je Konto) → die Id des gewählten Vereins, ohne Anmeldung/Verein `null`. */
  ensure(): Observable<number | null> {
    const uid = this.auth.currentUser?.userId ?? null;
    if (uid == null) return of(null);
    if (this.pending && this.pendingFor === uid) return this.pending;
    this.pendingFor = uid;
    this.pending = this.http.get<LeagueMe>('/api/league/me').pipe(
      map(me => {
        // gemerkt, sonst der bisher gewählte (reload nach der Vereinsverwaltung), sonst die Vorgabe des Servers
        const pick = pickClub(me, readRemembered() ?? this.current()?.id ?? null);
        this.clubs.set(me.clubs ?? []);
        this.current.set(pick);
        this.loaded.set(true);
        return pick?.id ?? null;
      }),
      catchError(() => {
        // beim nächsten Aufruf neu fragen (Netz weg, Server neu gestartet) — bis dahin ohne Verein, der Server wählt selbst
        this.pending = null;
        this.loaded.set(true);
        return of(null);
      }),
      shareReplay(1),
    );
    return this.pending;
  }

  /** Die Vereine neu holen (nach Anlegen/Ändern/Zuordnen in `/vereine`, 0.700.0): der Umschalter kennt dann den neuen Verein.
   *  Der gewählte Verein bleibt, solange das Konto noch dazugehört. */
  reload(): Observable<number | null> {
    this.pending = null;
    this.pendingFor = null;
    return this.ensure();
  }

  /** Verein wechseln (Umschalter im Kopf): gemerkt; die Seite lädt danach neu, damit nichts vom alten Verein stehen bleibt. */
  select(id: number): void {
    const club = this.clubs().find(c => c.id === id);
    if (!club) return;
    remember(id);
    this.current.set(club);
  }

  /** Teilen-Seiten: der Verein des Links (aus `GET /api/league/s/{token}` → `club`). */
  useShareClub(club: LeagueClubInfo | null | undefined): void {
    this.shareClub.set(club ?? null);
  }

  private shareFor: string | null = null;

  /** Teilen-Seiten ohne eigene Begegnungs-Abfrage (Hochladen, Formular): den Verein des Links einmal holen. */
  useShare(token: string): void {
    if (this.shareFor === token) return;
    this.shareFor = token;
    this.http.get<{ club?: LeagueClubInfo | null }>(`/api/league/s/${encodeURIComponent(token)}`).subscribe({
      next: r => this.shareClub.set(r?.club ?? null),
      error: () => { this.shareFor = null; },   // abgelaufener Link: die Seite sagt es selbst
    });
  }
}

/** Hängt `?club=` an jeden LeagueHub-Aufruf, der einen Verein braucht (wartet einmal auf `/api/league/me`). */
export const leagueClubInterceptor: HttpInterceptorFn = (req, next) => {
  if (!needsClub(req.url) || req.params.has('club')) return next(req);
  const ctx = inject(ClubContextService);
  return ctx.ensure().pipe(switchMap(id => next(id == null ? req : req.clone({ setParams: { club: String(id) } }))));
};
