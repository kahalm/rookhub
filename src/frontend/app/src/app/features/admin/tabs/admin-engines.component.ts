import { ChangeDetectionStrategy, Component, DestroyRef, LOCALE_ID, NgZone, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../../core/snackbar.service';
import { apiErrorText } from '../../../core/api-error';
import { localStore, readJson, writeJson } from '../../../core/local-json-store';
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
  /** Gruppe (Maschine), siehe {@link EngineMachine}. */
  machineKey: string;
  schedule: EngineSchedule | null;
  /** Warum eine freie Engine nichts rechnet (`admin.engines.idle.*`), nur im Zustand `idle`. */
  idleReason: string | null;
  speed: EngineSpeedSplit;
  apiTokenId: number | null;
}

/** Ø kN/s der letzten 7 Tage, „normal“ und „Schonzeit“ (nach der heutigen Regel eingeordnet). */
export interface EngineSpeedSplit { normal: number | null; reduced: number | null }

export interface EngineSchedule {
  slot: number; total: number; scope: string; rule: string; timeZone: string; reportedAt: string; openNow: boolean; percentNow: number;
}

export interface EngineMachineSchedule {
  rule: string; timeZone: string; scope: string; total: number; reportedAt: string; reported: number; openNow: number;
  percentNow: number; nextChangeAt: string | null; nextPercent: number | null;
}

export interface EngineMachine {
  key: string; name: string; ownerUserId: number | null; ownerName: string | null; source: string; apiTokenId: number | null;
  tokenName: string | null; engineIds: string[]; online: number; total: number; working: number; threads: number; doneDay: number;
  busyPercentDay: number | null; speed: EngineSpeedSplit; schedule: EngineMachineSchedule | null;
}

export interface EngineToken {
  id: number; name: string; prefix: string; ownerUserId: number; ownerName: string | null; createdAt: string;
  lastUsedAt: string | null; expiresAt: string | null; expired: boolean; engines: { id: string; name: string; online: boolean | null }[];
}

export interface EngineHint {
  kind: 'stuckJobs' | 'notInList' | 'listCap' | 'staleToken'; engineId: string | null; name: string | null; userId: number | null;
  count: number; max: number | null; since: string | null; detail: string | null;
}

export interface EngineQuiet {
  enabled: boolean; quietNow: boolean; quietUntil: string | null; timeZone: string; windows: { day: number; from: string; to: string }[];
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
  machines: EngineMachine[];
  tokens: EngineToken[];
  hints: EngineHint[];
  quiet: EngineQuiet;
  /** Wartende ÷ fertige Aufträge der letzten Stunde (h); nur, wenn die Stunde belastbar war. */
  queueEmptyInHours: number | null;
  backgroundCap: number;
}

/** Bis wann ein Token als „gerade benutzt“ gilt (Token-Tabelle, „noch nicht zugeordnet“). */
export const TOKEN_RECENT_MS = 30 * 60_000;

/** Gemerkte auf-/zugeklappte Maschinen je Betrachter (`core/local-storage-keys.ts`). */
export const ENGINE_MACHINES_STORAGE_KEY = 'rh.adminEngines.machines';

/** Sperrzeit-Fenster lesbar: aufeinanderfolgende Tage mit gleicher Zeit zusammengefasst („Mo–Do 08:00–17:00; Fr 08:00–14:00“). */
export function formatQuietWindows(windows: { day: number; from: string; to: string }[], locale: string): string {
  const dayName = (d: number) => new Intl.DateTimeFormat(locale, { weekday: 'short', timeZone: 'UTC' })
    .format(new Date(Date.UTC(2026, 10, 1 + d)));            // 1.11.2026 = Sonntag (day 0)
  const order = (d: number) => (d + 6) % 7;                    // Montag zuerst
  const byTime = new Map<string, number[]>();
  for (const w of windows) {
    const k = `${w.from}–${w.to}`;
    byTime.set(k, [...(byTime.get(k) ?? []), w.day]);
  }
  const parts: string[] = [];
  for (const [time, days] of byTime) {
    const sorted = [...new Set(days)].sort((a, b) => order(a) - order(b));
    const runs: number[][] = [];
    for (const d of sorted) {
      const last = runs[runs.length - 1];
      if (last && order(d) === order(last[last.length - 1]) + 1) last.push(d); else runs.push([d]);
    }
    const label = runs.map(r => r.length > 1 ? `${dayName(r[0])}–${dayName(r[r.length - 1])}` : dayName(r[0])).join(', ');
    parts.push(`${label} ${time}`);
  }
  return parts.join('; ');
}

