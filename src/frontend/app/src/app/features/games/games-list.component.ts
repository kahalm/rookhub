import { Component, OnInit, inject, DestroyRef, ChangeDetectionStrategy, signal } from '@angular/core';
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
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { TranslatePipe, TranslateService } from '@ngx-translate/core';
import { Subscription, timer } from 'rxjs';
import { switchMap } from 'rxjs/operators';
import { GamesService, PgnImportResult, SavedGame } from './games.service';
import { PgnImportDialogComponent } from './pgn-import-dialog.component';
import { formatTimeControl, TimeControlLabel } from './time-control.util';
import { distinctTags, filterByTag } from './tags.util';
import { distinctClassifiers, filterByClassifiers, hasUnclassified, NO_CLASSIFIER } from './classifier.util';
import { classifierToParam } from './mistakes-collection.util';
import { AnalyzeGameService } from './analyze-game.service';
import { gameSourceIcon, gameSourceLabel } from './game-source';
import { GuessUploadStatus } from '../analysis/game-analysis.service';
import { SnackbarService } from '../../core/snackbar.service';
import { ConfirmService } from '../../shared/confirm-dialog/confirm-dialog.component';
import { ScoresheetService, openPhotoBlob, photoFileName } from './scoresheet.service';
import { GameRoastData, GameRoastDialogComponent } from './game-roast-dialog.component';
import { ScoresheetPhotoDialogComponent } from './scoresheet-photo-dialog.component';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { PctPipe, formatPct } from '../../shared/format/pct';
import { HelpHintComponent } from '../../shared/help-hint/help-hint.component';
import { downloadBlob } from '../../shared/download.util';
import { pgnFileName } from '../../shared/pgn-export.util';

/** Solange eine Analyse läuft, holt die Liste alle zehn Sekunden den Stand — derselbe Takt wie die Kurve. */
export const ANALYSIS_POLL_MS = 10_000;

