import {
  Component, Input, Output, EventEmitter, OnChanges,
  SimpleChanges, ElementRef, ViewChild, ChangeDetectionStrategy, input, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Move } from 'chess.js';
import { TranslateService } from '@ngx-translate/core';
import { SanPipe } from '../chess/san.pipe';
import { contrastText } from '../contrast-text.util';

/** Ein Stück Kommentartext; mit `move` ein anklickbarer Zug, dessen Stellung `fen` ist. */
export interface MoveListCommentSegment { text?: string; move?: string; fen?: string; from?: string; to?: string; }

/** Ein Zeichen hinter einem Zug der Liste: Symbol, Farbe des Schilds, Klartext für den Tooltip. */
export interface MoveListMark {
  symbol: string;
  color: string;
  label?: string;
}

@Component({
  changeDetection: ChangeDetectionStrategy.Default,
  selector: 'app-move-list',
  standalone: true,
  // UI-Sweep 2026-10-11 (r2-rg-notation): Züge in den Figurenbuchstaben der Oberflächensprache.
  imports: [CommonModule, SanPipe],
  template: `
    <ng-template #commentTpl let-idx>
      @for (seg of segmentsFor(idx); track $index) {
        @if (seg.move) {
          <button type="button" class="cmt-move" (click)="commentMoveClicked.emit(seg)">{{ seg.move }}</button>
        } @else {
          <span>{{ seg.text }}</span>
        }
      }
    </ng-template>
    <ng-template #markTpl let-i>
      @if (marks()?.[i]; as m) {
        <span class="mark" [style.background]="m.color" [style.color]="markText(m.color)" [attr.title]="m.label ?? null">{{ m.symbol }}</span>
      }
    </ng-template>
    <!-- F8-018: Liste mit EINER Tab-Station (roving tabindex) — Pfeiltasten wandern von Zug zu Zug, Enter/Leertaste wählt. -->
    <div class="move-list" #moveListEl role="listbox" [attr.aria-label]="label()" (keydown)="onKeydown($event)">
      @if (comments[-1]) {
        <div class="comment-row comment-intro" role="none">
          <ng-container *ngTemplateOutlet="commentTpl; context: { $implicit: -1 }"></ng-container>
        </div>
      }
      @for (pair of movePairs; track $index) {
        <div class="move-row" role="none" [class.row-active]="isRowActive(pair)">
          <span class="move-number" role="none">{{ pair.number }}.</span>
          @if (pair.white !== undefined) {
            <button type="button" class="move" role="option" [class.active]="pair.whiteIndex === currentMoveIndex"
                    [attr.data-index]="pair.whiteIndex" [attr.tabindex]="pair.whiteIndex === tabStop() ? 0 : -1"
                    [attr.aria-selected]="pair.whiteIndex === currentMoveIndex"
                    [attr.aria-current]="pair.whiteIndex === currentMoveIndex ? 'true' : null"
                    (click)="onMoveClick(pair.whiteIndex, $event)">{{ pair.white | san }}<ng-container *ngTemplateOutlet="markTpl; context: { $implicit: pair.whiteIndex }"></ng-container></button>
          } @else {
            <span class="move-empty" role="none"></span>
          }
          @if (pair.black) {
            <button type="button" class="move" role="option" [class.active]="pair.blackIndex === currentMoveIndex"
                    [attr.data-index]="pair.blackIndex" [attr.tabindex]="pair.blackIndex === tabStop() ? 0 : -1"
                    [attr.aria-selected]="pair.blackIndex === currentMoveIndex"
                    [attr.aria-current]="pair.blackIndex === currentMoveIndex ? 'true' : null"
                    (click)="onMoveClick(pair.blackIndex!, $event)">{{ pair.black | san }}<ng-container *ngTemplateOutlet="markTpl; context: { $implicit: pair.blackIndex }"></ng-container></button>
          } @else {
            <span class="move-empty" role="none"></span>
          }
        </div>
        @if (commentIndex(pair) !== null) {
          <div class="comment-row" role="none">
            <ng-container *ngTemplateOutlet="commentTpl; context: { $implicit: commentIndex(pair) }"></ng-container>
          </div>
        }
      }
    </div>
  `,
  styles: [`
    /* Zugliste wie ein Partieformular: Nummern-Spalte links abgesetzt, Züge in der Textschrift mit Tabellenziffern
       (die Monospace-Schrift war nirgends geladen und fiel auf die Systemschrift zurück), kompakte Zeilen. */
    .move-list {
      font-size: 14px;
      font-variant-numeric: tabular-nums;
      overflow-y: auto;
      height: 100%;
      padding: 0;
    }
    .move-row {
      display: grid;
      grid-template-columns: 40px 1fr 1fr;
      align-items: stretch;
      border-bottom: 1px solid color-mix(in srgb, currentColor 6%, transparent);
    }
    .move-row.row-active .move-number {
      color: inherit;
      background: color-mix(in srgb, currentColor 10%, transparent);
    }
    .move-number {
      display: flex; align-items: center; justify-content: center;
      color: color-mix(in srgb, currentColor 45%, transparent);
      background: color-mix(in srgb, currentColor 4%, transparent);
      font-size: 12px;
      user-select: none;
    }
    .move {
      /* Ein <button> (F8-018: per Tastatur erreichbar, aktiver Zug mit aria-current), sieht aber aus wie der
         frühere <span>: Knopf-Vorgaben zurücksetzen, font VOR line-height (die Kurzform setzt sie mit). */
      font: inherit; color: inherit; background: none; border: 0; margin: 0;
      display: block; width: 100%; box-sizing: border-box; text-align: left;
      cursor: pointer;
      padding: 4px 12px;
      font-weight: 500;
      line-height: 1.5;
    }
    .move:hover { background: color-mix(in srgb, currentColor 10%, transparent); }
    /* Sichtbarer Fokus (F8-018) in der Schriftfarbe — auf dem farbigen aktiven Zug also hell. */
    .move:focus-visible { outline: 2px solid currentColor; outline-offset: -3px; }
    .move.active { background: var(--rh-accent, #1976d2); color: var(--mat-sys-on-primary, #fff); }
    .move-empty { display: block; }
    /* Zug-Klasse aus der Engine-Auswertung (0.836.0): kleines farbiges Schild hinter dem Zug, die Schrift schwarz oder
       weiß je nach Schildfarbe (markText) — lesbar auf hellem, dunklem und dem farbigen aktiven Grund. */
    .mark {
      display: inline-block; margin-left: 6px; padding: 0 5px; border-radius: 8px;
      font-size: 11px; font-weight: 700; line-height: 16px; vertical-align: 1px;
    }
    .comment-intro { padding: 6px; }
    .cmt-move {
      font: inherit; font-style: normal; font-weight: 600; cursor: pointer;
      color: var(--mat-sys-primary, #1565c0);
      background: color-mix(in srgb, currentColor 10%, transparent);
      border: 1px solid color-mix(in srgb, currentColor 35%, transparent);
      border-radius: 4px; padding: 0 4px; margin: 0 1px; white-space: nowrap;
    }
    .cmt-move:hover { background: color-mix(in srgb, currentColor 22%, transparent); }
    .comment-row {
      padding: 4px 10px 6px 50px;
      border-bottom: 1px solid color-mix(in srgb, currentColor 6%, transparent);
      color: color-mix(in srgb, currentColor 60%, transparent);
      font-style: italic;
      font-size: 12px;
      font-family: 'Roboto', sans-serif;
      line-height: 1.5;
    }
  `]
})
export class MoveListComponent implements OnChanges {
  @Input() moves: Move[] = [];
  @Input() currentMoveIndex = -1;
  @Input() comments: { [moveIndex: number]: string } = {};
  /** Optional: Kommentare in Stücken mit anklickbaren Zügen (Schlüssel wie `comments`). Ohne → reiner Text. */
  @Input() commentSegments: { [moveIndex: number]: MoveListCommentSegment[] } | null = null;
  /** Optional (0.836.0): Zeichen der Engine-Auswertung je Zug („??", „!" …), Schlüssel = Zugindex. */
  readonly marks = input<Readonly<Record<number, MoveListMark>> | null>(null);
  @Output() moveClicked = new EventEmitter<number>();
  /** Ein anklickbarer Zug in einem Kommentar wurde gewählt (Vorschau seiner Stellung). */
  @Output() commentMoveClicked = new EventEmitter<MoveListCommentSegment>();

