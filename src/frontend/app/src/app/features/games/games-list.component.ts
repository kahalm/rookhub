import { Component, OnInit, inject, DestroyRef, ChangeDetectionStrategy } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatCheckboxModule } from '@angular/material/checkbox';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { GamesService, SavedGame } from './games.service';
import { AnalyzeGameService } from './analyze-game.service';
import { GuessUploadStatus } from '../analysis/game-analysis.service';
import { SnackbarService } from '../../core/snackbar.service';

/** Solange eine Analyse läuft, holt die Liste alle zehn Sekunden den Stand — derselbe Takt wie die Kurve. */
export const ANALYSIS_POLL_MS = 10_000;

/** Was die Liste zu einer Partie zeigt: Knopf (keine/gescheiterte Analyse), Prozent (läuft) oder nichts (fertig). */
export type AnalysisState = 'none' | 'running' | 'done';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-games-list',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressSpinnerModule, MatCheckboxModule, TranslatePipe,
  ],
  template: `
    <div class="games-page">
      <div class="head">
        <h1>{{ 'games.title' | translate }}</h1>
        <p class="hint">{{ 'games.hint' | translate }}</p>
        <!-- „Wo liegt noch Arbeit?" — der Filter zeigt nur Partien mit offenen Fehlern. Er erscheint erst,
             wenn es überhaupt welche gibt, sonst stünde ein Schalter da, der nichts tut. -->
        @if (withOpenMistakes() > 0) {
          <mat-checkbox class="only-open" [(ngModel)]="onlyOpen" name="onlyOpen">
            {{ 'games.mistakes.onlyOpen' | translate: { count: withOpenMistakes() } }}
          </mat-checkbox>
        }
      </div>

      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (games.length === 0) {
        <mat-card class="empty">
          <mat-icon>sports_esports</mat-icon>
          <p>{{ 'games.empty' | translate }}</p>
        </mat-card>
      } @else {
        <div class="list">
          @for (g of shownGames(); track g.id) {
            <mat-card class="game">
              <div class="info">
                <mat-icon class="src" [matTooltip]="g.source">{{ sourceIcon(g.source) }}</mat-icon>
                <div class="players">
                  <!-- Der Name führt auf die Partie-SEITE (/games/:id) — kein Dialog mehr (gemeldet 2026-09-23). -->
                  <a class="vs" [routerLink]="['/games', g.id]"><strong>{{ g.white || '?' }}</strong> – <strong>{{ g.black || '?' }}</strong></a>
                  <span class="meta">
                    @if (g.result && g.result !== '*') { <span class="result">{{ g.result }}</span> }
                    <span>{{ g.moveCount }} {{ 'games.moves' | translate }}</span>
                    <span class="date">{{ (g.playedAt || g.createdAt) | date:'mediumDate' }}</span>
                    <!-- Fertig gerechnet: die Genauigkeit beider Seiten steht hier bei den Partie-Daten, der
                         Analysieren-Knopf ist dann weg (gewünscht 2026-09-24). -->
                    @if (analysisState(g) === 'done') {
                      <span class="accuracy" [matTooltip]="'games.accuracyHint' | translate">
                        ♔ {{ pct(g.analysis?.accuracyWhite) }} · ♚ {{ pct(g.analysis?.accuracyBlack) }}
                      </span>
                    }
                    <!-- Stand des Fehler-Trainings (0.524.0): offene Aufgaben hervorgehoben — das ist die
                         Zeile, wegen der man die Partie noch einmal aufmacht. -->
                    @if (g.mistakes; as m) {
                      <span class="mistakes" [class.open]="m.open > 0" [matTooltip]="'games.mistakes.tooltip' | translate">
                        {{ 'games.mistakes.progressShort' | translate: { solved: m.solved, total: m.total, open: m.open } }}
                      </span>
                    }
                  </span>
                </div>
              </div>
              <div class="actions">
                <a mat-icon-button [routerLink]="['/games', g.id]" [matTooltip]="'games.replay' | translate" [attr.aria-label]="'games.replay' | translate">
                  <mat-icon>play_arrow</mat-icon>
                </a>
                <button mat-icon-button (click)="openInAnalysis(g)" [matTooltip]="'games.openInAnalysis' | translate" [attr.aria-label]="'games.openInAnalysis' | translate">
                  <mat-icon>biotech</mat-icon>
                </button>
                @switch (analysisState(g)) {
                  @case ('running') {
                    <!-- Statt des Knopfs der Fortschritt — er läuft mit dem 10-s-Nachfragen mit. -->
                    <span class="progress" [matTooltip]="progressTip(g)">{{ progressPercent(g) }} %</span>
                  }
                  @case ('none') {
                    <!-- Derselbe Weg wie auf der geteilten Partie: rechnen lassen, die Kurve steht danach auf der Partie-Seite. -->
                    <button mat-icon-button class="analyze" (click)="analyze(g)"
                            [disabled]="analyzingId === g.id || uploadStatus?.engineAvailable === false"
                            [matTooltip]="(uploadStatus?.engineAvailable === false ? 'guess.upload.noEngine' : 'games.analyze') | translate"
                            [attr.aria-label]="'games.analyze' | translate">
                      <mat-icon>{{ analyzingId === g.id ? 'hourglass_top' : 'insights' }}</mat-icon>
                    </button>
                  }
                }
                <button mat-icon-button (click)="share(g)" [matTooltip]="'games.share' | translate" [attr.aria-label]="'games.share' | translate">
                  <mat-icon>share</mat-icon>
                </button>
                @if (g.sourceUrl) {
                  <a mat-icon-button [href]="g.sourceUrl" target="_blank" rel="noopener" [matTooltip]="'games.openOriginal' | translate" [attr.aria-label]="'games.openOriginal' | translate">
                    <mat-icon>open_in_new</mat-icon>
                  </a>
                }
                <button mat-icon-button color="warn" (click)="remove(g)" [matTooltip]="'common.delete' | translate" [attr.aria-label]="'common.delete' | translate">
                  <mat-icon>delete</mat-icon>
                </button>
              </div>
            </mat-card>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .games-page { max-width: 900px; margin: 0 auto; padding: 16px; }
    .head h1 { margin: 0 0 4px; }
    .hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0 0 16px; font-size: 0.9rem; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .list { display: flex; flex-direction: column; gap: 8px; }
    .game { display: flex; align-items: center; justify-content: space-between; gap: 12px; padding: 8px 12px; }
    .info { display: flex; align-items: center; gap: 12px; min-width: 0; }
    .src { flex-shrink: 0; opacity: 0.7; }
    .players { display: flex; flex-direction: column; min-width: 0; }
    .vs { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; color: inherit; text-decoration: none; }
    .vs:hover { text-decoration: underline; }
    .meta { display: flex; flex-wrap: wrap; gap: 10px; font-size: 0.8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    .result { color: #1976d2; font-weight: 600; }
    .mistakes { white-space: nowrap; }
    .mistakes.open { color: #e58f2a; font-weight: 500; }
    .only-open { margin-top: 4px; }
    .accuracy { font-variant-numeric: tabular-nums; white-space: nowrap; color: color-mix(in srgb, currentColor 80%, transparent); }
    .actions { display: flex; align-items: center; flex-shrink: 0; }
    /* So breit wie ein Icon-Knopf, damit die Zeile beim Wechsel Knopf → Prozent nicht springt. */
    .progress {
      display: inline-flex; align-items: center; justify-content: center; width: 40px; height: 40px;
      font-size: 0.8rem; font-variant-numeric: tabular-nums; color: #1976d2; cursor: default;
    }
    @media (max-width: 600px) {
      .game { flex-direction: column; align-items: stretch; }
      .actions { justify-content: flex-end; }
    }
  `]
})
export class GamesListComponent implements OnInit {
  games: SavedGame[] = [];
  loading = true;
  /** Filter „nur mit offenen Fehlern" — bewusst NICHT gemerkt: er beantwortet eine Frage von jetzt. */
  onlyOpen = false;
  /** Welche Partie gerade als Punktepartie eingeworfen wird (sperrt nur ihren Knopf). */
  analyzingId: number | null = null;
  /** Engine da / Plätze frei? `null` = Auskunft fehlt, der Server entscheidet beim Einwurf. */
  uploadStatus: GuessUploadStatus | null = null;
  /** Partien mit mindestens einem noch nicht selbst gefundenen Fehler. */
  withOpenMistakes(): number {
    return this.games.filter(g => (g.mistakes?.open ?? 0) > 0).length;
  }

