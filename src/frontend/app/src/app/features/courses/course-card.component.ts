import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatProgressBarModule } from '@angular/material/progress-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { MatMenuModule } from '@angular/material/menu';
import { TranslatePipe } from '@ngx-translate/core';
import { LoadingSpinnerComponent } from '../../shared/loading-spinner/loading-spinner.component';
import { CourseListItem, CourseChapter } from './course.service';

/**
 * Praesentationale Kurs-Karte: zeigt Titel/Badges/Fortschritt/Themen-Chips/Aktions-Menue/Kapitel
 * eines Kurses. Enthaelt KEINE Logik/Service-Aufrufe — alle Aktionen werden als Outputs an den
 * Container (CourseListComponent) gemeldet, der den State (optimistische Toggles etc.) behaelt.
 * `:host { display: contents }` haelt die Karte 1:1 als Grid-Item im `.course-grid` (wie zuvor der
 * `*ngTemplateOutlet`-`ng-container`).
 */
@Component({
  selector: 'app-course-card',
  standalone: true,
  imports: [
    CommonModule, RouterModule, MatCardModule, MatButtonModule, MatIconModule,
    MatProgressBarModule, MatTooltipModule, MatMenuModule, TranslatePipe, LoadingSpinnerComponent,
  ],
  template: `
      <mat-card class="course-card">
        <mat-card-content>
          <div class="card-title">{{ course.displayName }}</div>
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
            <span>{{ 'courses.puzzleCount' | translate:{ count: course.puzzleCount } }}</span>
            @if (course.difficulty) { <span class="meta-sep">·</span><span>{{ course.difficulty }}</span> }
            @if (course.rating) { <span class="meta-sep">·</span><span>{{ course.rating }}/10</span> }
          </div>

          @if (course.themes?.length) {
            <div class="theme-chips" [attr.aria-label]="'courses.themes.tooltip' | translate">
              @for (t of course.themes; track t) {
                <span class="theme-chip">{{ ('trainingGoals.theme.' + t) | translate }}</span>
              }
            </div>
          }

          <div class="progress-row">
            <mat-progress-bar mode="determinate" [value]="course.progressPercent"></mat-progress-bar>
            <span class="progress-label">{{ course.solvedCount }}/{{ course.puzzleCount }}</span>
          </div>

          @if (course.puzzleCount > 0 && course.solvedCount >= course.puzzleCount) {
            <p class="done-hint"><mat-icon>emoji_events</mat-icon> {{ 'courses.completed' | translate }}</p>
          }
        </mat-card-content>

        <div class="card-footer">
          <div class="action-row">
            <div class="primary-actions">
              <button mat-flat-button color="primary"
                      [routerLink]="['/courses', course.bookId, 'sequential']" [disabled]="course.puzzleCount === 0">
                <mat-icon>format_list_numbered</mat-icon>{{ 'courses.sequential' | translate }}
              </button>
              <button mat-stroked-button
                      [routerLink]="['/courses', course.bookId, 'random']" [disabled]="course.puzzleCount === 0">
                <mat-icon>shuffle</mat-icon>{{ 'courses.random' | translate }}
              </button>
            </div>
            <div class="util-actions">
              <button mat-icon-button [matMenuTriggerFor]="actionMenu"
                      [matTooltip]="'courses.moreActions' | translate"
                      [attr.aria-label]="'courses.moreActions' | translate">
                <mat-icon>more_vert</mat-icon>
              </button>
              <mat-menu #actionMenu="matMenu">
                <button mat-menu-item [disabled]="course.puzzleCount === 0"
                        [routerLink]="['/courses', course.bookId, 'browse']">
                  <mat-icon>auto_stories</mat-icon>
                  <span>{{ 'courses.browseTooltip' | translate }}</span>
                </button>
                <button mat-menu-item [class.active-item]="course.isPinned"
                        [disabled]="pinning" (click)="pinToggle.emit()">
                  <mat-icon>push_pin</mat-icon>
                  <span>{{ (course.isPinned ? 'courses.unpinTooltip' : 'courses.pinTooltip') | translate }}</span>
                </button>
                <button mat-menu-item
                        [disabled]="course.puzzleCount === 0 || savingOffline"
                        (click)="offlineToggle.emit()">
                  <mat-icon>{{ offline ? 'cloud_done' : 'cloud_download' }}</mat-icon>
                  <span>{{ (offline ? 'courses.offlineRemoveTooltip' : 'courses.offlineSaveTooltip') | translate }}</span>
                </button>
                <button mat-menu-item
                        [disabled]="course.puzzleCount === 0 || downloadingPgn" (click)="pgnDownload.emit()">
                  <mat-icon>download</mat-icon>
                  <span>{{ 'courses.downloadPgnTooltip' | translate }}</span>
                </button>
                <button mat-menu-item
                        [disabled]="course.solvedCount === 0" (click)="progressReset.emit()">
                  <mat-icon>restart_alt</mat-icon>
                  <span>{{ 'courses.resetTooltip' | translate }}</span>
                </button>
                <button mat-menu-item
                        [disabled]="converting" (click)="convertRepertoire.emit()">
                  <mat-icon>library_books</mat-icon>
                  <span>{{ 'courses.convertToRepertoireTooltip' | translate }}</span>
                </button>
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

          @if (course.puzzleCount > 0) {
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
                        <span class="chapter-name" [title]="ch.name || ('courses.noChapter' | translate)">
                          {{ ch.name || ('courses.noChapter' | translate) }}
                        </span>
                        <div class="chapter-progress">
                          <mat-progress-bar class="chapter-bar" mode="determinate" [value]="ch.progressPercent"></mat-progress-bar>
                          <span class="chapter-label">{{ ch.solvedCount }}/{{ ch.puzzleCount }}</span>
                        </div>
                        <div class="chapter-btns">
                          <button mat-icon-button [matTooltip]="'courses.browseTooltip' | translate"
                                  [routerLink]="['/courses', course.bookId, 'chapter', ch.index, 'browse']">
                            <mat-icon>auto_stories</mat-icon>
                          </button>
                          <button mat-icon-button color="primary" [matTooltip]="'courses.sequential' | translate"
                                  [routerLink]="['/courses', course.bookId, 'chapter', ch.index, 'sequential']">
                            <mat-icon>format_list_numbered</mat-icon>
                          </button>
                          <button mat-icon-button [matTooltip]="'courses.random' | translate"
                                  [routerLink]="['/courses', course.bookId, 'chapter', ch.index, 'random']">
                            <mat-icon>shuffle</mat-icon>
                          </button>
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
      color: var(--mdc-theme-primary, #3f51b5); text-decoration: none; cursor: pointer; max-width: 100%;
    }
    .linked-badge:hover { text-decoration: underline; }
    .linked-badge mat-icon { font-size: 14px; width: 14px; height: 14px; }
    .course-card {
      display: flex; flex-direction: column;
      padding: 0;
      mat-card-content { padding: 14px 16px 8px; }
    }
    .card-title { font-size: 0.95rem; font-weight: 600; line-height: 1.35; margin-bottom: 2px; }
    .card-meta {
      display: flex; align-items: center; gap: 4px; flex-wrap: wrap;
      font-size: 0.8rem; color: color-mix(in srgb, currentColor 55%, transparent);
      margin-bottom: 8px;
    }
    .meta-sep { opacity: 0.5; }
    .progress-row { display: flex; align-items: center; gap: 8px; }
    .progress-row mat-progress-bar { flex: 1; --mdc-linear-progress-track-height: 5px; --mdc-linear-progress-active-indicator-height: 5px; border-radius: 3px; }
    .progress-label { font-variant-numeric: tabular-nums; font-size: 0.78rem; color: color-mix(in srgb, currentColor 55%, transparent); white-space: nowrap; min-width: 46px; text-align: right; }
    .done-hint { display: flex; align-items: center; gap: 4px; color: #4caf50; font-size: 0.82rem; font-weight: 500; margin: 6px 0 0; }
    .done-hint mat-icon { font-size: 16px; width: 16px; height: 16px; }

    .theme-chips { display: flex; flex-wrap: wrap; gap: 4px; margin: 6px 0 0; }
    .theme-chip { font-size: 0.72rem; line-height: 1; padding: 3px 8px; border-radius: 999px;
      background: color-mix(in srgb, var(--mat-sys-primary, #1565c0) 16%, transparent);
      color: var(--mat-sys-primary, #1565c0); font-weight: 600; }

    .card-footer { padding: 0 16px 12px; border-top: 1px solid color-mix(in srgb, currentColor 8%, transparent); margin-top: 2px; }

    /* Zwei Primär-Buttons links, alle weiteren Aktionen im „⋮"-Überlaufmenü rechts.
       Nur noch drei Elemente in der Zeile → läuft auch auf schmalen Phones nicht über. */
    .action-row { display: flex; align-items: center; gap: 8px; padding-top: 8px; }
    .primary-actions { display: flex; gap: 6px; flex: 1; min-width: 0; }
    .primary-actions button { font-size: 0.82rem; height: 32px; line-height: 32px; padding: 0 10px; }
    .primary-actions mat-icon { font-size: 16px; width: 16px; height: 16px; margin-right: 4px; vertical-align: middle; }
    .util-actions { display: flex; align-items: center; flex-shrink: 0; }
    .util-actions .mat-mdc-icon-button { width: 32px; height: 32px; padding: 4px; }
    .util-actions mat-icon { font-size: 18px; }

    /* Aktionsmenü: aktive Aktion (angepinnt / verknüpft) in Primärfarbe, Löschen rot. */
    .active-item mat-icon { color: var(--mdc-theme-primary, #3f51b5); }
    .delete-item mat-icon { color: color-mix(in srgb, #e53935 80%, currentColor); }

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
    .chapter-btns { display: flex; flex-shrink: 0; }
    .chapter-btns .mat-mdc-icon-button { width: 30px; height: 30px; padding: 4px; }
    .chapter-btns mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .chapter-empty { color: color-mix(in srgb, currentColor 55%, transparent); font-style: italic; font-size: 0.82rem; margin: 4px 0 0; }
  `],
})
export class CourseCardComponent {
  @Input({ required: true }) course!: CourseListItem;
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
}
