import { ChangeDetectionStrategy, Component, OnInit, inject, input, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { SnackbarService } from '../../core/snackbar.service';
import { apiErrorText } from '../../core/api-error';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import {
  TRAINING_SHARE_DAYS, TRAINING_SHARE_DEFAULT_DAYS, TrainingShare, TrainingVideosService,
} from './training-videos.service';

/**
 * „Teilen“ auf der Stundenseite (nur der Besitzer des Eintrags): Frist wählen (1 / 7 / 30 Tage, Vorgabe 7), Link anlegen,
 * darunter die aktiven Links mit Ablaufdatum, „Kopieren“ und „Widerrufen“ (mit Rückfrage). Mehrere Links je Eintrag sind
 * erlaubt. Der Gast sieht unter `/tl/{token}` genau diesen Eintrag, ohne Notizen und Übungsstand.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-training-share-panel',
  standalone: true,
  imports: [DatePipe, MatButtonModule, MatIconModule, MatButtonToggleModule, TranslatePipe, IconLabelDirective],
  template: `
    <section class="share" [attr.aria-label]="'trainingVideos.share.title' | translate">
      <h2>{{ 'trainingVideos.share.title' | translate }}</h2>
      <p class="hint">{{ 'trainingVideos.share.hint' | translate }}</p>
      <div class="create">
        <mat-button-toggle-group class="days" [value]="days()" (change)="days.set($event.value)"
                                 [attr.aria-label]="'trainingVideos.share.validity' | translate">
          @for (d of dayOptions; track d) {
            <mat-button-toggle [value]="d">{{ (d === 1 ? 'trainingVideos.share.daysOne' : 'trainingVideos.share.days') | translate: { count: d } }}</mat-button-toggle>
          }
        </mat-button-toggle-group>
        <button mat-flat-button color="primary" type="button" class="tv-share-create" (click)="create()" [disabled]="busy()">
          <mat-icon>add_link</mat-icon>{{ 'trainingVideos.share.create' | translate }}
        </button>
      </div>
      @if (loadFailed()) {
        <p class="error" role="alert">{{ 'trainingVideos.share.loadFailed' | translate }}</p>
      } @else if (shares().length) {
        <ul class="links">
          @for (s of shares(); track s.id) {
            <li class="link">
              <span class="expires">{{ 'trainingVideos.share.expires' | translate: { date: (s.expiresAt | date: 'medium') } }}</span>
              <code class="url">{{ url(s) }}</code>
              <span class="actions">
                <button mat-stroked-button type="button" class="tv-share-copy" (click)="copy(s)">
                  <mat-icon>content_copy</mat-icon>{{ 'common.copy' | translate }}
                </button>
                <button mat-button type="button" class="tv-share-revoke" (click)="revoke(s)">
                  <mat-icon>link_off</mat-icon>{{ 'trainingVideos.share.revoke' | translate }}
                </button>
              </span>
            </li>
          }
        </ul>
      } @else if (loaded()) {
        <p class="none">{{ 'trainingVideos.share.none' | translate }}</p>
      }
    </section>
  `,
  styles: [`
    .share { margin-top: 24px; border-top: 1px solid var(--mat-sys-outline-variant); padding-top: 12px; }
    .share h2 { font-size: 1.1rem; margin: 0 0 4px; }
    .hint, .none { margin: 0 0 8px; color: var(--mat-sys-on-surface-variant); font-size: .9rem; }
    .error { color: var(--rh-error); margin: 0; }
    .create { display: flex; flex-wrap: wrap; gap: 8px 12px; align-items: center; margin-bottom: 8px; }
    .create button { min-height: 44px; }
    .links { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
    .link { display: flex; flex-direction: column; gap: 4px; border: 1px solid var(--mat-sys-outline-variant); border-radius: 8px; padding: 8px 10px; }
    .expires { font-size: .85rem; color: var(--mat-sys-on-surface-variant); font-variant-numeric: tabular-nums; }
    .url { overflow-wrap: anywhere; font-size: .8rem; }
    .actions { display: flex; flex-wrap: wrap; gap: 4px; }
    .actions button { min-height: 44px; }
  `],
})
export class TrainingSharePanelComponent implements OnInit {
  private readonly svc = inject(TrainingVideosService);
  private readonly snackbar = inject(SnackbarService);
  private readonly translate = inject(TranslateService);
  private readonly confirm = inject(ConfirmService);

  readonly lessonId = input.required<number>();

  readonly dayOptions = TRAINING_SHARE_DAYS;
  readonly days = signal<number>(TRAINING_SHARE_DEFAULT_DAYS);
  readonly shares = signal<TrainingShare[]>([]);
  readonly loaded = signal(false);
  readonly loadFailed = signal(false);
  readonly busy = signal(false);

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loadFailed.set(false);
    this.svc.shares(this.lessonId()).subscribe({
      next: list => { this.shares.set(list ?? []); this.loaded.set(true); },
      error: () => { this.loadFailed.set(true); this.loaded.set(true); },
    });
  }

  url(s: TrainingShare): string { return this.svc.shareUrl(s.token); }

  /** Neuen Link anlegen und gleich in die Zwischenablage legen. */
  create(): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.svc.createShare(this.lessonId(), this.days()).subscribe({
      next: s => {
        this.busy.set(false);
        this.shares.update(list => [s, ...list]);
        this.copy(s);
      },
      error: err => { this.busy.set(false); this.snackbar.warn(apiErrorText(err, this.translate, 'trainingVideos.share.createFailed')); },
    });
  }

  copy(s: TrainingShare): void {
    const url = this.url(s);
    void (navigator.clipboard?.writeText(url) ?? Promise.reject(new Error('no clipboard'))).then(
      () => this.snackbar.copy(this.translate.instant('trainingVideos.share.copied')),
      () => this.snackbar.warn(url),
    );
  }

  revoke(s: TrainingShare): void {
    this.confirm.ask('trainingVideos.share.revokeConfirm').subscribe(ok => {
      if (!ok) return;
      this.svc.revokeShare(this.lessonId(), s.id).subscribe({
        next: () => this.shares.update(list => list.filter(x => x.id !== s.id)),
        error: err => this.snackbar.warn(apiErrorText(err, this.translate, 'trainingVideos.share.revokeFailed')),
      });
    });
  }
}