export const ENGINE_OVERVIEW_URL = '/api/admin/engines/overview';

/** `PUT` mit `{ userId, enabled }`: die Engine in die Hintergrund-Liste dieses Kontos aufnehmen bzw. daraus entfernen (0.951.0). */
export function engineBackgroundUrl(engineId: string): string {
  return `/api/admin/engines/${encodeURIComponent(engineId)}/background`;
}

/** Eine Zeile „Hintergrund-Liste“ auf der Engine-Karte: ein Konto und ob die Engine in seiner Liste steht. */
export interface EngineBackgroundRow {
  userId: number;
  name: string;
  /** Steht in der Liste dieses Kontos. */
  inList: boolean;
  /** Konto = Besitzer der Engine. */
  owner: boolean;
  /** Registrierte Engine in der Liste eines FREMDEN Kontos (der Worker kann sie dort nicht nutzen). */
  foreign: boolean;
}

/**
 * Die Konten, deren Hintergrund-Liste die Karte zeigt und umschalten lässt: jedes Konto, in dessen Liste die Engine steht
 * (dort „entfernen“), und der Besitzer, falls sie bei ihm fehlt (dort „aufnehmen“). Ohne eindeutigen Besitzer (Lichess-
 * Kennung in keiner oder mehreren Listen, verwaiste Kennung) gibt es kein „aufnehmen“. Besitzer zuerst.
 */