  /** Die angezeigte Liste — ungefiltert, oder nur die mit offenen Fehlern. */
  shownGames(): SavedGame[] {
    return this.onlyOpen ? this.games.filter(g => (g.mistakes?.open ?? 0) > 0) : this.games;
  }

  private destroyRef = inject(DestroyRef);
  private analyzeGame = inject(AnalyzeGameService);
  private poll?: Subscription;

  constructor(
    private service: GamesService,
    private router: Router,
    private snackbar: SnackbarService,
    private translate: TranslateService,
  ) {}

  ngOnInit(): void {
    this.service.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => { this.games = list; this.loading = false; this.schedulePoll(); },
      error: () => { this.loading = false; },
    });
    this.analyzeGame.status().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(u => this.uploadStatus = u);
  }

  /** Knopf, Prozent oder nichts: eine gescheiterte Analyse zählt wie keine — der Knopf lädt zum Neuversuch. */
  analysisState(g: SavedGame): AnalysisState {
    const s = g.analysis?.status;
    if (s === 'pending' || s === 'running') return 'running';
    if (s === 'done') return 'done';
    return 'none';
  }

  progressPercent(g: SavedGame): number {
    const a = g.analysis;
    return a && a.total > 0 ? Math.round(100 * a.analyzed / a.total) : 0;
  }

  progressTip(g: SavedGame): string {
    return this.translate.instant('games.analyzing', {
      pct: this.progressPercent(g), analyzed: g.analysis?.analyzed ?? 0, total: g.analysis?.total ?? 0,
    });
  }

  /** „87 %" oder „—", wenn die Seite keinen bewertbaren Zug hatte. */
  pct(value: number | null | undefined): string {
    return value == null ? '—' : `${Math.round(value)} %`;
  }

  /** Die Partie rechnen lassen — siehe {@link AnalyzeGameService}. Das PGN braucht es dafür nicht mehr,
   *  der Server hat es; er rechnet auch nur, wenn es noch keine brauchbare Analyse gibt. Danach holt
   *  die Liste den Stand: der Knopf wird zum Fortschritt, man bleibt hier. */
  analyze(g: SavedGame): void {
    if (this.analyzingId !== null) return;
    this.analyzingId = g.id;
    this.analyzeGame.submit(this.service.analyzeUrl(g.id), this.uploadStatus)
      .subscribe(started => { this.analyzingId = null; if (started) this.refresh(); });
  }

  /** Stand aller Partien neu holen — und weiter nachfragen, solange irgendwo gerechnet wird. */
  private refresh(): void {
    this.service.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => { this.games = list; this.schedulePoll(); },
      error: () => this.schedulePoll(),
    });
  }

  /** Alle zehn Sekunden, SOLANGE eine Analyse läuft — sonst ruht die Liste (kein Dauer-Poll für nichts). */
  private schedulePoll(): void {
    this.poll?.unsubscribe();
    if (!this.games.some(g => this.analysisState(g) === 'running')) return;
    this.poll = timer(ANALYSIS_POLL_MS).pipe(
      switchMap(() => this.service.list()),
      takeUntilDestroyed(this.destroyRef),
    ).subscribe({
      next: list => { this.games = list; this.schedulePoll(); },
      error: () => this.schedulePoll(),
    });
  }

  sourceIcon(source: string): string {
    return source === 'lichess' ? 'public' : 'sports_esports';
  }

  /** PGN nachladen und in der Analyse-Seite öffnen (Übergabe via Router-State). */
  openInAnalysis(g: SavedGame): void {
    this.service.get(g.id).subscribe({
      next: detail => this.router.navigate(['/analysis'], { state: { pgn: detail.pgn }, queryParams: { from: '/games' } }),
      error: () => this.snackbar.warn(this.translate.instant('games.loadError')),
    });
  }

  /** Eindeutigen Teilen-Link in die Zwischenablage kopieren. */
  share(g: SavedGame): void {
    const url = this.service.shareUrl(g.shareToken);
    navigator.clipboard?.writeText(url).then(
      () => this.snackbar.copy(this.translate.instant('games.shareCopied')),
      () => this.snackbar.warn(url),
    );
  }

  remove(g: SavedGame): void {
    if (!confirm(this.translate.instant('games.deleteConfirm'))) return;
    this.service.delete(g.id).subscribe({
      next: () => { this.games = this.games.filter(x => x.id !== g.id); },
      error: () => this.snackbar.warn(this.translate.instant('games.deleteError')),
    });
  }
}
