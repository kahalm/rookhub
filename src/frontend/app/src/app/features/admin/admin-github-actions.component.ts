import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { timer, switchMap, catchError, of } from 'rxjs';

export interface CiRun {
  id: number; name: string; title: string; branch: string; event: string;
  status: string; conclusion: string | null; runNumber: number;
  createdAt: string; updatedAt: string; htmlUrl: string; actor: string | null;
  headSha: string | null; ref: string | null; isTag: boolean;
}
export interface CiRepo {
  repo: string; error: string | null; runs: CiRun[];
  /** SHA/Ref des in DIESEM Stack laufenden Images (vom Server abgefragt). Für rookhub null → Browser liefert es selbst. */
  runningSha?: string | null; runningRef?: string | null;
}
export interface CiOverview { configured: boolean; repos: CiRepo[]; fetchedAt: string; }

/**
 * Admin-CI-Übersicht: die letzten 5 GitHub-Actions-Läufe je beteiligtem Repo, alle 5 s aktualisiert.
 * Läuft nur, solange der Tab offen ist (via `*matTabContent` lazy instanziiert). Server cacht die
 * GitHub-Abrufe kurz, daher ist das 5-s-Polling rate-limit-schonend.
 */
@Component({
  selector: 'app-admin-github-actions',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslatePipe],
  template: `
    <div class="ci">
      <div class="ci-head">
        <h3>{{ 'admin.ci.title' | translate }}</h3>
        <span class="ci-sub">
          {{ 'admin.ci.subtitle' | translate }}
          @if (lastUpdated) { · {{ 'admin.ci.updated' | translate }} {{ lastUpdated | date:'HH:mm:ss' }} }
          @if (buildSha) { · {{ 'admin.ci.runningBuild' | translate }} {{ buildSha.slice(0, 7) }} }
        </span>
        @for (b of running; track b.repo) {
          <span class="ci-eta" [matTooltip]="'admin.ci.etaTooltip' | translate">
            <mat-icon>timer</mat-icon>
            <span class="ci-eta-repo">{{ b.repo }}</span>
            @if (b.remaining == null) {
              {{ 'admin.ci.etaUnknown' | translate }}
            } @else if (b.remaining <= 0) {
              {{ 'admin.ci.etaSoon' | translate }}
            } @else {
              {{ 'admin.ci.etaIn' | translate:{ time: fmtDur(b.remaining) } }}
            }
          </span>
        }
      </div>

      @if (loading && !overview) {
        <mat-spinner diameter="28"></mat-spinner>
      } @else if (overview && !overview.configured) {
        <p class="ci-hint">
          <mat-icon>key_off</mat-icon>
          {{ 'admin.ci.notConfigured' | translate }}
        </p>
      } @else if (overview) {
        <div class="repo-grid">
          @for (repo of overview.repos; track repo.repo) {
            <section class="repo-card">
              <div class="repo-title">
                <mat-icon>code</mat-icon>{{ repo.repo }}
                <button class="watch-btn" [class.watching]="isWatching(repo.repo)"
                        (click)="toggleWatch(repo.repo, $event)"
                        [matTooltip]="(isWatching(repo.repo) ? 'admin.ci.watchStop' : 'admin.ci.watchStart') | translate">
                  <mat-icon>{{ isWatching(repo.repo) ? 'visibility' : 'visibility_off' }}</mat-icon>
                </button>
                @if (repo.error) { <span class="repo-error">{{ repo.error }}</span> }
              </div>
              @if (repo.runs.length === 0 && !repo.error) {
                <p class="empty">{{ 'admin.ci.noRuns' | translate }}</p>
              }
              @for (run of repo.runs; track run.id) {
                <a class="run" [class.run-deployed]="isRunningBuild(run, repo)" [href]="run.htmlUrl" target="_blank" rel="noopener noreferrer">
                  <span class="run-badge" [ngClass]="badgeClass(run)"
                        [matTooltip]="run.conclusion || run.status">
                    <mat-icon>{{ badgeIcon(run) }}</mat-icon>
                  </span>
                  <span class="run-main">
                    <span class="run-title">
                      {{ run.title || run.name }}
                      @if (isRunningBuild(run, repo)) {
                        <span class="run-live-tag" [matTooltip]="'admin.ci.runningBuildTooltip' | translate">
                          <mat-icon>fiber_manual_record</mat-icon>{{ 'admin.ci.live' | translate }}
                        </span>
                      }
                    </span>
                    <span class="run-meta">
                      <mat-icon class="ref-icon">{{ run.isTag ? 'sell' : 'call_split' }}</mat-icon>
                      <span class="run-ref" [class.is-tag]="run.isTag">{{ refLabel(run) }}</span>
                      · {{ run.name }} · #{{ run.runNumber }}
                      · {{ 'admin.ci.started' | translate }} {{ run.createdAt | date:'dd.MM. HH:mm' }}
                      · {{ 'admin.ci.duration' | translate }} {{ durationLabel(run) }}
                      @if (run.actor) { · {{ run.actor }} }
                    </span>
                  </span>
                </a>
              }
            </section>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .ci { padding: 4px 2px 16px; }
    .ci-head { display: flex; align-items: baseline; gap: 10px; flex-wrap: wrap; margin-bottom: 12px; }
    .ci-head h3 { margin: 0; font-size: 1.05rem; }
    .ci-sub { color: color-mix(in srgb, currentColor 55%, transparent); font-size: 0.82rem; }
    /* Prominente ETA für laufende Builds (Countdown aus dem Mittel der letzten Läufe). */
    .ci-eta { display: inline-flex; align-items: center; gap: 5px; padding: 2px 10px; border-radius: 14px;
      background: #1565c0; color: #fff; font-size: 0.82rem; font-weight: 600; }
    .ci-eta mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .ci-eta-repo { font-weight: 700; }
    .ci-hint { display: flex; align-items: center; gap: 8px; color: color-mix(in srgb, currentColor 65%, transparent); }
    .repo-grid { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); }
    .repo-card { border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 8px; padding: 10px 12px; }
    .repo-title { display: flex; align-items: center; gap: 6px; font-weight: 600; font-size: 0.92rem; margin-bottom: 6px; }
    .repo-title mat-icon { font-size: 18px; width: 18px; height: 18px; opacity: 0.6; }
    .repo-error { color: #e53935; font-size: 0.78rem; font-weight: 400; }
    /* 👁 „beobachten"-Knopf je Repo: schaltet den 10-s-Schnell-Poll für dieses eine Repo ein. */
    .watch-btn { display: inline-flex; align-items: center; justify-content: center; border: none; background: none;
      cursor: pointer; padding: 2px; border-radius: 6px; color: inherit; opacity: 0.55; line-height: 0; }
    .watch-btn:hover { opacity: 1; background: color-mix(in srgb, currentColor 10%, transparent); }
    .watch-btn mat-icon { font-size: 18px; width: 18px; height: 18px; opacity: 1; }
    .watch-btn.watching { opacity: 1; color: #1565c0; }   /* aktiv = blau (baut/schnell-poll) */
    @keyframes watch-pulse { 0%,100% { opacity: 1; } 50% { opacity: 0.4; } }
    .watch-btn.watching mat-icon { animation: watch-pulse 1.4s ease-in-out infinite; }
    .empty { color: color-mix(in srgb, currentColor 50%, transparent); font-style: italic; font-size: 0.82rem; margin: 2px 0; }
    .run { display: flex; align-items: center; gap: 10px; padding: 5px 4px; border-radius: 6px; text-decoration: none; color: inherit; }
    .run:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    /* Deployter Build zusätzlich mit GELBEM Zeilen-Hintergrund markiert (nicht nur das Icon-Badge). */
    .run.run-deployed { background: color-mix(in srgb, #f9a825 22%, transparent); border-left: 3px solid #f9a825; padding-left: 6px; }
    .run.run-deployed:hover { background: color-mix(in srgb, #f9a825 32%, transparent); }
    /* Farbschema: BLAU = baut (läuft), GRÜN = fertig gebaut (Erfolg), GELB = deployed (läuft im Stack).
       Der deployte Build wird durch das GELBE „live"-Badge markiert (run-live-tag) — klar unterscheidbar
       vom grünen Erfolgs-Badge und vom blauen „baut"-Zustand. */
    .run-live-tag { display: inline-flex; align-items: center; gap: 2px; margin-left: 6px; padding: 0 6px; border-radius: 10px; background: #f9a825; color: #1a1a1a; font-size: 0.68rem; font-weight: 700; vertical-align: middle; }
    .run-live-tag mat-icon { font-size: 13px; width: 13px; height: 13px; }
    .run-badge { display: inline-flex; align-items: center; justify-content: center; width: 26px; height: 26px; border-radius: 50%; flex-shrink: 0; }
    .run-badge mat-icon { font-size: 17px; width: 17px; height: 17px; color: #fff; }
    .run-badge.ok { background: #2e7d32; }
    .run-badge.fail { background: #c62828; }
    .run-badge.run { background: #1565c0; }
    .run-badge.neutral { background: #757575; }
    .run-main { display: flex; flex-direction: column; min-width: 0; }
    .run-title { font-size: 0.86rem; font-weight: 500; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .run-meta { font-size: 0.74rem; color: color-mix(in srgb, currentColor 55%, transparent); white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .run-ref { font-family: monospace; }
    .run-ref.is-tag { color: #6a1b9a; font-weight: 600; }
    .ref-icon { font-size: 12px; width: 12px; height: 12px; vertical-align: -1px; opacity: 0.6; }
  `]
})
export class AdminGithubActionsComponent implements OnInit {
  private http = inject(HttpClient);
  private destroyRef = inject(DestroyRef);

