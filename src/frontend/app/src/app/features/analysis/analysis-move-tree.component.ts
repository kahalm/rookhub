import {
  AfterViewChecked, ChangeDetectionStrategy, ChangeDetectorRef, Component, ElementRef, EventEmitter, Input, OnChanges, OnDestroy,
  Output, SimpleChanges, ViewChild, inject, signal,
} from '@angular/core';
import { NgTemplateOutlet } from '@angular/common';
import { MatMenuModule, MatMenuTrigger } from '@angular/material/menu';
import { MatIconModule } from '@angular/material/icon';
import { TranslatePipe } from '@ngx-translate/core';
import { AnalysisNode, MoveRow, MoveTableItem, VariationBlock, buildMoveTable, isMainline } from './analysis-tree';

export type MoveTreeAction = 'star' | 'promote' | 'mainline' | 'delete';

/** So lange muss ein Finger auf einem Zug liegen, bis das Menü aufgeht (iOS kennt kein `contextmenu` bei langem Druck). */
const LONG_PRESS_MS = 500;
/** Bewegt sich der Finger weiter, ist es ein Wischen (Liste scrollen), kein langer Druck. */
const LONG_PRESS_SLOP_PX = 10;

/**
 * Die Zugliste des Analysebretts als Zugbaum (0.604.0, Wunsch 2026-09-29 mit Screenshot des Lichess-Analysebretts: „so
 * hätt ichs bei uns auch gern in der Analyse — inkl. der Variationen + Hauptlinie — + Rechtsklick Variante hochstufen/
 * löschen"): die Hauptlinie als Tabelle Zugnummer | Weiß | Schwarz mit der zuletzt gesehenen Bewertung je Zug, Varianten
 * als Block, der die Tabelle unterbricht, Untervarianten in Klammern. Rechtsklick (am Handy: lange drücken) öffnet ein
 * Menü: Stern, Variante hochstufen, zur Hauptvariante machen, ab hier löschen.
 *
 * Der Baum gehört dem Analysebrett und wird dort verändert — `version` sagt dieser Ansicht, dass sie neu aufbauen soll.
 */
