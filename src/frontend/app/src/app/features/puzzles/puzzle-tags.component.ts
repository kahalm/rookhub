import { ChangeDetectionStrategy, Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { TranslatePipe } from '@ngx-translate/core';

@Component({
  selector: 'app-puzzle-tags',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [CommonModule, TranslatePipe],
  template: `
    @if (tagList.length) {
      <span class="puzzle-tags-toggle" role="button" tabindex="0" [attr.aria-expanded]="expanded"
            (click)="expanded = !expanded"
            (keydown.enter)="expanded = !expanded" (keydown.space)="$event.preventDefault(); expanded = !expanded">
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
    /* Akzent-Token statt des Hellthema-Blaus #1976d2 (im Standard-Dunkelmodus 3,7:1, UX-008). */
    .puzzle-tags-toggle {
      font-size: 0.8em; color: var(--rh-accent); cursor: pointer; user-select: none;
    }
    .puzzle-tags-toggle:hover { text-decoration: underline; }
    /* Grober Zeiger (Codereview UX-007): der 0.8em-Text allein ist kein Touch-Ziel — Innenabstand bis 44 px. */
    @media (pointer: coarse) {
      .puzzle-tags-toggle { display: inline-flex; align-items: center; min-height: 44px; padding: 0 0.5rem; }
    }
    .puzzle-tags-chips { display: flex; flex-wrap: wrap; gap: 0.25rem; }
    .puzzle-tags-chip {
      background: color-mix(in srgb, currentColor 8%, transparent); border-radius: 12px; padding: 2px 10px;
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
