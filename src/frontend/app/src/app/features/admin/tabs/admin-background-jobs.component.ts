import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleChange, MatSlideToggleModule } from '@angular/material/slide-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../../core/snackbar.service';
import { LoadErrorComponent } from '../../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../../shared/loading-spinner/loading-spinner.component';
import { HelpHintComponent } from '../../../shared/help-hint/help-hint.component';

/** Ein Hintergrunddienst (`services[]` von `GET /api/admin/background-jobs/pause`). */
export interface BackgroundJobService {
  name: string;
  pausable: boolean;
  /** Ende des letzten erfolgreichen Takts (UTC), `null` = unbekannt/noch keiner. */
  lastTick: string | null;
}

/** Antwort von `GET/PUT /api/admin/background-jobs/pause`. */
export interface BackgroundJobsPause {
  /** Wirksame Pause (Schalter ODER Serverkonfiguration). */
  paused: boolean;
  since: string | null;
  by: string | null;
  /** Serverkonfiguration erzwingt die Pause — der Schalter hebt sie nicht auf. */
  forcedByConfig: boolean;
  /** Stand des Schalters selbst. */
  pausedInDb: boolean;
  services: BackgroundJobService[];
}

export const BACKGROUND_JOBS_URL = '/api/admin/background-jobs/pause';

/**
 * Admin-Tab „Hintergrundjobs": EIN Schalter hält alle pausierbaren Hintergrunddienste an (zur Laufzeit, ohne Neustart,
 * bleibt über Neustarts bestehen). Darunter, wer seit wann pausiert hat, ein Hinweis, wenn die Serverkonfiguration die
 * Pause erzwingt, und die Liste der Dienste mit „pausierbar ja/nein" und letztem Takt.
 */
@Component({
  selector: 'app-admin-background-jobs',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DatePipe, MatCardModule, MatIconModule, MatSlideToggleModule, TranslatePipe, LoadErrorComponent,
    LoadingSpinnerComponent, HelpHintComponent],
  template: `
    <div class="jobs">
      @if (error()) {
        <app-load-error (retry)="load()" />
      } @else if (state(); as s) {
        <mat-card appearance="outlined" class="jobs-card">
          <mat-card-header>
            <mat-card-title>{{ 'admin.jobs.title' | translate }}<span class="rh-nowrap">&nbsp;<app-help-hint [text]="'admin.jobs.help' | translate" /></span></mat-card-title>
          </mat-card-header>
          <mat-card-content>
            <div class="switch-row">
              <mat-slide-toggle [checked]="s.pausedInDb" [disabled]="saving()" (change)="toggle($event)"
                                data-testid="pause-toggle">
                {{ 'admin.jobs.toggle' | translate }}
              </mat-slide-toggle>
              <span class="status" [class.paused]="s.paused" data-testid="pause-status">
                <mat-icon aria-hidden="true">{{ s.paused ? 'pause_circle' : 'play_circle' }}</mat-icon>
                {{ (s.paused ? 'admin.jobs.statusPaused' : 'admin.jobs.statusRunning') | translate }}
              </span>
            </div>
            @if (s.pausedInDb && s.since) {
              <p class="meta" data-testid="pause-since">
                {{ 'admin.jobs.since' | translate:{ when: (s.since | date:'mediumDate') + ' · ' + (s.since | date:'shortTime'), by: s.by || '?' } }}
              </p>
            }
            @if (s.forcedByConfig) {
              <div class="banner" role="status" data-testid="pause-forced">
                <mat-icon aria-hidden="true">lock</mat-icon>
                <span>{{ 'admin.jobs.forced' | translate }}</span>
              </div>
            }

            <h3 class="list-title">{{ 'admin.jobs.services' | translate }}</h3>
            @if (!s.services.length) {
              <p class="empty-state">{{ 'admin.jobs.noServices' | translate }}</p>
            } @else {
              <ul class="services">
                @for (svc of sortedServices(); track svc.name) {
                  <li data-testid="job-service">
                    <code class="name" [title]="svc.name">{{ svc.name }}</code>
                    <span class="chip" [class.always]="!svc.pausable">
                      {{ (svc.pausable ? 'admin.jobs.pausable' : 'admin.jobs.notPausable') | translate }}
                    </span>
                    <span class="tick">
                      @if (svc.lastTick) { {{ 'admin.jobs.lastTick' | translate:{ when: (svc.lastTick | date:'short') } }} }
                      @else { {{ 'admin.jobs.noTick' | translate }} }
                    </span>
                  </li>
                }
              </ul>
            }
          </mat-card-content>
        </mat-card>
      } @else {
        <app-loading-spinner />
      }
    </div>
  `,
  styles: [`
    .jobs { padding: 16px 0; }
    .jobs-card { max-width: 900px; }
    .switch-row { display: flex; flex-wrap: wrap; align-items: center; gap: 12px 24px; margin: 8px 0; }
    .status { display: inline-flex; align-items: center; gap: 6px; font-weight: 500; color: var(--rh-success); }
    .status.paused { color: var(--rh-warn); }
    .meta { margin: 4px 0 8px; color: var(--mat-sys-on-surface-variant); }
    .banner { display: flex; align-items: center; gap: 8px; padding: 8px 12px; margin: 8px 0; border-radius: 8px;
      background: color-mix(in srgb, var(--rh-warn) 14%, transparent); }
    .banner span { flex: 1 1 220px; }
    .list-title { margin: 16px 0 4px; font-size: 1rem; font-weight: 500; }
    .services { list-style: none; margin: 0; padding: 0; }
    .services li { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 12px; padding: 6px 0;
      border-bottom: 1px solid var(--mat-sys-outline-variant); }
    .name { flex: 1 1 220px; min-width: 0; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .chip { font-size: .72rem; padding: 1px 7px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 10%, transparent); white-space: nowrap; }
    .chip.always { color: var(--rh-info); }
    .tick { color: var(--mat-sys-on-surface-variant); font-size: .9em; white-space: nowrap; }
    .empty-state { text-align: center; padding: 2rem 1rem; }
  `],
})
export class AdminBackgroundJobsComponent implements OnInit {
  private readonly http = inject(HttpClient);
  private readonly translate = inject(TranslateService);
  private readonly snack = inject(SnackbarService);

  readonly state = signal<BackgroundJobsPause | null>(null);
  readonly error = signal(false);
  readonly saving = signal(false);

  /** Pausierbare zuerst, dann nach Namen (der Server sortiert schon so; hier nur als Absicherung). */
  readonly sortedServices = computed(() => [...(this.state()?.services ?? [])]
    .sort((a, b) => Number(b.pausable) - Number(a.pausable) || a.name.localeCompare(b.name)));

  ngOnInit(): void {
    void this.load();
  }

  async load(): Promise<void> {
    this.error.set(false);
    try {
      this.state.set(await firstValueFrom(this.http.get<BackgroundJobsPause>(BACKGROUND_JOBS_URL)));
    } catch {
      this.error.set(true);
    }
  }

  async toggle(ev: MatSlideToggleChange): Promise<void> {
    const paused = ev.checked;
    this.saving.set(true);
    try {
      this.state.set(await firstValueFrom(this.http.put<BackgroundJobsPause>(BACKGROUND_JOBS_URL, { paused })));
      this.snack.success(this.translate.instant(paused ? 'admin.jobs.pausedNow' : 'admin.jobs.resumedNow'));
    } catch {
      ev.source.checked = !paused;   // Schalter zurück — gespeichert ist der alte Stand
      this.snack.warn(this.translate.instant('admin.jobs.saveError'));
    } finally {
      this.saving.set(false);
    }
  }
}
