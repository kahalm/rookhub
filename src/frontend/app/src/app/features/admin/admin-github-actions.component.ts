import { Component, DestroyRef, OnInit, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { timer, switchMap, catchError, of } from 'rxjs';

export interface CiRun {
  id: number; name: string; title: string; branch: string; event: string;
  status: string; conclusion: string | null; runNumber: number;
  createdAt: string; updatedAt: string; htmlUrl: string; actor: string | null;
  headSha: string | null; ref: string | null; isTag: boolean;
}
export interface CiRepo { repo: string; error: string | null; runs: CiRun[]; }
export interface CiOverview { configured: boolean; repos: CiRepo[]; fetchedAt: string; }

/**
 * Admin-CI-Übersicht: die letzten 5 GitHub-Actions-Läufe je beteiligtem Repo, alle 5 s aktualisiert.
 * Läuft nur, solange der Tab offen ist (via `*matTabContent` lazy instanziiert). Server cacht die
 * GitHub-Abrufe kurz, daher ist das 5-s-Polling rate-limit-schonend.
 */
@Component({
  selector: 'app-admin-github-actions',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule, TranslateModule],
  template: `
    <div class="ci">
      <div class="ci-head">
        <h3>{{ 'admin.ci.title' | translate }}</h3>
        <span class="ci-sub">
          {{ 'admin.ci.subtitle' | translate }}
          @if (lastUpdated) { · {{ 'admin.ci.updated' | translate }} {{ lastUpdated | date:'HH:mm:ss' }} }
          @if (buildSha) { · {{ 'admin.ci.runningBuild' | translate }} {{ buildSha.slice(0, 7) }} }
        </span>
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
                <mat-icon>folder_open</mat-icon>{{ repo.repo }}
                @if (repo.error) { <span class="repo-error">{{ repo.error }}</span> }
              </div>
              @if (repo.runs.length === 0 && !repo.error) {
                <p class="empty">{{ 'admin.ci.noRuns' | translate }}</p>
              }
              @for (run of repo.runs; track run.id) {
                <a class="run" [class.run-live]="isRunningBuild(run)" [href]="run.htmlUrl" target="_blank" rel="noopener noreferrer">
                  <span class="run-badge" [ngClass]="badgeClass(run)"
                        [matTooltip]="run.conclusion || run.status">
                    <mat-icon>{{ badgeIcon(run) }}</mat-icon>
                  </span>
                  <span class="run-main">
                    <span class="run-title">
                      {{ run.title || run.name }}
                      @if (isRunningBuild(run)) {
                        <span class="run-live-tag" [matTooltip]="'admin.ci.runningBuildTooltip' | translate">
                          <mat-icon>play_circle</mat-icon>{{ 'admin.ci.live' | translate }}
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
    .ci-hint { display: flex; align-items: center; gap: 8px; color: color-mix(in srgb, currentColor 65%, transparent); }
    .repo-grid { display: grid; gap: 12px; grid-template-columns: repeat(auto-fill, minmax(340px, 1fr)); }
    .repo-card { border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 8px; padding: 10px 12px; }
    .repo-title { display: flex; align-items: center; gap: 6px; font-weight: 600; font-size: 0.92rem; margin-bottom: 6px; }
    .repo-title mat-icon { font-size: 18px; width: 18px; height: 18px; opacity: 0.6; }
    .repo-error { color: #e53935; font-size: 0.78rem; font-weight: 400; }
    .empty { color: color-mix(in srgb, currentColor 50%, transparent); font-style: italic; font-size: 0.82rem; margin: 2px 0; }
    .run { display: flex; align-items: center; gap: 10px; padding: 5px 4px; border-radius: 6px; text-decoration: none; color: inherit; }
    .run:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .run-live { background: color-mix(in srgb, #2e7d32 14%, transparent); outline: 1px solid color-mix(in srgb, #2e7d32 45%, transparent); }
    .run-live-tag { display: inline-flex; align-items: center; gap: 2px; margin-left: 6px; padding: 0 6px; border-radius: 10px; background: #2e7d32; color: #fff; font-size: 0.68rem; font-weight: 600; vertical-align: middle; }
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

  ngOnInit(): void {
    // Commit-SHA des laufenden Builds einmalig laden (fehlt bei alten Images/dev → kein Marker).
    this.http.get<{ sha: string }>('/build-info.json').pipe(catchError(() => of(null))).subscribe(info => {
      const sha = info?.sha;
      this.buildSha = sha && sha !== 'unknown' ? sha : null;
    });

    // Sofort + danach alle 5 s neu laden, solange der Tab (und damit diese Komponente) lebt.
    timer(0, 5000).pipe(
      switchMap(() => this.http.get<CiOverview>('/api/admin/ci/runs').pipe(catchError(() => of(null)))),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe(data => {
      this.loading = false;
      if (data) { this.overview = data; this.lastUpdated = new Date(); }
    });
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

  /** Hat dieser Run den aktuell laufenden Frontend-Build erzeugt? (head_sha == build-SHA, Prefix-tolerant) */
  isRunningBuild(run: CiRun): boolean {
    if (!this.buildSha || !run.headSha) return false;
    const a = this.buildSha, b = run.headSha;
    return a === b || a.startsWith(b) || b.startsWith(a);
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