export function backgroundRows(e: EngineOverviewEngine): EngineBackgroundRow[] {
  const label = (id: number, name: string | null) => name || '#' + id;
  const rows: EngineBackgroundRow[] = e.backgroundFor.map(u => ({
    userId: u.userId, name: label(u.userId, u.name), inList: true, owner: u.userId === e.ownerUserId,
    foreign: e.registered && e.ownerUserId != null && u.userId !== e.ownerUserId,
  }));
  if (e.ownerUserId != null && !rows.some(r => r.owner))
    rows.push({ userId: e.ownerUserId, name: label(e.ownerUserId, e.ownerName), inList: false, owner: true, foreign: false });
  return rows.sort((a, b) => Number(b.owner) - Number(a.owner));
}

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
 * Durchsatz (letzte Stunde / 24 h / 7 Tage). Seit 0.951.0 schaltet der Admin je Karte um, in welchen Hintergrund-Listen
 * die Engine steht ({@link toggleBackground}); die Pause der Hintergrundjobs wird im Reiter „Hintergrundjobs" umgelegt (verlinkt). Frischt sich alle {@link ENGINE_OVERVIEW_POLL_MS} auf, nur bei sichtbarem Tab.
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
  private readonly translate = inject(TranslateService);
  private readonly snackbar = inject(SnackbarService);

  readonly data = signal<EngineOverview | null>(null);
  readonly error = signal(false);
  /** Auffrischen gescheitert, alter Stand steht noch. */
  readonly stale = signal(false);
  readonly showOffline = signal(false);
  /** Laufendes Umschalten (`engineId|userId`); solange gesetzt, sind alle Hintergrund-Knöpfe gesperrt. */
  readonly saving = signal<string | null>(null);
  private loading = false;
  private lastLoad = 0;
  /** Nur die Antwort des jüngsten Abrufs zählt — ein langsamer Takt-Abruf darf den Stand nach dem Umschalten nicht überschreiben. */
  private loadSeq = 0;

  readonly engines = computed(() => this.data()?.engines ?? []);
  /** Ausdrücklich auf-/zugeklappte Maschinen (Schlüssel → offen); fehlt eine, ist sie offen. Ganz offline Maschinen
   *  verschwinden ohnehin mit „offline ausblenden“. */
  readonly machineOpen = signal<Record<string, boolean>>(readJson<Record<string, boolean>>(localStore(), ENGINE_MACHINES_STORAGE_KEY) ?? {});
  /** Maschinen mit ihren sichtbaren Engines (Offline-Engines wie bisher eingeklappt). */
  readonly machineGroups = computed(() => {
    const d = this.data();
    if (!d) return [];
    const visible = new Set(this.visibleEngines().map(e => e.id));
    const byId = new Map(this.engines().map(e => [e.id, e]));
    return (d.machines ?? []).map(m => ({
      machine: m,
      engines: m.engineIds.filter(id => visible.has(id)).map(id => byId.get(id)!).filter(Boolean),
    })).filter(g => g.engines.length > 0);
  });
  readonly quietText = computed(() => {
    const q = this.data()?.quiet;
    return q && q.enabled ? formatQuietWindows(q.windows, this.locale) : '';
  });
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

  /** @param force auch während eines laufenden Abrufs (nach dem Umschalten); die ältere Antwort wird dann verworfen. */
  async load(force = false): Promise<void> {
    if (this.loading && !force) return;
    this.loading = true;
    this.lastLoad = Date.now();
    const seq = ++this.loadSeq;
    try {
      const d = await firstValueFrom(this.http.get<EngineOverview>(ENGINE_OVERVIEW_URL));
      if (seq !== this.loadSeq) return;
      this.data.set(d);
      this.error.set(false);
      this.stale.set(false);
    } catch {
      if (seq !== this.loadSeq) return;
      // Erstes Laden gescheitert → Fehlerzustand; danach bleibt der alte Stand mit Hinweis stehen.
      if (this.data()) this.stale.set(true);
      else this.error.set(true);
    } finally {
      if (seq === this.loadSeq) this.loading = false;
    }
  }

  /**
   * Die Engine in die Hintergrund-Liste von `row` aufnehmen bzw. daraus entfernen. Ohne Rückfrage: umkehrbar (DESIGN 5.4).
   * Während des Speicherns sind alle Hintergrund-Knöpfe gesperrt; danach wird die Übersicht neu geladen (der Server hat
   * seinen Zwischenspeicher verworfen). Fehler → Snackbar.
   */
  async toggleBackground(e: EngineOverviewEngine, row: EngineBackgroundRow): Promise<void> {
    if (this.saving()) return;
    const enabled = !row.inList;
    this.saving.set(e.id + '|' + row.userId);
    try {
      await firstValueFrom(this.http.put(engineBackgroundUrl(e.id), { userId: row.userId, enabled }));
      this.snackbar.quick(this.translate.instant(enabled ? 'admin.engines.bgAdded' : 'admin.engines.bgRemoved', { name: row.name }));
      await this.load(true);
    } catch (err) {
      this.snackbar.show(apiErrorText(err, this.translate, 'admin.engines.bgError'));
    } finally {
      this.saving.set(null);
    }
  }

  bgRows(e: EngineOverviewEngine): EngineBackgroundRow[] { return backgroundRows(e); }

  isSaving(e: EngineOverviewEngine, row: EngineBackgroundRow): boolean { return this.saving() === e.id + '|' + row.userId; }

  retry(): void {
    this.error.set(false);
    void this.load();
  }

  toggleOffline(): void { this.showOffline.update(v => !v); }

  isMachineOpen(m: EngineMachine): boolean {
    return this.machineOpen()[m.key] ?? true;
  }

  toggleMachine(m: EngineMachine): void {
    const next = { ...this.machineOpen(), [m.key]: !this.isMachineOpen(m) };
    this.machineOpen.set(next);
    writeJson(localStore(), ENGINE_MACHINES_STORAGE_KEY, next);
  }

  onlineCount(t: EngineToken): number { return t.engines.filter(e => e.online).length; }

  /**
   * Token ohne zugeordnete Engine, aber kürzlich benutzt (≤ {@link TOKEN_RECENT_MS} vor dem Stand): ein Provider läuft
   * damit, seine Engines sind nur noch nicht verknüpft (Registrierung von vor 0.953.0) — der Server trägt das beim
   * nächsten Poll nach. „Letzter Zugriff“ ist auf 5 Minuten genau.
   */
  recentlyUsed(t: EngineToken): boolean {
    const d = this.data();
    if (!t.lastUsedAt || !d) return false;
    return Date.parse(d.generatedAt) - Date.parse(t.lastUsedAt) <= TOKEN_RECENT_MS;
  }

  /** „stuckJobs“-Detail: Grund und (nach „|“) der letzte Fehlertext. */
  hintReason(h: EngineHint): string { return (h.detail ?? '').split('|')[0]; }
  hintError(h: EngineHint): string | null { const i = (h.detail ?? '').indexOf('|'); return i < 0 ? null : h.detail!.slice(i + 1); }

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

  /** Anzeigename: Name, sonst die Kennung. */
  title(e: EngineOverviewEngine): string { return e.name || e.id; }
}
