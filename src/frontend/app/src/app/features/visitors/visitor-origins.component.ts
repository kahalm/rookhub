import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, signal, untracked } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatChipsModule } from '@angular/material/chips';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { LatestRequest } from '../../shared/latest-request.util';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import {
  MapCountry, VISITOR_DAYS, VISITOR_STEPS, VISITOR_SURFACES, VISITOR_VIEWS, VISITOR_VPN, VisitorCountry, VisitorOrigins,
  VisitorOriginsService, VisitorView, WorldMap, visitorStep,
} from './visitor-origins.service';

/** Ein Land, wie die Karte es zeichnet. */
interface MapShape {
  id: string;
  d: string;
  continent: Exclude<VisitorView, 'world'>;
  users: number;
  /** Farbstufe 0…5 im aktuellen Ausschnitt. */
  step: number;
  /** Gehört nicht zum gewählten Kontinent (blass gezeichnet). */
  dim: boolean;
}

/** Eine Zeile der Rangliste. */
interface RankRow {
  rank: number;
  country: string;
  name: string;
  users: number;
  hits: number | null;
  /** Balkenlänge in Prozent des größten Werts. */
  bar: number;
}

/** So viele Länder zeigt die Welt-Rangliste, bevor „N weitere Länder“ zusammenfasst. */
export const VISITOR_TOP = 10;

/**
 * Besucher-Herkunft (`/visitors`): Weltkarte mit eindeutigen Benutzern je Land in fünf Stufen, daneben die Rangliste.
 * Jedes angemeldete Konto sieht alles Erfasste über alle Oberflächen; die Filterleiste (Oberfläche, Zeitraum) bekommt
 * nur der Admin — der Server verwirft die Parameter anderer Konten. Die Kontinent-Wahl ist reine Ansicht (Ausschnitt,
 * Kennzahlen und Rangliste des Kontinents, gerechnet aus den gelieferten Länderzahlen) und steht allen offen.
 * Zustand in der URL: `?continent=eu` sowie für Admins `?days=30&surface=leaguehub`.
 */