  overview: CiOverview | null = null;
  loading = true;
  lastUpdated: Date | null = null;
  /** Commit-SHA des LAUFENDEN Frontend-Builds (aus /build-info.json, vom Docker-Build gesetzt). */
  buildSha: string | null = null;
  /** Ref des laufenden Builds: "master" bei :dev, Tag-Name (z. B. "v0.234.0") bei :prod. */
  buildRef: string | null = null;

  /** „Jetzt" in ms, im 1-s-Takt aktualisiert → treibt den Live-Countdown der ETA. */
  private nowMs = Date.now();
  /** Aktuell laufende CI-Builds mit geschätzter Restzeit (Mittel der letzten abgeschlossenen Läufe). */
  running: { repo: string; remaining: number | null }[] = [];

  /** Normaler Voll-Abruf-Takt (alle Repos). Kurz, weil rookhub die Läufe jetzt per GitHub-Webhook
   *  gepusht bekommt (Start/Ende live) und die GitHub-API selbst nur selten [Server-Cache] anfragt —
   *  der FE-Poll trifft also überwiegend rookhubs eigenen Cache, nicht die GitHub-API. */
  private static readonly NORMAL_MS = 20_000;   // 20 s
  /** „👁 beobachten"-Schnell-Takt (nur das beobachtete Repo). */
  private static readonly WATCH_MS = 10_000;     // 10 s
  /** Basis-Beobachtungsfenster ab Klick. */
  private static readonly WATCH_WINDOW_MS = 60_000;   // 1 min
  /** Läuft im beobachteten Repo eine Aktion, wird das Fenster rollend so lange verlängert. */
  private static readonly WATCH_RUNNING_EXTEND_MS = 15_000;

