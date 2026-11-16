import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal, untracked } from '@angular/core';
import { DatePipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { ChessBoardComponent } from '../../shared/pgn-viewer/chess-board.component';
import { SanPipe } from '../../shared/chess/san.pipe';
import { localizeSan } from '../../shared/chess/keyboard-move.util';
import { formatSecondsClock } from '../../shared/clock-format.util';
import { TrainingGameComment, TrainingVideosService } from '../training-videos/training-videos.service';

/** Eine Stellung der Partie mit Trainerkommentar — für die Sprungzeile. */
export interface TrainerCommentMark { fenIndex: number; label: string; }

/** Zugbezeichnung „4…Ld7“ der Stellung NACH dem Halbzug `fenIndex` (0 = Anfangsstellung → `null`). */
export function plyLabel(fens: readonly string[], sans: readonly string[], fenIndex: number, lang: string | null): string | null {
  if (fenIndex <= 0 || fenIndex > sans.length) return null;
  const before = fens[fenIndex - 1] ?? '';
  const parts = before.split(/\s+/);
  const white = (parts[1] ?? 'w') === 'w';
  const no = Number(parts[5]) || Math.ceil(fenIndex / 2);
  return `${no}${white ? '.' : '…'}${localizeSan(sans[fenIndex - 1], lang)}`;
}

/**
 * Trainerkommentare an der eigenen Partie (`/games/:id`, nur mit Menüpunkt „Trainerstunden“): lädt einmal je Partie
 * `GET /api/training-videos/games/{id}/comments` und zeigt die Kommentare der Stellung auf dem Brett (`fenIndex` =
 * `currentMoveIndex + 1`, als Eingabe hereingereicht, damit die Karte unter OnPush mitzieht). Darüber eine leise Zeile mit
 * allen kommentierten Zügen zum Hinspringen. Gibt es keine Kommentare, rendert sie gar nichts.
 *
 * Bewusst getrennt von der Engine-Erklärung („Warum war das ein Fehler?“, 💬): eigene umrandete Karte mit Überschrift
 * „Trainerkommentar · Datum“, Symbol `school`, kein Sprechblasen-Zeichen (DESIGN 8.1).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  selector: 'app-trainer-comments',
  standalone: true,
  imports: [DatePipe, RouterLink, MatButtonModule, TranslatePipe, ChessBoardComponent, SanPipe],
  template: `
    @if (marks().length) {
      <section class="trainer" [attr.aria-label]="'trainingVideos.comments.movesLine' | translate">
        <p class="marks">
          <span class="lead">{{ 'trainingVideos.comments.movesLine' | translate }}</span>
          @for (m of marks(); track m.fenIndex) {
            <button type="button" class="mark" [class.here]="m.fenIndex === fenIndex()" [attr.aria-current]="m.fenIndex === fenIndex() ? 'true' : null"
                    (click)="goTo.emit(m.fenIndex - 1)">{{ m.label }}</button>
          }
        </p>
        @for (c of here(); track c.segmentId) {
          <article class="tc-card">
            <h3><span class="tc-icon" aria-hidden="true">🎓</span>{{ 'trainingVideos.comments.title' | translate: { date: (c.date | date: 'mediumDate') } }}</h3>
            @if (c.matchedBy === 'variation' && c.fen) {
              <div class="side-line">
                <div class="mini"><app-chess-board [fen]="c.fen" /></div>
                <p class="muted">{{ 'trainingVideos.comments.sideLine' | translate }}@if (c.move) { · <strong>{{ c.move | san }}</strong>}</p>
              </div>
            }
            @if (c.comment) { <p class="text">{{ c.comment }}</p> }
            @if (c.variations) { <p class="variations">{{ c.variations | san: 'text' }}</p> }
            <div class="links">
              @if (c.hasVideo) {
                <a mat-button class="tc-video" [routerLink]="['/training-videos', c.lessonId]" [queryParams]="{ t: c.startSec, seg: c.segmentId }">
                  {{ 'trainingVideos.comments.inVideo' | translate: { time: clock(c.startSec) } }}
                </a>
              }
              <a mat-button class="tc-drill" [routerLink]="['/training-videos', c.lessonId, 'drill']" [queryParams]="{ seg: c.segmentId }">
                {{ 'trainingVideos.replay' | translate }}
              </a>
            </div>
          </article>
        }
      </section>
    }
  `,
  styles: [`
    :host { display: block; }
    .trainer { display: flex; flex-direction: column; gap: 8px; }
    .marks { margin: 0; display: flex; flex-wrap: wrap; align-items: center; gap: 2px 6px; font-size: .85rem;
      color: var(--mat-sys-on-surface-variant); }
    .lead { margin-right: 2px; }
    .mark { background: none; border: 0; padding: 0 2px; min-height: 28px; font: inherit; color: var(--mat-sys-on-surface);
      text-decoration: underline dotted; cursor: pointer; }
    .mark.here { font-weight: 700; text-decoration: underline solid; }
    @media (pointer: coarse) { .mark { min-height: 44px; min-width: 44px; } }
    .tc-card { border: 1px solid var(--mat-sys-outline); border-left: 4px solid var(--rh-info); border-radius: 8px; padding: 10px 12px 4px;
      background: color-mix(in srgb, var(--rh-info) 6%, transparent); overflow-wrap: anywhere; }
    .tc-card h3 { margin: 0 0 4px; font-size: .95rem; display: flex; align-items: center; gap: 6px; }
    .tc-card p { margin: 4px 0; }
    .side-line { display: flex; gap: 10px; align-items: center; }
    .mini { width: 120px; flex: none; }
    .muted { color: var(--mat-sys-on-surface-variant); font-size: .85rem; }
    .links { display: flex; flex-wrap: wrap; gap: 4px; }
    .links a { min-height: 44px; }
  `],
})
export class TrainerCommentsComponent {
  private readonly svc = inject(TrainingVideosService);
  private readonly translate = inject(TranslateService);

  readonly gameId = input.required<number>();
  /** Stellung auf dem Brett: 0 = Anfang, n = nach dem n-ten Halbzug. */
  readonly fenIndex = input.required<number>();
  readonly fens = input<readonly string[]>([]);
  readonly sans = input<readonly string[]>([]);
  /** Sprung zu einem Zug (`currentMoveIndex`, -1 = Anfang). */
  readonly goTo = output<number>();

  readonly items = signal<TrainingGameComment[]>([]);

  readonly here = computed(() => this.items().filter(c => c.fenIndex === this.fenIndex()));
  readonly marks = computed<TrainerCommentMark[]>(() => {
    const lang = this.translate.currentLang() ?? null;
    const idx = [...new Set(this.items().map(c => c.fenIndex))].sort((a, b) => a - b);
    return idx.map(i => ({ fenIndex: i, label: plyLabel(this.fens(), this.sans(), i, lang) ?? this.translate.instant('trainingVideos.comments.start') }));
  });

  constructor() {
    effect(() => {
      const id = this.gameId();
      untracked(() => {
        this.items.set([]);
        // Still bei Fehlern: Zusatzinfo der Partieseite, kein Schritt des Nutzers (Fehlerregel „Hintergrund-Feed“).
        this.svc.gameComments(id).subscribe({ next: r => this.items.set(r?.items ?? []), error: () => this.items.set([]) });
      });
    });
  }

  clock(sec: number): string { return formatSecondsClock(sec); }
}