@Component({
  selector: 'app-visitor-origins',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [MatButtonModule, MatCardModule, MatChipsModule, MatIconModule, TranslatePipe, LoadErrorComponent, LoadingSpinnerComponent],
  templateUrl: './visitor-origins.component.html',
  styleUrl: './visitor-origins.component.scss',
})
export class VisitorOriginsComponent {
  private readonly api = inject(VisitorOriginsService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly translate = inject(TranslateService);
  private readonly auth = inject(AuthService);
  private readonly request = new LatestRequest();
  private readonly mapRequest = new LatestRequest();

  readonly views = VISITOR_VIEWS;
  readonly surfaces = VISITOR_SURFACES;
  readonly dayOptions = VISITOR_DAYS;
  readonly steps = Array.from({ length: VISITOR_STEPS }, (_, i) => i + 1);

  private readonly query = toSignal(this.route.queryParamMap);

  /** Kartenausschnitt aus `?continent=` (unbekannt → Welt). */
  readonly view = computed<VisitorView>(() => {
    const raw = this.query()?.get('continent') ?? '';
    return (VISITOR_VIEWS as readonly string[]).includes(raw) ? raw as VisitorView : 'world';
  });
  /** Admin-Filter aus der URL; für andere Konten ohne Wirkung (der Server verwirft sie). */
  readonly days = computed<number | null>(() => {
    const n = Number(this.query()?.get('days'));
    return (VISITOR_DAYS as readonly number[]).includes(n) ? n : null;
  });
  readonly surface = computed<string | null>(() => {
    const raw = this.query()?.get('surface') ?? '';
    return (VISITOR_SURFACES as readonly string[]).includes(raw) ? raw : null;
  });

  readonly data = signal<VisitorOrigins | null>(null);
  readonly loading = signal(true);
  readonly error = signal<unknown>(null);
  readonly failed = signal(false);
  readonly map = signal<WorldMap | null>(null);
  readonly mapFailed = signal(false);
  /** Welt-Rangliste: alle Länder statt der ersten zehn. */
  readonly showAll = signal(false);
  /** Angetipptes Land (ISO) — seine Zahl steht in der Zeile unter der Karte. */
  readonly selected = signal<string | null>(null);
  /** Schwebender Hinweis am Mauszeiger: Lage in Prozent der Kartenfläche. */
  readonly tip = signal<{ x: number; y: number; text: string } | null>(null);

  /** Filterleiste nur für Admins: maßgeblich ist die Antwort des Servers, davor das eigene Konto. */
  readonly admin = computed(() => this.data()?.admin ?? this.auth.isAdmin);

  private readonly lang = computed(() => this.translate.currentLang() || this.translate.getFallbackLang() || 'en');
  private readonly numberFormat = computed(() => safe(() => new Intl.NumberFormat(this.lang())) ?? new Intl.NumberFormat('en'));
  private readonly regionNames = computed(() =>
    safe(() => new Intl.DisplayNames([this.lang()], { type: 'region', fallback: 'code' })));

  private readonly continentOf = computed(() => new Map((this.map()?.countries ?? []).map(c => [c.id, c.c])));

  /** Länder des Ausschnitts, absteigend nach Benutzern (die Welt: alle). */
  readonly countries = computed<VisitorCountry[]>(() => {
    const all = this.data()?.byCountry ?? [];
    const view = this.view();
    if (view === 'world') return all;
    const continentOf = this.continentOf();
    return all.filter(c => continentOf.get(c.country) === view);
  });

  /**
   * Eindeutige Benutzer des Ausschnitts. Welt: die Zahl des Servers (jeder einmal). Kontinent: Summe seiner Länder —
   * wer im Zeitraum aus zwei Ländern desselben Kontinents kam, zählt dort zweimal (bewusst: kein eigener Abruf).
   */
  readonly users = computed(() => this.view() === 'world'
    ? this.data()?.uniqueUsers ?? 0
    : this.countries().reduce((sum, c) => sum + c.users, 0));
  /** „VPN“ ist kein Land: zählt nicht bei „Länder“ und nicht für die Farbskala der Karte. */
  readonly countryCount = computed(() => this.countries().filter(c => c.country !== VISITOR_VPN).length);
  private readonly maxUsers = computed(() => this.countries().reduce((max, c) => Math.max(max, c.users), 0));
  private readonly maxMapUsers = computed(() => this.countries()
    .reduce((max, c) => c.country === VISITOR_VPN ? max : Math.max(max, c.users), 0));

  /** Noch gar nichts gezählt (für diese Auswahl). */
  readonly empty = computed(() => (this.data()?.uniqueUsers ?? 0) === 0);

  readonly shapes = computed<MapShape[]>(() => {
    const map = this.map();
    if (!map) return [];
    const view = this.view();
    const max = this.maxMapUsers();
    const users = new Map((this.data()?.byCountry ?? []).map(c => [c.country, c.users]));
    return map.countries.map((c: MapCountry) => {
      const dim = view !== 'world' && c.c !== view;
      const n = users.get(c.id) ?? 0;
      return { id: c.id, d: c.d, continent: c.c, users: n, step: dim ? 0 : visitorStep(n, max), dim };
    });
  });

  readonly viewBox = computed(() => (this.map()?.views[this.view()] ?? [0, 0, 960, 400]).join(' '));
  /** Seitenverhältnis des Ausschnitts — die Kartenfläche hält es (gedeckelt in der Höhe). */
  readonly aspect = computed(() => {
    const [, , w, h] = this.map()?.views[this.view()] ?? [0, 0, 960, 400];
    return `${w} / ${h}`;
  });

  readonly rows = computed<RankRow[]>(() => {
    const max = this.maxUsers();
    const all = this.countries().map((c, i) => ({
      rank: i + 1, country: c.country, name: this.countryName(c.country), users: c.users, hits: c.hits,
      bar: max > 0 ? Math.max(2, Math.round(c.users / max * 100)) : 0,
    }));
    return this.limited() ? all.slice(0, VISITOR_TOP) : all;
  });
  /** Die Welt-Rangliste zeigt zehn Länder und fasst den Rest zusammen; ein Kontinent zeigt alle. */
  private readonly limited = computed(() => this.view() === 'world' && !this.showAll() && this.countries().length > VISITOR_TOP);
  readonly rest = computed(() => {
    if (!this.limited()) return null;
    const tail = this.countries().slice(VISITOR_TOP);
    return {
      count: tail.length,
      users: tail.reduce((sum, c) => sum + c.users, 0),
      hits: tail.reduce((sum, c) => sum + (c.hits ?? 0), 0),
    };
  });
  readonly canCollapse = computed(() => this.view() === 'world' && this.showAll() && this.countries().length > VISITOR_TOP);

  /** Zeile unter der Karte: Land + Zahl des angetippten Landes. */
  readonly selectedText = computed(() => {
    const id = this.selected();
    return id ? this.countryLine(id) : null;
  });

  readonly sinceText = computed(() => {
    const since = this.data()?.since;
    if (!since) return null;
    const date = new Date(since + 'T00:00:00');
    return safe(() => new Intl.DateTimeFormat(this.lang(), { dateStyle: 'medium' }).format(date)) ?? since;
  });

  constructor() {
    // Neu laden, wenn der Admin Zeitraum oder Oberfläche wechselt (die Kontinent-Wahl lädt nichts).
    effect(() => {
      const days = this.days();
      const surface = this.surface();
      untracked(() => this.load(days, surface));
    });
    this.loadMap();
    inject(DestroyRef).onDestroy(() => { this.request.cancel(); this.mapRequest.cancel(); });
  }

  reload(): void {
    this.load(this.days(), this.surface());
    if (!this.map()) this.loadMap();
  }

  private load(days: number | null, surface: string | null): void {
    this.loading.set(true);
    this.failed.set(false);
    this.request.run(this.api.get(days, surface), {
      next: data => { this.data.set(data); this.loading.set(false); },
      error: err => { this.error.set(err); this.failed.set(true); this.loading.set(false); },
    });
  }

  private loadMap(): void {
    this.mapFailed.set(false);
    this.mapRequest.run(this.api.map(), {
      next: map => this.map.set(map),
      // Ohne Karte bleiben Kennzahlen und Rangliste — die Seite sagt, dass die Karte fehlt.
      error: () => { this.api.resetMap(); this.mapFailed.set(true); },
    });
  }

  format(n: number | null | undefined): string {
    return this.numberFormat().format(n ?? 0);
  }

  /** Ländername in der Oberflächensprache (`Intl.DisplayNames`), sonst das ISO-Kürzel; `XV` heißt „VPN“. */
  countryName(code: string): string {
    if (code === VISITOR_VPN) return this.translate.instant('visitors.vpn');
    return safe(() => this.regionNames()?.of(code)) ?? code;
  }

  private countryLine(id: string): string {
    const users = this.data()?.byCountry.find(c => c.country === id)?.users ?? 0;
    return this.translate.instant('visitors.countryValue', { country: this.countryName(id), count: this.format(users) });
  }

  // ── Wahl (alles über die URL) ──────────────────────────────────────────────────────────────────

  goView(view: VisitorView | undefined): void {
    const next = view ?? 'world';
    if (next !== this.view()) this.selected.set(null);
    this.tip.set(null);
    this.navigate({ continent: next === 'world' ? null : next });
  }

  setDays(days: number | null | undefined): void {
    this.navigate({ days: days ?? null });
  }

  setSurface(surface: string | null | undefined): void {
    this.navigate({ surface: surface || null });
  }

  private navigate(queryParams: Record<string, string | number | null>): void {
    void this.router.navigate([], { relativeTo: this.route, queryParams, queryParamsHandling: 'merge' });
  }

  // ── Karte ──────────────────────────────────────────────────────────────────────────────────────

  private shapeAt(event: Event): MapShape | null {
    const id = (event.target as Element | null)?.getAttribute?.('data-id');
    return id ? this.shapes().find(s => s.id === id) ?? null : null;
  }

  /** Klick/Tap auf ein Land: aus der Welt (oder auf ein blasses Nachbarland) in dessen Kontinent, sonst seine Zahl zeigen. */
  onMapClick(event: Event): void {
    const shape = this.shapeAt(event);
    if (!shape) return;
    if (this.view() !== shape.continent) {
      this.goView(shape.continent);
      this.selected.set(shape.id);
    } else {
      this.selected.set(this.selected() === shape.id ? null : shape.id);
    }
  }

  /** Mauszeiger über einem Land: Name + Zahl am Zeiger. Lage als Anteil der Fläche (DESIGN 3.7: keine px zurückschreiben). */
  onMapMove(event: MouseEvent): void {
    const shape = this.shapeAt(event);
    const box = (event.currentTarget as Element | null)?.getBoundingClientRect();
    if (!shape || !box || box.width === 0 || box.height === 0) { this.tip.set(null); return; }
    this.tip.set({
      x: Math.min(100, Math.max(0, (event.clientX - box.left) / box.width * 100)),
      y: Math.min(100, Math.max(0, (event.clientY - box.top) / box.height * 100)),
      text: this.countryLine(shape.id),
    });
  }
}

/** `Intl` kann bei unbekannter Sprache oder altem Browser werfen — dann gilt der Rückfall des Aufrufers. */
function safe<T>(make: () => T | undefined): T | undefined {
  try { return make(); } catch { return undefined; }
}
