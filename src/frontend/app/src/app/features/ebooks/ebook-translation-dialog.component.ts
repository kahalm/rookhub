import { ChangeDetectionStrategy, ChangeDetectorRef, Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatDialogModule } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { apiErrorText } from '../../core/api-error';
import { SnackbarService } from '../../core/snackbar.service';
import { LoadErrorComponent } from '../../shared/load-error/load-error.component';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { EbookService, EbookTranslationStatus } from './ebook.service';

/**
 * Buch-Übersetzung (0.876.0), Übersicht für Verwalter aus dem ⋮ der Bibliothek: läuft der Dienst (Spark, Sperrzeit), welches
 * Buch gerade dran ist (Fortschritt, Restdauer), Stand je Zielsprache, die nächsten in der Reihe und die gescheiterten —
 * „Fehlgeschlagene wiederholen“ reiht sie neu ein.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-ebook-translation-dialog',
  standalone: true,
  imports: [CommonModule, MatDialogModule, MatButtonModule, MatIconModule, MatProgressBarModule, TranslatePipe, LoadErrorComponent, LoadingSpinnerComponent],
  template: `
    <h2 mat-dialog-title>{{ 'ebooks.translation.title' | translate }}</h2>
    <mat-dialog-content class="rh-scroll-hint">
      @if (loading()) {
        <app-loading-spinner />
      } @else if (error() || !status()) {
        <app-load-error (retry)="load()" />
      } @else if (status(); as s) {
        <p class="et-state" role="status">
          <mat-icon>{{ !s.enabled ? 'pause_circle' : s.quietNow ? 'bedtime' : !s.modelAvailable ? 'cloud_off' : 'translate' }}</mat-icon>
          <span>
            @if (!s.enabled) { {{ 'ebooks.translation.off' | translate }} }
            @else if (!s.modelAvailable) { {{ 'ebooks.translation.noModel' | translate }} }
            @else if (s.quietNow) { {{ 'ebooks.translation.quiet' | translate: { until: (s.quietUntil | date: 'shortTime') } }} }
            @else if (s.running) { {{ 'ebooks.translation.active' | translate }} }
            @else { {{ 'ebooks.translation.idle' | translate }} }
          </span>
        </p>

        @if (s.running; as r) {
          <section class="et-run">
            <div class="et-run-title">{{ r.title }}</div>
            <mat-progress-bar mode="determinate" [value]="r.segmentsTotal ? (100 * r.segmentsDone / r.segmentsTotal) : 0"
                              [attr.aria-label]="'ebooks.translation.progress' | translate: { done: r.segmentsDone, total: r.segmentsTotal }" />
            <div class="et-meta">
              <span>{{ 'ebooks.translation.progress' | translate: { done: r.segmentsDone, total: r.segmentsTotal } }}</span>
              @if (r.etaMinutes != null) { <span>{{ 'ebooks.translation.eta' | translate: { n: r.etaMinutes } }}</span> }
            </div>
          </section>
        }

        @for (t of s.targets; track t.language) {
          <section class="et-target">
            <h3>{{ ('ebooks.translation.lang.' + t.language) | translate }}</h3>
            <div class="et-counts">
              <span class="chip">{{ 'ebooks.translation.done' | translate: { n: t.done } }}</span>
              <span class="chip">{{ 'ebooks.translation.remaining' | translate: { n: t.remaining } }}</span>
              @if (t.remainingFirstRound > 0) { <span class="chip">{{ 'ebooks.translation.firstRound' | translate: { n: t.remainingFirstRound } }}</span> }
              @if (t.failed > 0) { <span class="chip chip--warn">{{ 'ebooks.translation.failed' | translate: { n: t.failed } }}</span> }
              @if (t.noText > 0) { <span class="chip">{{ 'ebooks.translation.noText' | translate: { n: t.noText } }}</span> }
            </div>
          </section>
        }

        @if (s.bookLanguages.length > 0) {
          <section>
            <h3>{{ 'ebooks.translation.bookLanguages' | translate }}</h3>
            <div class="et-counts">
              @for (b of s.bookLanguages; track b.language) {
                <span class="chip">{{ languageLabel(b.language) }}: {{ b.books }}</span>
              }
            </div>
          </section>
        }

        @if (s.next.length > 0) {
          <section>
            <h3>{{ 'ebooks.translation.next' | translate }}</h3>
            <ol class="et-list">
              @for (n of s.next; track n.ebookId + n.language) {
                <li>
                  <span class="et-book">{{ n.title }}</span>
                  <span class="et-sub">{{ languageLabel(n.sourceLanguage) }}@if (n.priority) { · {{ 'ebooks.translation.priority' | translate }} }@if (n.segmentsTotal > 0) { · {{ 'ebooks.translation.progress' | translate: { done: n.segmentsDone, total: n.segmentsTotal } }} }</span>
                </li>
              }
            </ol>
          </section>
        }

        @if (s.failed.length > 0) {
          <section>
            <h3>{{ 'ebooks.translation.failedTitle' | translate }}</h3>
            <ul class="et-list">
              @for (f of s.failed; track f.ebookId + f.language) {
                <li><span class="et-book">{{ f.title }}</span>@if (f.error) {<span class="et-sub">{{ f.error }}</span>}</li>
              }
            </ul>
          </section>
        }
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      @if (status(); as s) {
        @if (s.targets.length > 0) {
          <button mat-button type="button" (click)="retryFailed()" [disabled]="busy()">
            <mat-icon>replay</mat-icon>{{ 'ebooks.translation.retryFailed' | translate }}
          </button>
        }
      }
      <button mat-flat-button color="primary" mat-dialog-close type="button">{{ 'common.close' | translate }}</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .et-state { display: flex; align-items: center; gap: 8px; margin: 0 0 12px; }
    .et-run { margin: 0 0 16px; }
    .et-run-title { font-weight: 500; margin-bottom: 6px; overflow-wrap: anywhere; }
    .et-meta { display: flex; flex-wrap: wrap; gap: 4px 12px; margin-top: 4px; font-size: .875rem; color: var(--mat-sys-on-surface-variant); }
    h3 { font-size: 1rem; font-weight: 500; margin: 16px 0 6px; }
    .et-counts { display: flex; flex-wrap: wrap; gap: 6px; }
    .chip { font-size: .75rem; padding: 1px 7px; border-radius: 999px; background: color-mix(in srgb, currentColor 10%, transparent); }
    .chip--warn { background: color-mix(in srgb, var(--mat-sys-error) 22%, transparent); }
    .et-list { margin: 0; padding-left: 20px; }
    .et-list li { margin: 4px 0; }
    .et-book { display: block; overflow-wrap: anywhere; }
    .et-sub { display: block; font-size: .8125rem; color: var(--mat-sys-on-surface-variant); overflow-wrap: anywhere; }
  `],
})
export class EbookTranslationDialogComponent implements OnInit {
  private ebooks = inject(EbookService);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  readonly status = signal<EbookTranslationStatus | null>(null);
  readonly loading = signal(true);
  readonly error = signal(false);
  readonly busy = signal(false);

  ngOnInit(): void { this.load(); }

  load(): void {
    this.loading.set(true);
    this.error.set(false);
    this.ebooks.translationStatus().subscribe({
      next: s => { this.status.set(s); this.loading.set(false); this.cdr.markForCheck(); },
      error: () => { this.error.set(true); this.loading.set(false); this.cdr.markForCheck(); },
    });
  }

  retryFailed(): void {
    this.busy.set(true);
    this.ebooks.retryFailedTranslations().subscribe({
      next: r => {
        this.busy.set(false);
        this.snackbar.quick(this.translate.instant('ebooks.translation.retried', { n: r.queued }));
        this.load();
      },
      error: err => { this.busy.set(false); this.snackbar.warn(apiErrorText(err, this.translate, 'ebooks.translation.actionFailed')); },
    });
  }

  /** Sprachname in der Oberflächensprache (Intl), Sonderwerte übersetzt. */
  languageLabel(code: string | null | undefined): string {
    if (!code || code === 'und') return this.translate.instant('ebooks.translation.unclear');
    if (code === 'notext') return this.translate.instant('ebooks.translation.noTextLabel');
    if (code === 'pending') return this.translate.instant('ebooks.translation.undetected');
    try {
      const names = new Intl.DisplayNames([this.translate.currentLang() || 'en'], { type: 'language' });
      return names.of(code) ?? code;
    } catch {
      return code;
    }
  }
}
