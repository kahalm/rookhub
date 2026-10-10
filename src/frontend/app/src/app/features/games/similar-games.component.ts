import { ChangeDetectionStrategy, Component, DestroyRef, inject, input, signal } from '@angular/core';
import { Router } from '@angular/router';
import { MatButtonModule } from '@angular/material/button';
import { MatDialog } from '@angular/material/dialog';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { AuthService } from '../../core/auth.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GameAnalysis, GameAnalysisService } from '../analysis/game-analysis.service';
import { GuessService } from '../guess/guess.service';
import { LibraryService } from '../guess/library.service';
import { GamesService, SimilarGame, SimilarGames } from './games.service';

/**
 * „Ähnliche Meisterpartien" (0.544.0) unter der Partie: kommentierte Partien des Rohbestands, die am längsten dieselben
 * Züge gespielt haben, mit der Stelle, an der der Meister abbog. Eingeklappt und erst beim Aufklappen geladen — die Suche
 * kostet ein paar Zählungen, und die Seite ist auch ohne sie vollständig. Von dort geht es wie im Anforderungs-Dialog
 * weiter: spielen, was schon spielbar ist, sonst anfordern (angemeldet).
 *
 * Seit 0.567.0 (gewünscht 2026-09-27): angemeldet lässt sich jede Partie auch einfach ANSCHAUEN — Züge und Anmerkungen im
 * Nachspiel-Dialog, in der Sprache der Oberfläche, ohne Rechnen (`GET /api/library-games/{id}/view`). Und eine angeforderte
 * Partie steht erst dann auf „Spielen", wenn die Engine sie FERTIG gerechnet hat; bis dahin zeigt die Zeile den Fortschritt
 * (aus der eigenen Analysenliste, alle `PollMs` nachgefragt, solange eine rechnet). Vorher sprang sie sofort auf „Spielen",
 * und das Spiel stand dann vor ungerechneten Stellungen.
 */
