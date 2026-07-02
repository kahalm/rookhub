import { Component, Input, Output, EventEmitter, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink } from '@angular/router';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslateModule } from '@ngx-translate/core';
import { Move } from 'chess.js';
import { MoveListComponent } from '../../shared/pgn-viewer/move-list.component';
import { RepertoireLine } from './repertoire-viewer.service';

/** Ein Chapter-Bucket mit seinen Linien. Reihenfolge = erstes Auftreten im PGN. */
interface ChapterGroup {
  chapter: string;
  lines: RepertoireLine[];
  /** Angezeigt = false klappt zusammen. */
  expanded: boolean;
}

@Component({
  selector: 'app-repertoire-lines',
  standalone: true,
  imports: [
    CommonModule, RouterLink, MatListModule, MatIconModule, MatButtonModule, MatTooltipModule,
    TranslateModule, MoveListComponent,
  ],
  template: `
    @if (selectedIndex >= 0) {
      <div class="move-view">
        <button mat-button (click)="lineDeselected.emit()" class="back-btn">
          <mat-icon>arrow_back</mat-icon> {{ 'repertoire.lines.allLines' | translate }}
        </button>
        <div class="line-header">
          <span class="players">{{ lines[selectedIndex].white }} vs {{ lines[selectedIndex].black }}</span>
          <span class="result">{{ lines[selectedIndex].result }}</span>
        </div>
        <div class="move-list-wrap">
          <app-move-list
            [moves]="moves"
            [currentMoveIndex]="currentMoveIndex"
            [comments]="comments"
            (moveClicked)="moveClicked.emit($event)" />
        </div>
      </div>
    } @else {
      <div class="lines-list">
        @if (chapterGroups().length === 0) {
          <div class="empty">{{ 'repertoire.lines.empty' | translate }}</div>
        } @else {
          @for (group of chapterGroups(); track group.chapter) {
            <div class="chapter-block">
              <div class="chapter-head">
                <button class="chapter-toggle" (click)="toggleChapter(group.chapter)"
                        [attr.aria-expanded]="group.expanded"
                        [attr.aria-label]="'repertoire.lines.toggleChapter' | translate">
                  <mat-icon>{{ group.expanded ? 'expand_more' : 'chevron_right' }}</mat-icon>
                  <span class="chapter-name">{{ group.chapter || ('repertoire.lines.noChapter' | translate) }}</span>
                  <span class="chapter-count">{{ group.lines.length }}</span>
                </button>
                @if (repertoireId != null && group.chapter) {
                  <a mat-icon-button [routerLink]="['/repertoires', repertoireId, 'train']"
                     [queryParams]="{ chapter: group.chapter }"
                     [matTooltip]="'repertoire.lines.trainChapter' | translate"
                     [attr.aria-label]="'repertoire.lines.trainChapter' | translate">
                    <mat-icon>school</mat-icon>
                  </a>
                }
              </div>
              @if (group.expanded) {
                @for (line of group.lines; track line.gameIndex) {
                  <div class="line-item" role="button" tabindex="0"
                       (click)="lineSelected.emit(line.gameIndex)"
                       (keydown.enter)="lineSelected.emit(line.gameIndex)"
                       (keydown.space)="$event.preventDefault(); lineSelected.emit(line.gameIndex)">
                    <div class="line-players">
                      <span>{{ line.white }} vs {{ line.black }}</span>
                      <span class="line-result">{{ line.result }}</span>
                    </div>
                    @if (line.opening) {
                      <div class="line-opening">{{ line.opening }}</div>
                    }
                    <div class="line-summary">{{ line.summary }}</div>
                  </div>
                }
              }
            </div>
          }
        }
      </div>
    }
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .lines-list { overflow-y: auto; height: 100%; }
    .chapter-block { border-bottom: 1px solid color-mix(in srgb, currentColor 8%, transparent); }
    .chapter-head { display: flex; align-items: center; gap: 4px; padding: 4px 8px 4px 4px;
      background: color-mix(in srgb, currentColor 5%, transparent); }
    .chapter-toggle { flex: 1; display: flex; align-items: center; gap: 4px;
      background: none; border: none; padding: 8px 4px; text-align: left; cursor: pointer;
      font: inherit; color: inherit; }
    .chapter-toggle:hover { background: color-mix(in srgb, currentColor 6%, transparent); }
    .chapter-name { font-weight: 600; flex: 1; }
    .chapter-count { color: color-mix(in srgb, currentColor 55%, transparent); font-size: 12px;
      padding: 2px 8px; border-radius: 999px;
      background: color-mix(in srgb, currentColor 10%, transparent); }
    .line-item { padding: 10px 16px 10px 32px; border-top: 1px solid color-mix(in srgb, currentColor 6%, transparent);
      cursor: pointer; transition: background 0.15s; }
    .line-item:hover { background: color-mix(in srgb, currentColor 4%, transparent); }
    .line-players { display: flex; justify-content: space-between; align-items: center;
      font-weight: 500; font-size: 14px; }
    .line-result { color: #1976d2; font-weight: 600; font-size: 13px; }
    .line-opening { color: color-mix(in srgb, currentColor 60%, transparent); font-size: 12px; margin-top: 2px; }
    .line-summary { font-family: 'Roboto Mono', monospace; font-size: 12px;
      color: color-mix(in srgb, currentColor 47%, transparent); margin-top: 4px;
      white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .move-view { display: flex; flex-direction: column; height: 100%; }
    .back-btn { align-self: flex-start; margin: 4px; }
    .line-header { display: flex; justify-content: space-between; padding: 4px 16px 8px;
      font-size: 14px; border-bottom: 1px solid color-mix(in srgb, currentColor 12%, transparent); }
    .players { font-weight: 500; }
    .result { color: #1976d2; font-weight: 600; }
    .move-list-wrap { flex: 1; overflow: hidden; }
    .empty { padding: 2rem; text-align: center; color: color-mix(in srgb, currentColor 47%, transparent); }
  `],
})
export class RepertoireLinesComponent {
  @Input() lines: RepertoireLine[] = [];
  @Input() selectedIndex = -1;
  @Input() moves: Move[] = [];
  @Input() currentMoveIndex = -1;
  @Input() comments: { [moveIndex: number]: string } = {};
  /** Repertoire-Id (für Deep-Link auf /repertoires/:id/train?chapter=…). */
  @Input() repertoireId: number | null = null;

  @Output() lineSelected = new EventEmitter<number>();
  @Output() lineDeselected = new EventEmitter<void>();
  @Output() moveClicked = new EventEmitter<number>();

  /** Aufgeklappte Kapitel — Set an Chapter-Names. Persistiert bewusst NICHT (Ansicht ist ephemer). */
  private collapsed = signal<Set<string>>(new Set());

  /** Linien nach Kapitel gruppiert (Reihenfolge = erstes Auftreten im PGN). */
  readonly chapterGroups = computed<ChapterGroup[]>(() => {
    const collapsed = this.collapsed();
    const map = new Map<string, RepertoireLine[]>();
    for (const line of this.lines) {
      const key = line.chapter || '';
      let bucket = map.get(key);
      if (!bucket) { bucket = []; map.set(key, bucket); }
      bucket.push(line);
    }
    return [...map.entries()].map(([chapter, lines]) => ({
      chapter,
      lines,
      expanded: !collapsed.has(chapter),
    }));
  });

  toggleChapter(chapter: string): void {
    const next = new Set(this.collapsed());
    if (next.has(chapter)) next.delete(chapter); else next.add(chapter);
    this.collapsed.set(next);
  }
}
