import { ChangeDetectionStrategy, Component, DestroyRef, LOCALE_ID, NgZone, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { LoadErrorComponent } from '../../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '../../../shared/help-hint/help-hint.component';
import { formatCount, formatElapsed, formatKiloNps } from '../../analysis/engine-lines.util';

/** Durchsatz eines Fensters (`EngineThroughputWindowDto`). */
export interface EngineThroughputWindow {
  runs: number; done: number; failed: number; interrupted: number; retried: number;
  unavailable: number; timeouts: number; errors: number;
  busySeconds: number;
  /** Mittlere Rechenzeit je fertigem Auftrag (s), `null` = keiner fertig. */
  avgJobSeconds: number | null;
  /** Knoten je Wanduhr-Sekunde in Tausend, `null` = keine Knotenangabe. */
  kiloNodesPerSecond: number | null;
}

export interface EngineThroughput { hour: EngineThroughputWindow; day: EngineThroughputWindow; week: EngineThroughputWindow }

export interface EngineCurrentJob {
  jobId: number; ownerUserId: number; ownerName: string | null; title: string | null; fen: string; kind: string;
  background: boolean; batch: boolean; targetDepth: number; targetNodes: number | null; multiPv: number;
  reachedDepth: number; depth: number; nps: number; nodes: number; seconds: number; runningSince: string | null;
  gameAnalysisId: number | null; ply: number | null; gameTitle: string | null;
}

export interface EngineBrokerCounters {
  jobs: number; completed: number; withoutBestmove: number; requesterGone: number; providerGone: number;
  providerTimeouts: number; queueFull: number; ongoingExpired: number;
}

export type EngineState = 'working' | 'live' | 'idle' | 'offline' | 'unknown';

export interface EngineOverviewEngine {
  id: string; name: string | null; source: 'rookhub' | 'lichess' | 'other'; registered: boolean;
  ownerUserId: number | null; ownerName: string | null; online: boolean | null; lastSeenAt: string | null;
  registeredAt: string | null; maxThreads: number | null; maxHash: number | null; variants: string[];
  backgroundFor: { userId: number; name: string | null }[]; houseEngine: boolean; explicitOnly: boolean;
  state: EngineState; liveStreams: number; providerPolling: number; brokerQueued: number;
  current: EngineCurrentJob | null; waitingQueued: number; waitingPaused: number; oldestWaitingSince: string | null;
  throughput: EngineThroughput; broker: EngineBrokerCounters | null;
}

export interface EngineQueueKind { kind: string; queued: number; paused: number; running: number; oldestWaitingSince: string | null }

/** Antwort von `GET /api/admin/engines/overview`. */
export interface EngineOverview {
  generatedAt: string;
  serverStartedAt: string;
  bucketMinutes: number;
  pause: { paused: boolean; forcedByConfig: boolean; since: string | null; by: string | null };
  queue: {
    queued: number; paused: number; running: number; oldestWaitingSince: string | null; byKind: EngineQueueKind[];
    gameAnalysesPending: number; gameAnalysesRunning: number;
  };
  totals: EngineThroughput;
  engines: EngineOverviewEngine[];
}

export const ENGINE_OVERVIEW_URL = '/api/admin/engines/overview';

/** Auffrischen alle 15 s, solange der Tab sichtbar ist (der Server hält die Antwort 10 s vor). */
export const ENGINE_OVERVIEW_POLL_MS = 15_000;

/** Symbol je Zustand — der Zustand steht nie nur als Farbe da (E-Ink, DESIGN 3.6). */
const STATE_ICON: Record<EngineState, string> = {
  working: 'memory', live: 'visibility', idle: 'pause_circle', offline: 'cloud_off', unknown: 'help_outline',
};

/** Fehlerzahl eines Fensters: alles, was nicht fertig, nicht von uns unterbrochen und nicht „wird fortgesetzt" ist. */
export function problemCount(w: EngineThroughputWindow): number {
  return w.failed + w.unavailable + w.timeouts + w.errors;
}

/**
 * Admin-Reiter „Engines" (0.946.0): alle registrierten Engines aller Konten — Quelle, Besitzer, online/offline, wer sie
 * im Hintergrund nutzt —, die Warteschlange der Analyse-Aufträge nach Art, was jede Engine gerade rechnet und der
 * Durchsatz (letzte Stunde / 24 h / 7 Tage). Nur lesend; die Pause der Hintergrundjobs wird im Reiter „Hintergrundjobs"
 * umgelegt (verlinkt). Frischt sich alle {@link ENGINE_OVERVIEW_POLL_MS} auf, nur bei sichtbarem Tab.
 */
@Component({
  selector: 'app-admin-engines',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, RouterLink, MatButtonModule, MatCardModule, MatIconModule, TranslatePipe, LoadErrorComponent,
    LoadingSpinnerComponent, HelpHintComponent],
  templateUrl: './admin-engines.component.html',
  styleUrls: ['./admin-engines.component.scss'],
})
export class AdminEnginesComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly locale = inject(LOCALE_ID);
  private readonly destroyRef = inject(DestroyRef);
  private readonly zone = inject(NgZone);

  readonly data = signal<EngineOverview | null>(null);
  readonly error = signal(false);
  /** Auffrischen gescheitert, alter Stand steht noch. */
  readonly stale = signal(false);
  readonly showOffline = signal(false);
  private loading = false;
  private lastLoad = 0;

  readonly engines = computed(() => this.data()?.engines ?? []);
  readonly offlineCount = computed(() => this.engines().filter(e => e.state === 'offline').length);
  readonly visibleEngines = computed(() => this.showOffline() ? this.engines() : this.engines().filter(e => e.state !== 'offline'));
  readonly counts = computed(() => {
    const c: Record<EngineState, number> = { working: 0, live: 0, idle: 0, offline: 0, unknown: 0 };
    for (const e of this.engines()) c[e.state]++;
    return c;
  });
  /** Zustände in der Kopfzeile: „rechnet" und „Leerlauf" immer, die übrigen nur mit Anzahl. */
  readonly shownStates = computed(() => {
    const c = this.counts();
    return (['working', 'live', 'idle', 'offline', 'unknown'] as EngineState[])
      .filter(s => s === 'working' || s === 'idle' || c[s] > 0);
  });
  readonly windows = computed(() => {
    const t = this.data()?.totals;
    return t ? [{ key: 'hour', w: t.hour }, { key: 'day', w: t.day }, { key: 'week', w: t.week }] : [];
  });

  ngOnInit(): void {
    void this.load();
    // Außerhalb der Zone: ein Dauer-Takt hielte die App sonst nie „stabil“ (whenStable, Service Worker). Die Antwort
    // landet in Signalen, die zeichnen auch ohne Zone neu.
    const timer = this.zone.runOutsideAngular(() =>
      setInterval(() => { if (this.visible()) this.zone.run(() => void this.load()); }, ENGINE_OVERVIEW_POLL_MS));
    const onVisible = () => {
      if (this.visible() && Date.now() - this.lastLoad >= ENGINE_OVERVIEW_POLL_MS) void this.load();
    };
    document.addEventListener('visibilitychange', onVisible);
    this.destroyRef.onDestroy(() => {
      clearInterval(timer);
      document.removeEventListener('visibilitychange', onVisible);
    });
  }

  private visible(): boolean {
    return typeof document === 'undefined' || document.visibilityState !== 'hidden';
  }

  async load(): Promise<void> {
    if (this.loading) return;
    this.loading = true;
    this.lastLoad = Date.now();
    try {
      this.data.set(await firstValueFrom(this.http.get<EngineOverview>(ENGINE_OVERVIEW_URL)));
      this.error.set(false);
      this.stale.set(false);
    } catch {
      // Erstes Laden gescheitert → Fehlerzustand; danach bleibt der alte Stand mit Hinweis stehen.
      if (this.data()) this.stale.set(true);
      else this.error.set(true);
    } finally {
      this.loading = false;
    }
  }

  retry(): void {
    this.error.set(false);
    void this.load();
  }

  toggleOffline(): void { this.showOffline.update(v => !v); }

  stateIcon(s: EngineState): string { return STATE_ICON[s] ?? 'help_outline'; }

  problems(w: EngineThroughputWindow): number { return problemCount(w); }

  count(n: number): string { return formatCount(n, this.locale); }

  kiloNps(nps: number): string { return formatKiloNps(nps, this.locale); }

  /** kN/s eines Fensters (Server liefert schon Tausend). */
  knps(k: number | null): string { return k == null ? '–' : formatCount(k, this.locale) + ' kN/s'; }

  duration(sec: number | null): string { return sec == null ? '–' : formatElapsed(sec); }

  /** Rechenzeit als Anteil der Fensterlänge („Auslastung"), ohne Nachkommastelle. */
  busyShare(w: EngineThroughputWindow, key: string): number {
    const len = key === 'hour' ? 3600 : key === 'day' ? 86_400 : 604_800;
    return Math.min(100, Math.round(w.busySeconds / len * 100));
  }

  /** Konten, in deren Hintergrund-Liste die Engine steht. */
  bgNames(e: EngineOverviewEngine): string {
    return e.backgroundFor.map(u => u.name || '#' + u.userId).join(', ');
  }

  /** Anzeigename: Name, sonst die Kennung. */
  title(e: EngineOverviewEngine): string { return e.name || e.id; }
}
