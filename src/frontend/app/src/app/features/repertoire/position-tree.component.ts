import { ChangeDetectionStrategy, Component, EventEmitter, Input, Output } from '@angular/core';
import { CommonModule } from '@angular/common';
import { MatIconModule } from '@angular/material/icon';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';
import { PositionTreeNode } from '../../core/repertoire.service';

/**
 * Rendert den Repertoire-Zugbaum EINES Repertoires ab der gesuchten Stellung (Baummodus des
 * „Stellung in meinen Repertoires"-Panels). Die Rekursion läuft über ein `ng-template` +
 * `ngTemplateOutlet` (kein Selbst-Import der Komponente), der Pfad zur Wurzel wandert im
 * Template-Kontext mit — damit kann ein Klick die ganze Zugfolge aufs Brett spielen.
 *
 * Der Baum kommt fertig zusammengeführt vom Server (`POST /api/repertoires/position-tree`),
 * inklusive Varianten; hier wird nur dargestellt.
 */
@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-position-tree',
  standalone: true,
  imports: [CommonModule, MatIconModule, MatTooltipModule, TranslatePipe],
  template: `
    <ng-template #nodeTpl let-node let-ply="ply" let-path="path">
      <div class="pt-row">
        <button class="pt-caret" type="button" (click)="toggle(node)"
                [disabled]="!node.children.length"
                [attr.aria-label]="(isOpen(node, ply) ? 'positionInReps.tree.collapseNode' : 'positionInReps.tree.expandNode') | translate">
          @if (node.children.length) {
            <mat-icon>{{ isOpen(node, ply) ? 'expand_more' : 'chevron_right' }}</mat-icon>
          }
        </button>

        <button class="pt-move" type="button" (click)="onMoveClick(node, ply, path)"
                [matTooltip]="(canPlay ? 'positionInReps.tree.playHint' : 'positionInReps.tree.expandHint') | translate">
          <span class="pt-no">{{ movePrefix(ply) }}</span>{{ node.san }}
        </button>

        @if (node.count > 1) {
          <span class="pt-count" [matTooltip]="'positionInReps.tree.linesThrough' | translate:{ count: node.count }">{{ node.count }}</span>
        }
        @if (node.isEnd) {
          <mat-icon class="pt-end" [matTooltip]="'positionInReps.tree.lineEnd' | translate">flag</mat-icon>
        }

        @if (node.lineName || node.chapter) {
          <span class="pt-line" [matTooltip]="node.chapter || ''">{{ node.lineName || node.chapter }}</span>
          <span class="pt-actions">
            <button class="pt-icon" type="button" (click)="train.emit(node)" [matTooltip]="'positionInReps.train' | translate">
              <mat-icon>fitness_center</mat-icon>
            </button>
            <button class="pt-icon" type="button" (click)="view.emit(node)" [matTooltip]="'positionInReps.view' | translate">
              <mat-icon>visibility</mat-icon>
            </button>
          </span>
        }
      </div>

      @if (isOpen(node, ply) && node.children.length) {
        <div class="pt-children">
          @for (child of node.children; track child.san) {
            <ng-container *ngTemplateOutlet="nodeTpl; context: { $implicit: child, ply: ply + 1, path: path.concat(child.san) }"></ng-container>
          }
        </div>
      }
    </ng-template>

    @for (node of nodes; track node.san) {
      <ng-container *ngTemplateOutlet="nodeTpl; context: { $implicit: node, ply: 0, path: [node.san] }"></ng-container>
    }
  `,
  styles: [`
    :host { display: block; }
    .pt-row { display: flex; align-items: center; gap: 4px; min-height: 26px; }
    .pt-children { margin-left: 14px; border-left: 1px solid color-mix(in srgb, currentColor 12%, transparent); padding-left: 4px; }
    .pt-caret { width: 20px; height: 20px; padding: 0; border: none; background: none; color: inherit; cursor: pointer; display: inline-flex; align-items: center; justify-content: center; flex: 0 0 auto; }
    .pt-caret:disabled { cursor: default; }
    .pt-caret mat-icon { font-size: 18px; width: 18px; height: 18px; }
    .pt-move { font: inherit; font-variant-numeric: tabular-nums; background: none; border: none; color: inherit; cursor: pointer; padding: 1px 4px; border-radius: 4px; white-space: nowrap; }
    .pt-move:hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    .pt-no { color: color-mix(in srgb, currentColor 50%, transparent); margin-right: 3px; }
    .pt-count { font-size: .68rem; background: color-mix(in srgb, currentColor 14%, transparent); border-radius: 9px; padding: 0 6px; flex: 0 0 auto; }
    .pt-end { font-size: 14px; width: 14px; height: 14px; color: color-mix(in srgb, currentColor 45%, transparent); }
    .pt-line { font-size: .74rem; color: color-mix(in srgb, currentColor 58%, transparent); overflow: hidden; text-overflow: ellipsis; white-space: nowrap; min-width: 0; }
    .pt-actions { display: inline-flex; flex: 0 0 auto; }
    .pt-icon { width: 24px; height: 24px; padding: 0; border: none; background: none; color: inherit; cursor: pointer; display: inline-flex; align-items: center; justify-content: center; }
    .pt-icon:hover { background: color-mix(in srgb, currentColor 12%, transparent); border-radius: 4px; }
    .pt-icon mat-icon { font-size: 16px; width: 16px; height: 16px; }
  `]
})
export class PositionTreeComponent {
  @Input() nodes: PositionTreeNode[] = [];
  /** Zugnummer der Stellung (FEN-Feld 6) — für die Nummerierung im Baum. */
  @Input() startMoveNumber = 1;
  /** true, wenn an der Stellung Schwarz am Zug ist (FEN-Feld 2). */
  @Input() blackToMove = false;
  /** true = Klick auf einen Zug spielt ihn aufs Brett (nur wo ein Brett zuhört). */
  @Input() canPlay = false;
  /** Bis zu welcher Tiefe standardmäßig aufgeklappt ist. */
  @Input() defaultOpenDepth = 2;

