import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslateModule } from '@ngx-translate/core';

@Component({
  selector: 'app-puzzle-tags',
  standalone: true,
  imports: [CommonModule, TranslateModule],
  template: `
    @if (tagList.length) {
      <span class="puzzle-tags-toggle" (click)="expanded = !expanded">
        {{ (expanded ? 'endless.game.hideTags' : 'endless.game.showTags') | translate }}
      </span>
      @if (expanded) {
        <div class="puzzle-tags-chips">
          @for (t of tagList; track t) {
            <span class="puzzle-tags-chip">{{ t }}</span>
          }
        </div>
      }
    }
  `,
  styles: [`
    :host { display: contents; }
    .puzzle-tags-toggle {
      font-size: 0.8em; color: #1976d2; cursor: pointer; user-select: none;
    }
    .puzzle-tags-toggle:hover { text-decoration: underline; }
    .puzzle-tags-chips { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .puzzle-tags-chip {
      background: rgba(0,0,0,0.08); border-radius: 12px; padding: 2px 10px;
      font-size: 0.85em; white-space: nowrap;
    }
  `],
})
export class PuzzleTagsComponent {
  /** Space-separated tag string (z. B. "fork pin mate"). Leer/null = nichts anzeigen. */
  @Input() set tags(value: string | null | undefined) {
    this.tagList = (value || '').split(' ').filter(t => t);
    this.expanded = false;   // Bei neuem Puzzle Default = ausgeblendet
  }

  tagList: string[] = [];
  expanded = false;
}