@Component({
  selector: 'app-similar-games',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TranslatePipe, MatButtonModule, MatIconModule, MatProgressSpinnerModule, MatTooltipModule],
  template: `
    <details class="similar" (toggle)="onToggle($event)">
      <summary><mat-icon>auto_stories</mat-icon> <span class="label">{{ 'games.similar.title' | translate }}</span>
        <mat-icon class="chevron">expand_more</mat-icon></summary>
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
              <div class="actions">
                @if (loggedIn) {
                  <button mat-icon-button class="view" [disabled]="busy() === s.game.id" (click)="view(s)"
                          [matTooltip]="'games.similar.view' | translate" [attr.aria-label]="'games.similar.view' | translate">
                    <mat-icon>visibility</mat-icon>
                  </button>
                }
                @switch (state(s)) {
                  @case ('play') {
                    <button mat-stroked-button class="play" [disabled]="busy() === s.game.id" (click)="play(s)">
                      <mat-icon>play_arrow</mat-icon> {{ 'guess.play' | translate }}
                    </button>
                  }
                  @case ('computing') {
                    <span class="computing" [matTooltip]="'games.similar.computingHint' | translate">
                      <mat-spinner diameter="14"></mat-spinner>
                      {{ 'games.similar.computing' | translate: { percent: percent(s) } }}
                    </span>
                  }
                  @case ('request') {
                    <button mat-stroked-button class="request" [disabled]="busy() === s.game.id" (click)="request(s)">
                      <mat-icon>hourglass_top</mat-icon>
                      {{ (analysisFailed(s) ? 'games.similar.retry' : 'guess.library.request') | translate }}
                    </button>
                  }
                  @default {
                    <button mat-stroked-button class="login" (click)="login()">{{ 'guess.library.requestNeedsLogin' | translate }}</button>
                  }
                }
              </div>
            </div>
          }
        }
      }
    </details>
  `,
  styles: [`
    :host { display: block; width: 100%; }
    /* Rahmen bringt die Seite mit (Seitenspalte der Partieseite) — hier nur Zeile und Inhalt. */
    .similar { border-radius: inherit; }
    summary {
      cursor: pointer; display: flex; align-items: center; gap: 8px; padding: 10px 12px;
      font-size: 0.92rem; font-weight: 500; list-style: none; border-radius: inherit;
    }
    summary::-webkit-details-marker { display: none; }
    summary:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    summary mat-icon { font-size: 18px; width: 18px; height: 18px; opacity: 0.7; }
    summary .label { flex: 1; }
    summary .chevron { font-size: 20px; width: 20px; height: 20px; transition: transform 0.15s; }
    .similar[open] summary .chevron { transform: rotate(180deg); }
    .similar[open] summary { border-bottom: 1px solid color-mix(in srgb, currentColor 10%, transparent); border-radius: 0; }
    .similar > :not(summary) { margin-left: 12px; margin-right: 12px; }
    .similar[open] { padding-bottom: 6px; }
    .center { display: flex; justify-content: center; padding: 8px; }
    .muted { font-size: 0.82rem; color: color-mix(in srgb, currentColor 65%, transparent); margin: 6px 0; }
    .row { display: flex; align-items: center; justify-content: space-between; gap: 10px; padding: 6px 0;
           border-top: 1px solid color-mix(in srgb, currentColor 8%, transparent); }
    .who { display: flex; flex-direction: column; min-width: 0; font-size: 0.86rem; }
    .meta, .branch { font-size: 0.78rem; color: color-mix(in srgb, currentColor 70%, transparent); }
    .actions { display: flex; align-items: center; gap: 2px; flex: 0 0 auto; }
    .computing { display: inline-flex; align-items: center; gap: 6px; font-size: 0.8rem; white-space: nowrap;
                 color: color-mix(in srgb, currentColor 70%, transparent); padding: 0 6px; }
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
  private analyses = inject(GameAnalysisService);
  private dialog = inject(MatDialog);

  /** So oft wird der Stand angeforderter Partien nachgefragt, solange eine rechnet. */
  static readonly PollMs = 10_000;

  /** `GET …/similar` dieser Partie (eigene oder Teilen-Link). */
  readonly url = input<string | null>(null);

  readonly data = signal<SimilarGames | null>(null);
  readonly loading = signal(false);
  readonly failed = signal(false);
  readonly busy = signal<number | null>(null);
  /** Stand der EIGENEN Analysen (angeforderte Partien) je Analyse-Id — die des Bestands sind ohnehin spielbar. */
  readonly progress = signal<ReadonlyMap<number, GameAnalysis>>(new Map());
  private pollTimer: ReturnType<typeof setTimeout> | null = null;
  private readonly stopPoll = inject(DestroyRef).onDestroy(() => this.clearPoll());

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
      next: d => { this.data.set(d); this.loading.set(false); this.refreshProgress(); },
      error: () => { this.failed.set(true); this.loading.set(false); },
    });
  }

  /**
   * Was die Zeile anbietet: `play` (im Bestand oder fertig gerechnet), `computing` (angefordert, rechnet noch), `request`
   * (angemeldet, noch nicht angefordert — oder die eigene Rechnung ist gescheitert), `login` (anonym).
   */
  state(s: SimilarGame): 'play' | 'computing' | 'request' | 'login' {
    const g = s.game;
    if (g.gameAnalysisId && (g.inPool || !g.requested)) return 'play';
    if (g.gameAnalysisId) {
      const a = this.progress().get(g.gameAnalysisId);
      if (!a) return 'computing';           // noch nicht nachgesehen — lieber warten lassen als ins Leere spielen
      if (a.status === 'done') return 'play';
      if (a.status === 'failed') return this.loggedIn ? 'request' : 'login';
      return 'computing';
    }
    return this.loggedIn ? 'request' : 'login';
  }

  /** Die eigene Rechnung dieser Partie ist gescheitert — der Knopf heißt dann „Erneut anfordern". */
  analysisFailed(s: SimilarGame): boolean {
    const id = s.game.gameAnalysisId;
    return !!id && this.progress().get(id)?.status === 'failed';
  }

  percent(s: SimilarGame): number {
    const a = s.game.gameAnalysisId ? this.progress().get(s.game.gameAnalysisId) : undefined;
    return a && a.plyCount > 0 ? Math.floor((a.analyzedPlies / a.plyCount) * 100) : 0;
  }

  /** Den Stand der eigenen angeforderten Partien holen — und wieder nachfragen, solange eine rechnet. */
  private refreshProgress(): void {
    this.clearPoll();
    const ids = this.ownAnalysisIds();
    if (!ids.length || !this.loggedIn) return;
    this.analyses.list().subscribe({
      next: list => {
        const byId = new Map(list.filter(a => ids.includes(a.id)).map(a => [a.id, a] as const));
        this.progress.set(byId);
        if (ids.some(id => { const st = byId.get(id)?.status; return st !== 'done' && st !== 'failed'; })) this.schedulePoll();
      },
      error: () => this.schedulePoll(),
    });
  }

  private ownAnalysisIds(): number[] {
    return (this.data()?.items ?? [])
      .filter(s => s.game.requested && !s.game.inPool && s.game.gameAnalysisId)
      .map(s => s.game.gameAnalysisId!);
  }

  private schedulePoll(): void {
    this.clearPoll();
    this.pollTimer = setTimeout(() => this.refreshProgress(), SimilarGamesComponent.PollMs);
  }

  private clearPoll(): void {
    if (this.pollTimer) { clearTimeout(this.pollTimer); this.pollTimer = null; }
  }

  /** Anschauen: die Partie mit ihren Anmerkungen im Nachspiel-Dialog (der Dialog wird erst hier nachgeladen). */
  view(s: SimilarGame): void {
    if (this.busy()) return;
    this.busy.set(s.game.id);
    this.library.view(s.game.id, this.translate.currentLang() || undefined).subscribe({
      next: async v => {
        try {
          const { PgnViewerComponent } = await import('../../shared/pgn-viewer/pgn-viewer.component');
          this.dialog.open(PgnViewerComponent, { data: { pgn: v.pgn }, maxWidth: '96vw', panelClass: 'pgn-viewer-dialog' });
        } finally {
          this.busy.set(null);
        }
      },
      error: () => { this.busy.set(null); this.snackbar.warn(this.translate.instant('games.similar.viewFailed')); },
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
            ? { ...x, game: { ...x.game, requested: !x.game.inPool, gameAnalysisId: r.analysis.id } } : x),
        });
        this.progress.update(m => new Map(m).set(r.analysis.id, r.analysis));
        const done = r.analysis.status === 'done';
        this.snackbar.success(this.translate.instant(done ? 'guess.library.alreadyThere' : 'games.similar.queued'));
        this.refreshProgress();
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
