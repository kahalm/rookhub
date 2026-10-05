import { scrollIntoContainer } from '../../shared/pgn-viewer/move-list.component';
import { ElementRef, ChangeDetectionStrategy, Component, DestroyRef, HostListener, OnDestroy, OnInit, computed, effect, inject, signal, untracked } from '@angular/core';
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
import { Observable, of } from 'rxjs';
import { catchError } from 'rxjs/operators';
import { ChessBoardComponent, UserBoardMove } from '../../shared/pgn-viewer/chess-board.component';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { PreferencesService } from '../../core/preferences.service';
import { SnackbarService } from '../../core/snackbar.service';
import { GamesService, SavedGameDetail } from './games.service';
import { ScoresheetPhotoDialogComponent } from './scoresheet-photo-dialog.component';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { ScoresheetService, openPhotoBlob, photoFileName } from './scoresheet.service';
import { distinctClassifiers, seasonOf } from './classifier.util';
import { addTag, distinctTags, MAX_TAG_LENGTH, MAX_TAGS } from './tags.util';
import { commentsForSave, headersOf, isoDateOf, pliesOfPgn, startFenOf, stripSheetNotes, toServer } from './game-edit.util';
import { SheetEditSession } from './sheet-edit-session';
import { LeaveConfirm } from '../../core/unsaved-changes.guard';
import { isBoardHotkey } from '../../shared/keyboard.util';

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
        @if (clubGameId()) {
          <!-- Kopie einer Vereinspartie (0.660.0) -->
          <p class="club-linked">{{ 'games.edit.clubLinked' | translate }}</p>
        }

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
            <input matInput type="date" [(ngModel)]="header.date" name="date" (ngModelChange)="dirty.set(true); headerDate.set($event)" /></mat-form-field>
          <!-- Meine Seite: dreht Partieseite, Teilen-Link und Vorschaubild — und hier gleich das Brett. -->
          <mat-form-field appearance="outline" subscriptSizing="dynamic" class="short"><mat-label>{{ 'games.edit.ownerSide' | translate }}</mat-label>
            <mat-select [(ngModel)]="header.ownerSide" name="ownerSide" (ngModelChange)="onSide($event)">
              <mat-option value="">{{ 'games.edit.sideNone' | translate }}</mat-option>
              <mat-option value="white">{{ 'scoresheet.sideWhite' | translate }}</mat-option>
              <mat-option value="black">{{ 'scoresheet.sideBlack' | translate }}</mat-option>
            </mat-select></mat-form-field>
          <!-- Klassifizierer der Partienliste (0.661.0): Seite/Liga und Modus/Jahrgang. Bei Online-Partien steht der
               abgeleitete Wert als Platzhalter da; leer lassen = er gilt weiter. Vorschläge aus den eigenen Partien. -->
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.edit.classifier1' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.classifier1" name="classifier1" maxlength="80" list="cls1-options"
                   [placeholder]="derived1()" (ngModelChange)="dirty.set(true)" />
            <mat-hint>{{ 'games.edit.classifier1Hint' | translate }}</mat-hint></mat-form-field>
          <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.edit.classifier2' | translate }}</mat-label>
            <input matInput [(ngModel)]="header.classifier2" name="classifier2" maxlength="80" list="cls2-options"
                   [placeholder]="derived2()" (ngModelChange)="dirty.set(true)" />
            <mat-hint>{{ 'games.edit.classifier2Hint' | translate }}</mat-hint></mat-form-field>
          <!-- Eigene Tags (0.662.0): Freitext, Enter oder Komma fügt hinzu; die Vorschläge sind die schon vergebenen. -->
          <div class="tags-field">
            <mat-form-field appearance="outline" subscriptSizing="dynamic"><mat-label>{{ 'games.tags.label' | translate }}</mat-label>
              <input matInput [(ngModel)]="tagInput" name="tagInput" [maxlength]="maxTagLength" list="tag-options"
                     [disabled]="tags().length >= maxTags" (keydown.enter)="$event.preventDefault(); commitTag()"
                     (input)="onTagInput()" (change)="commitTag()" />
              <mat-hint>{{ 'games.tags.hint' | translate: { max: maxTags } }}</mat-hint></mat-form-field>
            <span class="tag-chips">
              @for (t of tags(); track t) {
                <span class="tag-chip">#{{ t }}<button type="button" (click)="removeTag(t)" [attr.aria-label]="'games.tags.remove' | translate: { tag: t }">×</button></span>
              }
            </span>
          </div>
          <datalist id="tag-options">@for (v of tagSuggestions(); track v) { <option [value]="v"></option> }</datalist>
          <datalist id="cls1-options">@for (v of options1(); track v) { <option [value]="v"></option> }</datalist>
          <datalist id="cls2-options">@for (v of options2(); track v) { <option [value]="v"></option> }</datalist>
        </mat-card>

        <div class="layout" [class.with-photo]="!!photoUrl()">
          @if (photoUrl(); as src) {
            <mat-card class="photo">
              <div class="photo-bar">
                <strong>{{ 'games.edit.photo' | translate }}</strong>
                <!-- Formular über mehrere Blätter (0.600.0): blättern; der gewählte Zug blättert von selbst mit. -->
                @if (pageCount() > 1) {
                  <mat-button-toggle-group class="pager" [value]="shownPage()" (change)="shownPage.set($event.value)" hideSingleSelectionIndicator>
                    @for (n of pageNumbers(); track n) {
                      <mat-button-toggle [value]="n">{{ n }}</mat-button-toggle>
                    }
                  </mat-button-toggle-group>
                }
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
                    @if (pageCount() > 1) { <span class="written">{{ 'scoresheet.page' | translate: { n: c.page } }}</span> }
                    @if (c.written) { <span class="written">{{ 'games.edit.written' | translate: { text: c.written } }}</span> }
                  </div>
                  <div class="crop-frame" [class.uncertain]="c.uncertain" [style.aspect-ratio]="c.view.aspect" [style.--crop-aspect]="c.view.aspect">
                    <img [src]="pageUrl(c.page) ?? src" alt="" [style.width.%]="c.view.imgW" [style.height.%]="c.view.imgH"
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
              @if (session.canUndoRemove()) {
                <div class="undo-line">
                  <span>{{ 'games.edit.deleted' | translate }}</span>
                  <button mat-button (click)="session.undoRemove()" [disabled]="busy()"><mat-icon>undo</mat-icon> {{ 'common.undo' | translate }}</button>
                </div>
              }
              @if (current(); as p) {
                <div class="where">
                  <strong>{{ plyLabel(cursor()) }}</strong>
                  <span class="san" [class.bad]="p.illegal">{{ p.san }}</span>
                  @if (p.written) { <span class="written">{{ 'games.edit.written' | translate: { text: p.written } }}</span> }
                  @if (p.match === 'inserted') { <span class="chip warn">{{ 'games.edit.notOnSheet' | translate }}</span> }
                  @if (p.uncertain && !p.confirmed) { <span class="chip warn">{{ 'games.edit.uncertain' | translate }}</span> }
                  @if (p.check && !p.confirmed) {
                    <span class="chip warn" [title]="'games.edit.engineCheckHint' | translate">{{ (p.check === 'replaced' ? 'games.edit.engineReplaced' : 'games.edit.engineSuggested') | translate }}</span>
                  }
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
                  <!-- Abgesetzt von „Stimmt" (UX-070): Textknopf in Warnfarbe, ganz rechts. -->
                  <button mat-button color="warn" class="del" (click)="remove()" [disabled]="busy()"><mat-icon>backspace</mat-icon> {{ 'games.edit.delete' | translate }}</button>
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
    .club-linked { margin: 0 0 12px; padding: 8px 12px; border-radius: 6px; font-size: 14px;
      background: color-mix(in srgb, var(--rh-info, #1976d2) 10%, transparent); }
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
    .tags-field { display: flex; flex-direction: column; gap: 6px; grid-column: 1 / -1; }
    .tag-chips { display: flex; flex-wrap: wrap; gap: 6px; }
    .tag-chip { display: inline-flex; align-items: center; gap: 4px; padding: 3px 4px 3px 10px; border-radius: 14px; font-size: 0.85rem;
      background: color-mix(in srgb, var(--rh-info, #1976d2) 14%, transparent); }
    .tag-chip button { border: 0; background: transparent; color: inherit; cursor: pointer; font-size: 1.1rem; line-height: 1; padding: 0 6px; }
    .photo-bar { display: flex; align-items: center; gap: 4px; padding: 0 4px 4px; }
    .pager { margin-left: 8px; }
    .pager .mat-button-toggle { font-size: 0.85rem; }
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
    .ply-actions .del { margin-left: auto; }
    .undo-line { display: flex; flex-wrap: wrap; align-items: center; gap: 4px 12px; font-size: 0.9rem; }
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
export class GameEditComponent implements OnInit, OnDestroy, LeaveConfirm {
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
  readonly saving = signal(false);
  readonly dirty = signal(false);
  readonly flipped = signal(false);
  readonly zoom = signal(false);
  /** Formular-Fotos je Seite (Index = Seite − 1), sobald geladen. */
  readonly photoUrls = signal<(string | null)[]>([]);
  readonly pageCount = signal(1);
  readonly pageNumbers = computed(() => Array.from({ length: this.pageCount() }, (_, i) => i + 1));
  /** Welche Seite oben gezeigt wird. */
  readonly shownPage = signal(1);
  readonly photoUrl = computed(() => this.photoUrls()[this.shownPage() - 1] ?? this.photoUrls()[0] ?? null);
  private photoBlobs: (Blob | null)[] = [];

  /** Der Arbeitsstand (geteilt mit der Formular-Korrektur in LeagueHub). */
  readonly session = new SheetEditSession({
    resolve: (prefix, writtenFrom) => this.sheets.resolve(this.gameId, prefix, writtenFrom),
    changed: () => this.dirty.set(true),
    moved: () => this.revealCursor(),
    resolveFailed: () => this.snackbar.info(this.translate.instant('games.edit.resolveFailed')),
    bind: o => o.pipe(takeUntilDestroyed(this.destroyRef)),
  });
  readonly plies = this.session.plies;
  readonly cursor = this.session.cursor;
  readonly mode = this.session.mode;
  readonly unresolved = this.session.unresolved;
  readonly isScoresheet = this.session.isScoresheet;
  /** Kopie einer Vereinspartie (0.660.0) — Hinweis, dass eine Zugkorrektur dorthin geht oder die Kopie löst. */
  readonly clubGameId = signal<number | null>(null);
  readonly photoSize = this.session.photoSize;
  readonly busy = this.session.busy;
  readonly legalCount = this.session.legalCount;
  readonly illegalCount = this.session.illegalCount;
  readonly cursorFen = this.session.cursorFen;
  readonly current = this.session.current;
  readonly lastMove = this.session.lastMove;
  readonly arrows = this.session.arrows;
  readonly crop = this.session.crop;
  readonly uncertainLeft = this.session.uncertainLeft;
  readonly rows = this.session.rows;

  header = { white: '', black: '', result: '*', event: '', site: '', round: '', date: '', ownerSide: '', classifier1: '', classifier2: '' };

  /** Der abgeleitete Wert (nur Online-Partien) — Platzhalter, solange der Nutzer nichts einträgt. */
  readonly derived1 = signal('');
  readonly derived2 = signal('');
  /** Vorschläge: Werte der eigenen Partien; beim Jahrgang zusätzlich die Saison zum Spieldatum. */
  private readonly known1 = signal<string[]>([]);
  private readonly known2 = signal<string[]>([]);
  readonly options1 = computed(() => this.known1());
  readonly options2 = computed(() => {
    const season = seasonOf(this.headerDate());
    const all = this.known2();
    return season && !all.includes(season) ? [season, ...all] : all;
  });
  readonly headerDate = signal('');

  /** Eigene Tags der Partie + das Eingabefeld; Vorschläge sind die schon vergebenen, soweit noch nicht an dieser Partie. */
  readonly tags = signal<string[]>([]);
  tagInput = '';
  readonly maxTags = MAX_TAGS;
  readonly maxTagLength = MAX_TAG_LENGTH;
  private readonly knownTags = signal<string[]>([]);
  readonly tagSuggestions = computed(() => {
    const own = this.tags().map(t => t.toLowerCase());
    return this.knownTags().filter(t => !own.includes(t.toLowerCase()));
  });

  commitTag(): void {
    const next = addTag(this.tags(), this.tagInput);
    if (next) { this.tags.set(next); this.dirty.set(true); }
    this.tagInput = '';
  }

  /** Ein Komma schließt den Tag ab, wie Enter. */
  onTagInput(): void {
    if (this.tagInput.includes(',')) this.commitTag();
  }

  removeTag(tag: string): void {
    this.tags.set(this.tags().filter(t => t !== tag));
    this.dirty.set(true);
  }

  ngOnInit(): void {
    this.gameId = Number(this.route.snapshot.paramMap.get('id'));
    this.games.get(this.gameId).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: game => this.load(game),
      error: () => { this.notFound.set(true); this.loading.set(false); },
    });
  }

  constructor() {
    // Der gewählte Zug steht auf einer anderen Seite: dorthin blättern (von Hand blättern bleibt, bis der Zug wechselt).
    effect(() => {
      const page = this.session.currentPage();
      if (page) untracked(() => this.shownPage.set(Math.min(page, this.pageCount())));
    });
  }

  ngOnDestroy(): void {
    for (const url of this.photoUrls()) if (url) URL.revokeObjectURL(url);
  }

  /** Vor dem Verlassen fragen, solange es ungespeicherte Korrekturen gibt (`unsavedChangesGuard`). Speichern setzt
   *  `dirty` vor dem Weiterleiten zurück — dann ohne Rückfrage. */
  canLeave(): boolean | Observable<boolean> {
    return !this.dirty() || this.confirmDialog.ask('games.edit.discardChanges');
  }

  /** Neu laden oder Tab schließen: der Browser fragt mit seinem eigenen Text. */
  @HostListener('window:beforeunload', ['$event'])
  onBeforeUnload(e: BeforeUnloadEvent): void {
    if (!this.dirty()) return;
    e.preventDefault();
    e.returnValue = true;   // ältere Browser fragen nur so
  }

  /** Das Foto einer Seite, sobald es geladen ist. */
  pageUrl(page: number): string | null {
    return this.photoUrls()[page - 1] ?? null;
  }

  private loadPage(page: number): void {
    this.sheets.photo(this.gameId, page).pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef)).subscribe(blob => {
      if (!blob) return;
      this.photoBlobs[page - 1] = blob;
      const url = URL.createObjectURL(blob);
      this.photoUrls.update(list => { const next = [...list]; next[page - 1] = url; return next; });
      // Maße gleich messen — der Ausschnitt einer Seite braucht sie, auch wenn oben gerade eine andere steht.
      const img = new Image();
      img.onload = () => this.session.setPageSize(page, img.naturalWidth, img.naturalHeight);
      img.src = url;
    });
  }

  private load(game: SavedGameDetail): void {
    const h = headersOf(game.pgn);
    this.header = {
      white: game.white ?? '', black: game.black ?? '', result: game.result || '*',
      event: h['Event'] ?? '', site: h['Site'] ?? '', round: h['Round'] ?? '', date: isoDateOf(h['Date']),
      ownerSide: game.ownerSide ?? '',
      classifier1: game.classifier1Set ?? '', classifier2: game.classifier2Set ?? '',
    };
    this.headerDate.set(this.header.date);
    this.tags.set(game.tags ?? []);
    // Abgeleitet = geltender Wert, solange nichts gesetzt ist (der Server liefert nur den geltenden).
    this.derived1.set(game.classifier1Set ? '' : game.classifier1 ?? '');
    this.derived2.set(game.classifier2Set ? '' : game.classifier2 ?? '');
    this.games.list(500).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: list => { this.known1.set(distinctClassifiers(list, 1)); this.known2.set(distinctClassifiers(list, 2)); this.knownTags.set(distinctTags(list)); },
      error: () => { /* Vorschläge sind Zugabe */ },
    });
    // RepCheck-Partien tragen „RepCheck saved game" als Veranstaltung — das ist keine Angabe des Nutzers.
    if (this.header.event === 'RepCheck saved game') this.header.event = '';
    const fromPgn = pliesOfPgn(game.pgn);
    this.session.startFen.set(startFenOf(game.pgn));   // Stellungspartie (FEN-Kopf): Brett und Legalität ab dort
    this.flipped.set(game.ownerSide === 'black');

    this.clubGameId.set(game.clubGameId ?? null);
    // Kopie einer Vereinspartie, deren Formular noch aufbewahrt ist (0.660.0): wie eine eingelesene Partie korrigieren
    if (!game.scanId && !game.clubSheet) {
      this.plies.set(fromPgn);
      this.loading.set(false);
      return;
    }
    this.isScoresheet.set(true);
    this.loadPage(1);
    this.sheets.editState(this.gameId).pipe(catchError(() => of(null)), takeUntilDestroyed(this.destroyRef)).subscribe(state => {
      const pages = Math.max(1, state?.pageCount ?? 1);
      this.pageCount.set(pages);
      for (let p = 2; p <= pages; p++) this.loadPage(p);
      const comments = fromPgn.map(p => stripSheetNotes(p.comment));
      // Der gespeicherte Stand gilt nur, wenn er zu den Zügen der Partie passt (sie kann anderswo geändert worden sein).
      const matches = state && state.plies.length === fromPgn.length && state.plies.every((p, i) => p.san === fromPgn[i].san);
      if (matches) {
        this.session.loadSheet(state!, comments);
      } else {
        this.plies.set(fromPgn.map((p, i) => ({ ...p, comment: comments[i] })));
        this.session.boxes.set(state?.boxes ?? []);
        this.session.sheetEntries.set(state?.written ?? []);
        this.session.entryPages.set(state?.pages ?? []);
        this.session.goToFirstUncertain();
      }
      this.loading.set(false);
    });
  }

  onPhotoLoad(e: Event): void {
    const img = e.target as HTMLImageElement;
    if (this.shownPage() === 1) this.session.onPhotoLoad(e);
    else this.session.setPageSize(this.shownPage(), img.naturalWidth, img.naturalHeight);
  }

  onSide(side: string): void {
    this.dirty.set(true);
    this.flipped.set(side === 'black');
  }

  go(i: number): void {
    this.session.go(i);
  }

  /** Der gewählte Halbzug soll in der Zugliste sichtbar bleiben (Pfeiltasten, „nächste unsichere Stelle") — NUR in deren
   *  eigenem Rollbereich. `scrollIntoView` rollte am Handy die ganze Seite, und das Brett wanderte bei jedem Zug nach oben
   *  (gemeldet 2026-09-28, dieselbe Falle wie 0.514.1 auf der Partieseite). */
  private revealCursor(): void {
    setTimeout(() => {
      const el = (this.host.nativeElement as HTMLElement).querySelector<HTMLElement>('.moves .ply.cursor');
      if (el) scrollIntoContainer(el);
    });
  }

  nextUncertain(): void {
    this.session.nextUncertain();
  }

  @HostListener('document:keydown', ['$event'])
  onKey(e: KeyboardEvent): void {
    if (!isBoardHotkey(e)) return;
    if (e.key === 'ArrowLeft') { this.go(this.cursor() - 1); e.preventDefault(); }
    if (e.key === 'ArrowRight') { this.go(this.cursor() + 1); e.preventDefault(); }
  }

  plyLabel(i: number): string {
    return this.session.plyLabel(i);
  }

  plyClass(i: number): Record<string, boolean> {
    return this.session.plyClass(i);
  }

  /** Ein Zug am Brett: ersetzt den Halbzug am Cursor (oder fügt davor ein). */
  onBoardMove(m: UserBoardMove): void {
    this.session.play(m.san);
  }

  choose(o: Parameters<SheetEditSession['choose']>[0]): void {
    this.session.choose(o);
  }

  remove(): void {
    this.session.remove();
  }

  confirm(): void {
    this.session.confirm();
  }

  setComment(text: string): void {
    this.session.setComment(text);
  }

  openPhoto(download: boolean): void {
    const page = this.shownPage();
    const blob = this.photoBlobs[page - 1];
    if (!blob) return;
    if (download) openPhotoBlob(blob, photoFileName(this.gameId, blob, page));
    else ScoresheetPhotoDialogComponent.open(this.dialog, this.gameId, page);
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
    this.commitTag();   // ein noch nicht bestätigter Tag geht beim Speichern nicht verloren
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
      classifier1: this.header.classifier1.trim(),
      classifier2: this.header.classifier2.trim(),
      tags: this.tags(),
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
