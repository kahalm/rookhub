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
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatTabsModule } from '@angular/material/tabs';
import { MatMenuModule } from '@angular/material/menu';
import { FormsModule } from '@angular/forms';
import { Subscription, interval } from 'rxjs';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { GuessService, GuessSession } from './guess.service';
import { GameAnalysis, GameAnalysisService, GuessUploadStatus } from '../analysis/game-analysis.service';
import { AuthService } from '../../core/auth.service';
import { ViewStateService } from '../../core/view-state.service';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';

/** Reihenfolge des Bestands auf der Punktepartie-Seite. */
export type GuessSort = 'title' | 'short' | 'long' | 'event';

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
  imports: [IconLabelDirective, CommonModule, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule,
    MatTooltipModule, MatButtonToggleModule, MatFormFieldModule, MatInputModule,
    MatProgressBarModule, MatDialogModule, MatTabsModule, MatMenuModule,
    TranslatePipe, LoadingSpinnerComponent, HelpHintComponent],
  template: `
    <div class="gl-container">
      <div class="header">
        <h1>{{ 'guess.title' | translate }}
          <!-- Rest der Erklärung + „Seite des Gewinners" hinter dem ⓘ (d2-rk-guess-vorlauf). -->
          <app-help-hint [text]="('guess.introMore' | translate) + paraSep + ('guess.curatedHint' | translate)" /></h1>
        <!-- Alle Knoepfe in EINEM Kasten: die Kopfzeile verteilt ihre Kinder ueber die ganze
             Breite (space-between), einzeln stuenden sie also auseinander statt nebeneinander
             am Rand. Und alle in derselben Form — ein gefuellter Knopf daneben behauptete eine
             Rangfolge, die es hier nicht gibt.
             Der STELLUNGSFILTER steht auch ohne Anmeldung da: er ist eine Suche, und der Baum
             wie die Listen dahinter sind anonym abfragbar. Nur das Anfordern einer Partie
             braucht ein Konto, und das sagt der Dialog an der Stelle, wo der Knopf waere. -->
        <div class="header-actions">
          @if (loggedIn) {
            <a mat-stroked-button routerLink="/analysis/games" [appIconLabel]="'guess.toAnalyses' | translate">
              <mat-icon>insights</mat-icon> <span class="ha-label">{{ 'guess.toAnalyses' | translate }}</span>
            </a>
            <button mat-stroked-button (click)="openLibrary()" [appIconLabel]="'guess.library.open' | translate">
              <mat-icon>library_books</mat-icon> <span class="ha-label">{{ 'guess.library.open' | translate }}</span>
            </button>
          }
          <button mat-stroked-button (click)="openPositionFilter()" [appIconLabel]="'guess.tree.open' | translate">
            <mat-icon>account_tree</mat-icon> <span class="ha-label">{{ 'guess.tree.open' | translate }}</span>
          </button>
        </div>
      </div>
      <!-- Ohne Konto: der Hinweis als zweiter Halbsatz im Untertitel statt eigener Zeile (z1-rk-guess-einzeltab). -->
      <p class="muted intro">{{ 'guess.intro' | translate }}@if (!loggedIn) { <span class="anon-hint">{{ 'guess.anonHint' | translate }}</span>}</p>

      @if (loading) {
        <app-loading-spinner />
      } @else {
        <!-- Angefangene Durchläufe stehen oben (d2-rk-guess-durchlaeufe-unten): wer weiterspielen will, scrollt nicht ans Ende. -->
        <!-- z1-rk-guess-vorlauf: EINE Zeile — Titel einzeilig mit „…", Seite + Punkte klein darunter, ▶ und Löschen rechts. -->
        @for (s of runningSessions; track s.id) {
          <mat-card class="run resume">
            <mat-card-content>
              <div class="run-row resume-row">
                <div class="resume-main">
                  <a class="g-title" [routerLink]="['/guess', s.id]" [title]="s.title || ''"><span class="resume-label">{{ 'guess.resume' | translate }}:</span>
                    {{ s.title || ('guess.untitled' | translate) }}</a>
                  <span class="muted small resume-meta">{{ (s.guessWhite ? 'guess.white' : 'guess.black') | translate }} ·
                    <span class="pts">{{ 'guess.score' | translate:{ points: s.points, max: s.maxPoints } }}</span></span>
                </div>
                <a mat-flat-button color="primary" class="resume-btn" [routerLink]="['/guess', s.id]"
                   [attr.aria-label]="'guess.resume' | translate">
                  <mat-icon>play_arrow</mat-icon> <span class="resume-btn-label">{{ 'guess.resume' | translate }}</span>
                </a>
                <button mat-icon-button
                        [appIconLabel]="'common.delete' | translate" (click)="remove(s)">
                  <mat-icon>delete</mat-icon>
                </button>
              </div>
            </mat-card-content>
          </mat-card>
        }
        <!-- Suchfeld in der Filterzeile der Liste statt eigener Zeile darüber (z1-rk-guess-vorlauf). -->
        <ng-template #searchField>
          <mat-form-field appearance="outline" class="search" subscriptSizing="dynamic">
            <mat-label>{{ 'guess.search.label' | translate }}</mat-label>
            <mat-icon matPrefix>search</mat-icon>
            <input matInput [ngModel]="query" (ngModelChange)="setQuery($event)" name="guessSearch"
                   [placeholder]="'guess.search.placeholder' | translate">
            @if (query) {
              <button matSuffix mat-icon-button (click)="setQuery('')"
                      [attr.aria-label]="'guess.search.clear' | translate">
                <mat-icon>close</mat-icon>
              </button>
            }
          </mat-form-field>
        </ng-template>

        <!-- Ohne Konto gibt es nur den Meisterpartien-Bestand: dann keine Reiterleiste mit einem einzigen Reiter,
             die Anzahl steht in der Filterzeile (z1-rk-guess-einzeltab). -->
        @if (loggedIn) {
          <mat-tab-group class="lists" [(selectedIndex)]="tab" animationDuration="0ms">
          <mat-tab [label]="('guess.curated' | translate) + ' (' + curatedShown.length + ')'">
            <ng-container *ngTemplateOutlet="curatedList" />
          </mat-tab>
          <mat-tab [label]="('guess.ownGames' | translate) + ' (' + ownShown.length + ')'">
            <ng-container *ngTemplateOutlet="ownList" />
          </mat-tab>
          </mat-tab-group>
        } @else {
          <div class="lists single">
            <ng-container *ngTemplateOutlet="curatedList" />
          </div>
        }

        <ng-template #curatedList>
        <mat-card class="start-card">
          <mat-card-content>
            <div class="sec-head">
              <ng-container *ngTemplateOutlet="searchField" />
              @if (hasAnnotated) {
                <mat-button-toggle-group [(ngModel)]="annotatedOnly" (change)="onFilterChange()"
                                         [attr.aria-label]="'guess.filterLabel' | translate" class="small-toggle">
                  <mat-button-toggle [value]="false">{{ 'guess.filterAll' | translate }}</mat-button-toggle>
                  <mat-button-toggle [value]="true">{{ 'guess.filterAnnotated' | translate }}</mat-button-toggle>
                </mat-button-toggle-group>
              }
              @if (!loggedIn) {
                <span class="muted small count">{{ 'guess.gamesCount' | translate:{ count: curatedShown.length } }}</span>
              }
              <!-- Sortieren statt Scrollen: „eine kurze Partie" findet man ueber die Laenge, nicht
                   ueber 655 Titel (Codereview W5 UX-016). Die Vorgabe bleibt die Reihenfolge des
                   Servers (nach Titel). Als Symbolknopf ⇅ mit Menü — passt am Handy neben Suche und Filter. -->
              <button mat-icon-button class="sort-btn" [class.active]="sort !== 'title'" [matMenuTriggerFor]="sortMenu"
                      [appIconLabel]="('guess.sort.label' | translate) + ': ' + (('guess.sort.' + sort) | translate)">
                <mat-icon>swap_vert</mat-icon>
              </button>
              <mat-menu #sortMenu="matMenu">
                @for (o of sortOptions; track o) {
                  <button mat-menu-item class="sort-option" (click)="setSort(o)" [attr.aria-checked]="sort === o" role="menuitemradio">
                    <mat-icon>{{ sort === o ? 'radio_button_checked' : 'radio_button_unchecked' }}</mat-icon>
                    {{ ('guess.sort.' + o) | translate }}
                  </button>
                }
              </mat-menu>
            </div>
            @if (curatedShown.length === 0) {
              <p class="muted">{{ (query ? 'guess.search.none' : 'guess.noCurated') | translate }}</p>
            } @else {
              <!-- Seitenweise statt alle auf einmal: 655 Zeilen waren am Handy 85 000 px
                   (Codereview W5 UX-016). Die Metazeile ist EIN Kasten, damit der Knopf am Handy
                   immer rechts in der zweiten Zeile steht — mit losen Chips sprang er je nach
                   Zeile nach links unten. -->
              @for (g of curatedVisible; track g.id) {
                <div class="game-row">
                  <span class="g-title">{{ g.title || ('guess.untitled' | translate) }}</span>
                  <span class="g-meta">
                    <span class="muted small">{{ 'guess.moves' | translate:{ moves: moveCount(g) } }}</span>
                    @if (g.guessWhite !== null && g.guessWhite !== undefined) {
                      <span class="side" [class.side-black]="!g.guessWhite">
                        {{ (g.guessWhite ? 'guess.white' : 'guess.black') | translate }}
                      </span>
                    }
                    @if (g.annotated) {
                      <span class="chip">{{ 'guess.annotatedBadge' | translate }}</span>
                    }
                  </span>
                  <span class="spacer"></span>
                  <button mat-stroked-button class="g-play" [disabled]="starting" (click)="start(g)"
                          [attr.aria-label]="'guess.play' | translate">
                    <mat-icon>play_arrow</mat-icon> <span class="g-play-label">{{ 'guess.play' | translate }}</span>
                  </button>
                </div>
              }
              @if (curatedRemaining > 0) {
                <button mat-stroked-button class="more" (click)="showMore()">
                  {{ 'guess.showMore' | translate:{ count: nextPageSize } }}
                </button>
              }
            }
          </mat-card-content>
        </mat-card>
        </ng-template>

        <ng-template #ownList>
          <mat-card class="start-card">
            <mat-card-content>
              <div class="sec-head">
                <ng-container *ngTemplateOutlet="searchField" />
              </div>

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

              @if (ownShown.length === 0) {
                <p class="muted">{{ (query ? 'guess.search.none' : 'guess.noGames') | translate }}</p>
                @if (!query) {
                  <a mat-stroked-button routerLink="/analysis/games">{{ 'guess.analyseFirst' | translate }}</a>
                }
              } @else {
                <div class="side-pick">
                  <span class="muted small">{{ 'guess.sideLabel' | translate }}</span>
                  <mat-button-toggle-group [(ngModel)]="guessWhite" [attr.aria-label]="'guess.sideLabel' | translate">
                    <mat-button-toggle [value]="true">{{ 'guess.white' | translate }}</mat-button-toggle>
                    <mat-button-toggle [value]="false">{{ 'guess.black' | translate }}</mat-button-toggle>
                  </mat-button-toggle-group>
                </div>
                @for (g of ownShown; track g.id) {
                  <div class="game-row">
                    <span class="g-title">{{ g.title || ('guess.untitled' | translate) }}</span>
                    <span class="g-meta">
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
                    </span>
                    <span class="spacer"></span>
                    <button mat-stroked-button class="g-play" [disabled]="starting || g.analyzedPlies === 0"
                            [matTooltip]="g.analyzedPlies === 0 ? ('guess.notReady' | translate) : ''"
                            (click)="start(g, guessWhite)" [attr.aria-label]="'guess.play' | translate">
                      <mat-icon>play_arrow</mat-icon> <span class="g-play-label">{{ 'guess.play' | translate }}</span>
                    </button>
                  </div>
                  @if (g.status === 'pending' || g.status === 'running') {
                    <mat-progress-bar mode="determinate" [value]="percent(g)" />
                  }
                }
              }
            </mat-card-content>
          </mat-card>
        </ng-template>

        @if (finishedSessions.length) {
          <h2 class="sec">{{ 'guess.yourRuns' | translate }}</h2>
          @for (s of finishedSessions; track s.id) {
            <mat-card class="run">
              <mat-card-content>
                <div class="run-row">
                  <a class="g-title" [routerLink]="['/guess', s.id]">{{ s.title || ('guess.untitled' | translate) }}</a>
                  <span class="muted small">{{ (s.guessWhite ? 'guess.white' : 'guess.black') | translate }}</span>
                  <span class="pts">{{ 'guess.score' | translate:{ points: s.points, max: s.maxPoints } }}</span>
                  <span class="spacer"></span>
                  <span class="chip">{{ ('guess.status.' + s.status) | translate }}</span>
                  <button mat-icon-button
                          [appIconLabel]="'common.delete' | translate" (click)="remove(s)">
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
    /* Filter und Sortieren in EINER Zeile, auch am Handy (d2-rk-guess-vorlauf). */
    /* z1-rk-guess-vorlauf: Suche, Filter und ⇅ in EINER Zeile; am Handy darf sie notfalls umbrechen. */
    .sec-head { display: flex; align-items: center; gap: 8px 10px; flex-wrap: wrap; margin-bottom: 8px; }
    .sec-head .search { flex: 1 1 140px; min-width: 120px; width: auto; margin: 0; }
    .sec-head .count { white-space: nowrap; }
    .sort-btn { flex: none; margin-left: auto; }
    .sort-btn.active { color: var(--mat-sys-primary); }
    .more { display: block; margin: 12px auto 4px; }
    .small-toggle { font-size: .8rem; }
    .side-pick { display: flex; align-items: center; gap: 10px; margin-bottom: 10px; flex-wrap: wrap; }
    .full { width: 100%; }
    .search { width: 100%; }
    .header-actions { display: flex; gap: 8px; flex-wrap: wrap; }
    /* Die Karte sitzt IM Reiter — ohne das doppelte sich der Rahmen mit dem des Reiters. */
    .lists .start-card { box-shadow: none; background: transparent; margin: 0; }
    .lists ::ng-deep .mat-mdc-tab-body-content { padding-top: 8px; }
    .lists .start-card .mat-mdc-card-content { padding-top: 0; }
    .lists.single .start-card .mat-mdc-card-content { padding-left: 0; padding-right: 0; }
    .search mat-icon[matPrefix] { margin-right: 8px; opacity: .6; }
    .upload-row { display: flex; align-items: center; gap: 12px; flex-wrap: wrap; margin-bottom: 12px; }
    .chip.err { color: var(--rh-error); }
    /* Zwei Kanaele, nicht nur Farbe: der Punkt davor ist gefuellt bzw. hohl. */
    .side { font-size: .72rem; padding: 2px 8px 2px 6px; border-radius: 10px;
            border: 1px solid color-mix(in srgb, currentColor 35%, transparent); white-space: nowrap; }
    .side::before { content: '●'; margin-right: 5px; }
    .side-black::before { content: '○'; }
    mat-progress-bar { margin: 0 0 8px; border-radius: 3px; }
    .game-row, .run-row { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; padding: 6px 0; }
    .game-row + .game-row { border-top: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .g-meta { display: inline-flex; align-items: center; gap: 10px; flex-wrap: wrap; }
    /* Am Handy ein festes Raster: Titel ueber die volle Breite, darunter Metazeile links und
       Knopf rechts — in JEDER Zeile an derselben Stelle (Codereview W5 UX-016). */
    /* UI-Sweep 2026-10-11 (r2-rg-guess-rows): Knopf und Chips getrennt — der Spielen-Knopf (nur ▶) steht rechts neben
       dem Titel, die Metazeile darunter über die volle Breite und darf umbrechen. Vorher rutschte „kommentiert" neben
       „Schwarz" in eine eigene Zeile und zog den Knopf mittig, die Zeilenhöhen wechselten. */
    @media (max-width: 599.98px) {
      .game-row { display: grid; grid-template-columns: minmax(0, 1fr) auto; grid-template-areas: 'title play' 'meta meta';
                  gap: 4px 10px; align-items: start; }
      .game-row .g-title { grid-area: title; align-self: center; }
      .game-row .g-meta { grid-area: meta; gap: 4px 8px; }
      .game-row .g-play { grid-area: play; min-width: 44px; padding: 0 10px; }
      .game-row .spacer { display: none; }
    }
    /* „Spielen" bleibt beschriftet; erst ganz schmal nur ▶ (DESIGN 5.4, z1-rk-guess-spielen-label). */
    @media (max-width: 380px) {
      .game-row .g-play mat-icon { margin: 0; }
      .game-row .g-play-label { display: none; }
    }
    .g-title { font-weight: 600; text-decoration: none; color: inherit; }
    a.g-title:hover { text-decoration: underline; }
    .spacer { flex: 1 1 auto; }
    .pts { font-variant-numeric: tabular-nums; }
    .chip { font-size: .72rem; padding: 2px 8px; border-radius: 10px; border: 1px solid currentColor;
            color: color-mix(in srgb, currentColor 65%, transparent); }
    .run { margin-bottom: 8px; }
    /* Abgeschlossene Durchläufe am Inhaltsrand der Liste (die Liste sitzt ohne Rahmen 16 px eingerückt im Reiter). */
    h2.sec, .run:not(.resume) { margin-left: 16px; margin-right: 16px; }
    .resume { border-left: 4px solid var(--mat-sys-primary); }
    .resume-label { font-weight: 600; }
    .resume .mat-mdc-card-content { padding-top: 6px; padding-bottom: 6px; }
    .resume-row { flex-wrap: nowrap; gap: 8px; padding: 2px 0; }
    .resume-main { flex: 1 1 0; min-width: 0; display: flex; flex-direction: column; }
    .resume .g-title { white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .resume-btn { flex: none; }
    @media (max-width: 599.98px) {
      .resume-btn { min-width: 44px; padding: 0 10px; border-radius: 50%; }
      .resume-btn mat-icon { margin: 0; }
      .resume-btn-label { display: none; }
    }
    /* Handy: Titel + drei Symbolknöpfe in einer Zeile, Beschriftungen nur als Tooltip/aria-label. */
    @media (max-width: 599.98px) {
      .header { flex-wrap: nowrap; }
      .header-actions { flex-wrap: nowrap; gap: 4px; }
      .header-actions .ha-label { display: none; }
      .header-actions .mat-mdc-button-base { min-width: 44px; padding: 0 10px; }
      .header-actions .mat-mdc-button-base mat-icon { margin: 0; }
      .small-toggle .mat-button-toggle { font-size: .75rem; }
    }
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
  private dialog = inject(MatDialog);
  private snackbar = inject(SnackbarService);
  private translate = inject(TranslateService);
  private cdr = inject(ChangeDetectorRef);
  private confirm = inject(ConfirmService);

  sessions: GuessSession[] = [];
  /** Angefangene Durchläufe — stehen als „Weiterspielen" über der Suche. */
  get runningSessions(): GuessSession[] { return this.sessions.filter(s => s.status === 'running'); }
  /** Beendete Durchläufe — bleiben unten unter „Deine Durchläufe". */
  get finishedSessions(): GuessSession[] { return this.sessions.filter(s => s.status !== 'running'); }
  /** Absatztrenner für zusammengesetzte Hilfetexte. */
  readonly paraSep = '\n\n';
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

  /**
   * Freitext-Suche ueber BEIDE Listen — Titel, Namen, Turnier.
   *
   * <p>Sie filtert im BROWSER, wie der Kommentar-Filter daneben: der Bestand ist eine ueberschaubare
   * Bibliothek, die ohnehin schon vollstaendig ausgeliefert ist, und ein Server-Umlauf je getipptem
   * Buchstaben waere dafuer zu viel. Waechst er auf Tausende, gehoert die Suche auf den Server —
   * dann aber mit Seiten, nicht nur mit einem Filter.</p>
   */
  query = '';

  /** Welcher Reiter offen ist: 0 = Meisterpartien, 1 = eigene Partien. */
  tab = 0;

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
    const c = this.shownCache;
    if (c && c.src === this.curated && c.annotatedOnly === this.annotatedOnly
        && c.query === this.query && c.sort === this.sort) return c.out;
    const pool = this.annotatedOnly ? this.curated.filter(g => g.annotated) : this.curated;
    const out = GuessListComponent.sorted(this.filtered(pool), this.sort);
    this.shownCache = { src: this.curated, annotatedOnly: this.annotatedOnly, query: this.query, sort: this.sort, out };
    return out;
  }

  get ownShown(): GameAnalysis[] {
    return this.filtered(this.ownGames);
  }

  /** Wie viele Meisterpartien je Seite stehen — und um wie viele „mehr anzeigen" erweitert. */
  static readonly PageSize = 50;
  readonly sortOptions: readonly GuessSort[] = ['title', 'short', 'long', 'event'];
  /** Reihenfolge des Bestands; `title` = wie der Server liefert. Bewusst nicht gemerkt. */
  sort: GuessSort = 'title';
  /** Wie viele Zeilen des Bestands gerade gerendert werden. */
  curatedLimit = GuessListComponent.PageSize;

  private shownCache?: { src: GameAnalysis[]; annotatedOnly: boolean; query: string; sort: GuessSort; out: GameAnalysis[] };
  private visibleCache?: { src: GameAnalysis[]; limit: number; out: GameAnalysis[] };

  /** Die gerade gerenderte Seite des Bestands. Zwischengespeichert, weil die Vorlage sie bei jeder
   *  Aenderungserkennung liest (Default-CD, 655 Eintraege). */
  get curatedVisible(): GameAnalysis[] {
    const shown = this.curatedShown;
    const c = this.visibleCache;
    if (c && c.src === shown && c.limit === this.curatedLimit) return c.out;
    const out = shown.slice(0, this.curatedLimit);
    this.visibleCache = { src: shown, limit: this.curatedLimit, out };
    return out;
  }

  /** Wie viele Partien hinter der aktuellen Seite noch kommen. */
  get curatedRemaining(): number {
    return Math.max(0, this.curatedShown.length - this.curatedLimit);
  }

  /** So viele kaemen mit dem naechsten Klick dazu. */
  get nextPageSize(): number {
    return Math.min(GuessListComponent.PageSize, this.curatedRemaining);
  }

  showMore(): void {
    this.curatedLimit += GuessListComponent.PageSize;
  }

  /** Suche, Filter und Sortierung zeigen eine ANDERE Liste — die beginnt wieder auf Seite eins. */
  private resetPaging(): void {
    this.curatedLimit = GuessListComponent.PageSize;
  }

  setQuery(q: string): void {
    this.query = q ?? '';
    this.resetPaging();
  }

  setSort(sort: GuessSort): void {
    this.sort = sort;
    this.resetPaging();
  }

  /** Vom Umschalter „alle / nur kommentierte" aufgerufen. */
  onFilterChange(): void {
    this.resetPaging();
    this.saveFilter();
  }

  private static sorted(games: GameAnalysis[], sort: GuessSort): GameAnalysis[] {
    switch (sort) {
      case 'short': return [...games].sort((a, b) => a.plyCount - b.plyCount);
      case 'long': return [...games].sort((a, b) => b.plyCount - a.plyCount);
      // Ohne Turnier ans Ende; innerhalb eines Turniers bleibt die Reihenfolge des Servers (stabil).
      case 'event': return [...games].sort((a, b) =>
        !a.event ? (!b.event ? 0 : 1) : !b.event ? -1 : a.event.localeCompare(b.event));
      default: return games;
    }
  }

  private filtered(games: GameAnalysis[]): GameAnalysis[] {
    const needle = GuessListComponent.fold(this.query);
    if (!needle) return games;
    return games.filter(g => GuessListComponent.haystack(g).includes(needle));
  }

  /** Gesucht wird in dem, was auf der Zeile steht, PLUS den Namen und dem Turnier: der Titel einer
   *  eingeworfenen Partie kann „Partie" heissen, die Spieler stehen trotzdem in den Kopfdaten. */
  private static haystack(g: GameAnalysis): string {
    return GuessListComponent.fold([g.title, g.white, g.black, g.event].filter(Boolean).join(' '));
  }

  /**
   * Kleinschreibung und Umlaute auf den Grundvokal.
   *
   * <p>Ohne das Falten findet „Zurich" das „Zuerich" im Bestand nicht und „Munchen" kein
   * „München" — die Partien kommen aus verschiedenen Quellen und schreiben dieselben Namen
   * verschieden.</p>
   *
   * <p>Gefaltet werden BEIDE Seiten, und nur deshalb ist das hier unbedenklich: „Quedlinburg" wird
   * zu „qudlinburg", die Eingabe „Qued" aber genauso zu „qud" — die Teilzeichenkette passt weiter.
   * Im Ortslexikon des Turnierkalenders steht ausdruecklich das Gegenteil (dort wird beim SUCHEN
   * nicht gefaltet), weil dort gegen eine VORNORMALISIERTE Spalte gesucht wird und die Faltung
   * damit einseitig waere.</p>
   */
  private static fold(text: string | null | undefined): string {
    return (text ?? '')
      .toLowerCase()
      .normalize('NFD')
      .replace(/[\u0300-\u036f]/g, '')   // Akzente weg (é → e)
      .replace(/ae/g, 'a').replace(/oe/g, 'o').replace(/ue/g, 'u').replace(/ss/g, 's')
      .trim();
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

  /**
   * Den Rohbestand oeffnen: 130 000 kommentierte Meisterpartien, durchsuchbar, einzeln anforderbar.
   *
   * <p>Der Dialog gibt eine Analyse-Id zurueck, wenn die gewaehlte Partie schon spielbar ist —
   * dann geht es direkt in die Punktepartie. Sonst wurde etwas in die Warteschlange gestellt, und
   * die eigene Liste muss neu geholt werden, damit die neue Partie dort auftaucht.</p>
   */
  openLibrary(): void {
    import('./library-dialog.component').then(m => {
      const ref = this.dialog.open(m.LibraryDialogComponent, { maxWidth: '96vw' });
      ref.afterClosed().subscribe((analysisId?: number) => {
        if (analysisId) this.start({ id: analysisId } as GameAnalysis);
        else if (this.loggedIn) this.loadOwnGames();   // ohne Konto gibt es keine eigenen Partien
      });
    });
  }

  /**
   * „Nach Stellung filtern": Brett plus Eroeffnungsbaum. Die Namenssuche daneben beantwortet „ich
   * weiss, wie die Partie heisst" — diese Frage ist die andere, die man vor dem Ueben stellt.
   */
  openPositionFilter(): void {
    import('./position-filter-dialog.component').then(m => {
      const ref = this.dialog.open(m.PositionFilterDialogComponent, { maxWidth: '96vw' });
      ref.afterClosed().subscribe((analysisId?: number) => {
        if (analysisId) this.start({ id: analysisId } as GameAnalysis);
        else this.loadOwnGames();
      });
    });
  }

  /** Der Lauf geht samt Zuegen und Punkten endgueltig weg — darum erst nach Ja (Codereview W5 F4-013). */
  remove(s: GuessSession): void {
    const title = s.title || this.translate.instant('guess.untitled');
    this.confirm.ask('guess.deleteConfirm', { title }).subscribe(ok => {
      if (!ok) return;
      this.guess.delete(s.id).subscribe({
        next: () => { this.sessions = this.sessions.filter(x => x.id !== s.id); this.cdr.markForCheck(); },
        error: () => this.snackbar.warn(this.translate.instant('guess.deleteFailed')),
      });
    });
  }
}
