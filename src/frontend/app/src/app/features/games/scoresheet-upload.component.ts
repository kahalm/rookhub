import { ChangeDetectionStrategy, Component, DestroyRef, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatCardModule } from '@angular/material/card';
import { MatIconModule } from '@angular/material/icon';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { switchMap, takeWhile } from 'rxjs/operators';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { ScoresheetScan, ScoresheetService, ScoresheetStatus } from './scoresheet.service';

/** So oft fragt die Seite nach, solange Claude liest. */
export const SCAN_POLL_MS = 3000;

/** Gemerkte Notationssprache (je Gerät — wer deutsche Formulare schreibt, tut das meistens). */
export const SCORESHEET_LANG_KEY = 'rookhub_scoresheet_lang';

/** Gemerkte eigene Seite beim Einlesen. */
export const SCORESHEET_SIDE_KEY = 'rookhub_scoresheet_side';

/**
 * „Partieformular einlesen" (0.529.0): Foto aufnehmen oder auswählen, Notationssprache wählen, einlesen.
 * Claude liest im Hintergrund; die Seite fragt nach, bis die Partie da ist, und führt dann zur Korrektur
 * bzw. zur Partie. Die letzten Einlesungen stehen darunter — wer die Seite verlässt, findet sie hier wieder.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-scoresheet-upload',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink, MatButtonModule, MatCardModule, MatIconModule, MatFormFieldModule,
    MatSelectModule, MatProgressSpinnerModule, MatButtonToggleModule, TranslatePipe, HelpHintComponent,
  ],
  template: `
    <div class="sheet-page">
      <div class="head">
        <h1>{{ 'scoresheet.title' | translate }}</h1>
        <app-help-hint [text]="'scoresheet.help' | translate" />
      </div>
      <p class="hint">{{ 'scoresheet.hint' | translate }}</p>

      @if (status(); as st) {
        @if (!st.available) {
          <mat-card class="notice">
            <mat-icon>cloud_off</mat-icon>
            <p>{{ 'scoresheet.notConfigured' | translate }}</p>
          </mat-card>
        } @else {
          <mat-card class="upload">
            <div class="pick">
              <!-- Zwei Wege: „capture" öffnet am Handy direkt die Kamera, ohne die Galerie anzubieten —
                   deshalb daneben die gewöhnliche Dateiauswahl. -->
              <input #camera type="file" accept="image/*" capture="environment" hidden (change)="onFile($event)" />
              <input #picker type="file" accept="image/jpeg,image/png,image/webp" hidden (change)="onFile($event)" />
              <button mat-stroked-button type="button" (click)="camera.click()" [disabled]="uploading()">
                <mat-icon>photo_camera</mat-icon> {{ 'scoresheet.takePhoto' | translate }}
              </button>
              <button mat-stroked-button type="button" (click)="picker.click()" [disabled]="uploading()">
                <mat-icon>image</mat-icon> {{ 'scoresheet.choosePhoto' | translate }}
              </button>
            </div>

            @if (preview(); as src) {
              <img class="preview" [src]="src" [alt]="'scoresheet.previewAlt' | translate" />
            }

            <mat-form-field appearance="outline" class="lang">
              <mat-label>{{ 'scoresheet.language' | translate }}</mat-label>
              <mat-select [(ngModel)]="language" (ngModelChange)="rememberLanguage($event)" name="language">
                <mat-option value="auto">{{ 'scoresheet.languageAuto' | translate }}</mat-option>
                @for (l of st.languages; track l.code) {
                  <mat-option [value]="l.code">{{ l.name }} <span class="pieces">({{ l.pieces }})</span></mat-option>
                }
              </mat-select>
            </mat-form-field>

            <!-- Die eigene Seite: dreht Partieseite, Teilen-Link und Vorschaubild (0.531.0). „automatisch" sucht den
                 Profilnamen unter den gelesenen Spielernamen. -->
            <div class="side">
              <span class="side-label">{{ 'scoresheet.side' | translate }}</span>
              <mat-button-toggle-group [value]="side" (change)="setSide($event.value)" hideSingleSelectionIndicator>
                <mat-button-toggle value="white">{{ 'scoresheet.sideWhite' | translate }}</mat-button-toggle>
                <mat-button-toggle value="black">{{ 'scoresheet.sideBlack' | translate }}</mat-button-toggle>
                <mat-button-toggle value="auto">{{ 'scoresheet.sideAuto' | translate }}</mat-button-toggle>
              </mat-button-toggle-group>
            </div>

            <div class="actions">
              <button mat-flat-button color="primary" (click)="upload()" [disabled]="!file() || uploading() || !canRead()">
                <mat-icon>{{ uploading() ? 'hourglass_top' : 'document_scanner' }}</mat-icon> {{ 'scoresheet.read' | translate }}
              </button>
              @if (!st.unlimited) {
                <span class="quota">{{ 'scoresheet.quota' | translate: { used: st.usedToday, limit: st.dailyLimit } }}
                  · {{ 'scoresheet.budget' | translate: { percent: st.budgetUsedPercent ?? 0 } }}</span>
              }
            </div>
            @if (st.blocked) { <p class="error">{{ 'scoresheet.error.' + st.blocked | translate }}</p> }
            @if (error(); as e) { <p class="error">{{ e }}</p> }
          </mat-card>
        }
      } @else {
        <div class="center"><mat-spinner diameter="36"></mat-spinner></div>
      }

      @if (current(); as scan) {
        <mat-card class="progress" [class.failed]="scan.status === 'failed'">
          @switch (scan.status) {
            @case ('done') {
              <div class="done">
                <mat-icon class="ok">task_alt</mat-icon>
                <div>
                  <strong>{{ 'scoresheet.doneTitle' | translate: { moves: scan.moveCount } }}</strong>
                  <div class="facts">
                    @if (scan.uncertainCount > 0) { <span class="warn">{{ 'scoresheet.uncertain' | translate: { count: scan.uncertainCount } }}</span> }
                    @if (scan.unresolvedCount > 0) { <span class="bad">{{ 'scoresheet.unresolved' | translate: { count: scan.unresolvedCount } }}</span> }
                    @if (scan.uncertainCount === 0 && scan.unresolvedCount === 0) { <span>{{ 'scoresheet.allClear' | translate }}</span> }
                  </div>
                </div>
              </div>
              <div class="actions">
                <a mat-flat-button color="primary" [routerLink]="['/games', scan.savedGameId, 'edit']">
                  <mat-icon>edit_note</mat-icon> {{ 'scoresheet.review' | translate }}
                </a>
                <a mat-stroked-button [routerLink]="['/games', scan.savedGameId]">
                  <mat-icon>play_arrow</mat-icon> {{ 'scoresheet.openGame' | translate }}
                </a>
              </div>
            }
            @case ('failed') {
              <div class="done">
                <mat-icon class="bad">error_outline</mat-icon>
                <span>{{ 'scoresheet.error.' + (scan.error || 'failed') | translate }}</span>
              </div>
            }
            @default {
              <div class="running">
                <mat-spinner diameter="28"></mat-spinner>
                <div>
                  <strong>{{ (scan.status === 'pending' ? 'scoresheet.pending' : 'scoresheet.running') | translate }}</strong>
                  <div class="facts">{{ 'scoresheet.runningHint' | translate: { seconds: elapsed() } }}</div>
                </div>
              </div>
            }
          }
        </mat-card>
      }

      @if (recent().length > 0) {
        <h2>{{ 'scoresheet.recent' | translate }}</h2>
        <div class="recent">
          @for (s of recent(); track s.id) {
            <div class="row">
              <mat-icon class="state" [class.bad]="s.status === 'failed'" [class.ok]="s.status === 'done'">{{ stateIcon(s) }}</mat-icon>
              <span class="when">{{ s.createdAt | date:'short' }}</span>
              <span class="what">
                @if (s.status === 'done') {
                  {{ s.white || '?' }} – {{ s.black || '?' }} · {{ 'scoresheet.moves' | translate: { count: s.moveCount } }}
                  @if (s.uncertainCount > 0) { · <span class="warn">{{ 'scoresheet.uncertain' | translate: { count: s.uncertainCount } }}</span> }
                } @else if (s.status === 'failed') {
                  <span class="bad">{{ 'scoresheet.error.' + (s.error || 'failed') | translate }}</span>
                } @else {
                  {{ (s.status === 'pending' ? 'scoresheet.pending' : 'scoresheet.running') | translate }}
                }
              </span>
              @if (s.savedGameId) {
                <a mat-icon-button [routerLink]="['/games', s.savedGameId, 'edit']" [attr.aria-label]="'scoresheet.review' | translate">
                  <mat-icon>edit_note</mat-icon>
                </a>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .sheet-page { max-width: 760px; margin: 0 auto; padding: 16px; }
    .head { display: flex; align-items: center; gap: 4px; }
    .head h1 { margin: 0; }
    .hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 4px 0 16px; font-size: 0.9rem; }
    .center { display: flex; justify-content: center; padding: 32px; }
    .notice { display: flex; align-items: center; gap: 12px; padding: 16px; }
    .upload { display: flex; flex-direction: column; gap: 12px; padding: 16px; }
    .pick { display: flex; flex-wrap: wrap; gap: 8px; }
    .preview { max-width: 100%; max-height: 360px; object-fit: contain; align-self: center; border-radius: 4px;
      border: 1px solid color-mix(in srgb, currentColor 15%, transparent); }
    .lang { width: 100%; max-width: 360px; }
    .pieces { opacity: 0.6; font-size: 0.85em; }
    .side { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; }
    .side-label { font-size: 0.9rem; }
    .actions { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 12px; }
    .quota { font-size: 0.85rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .error { color: var(--mat-sys-error, #c62828); margin: 0; }
    .progress { margin-top: 16px; padding: 16px; display: flex; flex-direction: column; gap: 12px; }
    .running, .done { display: flex; align-items: center; gap: 12px; }
    .facts { font-size: 0.85rem; display: flex; flex-wrap: wrap; gap: 8px; color: color-mix(in srgb, currentColor 70%, transparent); }
    .ok { color: #2e7d32; }
    .bad { color: var(--mat-sys-error, #c62828); }
    .warn { color: #ef6c00; }
    h2 { font-size: 1.05rem; margin: 24px 0 8px; }
    .recent { display: flex; flex-direction: column; }
    .row { display: grid; grid-template-columns: 28px 120px minmax(0, 1fr) 40px; align-items: center; gap: 8px;
      padding: 4px 0; border-bottom: 1px solid color-mix(in srgb, currentColor 10%, transparent); }
    .when { font-size: 0.8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .what { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .state { opacity: 0.8; }
    @media (max-width: 520px) { .row { grid-template-columns: 28px minmax(0, 1fr) 40px; } .when { display: none; } }
  `],
})
export class ScoresheetUploadComponent implements OnInit, OnDestroy {
  private service = inject(ScoresheetService);
  private translate = inject(TranslateService);
  private destroyRef = inject(DestroyRef);

  readonly status = signal<ScoresheetStatus | null>(null);
  readonly file = signal<File | null>(null);
  readonly preview = signal<string | null>(null);
  readonly uploading = signal(false);
  readonly error = signal<string | null>(null);
  readonly current = signal<ScoresheetScan | null>(null);
  readonly recent = signal<ScoresheetScan[]>([]);
  readonly elapsed = signal(0);
  /** Darf gerade eingelesen werden? Kostenbudget und Tageszahl — Admins nur das Gesamtbudget. */
  readonly canRead = computed(() => {
    const st = this.status();
    if (!st || st.blocked) return false;
    return !!st.unlimited || st.usedToday < st.dailyLimit;
  });

  language = 'auto';
  side: 'white' | 'black' | 'auto' = 'auto';
  private poll?: Subscription;
  private tick?: Subscription;

  ngOnInit(): void {
    try {
      this.language = localStorage.getItem(SCORESHEET_LANG_KEY) || 'auto';
      const side = localStorage.getItem(SCORESHEET_SIDE_KEY);
      if (side === 'white' || side === 'black' || side === 'auto') this.side = side;
    } catch { /* privat: Vorgabe */ }
    this.loadStatus();
    this.loadRecent();
  }

  ngOnDestroy(): void {
    this.revokePreview();
  }

  setSide(side: 'white' | 'black' | 'auto'): void {
    this.side = side;
    try { localStorage.setItem(SCORESHEET_SIDE_KEY, side); } catch { /* nur Bequemlichkeit */ }
  }

  rememberLanguage(code: string): void {
    try { localStorage.setItem(SCORESHEET_LANG_KEY, code); } catch { /* nur Bequemlichkeit */ }
  }

  onFile(event: Event): void {
    const input = event.target as HTMLInputElement;
    const f = input.files?.[0] ?? null;
    input.value = '';
    if (!f) return;
    this.error.set(null);
    this.revokePreview();
    this.file.set(f);
    this.preview.set(URL.createObjectURL(f));
  }

  upload(): void {
    const f = this.file();
    if (!f || this.uploading()) return;
    this.uploading.set(true);
    this.error.set(null);
    this.service.upload(f, this.language, this.side).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: scan => {
        this.uploading.set(false);
        this.file.set(null);
        this.revokePreview();
        this.follow(scan);
        this.loadStatus();
      },
      error: (err: HttpErrorResponse) => {
        this.uploading.set(false);
        const reason = err.error?.reason ?? 'failed';
        this.error.set(this.translate.instant('scoresheet.error.' + reason));
      },
    });
  }

  /** Einer Einlesung folgen, bis sie fertig oder gescheitert ist. */
  private follow(scan: ScoresheetScan): void {
    this.current.set(scan);
    this.poll?.unsubscribe();
    this.tick?.unsubscribe();
    const started = Date.now();
    this.elapsed.set(0);
    this.tick = timer(1000, 1000).pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe(() => this.elapsed.set(Math.round((Date.now() - started) / 1000)));
    this.poll = timer(SCAN_POLL_MS, SCAN_POLL_MS).pipe(
      switchMap(() => this.service.scan(scan.id)),
      takeWhile(s => s.status === 'pending' || s.status === 'running', true),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: s => {
        this.current.set(s);
        if (s.status === 'done' || s.status === 'failed') {
          this.tick?.unsubscribe();
          this.loadRecent();
        }
      },
      error: () => this.tick?.unsubscribe(),
    });
  }

  stateIcon(s: ScoresheetScan): string {
    return s.status === 'done' ? 'task_alt' : s.status === 'failed' ? 'error_outline' : 'hourglass_top';
  }

  private loadStatus(): void {
    this.service.status().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: st => this.status.set(st),
      error: () => this.status.set({ available: false, dailyLimit: 0, usedToday: 0, languages: [] }),
    });
  }

  private loadRecent(): void {
    this.service.recent(10).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => {
        this.recent.set(list);
        // Wer mit einer laufenden Einlesung zurückkommt, sieht sie wieder oben.
        const open = list.find(s => s.status === 'pending' || s.status === 'running');
        if (open && !this.current()) this.follow(open);
      },
      error: () => this.recent.set([]),
    });
  }

  private revokePreview(): void {
    const p = this.preview();
    if (p) URL.revokeObjectURL(p);
    this.preview.set(null);
  }
}