@Component({
  selector: 'app-analysis-move-tree',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [NgTemplateOutlet, MatMenuModule, MatIconModule, TranslatePipe],
  template: `
    <div class="tree" #list>
      @for (item of items; track $index) {
        @if (item.kind === 'row') {
          @let r = asRow(item);
          <div class="row">
            <span class="no">{{ r.number }}</span>
            <ng-container *ngTemplateOutlet="cell; context: { $implicit: r.white }" />
            <ng-container *ngTemplateOutlet="cell; context: { $implicit: r.black }" />
          </div>
        } @else {
          @let v = asBlock(item);
          <div class="vars">
            @for (line of v.lines; track $index) {
              <div class="var-line">
                @for (t of line; track $index) {
                  @if (t.kind === 'move') {
                    <span class="vmove" [class.active]="t.node === current" [class.starred]="t.node.starred"
                          (click)="pick(t.node)" (contextmenu)="onContextMenu($event, t.node)"
                          (pointerdown)="onPointerDown($event, t.node)" (pointermove)="onPointerMove($event)"
                          (pointerup)="clearPress()" (pointercancel)="clearPress()">{{ t.label }}@if (t.node.starred) {<span class="star">★</span>}</span>
                  } @else if (t.kind === 'open') {
                    <span class="paren">(</span>
                  } @else {
                    <span class="paren">)</span>
                  }
                }
              </div>
            }
          </div>
        }
      }
    </div>

    <ng-template #cell let-c>
      @if (c === 'gap') {
        <span class="cell gap">…</span>
      } @else if (c) {
        <span class="cell move" [class.active]="c === current" [class.starred]="c.starred"
              (click)="pick(c)" (contextmenu)="onContextMenu($event, c)"
              (pointerdown)="onPointerDown($event, c)" (pointermove)="onPointerMove($event)"
              (pointerup)="clearPress()" (pointercancel)="clearPress()">
          <span class="san">{{ c.san }}@if (c.starred) {<span class="star">★</span>}</span>
          @if (c.evalText) { <span class="ev">{{ c.evalText }}</span> }
        </span>
      } @else {
        <span class="cell"></span>
      }
    </ng-template>

    <!-- Anker des Menüs: steht genau dort, wo geklickt bzw. gedrückt wurde. -->
    <span class="ctx-anchor" [style.left.px]="menuAt().x" [style.top.px]="menuAt().y"
          [matMenuTriggerFor]="ctxMenu" #trigger="matMenuTrigger"></span>
    <mat-menu #ctxMenu="matMenu">
      @if (menuNode(); as n) {
        <button mat-menu-item type="button" (click)="act('star', n)">
          <mat-icon>{{ n.starred ? 'star' : 'star_border' }}</mat-icon>
          {{ (n.starred ? 'analysis.star.remove' : 'analysis.star.add') | translate }}
        </button>
        @if (!onMainline(n)) {
          <button mat-menu-item type="button" (click)="act('promote', n)">
            <mat-icon>arrow_upward</mat-icon> {{ 'analysis.tree.promote' | translate }}
          </button>
          <button mat-menu-item type="button" (click)="act('mainline', n)">
            <mat-icon>vertical_align_top</mat-icon> {{ 'analysis.tree.makeMainline' | translate }}
          </button>
        }
        <button mat-menu-item type="button" (click)="act('delete', n)">
          <mat-icon>delete_outline</mat-icon> {{ 'analysis.tree.deleteFrom' | translate }}
        </button>
      }
    </mat-menu>
  `,
  styles: [`
    :host { display: block; }
    .tree { max-height: 420px; overflow-y: auto; border-radius: 4px; font-variant-numeric: tabular-nums; }
    .row { display: grid; grid-template-columns: 2.6em 1fr 1fr; align-items: stretch; }
    .no { color: color-mix(in srgb, currentColor 45%, transparent); font-size: .85rem; padding: 4px 6px; text-align: center;
      background: color-mix(in srgb, currentColor 5%, transparent); }
    .cell { display: flex; align-items: baseline; justify-content: space-between; gap: 6px; padding: 4px 8px; min-width: 0;
      font-family: 'Courier New', monospace; }
    .cell.gap { color: color-mix(in srgb, currentColor 40%, transparent); }
    .move, .vmove { cursor: pointer; user-select: none; -webkit-user-select: none; -webkit-touch-callout: none;
      touch-action: manipulation; }
    .move:hover, .vmove:hover { background: color-mix(in srgb, currentColor 8%, transparent); }
    .move.active, .vmove.active { background: #1976d2; color: #fff; }
    .move.active .ev { color: rgba(255, 255, 255, .85); }
    .san { font-weight: 600; white-space: nowrap; }
    .ev { font-size: .78rem; color: color-mix(in srgb, currentColor 55%, transparent); white-space: nowrap; }
    .star { color: #f9a825; font-size: .75rem; margin-left: 2px; }
    .vars { padding: 4px 8px 6px 10px; border-left: 2px solid color-mix(in srgb, currentColor 18%, transparent);
      background: color-mix(in srgb, currentColor 4%, transparent); font-size: .88rem; }
    .var-line { line-height: 1.8; }
    .var-line + .var-line { border-top: 1px dashed color-mix(in srgb, currentColor 12%, transparent); }
    .vmove { font-family: 'Courier New', monospace; padding: 1px 3px; border-radius: 3px; margin-right: .2em; }
    .paren { color: color-mix(in srgb, currentColor 45%, transparent); margin-right: .2em; }
    .ctx-anchor { position: fixed; width: 0; height: 0; pointer-events: none; }
  `],
})
export class AnalysisMoveTreeComponent implements OnChanges, AfterViewChecked, OnDestroy {
  private readonly cdr = inject(ChangeDetectorRef);

  @Input({ required: true }) root!: AnalysisNode;
  /** Der Knoten, dessen Stellung auf dem Brett steht (die Wurzel = Ausgangsstellung). */
  @Input({ required: true }) current!: AnalysisNode;
  /** Zählt jede Änderung am Baum (Zug, Variante, Stern, Bewertung) — der Baum selbst wird an Ort und Stelle verändert. */
  @Input() version = 0;