  /** SAN-Pfad von der gesuchten Stellung bis zum geklickten Zug. */
  @Output() playPath = new EventEmitter<string[]>();
  @Output() train = new EventEmitter<PositionTreeNode>();
  @Output() view = new EventEmitter<PositionTreeNode>();

  /** Explizit vom Nutzer umgeschaltete Knoten (Objekt-Identität; setzt sich beim Neuladen zurück). */
  private readonly toggled = new Map<PositionTreeNode, boolean>();

  isOpen(node: PositionTreeNode, ply: number): boolean {
    const explicit = this.toggled.get(node);
    return explicit ?? ply < this.defaultOpenDepth;
  }

  toggle(node: PositionTreeNode): void {
    if (!node.children.length) return;
    // Der aktuelle Zustand hängt von der Tiefe ab — die kennt der Aufrufer, nicht wir.
    // Deshalb den gespeicherten Wert kippen bzw. beim ersten Mal aus dem Default ableiten.
    const current = this.toggled.get(node);
    this.toggled.set(node, current === undefined ? !this.wasOpenByDefault(node) : !current);
  }

  onMoveClick(node: PositionTreeNode, ply: number, path: string[]): void {
    if (this.canPlay) { this.playPath.emit(path); return; }
    this.toggle(node);
  }

  /** Präfix „12." bzw. „12…" vor dem Zug, abhängig von Halbzug-Abstand zur Stellung. */
  movePrefix(ply: number): string {
    const whiteMove = this.blackToMove ? ply % 2 === 1 : ply % 2 === 0;
    const moveNo = this.startMoveNumber + Math.floor((ply + (this.blackToMove ? 1 : 0)) / 2);
    return whiteMove ? `${moveNo}.` : (ply === 0 ? `${moveNo}…` : '');
  }

  /** Default-Zustand eines Knotens, wenn der Nutzer ihn noch nie angefasst hat. */
  private wasOpenByDefault(node: PositionTreeNode): boolean {
    const depth = this.depthOf(node, this.nodes, 0);
    return depth >= 0 && depth < this.defaultOpenDepth;
  }

  private depthOf(target: PositionTreeNode, list: PositionTreeNode[], depth: number): number {
    for (const n of list) {
      if (n === target) return depth;
      const found = this.depthOf(target, n.children, depth + 1);
      if (found >= 0) return found;
    }
    return -1;
  }
}
