import { ChangeDetectionStrategy, Component, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GuessService } from '../guess/guess.service';
import { LibraryService } from '../guess/library.service';
import { GamesService, SimilarGame, SimilarGames } from './games.service';

/**
 * „Ähnliche Meisterpartien" (0.544.0) unter der Partie: kommentierte Partien des Rohbestands, die am längsten dieselben
 * Züge gespielt haben, mit der Stelle, an der der Meister abbog. Eingeklappt und erst beim Aufklappen geladen — die Suche
 * kostet ein paar Zählungen, und die Seite ist auch ohne sie vollständig. Von dort geht es wie im Anforderungs-Dialog
 * weiter: spielen, was schon spielbar ist, sonst anfordern (angemeldet).
 */
@Component({
  selector: 'app-similar-games',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, MatButtonModule, MatIconModule, MatProgressSpinnerModule],
  template: `
    <details class="similar" (toggle)="onToggle($event)">
      <summary><mat-icon>auto_stories</mat-icon> {{ 'games.similar.title' | translate }}</summary>
      @if (loading()) {
        <div class="center"><mat-spinner diameter="22"></mat-spinner></div>
      } @else if (failed()) {
        <p class="muted">{{ 'games.similar.loadFailed' | translate }}</p>
      } @else if (data(); as d) {
        @if (!d.items.length) {
          <p class="muted">{{ 'games.similar.none' | translate }}</p>
        } @else {
          <p class="muted shared">{{ 'games.similar.sharedLine' | translate: { line: d.sharedLine } }}</p>
          @for (s of d.items; track s.game.id) {
            <div class="row">
              <div class="who">
                <strong>{{ s.game.white || '?' }} – {{ s.game.black || '?' }}</strong>
                <span class="meta">{{ meta(s) }}</span>
                <span class="branch">{{ branch(s) }}</span>
              </div>
              @if (s.game.gameAnalysisId) {
                <button mat-stroked-button class="play" [disabled]="busy() === s.game.id" (click)="play(s)">
                  <mat-icon>play_arrow</mat-icon> {{ 'guess.play' | translate }}
                </button>
              } @else if (loggedIn) {
                <button mat-stroked-button class="request" [disabled]="busy() === s.game.id" (click)="request(s)">
                  <mat-icon>hourglass_top</mat-icon> {{ 'guess.library.request' | translate }}
                </button>
              } @else {
                <button mat-stroked-button class="login" (click)="login()">{{ 'guess.library.requestNeedsLogin' | translate }}</button>
              }
            </div>
          }
        }
      }
    </details>
  `,
  styles: [`
    :host { display: block; width: 100%; }
    .similar { border: 1px solid color-mix(in srgb, currentColor 12%, transparent); border-radius: 4px; padding: 6px 10px; }
    summary { cursor: pointer; display: flex; align-items: center; gap: 6px; font-size: 0.92rem; }
    summary mat-icon { font-size: 18px; width: 18px; height: 18px; opacity: 0.7; }
    .center { display: flex; justify-content: center; padding: 8px; }
    .muted { font-size: 0.82rem; color: color-mix(in srgb, currentColor 65%, transparent); margin: 6px 0; }
    .row { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 6px 0;
           border-top: 1px solid color-mix(in srgb, currentColor 8%, transparent); }
    .who { display: flex; flex-direction: column; min-width: 0; font-size: 0.86rem; }
    .meta, .branch { font-size: 0.78rem; color: color-mix(in srgb, currentColor 70%, transparent); }
    .row button { flex: 0 0 auto; }
  `],
})
export class SimilarGamesComponent {
  private games = inject(GamesService);
  private guess = inject(GuessService);
  private library = inject(LibraryService);
  private router = inject(Router);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private auth = inject(AuthService);

  /** `GET …/similar` dieser Partie (eigene oder Teilen-Link). */
  readonly url = input<string | null>(null);

  readonly data = signal<SimilarGames | null>(null);
  readonly loading = signal(false);
  readonly failed = signal(false);
  readonly busy = signal<number | null>(null);

  get loggedIn(): boolean { return this.auth.isLoggedIn; }

  onToggle(event: Event): void {
    const open = (event.target as HTMLDetailsElement | null)?.open;
    if (open && !this.data() && !this.loading()) this.load();
  }

  private load(): void {
    const url = this.url();
    if (!url) return;
    this.loading.set(true);
    this.failed.set(false);
    this.games.similar(url).subscribe({
      next: d => { this.data.set(d); this.loading.set(false); },
      error: () => { this.failed.set(true); this.loading.set(false); },
    });
  }

  /** „London 2001 · kommentiert von X". */
  meta(s: SimilarGame): string {
    const g = s.game;
    const year = g.playedOn ? g.playedOn.slice(0, 4) : '';
    const place = [g.event && g.event !== '?' ? g.event : '', year].filter(Boolean).join(' ');
    const by = g.annotator ? this.translate.instant('games.review.masterBy', { name: g.annotator }) : '';
    return [place, by].filter(Boolean).join(' · ');
  }

  /** „gleich bis 7.Bb3, dann 7...O-O statt 7...d6". */
  branch(s: SimilarGame): string {
    return s.masterMove && s.gameMove
      ? this.translate.instant('games.similar.branch', { last: s.lastSharedMove, master: s.masterMove, game: s.gameMove })
      : this.translate.instant('games.similar.sameEnd', { last: s.lastSharedMove });
  }

  play(s: SimilarGame): void {
    const id = s.game.gameAnalysisId;
    if (!id || this.busy()) return;
    this.busy.set(s.game.id);
    this.guess.start(id).subscribe({
      next: session => { this.busy.set(null); this.router.navigate(['/guess', session.id]); },
      error: () => { this.busy.set(null); this.snackbar.warn(this.translate.instant('games.similar.playFailed')); },
    });
  }

  request(s: SimilarGame): void {
    if (this.busy()) return;
    this.busy.set(s.game.id);
    this.library.request(s.game.id).subscribe({
      next: r => {
        this.busy.set(null);
        this.data.update(d => d && {
          ...d,
          items: d.items.map(x => x.game.id === s.game.id
            ? { ...x, game: { ...x.game, requested: true, gameAnalysisId: r.analysis.id } } : x),
        });
        this.snackbar.success(this.translate.instant(r.alreadyPlayable ? 'guess.library.alreadyThere' : 'guess.library.queued'));
      },
      error: err => {
        this.busy.set(null);
        const reason = err?.error?.reason;
        this.snackbar.warn(reason
          ? this.translate.instant('guess.upload.reason.' + reason)
          : this.translate.instant('guess.library.requestFailed'));
      },
    });
  }

  login(): void {
    this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
  }
}
