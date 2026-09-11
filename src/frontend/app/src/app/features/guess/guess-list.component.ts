import { Component, OnInit, OnDestroy, ChangeDetectionStrategy, ChangeDetectorRef, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { FormsModule } from '@angular/forms';
import { Subscription, interval } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '../../core/snackbar.service';
import { GuessService, GuessSession } from './guess.service';
import { GameAnalysis, GameAnalysisService, GuessUploadStatus } from '../analysis/game-analysis.service';
import { AuthService } from '../../core/auth.service';
import { ViewStateService } from '../../core/view-state.service';

/**
 * Punktepartie-Übersicht (`/guess`): welche Partien lassen sich spielen, und welche Durchläufe gibt
 * es schon. Gespielt werden kann nur, was die Engine (mindestens teilweise) gerechnet hat — sonst
 * gäbe es nichts zu werten.
 *
 * <b>Zwei Töpfe, und sie verhalten sich verschieden.</b> Der KURATIERTE Bestand
 * (`GameAnalysis.IsPublic`) steht jedem offen, auch ohne Anmeldung, und dort wird die Seite NICHT
 * gewählt: man übernimmt die des Gewinners — das ist der Sinn der Übung, und eine Auswahl, in der
 * man sich selbst die verlorene Seite gibt, wäre nur eine Falle. Die EIGENEN Analysen (nur
 * angemeldet) behalten die Wahl: dort sind es die eigenen Partien, und da will man auch mal die
 * eigene Seite sehen.
 *
 * Der Filter „alle / nur kommentierte" arbeitet auf dem ausgelieferten `annotated`-Merkmal, also im
 * Browser — der Bestand ist eine überschaubare Bibliothek, und ein Server-Umlauf je Klick wäre für
 * ein Häkchen zu viel.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-guess-list',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule,
    MatTooltipModule, MatButtonToggleModule, MatFormFieldModule, MatInputModule, MatProgressBarModule,
    TranslatePipe, LoadingSpinnerComponent],
  template: `
    <div class="gl-container">
      <div class="header">
        <h1>{{ 'guess.title' | translate }}</h1>
        @if (loggedIn) {
          <a mat-stroked-button routerLink="/analysis/games">
            <mat-icon>insights</mat-icon> {{ 'guess.toAnalyses' | translate }}
          </a>
        }
      </div>
      <p class="muted intro">{{ 'guess.intro' | translate }}</p>
      @if (!loggedIn) {
        <p class="muted small">{{ 'guess.anonHint' | translate }}</p>
      }

      @if (loading) {
        <app-loading-spinner />
      } @else {
        <mat-card class="start-card">
          <mat-card-content>
            <div class="sec-head">
              <h2>{{ 'guess.curated' | translate }}</h2>
              @if (hasAnnotated) {
                <mat-button-toggle-group [(ngModel)]="annotatedOnly" (change)="saveFilter()"
                                         aria-label="filter" class="small-toggle">
                  <mat-button-toggle [value]="false">{{ 'guess.filterAll' | translate }}</mat-button-toggle>
                  <mat-button-toggle [value]="true">{{ 'guess.filterAnnotated' | translate }}</mat-button-toggle>
                </mat-button-toggle-group>
              }
            </div>
            <p class="muted small">{{ 'guess.curatedHint' | translate }}</p>
            @if (curatedShown.length === 0) {
              <p class="muted">{{ 'guess.noCurated' | translate }}</p>
            } @else {
              @for (g of curatedShown; track g.id) {
                <div class="game-row">
                  <span class="g-title">{{ g.title || ('guess.untitled' | translate) }}</span>
                  <span class="muted small">{{ 'guess.moves' | translate:{ moves: moveCount(g) } }}</span>
                  @if (g.annotated) {
                    <span class="chip">{{ 'guess.annotatedBadge' | translate }}</span>
                  }
                  <span class="spacer"></span>
                  <button mat-stroked-button [disabled]="starting" (click)="start(g)">
                    <mat-icon>play_arrow</mat-icon> {{ 'guess.play' | translate }}
                  </button>
                </div>
              }
            }
          </mat-card-content>
        </mat-card>

        @if (loggedIn) {
          <mat-card class="start-card">
            <mat-card-content>
              <h2>{{ 'guess.ownGames' | translate }}</h2>

              @if (uploadStatus; as u) {
                @if (u.engineAvailable) {
                  <p class="muted small">
                    {{ (u.ownEngine ? 'guess.upload.hintOwn' : 'guess.upload.hintHouse') | translate }}
                  </p>
                  <p class="muted small">{{ 'guess.upload.queueHint' | translate }}</p>
                  <p class="muted small">
                    {{ (u.ownEngine ? 'guess.upload.speedUpOwn' : 'guess.upload.speedUpHouse') | translate }}
                    <a [href]="engineGuideUrl" target="_blank" rel="noopener noreferrer">
                      {{ 'guess.upload.engineGuide' | translate }}
                    </a>
                  </p>
                  <mat-form-field appearance="outline" class="full">
                    <mat-label>{{ 'guess.upload.pgnLabel' | translate }}</mat-label>
                    <textarea matInput rows="4" [(ngModel)]="pgn" [disabled]="uploading"
                              [placeholder]="'guess.upload.placeholder' | translate"></textarea>
                  </mat-form-field>
                  <div class="upload-row">
                    <button mat-flat-button color="primary" [disabled]="uploading || !pgn.trim()" (click)="upload()">
                      <mat-icon>upload</mat-icon> {{ 'guess.upload.submit' | translate }}
                    </button>
                    <span class="muted small">
                      {{ 'guess.upload.quota' | translate:{ open: u.openGames, max: u.maxGames } }}
                    </span>
                  </div>
                } @else {
                  <p class="muted small">{{ 'guess.upload.noEngine' | translate }}</p>
                }
              }

              @if (ownGames.length === 0) {
                <p class="muted">{{ 'guess.noGames' | translate }}</p>
                <a mat-stroked-button routerLink="/analysis/games">{{ 'guess.analyseFirst' | translate }}</a>
              } @else {
                <div class="side-pick">
                  <span class="muted small">{{ 'guess.sideLabel' | translate }}</span>
                  <mat-button-toggle-group [(ngModel)]="guessWhite" aria-label="side">
                    <mat-button-toggle [value]="true">{{ 'guess.white' | translate }}</mat-button-toggle>
                    <mat-button-toggle [value]="false">{{ 'guess.black' | translate }}</mat-button-toggle>
                  </mat-button-toggle-group>
                </div>
                @for (g of ownGames; track g.id) {
                  <div class="game-row">
                    <span class="g-title">{{ g.title || ('guess.untitled' | translate) }}</span>
                    <span class="muted small">{{ 'guess.moves' | translate:{ moves: moveCount(g) } }}</span>
                    @if (g.status !== 'done' && g.status !== 'failed') {
                      <span class="muted small">{{ 'guess.analysed' | translate:{ done: g.analyzedPlies, total: g.plyCount } }}</span>
                    }
                    @if (g.status === 'failed') {
                      <span class="chip err">{{ 'guess.failedBadge' | translate }}</span>
                    } @else if (g.status === 'pending') {
                      <!-- Es wird immer nur EINE Partie je Nutzer gerechnet; die anderen warten. -->
                      <span class="chip">{{ 'guess.waiting' | translate }}</span>
                    } @else if (g.status !== 'done') {
                      <span class="chip">{{ 'guess.computing' | translate }}</span>
                    }
                    <span class="spacer"></span>
                    <button mat-stroked-button [disabled]="starting || g.analyzedPlies === 0"
                            [matTooltip]="g.analyzedPlies === 0 ? ('guess.notReady' | translate) : ''"
                            (click)="start(g, guessWhite)">
                      <mat-icon>play_arrow</mat-icon> {{ 'guess.play' | translate }}
                    </button>
                  </div>
                  @if (g.status === 'pending' || g.status === 'running') {
                    <mat-progress-bar mode="determinate" [value]="percent(g)" />
                  }
                }
              }
            </mat-card-content>
          </mat-card>
        }

        @if (sessions.length) {
          <h2 class="sec">{{ 'guess.yourRuns' | translate }}</h2>
          @for (s of sessions; track s.id) {
            <mat-card class="run">
              <mat-card-content>
                <div class="run-row">
                  <a class="g-title" [routerLink]="['/guess', s.id]">{{ s.title || ('guess.untitled' | translate) }}</a>
                  <span class="muted small">{{ (s.guessWhite ? 'guess.white' : 'guess.black') | translate }}</span>
                  <span class="pts">{{ 'guess.score' | translate:{ points: s.points, max: s.maxPoints } }}</span>
                  <span class="spacer"></span>
                  <span class="chip">{{ ('guess.status.' + s.status) | translate }}</span>
                  <button mat-icon-button [attr.aria-label]="'common.delete' | translate"
                          [matTooltip]="'common.delete' | translate" (click)="remove(s)">
                    <mat-icon>delete</mat-icon>
                  </button>
                </div>
              </mat-card-content>
            </mat-card>
          }
        }
      }
    </div>
  `,
  styles: [`
    .gl-container { max-width: min(var(--page-max-width), 96vw); margin: 16px auto; padding: 0 12px; }
    .header { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
    h1 { margin: 0; font-size: 1.5rem; }
    h2 { margin: 0 0 8px; font-size: 1.05rem; }
    h2.sec { margin: 18px 0 8px; }
    .intro { margin: 4px 0 14px; }
    .start-card { margin-bottom: 8px; }
    .sec-head { display: flex; align-items: center; justify-content: space-between; gap: 10px; flex-wrap: wrap; }
    .small-toggle { font-size: .8rem; }
    .side-pick { display: flex; align-items: center; gap: 10px; margin-bottom: 10px; flex-wrap: wrap; }
    .full { width: 100%; }
    .upload-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .chip.err { color: #ef9a9a; }
    mat-progress-bar { margin: 0 0 8px; border-radius: 3px; }
    .game-row, .run-row { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; padding: 6px 0; }
    .game-row + .game-row { border-top: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .g-title { font-weight: 600; text-decoration: none; color: inherit; }
    a.g-title:hover { text-decoration: underline; }
    .spacer { flex: 1 1 auto; }
    .pts { font-variant-numeric: tabular-nums; }
    .chip { font-size: .72rem; padding: 2px 8px; border-radius: 10px; border: 1px solid currentColor;
            color: color-mix(in srgb, currentColor 65%, transparent); }
    .run { margin-bottom: 8px; }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: .8rem; }
  `],
})
export class GuessListComponent implements OnInit, OnDestroy {
  private guess = inject(GuessService);
  private analyses = inject(GameAnalysisService);
  private auth = inject(AuthService);
  private viewState = inject(ViewStateService);
  private router = inject(Router);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);

  sessions: GuessSession[] = [];
  /** Kuratierter Bestand — auch ohne Anmeldung. */
  curated: GameAnalysis[] = [];
  /** Eigene Analysen (nur angemeldet) — auch die noch rechnenden: wer gerade eine Partie
   *  eingeworfen hat, will sehen, dass sie da ist und wie weit sie ist. */
  ownGames: GameAnalysis[] = [];
  loading = true;
  starting = false;
  guessWhite = true;
  /**
   * Anleitung, die eigene Maschine als Engine anzuschliessen — Docker und Windows, beides im
   * README des Engine-Providers.
   *
   * <p>Bewusst ein Link nach draussen und keine eigene Hilfeseite: die Anleitung gehoert zum
   * Provider und aendert sich MIT ihm (Variablen, Stockfish-Version, die Windows-Zombie-Falle).
   * Eine Zweitfassung in der App waere ab dem naechsten Provider-Umbau falsch, und falsch ist
   * schlimmer als anderswo.</p>
   */
  readonly engineGuideUrl = 'https://github.com/kahalm/rookhub/blob/master/engine-provider/README.md';

  /** Eingeworfenes PGN. */
  pgn = '';
  uploading = false;
  /** Ob ueberhaupt eingeworfen werden darf (und wie oft noch); null = noch nicht geladen. */
  uploadStatus: GuessUploadStatus | null = null;
  private poll?: Subscription;
  /**
   * Filter des Bestands: nur Partien mit Kommentaren zeigen.
   *
   * Der Zustand haengt am NUTZER, nicht am Geraet — wer am Rechner „nur kommentierte" eingestellt
   * hat, will das am Handy auch so vorfinden (`UserViewStates`, derselbe Weg wie die Filterleiste
   * des Turnierkalenders). Ohne Anmeldung gibt es kein Konto, dort traegt der `localStorage`.
   */
  private static readonly ViewKey = 'guess.list';
  private static readonly LocalKey = 'rookhub_guess_list_filter';
  annotatedOnly = false;

  get loggedIn(): boolean { return this.auth.isLoggedIn; }

  /** Der Filter wird nur angeboten, wenn es überhaupt etwas zu filtern gibt — ein Umschalter, der
   *  beide Male dieselbe Liste zeigt, verwirrt mehr als er hilft. */
  get hasAnnotated(): boolean { return this.curated.some(g => g.annotated); }

  get curatedShown(): GameAnalysis[] {
    return this.annotatedOnly ? this.curated.filter(g => g.annotated) : this.curated;
  }

  ngOnInit(): void {
    this.loadFilter();
    this.analyses.listPublic().subscribe({
      next: list => { this.curated = list; this.cdr.markForCheck(); },
      error: () => { /* bleibt leer; der Hinweis „noch nichts im Bestand" greift */ },
    });
    if (this.loggedIn) {
      this.loadOwnGames();
      this.analyses.guessUploadStatus().subscribe({
        next: u => { this.uploadStatus = u; this.cdr.markForCheck(); },
        error: () => { /* ohne Antwort bleibt das Feld aus — lieber gar nicht anbieten als ins Leere */ },
      });
    }
    this.guess.list().subscribe({
      next: rows => { this.sessions = rows; this.loading = false; this.cdr.markForCheck(); },
      error: () => { this.loading = false; this.cdr.markForCheck(); },
    });
  }

  ngOnDestroy(): void { this.poll?.unsubscribe(); }

  /**
   * Die eigenen Analysen — anders als der kuratierte Bestand AUCH die noch rechnenden. Frueher
   * standen hier nur Partien mit mindestens einer gerechneten Stellung; seit man auf dieser Seite
   * selbst eine einwerfen kann, waere das ein Loch: die eingeworfene Partie verschwaende fuer
   * Minuten spurlos, und man wuerde sie ein zweites Mal einwerfen.
   *
   * <p>Der kuratierte Bestand steht oben schon, hier ginge er sonst doppelt durch.</p>
   */
  private loadOwnGames(): void {
    this.analyses.list().subscribe({
      next: list => {
        this.ownGames = list.filter(a => !a.isPublic);
        this.syncPoll();
        this.cdr.markForCheck();
      },
      error: () => { /* die Liste bleibt leer; der Hinweis „erst analysieren" greift */ },
    });
  }

  /** Nachfassen, SOLANGE etwas rechnet — eine fertige Liste erzeugt keinen Verkehr (dasselbe
   *  Muster wie auf der Auftragsseite). */
  private syncPoll(): void {
    const busy = this.ownGames.some(g => g.status === 'pending' || g.status === 'running');
    if (busy && !this.poll) {
      this.poll = interval(10000).subscribe(() => this.loadOwnGames());
    } else if (!busy && this.poll) {
      this.poll.unsubscribe();
      this.poll = undefined;
    }
  }

  /** Zuege statt Halbzuege — „42 Zuege" ist die Zahl, die ein Schachspieler erwartet. */
  moveCount(g: GameAnalysis): number {
    return Math.ceil(g.plyCount / 2);
  }

  percent(g: GameAnalysis): number {
    return g.plyCount > 0 ? Math.round((g.analyzedPlies / g.plyCount) * 100) : 0;
  }

  /**
   * Eine eigene Partie einwerfen. Ohne Tiefe und ohne Linienzahl — beides setzt der Server
   * (Tiefe 20). Ein Regler haette hier nichts zu suchen: gerechnet wird meist auf der Engine des
   * Hauses, und die Punktepartie wertet ohnehin gegen den tatsaechlich gespielten Zug.
   *
   * <p>Die Absage kommt als GRUND und nicht als Satz (`too-many-open`/`no-engine`/`invalid-pgn`) —
   * der Server kennt die Sprache des Nutzers nicht, die Seite schon.</p>
   */
  upload(): void {
    const pgn = this.pgn.trim();
    if (!pgn || this.uploading) return;
    this.uploading = true;
    this.analyses.createForGuess(pgn).subscribe({
      next: () => {
        this.uploading = false;
        this.pgn = '';
        this.snackbar.success(this.translate.instant('guess.upload.started'));
        this.loadOwnGames();
        this.analyses.guessUploadStatus().subscribe(u => { this.uploadStatus = u; this.cdr.markForCheck(); });
        this.cdr.markForCheck();
      },
      error: err => {
        this.uploading = false;
        const reason = err?.error?.reason;
        this.snackbar.warn(reason
          ? this.translate.instant('guess.upload.reason.' + reason, { max: this.uploadStatus?.maxGames })
          : this.translate.instant('guess.upload.failed'));
        this.cdr.markForCheck();
      },
    });
  }

  /** Angemeldet vom Server, sonst aus dem Geraetespeicher. Fehler sind still: der Filter ist eine
   *  Bequemlichkeit, und eine Meldung „dein Filter konnte nicht geladen werden" klickt man weg. */
  private loadFilter(): void {
    if (this.loggedIn) {
      this.viewState.get<{ annotatedOnly?: boolean }>(GuessListComponent.ViewKey).subscribe(v => {
        if (v) { this.annotatedOnly = !!v.annotatedOnly; this.cdr.markForCheck(); }
      });
      return;
    }
    try {
      this.annotatedOnly = localStorage.getItem(GuessListComponent.LocalKey) === '1';
    } catch { /* Vorgabe behalten */ }
  }

  /** Vom Umschalter aufgerufen. */
  saveFilter(): void {
    if (this.loggedIn) {
      this.viewState.save(GuessListComponent.ViewKey, { annotatedOnly: this.annotatedOnly }).subscribe();
      return;
    }
    try {
      localStorage.setItem(GuessListComponent.LocalKey, this.annotatedOnly ? '1' : '0');
    } catch { /* nicht speicherbar: gilt fuer diesen Aufruf trotzdem */ }
  }

  /** `side` weglassen = der Server nimmt die Seite des Gewinners (kuratierter Bestand). */
  start(game: GameAnalysis, side?: boolean): void {
    if (this.starting) return;
    this.starting = true;
    this.guess.start(game.id, side).subscribe({
      next: s => { this.starting = false; this.router.navigate(['/guess', s.id]); },
      error: err => {
        this.starting = false;
        this.snackbar.warn(err?.error?.message || this.translate.instant('guess.startFailed'));
        this.cdr.markForCheck();
      },
    });
  }

  remove(s: GuessSession): void {
    this.guess.delete(s.id).subscribe({
      next: () => { this.sessions = this.sessions.filter(x => x.id !== s.id); this.cdr.markForCheck(); },
      error: () => this.snackbar.warn(this.translate.instant('guess.deleteFailed')),
    });
  }
}
