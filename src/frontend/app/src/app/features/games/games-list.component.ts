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
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { GamesService, SavedGame } from './games.service';
import { formatTimeControl, TimeControlLabel } from './time-control.util';
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
    MatProgressSpinnerModule, MatCheckboxModule, MatMenuModule, TranslatePipe,
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
        <!-- Tabelle im Schnitt der chess.com-Uebersicht (gewuenscht 24.09.2026): Spieler, Ergebnis,
             Genauigkeit, Zuege, Datum — genau die Spalten, die der Nutzer dort vor sich hat. Die
             Kopfzeile ist am Handy weg, dort steht dieselbe Zeile umbrochen. -->
        <div class="table">
          <div class="row head-row">
            <span></span>
            <span>{{ 'games.col.players' | translate }}</span>
            <span class="mid">{{ 'games.col.result' | translate }}</span>
            <span>{{ 'games.col.accuracy' | translate }}</span>
            <span class="mid">{{ 'games.col.moves' | translate }}</span>
            <span>{{ 'games.col.date' | translate }}</span>
            <span></span>
          </div>
          @for (g of shownGames(); track g.id) {
            <div class="row game">
              <div class="kind">
                <mat-icon class="src" [matTooltip]="g.source">{{ sourceIcon(g.source) }}</mat-icon>
                @if (timeControl(g); as tc) { <span class="tc">{{ 'games.tc.' + tc.key | translate: tc.params }}</span> }
              </div>
              <!-- Die Namen führen auf die Partie-SEITE (/games/:id) — kein Dialog mehr (gemeldet 2026-09-23). -->
              <a class="players" [routerLink]="['/games', g.id]">
                <span class="p"><i class="dot white"></i><span class="name">{{ g.white || '?' }}</span>@if (g.whiteElo) { <span class="elo">({{ g.whiteElo }})</span> }</span>
                <span class="p"><i class="dot black"></i><span class="name">{{ g.black || '?' }}</span>@if (g.blackElo) { <span class="elo">({{ g.blackElo }})</span> }</span>
              </a>
              <!-- Punkte wie auf chess.com untereinander, die Gewinnerseite hervorgehoben. -->
              <div class="score">
                <span [class.win]="score(g).white === '1'">{{ score(g).white }}</span>
                <span [class.win]="score(g).black === '1'">{{ score(g).black }}</span>
              </div>
              <div class="acc-cell">
                @switch (analysisState(g)) {
                  @case ('done') {
                    <span class="accuracy" [matTooltip]="'games.accuracyHint' | translate">
                      <span>♔ {{ pct(g.analysis?.accuracyWhite) }}</span>
                      <span>♚ {{ pct(g.analysis?.accuracyBlack) }}</span>
                    </span>
                  }
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
              </div>
              <div class="moves mid">{{ g.moveCount }}</div>
              <div class="date">{{ (g.playedAt || g.createdAt) | date:'mediumDate' }}</div>
              <div class="actions">
                <a mat-icon-button [routerLink]="['/games', g.id]" [matTooltip]="'games.replay' | translate" [attr.aria-label]="'games.replay' | translate">
                  <mat-icon>play_arrow</mat-icon>
                </a>
                <!-- Alles Weitere ins ⋮: eine Zeile mit sechs Knöpfen liest sich nicht mehr. -->
                <button mat-icon-button [matMenuTriggerFor]="menu"
                        [matTooltip]="'games.moreActions' | translate" [attr.aria-label]="'games.moreActions' | translate">
                  <mat-icon>more_vert</mat-icon>
                </button>
                <mat-menu #menu="matMenu">
                  <button mat-menu-item (click)="openInAnalysis(g)">
                    <mat-icon>biotech</mat-icon><span>{{ 'games.openInAnalysis' | translate }}</span>
                  </button>
                  <button mat-menu-item (click)="share(g)">
                    <mat-icon>share</mat-icon><span>{{ 'games.share' | translate }}</span>
                  </button>
                  @if (g.sourceUrl) {
                    <a mat-menu-item [href]="g.sourceUrl" target="_blank" rel="noopener">
                      <mat-icon>open_in_new</mat-icon><span>{{ 'games.openOriginal' | translate }}</span>
                    </a>
                  }
                  <button mat-menu-item (click)="remove(g)">
                    <mat-icon color="warn">delete</mat-icon><span>{{ 'common.delete' | translate }}</span>
                  </button>
                </mat-menu>
              </div>
              <!-- Fußzeile der Zeile wie chess.coms Eröffnungszeile: hier der Stand des Fehler-Trainings
                   (0.524.0) — die Zeile, wegen der man die Partie noch einmal aufmacht. -->
              @if (g.mistakes; as m) {
                <div class="foot">
                  <!-- Nur DASS nachgespielt wurde (gewünscht 2026-09-24); die Zahlen „9 von 11 gefunden · 2 offen" stehen
                       im Tooltip. -->
                  <span class="mistakes" [class.open]="m.open > 0"
                        [matTooltip]="'games.mistakes.progressShort' | translate: { solved: m.solved, total: m.total, open: m.open }">
                    <mat-icon class="mistakes-icon">task_alt</mat-icon> {{ 'games.mistakes.replayed' | translate }}
                  </span>
                </div>
              }
            </div>
          }
        </div>
      }
    </div>
  `,
  styles: [`
    .games-page { max-width: 1040px; margin: 0 auto; padding: 16px; }
    .head h1 { margin: 0 0 4px; }
    .hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0 0 16px; font-size: 0.9rem; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .only-open { margin-top: 4px; }

    /* Eine Zeile = ein Raster; die Kopfzeile benutzt dasselbe, damit die Spalten stehen. */
    .table { display: flex; flex-direction: column; }
    .row {
      display: grid;
      /* Die Ergebnis-Spalte traegt zwei Zeichen, ihre Beschriftung aber acht („Ergebnis", „Rezultat",
         „Eredmény"): unter 72px lief die Kopfzeile in die naechste hinein bzw. wurde abgeschnitten
         (im Bild nachgemessen). */
      grid-template-columns: 78px minmax(0, 1fr) 72px 84px 48px 104px 88px;
      align-items: center;
      gap: 8px;
      padding: 6px 8px;
      border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent);
    }
    .head-row {
      font-size: 0.7rem; text-transform: uppercase; letter-spacing: 0.03em;
      color: color-mix(in srgb, currentColor 55%, transparent);
      border-bottom-width: 2px; padding-bottom: 4px;
    }
    /* Notnagel fuer laengere Sprachen: lieber abschneiden als in die Nachbarspalte laufen. */
    .head-row span { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .game:hover { background: color-mix(in srgb, currentColor 5%, transparent); }
    .mid { text-align: center; }

    .kind { display: flex; align-items: center; gap: 4px; min-width: 0; }
    .src { flex-shrink: 0; opacity: 0.7; }
    .tc { font-size: 0.8rem; white-space: nowrap; color: color-mix(in srgb, currentColor 70%, transparent); }

    .players { display: flex; flex-direction: column; min-width: 0; color: inherit; text-decoration: none; }
    .players:hover .name { text-decoration: underline; }
    .p { display: flex; align-items: center; gap: 6px; min-width: 0; line-height: 1.45; }
    .name { overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
    .elo { font-size: 0.8rem; color: color-mix(in srgb, currentColor 55%, transparent); }
    /* Farbmarke wie in chess.coms Zeile — ein Rahmen, damit Weiss auf hellem Grund sichtbar bleibt. */
    .dot { width: 9px; height: 9px; flex-shrink: 0; border: 1px solid color-mix(in srgb, currentColor 45%, transparent); }
    .dot.white { background: #fff; }
    .dot.black { background: #333; }

    .score { display: flex; flex-direction: column; text-align: center; font-variant-numeric: tabular-nums; }
    .score span { line-height: 1.45; color: color-mix(in srgb, currentColor 55%, transparent); }
    .score .win { color: inherit; font-weight: 700; }

    .acc-cell { display: flex; align-items: center; justify-content: center; }
    .accuracy {
      display: flex; flex-direction: column; text-align: right; white-space: nowrap;
      font-size: 0.85rem; font-variant-numeric: tabular-nums;
      color: color-mix(in srgb, currentColor 80%, transparent);
    }
    .accuracy span { line-height: 1.45; }
    /* So breit wie ein Icon-Knopf, damit die Zeile beim Wechsel Knopf → Prozent nicht springt. */
    .progress {
      display: inline-flex; align-items: center; justify-content: center; width: 40px; height: 40px;
      font-size: 0.8rem; font-variant-numeric: tabular-nums; color: #1976d2; cursor: default;
    }
    .moves { font-variant-numeric: tabular-nums; color: color-mix(in srgb, currentColor 70%, transparent); }
    .date { font-size: 0.85rem; white-space: nowrap; color: color-mix(in srgb, currentColor 70%, transparent); }
    .actions { display: flex; align-items: center; justify-content: flex-end; }

    /* Fusszeile der Zeile, an den Spielernamen ausgerichtet (chess.com setzt dort die Eroeffnung hin). */
    .foot { grid-column: 2 / -1; font-size: 0.8rem; }
    .mistakes { white-space: nowrap; color: color-mix(in srgb, currentColor 60%, transparent); }
    .mistakes { display: inline-flex; align-items: center; gap: 3px; }
    /* Grün wie die Zug-Klasse „best“ (MOVE_CLASS_COLORS) — das Häkchen sagt „erledigt“, der graue Text daneben bleibt ruhig. */
    .mistakes-icon { font-size: 15px; width: 15px; height: 15px; color: #96bc4b; }

    /* Am Handy gibt es keine Spalten mehr: dieselben Teile umbrechen, Zahl und Datum rutschen
       in die zweite Reihe. Die Kopfzeile faellt weg — sie beschriftete Spalten, die es nicht gibt. */
    @media (max-width: 760px) {
      .row { display: flex; flex-wrap: wrap; gap: 6px 10px; padding: 10px 8px; }
      /* MIT .row davor: beide Regeln haetten sonst dieselbe Spezifitaet, und die spaetere gewinnt —
         die Kopfzeile stand dann als Wortreihe ueber der Liste (im Bild nachgemessen). */
      .row.head-row { display: none; }
      .players { flex: 1 1 55%; }
      .acc-cell { margin-left: auto; }
      .accuracy { flex-direction: row; gap: 8px; text-align: left; }
      .actions { order: 9; margin-left: auto; }
      /* Die Fussnote ganz nach hinten, sonst draengt sie die Knoepfe in eine eigene leere Zeile. */
      .foot { order: 10; flex-basis: 100%; }
      .moves, .date, .foot { font-size: 0.8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
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

  /** Die beiden Punkte untereinander, wie in chess.coms Ergebnis-Spalte. Offen/unbekannt = leer. */
  score(g: SavedGame): { white: string; black: string } {
    switch (g.result) {
      case '1-0': return { white: '1', black: '0' };
      case '0-1': return { white: '0', black: '1' };
      case '1/2-1/2': return { white: '½', black: '½' };
      default: return { white: '', black: '' };
    }
  }

  /** Bedenkzeit als Schlüssel + Zahlen („3 + 2"); `null` = keine bekannt, dann steht dort nichts. */
  timeControl(g: SavedGame): TimeControlLabel | null {
    return formatTimeControl(g.timeControl);
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
