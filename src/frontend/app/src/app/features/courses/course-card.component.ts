import { Component, Input, Output, EventEmitter, ChangeDetectionStrategy } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { IconLabelDirective } from '../../shared/icon-label/icon-label.directive';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { CourseListItem, CourseChapter } from './course.service';
import { formatScore, maxPoints } from './calc/calc-review.util';
import { labelOr } from './course-language.util';

/**
 * Praesentationale Kurs-Karte: zeigt Titel/Badges/Fortschritt/Themen-Chips/Aktions-Menue/Kapitel
 * eines Kurses. Enthaelt KEINE Logik/Service-Aufrufe — alle Aktionen werden als Outputs an den
 * Container (CourseListComponent) gemeldet, der den State (optimistische Toggles etc.) behaelt.
 * `:host { display: contents }` haelt die Karte 1:1 als Grid-Item im `.course-grid` (wie zuvor der
 * `*ngTemplateOutlet`-`ng-container`).
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-course-card',
  standalone: true,
  imports: [
    CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressBarModule, MatTooltipModule, IconLabelDirective, MatMenuModule, TranslatePipe, LoadingSpinnerComponent,
  ],
  template: `
      <mat-card class="course-card">
        <mat-card-content>
          <div class="title-row">
          <a class="card-title" [routerLink]="['/courses', course.bookId]" [attr.title]="course.displayName"
             [matTooltip]="'courses.detail.openTooltip' | translate">{{ course.displayName }}</a>
          @if (course.needsReimport) {
            <!-- Showstopper am KURS statt als anonyme Zahl im Banner: nur hier ist zu sehen, WELCHER
                 Kurs gemeint ist (gemeldet 2026-09-20). UI-Sweep 2026-10-11 (r2-rp-course-warn): Text-Chip
                 statt nacktem (!) — ein Tipp klappt die Erklärung auf (am Handy gibt es keinen Tooltip). -->
            <button type="button" class="stale-chip" (click)="staleOpen = !staleOpen"
                    [attr.aria-expanded]="staleOpen"
                    [matTooltip]="'reprocess.itemNeedsReimport.courses' | translate">
              {{ 'courses.reimport.chip' | translate }} <mat-icon>info_outline</mat-icon>
            </button>
          }
          </div>
          @if (course.needsReimport && staleOpen) {
            <p class="stale-hint">{{ 'reprocess.itemNeedsReimport.courses' | translate }}</p>
          }
          <!-- UI-Sweep 2026-10-11 (r2-rp-course-dupes): gleichnamige Kurse bekommen ihre Herkunft als Untertitel. -->
          @if (origin) {
            <div class="origin-line">
              {{ (origin.chessable ? 'courses.origin.chessable' : 'courses.origin.pgn') | translate }}@if (origin.owner) { · {{ 'courses.origin.by' | translate:{ name: origin.owner } }} }
            </div>
          }
          @if (course.isShared && course.sharedByUsername) {
            <div class="shared-badge">
              <mat-icon>group</mat-icon>{{ 'courses.share.sharedBy' | translate:{ name: course.sharedByUsername } }}
            </div>
          }
          @if (course.linkedBookId && course.linkedDisplayName) {
            <a class="linked-badge" [routerLink]="['/courses', course.linkedBookId, 'sequential']"
               [matTooltip]="'courses.link.openLinked' | translate">
              <mat-icon>link</mat-icon>{{ course.linkedDisplayName }}
            </a>
          }
          <div class="card-meta">
            @if (!isEmptyQuiz) {
              <span>{{ 'courses.puzzleCount' | translate:{ count: course.puzzleCount } }}</span>
            }
            @if (course.difficulty) { <span class="meta-sep">·</span><span>{{ course.difficulty }}</span> }
            @if (course.rating) { <span class="meta-sep">·</span><span>{{ course.rating }}/10</span> }
            <!-- Kalkulationsbuch: Punkte der Selbstbewertung — IMMER mit dem Maximum, sonst ist
                 die Zahl ohne die Stellungsanzahl nicht lesbar. -->
            @if (calcScore) {
              <span class="meta-sep">·</span>
              <span class="calc-points" [matTooltip]="'courses.detail.calcPointsHint' | translate">
                {{ 'courses.calcPoints' | translate:{ score: calcScore } }}
              </span>
            }
          </div>

          @if (themeChipKeys.length) {
            <div class="theme-chips" [attr.aria-label]="'courses.themes.tooltip' | translate">
              @for (k of themeChipKeys; track k) {
                <span class="theme-chip">{{ k | translate }}</span>
              }
            </div>
          }

          @if (isEmptyQuiz) {
            <!-- UI-Sweep 2026-10-11 (r2-rp-empty-course): statt „0 Puzzles · 0/0" und grauer Knöpfe sagen,
                 was der Kurs ist — nur kommentierte Partien (durchsehen) oder noch leer. -->
            <p class="empty-note">{{ (hasInfoLines ? 'courses.emptyCourse.gamesOnly'
              : canManageThemes ? 'courses.emptyCourse.noLinesOwner' : 'courses.emptyCourse.noLines') | translate }}</p>
          } @else {
            <div class="progress-row">
              <mat-progress-bar mode="determinate" [value]="course.progressPercent"></mat-progress-bar>
              <span class="progress-label">{{ course.solvedCount }}/{{ course.puzzleCount }}</span>
            </div>
          }

          @if (course.puzzleCount > 0 && course.solvedCount >= course.puzzleCount) {
            <p class="done-hint"><mat-icon>emoji_events</mat-icon> {{ 'courses.completed' | translate }}</p>
          }
        </mat-card-content>

        <div class="card-footer">
          <div class="action-row">
            <div class="primary-actions">
              @if (isEmptyQuiz) {
                @if (hasInfoLines) {
                  <button mat-flat-button color="primary" [routerLink]="['/courses', course.bookId, 'browse']">
                    <mat-icon>auto_stories</mat-icon>{{ 'courses.emptyCourse.browseGames' | translate }}
                  </button>
                } @else if (canManageThemes) {
                  <button mat-flat-button color="primary" [routerLink]="['/courses', course.bookId]">
                    <mat-icon>playlist_add</mat-icon>{{ 'courses.emptyCourse.addLines' | translate }}
                  </button>
                }
              } @else if (course.isCalculation) {
                <!-- Kalkulationsbuch: reine Stellungen ohne Lösung → nur der Kalkulations-Modus. -->
                <button mat-flat-button color="primary"
                        [routerLink]="['/courses', course.bookId, 'calc']" [disabled]="course.puzzleCount === 0">
                  <mat-icon>calculate</mat-icon>{{ 'courses.calculate' | translate }}
                </button>
              } @else {
                <button mat-flat-button color="primary"
                        [routerLink]="['/courses', course.bookId, 'sequential']" [disabled]="course.puzzleCount === 0">
                  <mat-icon>format_list_numbered</mat-icon>{{ 'courses.sequential' | translate }}
                </button>
                <button mat-stroked-button
                        [routerLink]="['/courses', course.bookId, 'random']" [disabled]="course.puzzleCount === 0">
                  <mat-icon>shuffle</mat-icon>{{ 'courses.random' | translate }}
                </button>
              }
            </div>
            <div class="util-actions">
              <button mat-icon-button [matMenuTriggerFor]="actionMenu"
                      [appIconLabel]="'common.moreActions' | translate">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #actionMenu="matMenu">
                <button mat-menu-item [routerLink]="['/courses', course.bookId]">
                  <mat-icon>info</mat-icon>
                  <span>{{ 'courses.detail.open' | translate }}</span>
                </button>
                <!-- „Durchsehen" und „Offline speichern" holen die LINIEN samt Zugfolge
                     (GET /api/courses/{id}/puzzles). Ein Kalkulationsbuch gibt die nicht heraus
                     (dort ist sie die Lösung) — beides entfällt deshalb, wie schon auf der
                     Detailseite, die „Durchsehen" bei Kalkulationsbüchern ebenfalls weglässt. -->
                @if (!course.isCalculation) {
                  <button mat-menu-item [disabled]="course.puzzleCount === 0 && !hasInfoLines"
                          [routerLink]="['/courses', course.bookId, 'browse']">
                    <mat-icon>auto_stories</mat-icon>
                    <span>{{ 'courses.browseTooltip' | translate }}</span>
                  </button>
                }
                <button mat-menu-item [class.active-item]="course.isPinned"
                        [disabled]="pinning" (click)="pinToggle.emit()">
                  <mat-icon>push_pin</mat-icon>
                  <span>{{ (course.isPinned ? 'courses.unpinTooltip' : 'courses.pinTooltip') | translate }}</span>
                </button>
                @if (!course.isCalculation) {
                  <button mat-menu-item
                          [disabled]="course.puzzleCount === 0 || savingOffline"
                          (click)="offlineToggle.emit()">
                    <mat-icon>{{ offline ? 'cloud_done' : 'cloud_download' }}</mat-icon>
                    <span>{{ (offline ? 'courses.offlineRemoveTooltip' : 'courses.offlineSaveTooltip') | translate }}</span>
                  </button>
                }
                <!-- PGN-Download und „In Repertoire umwandeln" liefern das Roh-PGN aller Wochen (bei
                     Kalkulationsbüchern samt Lösung) — der Server sperrt beides dort (404). -->
                @if (!course.isCalculation) {
                  <button mat-menu-item
                          [disabled]="course.puzzleCount === 0 || downloadingPgn" (click)="pgnDownload.emit()">
                    <mat-icon>download</mat-icon>
                    <span>{{ 'courses.downloadPgnTooltip' | translate }}</span>
                  </button>
                }
                <button mat-menu-item
                        [disabled]="course.solvedCount === 0" (click)="progressReset.emit()">
                  <mat-icon>restart_alt</mat-icon>
                  <span>{{ 'courses.resetTooltip' | translate }}</span>
                </button>
                @if (!course.isCalculation) {
                  <button mat-menu-item
                          [disabled]="converting" (click)="convertRepertoire.emit()">
                    <mat-icon>library_books</mat-icon>
                    <span>{{ 'courses.convertToRepertoireTooltip' | translate }}</span>
                  </button>
                }
                <button mat-menu-item [class.active-item]="course.linkedBookId" (click)="linkEdit.emit()">
                  <mat-icon>{{ course.linkedBookId ? 'link' : 'add_link' }}</mat-icon>
                  <span>{{ (course.linkedBookId ? 'courses.link.linkedTooltip' : 'courses.link.tooltip') | translate:{ name: course.linkedDisplayName } }}</span>
                </button>
                @if (canManageThemes) {
                  <button mat-menu-item (click)="themesEdit.emit()">
                    <mat-icon>sell</mat-icon>
                    <span>{{ 'courses.themes.tooltip' | translate }}</span>
                  </button>
                }
                @if (course.isOwned) {
                  <button mat-menu-item (click)="shareCourse.emit()">
                    <mat-icon>group_add</mat-icon>
                    <span>{{ 'courses.share.tooltip' | translate }}</span>
                  </button>
                  <button mat-menu-item class="delete-item"
                          [disabled]="deleting" (click)="deleteCourse.emit()">
                    <mat-icon>delete</mat-icon>
                    <span>{{ 'courses.deleteTooltip' | translate }}</span>
                  </button>
                }
              </mat-menu>
            </div>
          </div>

          <!-- Kalkulationsbücher haben keine Kapitel-Modi (kein Lösen/Fortschritt je Kapitel) —
               die Kapitel dienen dort nur als Sprungliste IM Kalkulations-Modus. -->
          @if (course.puzzleCount > 0 && !course.isCalculation) {
            <div class="chapters-block">
              <button class="chapters-toggle" (click)="chaptersToggle.emit()"
                      [attr.aria-expanded]="expanded">
                <mat-icon class="toggle-icon">{{ expanded ? 'expand_less' : 'expand_more' }}</mat-icon>
                <span>{{ 'courses.chapters' | translate }}@if (chapters) { ({{ chapters.length }}) }</span>
              </button>
              @if (expanded) {
                @if (loadingChapters) {
                  <app-loading-spinner />
                } @else if (chapters?.length) {
                  <ul class="chapter-list">
                    @for (ch of chapters; track ch.index) {
                      <li class="chapter-row">
                        <!-- Angezeigt wird die Übersetzung (label), geroutet weiter über den Index. -->
                        <span class="chapter-name" [title]="chapterName(ch) || ('courses.noChapter' | translate)">
                          {{ chapterName(ch) || ('courses.noChapter' | translate) }}
                        </span>
                        <div class="chapter-progress">
                          <mat-progress-bar class="chapter-bar" mode="determinate" [value]="ch.progressPercent"></mat-progress-bar>
                          <span class="chapter-label">{{ ch.solvedCount }}/{{ ch.puzzleCount }}@if (ch.infoCount > 0) {<span class="chapter-info-count" [matTooltip]="'courses.infoLinesTooltip' | translate">&nbsp;{{ 'courses.infoLines' | translate:{ count: ch.infoCount } }}</span>}</span>
                        </div>
                        <!-- Eine Primäraktion je Kapitel (im zuletzt genutzten Modus des Kurses) statt
                             dreier gleich aussehender Icons; die Alternativen liegen im ⋮-Menü der Zeile.
                             Ein Buch mit 15 Kapiteln zeigte vorher 45 Icons in einer Karte. -->
                        <div class="chapter-btns">
                          <button mat-icon-button color="primary"
                                  [appIconLabel]="startChapterTooltip | translate"
                                  [routerLink]="['/courses', course.bookId, 'chapter', ch.index, chapterMode]">
                            <mat-icon>play_arrow</mat-icon>
                          </button>
                          <button mat-icon-button [matMenuTriggerFor]="chapterMenu"
                                  [appIconLabel]="'common.moreActions' | translate">
                            <mat-icon>more_vert</mat-icon>
                          </button>
                          <mat-menu #chapterMenu="matMenu">
                            <button mat-menu-item [class.active-item]="chapterMode === 'sequential'"
                                    [routerLink]="['/courses', course.bookId, 'chapter', ch.index, 'sequential']">
                              <mat-icon>format_list_numbered</mat-icon>
                              <span>{{ 'courses.sequential' | translate }}</span>
                            </button>
                            <button mat-menu-item [class.active-item]="chapterMode === 'random'"
                                    [routerLink]="['/courses', course.bookId, 'chapter', ch.index, 'random']">
                              <mat-icon>shuffle</mat-icon>
                              <span>{{ 'courses.random' | translate }}</span>
                            </button>
                            <button mat-menu-item
                                    [routerLink]="['/courses', course.bookId, 'chapter', ch.index, 'browse']">
                              <mat-icon>auto_stories</mat-icon>
                              <span>{{ 'courses.browseTooltip' | translate }}</span>
                            </button>
                          </mat-menu>
                        </div>
                      </li>
                    }
                  </ul>
                } @else {
                  <p class="chapter-empty">{{ 'courses.chaptersEmpty' | translate }}</p>
                }
              }
            </div>
          }
        </div>
      </mat-card>
  `,
  styles: [`
    :host { display: contents; }
    .shared-badge {
      display: inline-flex; align-items: center; gap: 4px; font-size: 0.74rem;
      color: color-mix(in srgb, currentColor 60%, transparent); margin-bottom: 6px;
    }
    .shared-badge mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .linked-badge {
      display: inline-flex; align-items: center; gap: 4px; font-size: 0.74rem; margin-bottom: 6px;
      color: var(--mat-sys-primary); text-decoration: none; cursor: pointer; max-width: 100%;
    }
    .linked-badge:hover { text-decoration: underline; }
    .linked-badge mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .course-card {
      display: flex; flex-direction: column;
      padding: 0;
      /* UI-Sweep 2026-10-11 (d2-rk-course-card-aktionen): Inhalt wächst, damit Fußzeile (Aktionen, Kapitel) in jeder
         Karte einer Reihe auf derselben Höhe unten bündig sitzt (DESIGN 5.2). */
      /* UI-Sweep z1 (z1-rk-course-card-buendig): Inhalt als Flex-Spalte, der Fortschritt sitzt unten im Inhalt —
         so stehen die Balken einer Reihe auf derselben Höhe, egal wie viele Zeilen darüber stehen. */
      mat-card-content { padding: 14px 16px 8px; flex: 1 1 auto; display: flex; flex-direction: column; }
    }
    .shared-badge, .linked-badge { align-self: flex-start; }
    .progress-row, .empty-note { margin-top: auto; }
    /* Titel einzeilig mit „…" (DESIGN 5.2), voller Titel im title-Attribut. */
    .card-title {
      font-size: 0.95rem; font-weight: 600; line-height: 1.35; margin-bottom: 2px;
      display: block; color: inherit; text-decoration: none; cursor: pointer;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis;
    }
    .card-title:hover { text-decoration: underline; }
    .title-row { display: flex; align-items: flex-start; gap: 4px; }
    .title-row .card-title { flex: 1 1 auto; min-width: 0; }
    /* (!) am Eintrag: klein und warnfarben, aber nicht schreiend — es ist ein Hinweis, kein Fehler. */
    .stale-chip {
      flex: 0 0 auto; display: inline-flex; align-items: center; gap: 2px; cursor: pointer;
      border: 1px solid var(--rh-warn); color: var(--rh-warn); background: none; border-radius: 10px;
      padding: 0 4px 0 7px; font: inherit; font-size: 0.7rem; line-height: 18px; white-space: nowrap;
    }
    .stale-chip mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .stale-hint { font-size: 0.78rem; margin: 2px 0 6px; color: color-mix(in srgb, currentColor 70%, transparent); }
    .origin-line { font-size: 0.75rem; color: color-mix(in srgb, currentColor 55%, transparent); margin-bottom: 2px; }
    .card-meta {
      display: flex; align-items: center; gap: 4px; flex-wrap: wrap;
      font-size: 0.8rem; color: color-mix(in srgb, currentColor 55%, transparent);
      margin-bottom: 8px;
    }
    .meta-sep { opacity: 0.5; }
    .calc-points { font-variant-numeric: tabular-nums; font-weight: 600; }
    .progress-row { display: flex; align-items: center; gap: 8px; }
    .empty-note { margin: 4px 0 0; font-size: 0.85rem; color: color-mix(in srgb, currentColor 65%, transparent); }
    .progress-row mat-progress-bar { flex: 1; --mdc-linear-progress-track-height: 5px; --mdc-linear-progress-active-indicator-height: 5px; border-radius: 3px; }
    .progress-label { font-variant-numeric: tabular-nums; font-size: 0.78rem; color: color-mix(in srgb, currentColor 55%, transparent); white-space: nowrap; min-width: 46px; text-align: right; }
    .done-hint { display: flex; align-items: center; gap: 4px; color: var(--rh-success); font-size: 0.82rem; font-weight: 500; margin: 6px 0 0; }
    .done-hint mat-icon { font-size: 16px; width: 16px; height: 16px; }

    .theme-chips { display: flex; flex-wrap: wrap; gap: 4px; margin: 6px 0 0; }
    .theme-chip { font-size: 0.72rem; line-height: 1; padding: 3px 8px; border-radius: 999px;
      background: color-mix(in srgb, var(--mat-sys-primary, #1565c0) 16%, transparent);
      color: var(--mat-sys-primary, #1565c0); font-weight: 600; }

    .card-footer { padding: 0 16px 12px; border-top: 1px solid color-mix(in srgb, currentColor 8%, transparent); margin-top: 2px; }
    /* UI-Sweep z1 (z1-rk-course-card-buendig): Fuß so hoch wie mit Kapitel-Zeile — Aktionen beginnen in jeder Karte
       einer Reihe auf derselben Höhe (am Handy einspaltig, dort nicht nötig). */
    @media (min-width: 600px) { .card-footer { min-height: 76px; box-sizing: border-box; } }

    /* Zwei Primär-Buttons links, alle weiteren Aktionen im „⋮"-Überlaufmenü rechts.
       Nur noch drei Elemente in der Zeile → läuft auch auf schmalen Phones nicht über. */
    .action-row { display: flex; align-items: center; gap: 8px; padding-top: 8px; margin-top: auto; }
    .primary-actions { display: flex; gap: 6px; flex: 1; min-width: 0; }
    .primary-actions button { font-size: 0.82rem; height: 32px; line-height: 32px; padding: 0 10px; }
    .primary-actions mat-icon { font-size: 16px; width: 16px; height: 16px; margin-right: 4px; vertical-align: middle; }
    .util-actions { display: flex; align-items: center; flex-shrink: 0; }
    .util-actions .mat-mdc-icon-button { width: 32px; height: 32px; padding: 4px; }
    .util-actions mat-icon { font-size: 18px; }

    /* Aktionsmenü: aktive Aktion (angepinnt / verknüpft) in Primärfarbe, Löschen rot. */
    .active-item mat-icon { color: var(--mat-sys-primary); }
    .delete-item mat-icon { color: var(--rh-error); }

    .chapters-block { margin-top: 6px; }
    .chapters-toggle {
      display: flex; align-items: center; gap: 4px; background: none; border: none; cursor: pointer;
      color: inherit; font-size: 0.8rem; opacity: 0.6; padding: 2px 0;
      transition: opacity .15s;
      &:hover { opacity: 1; }
    }
    .toggle-icon { font-size: 16px; width: 16px; height: 16px; }
    .chapter-list { list-style: none; margin: 4px 0 0; padding: 0; }
    .chapter-row { display: flex; align-items: center; gap: 8px; padding: 3px 0; }
    .chapter-name { flex: 1; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; font-size: 0.82rem; }
    .chapter-progress { display: flex; align-items: center; gap: 6px; flex-shrink: 0; }
    .chapter-bar { width: 70px; --mdc-linear-progress-track-height: 4px; --mdc-linear-progress-active-indicator-height: 4px; }
    .chapter-label { font-variant-numeric: tabular-nums; font-size: 0.75rem; color: color-mix(in srgb, currentColor 55%, transparent); white-space: nowrap; min-width: 36px; }
    .chapter-info-count { font-size: 0.7rem; color: color-mix(in srgb, currentColor 40%, transparent); font-style: italic; }
    .chapter-btns { display: flex; flex-shrink: 0; }
    .chapter-btns .mat-mdc-icon-button { width: 30px; height: 30px; padding: 4px; }
    .chapter-btns mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .chapter-empty { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; font-size: 0.82rem; margin: 4px 0 0; }
  `],
})
export class CourseCardComponent {
  @Input({ required: true }) course!: CourseListItem;
  /** UI-Sweep 2026-10-11 (r2-rp-course-dupes): Herkunft, nur gesetzt, wenn der Titel mehrfach vorkommt. */
  @Input() origin: { chessable: boolean; owner: string | null } | null = null;
  /** Erklärung „Neu holen" aufgeklappt? (r2-rp-course-warn) */
  staleOpen = false;

  /** Angezeigter Kapitelname: die Übersetzung (`label`, nur mit `?lang=`), sonst das Original. */
  chapterName(ch: CourseChapter): string | null {
    return labelOr(ch.label, ch.name);
  }
  /** bookId dieses Kurses wird gerade an-/abgepinnt (Button-Sperre). */
  @Input() pinning = false;
  @Input() savingOffline = false;
  @Input() downloadingPgn = false;
  @Input() converting = false;
  @Input() deleting = false;
  /** true = Kurs ist offline gecacht. */
  @Input() offline = false;
  /** Darf der Nutzer die Themen-Tags dieses Kurses setzen (Admin oder Besitzer)? */
  @Input() canManageThemes = false;
  /** Kapiteluebersicht dieses Kurses aufgeklappt? */
  @Input() expanded = false;
  @Input() loadingChapters = false;
  /** Lazy geladene Kapitel dieses Kurses (undefined = noch nicht geladen). */
  @Input() chapters: CourseChapter[] | undefined;

  @Output() pinToggle = new EventEmitter<void>();
  @Output() offlineToggle = new EventEmitter<void>();
  @Output() pgnDownload = new EventEmitter<void>();
  @Output() progressReset = new EventEmitter<void>();
  @Output() convertRepertoire = new EventEmitter<void>();
  @Output() linkEdit = new EventEmitter<void>();
  @Output() themesEdit = new EventEmitter<void>();
  @Output() shareCourse = new EventEmitter<void>();
  @Output() deleteCourse = new EventEmitter<void>();
  @Output() chaptersToggle = new EventEmitter<void>();

  /** Modus, in dem der Kapitel-Play-Knopf startet: der zuletzt in diesem Kurs genutzte
   *  (`CourseProgress.LastMode`), sonst sequenziell. Die andere Variante bleibt im ⋮-Menü. */
  get chapterMode(): 'sequential' | 'random' {
    return this.course.lastMode === 'random' ? 'random' : 'sequential';
  }

  /** Tooltip des Play-Knopfs — nennt den Modus, damit klar ist, was der Klick tut. */
  get startChapterTooltip(): string {
    return this.chapterMode === 'random' ? 'courses.startChapterRandom' : 'courses.startChapterSequential';
  }

  /**
   * Punktestand der Selbstbewertung („14 / 24"); leer, wenn es keiner ist (kein Kalkulationsbuch
   * bzw. Server ohne den Wert). Das Maximum gehört IMMER dazu — nackte Punkte sind ohne die Zahl
   * der Stellungen nicht lesbar; fehlt es, ergibt es sich aus den Stellungen (4 je Stellung).
   */
  /** UI-Sweep 2026-10-11 (r2-rp-empty-course): normaler Kurs ohne eine einzige Quiz-Linie. */
  get isEmptyQuiz(): boolean {
    return !this.course.isCalculation && this.course.puzzleCount === 0;
  }

  /** Hat der Kurs Info-/Partie-Linien zum Durchsehen (nur bei Kursen ohne Quiz-Linie bekannt)? */
  get hasInfoLines(): boolean {
    return (this.course.infoLineCount ?? 0) > 0;
  }

  /**
   * UI-Sweep 2026-10-11 (r2-rp-taktik-chip): Chips nur für GESETZTE Themen. Der Server liefert ohne
   * gesetzte Themen die Vorgabe ["tactics"] — als Chip unterschied „Taktik" damit nichts (auch Eröffnungs-
   * und Endspielkurse trugen ihn). Kalkulationsbücher heißen „Kalkulation".
   */
  get themeChipKeys(): string[] {
    if (this.course.isCalculation) return ['courses.kindCalculation'];
    const themes = this.course.themes ?? [];
    if (themes.length === 0 || (themes.length === 1 && themes[0] === 'tactics')) return [];
    return themes.map(t => 'trainingGoals.theme.' + t);
  }

  get calcScore(): string {
    if (!this.course.isCalculation || this.course.calcPoints == null) return '';
    return formatScore(this.course.calcPoints, this.course.calcMaxPoints ?? maxPoints(this.course.puzzleCount));
  }
}