  @ViewChild('moveListEl') moveListEl!: ElementRef<HTMLElement>;

  private readonly translate = inject(TranslateService, { optional: true });
  /** Name der Liste für Vorleser. */
  label(): string | null { return this.translate?.instant('moveList.label') ?? null; }

  /** Zug, auf den die Pfeiltasten zuletzt gewandert sind — `null` = die Tab-Station folgt dem aktiven Zug. */
  private focusIndex: number | null = null;

  /** Die EINE Tab-Station der Liste (roving tabindex, F8-018): der zuletzt angesteuerte, sonst der aktive, sonst der erste Zug. */
  tabStop(): number {
    const last = this.moves.length - 1;
    if (this.focusIndex !== null && this.focusIndex <= last) return this.focusIndex;
    return this.currentMoveIndex >= 0 && this.currentMoveIndex <= last ? this.currentMoveIndex : 0;
  }

  /** Pfeiltasten (←/↑ zurück, →/↓ vor), Pos1/Ende wandern zwischen den Zügen; Enter/Leertaste wählt (Knopf-Klick).
   *  Die Taste geht nicht weiter — sonst blätterte die Seite (globale ←/→) das Brett zusätzlich. */
  onKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    const raw = target?.getAttribute?.('data-index');
    if (raw === null || raw === undefined || !this.moves.length) return;
    const from = Number(raw);
    const last = this.moves.length - 1;
    let to: number;
    switch (event.key) {
      case 'ArrowRight': case 'ArrowDown': to = Math.min(from + 1, last); break;
      case 'ArrowLeft': case 'ArrowUp': to = Math.max(from - 1, 0); break;
      case 'Home': to = 0; break;
      case 'End': to = last; break;
      default: return;
    }
    event.preventDefault();
    event.stopPropagation();
    this.focusIndex = to;
    const next = this.moveListEl?.nativeElement.querySelector<HTMLElement>(`.move[data-index="${to}"]`);
    if (next) {
      next.focus({ preventScroll: true });
      scrollIntoContainer(next);
    }
  }

  movePairs: { number: number; white?: string; whiteIndex: number; black?: string; blackIndex?: number }[] = [];

  /** Schrift fürs Zeichen-Schild: Schwarz oder Weiß, was auf `color` (#rrggbb) mehr Kontrast hat (Gelb → Schwarz). */
  markText(color: string): string { return contrastText(color); }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['moves'] || changes['comments']) {
      this.buildPairs();
    }
    if (changes['currentMoveIndex']) {
      this.focusIndex = null;   // die Tab-Station folgt wieder dem aktiven Zug
      this.scrollToActive();
    }
  }

  isRowActive(pair: { whiteIndex: number; blackIndex?: number }): boolean {
    return pair.whiteIndex === this.currentMoveIndex ||
      (pair.blackIndex !== undefined && pair.blackIndex === this.currentMoveIndex);
  }

  /** Index des Kommentars, der unter dieser Zeile steht (Weiß vor Schwarz); `null` = keiner. Eine Zeile
   *  trägt höchstens EINEN Kommentar — hat Weiß einen, steht Schwarz in einer eigenen Zeile darunter. */
  commentIndex(pair: { whiteIndex: number; blackIndex?: number; white?: string }): number | null {
    if (pair.white !== undefined && this.comments[pair.whiteIndex]) return pair.whiteIndex;
    if (pair.blackIndex !== undefined && this.comments[pair.blackIndex]) return pair.blackIndex;
    return null;
  }

  segmentsFor(index: number): MoveListCommentSegment[] {
    return this.commentSegments?.[index] ?? [{ text: this.comments[index] }];
  }

  /** Klick auf einen Zug. Ein ZEIGER-Klick (Maus/Touch, `detail > 0`) lässt den Fokus nicht auf dem Knopf liegen —
   *  sonst machte die nächste Pfeiltaste dort einen Fokusring sichtbar, der stehen bleibt, während die Markierung
   *  weiterwandert, und Leertaste/Enter lösten den alten Zug erneut aus (das Brett spränge zurück). So bleibt die
   *  Maus beim Verhalten des früheren <span>. Enter/Leertaste (`detail` 0) behalten den Fokus; er folgt dann dem
   *  aktiven Zug (scrollToActive). Codereview F8-018, Nacharbeit. */
  onMoveClick(index: number, event: MouseEvent): void {
    if (event.detail > 0) (event.currentTarget as HTMLElement | null)?.blur();
    this.moveClicked.emit(index);
  }

  private buildPairs(): void {
    this.movePairs = [];
    let i = 0;
    while (i < this.moves.length) {
      const m = this.moves[i];
      const num = this.fullMoveNumber(m, i);
      if (m.color === 'b') {
        this.movePairs.push({ number: num, white: undefined, whiteIndex: -1, black: m.san, blackIndex: i });
        i += 1;
      } else {
        const black = this.moves[i + 1];
        // Kommentar hinter dem weißen Zug → Schwarz bekommt eine eigene Zeile, sonst ginge einer verloren.
        const hasBlack = !!black && black.color === 'b' && !this.comments[i];
        this.movePairs.push({
          number: num,
          white: m.san,
          whiteIndex: i,
          black: hasBlack ? black!.san : undefined,
          blackIndex: hasBlack ? i + 1 : undefined,
        });
        i += hasBlack ? 2 : 1;
      }
    }
  }

  private fullMoveNumber(m: Move, fallbackIndex: number): number {
    const before = (m as unknown as { before?: string }).before;
    const n = before ? parseInt(before.split(' ')[5], 10) : NaN;
    return Number.isFinite(n) ? n : Math.floor(fallbackIndex / 2) + 1;
  }

  private scrollToActive(): void {
    setTimeout(() => {
      const el = this.moveListEl?.nativeElement;
      if (!el) return;
      const active = el.querySelector('.move.active') as HTMLElement | null;
      if (!active) return;
      scrollIntoContainer(active);
      // Liegt der Fokus auf einem Zug DIESER Liste, folgt er dem aktiven Zug (F8-018, Nacharbeit) — sonst bliebe der
      // Fokusring beim zuerst angesprungenen Zug stehen, während → die Markierung weiterschiebt, und Enter/Leertaste
      // sprängen dorthin zurück. Fokus anderswo (Eingabefeld, Knopf außerhalb) bleibt unberührt; das Brett bewegt
      // sich nicht (preventScroll).
      const focused = document.activeElement;
      if (focused !== active && focused instanceof HTMLElement && focused.classList.contains('move') && el.contains(focused)) {
        active.focus({ preventScroll: true });
      }
    });
  }
}