  /** Repo → Zeitpunkt (ms), bis zu dem es schnell beobachtet wird. Leere Map = normaler 2-min-Takt. */
  private watchUntil: Record<string, number> = {};

  ngOnInit(): void {
    // Commit-SHA + Ref des laufenden Builds einmalig laden (fehlt bei alten Images/dev → kein Marker).
    this.http.get<{ sha: string; ref?: string }>('/build-info.json').pipe(catchError(() => of(null))).subscribe(info => {
      const sha = info?.sha;
      this.buildSha = sha && sha !== 'unknown' ? sha : null;
      const ref = info?.ref;
      this.buildRef = ref ? ref : null;
    });

    // Normaler Voll-Abruf: sofort + danach alle 2 min (schont das GitHub-Rate-Limit).
    timer(0, AdminGithubActionsComponent.NORMAL_MS).pipe(
      switchMap(() => this.http.get<CiOverview>('/api/admin/ci/runs').pipe(catchError(() => of(null)))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(data => {
      this.loading = false;
      if (data) { this.overview = data; this.lastUpdated = new Date(); }
      this.recomputeEta();
    });

    // „👁 beobachten"-Schnell-Poll: alle 10 s NUR die aktiv beobachteten Repos einzeln frisch abrufen.
    timer(AdminGithubActionsComponent.WATCH_MS, AdminGithubActionsComponent.WATCH_MS)
      .pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.watchTick());

    // 1-s-Takt für den Live-Countdown der ETA (Daten selbst kommen alle 5 s).
    timer(1000, 1000).pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
      this.nowMs = Date.now();
      this.recomputeEta();
    });
  }

  /** Mittlere Laufzeit der ABGESCHLOSSENEN Läufe eines Repos (aus den letzten 5), in Sekunden;
   *  null wenn es keinen abgeschlossenen Referenzlauf gibt. */
  private avgDurationSec(repo: CiRepo): number | null {
    const durs = repo.runs
      .filter(r => r.status === 'completed')
      .map(r => (Date.parse(r.updatedAt) - Date.parse(r.createdAt)) / 1000)
      .filter(d => !isNaN(d) && d > 0);
    if (!durs.length) return null;
    return Math.round(durs.reduce((a, b) => a + b, 0) / durs.length);
  }

  /** Aktualisiert (a) die ETA laufender Builds und (b) den je Stack deployten Build. */
  private recomputeEta(): void {
    const eta: { repo: string; remaining: number | null }[] = [];
    for (const repo of this.overview?.repos ?? []) {
      // laufender Build → Restzeit-Schätzung
      const run = repo.runs.find(r => r.status !== 'completed');
      if (run) {
        const started = Date.parse(run.createdAt);
        if (!isNaN(started)) {
          const elapsed = Math.max(0, Math.round((this.nowMs - started) / 1000));
          const avg = this.avgDurationSec(repo);
          eta.push({ repo: repo.repo, remaining: avg == null ? null : avg - elapsed });
        }
      }
    }
    this.running = eta;
  }

  /** Läuft in diesem Repo gerade eine Aktion (nicht abgeschlossener Run)? */
  private repoRunning(repo: string): boolean {
    return this.overview?.repos.find(r => r.repo === repo)?.runs.some(run => run.status !== 'completed') ?? false;
  }

  /** Wird dieses Repo aktuell schnell beobachtet? (steuert das 👁-Icon) */
  isWatching(repo: string): boolean {
    const until = this.watchUntil[repo];
    return until != null && (until > Date.now() || this.repoRunning(repo));
  }

  /** 👁 klicken: Beobachtung für dieses Repo starten (10 s, 1 min lang; bei laufender Aktion bis zu deren
   *  Ende) bzw. wieder ausschalten. Beim Einschalten sofort einmal frisch abrufen. */
  toggleWatch(repo: string, ev: Event): void {
    ev.preventDefault(); ev.stopPropagation();
    if (this.isWatching(repo)) { delete this.watchUntil[repo]; return; }
    this.watchUntil[repo] = Date.now() + AdminGithubActionsComponent.WATCH_WINDOW_MS;
    this.loadRepo(repo);
  }

  /** 10-s-Takt: jedes aktiv beobachtete Repo einzeln frisch abrufen; Fenster bei laufender Aktion
   *  rollend verlängern (= „bis zum Ende der Aktion"), sonst nach Ablauf beenden. */
  private watchTick(): void {
    const now = Date.now();
    for (const repo of Object.keys(this.watchUntil)) {
      const inWindow = this.watchUntil[repo] > now;
      const running = this.repoRunning(repo);
      if (inWindow || running) {
        this.loadRepo(repo);
        if (running) this.watchUntil[repo] = now + AdminGithubActionsComponent.WATCH_RUNNING_EXTEND_MS;
      } else {
        delete this.watchUntil[repo];   // Fenster abgelaufen + keine Aktion → Beobachtung stoppen
      }
    }
  }

  /** Ein einzelnes Repo frisch abrufen (ungecacht) und in die Übersicht einmergen. */
  private loadRepo(repo: string): void {
    this.http.get<CiRepo>(`/api/admin/ci/runs/${encodeURIComponent(repo)}`)
      .pipe(catchError(() => of(null)))
      .subscribe(dto => {
        if (!dto || !this.overview) return;
        const i = this.overview.repos.findIndex(r => r.repo === dto.repo);
        if (i >= 0) this.overview.repos[i] = dto;
        this.lastUpdated = new Date();
        this.recomputeEta();
      });
  }

  /** Kompakte Dauer „2m 5s" / „45s" für die ETA-Anzeige. */
  fmtDur(sec: number): string {
    const s = Math.max(0, Math.round(sec));
    if (s < 60) return `${s}s`;
    const m = Math.floor(s / 60);
    return m < 60 ? `${m}m ${s % 60}s` : `${Math.floor(m / 60)}h ${m % 60}m`;
  }

  /** Anzeige-Ref: Branch/Tag-Name, sonst Kurz-SHA. */
  refLabel(run: CiRun): string {
    return run.ref || run.branch || (run.headSha ? run.headSha.slice(0, 7) : '?');
  }

  /** Laufzeit: bei abgeschlossenen Runs created→updated, bei laufenden created→jetzt (live). */
  durationLabel(run: CiRun): string {
    const start = Date.parse(run.createdAt);
    if (isNaN(start)) return '–';
    const end = run.status === 'completed' ? Date.parse(run.updatedAt) : Date.now();
    const s = Math.max(0, Math.round((end - start) / 1000));
    if (s < 60) return `${s}s`;
    const m = Math.floor(s / 60);
    if (m < 60) return `${m}m ${s % 60}s`;
    return `${Math.floor(m / 60)}h ${m % 60}m`;
  }

  /**
   * Hat dieser Run das aktuell in `repo` laufende Image erzeugt?
   * Quelle der laufenden SHA/Ref: für rookhub der Browser (/build-info.json → buildSha/buildRef),
   * für die anderen Stacks der Server (repo.runningSha/runningRef aus deren build-info-Endpoint).
   * SHA muss passen (Prefix-tolerant) UND — sofern ein Ref gemeldet wird — auch der Ref: ein master-Push
   * und sein gleichnamiger Tag teilen dieselbe SHA, aber nur EINER baute das laufende Image
   * (:dev = master-Run, :prod = Tag-Run). Ältere Images ohne Ref matchen wie bisher nur per SHA.
   */
  isRunningBuild(run: CiRun, repo: CiRepo): boolean {
    // Bevorzugt die vom Server ermittelte laufende SHA/Ref des Stacks; für rookhub fällt es auf die
    // im Browser gelesene /build-info.json zurück, falls der Server sie (noch) nicht kennt.
    const isRookhub = repo.repo === 'rookhub';
    const sha = repo.runningSha ?? (isRookhub ? this.buildSha : null);
    const ref = repo.runningSha ? (repo.runningRef ?? null) : (isRookhub ? this.buildRef : null);
    if (!sha || !run.headSha) return false;
    const shaMatch = sha === run.headSha || sha.startsWith(run.headSha) || run.headSha.startsWith(sha);
    if (!shaMatch) return false;
    if (!ref) return true;
    return run.ref === ref;
  }

  badgeClass(run: CiRun): string {
    if (run.status !== 'completed') return 'run';
    switch (run.conclusion) {
      case 'success': return 'ok';
      case 'failure': case 'timed_out': case 'startup_failure': return 'fail';
      default: return 'neutral';   // cancelled/skipped/neutral/action_required
    }
  }

  badgeIcon(run: CiRun): string {
    if (run.status !== 'completed') return 'sync';
    switch (run.conclusion) {
      case 'success': return 'check';
      case 'failure': case 'timed_out': case 'startup_failure': return 'close';
      case 'cancelled': return 'block';
      default: return 'remove';
    }
  }
}
