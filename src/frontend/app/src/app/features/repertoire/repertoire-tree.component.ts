import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { TranslateModule } from '@ngx-translate/core';
import { TreeChild, Breadcrumb } from './move-tree.service';

@Component({
  selector: 'app-repertoire-tree',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatButtonModule, TranslateModule],
  template: `
    <div class="tree-container">
      <div class="breadcrumbs">
        <span class="crumb" (click)="goToRoot.emit()">{{ 'repertoire.tree.start' | translate }}</span>
        @for (crumb of breadcrumbs; track crumb.depth) {
          <mat-icon class="crumb-sep">chevron_right</mat-icon>
          <span class="crumb" (click)="goToDepth.emit(crumb.depth)">{{ crumb.san }}</span>
        }
      </div>

      @if (breadcrumbs.length > 0) {
        <button mat-button (click)="goUp.emit()" class="back-btn">
          <mat-icon>arrow_back</mat-icon> {{ 'common.back' | translate }}
        </button>
      }

      <div class="children-list">
        @for (child of children; track child.san) {
          <div class="child-item" (click)="nodeSelected.emit(child.san)">
            <span class="child-san">{{ child.san }}</span>
            <span class="child-count">{{ (child.count === 1 ? 'repertoire.tree.lineCount' : 'repertoire.tree.lineCountPlural') | translate: { count: child.count } }}</span>
          </div>
        } @empty {
          <div class="empty">{{ 'repertoire.tree.empty' | translate }}</div>
        }
      </div>
    </div>
  `,
  styles: [`
    :host { display: block; height: 100%; }
    .tree-container { display: flex; flex-direction: column; height: 100%; }
    .breadcrumbs {
      display: flex;
      align-items: center;
      flex-wrap: wrap;
      gap: 2px;
      padding: 8px 12px;
      border-bottom: 1px solid #e0e0e0;
      font-size: 14px;
      min-height: 40px;
    }
    .crumb {
      cursor: pointer;
      padding: 2px 6px;
      border-radius: 4px;
      font-weight: 500;
    }
    .crumb:hover { background: #e0e0e0; }
    .crumb-sep { font-size: 18px; width: 18px; height: 18px; color: #999; }
    .back-btn { align-self: flex-start; margin: 4px; }
    .children-list { flex: 1; overflow-y: auto; }
    .child-item {
      display: flex;
      justify-content: space-between;
      align-items: center;
      padding: 12px 16px;
      border-bottom: 1px solid #e0e0e0;
      cursor: pointer;
      transition: background 0.15s;
    }
    .child-item:hover { background: #f5f5f5; }
    .child-san {
      font-family: 'Roboto Mono', monospace;
      font-weight: 600;
      font-size: 15px;
    }
    .child-count { color: #666; font-size: 13px; }
    .empty { padding: 2rem; text-align: center; color: #888; }
  `]
})
export class RepertoireTreeComponent {
  @Input() children: TreeChild[] = [];
  @Input() breadcrumbs: Breadcrumb[] = [];

  @Output() nodeSelected = new EventEmitter<string>();
  @Output() goUp = new EventEmitter<void>();
  @Output() goToRoot = new EventEmitter<void>();
  @Output() goToDepth = new EventEmitter<number>();
}
