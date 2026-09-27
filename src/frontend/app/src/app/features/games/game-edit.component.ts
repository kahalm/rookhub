import { ElementRef, ChangeDetectionStrategy, Component, DestroyRef, HostListener, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSelectModule } from '@angular/material/select';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { of } from 'rxjs';
import { Chess } from 'chess.js';
import { catchError } from 'rxjs/operators';
import { ChessBoardComponent, BoardArrow, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GamesService, SavedGameDetail } from './games.service';
import { ScoresheetPhotoDialogComponent } from './scoresheet-photo-dialog.component';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ScoresheetOption, ScoresheetService, openPhotoBlob } from './scoresheet.service';
import {
  EditPly, commentsForSave, cropView, fensOf, fromServer, nextUncertainFrom, headersOf, isoDateOf, pliesOfPgn, resolveRequest, revalidate,
  stripSheetNotes, toServer, userPly, writtenIndexAt,
} from './game-edit.util';

/** Eine Zeile der Zugliste: Zugnummer + Index des weißen und des schwarzen Halbzugs. */
interface MoveRow { no: number; white: number; black: number | null; }

/**
 * Partie korrigieren (`/games/:id/edit`, 0.529.0). Für jede eigene Partie: Züge und Kopfdaten. Bei einer aus
 * einem Formular-Foto eingelesenen Partie zusätzlich das Foto daneben, was auf dem Formular stand, die
 * unsicheren Stellen mit ihren wahrscheinlichsten Lesarten — und nach jeder Änderung wird der REST aus den
 * Formular-Einträgen neu aufbereitet (Server, ohne Modell-Aufruf), statt ihn einfach stehen zu lassen.
 *
 * <para>Das Brett zeigt immer die Stellung VOR dem gewählten Halbzug (dem „Cursor"): wer dort einen Zug spielt,
 * ersetzt ihn (oder fügt davor ein). Der bisherige Zug steht als gelber Pfeil darauf.</para>
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-game-edit',
  standalone: true,
  imports: [
    CommonModule, FormsModule, RouterLink, MatButtonModule, MatButtonToggleModule, MatCardModule, MatFormFieldModule,
    MatIconModule, MatInputModule, MatProgressBarModule, MatProgressSpinnerModule, MatSelectModule, MatTooltipModule,
    MatDialogModule, TranslatePipe, ChessBoardComponent, HelpHintComponent,
  ],
  template: `
    <div class="edit-page">
      @if (loading()) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (notFound()) {
        <mat-card class="empty"><mat-icon>link_off</mat-icon><p>{{ 'games.edit.notFound' | translate }}</p></mat-card>
      } @else {
        <div class="head">
          <a mat-icon-button [routerLink]="['/games', gameId]" [matTooltip]="'common.back' | translate" [attr.aria-label]="'common.back' | translate">
            <mat-icon>arrow_back</mat-icon>
          </a>
          <h1>{{ 'games.edit.title' | translate }}</h1>
          <app-help-hint [text]="(isScoresheet() ? 'games.edit.helpScoresheet' : 'games.edit.help') | translate" />
          <span class="spacer"></span>
          <button mat-flat-button color="primary" (click)="save()" [disabled]="saving() || busy()">
            <mat-icon>{{ saving() ? 'hourglass_top' : 'save' }}</mat-icon> {{ 'common.save' | translate }}
          </button>
        </div>

        <mat-card class="headers">
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.edit.white' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.white" name="white" maxlength="120" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.edit.black' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.black" name="black" maxlength="120" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="short"><mat-label>{{ 'games.edit.result' | translate }}</mat-label>
            <mat-select [(ngModel)]="header.result" name="result" (ngModelChange)="dirty.set(true)">
              @for (r of results; track r) { <mat-option [value]="r">{{ r }}</mat-option> }
            </mat-select></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.edit.event' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.event" name="event" maxlength="200" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.edit.site' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.site" name="site" maxlength="200" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="short"><mat-label>{{ 'games.edit.round' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.round" name="round" maxlength="40" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="short"><mat-label>{{ 'games.edit.date' | translate }}</mat-label>
            <input matInput type="date" [(ngModel)]="header.date" name="date" (ngModelChange)="dirty.set(true)" /></mat-form-field>
          <!-- Meine Seite: dreht Partieseite, Teilen-Link und Vorschaubild — und hier gleich das Brett. -->
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="short"><mat-label>{{ 'games.edit.ownerSide' | translate }}</mat-label>
            <mat-select [(ngModel)]="header.ownerSide" name="ownerSide" (ngModelChange)="onSide($event)">
              <mat-option value="">{{ 'games.edit.sideNone' | translate }}</mat-option>
              <mat-option value="white">{{ 'scoresheet.sideWhite' | translate }}</mat-option>
              <mat-option value="black">{{ 'scoresheet.sideBlack' | translate }}</mat-option>
            </mat-select></mat-form-field>
        </mat-card>

        <div class="layout" [class.with-photo]="!!photoUrl()">
          @if (photoUrl(); as src) {
            <mat-card class="photo">
              <div class="photo-bar">
                <strong>{{ 'games.edit.photo' | translate }}</strong>
                <span class="spacer"></span>
                <button mat-icon-button (click)="zoom.set(!zoom())" [matTooltip]="(zoom() ? 'games.edit.zoomOut' : 'games.edit.zoomIn') | translate">
                  <mat-icon>{{ zoom() ? 'zoom_out' : 'zoom_in' }}</mat-icon>
                </button>
                <button mat-icon-button (click)="openPhoto(false)" [matTooltip]="'games.photo.show' | translate"><mat-icon>open_in_full</mat-icon></button>
                <button mat-icon-button (click)="openPhoto(true)" [matTooltip]="'games.photo.download' | translate"><mat-icon>download</mat-icon></button>
              </div>
              <div class="photo-scroll" [class.zoom]="zoom()">
                <img [src]="src" [alt]="'games.edit.photo' | translate" (load)="onPhotoLoad($event)" />
              </div>
              @if (crop(); as c) {
                <div class="crop">
                  <div class="crop-label">
                    {{ 'games.edit.cropTitle' | translate }}
                    @if (c.written) { <span class="written">{{ 'games.edit.written' | translate: { text: c.written } }}</span> }
                  </div>
                  <div class="crop-frame" [class.uncertain]="c.uncertain" [style.aspect-ratio]="c.view.aspect" [style.--crop-aspect]="c.view.aspect">
                    <img [src]="src" alt="" [style.width.%]="c.view.imgW" [style.height.%]="c.view.imgH"
                         [style.left.%]="c.view.left" [style.top.%]="c.view.top" />
                    <div class="crop-mark" [style.left.%]="c.view.markLeft" [style.top.%]="c.view.markTop"
                         [style.width.%]="c.view.markW" [style.height.%]="c.view.markH"></div>
                  </div>
                </div>
              }
            </mat-card>
          }

          <mat-card class="board-card">
            <div class="board-wrap">
              <app-chess-board [fen]="cursorFen()" [lastMove]="lastMove()" [arrows]="arrows()" [flipped]="flipped()"
                               [playable]="!busy()" (userMove)="onBoardMove($event)"
                               [boardTheme]="preferences.boardTheme" [pieceSet]="preferences.pieceSet" />
            </div>
            <div class="nav">
              <button mat-icon-button (click)="go(0)" [disabled]="cursor() === 0"><mat-icon>skip_previous</mat-icon></button>
              <button mat-icon-button (click)="go(cursor() - 1)" [disabled]="cursor() === 0"><mat-icon>navigate_before</mat-icon></button>
              <button mat-icon-button (click)="go(cursor() + 1)" [disabled]="cursor() >= legalCount()"><mat-icon>navigate_next</mat-icon></button>
              <button mat-icon-button (click)="go(legalCount())" [disabled]="cursor() >= legalCount()"><mat-icon>skip_next</mat-icon></button>
              <button mat-icon-button (click)="flipped.set(!flipped())"><mat-icon>swap_vert</mat-icon></button>
              @if (uncertainLeft() > 0) {
                <button mat-stroked-button class="next-uncertain" (click)="nextUncertain()">
                  <mat-icon>priority_high</mat-icon> {{ 'games.edit.nextUncertain' | translate: { count: uncertainLeft() } }}
                </button>
              }
            </div>
            <mat-button-toggle-group class="mode" [value]="mode()" (change)="mode.set($event.value)" hideSingleSelectionIndicator>
              <mat-button-toggle value="replace">{{ 'games.edit.modeReplace' | translate }}</mat-button-toggle>
              <mat-button-toggle value="insert">{{ 'games.edit.modeInsert' | translate }}</mat-button-toggle>
            </mat-button-toggle-group>

          </mat-card>

          <!-- Rechte Spalte: der gewählte Zug (Lesarten, Aktionen, Kommentar) über der Zugliste. Am PC füllt der
               Arbeitsbereich genau die Fensterhöhe, jede Spalte scrollt für sich (0.554.1). -->
          <div class="side">
          <mat-card class="cursor-card">
            <div class="cursor-panel">
              @if (busy()) { <mat-progress-bar mode="indeterminate" /> }
              @if (current(); as p) {
                <div class="where">
                  <strong>{{ plyLabel(cursor()) }}</strong>
                  <span class="san" [class.bad]="p.illegal">{{ p.san }}</span>
                  @if (p.written) { <span class="written">{{ 'games.edit.written' | translate: { text: p.written } }}</span> }
                  @if (p.match === 'inserted') { <span class="chip warn">{{ 'games.edit.notOnSheet' | translate }}</span> }
                  @if (p.uncertain && !p.confirmed) { <span class="chip warn">{{ 'games.edit.uncertain' | translate }}</span> }
                  @if (p.confirmed) { <span class="chip ok">{{ 'games.edit.confirmed' | translate }}</span> }
                  @if (p.illegal) { <span class="chip bad">{{ 'games.edit.illegal' | translate }}</span> }
                </div>
                @if (p.options?.length && !p.illegal) {
                  <div class="options">
                    <span class="label">{{ 'games.edit.options' | translate }}</span>
                    @for (o of p.options; track o.uci) {
                      <button mat-stroked-button class="option" [class.chosen]="o.uci === p.uci" (click)="choose(o)" [disabled]="busy()">
                        <span class="o-san">{{ o.san }}</span>
                        <span class="o-reach">{{ 'games.edit.reach' | translate: { count: o.reach } }}</span>
                        @if (o.preview.length) { <span class="o-preview">→ {{ o.preview.join(' ') }}</span> }
                      </button>
                    }
                  </div>
                }
                <div class="ply-actions">
                  @if (!p.illegal && !p.confirmed && (p.uncertain || isScoresheet())) {
                    <button mat-stroked-button (click)="confirm()"><mat-icon>check</mat-icon> {{ 'games.edit.confirm' | translate }}</button>
                  }
                  <button mat-stroked-button (click)="remove()" [disabled]="busy()"><mat-icon>backspace</mat-icon> {{ 'games.edit.delete' | translate }}</button>
                </div>
                <mat-form-field appearance="outline" subscriptSizing="dynamic" class="comment">
                  <mat-label>{{ 'games.edit.comment' | translate }}</mat-label>
                  <textarea matInput rows="2" maxlength="2000" [ngModel]="p.comment ?? ''" (ngModelChange)="setComment($event)"></textarea>
                </mat-form-field>
                <p class="tip">{{ (mode() === 'insert' ? 'games.edit.tipInsert' : 'games.edit.tipReplace') | translate }}</p>
              } @else {
                <p class="tip">{{ 'games.edit.tipAppend' | translate }}</p>
                @if (unresolved().length) {
                  <p class="tip">{{ 'games.edit.nextWritten' | translate: { text: unresolved()[0] } }}</p>
                }
              }
            </div>
          </mat-card>

          <mat-card class="moves-card">
            <div class="moves">
              @for (row of rows(); track row.no) {
                <span class="no">{{ row.no }}.</span>
                <button class="ply" [ngClass]="plyClass(row.white)" (click)="go(row.white)">{{ plies()[row.white].san }}</button>
                @if (row.black !== null) {
                  <button class="ply" [ngClass]="plyClass(row.black)" (click)="go(row.black)">{{ plies()[row.black].san }}</button>
                } @else { <span></span> }
              }
              <span class="no"></span>
              <button class="ply end" [class.cursor]="cursor() === plies().length" (click)="go(legalCount())">{{ 'games.edit.end' | translate }}</button>
            </div>
            @if (unresolved().length) {
              <div class="unresolved">
                <strong>{{ 'games.edit.unresolvedTitle' | translate: { count: unresolved().length } }}</strong>
                <span>{{ unresolved().join(' ') }}</span>
              </div>
            }
            @if (illegalCount() > 0) {
              <p class="warn-text">{{ 'games.edit.illegalHint' | translate: { count: illegalCount() } }}</p>
            }
          </mat-card>
          </div>
        </div>
      }
    </div>
  `,
  styles: [`
    /* Werkbank-Seite: am PC mehr Breite als die üblichen 1240 px (Foto + Brett + Zugliste nebeneinander). */
    .edit-page { max-width: min(1800px, 98vw); margin: 0 auto; padding: 12px 16px; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; }
    .head { display: flex; align-items: center; gap: 4px; margin-bottom: 8px; }
    .head h1 { margin: 0; font-size: 1.4rem; }
    .spacer { flex: 1; }
    /* Kopfdaten: kompakt, am PC in EINER Zeile (vorher zwei, die den Arbeitsbereich unter den Rand schoben). */
    .headers { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 8px 12px; padding: 10px 12px;
      margin-bottom: 12px; --mat-form-field-container-height: 44px; --mat-form-field-container-vertical-padding: 10px; }
    .layout { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 12px; align-items: start; }
    .layout.with-photo { grid-template-columns: minmax(0, 1.15fr) minmax(0, 1fr) minmax(0, 0.8fr); }
    .side { display: flex; flex-direction: column; gap: 12px; min-width: 0; }
    @media (max-width: 1100px) { .layout.with-photo { grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); } .photo { grid-column: 1 / -1; } }
    @media (max-width: 720px) { .layout, .layout.with-photo { grid-template-columns: minmax(0, 1fr); } }
    .photo { padding: 8px; }
    .photo-bar { display: flex; align-items: center; gap: 4px; padding: 0 4px 4px; }
    .photo-scroll { max-height: 72vh; overflow: auto; text-align: center; }
    .photo-scroll img { max-width: 100%; max-height: 70vh; object-fit: contain; }
    .photo-scroll.zoom img { max-width: none; max-height: none; width: 200%; }
    .crop { margin-top: 8px; padding-top: 8px; border-top: 1px solid color-mix(in srgb, currentColor 15%, transparent); }
    .crop-label { display: flex; gap: 8px; align-items: baseline; flex-wrap: wrap; padding: 0 4px 6px; font-size: 0.9rem; }
    .crop-frame { position: relative; overflow: hidden; width: 100%; max-width: 560px; margin: 0 auto;
      border-radius: 4px; outline: 2px solid color-mix(in srgb, currentColor 20%, transparent); }
    .crop-frame.uncertain { outline-color: var(--mat-sys-error, #c62828); }
    .crop-frame img { position: absolute; max-width: none; max-height: none; }
    .crop-mark { position: absolute; border: 2px solid var(--mat-sys-error, #c62828); border-radius: 3px; pointer-events: none;
      box-shadow: 0 0 0 9999px rgba(0, 0, 0, 0.12); }
    .board-card { padding: 12px; display: flex; flex-direction: column; gap: 8px; }
    .board-wrap { width: min(100%, 62vh); align-self: center; }
    .nav { display: flex; flex-wrap: wrap; align-items: center; justify-content: center; gap: 2px; }
    .next-uncertain { margin-left: 8px; }
    .mode { align-self: center; }
    .cursor-card { padding: 12px; }
    .cursor-panel { display: flex; flex-direction: column; gap: 8px; }
    .where { display: flex; flex-wrap: wrap; align-items: center; gap: 8px; }
    .san { font-family: monospace; font-size: 1.1rem; }
    .san.bad { text-decoration: line-through; color: var(--mat-sys-error, #c62828); }
    .written { font-size: 0.9rem; color: color-mix(in srgb, currentColor 70%, transparent); }
    .chip { font-size: 0.75rem; padding: 1px 8px; border-radius: 10px; border: 1px solid currentColor; }
    .chip.warn, .warn-text { color: #ef6c00; }
    .chip.ok { color: #2e7d32; }
    .chip.bad { color: var(--mat-sys-error, #c62828); }
    .options { display: flex; flex-direction: column; gap: 6px; }
    .options .label { font-size: 0.85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .option { justify-content: flex-start; text-align: left; height: auto; padding: 6px 12px; line-height: 1.3; }
    .option.chosen { border-color: #ef6c00; border-width: 2px; }
    .o-san { font-family: monospace; font-weight: 600; margin-right: 8px; }
    .o-reach { font-size: 0.8rem; margin-right: 8px; }
    .o-preview { font-family: monospace; font-size: 0.8rem; opacity: 0.7; }
    .ply-actions { display: flex; flex-wrap: wrap; gap: 8px; }
    .comment { width: 100%; }
    .tip { font-size: 0.82rem; color: color-mix(in srgb, currentColor 60%, transparent); margin: 0; }
    .moves-card { padding: 12px; }
    .moves { display: grid; grid-template-columns: 36px minmax(0, 1fr) minmax(0, 1fr); gap: 2px 4px; max-height: 70vh; overflow-y: auto; }
    .no { text-align: right; padding-right: 4px; color: color-mix(in srgb, currentColor 55%, transparent); align-self: center; font-size: 0.85rem; }
    .ply { font: inherit; font-family: monospace; text-align: left; padding: 3px 6px; border-radius: 4px; cursor: pointer;
      border: 1px solid transparent; background: transparent; color: inherit; }
    .ply:hover { background: color-mix(in srgb, currentColor 8%, transparent); }
    .ply.uncertain { border-color: #ef6c00; background: color-mix(in srgb, #ef6c00 12%, transparent); }
    .ply.confirmed { color: #2e7d32; }
    .ply.user { font-style: italic; }
    .ply.illegal { color: var(--mat-sys-error, #c62828); text-decoration: line-through; }
    .ply.cursor { outline: 2px solid var(--mat-sys-primary, #3f51b5); }
    .ply.end { font-family: inherit; font-size: 0.8rem; opacity: 0.7; }
    .unresolved { margin-top: 12px; display: flex; flex-direction: column; gap: 4px; color: var(--mat-sys-error, #c62828); font-size: 0.9rem; }
    .unresolved span { font-family: monospace; word-break: break-word; }
    /* PC: der Arbeitsbereich füllt die Fensterhöhe (Navigationsleiste, Kopfzeile und Kopfdaten abgezogen), jede Spalte
       scrollt für sich — Foto mit Ausschnitt, Brett und Zugliste sind zugleich sichtbar, ohne die Seite zu scrollen. */
    @media (min-width: 1101px) {
      .layout { --work-h: max(440px, calc(100dvh - 250px)); height: var(--work-h); align-items: stretch; }
      .layout > * { min-height: 0; }
      .photo { display: flex; flex-direction: column; overflow: hidden; }
      .photo-scroll { flex: 1 1 auto; min-height: 0; max-height: none; display: flex; justify-content: center; align-items: flex-start; }
      .photo-scroll img { max-height: 100%; }
      .photo-scroll.zoom { display: block; }
      .photo-scroll.zoom img { max-height: none; }
      .crop { flex: none; }
      .crop-frame { width: min(100%, calc(24vh * var(--crop-aspect, 3))); }
      .board-card { overflow: auto; }
      .board-wrap { width: min(100%, calc(var(--work-h) - 150px)); }
      .side { overflow: hidden; }
      .cursor-card { flex: none; max-height: 60%; overflow: auto; }
      .moves-card { flex: 1 1 auto; min-height: 0; display: flex; flex-direction: column; overflow: hidden; }
      .moves { max-height: none; flex: 1 1 auto; min-height: 0; }
    }
  `],
})
export class GameEditComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private games = inject(GamesService);
  private sheets = inject(ScoresheetService);
  private snackbar = inject(SnackbarService);
  private confirmDialog = inject(ConfirmService);
  private translate = inject(TranslateService);
  private destroyRef = inject(DestroyRef);
  private dialog = inject(MatDialog);
  private host = inject(ElementRef);
  readonly preferences = inject(PreferencesService);

  readonly results = ['*', '1-0', '0-1', '1/2-1/2'];

  gameId = 0;
  readonly loading = signal(true);
  readonly notFound = signal(false);
  readonly busy = signal(false);
  readonly saving = signal(false);
  readonly dirty = signal(false);
  readonly plies = signal<EditPly[]>([]);
  readonly cursor = signal(0);
  readonly mode = signal<'replace' | 'insert'>('replace');
  readonly flipped = signal(false);
  readonly unresolved = signal<string[]>([]);
  readonly isScoresheet = signal(false);
  readonly photoUrl = signal<string | null>(null);
  readonly zoom = signal(false);
  /** Je Formular-Eintrag der Kasten auf dem Foto (vom Modell; ältere Einlesungen haben keine). */
  readonly boxes = signal<(number[] | null)[]>([]);
  readonly sheetEntries = signal<string[]>([]);
  readonly unresolvedFrom = signal<number | null>(null);
  /** Pixelmaße des Fotos (aufrecht, wie der Browser es zeigt) — für das Seitenverhältnis des Ausschnitts. */
  readonly photoSize = signal<{ w: number; h: number } | null>(null);
  private photoBlob: Blob | null = null;
  private photoName = 'scoresheet.jpg';

  header = { white: '', black: '', result: '*', event: '', site: '', round: '', date: '', ownerSide: '' };

  readonly legalCount = computed(() => {
    const idx = this.plies().findIndex(p => p.illegal);
    return idx < 0 ? this.plies().length : idx;
  });
  readonly illegalCount = computed(() => this.plies().length - this.legalCount());
  readonly fens = computed(() => fensOf(this.plies()));
  readonly cursorFen = computed(() => this.fens()[Math.min(this.cursor(), this.fens().length - 1)]);
  readonly current = computed<EditPly | null>(() => this.plies()[this.cursor()] ?? null);
  readonly lastMove = computed<[string, string] | undefined>(() => {
    const prev = this.plies()[this.cursor() - 1];
    return prev && !prev.illegal ? [prev.uci.slice(0, 2), prev.uci.slice(2, 4)] : undefined;
  });
  /** Der bisherige Zug an dieser Stelle als gelber Pfeil — was man ersetzen würde. */
  readonly arrows = computed<BoardArrow[]>(() => {
    const p = this.current();
    return p && !p.illegal && p.uci ? [{ from: p.uci.slice(0, 2), to: p.uci.slice(2, 4), brush: 'yellow' }] : [];
  });
  /**
   * Der Ausschnitt des Formulars zum gewählten Halbzug, unter dem ganzen Foto: der Eintrag, aus dem der Zug stammt,
   * mit Umfeld. Am Ende der Zugliste der erste Eintrag, der sich nicht auflösen ließ. Ohne Kasten (vom Nutzer
   * eingefügter Zug, ältere Einlesung) kein Ausschnitt.
   */
  readonly crop = computed(() => {
    const size = this.photoSize();
    if (!size) return null;
    const p = this.current();
    const w = p ? p.w : this.unresolved().length ? this.unresolvedFrom() : null;
    if (w === null || w === undefined) return null;
    const view = cropView(this.boxes()[w], size.w, size.h);
    if (!view) return null;
    return { view, written: this.sheetEntries()[w] ?? '', uncertain: p ? p.uncertain && !p.confirmed : true };
  });
  readonly uncertainLeft = computed(() => this.plies().filter(p => p.uncertain && !p.confirmed && !p.illegal).length);
  readonly rows = computed<MoveRow[]>(() => {
    const out: MoveRow[] = [];
    const n = this.plies().length;
    for (let i = 0; i < n; i += 2) out.push({ no: i / 2 + 1, white: i, black: i + 1 < n ? i + 1 : null });
    return out;
  });

  ngOnInit(): void {
    this.gameId = Number(this.route.snapshot.paramMap.get('id'));
    this.games.get(this.gameId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: game => this.load(game),
      error: () => { this.notFound.set(true); this.loading.set(false); },
    });
  }

  ngOnDestroy(): void {
    const url = this.photoUrl();
    if (url) URL.revokeObjectURL(url);
  }

  private load(game: SavedGameDetail): void {
    const h = headersOf(game.pgn);
    this.header = {
      white: game.white ?? '', black: game.black ?? '', result: game.result || '*',
      event: h['Event'] ?? '', site: h['Site'] ?? '', round: h['Round'] ?? '', date: isoDateOf(h['Date']),
      ownerSide: game.ownerSide ?? '',
    };
    // RepCheck-Partien tragen „RepCheck saved game" als Veranstaltung — das ist keine Angabe des Nutzers.
    if (this.header.event === 'RepCheck saved game') this.header.event = '';
    const fromPgn = pliesOfPgn(game.pgn);
    this.flipped.set(game.ownerSide === 'black');

    if (!game.scanId) {
      this.plies.set(fromPgn);
      this.loading.set(false);
      return;
    }
    this.isScoresheet.set(true);
    this.sheets.photo(this.gameId).pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef)).subscribe(blob => {
      if (!blob) return;
      this.photoBlob = blob;
      this.photoName = `scoresheet-${this.gameId}.${blob.type === 'image/png' ? 'png' : blob.type === 'image/webp' ? 'webp' : 'jpg'}`;
      this.photoUrl.set(URL.createObjectURL(blob));
    });
    this.sheets.editState(this.gameId).pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef)).subscribe(state => {
      const comments = fromPgn.map(p => stripSheetNotes(p.comment));
      // Der gespeicherte Stand gilt nur, wenn er zu den Zügen der Partie passt (sie kann anderswo geändert worden sein).
      const matches = state && state.plies.length === fromPgn.length && state.plies.every((p, i) => p.san === fromPgn[i].san);
      this.plies.set(matches ? fromServer(state!.plies, comments) : fromPgn.map((p, i) => ({ ...p, comment: comments[i] })));
      this.unresolved.set(matches ? state!.unresolved : []);
      this.unresolvedFrom.set(matches ? state!.unresolvedFrom ?? null : null);
      this.boxes.set(state?.boxes ?? []);
      this.sheetEntries.set(state?.written ?? []);
      this.loading.set(false);
      const first = this.plies().findIndex(p => p.uncertain && !p.confirmed);
      if (first >= 0) { this.cursor.set(first); this.revealCursor(); }
    });
  }

  onPhotoLoad(e: Event): void {
    const img = e.target as HTMLImageElement;
    if (img.naturalWidth > 0 && img.naturalHeight > 0) this.photoSize.set({ w: img.naturalWidth, h: img.naturalHeight });
  }

  onSide(side: string): void {
    this.dirty.set(true);
    this.flipped.set(side === 'black');
  }

  go(i: number): void {
    this.cursor.set(Math.max(0, Math.min(i, this.legalCount())));
    this.revealCursor();
  }

  /** Am PC scrollt die Zugliste für sich — der gewählte Halbzug soll dort sichtbar bleiben (Pfeiltasten,
   *  „nächste unsichere Stelle"). */
  private revealCursor(): void {
    setTimeout(() => (this.host.nativeElement as HTMLElement).querySelector('.moves .ply.cursor')
      ?.scrollIntoView?.({ block: 'nearest' }));
  }

  nextUncertain(): void {
    const idx = nextUncertainFrom(this.plies(), this.cursor() + 1);
    if (idx !== null) this.go(idx);
  }

  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    const target = e.target as HTMLElement | null;
    if (target && /^(INPUT|TEXTAREA|SELECT)$/.test(target.tagName)) return;
    if (e.key === 'ArrowLeft') { this.go(this.cursor() - 1); e.preventDefault(); }
    if (e.key === 'ArrowRight') { this.go(this.cursor() + 1); e.preventDefault(); }
  }

  plyLabel(i: number): string {
    const no = Math.floor(i / 2) + 1;
    return i % 2 === 0 ? `${no}.` : `${no}…`;
  }

  plyClass(i: number): Record<string, boolean> {
    const p = this.plies()[i];
    return {
      uncertain: p.uncertain && !p.confirmed && !p.illegal,
      confirmed: p.confirmed && this.isScoresheet(),
      user: p.match === 'user' && this.isScoresheet(),
      illegal: p.illegal,
      cursor: this.cursor() === i,
    };
  }

  /** Ein Zug am Brett: ersetzt den Halbzug am Cursor (oder fügt davor ein). */
  onBoardMove(m: UserBoardMove): void {
    this.apply(m.san, this.mode());
  }

  /** Eine der angebotenen Lesarten wählen — wie „diesen Zug am Brett spielen". */
  /** Eine Lesart wählen. Die schon gewählte anzuklicken heißt „passt so" — wie Bestätigen. Danach geht es wie
   *  beim Bestätigen zur nächsten unsicheren Stelle, nicht bloß zum nächsten Halbzug (gewünscht 2026-09-27). */
  choose(o: ScoresheetOption): void {
    const p = this.current();
    if (p && !p.illegal && o.uci === p.uci) { this.confirm(); return; }
    this.apply(o.san, 'replace', true);
  }

  private apply(san: string, mode: 'replace' | 'insert', thenNextUncertain = false): void {
    const i = this.cursor();
    const list = this.plies();
    const old = list[i];
    const written = mode === 'insert' ? '' : old?.written ?? '';
    // Ein Halbzug ohne Eintrag (eingefügt) bleibt ohne; nur Anhängen am Ende verbraucht den nächsten offenen.
    const w = mode === 'insert' ? null : i < list.length ? list[i].w : writtenIndexAt(list, i);
    const comment = mode === 'replace' ? stripSheetNotes(old?.comment) : null;
    const uci = this.uciOf(san, this.cursorFen());
    if (!uci) return;
    const mine = userPly(san, uci, w, written, comment);
    this.dirty.set(true);

    if (!this.isScoresheet()) {
      const tail = mode === 'insert' ? list.slice(i) : list.slice(i + 1);
      this.plies.set(revalidate([...list.slice(0, i), mine, ...tail]));
      this.cursor.set(i + 1);
      return;
    }
    const req = resolveRequest(list, i, mode, san);
    this.reResolve(req, [...list.slice(0, i), mine], mode === 'insert' ? list.slice(i) : list.slice(i + 1), i + 1,
      thenNextUncertain);
  }

  /** Den Halbzug am Cursor streichen (ein doppelt notierter oder erfundener Eintrag). */
  remove(): void {
    const i = this.cursor();
    const list = this.plies();
    if (i >= list.length) return;
    this.dirty.set(true);
    if (!this.isScoresheet()) {
      this.plies.set(revalidate([...list.slice(0, i), ...list.slice(i + 1)]));
      return;
    }
    this.reResolve(resolveRequest(list, i, 'delete'), list.slice(0, i), list.slice(i + 1), i);
  }

  /** Ja, dieser Zug stimmt — die Stelle ist nicht mehr unsicher. */
  confirm(): void {
    const i = this.cursor();
    this.plies.update(list => list.map((p, k) => k === i ? { ...p, confirmed: true, uncertain: false } : p));
    this.dirty.set(true);
    if (this.uncertainLeft() > 0) this.nextUncertain();
    else this.go(i + 1);
  }

  setComment(text: string): void {
    const i = this.cursor();
    this.plies.update(list => list.map((p, k) => k === i ? { ...p, comment: text.trim() ? text : null } : p));
    this.dirty.set(true);
  }

  /**
   * Den Rest ab einer Stelle vom Server neu aufbereiten lassen (Formular-Einträge ab `writtenFrom`). Scheitert
   * das, bleibt der bisherige Rest stehen, soweit er noch legal ist — `fallbackTail`.
   */
  private reResolve(req: { prefix: string[]; writtenFrom: number }, head: EditPly[], fallbackTail: EditPly[],
    nextCursor: number, thenNextUncertain = false): void {
    this.busy.set(true);
    this.sheets.resolve(this.gameId, req.prefix, req.writtenFrom).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: res => {
        this.plies.set(revalidate([...head, ...fromServer(res.plies)]));
        this.unresolved.set(res.unresolved);
        this.unresolvedFrom.set(res.unresolvedFrom ?? null);
        this.busy.set(false);
        this.go(thenNextUncertain ? nextUncertainFrom(this.plies(), nextCursor) ?? nextCursor : nextCursor);
      },
      error: () => {
        this.busy.set(false);
        this.plies.set(revalidate([...head, ...fallbackTail]));
        this.go(nextCursor);
        this.snackbar.info(this.translate.instant('games.edit.resolveFailed'));
      },
    });
  }

  private uciOf(san: string, fen: string): string | null {
    try {
      const m = new Chess(fen).move(san);
      return m.from + m.to + (m.promotion ?? '');
    } catch {
      return null;
    }
  }

  openPhoto(download: boolean): void {
    if (!this.photoBlob) return;
    if (download) openPhotoBlob(this.photoBlob, this.photoName);
    else ScoresheetPhotoDialogComponent.open(this.dialog, this.gameId);
  }

  save(): void {
    if (this.illegalCount() > 0) {
      this.confirmDialog.ask('games.edit.dropIllegal', { count: this.illegalCount() })
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe(ok => { if (ok) this.doSave(); });
      return;
    }
    this.doSave();
  }

  private doSave(): void {
    const legal = this.plies().filter(p => !p.illegal);
    const comments = this.isScoresheet()
      ? commentsForSave(legal, this.unresolved())
      : legal.map(p => p.comment?.trim() || null);
    this.saving.set(true);
    this.sheets.update(this.gameId, {
      moves: legal.map((p, i) => ({ san: p.san, comment: comments[i] })),
      white: this.header.white || null,
      black: this.header.black || null,
      result: this.header.result || '*',
      event: this.header.event || null,
      site: this.header.site || null,
      round: this.header.round || null,
      date: this.header.date || null,
      ownerSide: this.header.ownerSide,
      scoresheetPlies: this.isScoresheet() ? toServer(legal) : null,
    }).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: () => {
        this.saving.set(false);
        this.dirty.set(false);
        this.plies.set(legal);
        this.snackbar.success(this.translate.instant('games.edit.saved'));
        this.router.navigate(['/games', this.gameId]);
      },
      error: (err: HttpErrorResponse) => {
        this.saving.set(false);
        this.snackbar.info(err.error?.message ?? this.translate.instant('games.edit.saveFailed'));
      },
    });
  }
}