/**
 * Bringt `el` in den sichtbaren Bereich seines NÄCHSTEN scrollbaren Vorfahren — und nur dort.
 *
 * Vorher stand hier `scrollIntoView({ block: 'nearest' })`, und das scrollt JEDEN scrollbaren
 * Vorfahren mit, das Dokument eingeschlossen. Am Handy steht die Zugliste unter dem Brett, der
 * aktive Zug liegt beim Blättern oft unterhalb des Bildschirmrands — also schob jeder Zug die ganze
 * Seite und damit das Brett nach oben (gemeldet 2026-09-23). Gibt es keinen scrollbaren Vorfahren
 * außer der Seite, passiert NICHTS: das Brett darf sich beim Navigieren nie bewegen.
 */
export function scrollIntoContainer(el: HTMLElement): void {
  const container = scrollParentOf(el);
  if (!container) return;
  const c = container.getBoundingClientRect();
  const a = el.getBoundingClientRect();
  const delta = a.top < c.top ? a.top - c.top : a.bottom > c.bottom ? a.bottom - c.bottom : 0;
  if (delta !== 0) container.scrollTo({ top: container.scrollTop + delta, behavior: 'smooth' });
}

/** Der nächste Vorfahre, der wirklich scrollt (overflow auto/scroll UND Überhang) — nie `html`/`body`. */
function scrollParentOf(el: HTMLElement): HTMLElement | null {
  for (let p = el.parentElement; p && p !== document.body && p !== document.documentElement; p = p.parentElement) {
    const overflowY = getComputedStyle(p).overflowY;
    if ((overflowY === 'auto' || overflowY === 'scroll') && p.scrollHeight > p.clientHeight) return p;
  }
  return null;
}