/** Was die Liste zu einer Partie zeigt: Knopf (keine/gescheiterte Analyse), Prozent (läuft) oder nichts (fertig). */
export type AnalysisState = 'none' | 'running' | 'done';

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-games-list',
  standalone: true,
  imports: [IconLabelDirective,
    CommonModule, FormsModule, RouterLink, MatCardModule, MatButtonModule, MatIconModule, MatTooltipModule,
    MatProgressSpinnerModule, MatCheckboxModule, MatMenuModule, MatDialogModule, MatFormFieldModule, MatSelectModule,
    TranslatePipe, PctPipe, HelpHintComponent,
  ],
  template: `
    <div class="games-page">
      <div class="page-head">
        <!-- Seitenkopf nach DESIGN 5.1: Titel links, Aktionen gruppiert rechts (d2-rk-games-kopfaktionen). -->
        <div class="header">
          <!-- d5-rk-games-erster-blick-vorlauf: in niedrigen Fenstern (Boox 200 %) steht der Erklärsatz im ⓘ am Titel
               (DESIGN 4.6 „Erklärsatz im ⓘ am Titel“) — nur bei gefüllter Liste; im Leerzustand bleibt er sichtbar
               (er nennt den RepCheck-Weg). -->
          <h1>{{ 'games.title' | translate }}@if (games.length > 0) { <app-help-hint class="hint-i" icon="info_outline" [text]="'games.hint' | translate" />}</h1>
          <div class="header-actions">
            <!-- d3-rk-games-leer-pgn-doppelt: im Leerzustand steht die eine Aktion in der Leerkarte (DESIGN 5.10). -->
            @if (loading || games.length > 0) {
              <button mat-stroked-button class="pgn-upload" (click)="uploadPgn()">
                <mat-icon>upload_file</mat-icon>
                <!-- Am Handy Kurztext, damit beide Kopfknöpfe in eine Zeile passen (v1-rk-games-mobile-fold-lead-filters-open). -->
                <span class="label-long">{{ 'games.pgnUpload.button' | translate }}</span>
                <span class="label-short" aria-hidden="true">{{ 'games.pgnUpload.short' | translate }}</span>
              </button>
            }
            <!-- Fehler mehrerer Partien am Stück (0.748.0) — übernimmt die gewählte Liga/Saison. -->
            @if (games.length > 0) {
              <a mat-stroked-button class="mistakes-collection" routerLink="/games/mistakes" [queryParams]="collectionParams()">
                <mat-icon>replay</mat-icon>
                <span class="label-long">{{ 'games.mistakes.collection.button' | translate }}</span>
                <span class="label-short" aria-hidden="true">{{ 'games.mistakes.collection.short' | translate }}</span>
              </a>
            }
          </div>
        </div>
        <p class="hint" [class.has-games]="games.length > 0">{{ 'games.hint' | translate }}</p>
        <!-- „Wo liegt noch Arbeit?" — der Filter zeigt nur Partien mit offenen Fehlern. Er erscheint erst,
             wenn es überhaupt welche gibt, sonst stünde ein Schalter da, der nichts tut. -->
        @if (withOpenMistakes() > 0) {
          <mat-checkbox class="only-open" [(ngModel)]="onlyOpen" name="onlyOpen">
            {{ 'games.mistakes.onlyOpen' | translate: { count: withOpenMistakes() } }}
          </mat-checkbox>
        }
        <!-- Zwei Klassifizierer (0.661.0): Online-Partien Seite + Modus, Ligapartien Liga + Jahrgang. Ein Filter je
             Klassifizierer, erst wenn es überhaupt Werte gibt — sonst stünde ein Schalter da, der nichts tut. -->
        @if (firstOptions().length > 0 || secondOptions().length > 0 || tagOptions().length > 0) {
          <!-- Am Handy (≤ 600 px) liegen die Filter hinter „Filter ▾" (zu als Vorgabe); gesetzte Filter stehen als Chips
               mit ✕ daneben (v1-rk-games-mobile-fold-lead-filters-open, DESIGN 4.6 Regel 7). Am PC immer offen. -->
          <div class="filters-bar">
            <button type="button" mat-stroked-button class="filters-toggle" [attr.aria-expanded]="filtersOpen()"
                    (click)="filtersOpen.set(!filtersOpen())">
              {{ 'games.filtersToggle' | translate }} {{ filtersOpen() ? '▴' : '▾' }}
            </button>
            @if (!filtersOpen()) {
              @for (c of activeFilters(); track c.which) {
                <button type="button" class="filter-chip" (click)="clearFilter(c.which)"
                        [attr.aria-label]="'games.filterRemove' | translate: { value: c.label }">
                  {{ c.label }} <span aria-hidden="true">✕</span>
                </button>
              }
            }
          </div>
          <!-- a1-rk-games-native-auswahl: Filter als Material-Felder wie auf /games/mistakes (DESIGN 5.8), nicht nativ. -->
          <div class="classifier-filters" [class.open]="filtersOpen()">
            @if (firstOptions().length > 0) {
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="filter-field">
                <mat-label>{{ 'games.classifier.first' | translate }}</mat-label>
                <mat-select [(ngModel)]="filter1" name="filter1" class="filter1">
                  <mat-option value="">{{ 'games.classifier.all' | translate }}</mat-option>
                  @for (v of firstOptions(); track v) { <mat-option [value]="v">{{ v }}</mat-option> }
                  @if (unclassified1()) { <mat-option [value]="none">{{ 'games.classifier.none' | translate }}</mat-option> }
                </mat-select>
              </mat-form-field>
            }
            @if (secondOptions().length > 0) {
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="filter-field">
                <mat-label>{{ 'games.classifier.second' | translate }}</mat-label>
                <mat-select [(ngModel)]="filter2" name="filter2" class="filter2">
                  <mat-option value="">{{ 'games.classifier.all' | translate }}</mat-option>
                  @for (v of secondOptions(); track v) { <mat-option [value]="v">{{ v }}</mat-option> }
                  @if (unclassified2()) { <mat-option [value]="none">{{ 'games.classifier.none' | translate }}</mat-option> }
                </mat-select>
              </mat-form-field>
            }
            @if (tagOptions().length > 0) {
              <mat-form-field appearance="outline" subscriptSizing="dynamic" class="filter-field">
                <mat-label>{{ 'games.tags.label' | translate }}</mat-label>
                <mat-select [(ngModel)]="filterTag" name="filterTag" class="filter-tag">
                  <mat-option value="">{{ 'games.classifier.all' | translate }}</mat-option>
                  @for (v of tagOptions(); track v) { <mat-option [value]="v">{{ v }}</mat-option> }
                </mat-select>
              </mat-form-field>
            }
          </div>
        }
      </div>

      @if (loading) {
        <div class="center"><mat-spinner diameter="40"></mat-spinner></div>
      } @else if (games.length === 0) {
        <mat-card class="empty-state">
          <mat-icon>sports_esports</mat-icon>
          <!-- a1-rk-games-leer-zwei-saetze: ein Satz, eine Aktion (DESIGN 5.10); der RepCheck-Weg steht im .hint darüber. -->
          <p>{{ 'games.empty' | translate }}</p>
          <button mat-stroked-button class="empty-upload" (click)="uploadPgn()">
            <mat-icon>upload_file</mat-icon> {{ 'games.pgnUpload.button' | translate }}
          </button>
        </mat-card>
      } @else {
        <!-- Tabelle im Schnitt der chess.com-Uebersicht (gewuenscht 24.09.2026): Spieler, Ergebnis,
             Genauigkeit, Zuege, Datum — genau die Spalten, die der Nutzer dort vor sich hat. Die
             Kopfzeile ist am Handy weg, dort steht dieselbe Zeile umbrochen. -->
        <!-- d3-rk-games-analyse-symbol-blass: ohne Engine einmal sichtbar der Grund statt eines grauen, gesperrten Knopfs
             in jeder Zeile, dessen Grund nur im Tooltip stand (am Handy nie zu sehen; DESIGN 5.4). -->
        <!-- d4-rk-games-engine-hinweis-jargon/-engine-hinweis-jargon/-liste-engine-hinweis-verwaist/-engine-hint-unter-filter:
             eigener Satz statt des Punktepartie-Texts („eingeworfene Partien“), der sagt, WAS fehlt (die Genauigkeit,
             Spalte „–“); als Info-Zeile mit Symbol und Abstand zu den Filtern, damit er nicht wie deren Beschriftung wirkt. -->
        @if (uploadStatus?.engineAvailable === false) {
          <p class="no-engine" id="games-no-engine"><mat-icon aria-hidden="true">info_outline</mat-icon><span>{{ 'games.listNoEngine' | translate }}</span></p>
        }
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
                <mat-icon class="src" [matTooltip]="sourceLabel(g.source)">{{ sourceIcon(g.source) }}</mat-icon>
                @if (timeControl(g); as tc) { <span class="tc">{{ 'games.tc.' + tc.key | translate: tc.params }}</span> }
              </div>
              <!-- Die Namen führen auf die Partie-SEITE (/games/:id) — kein Dialog mehr (gemeldet 2026-09-23). -->
              <a class="players" [routerLink]="['/games', g.id]">
                <span class="p"><i class="dot white"></i><span class="name">{{ g.white || '?' }}</span>@if (g.whiteElo) { <span class="elo">({{ g.whiteElo }})</span> }</span>
                <span class="p"><i class="dot black"></i><span class="name">{{ g.black || '?' }}</span>@if (g.blackElo) { <span class="elo">({{ g.blackElo }})</span> }</span>
                @if (g.classifier1 || g.classifier2 || g.tags?.length) {
                  <span class="classifiers">
                    @if (g.classifier1) { <span class="chip">{{ g.classifier1 }}</span> }
                    @if (g.classifier2) { <span class="chip">{{ g.classifier2 }}</span> }
                    @for (t of g.tags ?? []; track t) { <span class="chip tag">#{{ t }}</span> }
                  </span>
                }
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
                      <i class="sep" aria-hidden="true">·</i>
                      <span>♚ {{ pct(g.analysis?.accuracyBlack) }}</span>
                    </span>
                  }
                  @case ('running') {
                    <!-- Statt des Knopfs der Fortschritt — er läuft mit dem 10-s-Nachfragen mit. -->
                    <span class="progress" [matTooltip]="progressTip(g)">{{ progressPercent(g) | pct }}</span>
                  }
                  @case ('none') {
                    <!-- Derselbe Weg wie auf der geteilten Partie: rechnen lassen, die Kurve steht danach auf der Partie-Seite. -->
                    @if (uploadStatus?.engineAvailable === false) {
                      <span class="no-analysis" [matTooltip]="'games.listNoEngine' | translate">–</span>
                    } @else {
                      <button mat-icon-button class="analyze" (click)="analyze(g)" [disabled]="analyzingId === g.id"
                              [appIconLabel]="'games.analyze' | translate">
                        <mat-icon>{{ analyzingId === g.id ? 'hourglass_top' : 'insights' }}</mat-icon>
                      </button>
                    }
                  }
                }
              </div>
              <!-- Am Handy fehlt die Spaltenüberschrift — dort steht die Zahl mit „Züge" dahinter. -->
              <div class="moves mid"><span class="moves-num">{{ g.moveCount }}</span><span class="moves-label">{{ 'games.movesCount' | translate:{ count: g.moveCount } }}</span></div>
              <div class="date">{{ (g.playedAt || g.createdAt) | date:'mediumDate' }}</div>
              <div class="actions">
                <a mat-icon-button [routerLink]="['/games', g.id]" [appIconLabel]="'games.replay' | translate">
                  <mat-icon>play_arrow</mat-icon>
                </a>
                <!-- Alles Weitere ins ⋮: eine Zeile mit sechs Knöpfen liest sich nicht mehr. -->
                <button mat-icon-button [matMenuTriggerFor]="menu"
                        [appIconLabel]="'common.moreActions' | translate">
                  <mat-icon>more_vert</mat-icon>
                </button>
                <mat-menu #menu="matMenu">
                  <!-- d3-rk-spielmenue-reihenfolge-uneinheitlich (DESIGN 5.5): Öffnen/Bearbeiten → Aktionen → Teilen/Export →
                       Löschen, wie das ⋮ der Partieseite. -->
                  <button mat-menu-item (click)="openInAnalysis(g)">
                    <mat-icon>biotech</mat-icon><span>{{ 'games.openInAnalysis' | translate }}</span>
                  </button>
                  @if (g.sourceUrl) {
                    <a mat-menu-item [href]="g.sourceUrl" target="_blank" rel="noopener">
                      <mat-icon>open_in_new</mat-icon><span>{{ 'games.openOriginal' | translate }}</span>
                    </a>
                  }
                  <a mat-menu-item [routerLink]="['/games', g.id, 'edit']">
                    <mat-icon>edit_note</mat-icon><span>{{ 'games.edit.menu' | translate }}</span>
                  </a>
                  <button mat-menu-item (click)="roast(g)">
                    <mat-icon>local_fire_department</mat-icon><span>{{ 'games.roast.menu' | translate }}</span>
                  </button>
                  <button mat-menu-item (click)="share(g)">
                    <mat-icon>share</mat-icon><span>{{ 'games.share' | translate }}</span>
                  </button>
                  <!-- d4-rk-partie-menue-loeschen-fehlt-partieseite: dieselben Einträge wie das ⋮ der Partieseite (DESIGN 5.5). -->
                  <button mat-menu-item class="pgn-copy" (click)="copyPgn(g)">
                    <mat-icon>content_copy</mat-icon><span>{{ 'games.pgnCopy' | translate }}</span>
                  </button>
                  <button mat-menu-item class="pgn-download" (click)="downloadPgn(g)">
                    <mat-icon>file_download</mat-icon><span>{{ 'common.downloadPgn' | translate }}</span>
                  </button>
                  <!-- Partieformular (0.529.0): das Foto bleibt an der Partie; korrigieren geht bei jeder Partie. -->
                  @if (g.scanId) {
                    <button mat-menu-item (click)="photo(g, false)">
                      <mat-icon>image</mat-icon><span>{{ 'games.photo.show' | translate }}</span>
                    </button>
                    <button mat-menu-item (click)="photo(g, true)">
                      <mat-icon>download</mat-icon><span>{{ 'games.photo.download' | translate }}</span>
                    </button>
                  }
                  <!-- b1-rk-games-menue-loeschen-rot: rot über .delete-item (DESIGN 5.5) — color="warn" wirkt im M3-Thema nicht. -->
                  <button mat-menu-item class="delete-item" (click)="remove(g)">
                    <mat-icon>delete</mat-icon><span>{{ 'common.delete' | translate }}</span>
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
    /* a1-rk-games-ultrawide-schmal: Tabellenseite in der gemeinsamen Seitenbreite, ab 2400 px breiter (DESIGN 4.1). */
    .games-page { max-width: min(var(--page-max-width), 96vw); margin: 0 auto; padding: 16px; }
    .header { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; margin-bottom: 4px; }
    .header h1 { margin: 0; }
    /* ⓘ mit dem Erklärsatz nur in niedrigen Fenstern (Block unten), sonst steht der Satz darunter. */
    .hint-i { display: none; }
    .header-actions { display: flex; gap: .5rem; flex-wrap: wrap; }
    .hint { color: color-mix(in srgb, currentColor 60%, transparent); margin: 0 0 16px; font-size: 0.9rem; text-wrap: pretty; }
    .center { display: flex; justify-content: center; padding: 40px; }
    .empty-state { display: flex; flex-direction: column; align-items: center; gap: 8px; padding: 32px; text-align: center; }
    .empty-state > mat-icon { font-size: 40px; width: 40px; height: 40px; opacity: 0.5; }
    .only-open { margin-top: 4px; }
    .classifier-filters { display: flex; flex-wrap: wrap; align-items: center; gap: 8px 16px; margin-top: 8px; }
    .filter-field { width: 200px; max-width: 100%; }
    .filters-bar { display: none; }
    .label-short { display: none; }
    @media (max-width: 600px) {
      /* a1-rk-games-handy-filter-abstand: 12 px Luft zur ersten Partie (DESIGN 3.2), nicht 8 px wie im Zeilenraster. */
      .filters-bar { display: flex; flex-wrap: wrap; align-items: center; gap: 6px; margin: 8px 0 12px; }
      .classifier-filters:not(.open) { display: none; }
      .classifier-filters.open { margin: 0 0 12px; }
      .filter-field { width: auto; flex: 1 1 100%; }
      .filter-chip { font: inherit; font-size: 0.8rem; color: inherit; cursor: pointer; min-height: 32px; padding: 2px 10px;
        border: 0; border-radius: 16px; background: color-mix(in srgb, currentColor 10%, transparent); }
      .label-long { display: none; }
      .label-short { display: inline; }
    }
    /* Niedrige Fenster (Boox 200 % = 480 × 640, DESIGN 4.6 „Niedrige Fenster“): Abstände vor der Liste 4–8 px statt
       12–16 px — die erste Partie begann bei 45 % der Höhe. */
    @media (max-width: 768px) and (max-height: 720px) {
      .games-page { padding-top: 8px; }
      .hint { margin-bottom: 8px; }
      .filters-bar { margin: 4px 0 8px; }
      /* d5-rk-games-erster-blick-vorlauf: erste Partie bei 44 % — Erklärsatz ins ⓘ am Titel, Engine-Zeile knapper. */
      .hint.has-games { display: none; }
      .hint-i { display: inline-flex; }
    }
    .classifiers { display: flex; flex-wrap: wrap; gap: 4px; margin-top: 3px; }
    .chip.tag { background: color-mix(in srgb, var(--rh-info, #1976d2) 14%, transparent); }
    .chip { font-size: 0.72rem; line-height: 1; padding: 3px 7px; border-radius: 10px;
      background: color-mix(in srgb, currentColor 10%, transparent); color: color-mix(in srgb, currentColor 75%, transparent); }

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
    .no-engine { display: flex; align-items: center; gap: 8px; margin: 12px 0 8px; font-size: 0.875rem;
      color: var(--mat-sys-on-surface-variant); }
    .no-engine mat-icon { flex: none; font-size: 18px; width: 18px; height: 18px; }
    @media (max-width: 768px) and (max-height: 720px) {
      .no-engine { margin: 4px 0; font-size: .8125rem; }
    }
    .muted { color: color-mix(in srgb, currentColor 60%, transparent); }
    .small { font-size: 0.82rem; }
    .no-analysis { color: color-mix(in srgb, currentColor 45%, transparent); }
    .accuracy {
      display: flex; flex-direction: column; text-align: right; white-space: nowrap;
      font-size: 0.85rem; font-variant-numeric: tabular-nums;
      color: color-mix(in srgb, currentColor 80%, transparent);
    }
    .accuracy span { line-height: 1.45; }
    .accuracy .sep, .moves-label { display: none; }
    /* So breit wie ein Icon-Knopf, damit die Zeile beim Wechsel Knopf → Prozent nicht springt. */
    .progress {
      display: inline-flex; align-items: center; justify-content: center; width: 40px; height: 40px;
      font-size: 0.8rem; font-variant-numeric: tabular-nums; color: var(--rh-accent); cursor: default;
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
    @media (max-width: 768px) {
      .row { display: flex; flex-wrap: wrap; gap: 6px 10px; padding: 10px 8px; }
      /* MIT .row davor: beide Regeln haetten sonst dieselbe Spezifitaet, und die spaetere gewinnt —
         die Kopfzeile stand dann als Wortreihe ueber der Liste (im Bild nachgemessen). */
      .row.head-row { display: none; }
      /* d3-rk-games-liste-mobile-namen-versetzt: feste Typspalte, Tempo unter dem Symbol — die Namen beginnen in jeder
         Zeile an derselben Stelle (vorher x≈58 bei PGN, x≈90 mit „3 + 2“). */
      .kind { flex: 0 0 56px; flex-direction: column; align-items: center; gap: 0; }
      .tc { font-size: 0.7rem; white-space: normal; text-align: center; line-height: 1.15; }
      .players { flex: 1 1 55%; }
      .acc-cell { margin-left: auto; }
      /* Ohne Spaltenüberschrift als Etikett „♔ 85 % · ♚ 79 %" — eine nackte Zahl las sich nicht als Genauigkeit. */
      .accuracy { flex-direction: row; gap: 5px; text-align: left; font-size: 0.75rem; padding: 2px 8px; border-radius: 10px;
        background: color-mix(in srgb, var(--rh-accent) 16%, transparent); }
      .accuracy span { line-height: 1.3; }
      .accuracy .sep { display: inline; font-style: normal; opacity: 0.6; }
      .moves-num { display: none; }
      .moves-label { display: inline; white-space: nowrap; }
      .actions { order: 9; margin-left: auto; }
      /* Die Fussnote ganz nach hinten, sonst draengt sie die Knoepfe in eine eigene leere Zeile. */
      .foot { order: 10; flex-basis: 100%; }
      .moves, .date, .foot { font-size: 0.8rem; color: color-mix(in srgb, currentColor 60%, transparent); }
    }
    @media (min-width: 2400px) {
      .games-page { max-width: min(var(--page-max-width-wide, 1880px), 96vw); }
      /* b1-rk-breitstufe-zeilen-auseinander: in 1 880 px nahm die Spielerspalte den ganzen Rest (~1 000 px Lücke bis zum
         Ergebnis) — die Zahlenspalten wachsen jetzt mit (Mindestbreiten wie darunter), die Zeile bleibt verfolgbar. */
      .row { grid-template-columns: 78px minmax(0, 1.4fr) minmax(72px, 0.5fr) minmax(84px, 0.5fr) minmax(48px, 0.5fr) minmax(104px, 0.5fr) 88px; }
      .row > .date, .head-row > span:nth-child(6) { text-align: center; }
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

  /** Filter der Klassifizierer (leer = alle) — wie `onlyOpen` bewusst nicht gemerkt. */
  filter1 = '';
  filter2 = '';
  /** Tag-Filter (leer = alle). */
  filterTag = '';
  tagOptions(): string[] { return distinctTags(this.games); }
  readonly none = NO_CLASSIFIER;
  /** Filter am Handy aufgeklappt? Vorgabe zu (v1-rk-games-mobile-fold-lead-filters-open); am PC immer offen. */
  readonly filtersOpen = signal(false);

  /** Gesetzte Filter als Chips neben „Filter ▾" (Handy, zugeklappt). */
  activeFilters(): { which: 'filter1' | 'filter2' | 'filterTag'; label: string }[] {
    const label = (v: string) => v === NO_CLASSIFIER ? this.translate.instant('games.classifier.none') : v;
    const out: { which: 'filter1' | 'filter2' | 'filterTag'; label: string }[] = [];
    if (this.filter1) out.push({ which: 'filter1', label: label(this.filter1) });
    if (this.filter2) out.push({ which: 'filter2', label: label(this.filter2) });
    if (this.filterTag) out.push({ which: 'filterTag', label: this.filterTag });
    return out;
  }

  clearFilter(which: 'filter1' | 'filter2' | 'filterTag'): void { this[which] = ''; }

  /** Die gewählten Klassifizierer als Adresse für „Fehler nachspielen (Liga/Saison)". */
  collectionParams(): Record<string, string | null> {
    return { c1: classifierToParam(this.filter1), c2: classifierToParam(this.filter2) };
  }

  firstOptions(): string[] { return distinctClassifiers(this.games, 1); }
  secondOptions(): string[] { return distinctClassifiers(this.games, 2); }
  unclassified1(): boolean { return hasUnclassified(this.games, 1); }
  unclassified2(): boolean { return hasUnclassified(this.games, 2); }

  /** Die angezeigte Liste — ungefiltert, oder nur die mit offenen Fehlern bzw. dem gewählten Klassifizierer. */
  shownGames(): SavedGame[] {
    const open = this.onlyOpen ? this.games.filter(g => (g.mistakes?.open ?? 0) > 0) : this.games;
    // Ein Wert, den es nach dem Löschen/Neuladen nicht mehr gibt, filtert nicht mehr: sonst stünde eine leere Liste
    // da, und die Auswahl hätte keinen Eintrag mehr, mit dem man den Filter zurücknehmen könnte.
    const known = (value: string, options: string[], hasNone: boolean): string =>
      value === NO_CLASSIFIER ? (hasNone ? value : '') : options.includes(value) ? value : '';
    const tag = this.tagOptions().some(t => t.toLowerCase() === this.filterTag.toLowerCase()) ? this.filterTag : '';
    return filterByTag(filterByClassifiers(open,
      known(this.filter1, this.firstOptions(), this.unclassified1()),
      known(this.filter2, this.secondOptions(), this.unclassified2())), tag);
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
  private scoresheets = inject(ScoresheetService);
  private dialog = inject(MatDialog);
  private confirm = inject(ConfirmService);
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

  /** „87%“ (en) / „87 %“ (de) oder „—“, wenn die Seite keinen bewertbaren Zug hatte. */
  pct(value: number | null | undefined): string {
    return value == null ? '—' : formatPct(Math.round(value), this.translate.currentLang() || null);
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
    return gameSourceIcon(source);
  }

  sourceLabel(source: string): string {
    return gameSourceLabel(source, this.translate);
  }

  /** „PGN hochladen": Datei oder eingefügt; danach die Liste neu. Genau EINE neue Partie → gleich öffnen. */
  uploadPgn(): void {
    this.dialog.open(PgnImportDialogComponent, { maxWidth: '96vw' }).afterClosed()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((r: PgnImportResult | undefined) => {
        if (!r) return;
        this.snackbar.info(this.translate.instant('games.pgnUpload.summary',
          { imported: r.imported, duplicates: r.duplicates, failed: r.failed.length }));
        if (r.imported === 1 && r.ids.length === 1 && !r.failed.length) {
          this.router.navigate(['/games', r.ids[0]]);
          return;
        }
        this.service.list().pipe(takeUntilDestroyed(this.destroyRef)).subscribe(list => { this.games = list; this.schedulePoll(); });
      });
  }

  /** Das Formular-Foto einer eingelesenen Partie anzeigen (Dialog) oder herunterladen. */
  /** „Roast my game" (0.535.0). */
  roast(g: SavedGame): void {
    this.dialog.open(GameRoastDialogComponent, {
      data: { gameId: g.id, shareUrl: g.shareToken ? this.service.shareUrl(g.shareToken) : null, shareToken: g.shareToken ?? null } satisfies GameRoastData,
      maxWidth: '96vw',
    });
  }

  photo(g: SavedGame, download: boolean): void {
    if (!download) { ScoresheetPhotoDialogComponent.open(this.dialog, g.id); return; }
    this.scoresheets.photo(g.id).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: blob => openPhotoBlob(blob, photoFileName(g.id, blob)),
      error: () => this.snackbar.warn(this.translate.instant('games.photo.loadError')),
    });
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

  /** PGN nachladen und in die Zwischenablage (wie „PGN kopieren“ auf der Partieseite). */
  copyPgn(g: SavedGame): void {
    this.service.get(g.id).subscribe({
      next: detail => navigator.clipboard?.writeText(detail.pgn).then(
        () => this.snackbar.copy(this.translate.instant('games.pgnCopied')),
        () => this.snackbar.warn(this.translate.instant('games.pgnCopyFailed')),
      ),
      error: () => this.snackbar.warn(this.translate.instant('games.loadError')),
    });
  }

  /** PGN nachladen und als Datei „Weiß Schwarz_Datum.pgn“ speichern (wie auf der Partieseite). */
  downloadPgn(g: SavedGame): void {
    this.service.get(g.id).subscribe({
      next: detail => {
        const pgn = detail.pgn ?? '';
        const players = [detail.white, detail.black].filter(n => !!n).join(' ');
        downloadBlob(new Blob([pgn.endsWith('\n') ? pgn : pgn + '\n'], { type: 'application/x-chess-pgn' }),
          pgnFileName(players || 'game', (detail.playedAt ?? detail.createdAt ?? '').slice(0, 10)));
      },
      error: () => this.snackbar.warn(this.translate.instant('games.loadError')),
    });
  }

  remove(g: SavedGame): void {
    this.confirm.ask('games.deleteConfirm').subscribe(ok => {
      if (!ok) return;
      this.service.delete(g.id).subscribe({
        next: () => { this.games = this.games.filter(x => x.id !== g.id); },
        error: () => this.snackbar.warn(this.translate.instant('games.deleteError')),
      });
    });
  }
}