  @Output() readonly select = new EventEmitter<AnalysisNode>();
  @Output() readonly action = new EventEmitter<{ kind: MoveTreeAction; node: AnalysisNode }>();

  @ViewChild('trigger') private trigger?: MatMenuTrigger;
  @ViewChild('list') private list?: ElementRef<HTMLElement>;

  items: MoveTableItem[] = [];
  readonly menuNode = signal<AnalysisNode | null>(null);
  readonly menuAt = signal({ x: 0, y: 0 });

  private pressTimer: ReturnType<typeof setTimeout> | null = null;
  private pressStart: { x: number; y: number } | null = null;
  private suppressClickUntil = 0;
  private menuOpenedAt = 0;
  private scrolledTo: AnalysisNode | null = null;

  ngOnChanges(changes: SimpleChanges): void {
    // Nur der aktuelle Zug gewechselt: die Tabelle bleibt, es wandert nur die Markierung.
    if (changes['root'] || changes['version']) this.items = this.root ? buildMoveTable(this.root) : [];
  }

  /** Den aktuellen Zug in der Liste sichtbar halten — nur die Liste scrollt, nie die Seite. */
  ngAfterViewChecked(): void {
    if (this.scrolledTo === this.current) return;
    this.scrolledTo = this.current;
    const list = this.list?.nativeElement;
    const active = list?.querySelector<HTMLElement>('.active');
    if (!list || !active) return;
    const top = active.offsetTop - list.offsetTop;
    const bottom = top + active.offsetHeight;
    if (top < list.scrollTop) list.scrollTop = top - 4;
    else if (bottom > list.scrollTop + list.clientHeight) list.scrollTop = bottom - list.clientHeight + 4;
  }

  ngOnDestroy(): void { this.clearPress(); }

  asRow(item: MoveTableItem): MoveRow { return item as MoveRow; }
  asBlock(item: MoveTableItem): VariationBlock { return item as VariationBlock; }
  onMainline(node: AnalysisNode): boolean { return isMainline(node); }

  pick(node: AnalysisNode): void {
    if (Date.now() < this.suppressClickUntil) return;   // der Finger hob sich nach einem langen Druck
    this.select.emit(node);
  }

  act(kind: MoveTreeAction, node: AnalysisNode): void { this.action.emit({ kind, node }); }

  /** Rechtsklick — und am Android-Handy auch der lange Druck, dort schickt der Browser selbst ein `contextmenu`. */
  onContextMenu(e: MouseEvent, node: AnalysisNode): void {
    e.preventDefault();
    this.clearPress();
    if (Date.now() - this.menuOpenedAt < 800) return;   // der eigene lange Druck war schneller
    this.openMenu(node, e.clientX, e.clientY);
  }

  onPointerDown(e: PointerEvent, node: AnalysisNode): void {
    if (e.pointerType !== 'touch') return;
    this.clearPress();
    const x = e.clientX, y = e.clientY;
    this.pressStart = { x, y };
    this.pressTimer = setTimeout(() => {
      this.pressTimer = null;
      this.pressStart = null;
      this.suppressClickUntil = Date.now() + 800;
      this.openMenu(node, x, y);
    }, LONG_PRESS_MS);
  }

  onPointerMove(e: PointerEvent): void {
    const s = this.pressStart;
    if (s && Math.hypot(e.clientX - s.x, e.clientY - s.y) > LONG_PRESS_SLOP_PX) this.clearPress();
  }

  clearPress(): void {
    if (this.pressTimer) clearTimeout(this.pressTimer);
    this.pressTimer = null;
    this.pressStart = null;
  }

  private openMenu(node: AnalysisNode, x: number, y: number): void {
    this.menuOpenedAt = Date.now();
    this.menuNode.set(node);
    this.menuAt.set({ x, y });
    this.cdr.detectChanges();   // der Anker muss stehen, bevor das Menü an ihm ausgerichtet wird
    if (this.trigger?.menuOpen) this.trigger.updatePosition();
    else this.trigger?.openMenu();
  }
}
